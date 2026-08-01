using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Net;
using Unrendered.UI;

namespace Unrendered.Rendering;

/// <summary>
/// The multiplayer front door, drawn in the same cold chrome as every other screen. Four
/// faces of one panel: pick a door, hold a code open, type a code, wait on a dial.
/// </summary>
internal static class LobbyRenderer
{
    private const int W = Config.InternalWidth;
    private const int H = Config.InternalHeight;
    private const int Size = 14;
    private const int Spacing = 1;

    public static void Draw(LobbyScreen s, float elapsed)
    {
        Font font = Raylib.GetFontDefault();
        Centered(font, "MULTIPLAYER", 30, 20, Palette.HudChrome);

        switch (s.Where)
        {
            case LobbyScreen.Phase.Choosing: DrawChoosing(font, s); break;
            case LobbyScreen.Phase.Hosting: DrawHosting(font, s, elapsed); break;
            case LobbyScreen.Phase.Entering: DrawEntering(font, s, elapsed); break;
            case LobbyScreen.Phase.Joining: DrawJoining(font, s, elapsed); break;
        }

        if (s.Trouble is { } bad)
            Centered(font, bad, Size, H - 46, Palette.Warning);
    }

    private static void DrawChoosing(Font font, LobbyScreen s)
    {
        Centered(font, "HOST A MATCH", Size, 96, s.Choice == 0 ? Palette.Flag : Dim());
        Centered(font, "JOIN A MATCH", Size, 122, s.Choice == 1 ? Palette.Flag : Dim());

        // Steam's state, stated plainly. A player whose client is shut has to be told that
        // rather than left pressing a button that does nothing.
        Centered(font, SteamNet.Available ? "STEAM CONNECTED" : SteamNet.Trouble ?? "STEAM OFFLINE",
            10, 160, SteamNet.Available ? Palette.GridNear : Palette.Warning);

        Centered(font, "UP DN MOVE  ENTER SELECT  ESC BACK", 10, H - 26, Dim());
    }

    private static void DrawHosting(Font font, LobbyScreen s, float elapsed)
    {
        // The code, big and spaced out — this is a number someone is reading down a phone.
        Centered(font, "YOUR CODE", 10, 62, Dim());
        Centered(font, Space(s.Code), 26, 76, Palette.Flag);

        Centered(font, $"{s.Seated} / {s.Match.MaxPlayers} IN THE ROOM", 10, 110,
            Palette.GridNear);

        int y = 122;
        Row(font, s, LobbyScreen.Row.Map, "MAP",
            s.Match.Map == GameMap.Flat ? "FLAT" : "PLANET", y);
        Row(font, s, LobbyScreen.Row.Seats, "SEATS", s.Match.MaxPlayers.ToString(), y + 16);
        Row(font, s, LobbyScreen.Row.FriendlyFire, "FRIENDLY FIRE",
            s.Match.FriendlyFire ? "ON" : "OFF", y + 32);
        Row(font, s, LobbyScreen.Row.Revives, "REVIVES", s.Match.Revives.ToString(), y + 48);

        bool go = s.Selected == LobbyScreen.Row.Launch;
        // Only pulses once someone else is actually in, so a host staring at an empty room
        // is not being invited to start a one-player multiplayer match.
        float beat = s.Seated > 1 ? 0.7f + 0.3f * MathF.Abs(MathF.Sin(elapsed * 3f)) : 0.45f;
        Centered(font, "LAUNCH", Size, y + 74,
            go ? Scale(Palette.Flag, beat) : Dim());

        Centered(font, "< > CHANGE  ENTER LAUNCH  ESC BACK", 10, H - 26, Dim());
    }

    private static void DrawEntering(Font font, LobbyScreen s, float elapsed)
    {
        Centered(font, "ENTER THE HOST'S CODE", 10, 82, Dim());

        // Seven slots, so the length of the thing being typed is obvious before it is full.
        string shown = s.Typed.PadRight(7, '.');
        bool caret = s.Typed.Length < 7 && MathF.Sin(elapsed * 6f) > 0f;
        Centered(font, Space(shown), 26, 100, caret ? Palette.Flag : Palette.HudChrome);

        Centered(font, s.Typed.Length == 7 ? "ENTER TO CONNECT" : "TYPE SEVEN CHARACTERS",
            10, 140, s.Typed.Length == 7 ? Palette.GridNear : Dim());
        Centered(font, "BKSP DELETE  ESC BACK", 10, H - 26, Dim());
    }

    private static void DrawJoining(Font font, LobbyScreen s, float elapsed)
    {
        Centered(font, "CONNECTING", 20, 96, Palette.HudChrome);
        Centered(font, Space(s.Typed), 14, 124, Dim());

        // Three dots walking, so a dial that is taking its time still looks alive.
        int dots = (int)(elapsed * 2f) % 4;
        Centered(font, new string('.', dots), 20, 148, Palette.Flag);
        Centered(font, "ESC CANCEL", 10, H - 26, Dim());
    }

    private static void Row(Font font, LobbyScreen s, LobbyScreen.Row row,
        string label, string value, int y)
    {
        bool on = s.Selected == row;
        Color c = on ? Palette.Flag : Palette.HudChrome;
        Raylib.DrawTextEx(font, label, new Vector2(70f, y), 12, Spacing, on ? c : Dim());
        Raylib.DrawTextEx(font, on ? $"< {value} >" : value, new Vector2(200f, y), 12, Spacing, c);
    }

    /// <summary>Spreads a code out so it can be read a character at a time.</summary>
    private static string Space(string s) => string.Join(' ', s.ToCharArray());

    private static void Centered(Font font, string text, int size, int y, Color c)
    {
        Vector2 m = Raylib.MeasureTextEx(font, text, size, Spacing);
        Raylib.DrawTextEx(font, text, new Vector2((W - m.X) * 0.5f, y), size, Spacing, c);
    }

    private static Color Dim() => Scale(Palette.HudChrome, 0.45f);

    private static Color Scale(Color c, float k) => new(
        (byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k), c.A);
}
