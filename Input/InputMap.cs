using Raylib_cs;
using VoidTanks.Core;

namespace VoidTanks.Input;

/// <summary>
/// Deliberately limited controls (Doc 03): drive, turn, jump. No strafe.
/// Having to turn your whole body to face or flee a threat is the point. The
/// concrete key bindings live in <see cref="Settings"/> (so they can be swapped
/// or reschemed from the menu); this stays the single read-point the sim polls.
/// Menu navigation is fixed and never rebindable.
/// </summary>
public static class InputMap
{
    /// <summary>
    /// The active control config. Set once at startup from the loaded settings;
    /// the settings screen mutates it in place so changes apply immediately.
    /// Falls back to defaults if never assigned (e.g. headless self-test).
    /// </summary>
    public static Settings Active { get; set; } = new();

    public static bool Forward => Active.ForwardDown();
    public static bool Back => Active.BackDown();
    public static bool TurnLeft => Active.TurnLeftDown();
    public static bool TurnRight => Active.TurnRightDown();

    // Jump is the primary dodge — a single press, slightly awkward, which is correct.
    public static bool JumpPressed => Active.JumpPressed();

    // Fire. Held is fine — the tank's own cooldown paces it, and ammo is finite.
    public static bool Fire => Active.FireDown();

    // Heavy grenade (Button B): a burst that spends ten rounds at once. Held is
    // fine; the longer grenade cooldown keeps it costly.
    public static bool Grenade => Active.GrenadeDown();

    // Hyperspace warp (Button X): one press panic-teleports across the map, if
    // the Hyper reserve can pay for it.
    public static bool HyperspacePressed => Active.HyperspacePressed();

    public static bool QuitPressed => Raylib.IsKeyPressed(KeyboardKey.Escape);

    // --- Menu navigation (fixed; only meaningful while a menu screen is up) ---
    public static bool MenuUp => Raylib.IsKeyPressed(KeyboardKey.W) || Raylib.IsKeyPressed(KeyboardKey.Up);
    public static bool MenuDown => Raylib.IsKeyPressed(KeyboardKey.S) || Raylib.IsKeyPressed(KeyboardKey.Down);
    public static bool MenuLeft => Raylib.IsKeyPressed(KeyboardKey.A) || Raylib.IsKeyPressed(KeyboardKey.Left);
    public static bool MenuRight => Raylib.IsKeyPressed(KeyboardKey.D) || Raylib.IsKeyPressed(KeyboardKey.Right);
    public static bool MenuConfirm => Raylib.IsKeyPressed(KeyboardKey.Enter)
                                      || Raylib.IsKeyPressed(KeyboardKey.Space);

    // Secret keybind: physical 'L' position drops into the (empty for now) test
    // screen. Undocumented on purpose — a maintenance hatch into the machine.
    public static bool SecretTestPressed => Raylib.IsKeyPressed(KeyboardKey.L);

    /// <summary>
    /// Number-row 1..4 as a just-pressed digit (0 if none). The test screen uses it
    /// to scrub between an animated specimen's phases.
    /// </summary>
    public static int MenuDigitPressed()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.One)) return 1;
        if (Raylib.IsKeyPressed(KeyboardKey.Two)) return 2;
        if (Raylib.IsKeyPressed(KeyboardKey.Three)) return 3;
        if (Raylib.IsKeyPressed(KeyboardKey.Four)) return 4;
        return 0;
    }
}
