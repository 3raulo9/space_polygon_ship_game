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

    // Pause pixel-blur amount: 0 clean, 1 fully coarsened/dimmed. Eases in when
    // pausing, out when resuming. Seconds for a full sweep set by PauseFade.
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
    private readonly string? _captureScreen =
        Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_MENU");
    private bool _captureMenu =>
        _captureScreen is "1" or "menu" or "settings" or "test" or "class";
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
            Vector2? subject = _world!.Player.Soldier != null ? null
                : _world.Maw?.Position
                ?? (Vector2?)_world.Boss?.Position
                ?? (_world.Enemies.Count > 0 ? _world.Enemies[0].Position : null);
            if (subject is { } at)
            {
                Vector2 to = at - _world.Player.Position;
                _world.Player.Heading = MathF.Atan2(to.X, to.Y);
            }
        }
    }

    public void Run()
    {
        while (!Raylib.WindowShouldClose())
        {
            // Drain any time-scheduled audio (the boss's death cascade). Sits above
            // every early-out below on purpose: the cascade is queued as absolute
            // wall-clock times, so if the player pauses or bails to the menu part-way
            // through it still finishes rather than stranding a half-played death.
            Audio.Update();

            if (_capturePath != null && RunCaptureFrame()) break;

            SyncCursor();

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

            // Paused: world frozen behind the pixel-blur, pause panel driving.
            if (_state == GameState.Paused)
            {
                UpdatePaused();
                // "Back to menu" tears the world down inside UpdatePaused, so only
                // draw the paused frame while we're actually still paused — next
                // iteration draws whatever state we left for (menu or resumed play).
                if (_state == GameState.Paused) DrawPaused();
                continue;
            }

            // 'F' toggles the inventory overlay. Escape closes it too if it's open;
            // otherwise Escape opens the pause panel (the world only pauses when the
            // inventory is *not* up — the panel itself never freezes the sim).
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
                // R/T/Y/U throw whatever the matching equip slot holds (the crafted CRAB
                // CORE). Polled once per frame as a just-pressed edge, like the debug keys.
                int weaponSlot = InputMap.WeaponSlotPressed();
                if (weaponSlot >= 0)
                    _world!.UseWeaponSlot(weaponSlot);

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
            }

            // Accumulate real elapsed time and step the sim in fixed increments,
            // so a fast or slow display never changes the physics.
            _accumulator += Raylib.GetFrameTime();
            // Guard against spiral-of-death after a stall.
            if (_accumulator > 0.25) _accumulator = 0.25;

            while (_accumulator >= Config.FixedDt)
            {
                Update((float)Config.FixedDt);
                _accumulator -= Config.FixedDt;
            }

            // The live world, with the crafting panel laid over it when it's open.
            if (_inventoryOpen) DrawInventory();
            else Draw();
        }
    }

    /// <summary>True while the pointer is captured — locked to the window and feeding
    /// relative movement to the SOLDIER's look.</summary>
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
    /// The SOLDIER additionally needs the pointer <em>captured</em>: its look is driven
    /// by relative mouse movement, which means the real cursor has to be locked to the
    /// window centre or it walks off the edge of the screen mid-swing and the head stops
    /// turning. Captured only while that chassis is actually driving — the pause panel,
    /// the pack and every menu hand the mouse back.
    /// </summary>
    private void SyncCursor()
    {
        bool wantCapture = _state == GameState.Playing
                        && !_inventoryOpen
                        && !_fading
                        && _world?.Player.Soldier != null;

        if (wantCapture == _mouseCaptured)
        {
            // Raylib's HideCursor is not sticky across every window event, so it is
            // re-asserted each frame while the pointer is free. Cheap, and it is the
            // difference between "no cursor" and "no cursor most of the time".
            if (!_mouseCaptured) Raylib.HideCursor();
            return;
        }

        _mouseCaptured = wantCapture;
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
            case Menu.Action.OpenSettings:
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
                BeginFade(EnterSinglePlayer);
                break;
            case ClassSelectScreen.Action.Back:
                BeginFade(() => _state = GameState.Menu);
                break;
        }
    }

    /// <summary>
    /// Advances the controls screen. Leaving it saves the (already-live) settings
    /// to disk so the choices — including the launch-time turn swap — persist.
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
    /// Freezes the run and opens the pause panel. The blur starts clean and eases
    /// in over the next few frames; the sim stops stepping until it resumes.
    /// </summary>
    private void EnterPause()
    {
        _pauseMenu.Reset();
        _resuming = false;
        _state = GameState.Paused;
        Audio.PlayBlip();
    }

    /// <summary>
    /// Advances the pause panel and its blur. Entering, the blur eases toward full;
    /// once the player asks to resume it eases back out, and only when it has fully
    /// cleared do we hand control back to the sim (so the world un-blurs before it
    /// moves again). "Back to menu" abandons the run outright.
    /// </summary>
    private void UpdatePaused()
    {
        _menuTime += Raylib.GetFrameTime();

        // Ease the blur toward its target: in while paused, out while resuming.
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

        switch (_pauseMenu.Update())
        {
            case PauseMenu.Action.Resume:
                _resuming = true;
                break;
            case PauseMenu.Action.BackToMenu:
                // Carry the pause blur straight into the fade (start already
                // coarsened) so the paused world sinks to void and the menu pixel-
                // resolves in — one continuous dissolve, no sharp flash between.
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
            default: _renderer.DrawWorld(_world!); break; // Playing / Paused
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

    private void EnterSinglePlayer()
    {
        _world = new World.World(_loadout);
        _state = GameState.Playing;
        _inventoryOpen = false;
    }

    private void ReturnToMenu()
    {
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
                if (_captureScreen == "settings") _renderer.DrawSettings(_settingsScreen, _menuTime);
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
        _world!.Update((float)Config.FixedDt, acceptCombatInput: !_lanceHold);

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
            // Optional pause-blur capture: VOIDTANKS_CAPTURE_PAUSE=<0..1> grabs the
            // frozen frame under the pixel-blur at that amount, so the transition
            // and panel can be verified without a human pressing Escape.
            string? pause = Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_PAUSE");
            if (pause != null && float.TryParse(pause, out float pb))
            {
                _pauseBlur = Math.Clamp(pb, 0f, 1f);
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

    private void Update(float dt)
    {
        switch (_state)
        {
            case GameState.Playing:
                // Combat triggers are muted while the crafting panel is up so a click
                // on an item slot can't also fire the cannon; movement still runs.
                _world!.Update(dt, acceptCombatInput: !_inventoryOpen && !_lanceHold);
                break;
        }
    }

    private void Draw()
    {
        _renderer.DrawWorld(_world!);
        _renderer.Present();
    }

    private void DrawMenu()
    {
        _renderer.DrawMenu(_menu, _menuTime);
        _renderer.Present();
    }

    private void DrawPaused()
    {
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
