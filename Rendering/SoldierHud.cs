using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.Entities;

namespace VoidTanks.Rendering;

/// <summary>
/// The SOLDIER's instruments, laid <em>over</em> the standard dashboard rather than
/// instead of it. The vitals, the equip row, the radar and the firing scope are all
/// still there and all still mean what they have always meant — it is the same run and
/// the same reserves — and this adds the four things that only exist on a chassis
/// hanging off two steel cables:
///
///   the crosshair that brackets when there is something to anchor to,
///   the two hook indicators framing it,
///   how many rockets are left,
///   and damage, which on a body rather than a hull reads as blood on the lens.
///
/// Everything here is drawn well clear of the top strip and the bottom aim guide, so
/// nothing covers an instrument the player already knows how to read.
/// </summary>
internal static class SoldierHud
{
    private const int W = Config.InternalWidth;
    private const int H = Config.InternalHeight;
    private const int Cx = W / 2;
    private const int Cy = H / 2;

    /// <summary>Where the dashboard's top strip ends — everything below is ours.</summary>
    private const int StripH = 40;

    private static readonly Color Ink = new(196, 216, 224, 220);
    private static readonly Color Dim = new(120, 140, 148, 150);
    private static readonly Color Live = new(150, 240, 235, 230);

    public static void DrawOverlay(World.World world, SoldierRig rig, PlayerTank p)
    {
        DrawDamage(p, (float)Raylib.GetTime());
        DrawCrosshair(world, rig);
        DrawHookIndicators(rig);
        DrawRockets(p);
        DrawRigState(p, rig);
    }

    // --- The crosshair ---------------------------------------------------------

    /// <summary>
    /// A small centre crosshair that blooms into a bracket the moment the ray behind it
    /// finds something a hook can bite, and greys out when it doesn't. This is the one
    /// instrument the player actually watches: the whole skill of the class is reading
    /// the city at a glance while already moving, and this is what makes that readable
    /// without stopping.
    ///
    /// It sits at the centre of the frame rather than down with the tank's firing scope
    /// because it is not only a gun sight — it is where the next cable goes.
    /// </summary>
    private static void DrawCrosshair(World.World world, SoldierRig rig)
    {
        bool valid = world.AnchorInSight != null;
        bool weak = valid && world.AnchorIsWeak;

        // Both hooks committed: there is nothing a third shot could do, so the crosshair
        // goes cold whatever it happens to be resting on.
        bool spare = rig.Left.State == HookState.Stowed || rig.Right.State == HookState.Stowed;
        Color col = !spare ? Dim
                  : weak ? new Color(220, 90, 70, 230)
                  : valid ? Live
                  : Dim;

        // The centre dot. Always there — it is where the rifle points, and the rifle
        // points somewhere whether or not there is an anchor in front of it.
        Raylib.DrawRectangle(Cx, Cy, 1, 1, col);

        if (valid && spare)
        {
            // Bracketed: four corner ticks standing off the centre, drawn open rather
            // than closed so the thing being aimed at stays visible inside them.
            const int gap = 4, len = 3;
            foreach (int sx in new[] { -1, 1 })
            {
                foreach (int sy in new[] { -1, 1 })
                {
                    int x = Cx + sx * gap;
                    int y = Cy + sy * gap;
                    Raylib.DrawRectangle(sx < 0 ? x - len + 1 : x, y, len, 1, col);
                    Raylib.DrawRectangle(x, sy < 0 ? y - len + 1 : y, 1, len, col);
                }
            }

            // A weak anchor says so in words. It is one syllable and it is the
            // difference between a swing and a fall.
            if (weak) PixelFont.DrawCentered("WEAK", Cx, Cy + 10, 1, col);
        }
        else
        {
            // Idle: four short ticks, further out and dimmer. The gap between this and
            // the bracket above is the whole readout.
            const int gap = 6, len = 2;
            Raylib.DrawRectangle(Cx - gap - len, Cy, len, 1, col);
            Raylib.DrawRectangle(Cx + gap + 1, Cy, len, 1, col);
            Raylib.DrawRectangle(Cx, Cy - gap - len, 1, len, col);
            Raylib.DrawRectangle(Cx, Cy + gap + 1, 1, len, col);
        }
    }

    // --- The two hooks ---------------------------------------------------------

    // Left and right of the crosshair, matching the hips they hang off — so "the left
    // one is out" is read without any label, from the side of the screen it is on.
    private const int HookY = Cy - 3;
    private const int HookOffset = 26;
    private const int HookW = 12;
    private const int HookH = 7;

    private static void DrawHookIndicators(SoldierRig rig)
    {
        DrawHookState(rig.Left, Cx - HookOffset - HookW, 'Q');
        DrawHookState(rig.Right, Cx + HookOffset, 'E');
    }

    /// <summary>
    /// One hook's state as a small box: empty when stowed, a travelling tick while the
    /// cable is in flight, and filled — by however hard it is pulling — once it bites.
    /// The fill level is the tension, so a player can see which of their two cables is
    /// actually carrying them without any numbers at all.
    /// </summary>
    private static void DrawHookState(GrappleHook h, int x, char key)
    {
        Color frame = h.Anchored ? Live : Dim;
        Raylib.DrawRectangleLines(x, HookY, HookW, HookH, frame);

        switch (h.State)
        {
            case HookState.Flying:
            case HookState.Returning:
            {
                // A tick running across the box the way the cable is running: out on a
                // throw, back on a retract.
                float t = Math.Clamp(h.Flown / MathF.Max(1f, h.Reach), 0f, 1f);
                int at = x + 1 + (int)((HookW - 3) * t);
                Raylib.DrawRectangle(at, HookY + 2, 2, HookH - 4, Ink);
                break;
            }

            case HookState.Anchored:
            {
                // Filled from the left. A slack cable still shows a solid third —
                // "anchored" has to be unmistakable at a glance — and it is the tension
                // stacked on top of that which is the fine reading.
                int fill = 1 + (int)((HookW - 2) * (0.35f + 0.65f * Math.Clamp(h.Tension, 0f, 1f)));
                Color hot = GridRenderer.LerpColor(Live, Color.White, h.Tension);
                Raylib.DrawRectangle(x + 1, HookY + 1, fill, HookH - 2, hot);

                // Weak material under load: the box flashes as the anchor gives.
                if (h.Load > 0.2f && ((int)(h.Load * 14f) & 1) == 0)
                    Raylib.DrawRectangleLines(x - 1, HookY - 1, HookW + 2, HookH + 2,
                        new Color(220, 90, 70, 230));
                break;
            }
        }

        PixelFont.DrawCentered(key.ToString(), x + HookW / 2, HookY + HookH + 2, 1, frame);
    }

    // --- Rockets and the state of the rig --------------------------------------

    /// <summary>
    /// Rockets carried, as pips rather than a number — six of them, and how many are
    /// left is a shape the eye reads without counting. Tucked under the vital bars in
    /// the strip's own left column, so it reads as one more gauge on the same panel.
    /// </summary>
    private static void DrawRockets(PlayerTank p)
    {
        const int y = StripH + 6;
        PixelFont.Draw("RKT", 10, y, 1, Dim);

        for (int i = 0; i < p.MaxRockets; i++)
        {
            int x = 10 + i * 5;
            Color col = i < p.Rockets ? Palette.EliteFill : new Color(60, 70, 76, 160);
            Raylib.DrawRectangle(x, y + 8, 3, 6, col);
        }
    }

    /// <summary>
    /// What the rig is doing right now: reeling, and how much pressure is behind it.
    /// The reserve itself is already on the dashboard's H bar — this only says when the
    /// jet is actually firing, and shouts when the bottle is nearly out, which is the
    /// one fact about gas that has to reach a player who is busy looking at a wall.
    /// </summary>
    private static void DrawRigState(PlayerTank p, SoldierRig rig)
    {
        // Under the radar, not beside it: the radar is a 52-pixel square in the strip's
        // right-hand corner, and anything at the strip's own baseline over there lands on
        // top of it.
        const int y = RadarBottom + 4;

        if (rig.Reeling)
            PixelFont.Draw("REEL", W - 10 - PixelFont.Measure("REEL", 1), y, 1, Live);

        if (p.HyperFraction >= 0.25f) return;

        // Low pressure: flashing, and in the alarm colour. Off to the side rather than at
        // the centre, so it never sits over the thing the player is aiming at.
        if (((int)(Raylib.GetTime() * 5f) & 1) == 0) return;
        const string warn = "GAS LOW";
        PixelFont.Draw(warn, W - 10 - PixelFont.Measure(warn, 1), y + 9, 1,
            new Color(220, 90, 70, 235));
    }

    /// <summary>Where the dashboard's radar ends — mirrors HudRenderer's own geometry so
    /// nothing here is drawn underneath it.</summary>
    private const int RadarBottom = 52 + 6;

    // --- Damage ----------------------------------------------------------------

    /// <summary>
    /// The shield bar in the strip says what the numbers are. This says what it feels
    /// like: blood on the inside of the lens, thickening as the shield goes, and — under
    /// about a third — a heartbeat thumping a red wash in and out at the edges.
    ///
    /// The spatter pattern comes from a fixed hash rather than being rolled, so it holds
    /// still on the glass instead of crawling: blood that reshuffles every frame reads
    /// as static, not as damage.
    /// </summary>
    private static void DrawDamage(PlayerTank p, float elapsed)
    {
        float hurt = 1f - p.ShieldFraction;
        if (hurt <= 0.02f) return;

        int blobs = (int)(hurt * 26);
        for (int i = 0; i < blobs; i++)
        {
            int hash = (i * 2654435761u).GetHashCode();
            int x = (int)(((uint)hash >> 4) % W);
            int y = (int)(((uint)hash >> 13) % H);
            int size = 1 + (int)(((uint)hash >> 21) % 3);

            // Pooled toward the edges of the lens: the middle of the view is where the
            // player is aiming, and covering it in blood is a punishment for being hit
            // rather than a signal that they were.
            float fromCentre = Vector2.Distance(new Vector2(x, y), new Vector2(Cx, Cy))
                             / MathF.Sqrt(Cx * Cx + Cy * Cy);
            if (fromCentre < 0.45f) continue;

            var blood = new Color(120, 18, 18, (int)(90 + 120 * hurt));
            Raylib.DrawRectangle(x, y, size, size, blood);
        }

        if (hurt < 0.66f) return;

        // The heartbeat: a double thump, and it gets faster as the shield goes.
        float rate = 1.4f + hurt * 1.6f;
        float beat = (elapsed * rate) % 1f;
        float pulse = MathF.Max(Thump(beat, 0f), Thump(beat, 0.28f) * 0.6f);
        if (pulse <= 0.01f) return;

        var wash = new Color(90, 10, 10, (int)(70 * pulse * hurt));
        for (int i = 0; i < 7; i++)
            Raylib.DrawRectangleLines(i * 3, i * 3, W - i * 6, H - i * 6, wash);
    }

    /// <summary>One beat of the heart: a fast rise and a slower fall, zero everywhere
    /// else. Two of these offset from each other make the double thump.</summary>
    private static float Thump(float t, float at)
    {
        float d = t - at;
        if (d < 0f || d > 0.16f) return 0f;
        return d < 0.04f ? d / 0.04f : 1f - (d - 0.04f) / 0.12f;
    }
}
