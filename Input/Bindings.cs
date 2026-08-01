using Raylib_cs;

namespace Unrendered.Input;

/// <summary>
/// The whole control layout: one <see cref="InputBind"/> per <see cref="InputAction"/>.
///
/// <para>This replaced three curated presets — a turn swap, a WASD/arrows choice and a
/// three-way fire key. Those existed because a rebinding screen is a lot of surface and the
/// game did not have one; now that it does, a preset is a worse version of the same answer,
/// and every one of them is expressible as a binding.</para>
/// </summary>
public sealed class Bindings
{
    private readonly InputBind[] _binds = new InputBind[InputActions.All.Length];

    public Bindings() => Reset();

    public InputBind this[InputAction a]
    {
        get => _binds[(int)a];
        set => _binds[(int)a] = value;
    }

    public bool Down(InputAction a) => _binds[(int)a].Down();
    public bool Pressed(InputAction a) => _binds[(int)a].Pressed();

    /// <summary>Puts every row back to the shipped layout.</summary>
    public void Reset()
    {
        foreach (var a in InputActions.All) _binds[(int)a] = Default(a);
    }

    /// <summary>
    /// The layout the game ships with — which is, deliberately, exactly what the hardcoded
    /// controls were before this screen existed. Somebody who never opens the page must not
    /// be able to tell that it appeared.
    /// </summary>
    public static InputBind Default(InputAction a)
    {
        static InputBind K(KeyboardKey a, KeyboardKey b) => new(InputCode.Key(a), InputCode.Key(b));
        static InputBind One(KeyboardKey k) => new(InputCode.Key(k));
        static InputBind M(MouseButton m, KeyboardKey k) => new(InputCode.Mouse(m), InputCode.Key(k));

        return a switch
        {
            InputAction.Forward => K(KeyboardKey.W, KeyboardKey.Up),
            InputAction.Back => K(KeyboardKey.S, KeyboardKey.Down),
            InputAction.TurnLeft => K(KeyboardKey.A, KeyboardKey.Left),
            InputAction.TurnRight => K(KeyboardKey.D, KeyboardKey.Right),
            InputAction.Jump => One(KeyboardKey.Space),
            InputAction.Fire => M(MouseButton.Left, KeyboardKey.LeftControl),
            InputAction.Secondary => M(MouseButton.Right, KeyboardKey.G),
            InputAction.Hyperspace => One(KeyboardKey.X),
            InputAction.Inventory => One(KeyboardKey.F),

            // Middle mouse, with a key for anyone whose hand is nowhere near the wheel. That
            // key used to be G — which is also SECONDARY's, so pressing it threw a grenade
            // and dropped a marker in the same frame. B is free and next to nothing.
            InputAction.Mark => M(MouseButton.Middle, KeyboardKey.B),
            InputAction.Scoreboard => One(KeyboardKey.Tab),

            InputAction.TankLurch => One(KeyboardKey.Q),
            InputAction.TankSmoke => One(KeyboardKey.E),
            InputAction.TankSlug => One(KeyboardKey.R),

            InputAction.SpiderPounce => One(KeyboardKey.Q),

            InputAction.LeftHook => One(KeyboardKey.Q),
            InputAction.RightHook => One(KeyboardKey.E),
            InputAction.HighJump => K(KeyboardKey.Enter, KeyboardKey.Space),

            InputAction.Beat => K(KeyboardKey.W, KeyboardKey.Space),
            InputAction.Brake => One(KeyboardKey.S),

            InputAction.Slot1 => One(KeyboardKey.R),
            InputAction.Slot2 => One(KeyboardKey.T),
            InputAction.Slot3 => One(KeyboardKey.Y),
            InputAction.Slot4 => One(KeyboardKey.U),

            InputAction.SpectatePrev => new(InputCode.Key(KeyboardKey.Left), InputCode.Mouse(MouseButton.Left)),
            InputAction.SpectateNext => new(InputCode.Key(KeyboardKey.Right), InputCode.Mouse(MouseButton.Right)),

            // E alone. ENTER is the room's confirm — it opens the name editor and pulls the
            // launch lever — and is read as a literal key there alongside Escape and the
            // arrows, so binding it here as well would mean walking up to a pod and being
            // handed a text field instead.
            _ => One(KeyboardKey.E),
        };
    }

    /// <summary>
    /// Whether this action is fighting with another over a button, and with which.
    ///
    /// <para>Only actions in the same <see cref="InputSection"/> can clash. That is not a
    /// simplification — it is the game's oldest rule written down. Q has always been the
    /// tank's lurch, the spider's pounce and the soldier's left hook; W both drives a machine
    /// and beats a fish's tail. Never more than one chassis is being read, so those are not
    /// collisions, and flagging them would paint the shipped layout red on a first launch and
    /// teach the player to ignore the colour. What is a real collision is FIRE and SECONDARY
    /// on the same button, and that is what stays visible.</para>
    /// </summary>
    public InputAction? Clashes(InputAction a)
    {
        var bind = _binds[(int)a];
        var section = InputActions.SectionOf(a);

        foreach (var other in InputActions.All)
        {
            if (other == a) continue;
            if (InputActions.SectionOf(other) != section) continue;
            var o = _binds[(int)other];
            if (o.Uses(bind.Primary) || o.Uses(bind.Secondary)) return other;
        }
        return null;
    }

    // --- Persistence ------------------------------------------------------------------
    // Keyed by the action's enum NAME, so rows can be added, moved or retired without
    // scrambling a config written by an older build. An unknown name is skipped and a
    // missing one keeps its default, which is what makes an upgrade a non-event.

    public IEnumerable<string> Save()
    {
        foreach (var a in InputActions.All)
        {
            var b = _binds[(int)a];
            yield return $"bind.{a} = {b.Primary.Token} , {b.Secondary.Token}";
        }
    }

    /// <summary>Reads one <c>bind.Action = primary , secondary</c> line. Returns false for
    /// anything it does not recognise, which the caller treats as "not ours".</summary>
    public bool Load(string key, string value)
    {
        if (!key.StartsWith("bind.", System.StringComparison.OrdinalIgnoreCase)) return false;
        if (!System.Enum.TryParse(key[5..], ignoreCase: true, out InputAction action)) return false;

        string[] parts = value.Split(',');
        if (!InputCode.TryParse(parts[0], out InputCode primary)) return false;

        InputCode secondary = InputCode.None;
        if (parts.Length > 1 && !InputCode.TryParse(parts[1], out secondary)) return false;

        _binds[(int)action] = new InputBind(primary, secondary);
        return true;
    }

    // --- Capture ----------------------------------------------------------------------

    /// <summary>
    /// The button the player just pressed, for the rebinding prompt, or null if they have not
    /// pressed one yet. Escape and the mouse wheel are not offered: Escape has to stay
    /// available to back out of the prompt, and a wheel notch is not a button anything in
    /// this game could read as held.
    /// </summary>
    public static InputCode? CapturePressed()
    {
        int key = Raylib.GetKeyPressed();
        if (key != 0 && key != (int)KeyboardKey.Escape) return new InputCode(key);

        for (int b = 0; b <= (int)MouseButton.Back; b++)
            if (Raylib.IsMouseButtonPressed((MouseButton)b)) return InputCode.Mouse((MouseButton)b);

        return null;
    }
}
