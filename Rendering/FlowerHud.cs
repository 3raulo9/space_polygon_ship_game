using Raylib_cs;
using Unrendered.Core;
using Unrendered.Entities;

namespace Unrendered.Rendering;

/// <summary>
/// The FLOWER's instruments, laid <em>over</em> the standard dashboard the way the FISH's are.
/// The vitals, the equip row, the radar and the scope are all still there and all still mean
/// what they have always meant; this adds the three things that only exist on a plant.
///
/// <para>The petal ring is the one that matters. Every other chassis's ammunition is a number
/// on a bar, and a bar is exactly the wrong instrument for a dozen discrete objects that leave,
/// do something, and come back — a player needs to know not "how much" but "which ones, and
/// where are they in their journey". So it is drawn as a literal ring, in the same slot order
/// the head wears them: seated ones filled, ones in the air hollow with a tick showing whether
/// they are outbound or homing, and ones lost dimly ticking back up as they grow. It is the
/// class's whole tactical state in about thirty pixels of diameter.</para>
///
/// <para>At twelve slots the ring stopped being something anybody counts under fire, so the
/// numeral in its centre carries the count and the ring carries the <em>shape</em> of it — how
/// much is out, how much is coming, how much is gone. That division is why both are drawn.</para>
///
/// <para>The ripeness column beside it is the other half. This game has never had a bar that
/// counts <em>up toward something good</em> — every gauge in it is a thing being spent — and the
/// harvest is the first, which is why it gets the honest vertical tape rather than being folded
/// into the strip as a fifth letter.</para>
/// </summary>
internal static class FlowerHud
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
    private static readonly Color Bad = new(220, 90, 70, 235);
    private static readonly Color Crop = new(201, 178, 58, 235);   // the flag yellow: ripeness
    private static readonly Color Bloom = new(226, 190, 96, 240);

    public static void DrawOverlay(World.World world, FlowerRig stalk, PlayerTank p)
    {
        float now = (float)Raylib.GetTime();
        DrawRipeness(stalk, now);
        DrawPetalRing(stalk, now);
        DrawCrosshair(stalk);
        DrawLean(stalk);
        DrawSap(stalk, p, now);
        DrawReplant(world, stalk, p, now);
    }

    // --- Ripeness -----------------------------------------------------------------
    // Hard against the left edge, in the margin the dashboard never uses — the same column the
    // fish's depth ladder takes, and for the same reason: it is a continuous quantity and it
    // wants a tape, not a corner.

    private const int TapeX = 3;
    private const int TapeTop = StripH + 8;
    // Shortened when the ring went to twelve: a bigger ring needs more of this column, and the
    // tape loses nothing by it — it is a fraction, not a scale, and nobody reads it in units.
    private const int TapeH = 94;
    private const int TapeW = 7;

    /// <summary>
    /// How ripe the head is, as a tape that fills bottom-up. Ripe, it stops being a gauge and
    /// starts being an offer: the whole column goes solid and pulses, because at that point
    /// the only number the player wants is "yes".
    /// </summary>
    private static void DrawRipeness(FlowerRig stalk, float now)
    {
        Raylib.DrawRectangleLines(TapeX, TapeTop, TapeW, TapeH, Dim);

        int fill = (int)((TapeH - 2) * Math.Clamp(stalk.Ripeness, 0f, 1f));
        if (fill > 0)
            Raylib.DrawRectangle(TapeX + 1, TapeTop + TapeH - 1 - fill, TapeW - 2, fill, Crop);

        if (stalk.Ripe)
        {
            // Ripe: a border that breathes, so it reads from the corner of an eye while the
            // player is busy with whatever is actually shooting at them.
            float pulse = 0.5f + 0.5f * MathF.Sin(now * 4.2f);
            var ring = new Color(Bloom.R, Bloom.G, Bloom.B, (int)(120 + 135 * pulse));
            Raylib.DrawRectangleLines(TapeX - 1, TapeTop - 1, TapeW + 2, TapeH + 2, ring);
            PixelFont.Draw("SET", TapeX - 2, TapeTop + TapeH + 3, 1, Bloom);
            return;
        }

        // Growing: the light it is growing in, said in one glyph. Nothing else in the game
        // tells the player that the time of day is doing anything to them, and on SOLUNE it is
        // doing quite a lot.
        float rate = FlowerRig.RipenRateAt(stalk.Night);
        Color sun = rate > 1.2f ? Bloom : rate > 0.7f ? Ink : Dim;
        PixelFont.Draw("RIPE", TapeX - 2, TapeTop + TapeH + 3, 1, Dim);
        Raylib.DrawRectangle(TapeX + 2, TapeTop - 5, 3, 3, sun);
    }

    // --- The petal ring -----------------------------------------------------------
    // Clear of the ripeness tape and low enough to stay out of the radar's corner.

    // Grown from a radius of 13 when the ring went from six petals to twelve. At the old size
    // twelve pips sat about six pixels apart and the five-pixel hollow markers of the ones in
    // flight overlapped their neighbours into an unreadable smear. Sixteen gives about eight
    // pixels of arc between slots, which is the least that reads as twelve separate things.
    private const int RingCx = 27;
    private const int RingCy = StripH + 136;
    private const int RingR = 16;

    /// <summary>
    /// The six, in the ring they sit in on the head. A slot's appearance is its state and
    /// nothing else has to be read: filled is seated, an outward tick is a petal on its way
    /// out, an inward one is a petal coming home, and a dim wedge closing is one growing back.
    /// </summary>
    private static void DrawPetalRing(FlowerRig stalk, float now)
    {
        var ring = stalk.Petals;
        for (int i = 0; i < ring.Count; i++)
        {
            // Slot zero at the top and clockwise from there, which is how the head reads when
            // the plant is looked at from in front.
            float a = MathF.Tau * i / ring.Count;
            int x = RingCx + (int)MathF.Round(MathF.Sin(a) * RingR);
            int y = RingCy - (int)MathF.Round(MathF.Cos(a) * RingR);

            switch (ring[i].State)
            {
                case FlowerRig.PetalState.Seated:
                    Raylib.DrawRectangle(x - 1, y - 1, 3, 3, Crop);
                    break;

                case FlowerRig.PetalState.Outbound:
                case FlowerRig.PetalState.Homing:
                {
                    bool coming = ring[i].State == FlowerRig.PetalState.Homing;
                    // Hollow: the slot is empty and the player can see it is empty. Three
                    // pixels across rather than five — at twelve slots a five-pixel box is
                    // wider than the gap to its neighbour, and the ring closed into a smear.
                    Raylib.DrawRectangleLines(x - 1, y - 1, 3, 3, coming ? Live : Bloom);
                    // A tick pointing the way it is travelling — out of the ring, or back into
                    // it. One pixel, and it is the difference between "I have thrown that" and
                    // "I am about to have that back".
                    int dx = (int)MathF.Round(MathF.Sin(a) * (coming ? -3f : 3f));
                    int dy = (int)MathF.Round(MathF.Cos(a) * (coming ? 3f : -3f));
                    Raylib.DrawRectangle(x + dx, y + dy, 1, 1, coming ? Live : Bloom);
                    break;
                }

                default:
                {
                    // Regrowing: a dim pip that brightens as the clock runs out, so a player
                    // who is waiting on a petal can see it coming rather than being surprised.
                    float t = 1f - Math.Clamp(ring[i].Regrow / FlowerRig.RegrowTime, 0f, 1f);
                    var c = new Color(Dim.R, Dim.G, Dim.B, (int)(70 + 150 * t));
                    Raylib.DrawRectangle(x, y, 1, 1, c);
                    if (t > 0.75f) Raylib.DrawRectangleLines(x - 1, y - 1, 3, 3, c);
                    break;
                }
            }
        }

        // The count in the middle, which is the number a player actually calls out loud. It
        // matters more at twelve than it did at six: nobody counts a dozen pips under fire, so
        // the numeral is the readout and the ring around it is the texture.
        int seated = stalk.Seated;
        string n = seated.ToString();
        int nw = PixelFont.MeasureSmall(n);
        PixelFont.DrawSmall(n, RingCx - nw / 2 + 1, RingCy - 2,
            seated == 0 ? Bad : seated <= FlowerRig.PetalCount / 4 ? Bloom : Ink);

        // Empty ring: the whole thing goes red and pulses. Zero petals is the one situation on
        // this chassis that is genuinely dangerous and a small dim numeral does not carry it.
        // Said with the ring itself rather than with a word underneath it, because at this
        // radius there is no room left under the ring for a word.
        if (seated == 0 && MathF.Sin(now * 5f) > 0f)
            Raylib.DrawRectangleLines(RingCx - RingR - 2, RingCy - RingR - 2,
                RingR * 2 + 5, RingR * 2 + 5, Bad);
    }

    // --- The centre mark -----------------------------------------------------------

    /// <summary>
    /// The scope, saying the one thing the tank's never had to: whether there is a petal to
    /// throw. Same argument the fish's crosshair makes about the strike — it is the binary
    /// decision this chassis makes under pressure, so it belongs where the eye already is.
    /// </summary>
    private static void DrawCrosshair(FlowerRig stalk)
    {
        bool armed = stalk.CanThrow;
        Color col = stalk.Replanting ? Dim : armed ? Crop : Dim;

        Raylib.DrawRectangle(Cx, Cy, 1, 1, col);

        // Six ticks rather than four, and six rather than the twelve the head now wears: this
        // is a suggestion of the ring, not a second copy of the count — the numeral in the
        // corner is the count. Twelve ticks at this radius is a filled circle, which says
        // nothing at all. What it does say is whether a petal is available, by closing in when
        // one is, and a player learns to read that without ever being told what the ticks are.
        const int ticks = 6;
        int gap = armed ? 4 : 8;
        for (int i = 0; i < ticks; i++)
        {
            float a = MathF.Tau * i / ticks;
            int x = Cx + (int)MathF.Round(MathF.Sin(a) * gap);
            int y = Cy - (int)MathF.Round(MathF.Cos(a) * gap);
            Raylib.DrawRectangle(x, y, 1, 1, col);
        }
    }

    // --- The lean ------------------------------------------------------------------

    /// <summary>
    /// How far the stalk is bent and which way, as a dot in a small box under the ring.
    ///
    /// <para>This is the only movement readout in the game that is worth drawing, because it is
    /// the only chassis whose movement has a <em>limit you can hit</em>. Every other craft's
    /// answer to "can I go further that way" is yes; this one's is a hard stop about two metres
    /// out, and a player leaning into it during a firefight needs to know they have run out of
    /// stalk before the round arrives rather than after.</para>
    /// </summary>
    private static void DrawLean(FlowerRig stalk)
    {
        const int bx = RingCx - 8, by = RingCy + RingR + 8, size = 17;
        Raylib.DrawRectangleLines(bx, by, size, size, Dim);

        int cx = bx + size / 2, cy = by + size / 2;
        Raylib.DrawRectangle(cx, cy, 1, 1, Dim);

        float f = Math.Clamp(stalk.LeanFraction, 0f, 1f);
        int dx = (int)MathF.Round(stalk.Lean.X / FlowerRig.MaxLean * (size / 2 - 2));
        int dy = (int)MathF.Round(-stalk.Lean.Y / FlowerRig.MaxLean * (size / 2 - 2));

        // At the stop it goes red: the stalk will not give any further and leaning harder is
        // no longer an answer to anything.
        Raylib.DrawRectangle(cx + dx, cy + dy, 1, 1, f > 0.92f ? Bad : Live);
        if (f > 0.92f) Raylib.DrawRectangleLines(bx - 1, by - 1, size + 2, size + 2, Bad);
    }

    // --- The replant ----------------------------------------------------------------

    /// <summary>
    /// What is happening to the plant when the plant is not a plant. Three states, each with a
    /// word, because the transit is the one time the player has no control at all and telling
    /// them exactly how much longer that lasts is the difference between a cost and a bug.
    /// </summary>
    private static void DrawReplant(World.World world, FlowerRig stalk, PlayerTank p, float now)
    {
        const int y = StripH + 6;

        if (stalk.Replanting)
        {
            string word = stalk.State switch
            {
                FlowerRig.Stance.Wilting => "PULLING",
                FlowerRig.Stance.Seeded => "UNDER",
                _ => "TAKING ROOT",
            };
            PixelFont.DrawCentered(word, Cx, y, 1, Bloom);

            // A bar under the word running the whole transit rather than the current state, so
            // it fills once, monotonically, and never appears to restart three times.
            float through = stalk.State switch
            {
                FlowerRig.Stance.Wilting => stalk.StancePhase * 0.3f,
                FlowerRig.Stance.Seeded => 0.3f + stalk.StancePhase * 0.2f,
                _ => 0.5f + stalk.StancePhase * 0.5f,
            };
            const int bw = 60;
            Raylib.DrawRectangleLines(Cx - bw / 2, y + 9, bw, 3, Dim);
            Raylib.DrawRectangle(Cx - bw / 2 + 1, y + 10, (int)((bw - 2) * through), 1, Bloom);
            return;
        }

        if (!world.IsChoosingGround(p)) return;

        // Choosing — and it will always go. There is no affordability to report any more: the
        // replant is free, so the panel's only job here is to say which key finishes the
        // decision the player has already started making.
        PixelFont.DrawCentered("RELEASE TO GROW", Cx, y, 1, Live);
    }

    // --- The reserve ------------------------------------------------------------------

    /// <summary>
    /// What SAP is doing, which since the replant went free is one thing only: growing petals
    /// back. Drawn beside the ring rather than in the top strip, because the strip's Y bar says
    /// how much is left and this says what it is being spent on — and on this chassis those are
    /// the same sentence.
    ///
    /// <para>Silent while nothing is regrowing, since a reserve nothing is drawing on is not
    /// information. It only speaks in the two states that matter: petals coming back, and
    /// petals coming back <em>slowly</em> because the plant has run itself dry.</para>
    /// </summary>
    private static void DrawSap(FlowerRig stalk, PlayerTank p, float now)
    {
        int growing = 0;
        foreach (var q in stalk.Petals)
            if (q.State == FlowerRig.PetalState.Regrowing) growing++;
        if (growing == 0) return;

        const int y = RingCy + RingR + 27;
        int x0 = RingCx - 14;
        const int w = 28;

        // Starved is the whole point of the readout, so it is the loud state: the word flashes
        // and the bar goes to the warning colour. Fed, it is a quiet strip that happens to be
        // draining, which is all a player needs to glance at.
        bool starved = stalk.Starved;
        Color ink = starved ? Bad : Live;

        Raylib.DrawRectangleLines(x0, y, w, 3, Dim);
        Raylib.DrawRectangle(x0 + 1, y + 1, (int)((w - 2) * p.HyperFraction), 1, ink);

        if (starved && MathF.Sin(now * 5f) > 0f)
            PixelFont.Draw("DRY", x0 + 7, y + 5, 1, Bad);
    }
}
