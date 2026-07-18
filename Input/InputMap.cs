using Raylib_cs;

namespace VoidTanks.Input;

/// <summary>
/// Deliberately limited controls (Doc 03): drive, turn, jump. No strafe.
/// Having to turn your whole body to face or flee a threat is the point.
/// </summary>
public static class InputMap
{
    public static bool Forward => Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up);
    public static bool Back => Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down);
    public static bool TurnLeft => Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left);
    public static bool TurnRight => Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right);

    // Jump is the primary dodge — a single press, slightly awkward, which is correct.
    public static bool JumpPressed => Raylib.IsKeyPressed(KeyboardKey.Space);

    // Fire. Held is fine — the tank's own cooldown paces it, and ammo is finite.
    public static bool Fire => Raylib.IsKeyDown(KeyboardKey.LeftControl)
                               || Raylib.IsKeyDown(KeyboardKey.RightControl)
                               || Raylib.IsMouseButtonDown(MouseButton.Left)
                               || Raylib.IsKeyDown(KeyboardKey.Enter);

    public static bool QuitPressed => Raylib.IsKeyPressed(KeyboardKey.Escape);
}
