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

    /// <summary>
    /// The join/quit feed: a few short yellow lines stacked at the bottom-right, under the
    /// instruments, each fading out as it ages. Multiplayer only — single player never has a
    /// feed to draw. Drawn last, over everything, so a notice is never lost behind the world.
    /// </summary>
    public static void DrawNotices(Net.NoticeFeed feed)
    {
        Font font = Raylib.GetFontDefault();
        const int size = 9;
        const int pad = 6;
        var lines = feed.Entries;

        // Newest at the bottom, older ones stacked above it and climbing off the corner.
        for (int i = 0; i < lines.Count; i++)
        {
            Net.NoticeFeed.Entry e = lines[lines.Count - 1 - i];
            // Fade over the last second and a half of a line's life.
            float a = Math.Clamp(e.Remaining / 1.5f, 0f, 1f);
            byte alpha = (byte)(a * 255);
            Vector2 m = Raylib.MeasureTextEx(font, e.Text, size, 1);
            float x = W - pad - m.X;
            float y = H - pad - size - i * (size + 3);
            var col = new Color(Palette.Flag.R, Palette.Flag.G, Palette.Flag.B, alpha);
            Raylib.DrawTextEx(font, e.Text, new Vector2(x, y), size, 1, col);
        }
    }

    public static void Draw(World.World world, ItemIconRenderer icons)
    {
        _icons = icons;
        // The craft the instruments describe. Normally this machine's own; once its revives
        // are spent, the team-mate the camera has moved to — a spent player's own bars are a
        // row of zeroes and tell them nothing about the fight they are now watching.
        PlayerTank p = world.Eye;

        // A faint panel behind the strip so the bars/radar sit on a surface
        // rather than floating over the grid — but kept dark and translucent so
        // the void still bleeds through.
        Raylib.DrawRectangle(0, 0, W, StripH, new Color(5, 7, 10, 180));
        Raylib.DrawRectangle(0, StripH, W, 1, Scale(Palette.GridFar, 0.6f)); // seam line

        DrawBars(p);
        DrawWeaponSlots(world.InventoryOf(world.ViewSeat));
        if (world.Spectating) DrawSpectating(world);
        DrawRadar(world, p);
        // The firing sight sits dead centre, where the mouse aims the gun, and only on the
        // two machines: the SOLDIER and the FISH draw their own centre reticles (which
        // bloom into brackets and read the water), so a second one here would double up.
        if (p.IsMachine) DrawCrosshair(p);
        if (p.Spider is { } spider) DrawChargeMeter(spider, p);
        if (p.Claw is { } claw) DrawClawStatus(claw);
        // The TANK's siege readouts: the three timed/stateful pieces of its kit the vital
        // bars don't already cover. Only on that chassis — nothing else plants, smokes or slugs.
        if (p.Class == PlayerClass.Tank) DrawTankStatus(p);

        // The SOLDIER keeps every one of the above — the same vitals, the same equip
        // row and the same radar, because it is the same run and the same craft's worth
        // of information — and then adds the handful of things that only exist on a
        // chassis hanging off two cables, its own crosshair among them. See SoldierHud.
        // The cable kit's own instruments, for whoever is holding it: the hook indicators
        // are how a player reads which lines they still have, and a VIRUS wearing a stolen
        // body needs that as much as the chassis that was issued one.
        if (p.Rig is { } rig) SoldierHud.DrawOverlay(world, rig, p);

        // Same arrangement for the FISH, whose additions are all about the one axis this
        // dashboard has never had an instrument for: the radar says where things are on
        // the plane, and this chassis lives in the column. See FishHud.
        if (p.Fish is { } body) FishHud.DrawOverlay(world, body, p);

        // And the VIRUS, whose additions are the two things no other chassis has to say:
        // how much of the worn host is left before it bursts, and — the state shout — whether
        // there is a host at all. It also draws its own crosshair, since it is not a machine
        // and the dashboard's centre sight above is skipped for it. See VirusHud.
        if (p.Virus is { } virus) VirusHud.DrawOverlay(world, virus, p);
    }

    /// <summary>
    /// Says whose eyes these are. A player out of revives is no longer in the match but is
    /// still in the room, and the one thing they must not be left to wonder is why the craft
    /// on screen is not answering their keys. Named, so it also tells them who is left.
    /// </summary>
    private static void DrawSpectating(World.World world)
    {
        string who = world.NameOf(world.ViewSeat);
        if (who.Length == 0) who = $"SEAT {world.ViewSeat}";
        PixelFont.DrawCentered("SPECTATING", W / 2, StripH + 6, 1, Palette.Warning);
        PixelFont.DrawCentered(who, W / 2, StripH + 14, 1, Palette.HudChrome);
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

        // Broken: a round found the core mid-wind and took the charge with it. The gauge
        // says so plainly for the beat the emitter is dead, because the player is about to
        // pull a trigger that isn't going to do anything and should know why.
        if (spider.Broken)
        {
            int dead = (int)MathF.Round(barH * spider.BreakFraction);
            Raylib.DrawRectangle(ChargeX, ChargeBottom - dead, ChargeW, dead,
                Scale(Palette.Warning, 0.55f));
            Raylib.DrawRectangleLines(ChargeX, ChargeTop, ChargeW, barH, Palette.Warning);
            PixelFont.DrawCentered("BRK", ChargeX + ChargeW / 2, ChargeTop - 9, 1, Palette.Warning);
            return;
        }

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

    /// <summary>
    /// What is in the claw, on the opposite edge from the meter — the craft's two front
    /// limbs, one instrument each side of the frame.
    ///
    /// It reads out the one number that decides how the next few seconds go: how much of
    /// the held body is left. That figure is being spent by two things at once — the
    /// squeeze draining it, and every round the hostage eats on the player's behalf — and
    /// when it runs out the SPIDER's exposed core is uncovered again, which the player
    /// would very much rather find out about a second early than a second late.
    /// </summary>
    private static void DrawClawStatus(Entities.SpiderClaw claw)
    {
        if (claw.Victim is not { } body) return;

        const int x = 12;
        int y = ChargeBottom;

        PixelFont.Draw("HELD", x, y - 9, 1, Palette.HudChrome);

        // A short integrity stub for the catch. Rides warning-red as it comes apart, so a
        // shield about to fail is visibly about to fail.
        const int w = 26, h = 3;
        float f = Math.Clamp(body.Shield / (body.IsElite ? 5f : 3f), 0f, 1f);
        Raylib.DrawRectangle(x, y, w, h, new Color(10, 20, 24, 220));
        Raylib.DrawRectangle(x, y, (int)MathF.Round(w * f), h,
            f > 0.4f ? Palette.EnemyFill : Palette.Warning);
        Raylib.DrawRectangleLines(x, y, w, h, Scale(Palette.HudChrome, 0.5f));

        if (claw.Soaked > 0)
            PixelFont.Draw("+" + claw.Soaked, x + w + 3, y - 1, 1, Palette.Flag);
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

    // --- The TANK's siege readouts ---
    // A tight stack in the bottom-left corner, out of the way of the top strip and the centre
    // sight. Three lines: whether the plant is down, whether the dischargers have cooled (with a
    // sliver that fills as they do), and whether the magazine can pay for an AP slug. Each is lit
    // when its move is live and dimmed to a ghost when it isn't, so the row reads at a glance.
    private static void DrawTankStatus(PlayerTank p)
    {
        int x = 6;
        int y = H - 34;

        // SIEGE — boxed and lit teal while dug in, a dim ghost when rolling.
        bool planted = p.Planted;
        if (planted) Raylib.DrawRectangle(x - 2, y - 2, 34, 9, new Color(10, 30, 34, 210));
        PixelFont.Draw("SIEGE", x, y, 1, planted ? Palette.GridNear : Scale(Palette.HudChrome, 0.4f));

        // SMK — lit when ready, with a thin bar refilling while the dischargers cool.
        y += 11;
        bool smk = p.SmokeReady;
        PixelFont.Draw("SMK", x, y, 1, smk ? Palette.HudChrome : Scale(Palette.HudChrome, 0.4f));
        int barX = x + 18, barW = 14;
        Raylib.DrawRectangle(barX, y + 1, barW, 3, new Color(10, 20, 24, 220));
        int fill = (int)MathF.Round(barW * p.SmokeCooldownFraction);
        if (fill > 0)
            Raylib.DrawRectangle(barX, y + 1, fill, 3, smk ? Palette.GridNear : Scale(Palette.HudChrome, 0.7f));

        // AP — lit flag-yellow when the magazine can cover the slug's cost.
        y += 11;
        bool ap = p.Ammo >= PlayerTank.SlugAmmoCost;
        PixelFont.Draw("AP", x, y, 1, ap ? Palette.Flag : Scale(Palette.HudChrome, 0.4f));
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

        // The soldier squads. Drawn a shade smaller than a hunter and in their own cold
        // steel, because a contact that is thirty metres up in the air is a different kind
        // of problem from one on the grid and reading them as the same blip would be a lie.
        // The one currently on a run is drawn white and a pixel bigger: with four of them
        // circling, the only thing the radar really has to answer is which one is coming.
        foreach (var sol in world.Soldiers)
        {
            if (!sol.Alive) continue;
            Vector2 rel = Torus.Delta(p.Position, sol.Position);
            float rx = rel.X * c - rel.Y * s;
            float ry = rel.X * s + rel.Y * c;

            float px = Math.Clamp(cx - rx * scale, x0 + 1, x0 + RadarSize - 2);
            float py = Math.Clamp(cy - ry * scale, y0 + 1, y0 + RadarSize - 2);

            // Anything fighting on the player's side is not a contact any more, it is an
            // asset, and the radar says so: a turned body in the plague's own magenta, and a
            // squad flying cover in the charged teal every friendly thing on this display has
            // always used. With eight people in the air over one fight, which of them are
            // yours is the single most useful pixel on the panel.
            if (sol.Allied)
                Raylib.DrawRectangle((int)px, (int)py, 2, 2, Palette.BatteryCore);
            else if (sol.Carrier)
                Raylib.DrawRectangle((int)px, (int)py, 2, 2, Palette.NeonMagenta);
            else if (sol.BladesOut)
                Raylib.DrawRectangle((int)px - 1, (int)py - 1, 3, 3, Palette.SoldierBlade);
            else
                Raylib.DrawRectangle((int)px, (int)py, 2, 2,
                    sol.Tagged > 0f ? Palette.NeonMagenta
                    : sol.IsLeader ? Palette.SoldierMark : Palette.SoldierSteel);
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
