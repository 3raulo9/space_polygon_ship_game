using Raylib_cs;

namespace VoidTanks.Core;

/// <summary>
/// Player-configurable controls, persisted to a small text file next to the game
/// so choices survive a relaunch (the "swap for initial launch" preference has to
/// stick). Deliberately narrow: a turn-direction swap, a movement scheme, and a
/// fire key. No arbitrary rebinding UI — a couple of curated schemes keep the cold
/// terminal feel and stay unbreakable.
/// </summary>
public sealed class Settings
{
    /// <summary>Which keys drive/turn the craft.</summary>
    public enum Scheme
    {
        Wasd,    // W/A/S/D drive+turn, arrows mirror them
        Arrows,  // arrow keys primary, WASD mirror
    }

    /// <summary>Which key fires.</summary>
    public enum FireKey
    {
        Ctrl,    // either Control (default)
        Space,   // Space (jump then moves to Shift — see MovesJumpToShift)
        Enter,
    }

    /// <summary>When true, TurnLeft and TurnRight inputs are exchanged.</summary>
    public bool SwapTurn { get; set; }

    public Scheme Movement { get; set; } = Scheme.Wasd;
    public FireKey Fire { get; set; } = FireKey.Ctrl;

    // Config file lives beside the executable so it's found regardless of CWD.
    private static string FilePath =>
        Path.Combine(AppContext.BaseDirectory, "controls.cfg");

    /// <summary>Loads settings from disk, falling back to launch defaults on any problem.</summary>
    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            if (!File.Exists(FilePath)) return s;

            foreach (string raw in File.ReadAllLines(FilePath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq].Trim().ToLowerInvariant();
                string val = line[(eq + 1)..].Trim();

                switch (key)
                {
                    case "swapturn":
                        s.SwapTurn = val is "1" or "true";
                        break;
                    case "movement":
                        if (Enum.TryParse(val, ignoreCase: true, out Scheme sc)) s.Movement = sc;
                        break;
                    case "fire":
                        if (Enum.TryParse(val, ignoreCase: true, out FireKey fk)) s.Fire = fk;
                        break;
                }
            }
        }
        catch
        {
            // A corrupt or unreadable file must never block launch — use defaults.
            return new Settings();
        }
        return s;
    }

    /// <summary>Writes settings to disk. Failures are swallowed — saving is best-effort.</summary>
    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath,
                "# VOID TANKS controls\n" +
                $"swapTurn={(SwapTurn ? 1 : 0)}\n" +
                $"movement={Movement}\n" +
                $"fire={Fire}\n");
        }
        catch
        {
            // Read-only disk / permissions — the session's settings still apply.
        }
    }

    // --- Resolved key sets (consulted by InputMap) ---

    public bool ForwardDown() =>
        Movement == Scheme.Wasd
            ? Down(KeyboardKey.W) || Down(KeyboardKey.Up)
            : Down(KeyboardKey.Up) || Down(KeyboardKey.W);

    public bool BackDown() =>
        Down(KeyboardKey.S) || Down(KeyboardKey.Down);

    // Left/right honour the swap: with SwapTurn on, pressing "left" turns right.
    public bool TurnLeftDown() => RawTurn(left: !SwapTurn);
    public bool TurnRightDown() => RawTurn(left: SwapTurn);

    private static bool RawTurn(bool left) =>
        left ? Down(KeyboardKey.A) || Down(KeyboardKey.Left)
             : Down(KeyboardKey.D) || Down(KeyboardKey.Right);

    public bool FireDown() => Fire switch
    {
        FireKey.Space => Down(KeyboardKey.Space),
        FireKey.Enter => Down(KeyboardKey.Enter),
        _ => Down(KeyboardKey.LeftControl) || Down(KeyboardKey.RightControl),
    } || Raylib.IsMouseButtonDown(MouseButton.Left);

    // Heavy grenade (the pad's "B"): a distinct button, held is fine — the tank's
    // own longer cooldown paces it. Right mouse mirrors it for mouse-only play.
    public bool GrenadeDown() =>
        Down(KeyboardKey.G) || Raylib.IsMouseButtonDown(MouseButton.Right);

    // Hyperspace warp (the pad's "X"): a single deliberate press, not a hold —
    // you commit to the gamble once, you don't chain-warp.
    public bool HyperspacePressed() => Pressed(KeyboardKey.X);

    // Jump is Space unless Space is the fire key, in which case it moves to Shift
    // so the two never collide.
    public bool JumpPressed() =>
        Fire == FireKey.Space
            ? Pressed(KeyboardKey.LeftShift) || Pressed(KeyboardKey.RightShift)
            : Pressed(KeyboardKey.Space);

    private static bool Down(KeyboardKey k) => Raylib.IsKeyDown(k);
    private static bool Pressed(KeyboardKey k) => Raylib.IsKeyPressed(k);

    // --- Human-readable labels for the settings screen ---
    public string SwapLabel => SwapTurn ? "ON" : "OFF";
    public string MovementLabel => Movement == Scheme.Wasd ? "WASD" : "ARROWS";
    public string FireLabel => Fire switch
    {
        FireKey.Space => "SPACE",
        FireKey.Enter => "ENTER",
        _ => "CTRL",
    };
}
