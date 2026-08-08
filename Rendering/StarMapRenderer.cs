using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.UI;

namespace Unrendered.Rendering;

/// <summary>
/// The chart of the five worlds. Drawn twice over: as the solo screen between the hangar and
/// the drop, and as the panel that comes up when somebody leans on the lobby's holo table.
/// Both are the same picture — a row of worlds, the chosen one named and spelled out — because
/// they are the same decision, and a player who has made it alone should recognise it when
/// twenty people are making it together.
///
/// The worlds are octagons, not circles. Nothing in this game is round.
/// </summary>
public static class StarMapRenderer
{
    private const int W = Config.InternalWidth;
    private const int H = Config.InternalHeight;

    private const int Sides = 8;

    // The row across the top is uniform — five worlds, all the same size, so the eye reads it
    // as a set of choices rather than as one thing with four smaller things beside it. What
    // marks the selection is a ring and a name, and then the big body lower down.
    private const float RowRadius = 8f;
    private const float BigRadius = 27f;

    private const int RowY = 56;      // centres of the small discs
    private const int BigX = 54;      // centre of the selected world, low and to the left
    private const int BigY = 150;
    private const int TextX = 100;    // the readout column, clear of the big body
    private const int TextY = 112;

    /// <summary>Where world <paramref name="i"/>'s disc sits in the row. Public because the
    /// arch's panel is clicked rather than walked, and a hit test that derived the layout
    /// separately would drift the first time the row moved.</summary>
    public static Vector2 RowCentre(int i)
    {
        int n = Planet.All.Count;
        return new Vector2(W / (n + 1) * (i + 1), RowY);
    }

    /// <summary>
    /// Which world the mouse is over, or null. Generous — the click target is more than twice
    /// the disc, because these are eight-pixel octagons at 320×240 and asking a player to hit
    /// one exactly is asking them to fight the game rather than the planet.
    /// </summary>
    public static PlanetId? WorldAt(Vector2 point)
    {
        for (int i = 0; i < Planet.All.Count; i++)
        {
            Vector2 d = point - RowCentre(i);
            // Boxed rather than circular, and taller than it is wide, so the name under each
            // disc is part of its own target.
            if (MathF.Abs(d.X) <= RowRadius + 6f && d.Y >= -RowRadius - 4f && d.Y <= RowRadius + 12f)
                return Planet.All[i].Id;
        }
        return null;
    }

    /// <summary>
    /// The whole screen: the row of worlds, the chosen one large, its conditions written out,
    /// and — in a room — the tally under each and the clock over the lot.
    /// <paramref name="chart"/> supplies both the cursor and the votes; pass
    /// <paramref name="voting"/> false for a solo run, where there is nobody to vote with.
    /// </summary>
    public static void Draw(StarMap chart, GameMode mode, float elapsed, bool voting)
    {
        Raylib.ClearBackground(Palette.Void);
        DrawStarfield(elapsed);

        PixelFont.DrawCentered(mode == GameMode.Descent ? "DESCENT" : "SANDBOX", W / 2, 8, 1,
            Scale(Palette.HudChrome, 0.7f));
        PixelFont.DrawCentered("CHOOSE A DESTINATION", W / 2, 20, 2, Palette.HudChrome);

        Planet chosen = Planet.Get(chart.Cursor);
        DrawRow(chart, elapsed, voting);
        DrawBody(chosen, elapsed);
        DrawReadout(chosen, elapsed);

        if (voting) DrawClock(chart, elapsed);

        PixelFont.DrawCentered(voting
                ? "A/D TURN THE CHART   ENTER VOTE   ESC BACK"
                : "A/D TURN THE CHART   ENTER LAND   ESC BACK",
            W / 2, H - 14, 1, Scale(Palette.HudChrome, 0.6f));
    }

    /// <summary>
    /// The row of five, the cursor's world blown up in the middle of it. Each disc is painted
    /// in its own horizon colour, so the chart is legible as a set of skies before a single
    /// word of it is read — which is the whole reason five worlds exist.
    /// </summary>
    private static void DrawRow(StarMap chart, float elapsed, bool voting)
    {
        int n = Planet.All.Count;

        for (int i = 0; i < n; i++)
        {
            Planet p = Planet.All[i];
            bool on = (int)chart.Cursor == i;
            bool shut = chart.IsLocked(p.Id);
            Vector2 at = RowCentre(i);

            Disc(at, RowRadius, p, elapsed, lit: on && !shut);

            // A world the run has already used, or the one it is standing on. Struck through
            // rather than hidden: "we have been there" is a fact about the session worth
            // seeing, and a chart that silently shrank from five worlds to two would read as
            // the game having lost some.
            if (shut)
            {
                Raylib.DrawRectangle((int)at.X - (int)RowRadius - 4, RowY - 1,
                    (int)RowRadius * 2 + 8, 2, Palette.Warning);
                PixelFont.DrawCentered(p.Name, (int)at.X, RowY + (int)RowRadius + 6, 1,
                    Scale(Palette.HudChrome, 0.28f));
                continue;
            }

            if (on) Raylib.DrawPolyLines(at, Sides, RowRadius + 4f, elapsed * 18f, Palette.Flag);

            PixelFont.DrawCentered(p.Name, (int)at.X, RowY + (int)RowRadius + 6, 1,
                on ? Palette.Flag : Scale(Palette.HudChrome, 0.5f));

            if (!voting) continue;

            // The tally: one pip per vote, under the name. Pips rather than a number because
            // the room is looking at this from across a floor and a count of dots is readable
            // at a glance in a way "3" is not.
            int votes = chart.Tally(p.Id);
            for (int v = 0; v < votes && v < 10; v++)
                Raylib.DrawRectangle((int)at.X - votes * 2 + v * 4, RowY + (int)RowRadius + 16,
                    3, 3, Palette.GridNear);
        }
    }

    /// <summary>The world under the cursor, large, turning, off to the left of its own
    /// readout — so the chart has one thing on it big enough to actually look at.</summary>
    private static void DrawBody(Planet p, float elapsed)
    {
        var at = new Vector2(BigX, BigY);
        Disc(at, BigRadius, p, elapsed, lit: true);

        if (!p.HasCycle) return;
        // SOLUNE's moon, in orbit. The one world you can identify without reading a word.
        float a = elapsed * 0.55f;
        var moon = at + new Vector2(MathF.Cos(a) * (BigRadius + 12f), MathF.Sin(a) * (BigRadius * 0.45f));
        Raylib.DrawPoly(moon, 6, 4f, elapsed * 10f, Palette.HudChrome);
    }

    /// <summary>
    /// One world. Two flat tones and no gradient: the body in its own horizon colour with a
    /// darker inset pushed off to one side, which reads as a sphere lit from the left without
    /// anything resembling a light model. Eight sides, because nothing here is round.
    /// </summary>
    private static void Disc(Vector2 at, float r, Planet p, float elapsed, bool lit)
    {
        float spin = lit ? elapsed * 18f : 0f;
        float k = lit ? 1f : 0.5f;
        Raylib.DrawPoly(at, Sides, r, spin, Scale(p.SkyHorizon, k));
        Raylib.DrawPoly(at + new Vector2(r * 0.30f, 0f), Sides, r * 0.72f, spin, Scale(p.SkyMid, k));
    }

    /// <summary>
    /// What you are agreeing to. Three bars and a line of text — hostiles, pull and how far you
    /// will be able to see — because a vote without them is a vote about a colour.
    /// </summary>
    private static void DrawReadout(Planet p, float elapsed)
    {
        const int x = TextX;
        const int y = TextY;

        PixelFont.Draw(p.Name, x, y, 2, Palette.Flag);
        // The blurb is a full-width line, so it runs under the body rather than beside it.
        PixelFont.Draw(p.Blurb, 12, H - 32, 1, Scale(Palette.GridNear, 0.9f));

        // Hostiles is shown as a fraction of the worst on the chart, so the bar means something
        // relative to the other four rather than against an absolute nobody has seen.
        float worstHostiles = 0f;
        foreach (var q in Planet.All) worstHostiles = MathF.Max(worstHostiles, q.Hostiles);

        // VISIBILITY is the inverse of the world's murk. Worth being precise about what it
        // promises: every world is drawn to the same distance, so this is not how far you can
        // see but how much of what is out there you will be able to make out.
        Bar(x, y + 20, "HOSTILES", p.Hostiles / worstHostiles, Palette.EnemyFill);
        Bar(x, y + 32, "VISIBILITY", 1f - p.Murk, Palette.StructureGlow);
        PixelFont.Draw("GRAVITY", x, y + 44, 1, Scale(Palette.HudChrome, 0.7f));
        PixelFont.Draw(p.Gravity.ToString("0.0"), x + 74, y + 44, 1, Palette.HudChrome);

        if (!p.HasCycle) return;
        // The one world where the readout above is only half the story: it is different at
        // midnight, and a player deserves to be told that before they land rather than after.
        float beat = 0.6f + 0.4f * MathF.Abs(MathF.Sin(elapsed * 2f));
        PixelFont.Draw("DIURNAL", x, y + 58, 1, Scale(Palette.Flag, beat));
        PixelFont.Draw("IT GETS DARK", x + 74, y + 58, 1, Scale(Palette.Flag, beat));
    }

    private static void Bar(int x, int y, string label, float fraction, Color fill)
    {
        PixelFont.Draw(label, x, y, 1, Scale(Palette.HudChrome, 0.7f));
        const int w = 54, h = 5;
        int bx = x + 74;
        Raylib.DrawRectangleLines(bx, y, w, h, Scale(Palette.HudChrome, 0.45f));
        Raylib.DrawRectangle(bx + 1, y + 1, (int)((w - 2) * Math.Clamp(fraction, 0f, 1f)), h - 2, fill);
    }

    /// <summary>The countdown, over the chart, while a room is deciding.</summary>
    private static void DrawClock(StarMap chart, float elapsed)
    {
        if (!chart.VoteOpen)
        {
            PixelFont.DrawCentered("THE HOST IS CHOOSING", W / 2, 36, 1, Scale(Palette.HudChrome, 0.6f));
            return;
        }

        // Under five seconds it flashes. A vote closing is the one thing on this screen that
        // is about to stop being changeable.
        bool urgent = chart.SecondsLeft <= 5f;
        float beat = urgent ? 0.55f + 0.45f * MathF.Abs(MathF.Sin(elapsed * 8f)) : 1f;
        PixelFont.DrawCentered($"VOTE  {chart.SecondsLeft:0.0}", W / 2, 36, 1,
            Scale(urgent ? Palette.Warning : Palette.Flag, beat));
    }

    // --- Backdrop ---------------------------------------------------------------------

    private static readonly (int X, int Y, byte B)[] _stars = BuildStars();

    private static (int, int, byte)[] BuildStars()
    {
        var rng = new Random(90210);
        var s = new (int, int, byte)[64];
        for (int i = 0; i < s.Length; i++)
            s[i] = (rng.Next(W), rng.Next(H), (byte)(60 + rng.Next(120)));
        return s;
    }

    private static void DrawStarfield(float elapsed)
    {
        foreach (var (sx, sy, b) in _stars)
        {
            // A slow unsynchronised twinkle keeps the field from reading as a printed texture.
            float t = 0.65f + 0.35f * MathF.Sin(elapsed * 1.3f + sx * 0.11f + sy * 0.07f);
            byte v = (byte)(b * t);
            Raylib.DrawPixel(sx, sy, new Color(v, v, (byte)Math.Min(255, v + 20), (byte)255));
        }
    }

    private static Color Scale(Color c, float k)
        => new((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k), c.A);
}
