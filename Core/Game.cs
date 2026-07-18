using System.Numerics;
using Raylib_cs;
using VoidTanks.Entities;
using VoidTanks.Input;
using VoidTanks.Rendering;

namespace VoidTanks.Core;

/// <summary>
/// The loop. Simulation runs on a fixed timestep (deterministic movement and,
/// later, collision/AI); rendering is decoupled and runs once per frame.
/// </summary>
public sealed class Game : IDisposable
{
    private readonly Renderer _renderer;
    private readonly World.World _world;
    private GameState _state = GameState.Playing;

    private double _accumulator;

    // Verification harness: when VOIDTANKS_CAPTURE is set, run a scripted number
    // of frames, save a screenshot, and exit. Lets the render be checked without
    // a human at the window. No effect on normal play.
    private readonly string? _capturePath = Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE");
    private int _frame;

    public Game()
    {
        _renderer = new Renderer();
        _world = new World.World();

        // For capture, aim the craft at the seeded enemy so the shot frames it.
        // Harmless for normal play.
        if (_capturePath != null && _world.Enemies.Count > 0)
        {
            Vector2 to = _world.Enemies[0].Position - _world.Player.Position;
            _world.Player.Heading = MathF.Atan2(to.X, to.Y);
        }
    }

    public void Run()
    {
        while (!Raylib.WindowShouldClose())
        {
            if (InputMap.QuitPressed) break;

            if (_capturePath != null && RunCaptureFrame()) break;

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
    /// Scripted capture: advance a few frames of forward motion so the grid is
    /// clearly in view, then screenshot and signal exit. Returns true when done.
    /// </summary>
    private bool RunCaptureFrame()
    {
        // Let the world run so the enemy advances out of the fog toward the
        // player before we grab the frame.
        _world.Update((float)Config.FixedDt);
        _frame++;

        // Grab late enough that the enemy has closed to inside the fog boundary.
        int captureAt = int.TryParse(
            Environment.GetEnvironmentVariable("VOIDTANKS_CAPTURE_FRAME"), out int cf) ? cf : 180;
        if (_frame == captureAt)
        {
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
                _world.Update(dt);
                break;
        }
    }

    private void Draw()
    {
        _renderer.DrawWorld(_world);
        _renderer.Present();
    }

    public void Dispose()
    {
        _renderer.Dispose();
    }
}
