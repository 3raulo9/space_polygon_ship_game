using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.Entities;

namespace VoidTanks.Rendering;

/// <summary>
/// The in-world dashboard, drawn flat over the low-res target after the 3D pass
/// so it shares the chunky pixels. A thin strip runs across the TOP of the
/// viewport: the vital bars (Shields / Ammo / Hyper) grouped on the left, the
/// tactical radar on the right. Cold chrome on the void, no warmth — an
/// instrument panel bolted to the inside of the cockpit, not a friendly HUD.
///
/// Pure drawing: it reads the world's live state and never mutates it, so the
/// sim stays testable without a screen.
/// </summary>
internal static class HudRenderer
{
    private const int W = Config.InternalWidth;   // 320
    private const int H = Config.InternalHeight;  // 240

    // The dashboard strip: a band pinned to the top edge.
    private const int StripH = 40;

    // --- Bars (left group) ---
    private const int BarTop = 6;
    private const int BarBottom = StripH - 6;
    private const int BarW = 7;
    private const int BarGap = 16;   // centre-to-centre spacing of the three bars
    private const int BarsLeft = 10; // left edge of the first (Shields) bar

    // --- Radar (right group) ---
    private const int RadarSize = 34;               // square side, internal px
    private const int RadarMargin = 6;
    private const float RadarWorldRange = 90f;       // world units mapped to the radar edge

    public static void Draw(World.World world)
    {
        PlayerTank p = world.Player;

        // A faint panel behind the strip so the bars/radar sit on a surface
        // rather than floating over the grid — but kept dark and translucent so
        // the void still bleeds through.
        Raylib.DrawRectangle(0, 0, W, StripH, new Color(5, 7, 10, 180));
        Raylib.DrawRectangle(0, StripH, W, 1, Scale(Palette.GridFar, 0.6f)); // seam line

        DrawBars(p);
        DrawRadar(world, p);
    }

    // --- Vital bars: three vertical gauges, letter-labelled S / A / H ---

    private static void DrawBars(PlayerTank p)
    {
        // Shields dip to the warning red when critically low — the one gauge whose
        // emptiness ends the run, so it earns the alarm colour.
        Color shieldColor = p.ShieldFraction <= 0.25f
            ? Lerp(Palette.Warning, Palette.HudChrome, p.ShieldFraction / 0.25f)
            : Palette.HudChrome;

        DrawBar(BarsLeft + BarGap * 0, "S", p.ShieldFraction, shieldColor);
        DrawBar(BarsLeft + BarGap * 1, "A", p.AmmoFraction, Palette.Flag);
        DrawBar(BarsLeft + BarGap * 2, "H", p.HyperFraction, Palette.GridNear);
    }

    private static void DrawBar(int x, string label, float fraction, Color fill)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        int barH = BarBottom - BarTop;
        int filled = (int)MathF.Round(barH * fraction);

        // Frame + dark well.
        Raylib.DrawRectangle(x, BarTop, BarW, barH, new Color(10, 20, 24, 220)); // well (fog-ish)
        // Fill grows from the bottom up.
        if (filled > 0)
            Raylib.DrawRectangle(x, BarBottom - filled, BarW, filled, fill);
        // Thin chrome outline so the empty portion still reads as a gauge.
        Raylib.DrawRectangleLines(x, BarTop, BarW, barH, Scale(Palette.HudChrome, 0.5f));

        // Single-letter label tucked under the bar.
        Font font = Raylib.GetFontDefault();
        const int size = 8;
        Vector2 m = Raylib.MeasureTextEx(font, label, size, 1);
        Raylib.DrawTextEx(font, label,
            new Vector2(x + (BarW - m.X) * 0.5f, StripH - size + 1), size, 1,
            Scale(Palette.HudChrome, 0.85f));
    }

    // --- Radar: player-centred overhead grid ---

    private static void DrawRadar(World.World world, PlayerTank p)
    {
        int x0 = W - RadarSize - RadarMargin;
        int y0 = RadarMargin;
        int cx = x0 + RadarSize / 2;
        int cy = y0 + RadarSize / 2;

        // Backing + border.
        Raylib.DrawRectangle(x0, y0, RadarSize, RadarSize, new Color(5, 7, 10, 210));
        Raylib.DrawRectangleLines(x0, y0, RadarSize, RadarSize, Scale(Palette.HudChrome, 0.55f));
        // Cross-hair grid lines — the overhead reference frame.
        Color grid = Scale(Palette.GridFar, 0.45f);
        Raylib.DrawLine(cx, y0 + 1, cx, y0 + RadarSize - 1, grid);
        Raylib.DrawLine(x0 + 1, cy, x0 + RadarSize - 1, cy, grid);

        // The view rotates with the player so "up" on the radar is always where
        // the craft is pointing. Rotate world offsets by -heading into screen space.
        float c = MathF.Cos(-p.Heading);
        float s = MathF.Sin(-p.Heading);
        float scale = (RadarSize * 0.5f - 2f) / RadarWorldRange;

        foreach (var e in world.Enemies)
        {
            if (!e.Alive) continue;
            Vector2 rel = e.Position - p.Position;
            // World is (X east, Y=Z north). Rotate so heading points up (-screenY).
            float rx = rel.X * c - rel.Y * s;
            float ry = rel.X * s + rel.Y * c;

            float px = cx + rx * scale;
            float py = cy - ry * scale;   // screen Y is down; forward should read up

            // Clamp blips to the rim so distant contacts still register at the edge.
            px = Math.Clamp(px, x0 + 1, x0 + RadarSize - 2);
            py = Math.Clamp(py, y0 + 1, y0 + RadarSize - 2);

            Color blip = e.IsElite ? Palette.EliteFill : Palette.EnemyFill;
            Raylib.DrawRectangle((int)px, (int)py, 2, 2, blip);
        }

        // Floating salvage shows as friendly blips so the player can steer toward a
        // resupply: charged green for batteries, flag-yellow for stray rounds.
        foreach (var pk in world.Pickups)
        {
            Vector2 rel = pk.Position - p.Position;
            float rx = rel.X * c - rel.Y * s;
            float ry = rel.X * s + rel.Y * c;

            float px = cx + rx * scale;
            float py = cy - ry * scale;
            px = Math.Clamp(px, x0 + 1, x0 + RadarSize - 2);
            py = Math.Clamp(py, y0 + 1, y0 + RadarSize - 2);

            Color blip = pk.Kind == PickupKind.Battery ? Palette.BatteryCore : Palette.Flag;
            Raylib.DrawRectangle((int)px, (int)py, 1, 1, blip);
        }

        // Player: a small chrome triangle fixed at centre, always pointing up.
        DrawPlayerArrow(cx, cy);
    }

    /// <summary>
    /// A little upward-pointing triangle marking the (fixed, centred) craft. Drawn
    /// as horizontal spans from apex to base so it can't be lost to Raylib's 2D
    /// triangle back-face culling, and stays crisp at the internal resolution.
    /// </summary>
    private static void DrawPlayerArrow(int cx, int cy)
    {
        const int half = 3;   // half-width at the base
        // Rows from tip (top) down to the base; each row a little wider.
        for (int row = 0; row <= half; row++)
        {
            int y = cy - half + row;
            int w = row;                        // widens toward the base
            Raylib.DrawRectangle(cx - w, y, w * 2 + 1, 1, Palette.HudChrome);
        }
    }

    // --- helpers (mirror MenuRenderer's) ---

    private static Color Scale(Color col, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color((int)(col.R * t), (int)(col.G * t), (int)(col.B * t), col.A);
    }

    private static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t),
            255);
    }
}
