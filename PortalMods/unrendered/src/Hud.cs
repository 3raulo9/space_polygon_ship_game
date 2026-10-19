using Raylib_cs;
using Unrendered.Core;
using Unrendered.Rendering;
using GameWorld = Unrendered.World.World;

namespace UnrenderedPortal;

/// <summary>
/// The rift's few words on UNRENDERED's own HUD, in its own pixel font: the JOIN WORLDS
/// banner when a Minecraft world is open on the network, a line saying what the rift is
/// doing, and the pocket of Minecraft items.
/// </summary>
public static class Hud
{
    private static string _toast = "";
    private static double _toastAt = -10;

    public static void Toast(string text)
    {
        _toast = text.ToUpperInvariant();
        _toastAt = Raylib.GetTime();
    }

    private static readonly Color Gold = new(255, 213, 79, 255);      // glowstone
    private static readonly Color Water = new(120, 170, 255, 255);

    /// <summary>After the game's own instruments, still inside its low-res frame.</summary>
    internal static void Draw(GameWorld world)
    {
        int w = Config.InternalWidth;
        double now = Raylib.GetTime();

        // What the rift is doing, small, under the top strip.
        string? line = null;
        Color tint = Water;
        if (Realm.Active) { line = $"IN MINECRAFT — {Rift.Link?.WorldName.ToUpperInvariant()}"; tint = Gold; }
        else if (Rift.Live is { } live) line = $"RIFT OPEN TO {live.WorldName.ToUpperInvariant()}"
            + (Visitors.All.Count > 0 ? $" — {Visitors.All.Count} FROM MINECRAFT HERE" : "");
        else if (Rift.Link is { } pending) line = "REACHING " + pending.Target + "…";
        else if (!Rift.InMatch && Discovery.Best() is { } found)
        {
            // The invitation. Pulses so it is read, not missed.
            string text = $"MINECRAFT: {found.World.ToUpperInvariant()} IS OPEN";
            string join = $"PRESS {Rift.Cfg.PortalKey} TO JOIN WORLDS";
            byte a = (byte)(170 + 85 * MathF.Sin((float)now * 4f));
            int bw = Math.Max(Font.Measure(text, 1), Font.Measure(join, 1)) + 12;
            int bx = (w - bw) / 2, by = 40;
            Raylib.DrawRectangle(bx, by, bw, 24, new Color(5, 7, 10, 200));
            Raylib.DrawRectangleLines(bx, by, bw, 24, new Color(Gold.R, Gold.G, Gold.B, a));
            Font.Centered(text, w / 2, by + 4, 1, Gold);
            Font.Centered(join, w / 2, by + 14, 1, new Color(Water.R, Water.G, Water.B, a));
        }
        else if (!Rift.InMatch && Discovery.Vanilla() is { } lan)
            line = $"{lan.World.ToUpperInvariant()} IS OPEN TO LAN — NO RIFT MOD THERE";

        if (line != null)
        {
            line = Font.Clean(line);
            if (line.Length > 50) line = line[..50];
            Font.Centered(line, w / 2, 42, 1, tint);
        }

        // The last thing the rift said, large, for a few seconds.
        double age = now - _toastAt;
        if (age < 4.0 && _toast.Length > 0)
        {
            byte a = (byte)(255 * Math.Clamp(4.0 - age, 0, 1));
            string t = _toast.Length > 48 ? _toast[..48] : _toast;
            int tw = Font.Measure(t, 1);
            Raylib.DrawRectangle((w - tw) / 2 - 4, 150, tw + 8, 12, new Color(0, 0, 0, (int)(a * 0.6f)));
            Font.Centered(t, w / 2, 152, 1, new Color(Gold.R, Gold.G, Gold.B, a));
        }

        Pocket.Draw();
    }
}
