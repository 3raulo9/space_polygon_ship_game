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

    public static void Draw(World.World world, ItemIconRenderer icons)
    {
        _icons = icons;
        PlayerTank p = world.Player;

        // A faint panel behind the strip so the bars/radar sit on a surface
        // rather than floating over the grid — but kept dark and translucent so
        // the void still bleeds through.
        Raylib.DrawRectangle(0, 0, W, StripH, new Color(5, 7, 10, 180));
        Raylib.DrawRectangle(0, StripH, W, 1, Scale(Palette.GridFar, 0.6f)); // seam line

        DrawBars(p);
        DrawWeaponSlots(world.Inventory);
        DrawRadar(world, p);
        // The firing sight sits dead centre, where the mouse aims the gun, and only on the
        // two machines: the SOLDIER and the FISH draw their own centre reticles (which
        // bloom into brackets and read the water), so a second one here would double up.
        if (p.IsMachine) DrawCrosshair(p);
        if (p.Spider is { } spider) DrawChargeMeter(spider, p);

        // The SOLDIER keeps every one of the above — the same vitals, the same equip
        // row and the same radar, because it is the same run and the same craft's worth
        // of information — and then adds the handful of things that only exist on a
        // chassis hanging off two cables, its own crosshair among them. See SoldierHud.
        if (p.Soldier is { } rig) SoldierHud.DrawOverlay(world, rig, p);

        // Same arrangement for the FISH, whose additions are all about the one axis this
        // dashboard has never had an instrument for: the radar says where things are on
        // the plane, and this chassis lives in the column. See FishHud.
        if (p.Fish is { } body) FishHud.DrawOverlay(world, body, p);
    }

    // --- The SPIDER's lance meter: 0..100 down the right-hand edge ---
    // Deliberately not in the top strip with the vitals. The charge is the one gauge
    // the player has to watch *while standing still and exposed*, so it wants to be
    // tall, off to the side and unmissable, rather than another 7px stub in a row of
    // three. It sits under the radar and runs most of the way down the frame.

    private const int ChargeX = W - 12;
    private const int ChargeW = 8;
    private const int ChargeTop = StripH + 24;
    private const int ChargeBottom = H - 40;

    private static void DrawChargeMeter(SpiderWeapon spider, PlayerTank p)
    {
        int barH = ChargeBottom - ChargeTop;
        float f = spider.ChargeFraction;
        int filled = (int)MathF.Round(barH * f);

        Raylib.DrawRectangle(ChargeX, ChargeTop, ChargeW, barH, new Color(10, 20, 24, 220));

        if (filled > 0)
        {
            // The fill rides from the core's resting magenta to a blown-out white as it
            // tops out, so a full meter reads as "this is about to come out of you"
            // rather than as a bar that has merely reached its end.
            Color hot = Lerp(Palette.NeonMagenta, Color.White, f * f);
            Raylib.DrawRectangle(ChargeX, ChargeBottom - filled, ChargeW, filled, hot);
        }

        // The minimum a release needs to actually fire — below this the trigger fizzles,
        // so the line is worth drawing rather than leaving the player to discover it.
        int minY = ChargeBottom - (int)(barH * (SpiderWeapon.MinCharge / SpiderWeapon.MaxCharge));
        Raylib.DrawRectangle(ChargeX, minY, ChargeW, 1, Palette.Warning);

        Raylib.DrawRectangleLines(ChargeX, ChargeTop, ChargeW, barH, Scale(Palette.HudChrome, 0.5f));

        // The number, so "0 to 100" is literally what the player sees, and under it the
        // rounds the shot would cost — the meter is spending two things at once.
        int charge = (int)MathF.Round(spider.Charge);
        PixelFont.DrawCentered(charge.ToString(), ChargeX + ChargeW / 2, ChargeTop - 9, 1,
            spider.Charging ? Palette.HudChrome : Scale(Palette.HudChrome, 0.6f));

        if (!spider.Charging) return;

        int cost = spider.AmmoCost;
        bool afford = p.Ammo >= cost;
        PixelFont.DrawCentered("-" + cost, ChargeX + ChargeW / 2, ChargeBottom + 4, 1,
            afford ? Palette.Flag : Palette.Warning);
    }

    // --- Equip slots (R T Y U): the crafted CRAB CORE lives here ---
    // A small row of four boxes in the strip's free centre band, between the vital bars
    // on the left and the radar on the right. Pressing the matching key throws the slot's
    // contents (see InputMap.WeaponSlotPressed / World.UseWeaponSlot).
    private const int WSlot = 16;      // box side
    private const int WGap = 6;
    private const int WTop = 3;

    // The frame's live 3D item icons, handed in by the Renderer's world pass.
    private static ItemIconRenderer? _icons;

    private static void DrawWeaponSlots(Inventory inv)
    {
        int total = Inventory.WeaponCount * WSlot + (Inventory.WeaponCount - 1) * WGap;
        int x0 = (W - total) / 2;
        string letters = "RTYU";

        for (int i = 0; i < Inventory.WeaponCount; i++)
        {
            int x = x0 + i * (WSlot + WGap);
            Raylib.DrawRectangle(x, WTop, WSlot, WSlot, new Color(10, 20, 24, 220));
            Raylib.DrawRectangleLines(x, WTop, WSlot, WSlot, Scale(Palette.HudChrome, 0.5f));

            // The equipped item as its rotating 3D icon — the same model the inventory
            // panel shows, blitted from its render texture (flipped, as it's bottom-up).
            if (!inv.Weapons[i].IsEmpty && _icons is not null)
            {
                var src = new Rectangle(0, 0, ItemIconRenderer.Size, -ItemIconRenderer.Size);
                var dst = new Rectangle(x + 2, WTop + 2, WSlot - 4, WSlot - 4);
                Raylib.DrawTexturePro(_icons.Texture(inv.Weapons[i].Kind), src, dst,
                    Vector2.Zero, 0f, Color.White);
            }

            // Key letter tucked under the box, in the panel's crisp pixel font.
            PixelFont.DrawCentered(letters[i].ToString(), x + WSlot / 2, WTop + WSlot + 1, 1,
                Scale(Palette.HudChrome, 0.9f));
        }
    }

    // --- Crosshair: the firing sight, dead centre ---

    // Four short ticks around an open centre, with a single lit pixel in the middle —
    // small and precise, sitting exactly where the mouse points the gun so the player
    // aims at the thing rather than at a scope low on the dashboard. The camera looks
    // straight down the gun line on these two chassis (the standing tilt is dropped for
    // them), so screen centre really is where the round leaves.
    private const int AimGap = 3;    // half-gap of clear space around the centre point
    private const int AimTick = 5;   // length of each tick beyond the gap

    private static void DrawCrosshair(PlayerTank p)
    {
        int cx = W / 2, cy = H / 2;

        // Rounds run dry — dim the sight so it reads as "can't fire" rather than lying
        // about a shot that won't happen.
        Color line = p.Ammo > 0 ? new Color(235, 245, 255, 210)
                                 : new Color(235, 245, 255, 70);

        // Four ticks: left, right, up, down, each held off the exact centre by the gap.
        Raylib.DrawLine(cx - AimGap - AimTick, cy, cx - AimGap, cy, line);
        Raylib.DrawLine(cx + AimGap, cy, cx + AimGap + AimTick, cy, line);
        Raylib.DrawLine(cx, cy - AimGap - AimTick, cx, cy - AimGap, line);
        Raylib.DrawLine(cx, cy + AimGap, cx, cy + AimGap + AimTick, line);

        // A single centre pixel — the actual point of aim.
        Raylib.DrawRectangle(cx, cy, 1, 1, line);
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

        // The view rotates with the player so "up" on the radar is always where the craft
        // is pointing — which the mouse now aims. World forward is (sin h, cos h), so
        // projecting an offset onto the craft's right/forward axes is a rotation by
        // +heading — rotating by -heading only lines up facing north.
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
