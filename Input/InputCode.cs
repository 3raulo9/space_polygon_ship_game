using Raylib_cs;

namespace VoidTanks.Input;

/// <summary>
/// One physical button — a key or a mouse button — as a single number, so a binding table
/// can hold either without caring which.
///
/// <para>Raylib's <see cref="KeyboardKey"/> values are its own scancode-ish integers and top
/// out well under a thousand, so mouse buttons are simply parked above them. Nothing here
/// assumes the two spaces are contiguous or ordered; the only thing that matters is that
/// they cannot collide.</para>
/// </summary>
public readonly record struct InputCode(int Raw)
{
    /// <summary>Where the mouse buttons start. Comfortably above every raylib key code
    /// (the highest, KB_MENU, is 348) with room for the enum to grow.</summary>
    public const int MouseBase = 1000;

    /// <summary>Nothing bound. A binding may legitimately hold this in either slot — an
    /// action with no key is an action the player has decided not to have.</summary>
    public static readonly InputCode None = new(0);

    public static InputCode Key(KeyboardKey k) => new((int)k);
    public static InputCode Mouse(MouseButton b) => new(MouseBase + (int)b);

    public bool IsNone => Raw == 0;
    public bool IsMouse => Raw >= MouseBase;

    /// <summary>Held right now. Safe to ask with nothing bound — the answer is no.</summary>
    public bool Down() => Raw == 0
        ? false
        : IsMouse
            ? Raylib.IsMouseButtonDown((MouseButton)(Raw - MouseBase))
            : Raylib.IsKeyDown((KeyboardKey)Raw);

    /// <summary>Went down this frame.</summary>
    public bool Pressed() => Raw == 0
        ? false
        : IsMouse
            ? Raylib.IsMouseButtonPressed((MouseButton)(Raw - MouseBase))
            : Raylib.IsKeyPressed((KeyboardKey)Raw);

    // --- How it reads on screen -------------------------------------------------------

    /// <summary>
    /// What the controls page prints. Short on purpose: the value column is about sixty
    /// internal pixels wide and has to hold two of these, so "L-CTRL" rather than
    /// "LEFT CONTROL" and "LMB" rather than "MOUSE LEFT".
    /// </summary>
    public string Label
    {
        get
        {
            if (Raw == 0) return "--";
            if (IsMouse)
                return (MouseButton)(Raw - MouseBase) switch
                {
                    MouseButton.Left => "LMB",
                    MouseButton.Right => "RMB",
                    MouseButton.Middle => "MMB",
                    MouseButton.Side => "M4",
                    MouseButton.Extra => "M5",
                    MouseButton.Forward => "M-FWD",
                    MouseButton.Back => "M-BACK",
                    var b => "M" + (int)b,
                };

            return (KeyboardKey)Raw switch
            {
                KeyboardKey.Space => "SPACE",
                KeyboardKey.Enter => "ENTER",
                KeyboardKey.KpEnter => "KP-ENT",
                KeyboardKey.Tab => "TAB",
                KeyboardKey.Backspace => "BKSP",
                KeyboardKey.Escape => "ESC",
                KeyboardKey.LeftControl => "L-CTRL",
                KeyboardKey.RightControl => "R-CTRL",
                KeyboardKey.LeftShift => "L-SHFT",
                KeyboardKey.RightShift => "R-SHFT",
                KeyboardKey.LeftAlt => "L-ALT",
                KeyboardKey.RightAlt => "R-ALT",
                KeyboardKey.Up => "UP",
                KeyboardKey.Down => "DOWN",
                KeyboardKey.Left => "LEFT",
                KeyboardKey.Right => "RIGHT",
                KeyboardKey.Comma => ",",
                KeyboardKey.Period => ".",
                KeyboardKey.Slash => "/",
                KeyboardKey.Semicolon => ";",
                KeyboardKey.Apostrophe => "'",
                KeyboardKey.LeftBracket => "[",
                KeyboardKey.RightBracket => "]",
                KeyboardKey.Backslash => "\\",
                KeyboardKey.Minus => "-",
                KeyboardKey.Equal => "=",
                KeyboardKey.Grave => "`",
                var k => Short(k),
            };
        }
    }

    /// <summary>Whatever the enum calls it, upper-cased and with the noise stripped: the
    /// letters and digits come back as themselves, F-keys and keypad keys as something a
    /// player recognises, and anything else as its own name rather than a number.</summary>
    private static string Short(KeyboardKey k)
    {
        string name = System.Enum.GetName(k) ?? ((int)k).ToString();
        if (name.StartsWith("Kp", System.StringComparison.Ordinal) && name.Length > 2)
            return "KP" + name[2..].ToUpperInvariant();
        // "One".."Nine"/"Zero" are the number row; a player calls those 1..9 and 0.
        int digit = System.Array.IndexOf(DigitNames, name);
        if (digit >= 0) return digit.ToString();
        return name.ToUpperInvariant();
    }

    private static readonly string[] DigitNames =
        ["Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine"];

    // --- How it reads on disk ---------------------------------------------------------
    // The enum's own name, so controls.cfg stays something a human can open and fix. Never
    // the number: raylib's key codes are an implementation detail of a dependency, and a
    // config written in them would silently mean something else after an upgrade.

    public string Token => Raw == 0
        ? "none"
        : IsMouse
            ? "mouse:" + System.Enum.GetName((MouseButton)(Raw - MouseBase))
            : "key:" + System.Enum.GetName((KeyboardKey)Raw);

    public static bool TryParse(string s, out InputCode code)
    {
        code = None;
        s = s.Trim();
        if (s.Length == 0 || s.Equals("none", System.StringComparison.OrdinalIgnoreCase))
            return true;   // an explicit "nothing" is a successful parse, not a failure

        if (s.StartsWith("mouse:", System.StringComparison.OrdinalIgnoreCase))
        {
            if (!System.Enum.TryParse(s[6..], ignoreCase: true, out MouseButton b)) return false;
            code = Mouse(b);
            return true;
        }

        if (s.StartsWith("key:", System.StringComparison.OrdinalIgnoreCase)) s = s[4..];
        if (!System.Enum.TryParse(s, ignoreCase: true, out KeyboardKey k)) return false;
        code = Key(k);
        return true;
    }
}

/// <summary>
/// What one action is bound to: a first choice and a fallback.
///
/// Two slots rather than one because the game already doubles nearly everything up — W or
/// the up arrow, the left mouse button or control — and collapsing that to a single key
/// would take away controls that exist today. Two rather than three because a third is a
/// column nobody fills.
/// </summary>
public readonly record struct InputBind(InputCode Primary, InputCode Secondary)
{
    public static readonly InputBind Unbound = new(InputCode.None, InputCode.None);

    public InputBind(InputCode primary) : this(primary, InputCode.None) { }

    public bool Down() => Primary.Down() || Secondary.Down();
    public bool Pressed() => Primary.Pressed() || Secondary.Pressed();

    /// <summary>Slot 0 is primary, 1 is secondary. Out-of-range reads as unbound rather than
    /// throwing: this is indexed by a cursor on a UI page.</summary>
    public InputCode this[int slot] => slot == 0 ? Primary : slot == 1 ? Secondary : InputCode.None;

    public InputBind With(int slot, InputCode code) => slot == 0
        ? this with { Primary = code }
        : this with { Secondary = code };

    /// <summary>Whether this binding answers to a button. Ignores empty slots, so two
    /// unbound actions are not held to be fighting over nothing.</summary>
    public bool Uses(InputCode code)
        => !code.IsNone && (Primary == code || Secondary == code);
}
