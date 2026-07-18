using Raylib_cs;
using VoidTanks.Core;

// Entry point: bootstrap the window + loop. Everything 3D renders to a small
// internal target and is upscaled nearest-neighbor inside the Renderer.

// Headless combat self-test — no window. Verifies the sim without a display.
if (args.Contains("--selftest"))
    return SelfTest.Run();

Raylib.SetConfigFlags(ConfigFlags.VSyncHint);
Raylib.InitWindow(Config.WindowWidth, Config.WindowHeight, "VOID TANKS");
Raylib.SetExitKey(KeyboardKey.Null); // Escape is handled in the loop, not by Raylib
Raylib.SetTargetFPS(60);

using (var game = new Game())
{
    game.Run();
}

Raylib.CloseWindow();
return 0;
