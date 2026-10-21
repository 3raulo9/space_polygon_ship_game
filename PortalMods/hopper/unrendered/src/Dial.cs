using Raylib_cs;
using Unrendered.Core;
using Unrendered.Modding;
using GameWorld = Unrendered.World.World;

namespace DimensionHopper;

/// <summary>
/// The gun's dial: every world it can reach, UNRENDERED's own first, then other games' realms.
/// I and K move, a number picks directly, J chooses, H puts it away. A realm that belongs to
/// another game has to answer before the gun will open onto it: choosing one that has not is
/// the moment to establish a clear connection with it.
/// </summary>
public static class Dial
{
    public static bool Open { get; private set; }
    private static int _sel;
    private static string _said = "";
    private static List<Realms.Entry> _list = new();

    public static void Show(GameWorld w)
    {
        Open = true;
        _list = Realms.All(w);
        int at = _list.FindIndex(e => e.Id == Hopper.Destination);
        _sel = at >= 0 ? at : Math.Max(0, _list.FindIndex(e => !e.Here));
        _said = "";
        CueBank.Play(Cue.Pickup, w.Player.Position, 0f);
    }

    public static void Close() => Open = false;

    public static Realms.Entry? Selected => _sel >= 0 && _sel < _list.Count ? _list[_sel] : null;

    public static void Move(int by) { if (_list.Count > 0) _sel = (_sel + by + _list.Count) % _list.Count; _said = ""; }

    public static void Select(int i) { if (i >= 0 && i < _list.Count) { _sel = i; _said = ""; } }

    internal static void Keys(GameWorld w)
    {
        _list = Realms.All(w);
        if (Raylib.IsKeyPressed(KeyboardKey.I)) Move(-1);
        if (Raylib.IsKeyPressed(KeyboardKey.K)) Move(1);
        for (int n = 1; n <= 9; n++)
            if (Raylib.IsKeyPressed(KeyboardKey.One + n - 1)) Select(n - 1);
        if (Raylib.IsKeyPressed(KeyboardKey.J)) Choose(w);
    }

    /// <summary>J on the dial: set the gun to it, or reach for it if it has not answered yet.</summary>
    public static void Choose(GameWorld w)
    {
        if (Selected is not { } e) return;
        if (e.Here) { _said = "YOU ARE ALREADY HERE"; return; }
        if (!e.Clear)
        {
            _said = Realms.Connect(e.Id);
            Hopper.Log($"reaching for {e.Name}: {_said}");
            return;
        }
        Hopper.Destination = e.Id;
        Hopper.Notice($"HOPPER SET: {e.Name}");
        Close();
    }

    // --- Drawing ---------------------------------------------------------------------------

    private static readonly Color Panel = new(4, 14, 8, 220);
    private static readonly Color Edge = new(70, 200, 100, 255);
    private static readonly Color Dim = new(90, 130, 100, 255);

    internal static void Draw(GameWorld w)
    {
        if (!Open)
        {
            if (Hopper.Raised && Hopper.Destination != null)
                Text($"HOPPER: {Hopper.DestinationName(w)}", 4, Config.InternalHeight - 74, Dim);
            return;
        }
        _list = Realms.All(w);
        const int x = 34, y = 22, wd = 252, ht = 196;
        Raylib.DrawRectangle(x, y, wd, ht, Panel);
        Raylib.DrawRectangleLines(x, y, wd, ht, Edge);
        Raylib.DrawRectangleLines(x + 2, y + 2, wd - 4, ht - 4, new Color(30, 90, 45, 255));
        ModText.DrawCentered("DIMENSION HOPPER", x + wd / 2, y + 6, Gun.Glow, 1);

        int row = y + 22;
        string? game = null;
        for (int i = 0; i < _list.Count; i++)
        {
            var e = _list[i];
            if (e.Game != game)
            {
                game = e.Game;
                Text(game == "UNRENDERED" ? "UNRENDERED - THIS GAME" : "OTHER REALMS - " + game, x + 8, row, Dim);
                row += 10;
            }
            bool sel = i == _sel;
            if (sel) Raylib.DrawRectangle(x + 6, row - 1, wd - 12, 10, new Color(30, 110, 50, 200));
            string mark = e.Id == Hopper.Destination ? "*" : " ";
            Text($"{i + 1} {mark}{e.Name}", x + 10, row, sel ? Color.White : (e.Clear ? Edge : Dim));
            string tag = e.Here ? "HERE" : e.Clear ? "CLEAR" : "NO LINK";
            Text(tag, x + wd - 10 - ModText.Measure(Clean(tag)), row, e.Here ? Dim : e.Clear ? Gun.Glow : new Color(220, 150, 60, 255));
            row += 11;
        }
        if (!Realms.Bridged)
        {
            Text("OTHER REALMS", x + 8, row, Dim);
            Text("NO OTHER REALMS IN REACH", x + 10, row + 10, Dim);
        }

        // The selected realm, in full.
        int by = y + ht - 52;
        Raylib.DrawLine(x + 6, by - 3, x + wd - 6, by - 3, new Color(30, 90, 45, 255));
        if (Selected is { } s)
        {
            if (!s.Clear)
            {
                ModText.DrawCentered(Clean("ESTABLISH A CLEAR CONNECTION WITH THIS REALM"), x + wd / 2, by, new Color(255, 200, 80, 255), 1);
                Centered(s.Status, x + wd / 2, by + 11, Dim);
                Centered("J: ESTABLISH CONNECTION", x + wd / 2, by + 22, Edge);
            }
            else
            {
                Centered(s.Status, x + wd / 2, by, Dim);
                Centered(s.Here ? "YOU ARE HERE" : "J: SET THE GUN TO " + s.Name, x + wd / 2, by + 11, Edge);
            }
            if (_said.Length > 0) Centered(_said, x + wd / 2, by + 33, Color.White);
        }
        Centered("I/K OR 1-9 MOVE   J CHOOSE   H CLOSE", x + wd / 2, y + ht - 10, Dim);
    }

    private const string Known = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-/:+.<>!,?{} *";

    public static string Clean(string s)
    {
        var b = new System.Text.StringBuilder(s.Length);
        foreach (char ch in s.ToUpperInvariant().Replace("—", "-").Replace("…", "...").Replace('(', '{').Replace(')', '}')
                     .Replace('_', '-').Replace('\'', ' '))
            if (Known.IndexOf(ch) >= 0) b.Append(ch);
        string r = b.ToString();
        return r.Length > 46 ? r[..46] : r;
    }

    private static void Text(string s, int x, int y, Color c) => ModText.Draw(Clean(s), x, y, c, 1);

    private static void Centered(string s, int cx, int y, Color c) => ModText.DrawCentered(Clean(s), cx, y, c, 1);
}
