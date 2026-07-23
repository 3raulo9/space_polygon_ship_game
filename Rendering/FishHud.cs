using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.Entities;

namespace VoidTanks.Rendering;

/// <summary>
/// The FISH's instruments, laid <em>over</em> the standard dashboard rather than instead
/// of it. The vitals, the equip row, the radar and the firing scope are all still there
/// and all still mean what they have always meant — it is the same run and the same
/// reserves — and this adds the four things that only exist on a chassis swimming between
/// a floor that beaches it and a ceiling that poisons it:
///
///   the depth ladder, which is the class's whole spatial problem in one column,
///   the strike's state,
///   the breath, which refills only while the tail is still,
///   and the two hazards, each of which gets a shout the player cannot miss.
///
/// The ladder is the important one and it is why this file exists at all. Every other
/// chassis in this game plays on a plane and the radar in the corner tells them everything
/// they need. This one plays in a <em>column</em>, and there has never been an instrument
/// in the game that says how high anything is.
/// </summary>
internal static class FishHud
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
    private static readonly Color Bloom = new(226, 96, 178, 235);

    public static void DrawOverlay(World.World world, FishRig body, PlayerTank p)
    {
        DrawDepthLadder(body);
        DrawCrosshair(body);
        DrawStrikeState(body);
        DrawBreath(p, body);
        DrawHazards(body, (float)Raylib.GetTime());
    }

    // --- The depth ladder ---------------------------------------------------------

    // Hard against the left edge, and narrow. It shares this side of the frame with the
    // strike and breath readouts, so it takes the margin the dashboard never uses and
    // leaves everything from x=14 rightward to the text.
    private const int LadderX = 3;
    private const int LadderTop = StripH + 8;
    private const int LadderH = 146;
    private const int LadderW = 7;

    /// <summary>
    /// A vertical tape down the left of the frame showing where in the water column the
    /// body is, with both hazards drawn on it as bands rather than as numbers.
    ///
    /// It is a tape and not a readout on purpose. A figure — "34m" — tells a player their
    /// altitude, which is not the question they have. The question is *how much room is
    /// left*, in both directions, and a band with a marker sliding between two hatched
    /// ends answers that at a glance from the corner of an eye while the player is busy
    /// threading a tower.
    /// </summary>
    private static void DrawDepthLadder(FishRig body)
    {
        // The tape spans the deck to a little above the bloom's floor, so both hazards are
        // always on screen and the marker never runs off either end.
        const float top = FishRig.BloomHeight + 14f;

        Raylib.DrawRectangleLines(LadderX, LadderTop, LadderW, LadderH, Dim);

        // The bloom, hatched across the top of the tape. Drawn as alternating rows so it
        // reads as hazard tape rather than as a filled bar — a solid block up there looks
        // like a full gauge, which is the opposite of what it means.
        int bloomY = LadderTop + (int)(LadderH * (1f - FishRig.BloomHeight / top));
        int warnY = LadderTop + (int)(LadderH * (1f - FishRig.BloomWarnHeight / top));

        for (int y = LadderTop + 1; y < bloomY; y += 2)
            Raylib.DrawRectangle(LadderX + 1, y, LadderW - 2, 1, Bloom);
        // The warned band between them: dimmer, and every fourth row, so the two are
        // never confused for one another.
        for (int y = bloomY; y < warnY; y += 4)
            Raylib.DrawRectangle(LadderX + 1, y, LadderW - 2, 1,
                new Color(160, 70, 128, 170));

        // And the seabed, hatched along the bottom in the other hazard's colour.
        for (int y = LadderTop + LadderH - 5; y < LadderTop + LadderH - 1; y += 2)
            Raylib.DrawRectangle(LadderX + 1, y, LadderW - 2, 1, new Color(150, 130, 80, 190));

        // The body itself: a bar across the tape at its own height, plus a tick sticking
        // out to the right so the mark is findable against the hatching behind it.
        float f = Math.Clamp(body.Depth / top, 0f, 1f);
        int at = LadderTop + (int)(LadderH * (1f - f));
        at = Math.Clamp(at, LadderTop + 1, LadderTop + LadderH - 2);

        Color mark = body.InBloom ? Bloom : body.Beached ? Bad : Live;
        Raylib.DrawRectangle(LadderX - 1, at, LadderW + 3, 1, mark);
        Raylib.DrawRectangle(LadderX + LadderW + 2, at - 1, 3, 3, mark);

        // Labelled underneath rather than above. The tape's top end is the bloom, and a
        // word sitting on it would be a word inside the hazard hatching.
        PixelFont.Draw("DPTH", LadderX - 2, LadderTop + LadderH + 3, 1, Dim);
    }

    // --- The crosshair ------------------------------------------------------------

    /// <summary>
    /// A small centre mark that says one thing the tank's scope never had to: whether the
    /// strike is available. That is the only binary decision this chassis makes and it is
    /// made mid-carve, so it belongs at the centre of the frame where the player is already
    /// looking rather than on a gauge they would have to break off to read.
    /// </summary>
    private static void DrawCrosshair(FishRig body)
    {
        bool armed = body.Strike == StrikeState.Ready && !body.Beached;
        Color col = body.Strike == StrikeState.Lunge ? Color.White
                  : armed ? Live
                  : Dim;

        Raylib.DrawRectangle(Cx, Cy, 1, 1, col);

        if (armed)
        {
            // Armed: four ticks drawn in close, so the mark is tight and reads as ready.
            const int gap = 4, len = 3;
            Raylib.DrawRectangle(Cx - gap - len, Cy, len, 1, col);
            Raylib.DrawRectangle(Cx + gap + 1, Cy, len, 1, col);
            Raylib.DrawRectangle(Cx, Cy - gap - len, 1, len, col);
            Raylib.DrawRectangle(Cx, Cy + gap + 1, 1, len, col);
            return;
        }

        // Spent, coiling or beached: the same four ticks pushed out and dimmed. The gap
        // between the two states is the entire readout, and it costs eight rectangles.
        int wide = 8 + (int)(body.StrikePhase * 4f);
        Raylib.DrawRectangle(Cx - wide - 2, Cy, 2, 1, col);
        Raylib.DrawRectangle(Cx + wide + 1, Cy, 2, 1, col);
        Raylib.DrawRectangle(Cx, Cy - wide - 2, 1, 2, col);
        Raylib.DrawRectangle(Cx, Cy + wide + 1, 1, 2, col);
    }

    // --- The strike and the breath ------------------------------------------------

    // Clear to the right of the depth ladder, which owns the left margin.
    private const int PanelX = 16;
    private const int PanelY = StripH + 6;

    /// <summary>
    /// What the strike is doing, as a short bar under the vitals. Four states and each one
    /// is a different length and colour, so it is read as a shape rather than as a word —
    /// which matters, because the two states worth knowing about (winding, and spent) are
    /// both states in which the player has no attention to spare.
    /// </summary>
    private static void DrawStrikeState(FishRig body)
    {
        PixelFont.Draw("STRIKE", PanelX, PanelY, 1, Dim);

        const int barW = 34, barH = 4;
        int y = PanelY + 8;
        Raylib.DrawRectangleLines(PanelX, y, barW, barH, Dim);

        (float fill, Color col) = body.Strike switch
        {
            StrikeState.Ready => (1f, Live),
            StrikeState.Coil => (body.StrikePhase, Color.White),
            StrikeState.Lunge => (1f, Color.White),
            _ => (body.StrikePhase, new Color(150, 90, 70, 200)),
        };

        int fillW = (int)((barW - 2) * Math.Clamp(fill, 0f, 1f));
        if (fillW > 0) Raylib.DrawRectangle(PanelX + 1, y + 1, fillW, barH - 2, col);
    }

    /// <summary>
    /// The breath. The reserve itself is already on the dashboard's H bar — this says the
    /// one thing that bar cannot, which is whether it is currently <em>going up</em>.
    ///
    /// That distinction is the whole economy of the class. The reserve only refills while
    /// the tail is still, so a player mid-sprint is spending a resource that is not coming
    /// back, and the difference between "low" and "low and not recovering" is the
    /// difference between a beat in hand and a long sink down to the deck.
    /// </summary>
    private static void DrawBreath(PlayerTank p, FishRig body)
    {
        int y = PanelY + 18;
        bool low = p.HyperFraction < 0.25f;

        if (body.Recovering)
        {
            PixelFont.Draw("BREATH +", PanelX, y, 1, low ? Bad : Live);
            return;
        }

        // Held: the tail is working and nothing is coming back. Flashed while low, so a
        // player running themselves dry is told before the beats stop landing.
        if (low && ((int)(Raylib.GetTime() * 5f) & 1) == 0) return;
        PixelFont.Draw("BREATH -", PanelX, y, 1, low ? Bad : Dim);
    }

    // --- The two hazards ----------------------------------------------------------

    /// <summary>
    /// The floor and the ceiling, each shouted in the middle of the frame when it matters.
    ///
    /// Both are placed below the crosshair rather than over it — the player is aiming
    /// through the centre of the screen and a warning that covers the thing they are
    /// pointing at is a punishment for being in trouble rather than a way out of it. Both
    /// also say what to <em>do</em> and not merely what is wrong, because at the moment
    /// either of these fires the player has about a second and no attention left for
    /// interpreting anything.
    /// </summary>
    private static void DrawHazards(FishRig body, float elapsed)
    {
        if (body.Beached)
        {
            // The floor. No flashing — this state does not resolve itself and a steady
            // instruction is more useful than an urgent one.
            PixelFont.DrawCentered("BEACHED", Cx, Cy + 22, 2, Bad);
            PixelFont.DrawCentered("BEAT TO CLEAR THE DECK", Cx, Cy + 40, 1, Ink);
            return;
        }

        if (body.InBloom)
        {
            // Inside it and taking damage. Flashed at the rate of the bites themselves, so
            // the sign, the alarm and the shield bar are all counting the same clock.
            if (((int)(elapsed * 4f) & 1) == 0) return;
            PixelFont.DrawCentered("TOXIC BLOOM", Cx, Cy + 22, 2, Bloom);
            PixelFont.DrawCentered("DESCEND", Cx, Cy + 40, 1, Bad);
            return;
        }

        if (body.BloomNotice <= 0f) return;

        // The warned band: the free notice. Steady, one line, and deliberately calm — it
        // costs nothing to be up here yet, and crying wolf now would spend the alarm that
        // is about to be needed.
        PixelFont.DrawCentered("BLOOM ABOVE", Cx, Cy + 26, 1,
            GridRenderer.LerpColor(Ink, Bloom, body.BloomNotice));
    }
}
