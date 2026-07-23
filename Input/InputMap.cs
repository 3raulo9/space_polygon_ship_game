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

    // 'E' opens and closes the inventory / crafting panel. A just-pressed edge so a
    // held key doesn't flap the panel open and shut every frame.
    public static bool InventoryToggle => Raylib.IsKeyPressed(KeyboardKey.E);

    /// <summary>
    /// The four equip slots, wired to the physical R / T / Y / U row above the
    /// movement keys. Returns which one was just pressed (0..3) or -1 for none — the
    /// world throws whatever that slot holds. Just-pressed so a held key throws once.
    /// </summary>
    public static int WeaponSlotPressed()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.R)) return 0;
        if (Raylib.IsKeyPressed(KeyboardKey.T)) return 1;
        if (Raylib.IsKeyPressed(KeyboardKey.Y)) return 2;
        if (Raylib.IsKeyPressed(KeyboardKey.U)) return 3;
        return -1;
    }

    // F11 toggles borderless fullscreen. Fixed (never rebindable) and read from
    // every screen, so it works on the menu just as well as mid-run.
    public static bool FullscreenPressed => Raylib.IsKeyPressed(KeyboardKey.F11);

    // --- Menu navigation (fixed; only meaningful while a menu screen is up) ---
    public static bool MenuUp => Raylib.IsKeyPressed(KeyboardKey.W) || Raylib.IsKeyPressed(KeyboardKey.Up);
    public static bool MenuDown => Raylib.IsKeyPressed(KeyboardKey.S) || Raylib.IsKeyPressed(KeyboardKey.Down);
    public static bool MenuLeft => Raylib.IsKeyPressed(KeyboardKey.A) || Raylib.IsKeyPressed(KeyboardKey.Left);
    public static bool MenuRight => Raylib.IsKeyPressed(KeyboardKey.D) || Raylib.IsKeyPressed(KeyboardKey.Right);
    public static bool MenuConfirm => Raylib.IsKeyPressed(KeyboardKey.Enter)
                                      || Raylib.IsKeyPressed(KeyboardKey.Space);

    // Tab walks between the panes of a multi-column screen (the class-select hangar
    // is the only one so far); Shift-Tab walks back. Fixed, like the rest of menu nav.
    public static bool MenuTab => Raylib.IsKeyPressed(KeyboardKey.Tab);
    public static bool MenuTabBack => Raylib.IsKeyDown(KeyboardKey.LeftShift)
                                      || Raylib.IsKeyDown(KeyboardKey.RightShift);

    // Secret keybind: physical 'L' position drops into the (empty for now) test
    // screen. Undocumented on purpose — a maintenance hatch into the machine.
    public static bool SecretTestPressed => Raylib.IsKeyPressed(KeyboardKey.L);

    // In-world twin of the same 'L' hatch: a debug spawn that drops one random enemy
    // onto the horizon each press, so threats can be stacked on demand while playing.
    public static bool DebugSpawnPressed => Raylib.IsKeyPressed(KeyboardKey.L);

    // 'N' toggles the spawn director off and on — a quiet field to test against
    // without the horizon refilling behind you. Only affects automatic spawning;
    // the manual hatches below still work.
    public static bool DebugNoSpawnPressed => Raylib.IsKeyPressed(KeyboardKey.N);

    // 'K' plants a Crab-Core dead ahead of the player, parked outside its own
    // detect radius so it stays dormant until you choose to walk into it.
    public static bool DebugSpawnCrabPressed => Raylib.IsKeyPressed(KeyboardKey.K);

    // 'J' hangs a Maw-Core well ahead of the player, parked outside its own detect
    // radius so it drifts nowhere until you walk under it. The mouth's twin of the
    // 'K' hatch above.
    public static bool DebugSpawnMawPressed => Raylib.IsKeyPressed(KeyboardKey.J);

    /// <summary>
    /// Number-row 1..6 as a just-pressed digit (0 if none). The test screen uses it
    /// to scrub between an animated specimen's phases — six of them now that the
    /// Crab-Core's lance charge and burn are their own states.
    /// </summary>
    public static int MenuDigitPressed()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.One)) return 1;
        if (Raylib.IsKeyPressed(KeyboardKey.Two)) return 2;
        if (Raylib.IsKeyPressed(KeyboardKey.Three)) return 3;
        if (Raylib.IsKeyPressed(KeyboardKey.Four)) return 4;
        if (Raylib.IsKeyPressed(KeyboardKey.Five)) return 5;
        if (Raylib.IsKeyPressed(KeyboardKey.Six)) return 6;
        return 0;
    }
}
