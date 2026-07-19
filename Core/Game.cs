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
    private readonly SettingsScreen _settingsScreen;
    private readonly TestScreen _testScreen = new();
    private World.World? _world;
    private GameState _state = GameState.Menu;

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
            if (_capturePath != null && RunCaptureFrame()) break;

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

            if (InputMap.QuitPressed)
            {
                ReturnToMenu();
                continue;
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
                EnterSinglePlayer();
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
            // Draw twice so both the front and back buffers hold the same image;
            // TakeScreenshot reads after the swap, so a single draw would grab the
            // previous (blank) frame.
            for (int i = 0; i < 2; i++)
            {
                if (_captureScreen == "settings") DrawSettings();
                else if (_captureScreen == "test") DrawTest();
                else DrawMenu();
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
