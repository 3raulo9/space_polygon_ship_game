using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.Entities;
using VoidTanks.UI;

namespace VoidTanks.Rendering;

/// <summary>
/// The flat 2D overlay for the hidden test screen: a heading, the roster listed
/// down the left with the shown specimen bracketed, and a small stat readout for
/// it on the right. Drawn in the same 320x240 internal space and cold chrome as
/// the menu so it feels like another panel of the same dead terminal. The turning
/// specimen itself is drawn in 3D by the Renderer before this runs.
/// </summary>
internal static class TestRenderer
{
    private const int W = Config.InternalWidth;   // 320
    private const int H = Config.InternalHeight;  // 240
    private const int Spacing = 1;

    public static void Draw(TestScreen screen, float elapsed)
    {
        Font font = Raylib.GetFontDefault();

        DrawHeading(font, "ENEMY TEST", elapsed);
        DrawRoster(font, screen);
        DrawStats(font, screen.Current);

        if (screen.ShowingBoss)
        {
            DrawPhasePicker(font, screen, elapsed);
            DrawFooter(font, "< > CYCLE · 1-4 ANIM · ESC BACK");
        }
        else
        {
            DrawFooter(font, "< > CYCLE · ESC BACK");
        }
    }

    // --- Animation phase picker for the boss rig (bottom-left of the panel) ---
    private static readonly string[] PhaseNames =
        { "0 IDLE", "1 THREAT DISPLAY", "2 CLAMPING", "3 PURSUIT" };

    private static void DrawPhasePicker(Font font, TestScreen screen, float elapsed)
    {
        const int size = 8;
        const int x = 10;
        int y = 150;

        Raylib.DrawTextEx(font, "ANIMATION", new Vector2(x, y), size, Spacing,
            Scale(Palette.HudChrome, 0.7f));
        y += size + 4;

        int cur = (int)screen.CrabPhase;
        for (int i = 0; i < PhaseNames.Length; i++)
        {
            bool on = i == cur;
            // The live phase gets a bright, faintly pulsing row; the rest sit dim.
            float pulse = on ? 0.8f + 0.2f * MathF.Abs(MathF.Sin(elapsed * 3f)) : 0.35f;
            Color c = Scale(Palette.HudChrome, pulse);
            if (on)
                Raylib.DrawTextEx(font, ">", new Vector2(x - 6, y), size, Spacing, c);
            Raylib.DrawTextEx(font, PhaseNames[i], new Vector2(x, y), size, Spacing, c);
            y += 11;
        }
    }

    // --- Heading (a steadier echo of the title flicker) ---
    private static void DrawHeading(Font font, string text, float elapsed)
    {
        const int size = 20;
        Vector2 m = Raylib.MeasureTextEx(font, text, size, Spacing);
        float x = (W - m.X) * 0.5f;
        const int y = 14;

        float breathe = 0.78f + 0.22f * MathF.Abs(MathF.Sin(elapsed * 0.9f));
        Raylib.DrawTextEx(font, text, new Vector2(x, y), size, Spacing, Scale(Palette.HudChrome, breathe));

        int ruleW = (int)m.X;
        Color rule = Scale(Palette.GridFar, 0.5f + 0.2f * MathF.Sin(elapsed * 1.7f));
        Raylib.DrawRectangle((W - ruleW) / 2, y + size + 4, ruleW, 1, rule);
    }

    // --- Roster list down the left edge ---
    private static void DrawRoster(Font font, TestScreen screen)
    {
        const int size = 8;
        const int x = 10;
        int y = 64;
        const int step = 13;

        var all = EnemyCatalog.All;
        for (int i = 0; i < all.Count; i++)
        {
            bool selected = i == screen.Selected;
            string label = all[i].Name;
            Color c = selected ? Palette.HudChrome : Scale(Palette.HudChrome, 0.4f);

            if (selected)
                Raylib.DrawTextEx(font, ">", new Vector2(x - 6, y), size, Spacing, Palette.HudChrome);
            Raylib.DrawTextEx(font, label, new Vector2(x, y), size, Spacing, c);
            y += step;
        }
    }

    // --- Stat readout for the shown specimen, right column ---
    private static void DrawStats(Font font, EnemyArchetype a)
    {
        const int right = 310;   // right edge of the panel
        int y = 60;

        // Name, big and bright.
        const int nameSize = 14;
        Vector2 ns = Raylib.MeasureTextEx(font, a.Name, nameSize, Spacing);
        Raylib.DrawTextEx(font, a.Name, new Vector2(right - ns.X, y), nameSize, Spacing, Palette.HudChrome);
        y += nameSize + 2;

        // Class, small and dim under the name.
        const int classSize = 8;
        Vector2 cs = Raylib.MeasureTextEx(font, a.Class, classSize, Spacing);
        Raylib.DrawTextEx(font, a.Class, new Vector2(right - cs.X, y), classSize, Spacing,
            Scale(Palette.HudChrome, 0.55f));
        y += classSize + 12;

        // Stat bars, scaled against the roster's worst-case so they read relative.
        float maxHealth = 0f, maxDamage = 0f;
        foreach (var e in EnemyCatalog.All)
        {
            if (e.Health > maxHealth) maxHealth = e.Health;
            if (e.Damage > maxDamage) maxDamage = e.Damage;
        }

        DrawStatBar(font, right, y, "HEALTH", a.Health, maxHealth, Palette.GridNear);
        y += 22;
        DrawStatBar(font, right, y, "DAMAGE", a.Damage, maxDamage, Palette.EnemyFill);
    }

    /// <summary>A right-aligned labelled bar: LABEL over a filled meter + the value.</summary>
    private static void DrawStatBar(Font font, int right, int y, string label,
        float value, float max, Color fill)
    {
        const int size = 8;
        const int barW = 110;
        const int barH = 5;
        int left = right - barW;

        // Label and numeric value on the same line above the bar.
        Raylib.DrawTextEx(font, label, new Vector2(left, y), size, Spacing,
            Scale(Palette.HudChrome, 0.7f));
        string val = value % 1f == 0f ? ((int)value).ToString() : value.ToString("0.0");
        Vector2 vs = Raylib.MeasureTextEx(font, val, size, Spacing);
        Raylib.DrawTextEx(font, val, new Vector2(right - vs.X, y), size, Spacing, Palette.HudChrome);

        // Track, then fill.
        int barY = y + size + 2;
        Raylib.DrawRectangle(left, barY, barW, barH, Scale(Palette.HudChrome, 0.15f));
        float t = max > 0f ? Math.Clamp(value / max, 0f, 1f) : 0f;
        Raylib.DrawRectangle(left, barY, (int)(barW * t), barH, fill);
    }

    private static void DrawFooter(Font font, string hint)
    {
        const int size = 8;
        Vector2 s = Raylib.MeasureTextEx(font, hint, size, Spacing);
        Raylib.DrawTextEx(font, hint, new Vector2((W - s.X) * 0.5f, H - 16), size, Spacing,
            Scale(Palette.HudChrome, 0.35f));
    }

    private static Color Scale(Color c, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color((int)(c.R * t), (int)(c.G * t), (int)(c.B * t), c.A);
    }
}
