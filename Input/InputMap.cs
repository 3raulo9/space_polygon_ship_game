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

    // --- The TANK's siege kit ---------------------------------------------------
    // The heavy chassis gained a kit the day it gave up the jump. The plant rides the freed
    // jump binding — Space, or Shift when Space is the fire key (see Settings.JumpPressed) —
    // which is exactly the key that used to leave the ground and now digs in instead. The
    // other three are fixed keys under the left hand; they share physical keys with the
    // SOLDIER's hooks and the VIRUS's slots, but only ever one chassis reads them at a time,
    // the same way WASD already means a different thing on every craft.

    /// <summary>Dig in / stand up: the freed jump key. The treads never leave the grid now.</summary>
    public static bool TankPlantPressed => Active.JumpPressed();

    /// <summary>Q: the lurch — a track-boost dodge, paid out of the Hyper reserve.</summary>
    public static bool TankLurchPressed => Raylib.IsKeyPressed(KeyboardKey.Q);

    /// <summary>E: vent the smoke dischargers to blind the field.</summary>
    public static bool TankSmokePressed => Raylib.IsKeyPressed(KeyboardKey.E);

    /// <summary>R: the AP slug — a heavy round that punches through a line and through cover.</summary>
    public static bool TankSlugPressed => Raylib.IsKeyPressed(KeyboardKey.R);

    public static bool QuitPressed => Raylib.IsKeyPressed(KeyboardKey.Escape);

    /// <summary>
    /// 'F' opens and closes the inventory / crafting panel, on every chassis. A
    /// just-pressed edge, so a held key doesn't flap the panel open and shut every frame.
    ///
    /// It used to be E, and moved because the SOLDIER's right hook is bound to E and its
    /// left to Q — a player chaining swings would have opened the pack a dozen times a
    /// minute. Moving it for that one class and leaving it on E for the rest would have
    /// been worse than either: the pack is the same pack, and a key that means "open my
    /// things" on one chassis and "throw a grappling hook" on another is a key nobody can
    /// build a habit around. F is one along from the hand already resting on WASD.
    /// </summary>
    public static bool InventoryToggle => Raylib.IsKeyPressed(KeyboardKey.F);

    // --- The SOLDIER ---------------------------------------------------------
    // A separate scheme, not a re-skin of the tank's. This chassis is a person in first
    // person: the mouse is the aim, WASD is a body rather than a throttle, and the two
    // hooks are the whole game. Fixed bindings — the settings screen's schemes are all
    // about which key turns a vehicle, and none of them mean anything here.

    /// <summary>Frame's mouse movement in pixels. Only meaningful while the cursor is
    /// captured, which the loop does for exactly as long as a soldier is driving.</summary>
    public static System.Numerics.Vector2 LookDelta => Raylib.GetMouseDelta();

    /// <summary>E: throw the right hook, or let it go if it is already out.</summary>
    public static bool RightHookPressed => Raylib.IsKeyPressed(KeyboardKey.E);

    /// <summary>Q: the same, on the left.</summary>
    public static bool LeftHookPressed => Raylib.IsKeyPressed(KeyboardKey.Q);

    /// <summary>
    /// The gas burst that gets a soldier off the ground. ENTER, as the spec binds it —
    /// and SPACE alongside it, because every hand that has ever played a first-person
    /// game reaches for SPACE to jump, and a class whose whole opener is "leave the
    /// ground immediately" cannot afford the one second a player spends discovering that
    /// SPACE does nothing.
    /// </summary>
    public static bool HighJumpPressed
        => Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space);

    /// <summary>Left mouse: the rifle. Held is fine — the cadence paces it.</summary>
    public static bool RifleDown => Raylib.IsMouseButtonDown(MouseButton.Left);

    /// <summary>Right mouse: a rocket. A press, not a hold: six are carried and every
    /// one of them is a decision.</summary>
    public static bool RocketPressed => Raylib.IsMouseButtonPressed(MouseButton.Right);

    /// <summary>Raw WASD as (strafe, forward), each -1..1. A body, not a vehicle: A and
    /// D step sideways rather than turning, since the mouse is already doing the
    /// turning. Reads the physical keys and ignores the turn-swap setting, which is a
    /// preference about steering a craft.</summary>
    public static System.Numerics.Vector2 SoldierMove
    {
        get
        {
            float x = 0f, y = 0f;
            if (Raylib.IsKeyDown(KeyboardKey.D)) x += 1f;
            if (Raylib.IsKeyDown(KeyboardKey.A)) x -= 1f;
            if (Raylib.IsKeyDown(KeyboardKey.W)) y += 1f;
            if (Raylib.IsKeyDown(KeyboardKey.S)) y -= 1f;
            return new System.Numerics.Vector2(x, y);
        }
    }

    // --- The FISH ------------------------------------------------------------
    // Another scheme again, and the one difference from the soldier's that matters is
    // that this chassis has no held movement key at all. W is an <em>event</em>: one
    // press is one beat of the tail. Holding it does nothing, which is deliberate and is
    // the first thing a player discovers about the class.

    /// <summary>
    /// One beat of the tail. W is the key the hand is already on; SPACE is here for the
    /// same reason it is on the soldier — every hand that has played a first-person game
    /// reaches for it to leave the ground, and this is the chassis that most needs the
    /// player to succeed at that on their first try.
    /// </summary>
    public static bool BeatPressed
        => Raylib.IsKeyPressed(KeyboardKey.W) || Raylib.IsKeyPressed(KeyboardKey.Space);

    /// <summary>
    /// A and D as a roll, -1..1. Not a strafe and not a turn: it puts the body on its
    /// side, and the turn is what being on your side <em>earns</em> — see
    /// <see cref="Entities.FishRig"/>.
    /// </summary>
    public static float RollInput
    {
        get
        {
            float x = 0f;
            if (Raylib.IsKeyDown(KeyboardKey.D)) x += 1f;
            if (Raylib.IsKeyDown(KeyboardKey.A)) x -= 1f;
            return x;
        }
    }

    /// <summary>S folds the fins: a brake, held. There is no reverse on this chassis —
    /// turning round to leave is the same discipline the tank has always imposed.</summary>
    public static bool BrakeDown => Raylib.IsKeyDown(KeyboardKey.S);

    /// <summary>Left mouse: the spit. Held is fine — its own cadence paces it.</summary>
    public static bool SpitDown => Raylib.IsMouseButtonDown(MouseButton.Left);

    /// <summary>Right mouse: the strike. A press, not a hold — it commits the next second
    /// and a half of the player's life, and that is not something to hold a button
    /// through.</summary>
    public static bool StrikePressed => Raylib.IsMouseButtonPressed(MouseButton.Right);

    // --- The VIRUS -----------------------------------------------------------
    // The same first-person body scheme as the soldier and the fish, because it is the same
    // hand: the mouse aims, WASD moves. What differs is that this chassis never throws a hook
    // or beats a tail — it just flies and it just fires — so it needs only three reads.

    /// <summary>Raw WASD as (strafe, forward), each -1..1 — the mote's flight and the worn
    /// host's drive both take it. A body, not a vehicle: A and D step sideways, since the
    /// mouse already owns the turn. Shares the soldier's reading, physical keys only.</summary>
    public static System.Numerics.Vector2 VirusMove => SoldierMove;

    /// <summary>Left mouse: fire. The mote spits a weak round; a worn host fires its cannon.
    /// Held is fine — the cadence paces it.</summary>
    public static bool VirusFireDown => Raylib.IsMouseButtonDown(MouseButton.Left);

    /// <summary>Right mouse: overload the host into a bomb. A press, not a hold — it spends
    /// the whole body at once, which is not a thing to hold a button through. Dead while a
    /// mote, which has no host to spend.</summary>
    public static bool VirusOverloadPressed => Raylib.IsMouseButtonPressed(MouseButton.Right);

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
