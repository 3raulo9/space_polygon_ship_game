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
    private const int RadarSize = 52;               // square side, internal px
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

        // The view rotates with the player so "up" on the radar is always where the
        // craft is pointing. World forward is (sin h, cos h), so projecting an offset
        // onto the craft's right/forward axes is a rotation by +heading — rotating by
        // -heading (as before) only lines up facing north and flips once you turn.
        float c = MathF.Cos(p.Heading);
        float s = MathF.Sin(p.Heading);
        float scale = (RadarSize * 0.5f - 2f) / RadarWorldRange;

        foreach (var e in world.Enemies)
        {
            if (!e.Alive) continue;
            // Shortest offset across the torus, so a contact just over the world's seam
            // reads as close on the radar rather than clamped to the far rim.
            Vector2 rel = Torus.Delta(p.Position, e.Position);
            // World is (X east, Y=Z north). Rotate so heading points up (-screenY).
            float rx = rel.X * c - rel.Y * s;
            float ry = rel.X * s + rel.Y * c;

            // The world is left-handed (facing east, the craft's right is +north), so
            // screen-right is the negated rotated-X — otherwise the radar mirrors.
            float px = cx - rx * scale;
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
            Vector2 rel = Torus.Delta(p.Position, pk.Position);
            float rx = rel.X * c - rel.Y * s;
            float ry = rel.X * s + rel.Y * c;

            float px = cx - rx * scale;   // negated X: match the left-handed world
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
    /// A proper upward-pointing arrow marking the (fixed, centred) craft: a triangular
    /// head over a short shaft, so "up" reads unambiguously as the heading. Drawn as
    /// horizontal spans so it can't be lost to Raylib's 2D triangle back-face culling,
    /// and stays crisp at the internal resolution.
    /// </summary>
    private static void DrawPlayerArrow(int cx, int cy)
    {
        Color col = Palette.HudChrome;
        const int headH = 5;     // arrowhead height (rows widen 1,3,5,7,9 px)
        const int shaftH = 4;    // shaft length below the head
        const int shaftHalf = 1; // shaft half-width → 3 px stem
        int top = cy - (headH + shaftH) / 2;   // the arrow's tip

        // Arrowhead: each row a little wider, from the 1px tip down to the base.
        for (int row = 0; row < headH; row++)
        {
            int w = row;                        // half-width grows toward the base
            Raylib.DrawRectangle(cx - w, top + row, w * 2 + 1, 1, col);
        }
        // Shaft: a stubby stem hanging under the head so it reads as an arrow, not a wedge.
        Raylib.DrawRectangle(cx - shaftHalf, top + headH, shaftHalf * 2 + 1, shaftH, col);
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
