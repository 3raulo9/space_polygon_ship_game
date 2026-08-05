using Raylib_cs;
using Unrendered.Core;

namespace Unrendered.Input;

/// <summary>
/// What is left of the old read-point once the simulation started taking its input as
/// <see cref="InputFrame"/>s.
///
/// <para>Everything a craft does now arrives through <see cref="InputSampler"/>, resolved
/// from <see cref="Bindings"/> — see <see cref="Btn"/> for why that moved. What stays here
/// are the reads that happen <em>outside</em> a simulation step and so have no frame to ride
/// in: the overlays the loop opens and closes, the scoreboard the renderer consults, the
/// spectator's steering, and the fixed keys that were never anybody's to rebind.</para>
/// </summary>
public static class InputMap
{
    /// <summary>
    /// The active control config. Set once at startup from the loaded settings;
    /// the settings screen mutates it in place so changes apply immediately.
    /// Falls back to defaults if never assigned (e.g. headless self-test).
    /// </summary>
    public static Settings Active { get; set; } = new();

    private static Bindings Binds => Active.Bindings;

    // --- Rebindable, but read outside the fixed step -----------------------------------

    /// <summary>
    /// Opens and closes the inventory / crafting panel, on every chassis. A just-pressed
    /// edge, so a held key doesn't flap the panel open and shut every frame.
    ///
    /// It used to be E, and moved because the SOLDIER's right hook is bound to E and its
    /// left to Q — a player chaining swings would have opened the pack a dozen times a
    /// minute. Moving it for that one class and leaving it on E for the rest would have
    /// been worse than either: the pack is the same pack, and a key that means "open my
    /// things" on one chassis and "throw a grappling hook" on another is a key nobody can
    /// build a habit around. F is one along from the hand already resting on WASD.
    /// </summary>
    public static bool InventoryToggle => Binds.Pressed(InputAction.Inventory);

    /// <summary>Held, not pressed: the scoreboard is a thing you look at while the match
    /// carries on around you, not a screen you enter and leave.</summary>
    public static bool ScoreboardDown => Binds.Down(InputAction.Scoreboard);

    /// <summary>Drops a marker on the world at the crosshair. A coordination tool, so it has
    /// to be reachable from whatever the chassis has the player doing.</summary>
    public static bool WorldPingPressed => Binds.Pressed(InputAction.Mark);

    /// <summary>While spectating: step to the previous / next living team-mate. Sitting on
    /// one player until they die is not watching a match, it is waiting. These may sit on
    /// buttons that mean something else to a living player — the loop only reads them once
    /// the player's own craft is gone.</summary>
    public static bool SpectatePrevPressed => Binds.Pressed(InputAction.SpectatePrev);
    public static bool SpectateNextPressed => Binds.Pressed(InputAction.SpectateNext);

    // --- Fixed: never rebindable --------------------------------------------------------
    // Escape and the menu keys are the way out of every screen including the one that would
    // rebind them, so they are the two things a player must not be able to lose.

    public static bool QuitPressed => Raylib.IsKeyPressed(KeyboardKey.Escape);

    /// <summary>F11 toggles borderless fullscreen. Read from every screen, so it works on
    /// the menu just as well as mid-run.</summary>
    public static bool FullscreenPressed => Raylib.IsKeyPressed(KeyboardKey.F11);

    /// <summary>Frame's mouse movement in pixels. Only meaningful while the cursor is
    /// captured, which the loop does for exactly as long as a craft is driving.</summary>
    public static System.Numerics.Vector2 LookDelta => Raylib.GetMouseDelta();

    // --- Menu navigation (fixed; only meaningful while a menu screen is up) ---
    public static bool MenuUp => Raylib.IsKeyPressed(KeyboardKey.W) || Raylib.IsKeyPressed(KeyboardKey.Up);
    public static bool MenuDown => Raylib.IsKeyPressed(KeyboardKey.S) || Raylib.IsKeyPressed(KeyboardKey.Down);
    public static bool MenuLeft => Raylib.IsKeyPressed(KeyboardKey.A) || Raylib.IsKeyPressed(KeyboardKey.Left);
    public static bool MenuRight => Raylib.IsKeyPressed(KeyboardKey.D) || Raylib.IsKeyPressed(KeyboardKey.Right);
    public static bool MenuConfirm => Raylib.IsKeyPressed(KeyboardKey.Enter)
                                      || Raylib.IsKeyPressed(KeyboardKey.Space);

    /// <summary>Clears a row's binding from the controls page. Delete, which is what it is
    /// called on every keyboard and means nothing else on a menu.</summary>
    public static bool MenuDelete => Raylib.IsKeyPressed(KeyboardKey.Delete)
                                     || Raylib.IsKeyPressed(KeyboardKey.Backspace);

    /// <summary>Either shift key, held. A modifier rather than an action: it never does
    /// anything on its own, it only changes what the click or key beside it means, so it is
    /// fixed and not rebindable — there is nothing to rebind it away from.</summary>
    public static bool ShiftHeld => Raylib.IsKeyDown(KeyboardKey.LeftShift)
                                    || Raylib.IsKeyDown(KeyboardKey.RightShift);

    // Tab walks between the panes of a multi-column screen (the class-select hangar
    // is the only one so far); Shift-Tab walks back. Fixed, like the rest of menu nav.
    public static bool MenuTab => Raylib.IsKeyPressed(KeyboardKey.Tab);
    public static bool MenuTabBack => ShiftHeld;

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

    // 'H' drops a four-man soldier squad onto a tower out in the fog — the only enemy
    // that can't simply be planted in front of you, since the whole thing starts with
    // them perched on something.
    public static bool DebugSpawnSquadPressed => Raylib.IsKeyPressed(KeyboardKey.H);

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
