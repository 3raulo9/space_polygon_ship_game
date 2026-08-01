using System.Numerics;
using Raylib_cs;
using VoidTanks.Entities;
using VoidTanks.Input;
using VoidTanks.Rendering;
using VoidTanks.UI;

namespace VoidTanks.Core;

/// <summary>
/// The loop. Simulation runs on a fixed timestep (deterministic movement and,
/// later, collision/AI); rendering is decoupled and runs once per frame. The
/// game opens on the title menu; the world isn't spun up until Single Player is
/// chosen, so nothing hunts you while you're still deciding to enter.
/// </summary>
public sealed class Game : IDisposable
{
    private readonly Renderer _renderer;
    private readonly Settings _settings;
    private readonly Menu _menu = new();
    private readonly PauseMenu _pauseMenu = new();
    private readonly InventoryScreen _inventory = new();
    // The inventory / crafting panel is a live overlay, not a state: while it's open the
    // sim keeps running behind it (enemies still hunt, salvage still drifts) — it just
    // adds mouse-driven item management on top of an ordinary Playing frame.
    private bool _inventoryOpen;
    private readonly SettingsScreen _settingsScreen;
    private readonly TestScreen _testScreen = new();

    // The hangar's build. Held here rather than on the screen so it survives a run:
    // dying and coming back to the menu should not silently un-paint your craft or
    // reset the points you spent on it.
    private readonly Loadout _loadout = new();
    private readonly ClassSelectScreen _classSelect;
    private World.World? _world;
    private GameState _state = GameState.Menu;

    /// <summary>
    /// Whether the player is out in a match rather than sitting in front of one. The
    /// soundtrack rides on this: menus, the hangar, the lobby and the bestiary are all
    /// silent, and a piece that is sounding when the player leaves for one of them
    /// fades out rather than being cut.
    ///
    /// A pause counts as being in the world on purpose. The sim is frozen, but the
    /// music is not scoring the sim — it is scoring the session, and killing it the
    /// moment the panel opens makes stepping back from the game feel like quitting it.
    /// Death and the level-clear screen count for the same reason: both are still the
    /// match, and both are the last places you would want the sound to fall away.
    /// </summary>
    private bool InWorld => _state
        is GameState.Playing or GameState.Paused or GameState.Dead
        or GameState.LevelIntro or GameState.LevelClear;

    // Reads the keyboard once per frame and rations it out to the fixed steps. Everything
    // the sim knows about the player's hands arrives through this and nothing else, which
    // is what lets the same world be driven by a recording, a test, or a second player.
    private readonly InputSampler _input = new();

    // --- Multiplayer -------------------------------------------------------------
    // All null in a solo run, which is the point: single player and multiplayer are
    // separate modes, and nothing below is constructed until the second one is chosen.
    private readonly LobbyScreen _lobby = new();
    // The walkable 3D lobby: a domed room over a planet where players gather, pick a craft,
    // set a name, and the host launches. Non-null only while the multiplayer front-end is up.
    private World.LobbyRoom? _room;
    private Net.SteamNet? _steam;
    private Net.Session? _session;

    // Which side of a match this machine is setting up, remembered across the class-select
    // detour: choosing HOST or JOIN sends the player to the hangar to pick a chassis, and
    // this is what tells the hangar where to go when they are done rather than starting a
    // solo run. None the rest of the time, which is every single-player launch.
    private enum MpRole { None, Host, Join }
    private MpRole _mpRole = MpRole.None;

    /// <summary>True while a match is running, so the loop knows to pump the wire.</summary>
    private bool InMatch => _session != null;

    // How far the pause dim has come in: 0 clean, 1 fully dimmed. Eases in when pausing, out
    // when resuming. Seconds for a full sweep set by PauseFade. Also handed to BeginFade on
    // the way out to the menu, so the darkening the panel has already done carries into the
    // dissolve instead of being thrown away and redone.
    private float _pauseBlur;
    private bool _resuming;
    private const float PauseFade = 0.28f;

    // Screen-to-screen pixel fade. A transition dissolves the current screen into
    // blocks and toward the void (out), runs a swap at the crossover, then resolves
    // the new screen back in (in). Drives menu -> game and game -> menu.
    private bool _fading;
    private bool _fadeIn;               // false = dissolving out, true = resolving in
    private float _fade;                // 0 clear .. 1 full void
    private System.Action? _onCrossover; // runs once when the out phase completes
    private const float FadeDur = 0.30f;

    // Wall-clock seconds since boot — drives the menu's drift and flicker.
    private float _menuTime;

    private double _accumulator;

    // Verification harness: when VOIDTANKS_CAPTURE is set, run a scripted number
    // of frames, save a screenshot, and exit. Lets the render be checked without
    // a human at the window. No effect on normal play.
    private readonly string? _capturePath = Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE");
    // When set, capture grabs a UI screen instead of the world: "menu" or "settings".
    // "controls" and "sound" are the settings screen's two sub-pages, which are worth their
    // own names — they are the densest layouts in the game and the only ones a human would
    // otherwise have to reach by hand to look at.
    private readonly string? _captureScreen =
        Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_MENU");
    private bool _captureMenu =>
        _captureScreen is "1" or "menu" or "settings" or "controls" or "sound"
                       or "test" or "class" or "lobby" or "room";
    private int _frame;

    /// <summary>Capture-only: the harness is holding the SPIDER's lance charge, so no
    /// step of the sim may read the trigger as released. See RunCaptureFrame.</summary>
    private bool _lanceHold;

    /// <summary>Capture-only: the building VOIDTANKS_CAPTURE_FELL picked to cut down, so
    /// every later frame can re-park the camera on it while it comes apart.</summary>
    private World.Structure? _fellTarget;

    public Game()
    {
        _renderer = new Renderer();

        // Load persisted controls and make them the live binding set the sim polls.
        _settings = Settings.Load();
        InputMap.Active = _settings;
        // ...and the persisted faders into the mixer, before a single cue can be raised.
        Audio.ApplySettings(_settings);
        _settingsScreen = new SettingsScreen(_settings);
        _classSelect = new ClassSelectScreen(_loadout);

        // Capture runs the world directly (no menu), so build it now and aim the
        // craft at the seeded enemy. Normal play starts on the menu instead. The
        // menu-capture variant stays on the menu, so skip the world entirely.
        if (_capturePath != null && !_captureMenu)
        {
            EnterSinglePlayer();
            // Face whatever the capture was actually set up to photograph. A seeded
            // hunter drops in at a random bearing, so aiming at it points the camera
            // away from a deliberately-placed monster — which produces a picture of
            // empty grid and looks exactly like the monster failing to draw.
            //
            // A soldier is exempt: the world has already stood them in front of the
            // tower they open on, and turning them to look at a hunter somewhere out in
            // the fog throws away the one thing a picture of that chassis has to show.
            // A fish is exempt for the same reason a soldier is, and more so: it opens
            // thirty metres up looking out across the city, and swinging it round to face
            // a hunter crawling about on the grid throws away the entire picture.
            // A squad capture has already aimed itself — the world turned the craft onto
            // the tower it hung them from, pitch and all, and pointing at anything else
            // throws that away.
            Vector2? subject = _world!.Squads.Count > 0
                || _world.Player.Soldier != null || _world.Player.Fish != null ? null
                : _world.Maw?.Position
                ?? (Vector2?)_world.Boss?.Position
                ?? (_world.Enemies.Count > 0 ? _world.Enemies[0].Position : null);
            if (subject is { } at)
            {
                Vector2 to = at - _world.Player.Position;
                _world.Player.Heading = MathF.Atan2(to.X, to.Y);
            }

            // VOIDTANKS_MP_PEER=<class index> seats a second player in front of the first, as
            // that chassis, so the third-person craft render can be photographed without two
            // machines and a wire. It reuses the seat placement the real match uses, so what
            // this shows is what a host actually sees when a friend joins.
            string? peer = Environment.GetEnvironmentVariable("VOIDTANKS_MP_PEER");
            if (peer != null && int.TryParse(peer, out int pc)
                && Enum.IsDefined((PlayerClass)pc))
            {
                _world.Match = new MatchSettings { MaxPlayers = 4 };
                var mate = _world.AddPlayer(new Loadout { Class = (PlayerClass)pc });
                // Look straight at them, and give them a little height if they can hold it, so
                // the picture shows the whole craft rather than its feet.
                if (mate is not null)
                {
                    _world.SeatNames[_world.Seat(mate)] = "MATE";   // so the tag shows in capture
                    Vector2 to = mate.Position - _world.Player.Position;
                    _world.Player.Heading = MathF.Atan2(to.X, to.Y);
                    if (mate.Class is PlayerClass.Fish or PlayerClass.Virus) mate.Height = 6f;

                    // VOIDTANKS_MP_COMPARE=1 drops the matching enemy a few metres beside the
                    // peer, so a capture shows player craft and hunter side by side and the
                    // world-scale can be tuned until they read the same size. Tuning only.
                    if (Environment.GetEnvironmentVariable("VOIDTANKS_MP_COMPARE") == "1")
                    {
                        var beside = Torus.Wrap(mate.Position + new Vector2(9f, 0f));
                        if (mate.Class == PlayerClass.Soldier) _world.SpawnSoldierSquad(beside);
                        else _world.Enemies.Add(new Entities.EnemyTank(beside, elite: false));
                    }
                }
            }
        }
    }

    public void Run()
    {
        while (!Raylib.WindowShouldClose())
        {
            // Drain any time-scheduled audio (the boss's death cascade) and service the
            // soundtrack. Sits above every early-out below on purpose: the cascade is
            // queued as absolute wall-clock times, so if the player pauses or bails to
            // the menu part-way through it still finishes rather than stranding a
            // half-played death — and a music stream has to be fed every single frame
            // it is open, including the frames a fade or a menu owns.
            Audio.Update(InWorld);

            // Steam's own callbacks — connection state changes arrive through these. Cheap
            // and harmless when Steam never came up.
            Net.SteamNet.RunCallbacks();

            // Age the join/quit feed once per rendered frame so its lines fade out whatever
            // the sim is doing behind them.
            _session?.Notices.Age(Raylib.GetFrameTime());

            if (_capturePath != null && RunCaptureFrame()) break;

            // The host went away mid-match. Nothing here is simulated locally — a client is
            // shown the world, it does not run it — so what is left after the link dies is a
            // frozen city the player can walk a ghost around in for ever. Bail back to the
            // multiplayer front door with the reason on screen instead. Host-side this can
            // never fire: one player leaving is a departure, not a dead session.
            if (_steam is { Dropped: { } lost } && !_fading
                && _state is GameState.Playing or GameState.Paused)
            {
                BeginFade(() => AbandonMatch(lost), _pauseBlur);
            }

            SyncCursor();

            // The one place a live device becomes simulation input. Read once here, then
            // handed to the fixed steps below one frame at a time — see InputSampler for
            // why the edges have to be counted rather than re-polled per step.
            _input.Sample();

            // F11 flips borderless fullscreen from any screen. The Renderer already
            // rescales the low-res target off the live window size each Present, so
            // nothing else needs to know the resolution changed.
            if (InputMap.FullscreenPressed) ToggleFullscreen();

            // A screen-to-screen pixel fade is in flight: it owns the frame,
            // drawing whatever state we're in (or have just switched to) under the
            // dissolve, until it resolves back in.
            if (_fading)
            {
                UpdateFade();
                DrawFade();
                continue;
            }

            // The menu owns Escape (to quit); in-world, Escape returns to the menu.
            if (_state == GameState.Menu)
            {
                if (UpdateMenu()) break; // Quit requested
                DrawMenu();
                continue;
            }

            if (_state == GameState.ClassSelect)
            {
                UpdateClassSelect();
                // LAUNCH tears down the hangar and starts a fade, which owns the next
                // frame — so only draw the hangar while we're actually still in it.
                if (_state == GameState.ClassSelect && !_fading) DrawClassSelect();
                continue;
            }

            if (_state == GameState.Lobby)
            {
                UpdateLobby();
                if (_state == GameState.Lobby && !_fading) DrawLobby();
                continue;
            }

            if (_state == GameState.Settings)
            {
                UpdateSettings();
                DrawSettings();
                continue;
            }

            if (_state == GameState.Test)
            {
                UpdateTest();
                DrawTest();
                continue;
            }

            // Paused: the panel is up over the dimmed world.
            //
            // Single player freezes the run behind it. A networked match does not, and must
            // not: a room of twenty cannot be held still because one of them went looking for
            // the volume. The match runs on, this machine keeps talking to it, and the craft
            // sitting under the panel is exactly as killable as it was a second ago — which
            // is the price of opening it, and is meant to be felt.
            if (_state == GameState.Paused)
            {
                UpdatePaused();

                // "Back to menu" tears the world down inside UpdatePaused, so only carry on
                // while we're actually still paused — the next iteration draws whatever
                // state we left for (menu or resumed play).
                if (_state != GameState.Paused) continue;

                if (_session != null) StepSim(readInput: false);
                else _accumulator = 0;   // don't fast-forward the frozen gap on resume

                DrawPaused();
                continue;
            }

            // 'F' toggles the inventory overlay. Escape closes it too if it's open;
            // otherwise Escape opens the pause panel (the world only pauses when the
            // inventory is *not* up — the panel itself never freezes the sim).
            // Pointing at something, and — once your revives are spent — choosing which
            // team-mate to watch. Both are multiplayer-only and both are held back while the
            // inventory panel owns the mouse, since middle-click and the arrows mean something
            // else in there.
            if (_session != null && !_inventoryOpen)
            {
                if (InputMap.WorldPingPressed && !_world!.Spectating)
                    _session.Mark(_world.CrosshairTarget());

                if (_world!.Spectating)
                {
                    if (InputMap.SpectateNextPressed) _world.CycleSpectator(+1);
                    else if (InputMap.SpectatePrevPressed) _world.CycleSpectator(-1);
                }
            }

            if (InputMap.InventoryToggle)
                SetInventory(!_inventoryOpen);
            else if (InputMap.QuitPressed)
            {
                if (_inventoryOpen)
                    SetInventory(false);
                else
                {
                    EnterPause();
                    DrawPaused();
                    continue;
                }
            }

            // While the panel is up the mouse drives item management and the combat
            // hotkeys (throw / debug spawns) are held back so a drag can't also lob a
            // weapon. The sim itself still steps below regardless.
            if (_inventoryOpen)
            {
                _inventory.Update(_world!);
            }
            else
            {
                // R/T/Y/U used to be polled here. They now ride the input frame into
                // World.DriveSeat, so a remote player's throw reaches the host and is spent
                // from their own pack rather than only ever working for whoever is sitting
                // at this keyboard.

                // Debug hatch: 'L' drops one random enemy on the horizon each press.
                // Polled once per frame (a just-pressed edge), not per fixed step.
                if (InputMap.DebugSpawnPressed)
                    _world!.SpawnRandomEnemy();

                // 'N' silences the spawn director so the field stops refilling itself;
                // 'K' parks a dormant Crab-Core straight ahead. Both are testing hatches.
                if (InputMap.DebugNoSpawnPressed)
                    _world!.DynamicSpawning = !_world.DynamicSpawning;

                if (InputMap.DebugSpawnCrabPressed)
                {
                    _world!.SpawnCrabAhead();
                    _world!.GiveCrabCore();   // arm the tester against the thing they just raised
                }

                // 'J' does the same for the hanging mouth.
                if (InputMap.DebugSpawnMawPressed)
                    _world!.SpawnMawAhead();

                // 'H' raises a soldier squad on a tower out in the fog.
                if (InputMap.DebugSpawnSquadPressed)
                    _world!.SpawnSoldierSquad();
            }

            StepSim(readInput: true);

            // The live world, with the crafting panel laid over it when it's open.
            if (_inventoryOpen) DrawInventory();
            else Draw();
        }
    }

    /// <summary>
    /// Accumulates real elapsed time and steps the sim in fixed increments, so a fast or
    /// slow display never changes the physics.
    ///
    /// <para><paramref name="readInput"/> is false while the pause panel is up in a networked
    /// match. Everything else still runs — the wire turns, the world advances, the enemies
    /// keep walking — but this seat sends a frame with nothing held in it. That is the honest
    /// account of what is happening: the player's hands are off the keys. It also has to be
    /// an <em>empty</em> frame rather than no frame at all, because a seat that stops
    /// speaking is a seat the host reads as a dead connection.</para>
    /// </summary>
    private void StepSim(bool readInput)
    {
        _accumulator += Raylib.GetFrameTime();
        // Guard against spiral-of-death after a stall.
        if (_accumulator > 0.25) _accumulator = 0.25;

        while (_accumulator >= Config.FixedDt)
        {
            InputFrame frame = readInput ? _input.Next() : InputFrame.Empty;

            // The wire turns before the world does: a client's frame goes up and the
            // host's account comes down, so the step that follows runs on the freshest
            // thing either machine knows.
            if (_session is { } net && _state is GameState.Playing or GameState.Paused)
            {
                // A crafting panel is this machine's business — clear the combat bits
                // before they are sent rather than making the host reason about it.
                if (_inventoryOpen) frame = frame.WithoutCombat();
                net.Pump(frame);
            }

            Update((float)Config.FixedDt, frame);
            _accumulator -= Config.FixedDt;
        }
    }

    /// <summary>True while the pointer is captured — locked to the window and feeding
    /// relative movement to whichever chassis's look is on the mouse.</summary>
    private bool _mouseCaptured;

    /// <summary>
    /// Decides what the pointer is doing this frame.
    ///
    /// The operating system's cursor is never visible anywhere in this game. On every
    /// screen but one it is simply hidden — nothing here is driven by pointing at it —
    /// and the inventory panel, which is, draws its own chunky pixel arrow into the
    /// low-res target instead, so the pointer is made of the same fat pixels as the rest
    /// of the picture rather than sitting crisply on top of it.
    ///
    /// Every chassis now looks with the mouse — the SOLDIER and the FISH turn their whole
    /// body, the TANK and the SPIDER swing a head on top of a hull — and all of them need
    /// the pointer <em>captured</em> for it: relative mouse movement only reports while the
    /// real cursor is locked to the window centre, or it walks off the edge of the screen
    /// mid-look and the view stops turning. Captured only while the world is actually being
    /// driven — the pause panel, the pack and every menu hand the mouse back.
    /// </summary>
    private void SyncCursor()
    {
        // The lobby room captures the mouse too, but only while actually walking — a station
        // panel, the name field and the code box all want the pointer handed back so typing and
        // menu navigation behave.
        bool roomWalking = _state == GameState.Lobby && !_fading
                        && _room is { Where: World.LobbyRoom.Focus.Walking };

        bool wantCapture = roomWalking
                        || (_state == GameState.Playing
                            && !_inventoryOpen
                            && !_fading
                            && _world != null);

        if (wantCapture == _mouseCaptured)
        {
            // Raylib's HideCursor is not sticky across every window event, so it is
            // re-asserted each frame while the pointer is free. Cheap, and it is the
            // difference between "no cursor" and "no cursor most of the time".
            if (!_mouseCaptured) Raylib.HideCursor();
            return;
        }

        _mouseCaptured = wantCapture;
        // Either edge of the capture is a seam in what the sim was allowed to see, so the
        // held state is dropped across it. Without this, a key still down when the pause
        // panel came up is not an edge when play resumes (the sim never saw it go down),
        // and a key released behind the panel would leave its bit stuck on.
        _input.Forget();

        if (wantCapture)
        {
            Raylib.DisableCursor();
        }
        else
        {
            Raylib.EnableCursor();
            Raylib.HideCursor();
        }
    }

    /// <summary>
    /// Advances and draws the title menu. Returns true when the player asks to
    /// quit the whole game.
    /// </summary>
    private bool UpdateMenu()
    {
        _menuTime += Raylib.GetFrameTime();

        switch (_menu.Update())
        {
            case Menu.Action.StartSinglePlayer:
                // Single Player no longer drops straight into the world: it opens the
                // hangar first, where the chassis and the build are chosen. The fade
                // still runs, so the menu dissolves into the hangar the same way it
                // used to dissolve into the grid.
                BeginFade(() => _state = GameState.ClassSelect);
                break;
            case Menu.Action.StartMultiplayer:
                // The other mode. Steam comes up here rather than at boot, so a player who
                // only ever plays alone never waits on it and never sees it fail. The lobby is
                // now a place you walk into: a fresh room in its antechamber, host/join undecided
                // until the player reaches a pillar.
                Net.SteamNet.Start();
                _room = new World.LobbyRoom();
                BeginFade(() => _state = GameState.Lobby);
                break;
            case Menu.Action.OpenSettings:
                // Reopens on the front page, the same as it does from the pause panel. The
                // screen is one instance shared by both doors, and arriving on whichever
                // sub-page somebody was last looking at is disorienting from either.
                _settingsScreen.Reset();
                _state = GameState.Settings;
                break;
            case Menu.Action.OpenTestScreen:
                // Secret keybind ('L' position): drop into the hidden bestiary.
                _state = GameState.Test;
                break;
            case Menu.Action.Quit:
                return true;
        }
        return false;
    }

    /// <summary>
    /// Advances the hangar. LAUNCH dissolves out and spins the world up at the
    /// crossover with whatever build is on the bench; Escape falls back to the menu
    /// (through the same dissolve, so no screen ever hard-cuts to another).
    /// </summary>
    private void UpdateClassSelect()
    {
        _menuTime += Raylib.GetFrameTime();

        switch (_classSelect.Update())
        {
            case ClassSelectScreen.Action.Launch:
                BeginFade(_mpRole switch
                {
                    MpRole.Host => StartHosting,
                    MpRole.Join => StartJoining,
                    _ => EnterSinglePlayer,
                });
                break;
            case ClassSelectScreen.Action.Back:
                // In a match, the hangar's back button returns to the lobby it came from
                // rather than all the way out to the title.
                if (_mpRole != MpRole.None)
                {
                    _mpRole = MpRole.None;
                    _lobby.Reset();
                    BeginFade(() => _state = GameState.Lobby);
                }
                else BeginFade(() => _state = GameState.Menu);
                break;
        }
    }

    /// <summary>
    /// Advances the settings screen off the title menu. Leaving it saves the (already-live)
    /// settings to disk so the choices persist.
    /// </summary>
    private void UpdateSettings()
    {
        _menuTime += Raylib.GetFrameTime();

        if (_settingsScreen.Update() == SettingsScreen.Action.Back)
        {
            _settings.Save();
            _state = GameState.Menu;
        }
    }

    /// <summary>
    /// Advances the hidden test screen. Escape (its only exit) returns to the
    /// menu; the turntable spin is driven by the same wall-clock as the menu drift.
    /// </summary>
    private void UpdateTest()
    {
        _menuTime += Raylib.GetFrameTime();

        if (_testScreen.Update() == TestScreen.Action.Back)
            _state = GameState.Menu;
    }

    /// <summary>
    /// Opens the pause panel. The dim starts clean and eases in over the next few frames.
    /// Whether the sim keeps stepping behind it is the loop's business, not this method's —
    /// see the Paused branch in <see cref="Run"/>.
    /// </summary>
    private void EnterPause()
    {
        _pauseMenu.Reset();
        _pauseSettings = false;
        _resuming = false;
        _state = GameState.Paused;
        Audio.PlayBlip();
    }

    /// <summary>True while the settings screen is open <em>over</em> the pause panel. Not a
    /// game state of its own: everything about being paused still holds, and in a match the
    /// world is still running underneath — which is exactly why the settings have to be
    /// reachable from here rather than only from the title.</summary>
    private bool _pauseSettings;

    /// <summary>
    /// Advances the pause panel and its dim. Entering, the dim eases toward full; once the
    /// player asks to resume it eases back out, and only when it has fully cleared do we hand
    /// control back (so the world comes back up before it is theirs again). "Back to menu"
    /// abandons the run outright.
    /// </summary>
    private void UpdatePaused()
    {
        _menuTime += Raylib.GetFrameTime();

        // Ease the dim toward its target: in while paused, out while resuming.
        float step = Raylib.GetFrameTime() / PauseFade;
        _pauseBlur = Math.Clamp(_pauseBlur + (_resuming ? -step : step), 0f, 1f);

        if (_resuming)
        {
            if (_pauseBlur <= 0f)
            {
                _state = GameState.Playing;
                _resuming = false;
                _accumulator = 0; // don't fast-forward the sim across the paused gap
            }
            return; // panel is closing — ignore navigation while it clears
        }

        // The settings page owns the panel while it is open. Backing out of it saves, the
        // same as backing out of it from the title menu does — a control the player rebound
        // mid-match should still be rebound tomorrow.
        if (_pauseSettings)
        {
            if (_settingsScreen.Update() == SettingsScreen.Action.Back)
            {
                _settings.Save();
                _pauseSettings = false;
            }
            return;
        }

        switch (_pauseMenu.Update())
        {
            case PauseMenu.Action.Resume:
                _resuming = true;
                break;
            case PauseMenu.Action.OpenSettings:
                _settingsScreen.Reset();
                _pauseSettings = true;
                Audio.PlayBlip();
                break;
            case PauseMenu.Action.BackToMenu:
                // Carry the pause dim straight into the fade (start already darkened) so the
                // world sinks to void and the menu pixel-resolves in — one continuous
                // dissolve, no sharp flash between.
                BeginFade(ReturnToMenu, _pauseBlur);
                break;
        }
    }

    /// <summary>
    /// Opens or closes the live inventory overlay. Opening resets the drag state; closing
    /// returns any half-held stack to where it came from so nothing is stranded on the
    /// cursor. The sim is untouched either way — the panel never pauses the world.
    /// </summary>
    private void SetInventory(bool open)
    {
        if (open == _inventoryOpen) return;
        if (open) _inventory.Reset();
        else _inventory.Cancel(_world!);
        _inventoryOpen = open;
        Audio.PlayBlip();
    }

    /// <summary>
    /// Kicks off a screen-to-screen pixel fade. <paramref name="onCrossover"/> runs
    /// once the out phase has fully dissolved (swap state / build or tear down the
    /// world there); <paramref name="from"/> seeds the starting dissolve amount, so
    /// a transition can pick up from an already-coarsened screen (the pause blur).
    /// </summary>
    private void BeginFade(System.Action onCrossover, float from = 0f)
    {
        _fading = true;
        _fadeIn = false;
        _fade = Math.Clamp(from, 0f, 1f);
        _onCrossover = onCrossover;
    }

    /// <summary>
    /// Advances the active fade: dissolve out to void, run the crossover swap, then
    /// resolve the new screen back in. Uses wall-clock time (a cosmetic effect, not
    /// simulation), and leaves the sim frozen until the fade fully clears.
    /// </summary>
    private void UpdateFade()
    {
        _menuTime += Raylib.GetFrameTime();
        float d = Raylib.GetFrameTime() / FadeDur;

        if (!_fadeIn)
        {
            _fade += d;
            if (_fade >= 1f)
            {
                _fade = 1f;
                _onCrossover?.Invoke();
                _onCrossover = null;
                _fadeIn = true;
            }
        }
        else
        {
            _fade -= d;
            if (_fade <= 0f)
            {
                _fade = 0f;
                _fading = false;
                _accumulator = 0; // don't fast-forward the sim over the faded gap
            }
        }
    }

    /// <summary>Draws the current screen into the target, then the dissolve over it.</summary>
    private void DrawFade()
    {
        switch (_state)
        {
            case GameState.Menu: _renderer.DrawMenu(_menu, _menuTime); break;
            case GameState.ClassSelect: _renderer.DrawClassSelect(_classSelect, _menuTime); break;
            case GameState.Settings: _renderer.DrawSettings(_settingsScreen, _menuTime); break;
            case GameState.Test: _renderer.DrawTest(_testScreen, _menuTime); break;
            case GameState.Lobby:
                if (_room != null) _renderer.DrawLobbyRoom(_room, _menuTime);
                else _renderer.DrawMenu(_menu, _menuTime);
                break;
            // Playing / Paused — but only if there is actually a world to draw. Every
            // screen above is worldless, and a state that reaches the default branch
            // without one would dereference null mid-transition, which is exactly what
            // adding the lobby did before it was listed here.
            default:
                if (_world != null) _renderer.DrawWorld(_world);
                else _renderer.DrawMenu(_menu, _menuTime);
                break;
        }
        _renderer.ApplyPixelDissolve(_fade);
        _renderer.Present();
    }

    /// <summary>
    /// Flips between windowed and borderless fullscreen (F11). Borderless adopts
    /// the monitor resolution and reads cleaner than exclusive mode; the Renderer's
    /// Present recomputes the integer upscale and letterbox off the live window
    /// size, so the picture just re-fits itself. Leaving fullscreen restores the
    /// original windowed size and re-centers on the current monitor.
    /// </summary>
    private void ToggleFullscreen()
    {
        bool goingFullscreen = !Raylib.IsWindowState(ConfigFlags.BorderlessWindowMode);

        Raylib.ToggleBorderlessWindowed();

        if (!goingFullscreen)
        {
            // Back to windowed: restore the launch size and re-center it.
            Raylib.SetWindowSize(Config.WindowWidth, Config.WindowHeight);
            int mon = Raylib.GetCurrentMonitor();
            int mw = Raylib.GetMonitorWidth(mon);
            int mh = Raylib.GetMonitorHeight(mon);
            Raylib.SetWindowPosition((mw - Config.WindowWidth) / 2, (mh - Config.WindowHeight) / 2);
        }

        Audio.PlayBlip();
    }

    private void UpdateLobby()
    {
        if (_room is null) { BeginFade(ReturnToMenu); return; }
        _menuTime += Raylib.GetFrameTime();

        // A dial that failed, or a host that went away, drops the player back into the
        // antechamber with a reason rather than leaving them standing in a dead room. Only
        // ever a client's problem: a host sees a peer leave through the departure queue, and
        // reporting it here used to tear the whole session down and evict everybody.
        if (_steam is { Dropped: { } why }) { _room.Fail(why); TearDownMatch(); }
        // The host answered, and the answer was no — or never came at all.
        else if (_session is { Rejected: { } refused }) { _room.Fail(refused); TearDownMatch(); }

        // Keep the host's live rules on the world it already built, so a change made at the
        // console while people gather actually takes at launch.
        if (_session is { IsHost: true } host && host.World is { } hw)
            hw.Match = _room.Match.Clamped();

        // Once the host has seated us (their Welcome carried our seat), step out of the
        // antechamber into the room proper.
        if (_session is { IsHost: false } client && client.LocalSeat >= 0
            && _room.Stage != World.LobbyRoom.Phase.InRoom)
            _room.Seat(client.LocalSeat, client.LocalName);

        // Drive the walker and the stations from the live keyboard.
        World.LobbyRoom.Action act = _room.Update(_input.Current, Raylib.GetFrameTime());

        // A name set here is remembered for next time — read before the tick below clears the
        // flag on its way to the wire.
        if (_room.NameDirty) { _settings.Nickname = _room.MyName; _settings.Save(); }

        // Turn the wire while everyone is still standing here — the handshake that seats a
        // joiner and the transforms that make the room move both happen on this screen.
        _session?.LobbyTick(_room);

        switch (act)
        {
            case World.LobbyRoom.Action.Back:
                TearDownMatch();
                _room = null;
                BeginFade(ReturnToMenu);
                return;

            case World.LobbyRoom.Action.StartHost:
                StartHosting();
                break;

            case World.LobbyRoom.Action.StartJoin:
                StartJoining();
                break;

            case World.LobbyRoom.Action.Launch:
                if (_session?.World is { } ready)
                {
                    // Anyone seated before the host settled on a revive count gets it now, so
                    // the number on every HUD matches the one the host launched with.
                    foreach (var p in ready.Players) p.Lives = ready.Match.Revives + 1;
                    // Carry the roster's names onto the world so team-mates wear a tag in-match.
                    foreach (var kv in _session.SeatNames) ready.SeatNames[kv.Key] = kv.Value;
                    _session.StartMatch();
                    _session.Room = null;
                    _world = ready;
                    _room = null;
                    _inventoryOpen = false;
                    BeginFade(() => _state = GameState.Playing);
                    return;
                }
                break;
        }

        // A client comes in when the host presses LAUNCH — there is no launch button on that
        // end, the host owns when the match starts.
        //
        // ...but not before they have chosen a craft. Somebody who dials into a match that is
        // already running is seated and told START in the same breath, and walking them
        // straight in gave them no moment at the pod at all: they arrived permanently as the
        // placeholder TANK their hello carried, with no way ever to be anything else. So a
        // client with no pick yet stays on the floor by the pod, and comes in the instant
        // they choose. The host honours that pick mid-match like any other.
        if (_session is { IsHost: false, MatchStarted: true, LocalSeat: >= 0 } joined
            && joined.World is { } jw
            && _room is { MyChassis: not null })
        {
            // Install the craft this player chose at the pod as their own seat. The host has
            // been authoritative on it since the Pick and the snapshot names it for everyone
            // else, but a client never overwrites its own seat from a snapshot — so without
            // this the player would drive the placeholder TANK they were seated as, not the
            // chassis they picked.
            if (_room?.MyChassis is { } chosen)
            {
                _loadout.Class = chosen;
                jw.ReplacePlayer(joined.LocalSeat, _loadout);
            }
            // Carry the room's roster of names into the match so team-mates wear a tag. Only
            // where the host has not already named the seat: its SeatName packets are the
            // authority, and a stale figure still called PLAYER must not overwrite one.
            if (_room is { } r)
            {
                foreach (var a in r.Avatars.Values)
                    if (!jw.SeatNames.ContainsKey(a.Seat)) jw.SeatNames[a.Seat] = a.Name;
                if (!jw.SeatNames.ContainsKey(joined.LocalSeat))
                    jw.SeatNames[joined.LocalSeat] = r.MyName;
            }
            joined.Room = null;
            _world = jw;
            _room = null;
            _inventoryOpen = false;
            BeginFade(() => _state = GameState.Playing);
        }
    }

    private void DrawLobby()
    {
        if (_room != null) _renderer.DrawLobbyRoom(_room, _menuTime);
        _renderer.Present();
    }

    /// <summary>Closes the wire and forgets the match. Single player never touches this.</summary>
    private void TearDownMatch()
    {
        _steam?.Dispose();
        _steam = null;
        _session = null;
    }

    /// <summary>The multiplayer name this machine plays under: the nickname the player last
    /// set, or their Steam persona if they never set one.</summary>
    private string MpName()
    {
        string n = _settings.Nickname.Trim();
        return n.Length > 0 ? n : Net.SteamNet.LocalName;
    }

    /// <summary>Reached from the antechamber's HOST pillar: opens the socket and builds the
    /// host's world, then seats the host in the room proper. The host's own chassis is still
    /// chosen at the pod like everyone else, so the world opens on a placeholder until then.</summary>
    private void StartHosting()
    {
        if (_room is null) return;
        _steam = Net.SteamNet.Host();
        if (_steam == null) { _room.Fail("COULD NOT OPEN A SOCKET"); return; }
        _session = new Net.Session(_steam, host: true) { LocalName = MpName() };
        _session.HostMatch(new World.World(_loadout, _room.Match.Clamped()));
        _session.Room = _room;
        _room.IsHost = true;
        _room.Seat(0, _session.LocalName);
    }

    /// <summary>Reached from the antechamber's JOIN pillar with a code typed: dials the host and
    /// waits on the connecting banner until the Welcome seats us into the room.</summary>
    private void StartJoining()
    {
        if (_room is null) return;
        _steam = Net.SteamNet.Connect(_room.TypedCode);
        if (_steam == null) { _room.Fail("THAT IS NOT A CODE"); return; }
        _session = new Net.Session(_steam, host: false) { LocalName = MpName() };
        // A client owns nothing but its own craft: the host paints the rest through snapshots.
        _session.JoinMatch(new World.World(_loadout) { DynamicSpawning = false, Authoritative = false });
        // A placeholder chassis — the real one is chosen at the pod and sent as a Pick. The
        // Hello still carries our name, which is how the host has it before any rename.
        _session.SendHello(_loadout.Class);
        _session.Room = _room;
        _room.IsHost = false;
        _room.Connecting();
    }

    private void EnterSinglePlayer()
    {
        // Explicitly the solo match: one seat, sealed, nobody can be dropped into it.
        _world = new World.World(_loadout, MatchSettings.SinglePlayer);
        _state = GameState.Playing;
        _inventoryOpen = false;
    }

    /// <summary>
    /// The link died while playing: tear the match down and set the player back down in the
    /// multiplayer antechamber with the reason showing, rather than at the title screen with
    /// no explanation for why their match stopped existing.
    /// </summary>
    private void AbandonMatch(string why)
    {
        TearDownMatch();
        _world = null;
        _inventoryOpen = false;
        _accumulator = 0;
        _pauseBlur = 0f;
        _resuming = false;
        _room = new World.LobbyRoom();
        _room.Fail(why);
        _state = GameState.Lobby;
    }

    private void ReturnToMenu()
    {
        TearDownMatch();
        _world = null;
        _state = GameState.Menu;
        _accumulator = 0;
        _pauseBlur = 0f;
        _resuming = false;
        _inventoryOpen = false;
    }

    /// <summary>
    /// Scripted capture: advance a few frames of forward motion so the grid is
    /// clearly in view, then screenshot and signal exit. Returns true when done.
    /// </summary>
    private bool RunCaptureFrame()
    {
        _frame++;

        // UI variant: let the drift/flicker advance a little, then grab the screen.
        if (_captureMenu)
        {
            _menuTime += (float)Config.FixedDt;
            int menuAt = int.TryParse(
                Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_FRAME"), out int mf) ? mf : 30;
            if (_frame < menuAt) return false;
            // Optional dissolve overlay: VOIDTANKS_CAPTURE_FADE=<0..1> grabs the UI
            // screen mid pixel-fade, to verify the menu-side of a transition.
            string? fade = Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_FADE");
            float fa = fade != null && float.TryParse(fade, out float pf) ? Math.Clamp(pf, 0f, 1f) : 0f;
            // Draw twice so both the front and back buffers hold the same image;
            // TakeScreenshot reads after the swap, so a single draw would grab the
            // previous (blank) frame.
            for (int i = 0; i < 2; i++)
            {
                if (_captureScreen == "lobby") _renderer.DrawLobby(_lobby, _menuTime);
                else if (_captureScreen == "room") _renderer.DrawLobbyRoom(CaptureRoom(), _menuTime);
                else if (_captureScreen is "settings" or "controls" or "sound")
                {
                    // VOIDTANKS_CAPTURE_ROW=<n> scrolls the controls list to a given row
                    // before the grab, so the chassis sections further down the page can be
                    // photographed and not just the first screenful.
                    _settingsScreen.OpenForCapture(_captureScreen,
                        int.TryParse(Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_ROW"),
                            out int row) ? row : 0);
                    _renderer.DrawSettings(_settingsScreen, _menuTime);
                }
                else if (_captureScreen == "class") _renderer.DrawClassSelect(_classSelect, _menuTime);
                else if (_captureScreen == "test") _renderer.DrawTest(_testScreen, _menuTime);
                else _renderer.DrawMenu(_menu, _menuTime);
                _renderer.ApplyPixelDissolve(fa);
                _renderer.Present();
            }
            Raylib.TakeScreenshot(_capturePath!);
            return true;
        }

        // Inventory variant: seed a representative pack (a bit of every item, three
        // fragments loaded in the triangle so the CRAB CORE preview shows, one equipped)
        // and grab the crafting panel — lets the layout and font be verified headlessly.
        if (Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_INV") != null)
        {
            var inv = _world!.Inventory;
            inv.Add(ItemKind.Battery, 4);
            inv.Add(ItemKind.Bullet, 17);
            inv.Add(ItemKind.CrabFragment, 2);
            for (int i = 0; i < Inventory.CraftCount; i++)
                inv.Craft[i] = new ItemStack(ItemKind.CrabFragment, 1);
            inv.Weapons[0] = new ItemStack(ItemKind.CrabCore, 1);
            _menuTime += (float)Config.FixedDt;
            for (int i = 0; i < 2; i++) { _renderer.DrawInventory(_world!, _inventory, _menuTime); _renderer.Present(); }
            Raylib.TakeScreenshot(_capturePath!);
            return true;
        }

        // SOLDIER capture. Photographing this chassis is a scripting problem the others
        // don't have: what is worth looking at — two cables out, the horizon banked over,
        // the wind streaking past — only exists several seconds into a swing that a human
        // has to fly. So the hatch flies it. VOIDTANKS_CAPTURE_HOOK picks the beat:
        //   fire   the first hook leaving the launcher, cable mid-flight
        //   swing  jumped, anchored, hanging and reeling on one cable
        //   both   the signature state — both hooks bitten, the body suspended between
        // Pair with VOIDTANKS_CLASS_INDEX=4 (which is what puts a soldier in the seat)
        // and a CAPTURE_FRAME late enough for the beat to have arrived.
        string? hook = Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_HOOK");
        if (hook != null && _world!.Player.Soldier is { } soldierRig)
        {
            if (_frame == 1 && hook != "fire") soldierRig.Jump(_world.Player);
            // Give the leap a moment to clear the ground before throwing anything, so
            // the arc has somewhere to go.
            if (_frame == (hook == "fire" ? 1 : 30)) _world.FireSoldierHookForTest(right: true);
            // The second cable goes out a beat later and a few degrees off the first, so
            // the two anchors are genuinely apart and the player hangs between them
            // rather than from one line drawn twice.
            if (hook == "both" && _frame == 55)
            {
                _world.Player.Heading += 0.5f;
                _world.FireSoldierHookForTest(right: false);
                _world.Player.Heading -= 0.5f;
            }
            // Hold the reel in from the moment the first hook bites, which is what
            // builds the speed the vignette and the bank are keyed to. Set on the world
            // rather than on the rig: the sim reads the keyboard into MoveInput at the
            // top of every step, so anything written straight onto the rig here is
            // overwritten before it can do anything.
            if (hook != "shoot" && _frame > 40)
                _world.ScriptedSoldierMove = new System.Numerics.Vector2(0f, 1f);

            // "shoot" is the weapons beat instead: a burst of rifle down the line of
            // sight with a rocket travelling out ahead of it, so the tracers, the brass,
            // the muzzle flash and the rocket's motor trail can all be photographed in
            // one frame. Nothing else stages this — the rounds are gone in a second.
            // One weapon at a time, because they share a cooldown: a rocket locks the
            // trigger for most of a second, so a beat that fires both photographs the
            // rocket and none of the rifle.
            if (hook == "shoot" && _frame >= 6) _world.FireSoldierRifleForTest();
            if (hook == "rocket" && _frame == 6) _world.FireSoldierRocketForTest();
        }

        // FISH capture. The same scripting problem the soldier has, for the same reason:
        // what is worth photographing on this chassis — a body carving hard between two
        // towers, a strike mid-flight, the bloom staining the top of the frame — only
        // exists several seconds into a swim a human has to fly. So the hatch swims it.
        // VOIDTANKS_CAPTURE_SWIM picks the beat:
        //   cruise  a level sprint, murk and bubbles up, the lantern trailing
        //   carve   the same, rolled hard over, the horizon on its side
        //   strike  the lunge, mid-flight, everything pinned flat
        //   bloom   nosed up into the ceiling, alarmed and stained
        //   beach   down on the deck, flopping, the drained wash over everything
        // Pair with VOIDTANKS_CLASS_INDEX=3 (which is what puts a fish in the seat) and a
        // CAPTURE_FRAME late enough for the beat to have arrived.
        string? swim = Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_SWIM");
        if (swim != null && _world!.Player.Fish is { } fishBody)
        {
            // Beat on the tail's own cadence for the whole run-up: it is an impulse, so
            // there is no key to hold and speed only exists if the harness keeps supplying
            // it. Everything but the beach beat needs the speed under it.
            if (swim != "beach" && _frame % 17 == 0) _world.BeatFishForTest();

            // A carve is a held roll, and a full one takes most of a second to wind in —
            // which is exactly why it has to be scripted rather than poked in on the grab
            // frame. Fed through the world for the same reason the soldier's reel is: the
            // sim reads the keys into MoveInput at the top of every step, so anything
            // written straight onto the rig is overwritten before it can do anything.
            if (swim == "carve") _world.ScriptedFishMove = new Vector2(1f, 0f);

            // Nose up and hold it: the thin water fights back, so reaching the bloom takes
            // a sustained climb rather than a frame of pitch.
            if (swim == "bloom") _world.Player.Pitch = 0.55f;

            // ...and the opposite, driven into the grid hard enough to be a real beaching
            // rather than a settle.
            if (swim == "beach")
            {
                _world.Player.Pitch = -0.9f;
                if (_frame % 9 == 0) _world.BeatFishForTest();
            }

            // The lunge is over in four tenths of a second, so it is thrown late and on
            // one exact frame — anything earlier and the grab lands during the recovery.
            if (swim == "strike" && _frame == 70) _world.StrikeFishForTest();
            if (swim == "spit" && _frame >= 6) _world.FireFishSpitForTest();
        }

        // VIRUS capture. The exposed mote needs no staging — the naked frame is the picture
        // — but every hosted state only exists after a possession, which in play means
        // flying into a body. VOIDTANKS_CAPTURE_VIRUS picks the beat:
        //   host   a hunter parked on the player on the first frame, worn by the second —
        //          the decay meter full and the veins just starting in
        //   rot    the same, with the meter drained low so the failing-host tear and the
        //          OVERLOAD OR HOP shout can be photographed
        //   crab   a Crab-Core raised and the mote flown into its gem, so the worn-crab
        //          state (the CRAB meter, the high eye) can be photographed
        //   lance  the same, with the broken lance fired — the only way to photograph the
        //          shafts, which burn for half a second
        //   maw    a Maw-Core raised and the mote flown into its crystal — the hovering
        //          host, photographed at its own height
        // Pair with VOIDTANKS_CLASS_INDEX=2 (which is what puts a virus in the seat).
        string? virusBeat = Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_VIRUS");
        if (virusBeat != null && _world!.Player.Virus is { } virusRig)
        {
            if (virusBeat is "host" or "rot")
            {
                if (_frame == 1)
                    _world.Enemies.Add(new EnemyTank(_world.Player.Position, elite: false));

                // Drained through the same soak the sim bills damage through, a chunk a
                // frame, so the meter the picture shows is one real fire could produce.
                if (virusBeat == "rot" && virusRig.Hosted && virusRig.Decay > 0.22f)
                    virusRig.AbsorbDamage(20f);
            }

            // The two monsters: raised on the first frame, entered on the second by
            // standing the mote in the core — the possession itself runs through the same
            // contact test play uses.
            if (virusBeat is "crab" or "lance")
            {
                if (_frame == 1) _world.SpawnCrabAhead();
                if (_frame == 2 && _world.Boss is { } crab)
                {
                    _world.Player.Position = crab.Position;
                    _world.Player.Height = CrabCore.CoreHitHeight;
                }
                // Fired once the body has settled, angled down so at least the aimed
                // shaft visibly meets the grid — the breaks go where they go.
                if (virusBeat == "lance" && _frame == 30)
                {
                    _world.Player.Pitch = -0.2f;
                    _world.FireVirusLanceForTest();
                }
            }

            // The squads. "soldier" raises one on a tower, seeds the nearest of them and
            // stands the mote on the body so the possession runs through the same contact
            // test play uses — the only way to photograph a worn person, since catching one
            // otherwise takes a human. "plague" wears a hunter instead and spends it in the
            // middle of the squad, so the outbreak can be caught mid-spray.
            if (virusBeat is "soldier" or "plague")
            {
                if (_frame == 1)
                {
                    if (virusBeat == "plague")
                        _world.Enemies.Add(new EnemyTank(_world.Player.Position, elite: false));
                    _world.SpawnSoldierSquad(_world.Player.Position + _world.Player.Forward * 40f);
                }

                if (_frame == 3 && _world.Soldiers.Count > 0)
                {
                    // Bring them in close: a capture wants the fight at knife range, not the
                    // forty metres a squad would honestly take a few seconds to cross.
                    for (int i = 0; i < _world.Soldiers.Count; i++)
                        _world.Soldiers[i].Position = Torus.Wrap(_world.Player.Position
                            + new Vector2(4f + i * 3f, 2f));
                }

                if (virusBeat == "soldier" && _frame == 4 && _world.Soldiers.Count > 0)
                {
                    var mark = _world.Soldiers[0];
                    mark.Tag();
                    _world.Player.Position = mark.Position;
                    _world.Player.Height = mark.Height;
                }

                if (virusBeat == "plague" && _frame == 6) _world.OverloadVirusForTest();
            }

            if (virusBeat == "maw")
            {
                if (_frame == 1) _world.SpawnMawAhead();
                if (_frame == 2 && _world.Maw is { } mouth)
                {
                    _world.Player.Position = mouth.Position;
                    _world.Player.Height = MawRig.CrystalWorldY;
                }
            }
        }

        // Squad capture: keep the view on one of them for the whole run. They are the only
        // enemy in the game that is never in the same place twice, so a fixed camera
        // photographs the city they have already left.
        // =track follows one of them through their arcs; =pose holds one crossing the frame
        // at knife range so the figure itself can be looked at.
        switch (Environment.GetEnvironmentVariable("VOIDTANKS_SQUAD_NEAR"))
        {
            case "track": _world!.FaceTheSquad(); break;
            case "pose": _world!.PoseSoldierForCapture(); break;
        }

        // Blast cinematic capture: stage a CRAB CORE detonation dead ahead on the first
        // frame, then grab it mid-swell (pair with VOIDTANKS_CAPTURE_FRAME≈40).
        if (Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_BLAST") != null && _frame == 1)
            _world!.StageCrabBlastAheadForTest();

        // HUD capture: equip a CRAB CORE so the R/T/Y/U slots show their 3D icon.
        if (Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_HUD") != null && _frame == 1)
            _world!.GiveCrabCore();

        // Let the world run so the enemy advances out of the fog toward the
        // player before we grab the frame.
        // SPIDER capture: VOIDTANKS_CAPTURE_LANCE=hold parks the chassis mid-charge (so
        // the meter and the gathering flare can be photographed), and =fire looses it on
        // the first frame so the shaft is burning by the time the grab lands. Paired
        // with VOIDTANKS_CLASS_INDEX=1, which is what put a spider in the seat.
        string? lance = Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_LANCE");

        // A held charge has to have the sim's combat input muted, because that input is
        // what decides a charge has been let go: the harness holds no button, so an
        // ordinary step would read the trigger as released and fire the lance every
        // frame — a picture of a beam where a picture of a full meter was wanted. The
        // flag is a field because muting it here is not enough: a capture frame that
        // isn't the grab returns false, and the ordinary loop then steps the world a
        // *second* time with input live, which would fire the charge anyway.
        _lanceHold = lance == "hold";
        // An empty frame: the capture harness holds no keys, and the poses it wants are
        // driven by the scripted hooks on World rather than by anything a hand would do.
        _world!.Update((float)Config.FixedDt, InputFrame.Empty, acceptCombatInput: !_lanceHold);

        if (lance != null && _world!.Player.Spider is { } cap)
        {
            if (lance == "hold")
            {
                // Re-wound after the sim step, not before: the step's own trigger
                // handling releases any charge whose button isn't down, and the harness
                // has no button. Held at about three quarters, which is where the meter
                // is most worth looking at — visibly filling, not yet full.
                cap.Cancel();
                for (int i = 0; i < 45; i++) cap.Hold((float)Config.FixedDt);
                _world.Player.Rooted = true;
            }
            else if (_frame == 1)
            {
                for (int i = 0; i < 100; i++) cap.Hold((float)Config.FixedDt);
                _world.FireSpiderLanceForTest();
                _world.FirePlayerShot(laser: true);
            }
        }

        // VOIDTANKS_CAPTURE_FELL=1 walks the craft up to the nearest tower, aims at it
        // and cuts it down with a full lance on the first frame — the only way to
        // photograph a collapse, which otherwise needs a human to find a building, stand
        // still for two seconds and let go at the right moment. Pair it with
        // VOIDTANKS_CLASS_INDEX=1 (a spider in the seat, so there is a lance at all) and a
        // CAPTURE_FRAME somewhere in the first two seconds, which is how long the topple
        // takes; later than that and the picture is of empty grid and settling dust.
        //
        // The craft is re-parked every frame, not just the first. The capture rig drives
        // the craft forward the whole time it runs, and a collapse takes nearly two
        // seconds — quite long enough for the building being photographed to leave the
        // side of the frame while it falls.
        if (Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_FELL") != null
            && _world!.Player.Spider is { } emitter)
        {
            if (_frame == 1)
            {
                float best = float.MaxValue;
                foreach (var s in _world.Structures)
                {
                    if (s.Kind != World.StructureKind.Tower) continue;
                    float d = Torus.DistanceSquared(s.Position, _world.Player.Position);
                    if (d < best) { best = d; _fellTarget = s; }
                }
            }

            if (_fellTarget != null)
            {
                // Stand off it at a distance that frames the whole building, and aim.
                Vector2 away = Vector2.Normalize(
                    Torus.Delta(_fellTarget.Position, _world.Player.Position)) * 42f;
                _world.Player.Position = Torus.Wrap(_fellTarget.Position + away);
                _world.Player.Heading = MathF.Atan2(-away.X, -away.Y);

                if (_frame == 1)
                {
                    for (int i = 0; i < 120; i++) emitter.Hold((float)Config.FixedDt);
                    _world.FireSpiderLanceForTest();
                }
            }
        }

        // Grab late enough that the enemy has closed to inside the fog boundary.
        int captureAt = int.TryParse(
            Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_FRAME"), out int cf) ? cf : 180;

        // VOIDTANKS_CAPTURE_STAGE=<CrabSeizure.Stage> waits for a named beat of the
        // seizure instead of counting frames. The cinematic's beats are short and the
        // protocol that leads into one is not frame-exact, so hunting for the scream by
        // guessing frame numbers mostly produces pictures of the empty grid.
        string? stage = Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_STAGE");
        if (stage != null)
        {
            if (_world.Seizure is not { } s
                || !s.Phase.ToString().Equals(stage, StringComparison.OrdinalIgnoreCase))
                return false;
            Draw();
            Draw();
            Raylib.TakeScreenshot(_capturePath!);
            return true;
        }

        // The same hatch for the Maw-Core's digestion. Its beats are even harder to
        // hit by frame number than the seizure's, because getting eaten depends on the
        // thing finishing a wind-up over a player who has to be standing still — so
        // waiting on the named stage is the only reliable way to photograph it.
        string? mawStage = Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_MAW_STAGE");
        if (mawStage != null)
        {
            if (_world.Digestion is not { } d
                || !d.Phase.ToString().Equals(mawStage, StringComparison.OrdinalIgnoreCase))
                return false;
            Draw();
            Draw();
            Raylib.TakeScreenshot(_capturePath!);
            return true;
        }

        if (_frame == captureAt)
        {
            // Optional pause capture, so the panel and its dim can be verified without a
            // human pressing Escape:
            //   VOIDTANKS_CAPTURE_PAUSE=<0..1>   the panel, dimmed by that amount
            //   VOIDTANKS_CAPTURE_PAUSE=controls the bindings page over a live world
            //   VOIDTANKS_CAPTURE_PAUSE=sound    the mixer over a live world
            // The last two are the only way to see the settings screen as it actually looks
            // in a match, which is a different picture from the same page over the menu's
            // drifting grid — and the one this pass most needed to be able to look at.
            string? pause = Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_PAUSE");
            if (pause != null)
            {
                if (float.TryParse(pause, out float pb)) _pauseBlur = Math.Clamp(pb, 0f, 1f);
                else
                {
                    _pauseBlur = 1f;
                    _pauseSettings = true;
                    _settingsScreen.OpenForCapture(pause,
                        int.TryParse(Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_ROW"),
                            out int prow) ? prow : 0);
                }

                DrawPaused();
                DrawPaused();
                Raylib.TakeScreenshot(_capturePath!);
                return true;
            }

            // Draw twice (see the UI branch): TakeScreenshot reads after the buffer
            // swap, so a single draw would grab the prior frame.
            Draw();
            Draw();
            Raylib.TakeScreenshot(_capturePath!);
            return true;
        }
        return false;
    }

    /// <summary>Capture-only: builds a representative lobby room so the dome, the planet, the
    /// stations and a few avatars can be photographed headlessly. VOIDTANKS_ROOM=antechamber
    /// grabs the entry face instead; otherwise it is the room proper with the local player
    /// seated as host and two others milling about.</summary>
    private World.LobbyRoom CaptureRoom()
    {
        if (_room != null) return _room;
        var room = new World.LobbyRoom { IsHost = true };
        if (Environment.GetEnvironmentVariable("VOIDTANKS_ROOM") == "antechamber")
            return _room = room;

        room.Seat(0, "HOST");
        room.ApplyPick(0, PlayerClass.Spider, ready: true);
        room.ApplyAvatar(1, "RAUL", PlayerClass.Tank, true,
            new System.Numerics.Vector2(-6f, 10f), 2.4f, 0f, 0f);
        room.ApplyAvatar(2, "MOTE", PlayerClass.Virus, false,
            new System.Numerics.Vector2(7f, 8f), -1.2f, 0f, 2.5f);
        // Stand the camera back a little, and tilt down so the shot frames the deck, the
        // avatars and the planet glowing up through the glass at once.
        room.Position = new System.Numerics.Vector2(0f, -14f);
        room.Pitch = -0.22f;
        return _room = room;
    }

    private void Update(float dt, in InputFrame input)
    {
        switch (_state)
        {
            // Paused is here for the networked case only — the loop does not step the sim
            // at all while a single-player run is paused. What reaches this in a match is an
            // empty frame, so the world carries on around a craft that has stopped driving.
            case GameState.Playing:
            case GameState.Paused:
                // Combat triggers are muted while the crafting panel is up so a click
                // on an item slot can't also fire the cannon; movement still runs.
                _world!.Update(dt, input, acceptCombatInput: !_inventoryOpen && !_lanceHold);
                break;
        }
    }

    private void Draw()
    {
        // The join/quit feed rides along in a match; single player passes null and draws none.
        _renderer.DrawWorld(_world!, _session?.Notices);
        _renderer.Present();
    }

    private void DrawMenu()
    {
        _renderer.DrawMenu(_menu, _menuTime);
        _renderer.Present();
    }

    private void DrawPaused()
    {
        if (_pauseSettings)
            _renderer.DrawPausedSettings(_world!, _settingsScreen, _menuTime, _pauseBlur);
        else
            _renderer.DrawPaused(_world!, _pauseMenu, _menuTime, _pauseBlur);
        _renderer.Present();
    }

    private void DrawInventory()
    {
        // Wall-clock time (not _menuTime, which is frozen during play) so the craftable
        // core's pulse animates while the live world runs behind the panel.
        _renderer.DrawInventory(_world!, _inventory, (float)Raylib.GetTime());
        _renderer.Present();
    }

    private void DrawSettings()
    {
        _renderer.DrawSettings(_settingsScreen, _menuTime);
        _renderer.Present();
    }

    private void DrawClassSelect()
    {
        _renderer.DrawClassSelect(_classSelect, _menuTime);
        _renderer.Present();
    }

    private void DrawTest()
    {
        _renderer.DrawTest(_testScreen, _menuTime);
        _renderer.Present();
    }

    public void Dispose()
    {
        _renderer.Dispose();
    }
}
