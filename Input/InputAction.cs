namespace Unrendered.Input;

/// <summary>
/// Every control a player is allowed to move. One entry per <em>thing you can do</em>,
/// which is not the same list as the physical keys the game reads: Q is the tank's lurch,
/// the spider's pounce and the soldier's left hook, and those are three rows here because
/// they are three decisions a player might want to make differently.
///
/// <para>The order is the order the controls page walks, so the sections below are also the
/// layout. Nothing is persisted by index — <see cref="Bindings"/> writes the enum
/// <em>name</em> — so rows may be inserted, moved or retired without stranding anybody's
/// controls.cfg.</para>
/// </summary>
public enum InputAction : byte
{
    // --- GENERAL ---------------------------------------------------------------------
    // Read on every chassis, in every match.
    Forward,
    Back,
    TurnLeft,
    TurnRight,
    Jump,
    Fire,
    Secondary,
    Hyperspace,
    Inventory,
    Mark,
    Scoreboard,

    /// <summary>Held during a DESCENT salvage window to cut the break short. Its own row rather
    /// than a reuse of INTERACT — which is otherwise never read inside a match — because
    /// INTERACT defaults to E, and E is already the TANK's smoke and the SOLDIER's right hook.
    /// A key that lays a screen and votes to end the break in the same press is not one key.</summary>
    Ready,

    // --- TANK ------------------------------------------------------------------------
    // PLANT is deliberately absent: the heavy chassis digs in on the jump binding, which is
    // the key that used to leave the ground and now refuses to. Giving it a second row here
    // would let a player bind two contradictory answers to one question.
    TankLurch,
    TankSmoke,
    TankSlug,

    // --- SPIDER ----------------------------------------------------------------------
    // The claw and the emitter ride FIRE and SECONDARY, so the legs are the only row.
    SpiderPounce,

    // --- SOLDIER ---------------------------------------------------------------------
    LeftHook,
    RightHook,
    HighJump,

    // --- FISH ------------------------------------------------------------------------
    // The roll is the turn bindings and the spit is FIRE; what is its own is the tail.
    Beat,
    Brake,

    // --- FLOWER ----------------------------------------------------------------------
    // The seed and the petal ride FIRE and SECONDARY; these two are the class's own.
    Harvest,
    Replant,

    // --- VIRUS -----------------------------------------------------------------------
    Slot1,
    Slot2,
    Slot3,
    Slot4,

    // --- SPECTATING -------------------------------------------------------------------
    // Only ever read while your craft is gone, which is why these may sit on keys that mean
    // something else to a living player.
    SpectatePrev,
    SpectateNext,

    // --- LOBBY -------------------------------------------------------------------------
    /// <summary>Use the thing in front of you in the multiplayer room — a pod, the console,
    /// the name plate. Never read inside a match.</summary>
    Interact,
}

/// <summary>Which part of the game reads an action. Drives both the headings on the controls
/// page and, more importantly, what counts as a clash — see <see cref="Bindings.Clashes"/>.</summary>
public enum InputSection : byte
{
    General,
    Tank,
    Spider,
    Soldier,
    Fish,
    Flower,
    Virus,
    Spectating,
    Lobby,
}

/// <summary>The names and grouping the controls page draws. Pure data, kept beside the enum
/// so a new action is one edit rather than three.</summary>
public static class InputActions
{
    /// <summary>Every action, in page order.</summary>
    public static readonly InputAction[] All = System.Enum.GetValues<InputAction>();

    public static InputSection SectionOf(InputAction a) => a switch
    {
        <= InputAction.Ready => InputSection.General,
        <= InputAction.TankSlug => InputSection.Tank,
        <= InputAction.SpiderPounce => InputSection.Spider,
        <= InputAction.HighJump => InputSection.Soldier,
        <= InputAction.Brake => InputSection.Fish,
        <= InputAction.Replant => InputSection.Flower,
        <= InputAction.Slot4 => InputSection.Virus,
        <= InputAction.SpectateNext => InputSection.Spectating,
        _ => InputSection.Lobby,
    };

    public static string SectionLabel(InputSection s) => s switch
    {
        InputSection.General => "GENERAL",
        InputSection.Tank => "TANK",
        InputSection.Spider => "SPIDER",
        InputSection.Soldier => "SOLDIER",
        InputSection.Fish => "FISH",
        InputSection.Flower => "FLOWER",
        InputSection.Virus => "VIRUS",
        InputSection.Spectating => "SPECTATING",
        _ => "LOBBY",
    };

    /// <summary>
    /// A line beside the heading saying what the chassis does with the controls it is
    /// <em>not</em> being offered a row for. Without it the SPIDER section is a single row and
    /// reads like an oversight rather than a design.
    ///
    /// <para>Kept under about thirty characters. It is right-aligned against the same column
    /// the bindings end at, with the section heading on the left of the same line, and a
    /// longer one walks straight into it.</para>
    /// </summary>
    public static string? SectionNote(InputSection s) => s switch
    {
        InputSection.Tank => "PLANT = JUMP",
        InputSection.Spider => "CLAW/EMITTER = FIRE/SEC",
        InputSection.Soldier => "RIFLE/ROCKET = FIRE/SEC",
        InputSection.Fish => "ROLL=TURN SPIT/STRIKE=FIRE/SEC",
        InputSection.Flower => "LEAN=MOVE PETAL=SEC",
        InputSection.Virus => "FIRE/OVERLOAD = FIRE/SEC",
        _ => null,
    };

    public static string Label(InputAction a) => a switch
    {
        InputAction.Forward => "FORWARD",
        InputAction.Back => "BACK",
        InputAction.TurnLeft => "TURN LEFT",
        InputAction.TurnRight => "TURN RIGHT",
        InputAction.Jump => "JUMP",
        InputAction.Fire => "FIRE",
        InputAction.Secondary => "SECONDARY",
        InputAction.Hyperspace => "HYPERSPACE",
        InputAction.Inventory => "INVENTORY",
        InputAction.Mark => "MARK",
        InputAction.Scoreboard => "SCOREBOARD",
        InputAction.Ready => "READY (DESCENT)",

        InputAction.TankLurch => "LURCH",
        InputAction.TankSmoke => "SMOKE",
        InputAction.TankSlug => "AP SLUG",

        InputAction.SpiderPounce => "POUNCE",

        InputAction.LeftHook => "LEFT HOOK",
        InputAction.RightHook => "RIGHT HOOK",
        InputAction.HighJump => "HIGH JUMP",

        InputAction.Beat => "TAIL BEAT",
        InputAction.Brake => "BRAKE",

        InputAction.Harvest => "SET SEED",
        InputAction.Replant => "REPLANT (HOLD)",

        InputAction.Slot1 => "SLOT 1",
        InputAction.Slot2 => "SLOT 2",
        InputAction.Slot3 => "SLOT 3",
        InputAction.Slot4 => "SLOT 4",

        InputAction.SpectatePrev => "PREVIOUS",
        InputAction.SpectateNext => "NEXT",

        _ => "INTERACT",
    };
}
