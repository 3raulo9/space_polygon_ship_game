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
    private readonly SettingsScreen _settingsScreen;
    private readonly TestScreen _testScreen = new();
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
    private bool _captureMenu => _captureScreen is "1" or "menu" or "settings" or "test";
    private int _frame;

    public Game()
    {
        _renderer = new Renderer();

        // Load persisted controls and make them the live binding set the sim polls.
        _settings = Settings.Load();
        InputMap.Active = _settings;
        _settingsScreen = new SettingsScreen(_settings);

        // Capture runs the world directly (no menu), so build it now and aim the
        // craft at the seeded enemy. Normal play starts on the menu instead. The
        // menu-capture variant stays on the menu, so skip the world entirely.
        if (_capturePath != null && !_captureMenu)
        {
            EnterSinglePlayer();
            if (_world!.Enemies.Count > 0)
            {
                Vector2 to = _world.Enemies[0].Position - _world.Player.Position;
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

            // In-world Escape no longer bails to the menu — it opens the pause
            // panel (only Playing reaches here; Menu/Settings/Test handled above).
            if (InputMap.QuitPressed)
            {
                EnterPause();
                DrawPaused();
                continue;
            }

            // Debug hatch: 'L' drops one random enemy on the horizon each press.
            // Polled once per frame (a just-pressed edge), not per fixed step.
            if (InputMap.DebugSpawnPressed)
                _world!.SpawnRandomEnemy();

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

            Draw();
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
                // Dissolve the menu out, spin the world up at the crossover, then
                // resolve into the first-person view.
                BeginFade(EnterSinglePlayer);
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
        _world = new World.World();
        _state = GameState.Playing;
    }

    private void ReturnToMenu()
    {
        _world = null;
        _state = GameState.Menu;
        _accumulator = 0;
        _pauseBlur = 0f;
        _resuming = false;
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
                else if (_captureScreen == "test") _renderer.DrawTest(_testScreen, _menuTime);
                else _renderer.DrawMenu(_menu, _menuTime);
                _renderer.ApplyPixelDissolve(fa);
                _renderer.Present();
            }
            Raylib.TakeScreenshot(_capturePath!);
            return true;
        }

        // Let the world run so the enemy advances out of the fog toward the
        // player before we grab the frame.
        _world!.Update((float)Config.FixedDt);

        // Grab late enough that the enemy has closed to inside the fog boundary.
        int captureAt = int.TryParse(
            Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_FRAME"), out int cf) ? cf : 180;
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
                _world!.Update(dt);
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

    private void DrawSettings()
    {
        _renderer.DrawSettings(_settingsScreen, _menuTime);
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
