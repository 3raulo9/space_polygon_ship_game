using Raylib_cs;
using Unrendered.Core;
using Unrendered.Entities;

namespace Unrendered.Rendering;

/// <summary>
/// Everything DESCENT adds to the dashboard: the wave bar, the boss's stack of layer bars, and
/// the salvage window's clock and READY ring. Drawn under the existing top strip, in the band
/// between it and the crosshair, which is the one horizontal slice of this screen that has
/// always been empty.
///
/// <para>Nothing here draws in SANDBOX. The whole overlay is behind one
/// <see cref="World.World.IsDescent"/> test, so a sandbox run's HUD is the HUD it has
/// always been, to the pixel.</para>
/// </summary>
internal static class DescentHud
{
    private const int W = Config.InternalWidth;    // 320
    private const int H = Config.InternalHeight;   // 240
    private const int StripH = 40;                 // matches HudRenderer's top strip

    /// <summary>The band the whole overlay lives in — just under the strip's seam line.</summary>
    private const int Top = StripH + 3;
    private const int Margin = 10;

    /// <summary>
    /// Where this overlay has to stop on the right. The radar is 52 internal pixels square in a
    /// 40-pixel strip, so it hangs about eighteen pixels <em>below</em> the seam line — straight
    /// through the band everything here draws in. A full-width bar runs under it and a count
    /// right-aligned to the screen edge lands inside it and cannot be read at all, which is
    /// exactly how the first capture of this HUD came out.
    /// </summary>
    private const int BarRight = W - 62;
    private const int BarW = BarRight - Margin;

    /// <summary>The centre of the usable band, which is NOT the centre of the screen — see
    /// <see cref="BarRight"/>. Everything centred here centres on the space that is actually
    /// free.</summary>
    private const int Mid = Margin + BarW / 2;

    public static void Draw(World.World world)
    {
        if (world.Run is not { } run) return;

        switch (run.Phase)
        {
            case DescentPhase.Landing:
                DrawLanding(run);
                break;
            case DescentPhase.Wave:
                DrawWaveBar(run);
                break;
            case DescentPhase.Herald:
            case DescentPhase.Colossus:
                if (world.Headline is { } boss) DrawBossBars(boss, run);
                break;
            case DescentPhase.Intermission:
                DrawIntermission(run);
                break;
            case DescentPhase.Cleared:
            case DescentPhase.Lost:
                DrawEnding(run);
                break;
        }

        DrawFragmentTally(world, run);
    }

    // --- The landing ---------------------------------------------------------------

    private static void DrawLanding(Descent run)
    {
        Planet where = Planet.Get(run.Destination);
        PixelFont.DrawCentered(where.Name, Mid, Top, 2, Palette.HudChrome);
        // The lock stated plainly, once, at the only moment it can still surprise anybody.
        PixelFont.DrawCentered("YOU ARE BOUND TO THIS WORLD", Mid, Top + 18, 1,
            Scale(Palette.Flag, 0.85f));
        PixelFont.DrawCentered($"FIRST CONTACT IN {MathF.Ceiling(run.Clock):0}",
            Mid, Top + 28, 1, Palette.HudChrome);
    }

    // --- The wave bar ---------------------------------------------------------------

    /// <summary>
    /// The crowd, drawn as one notch per contact. This is the bar the mode was asked for —
    /// twenty of twenty is full, and it shrinks to nothing at the last enemy — with one
    /// addition worth the extra code: the notches for the ones still <em>on the field</em> are
    /// drawn brighter than the ones still out in the reserve. So the bar says two things at
    /// once, "how much of this wave is left" and "how much of it is currently trying to kill
    /// me", and a player can tell a lull from actual progress.
    ///
    /// <para>Above about forty contacts the notches stop being individually legible at 320
    /// pixels across, so it collapses to a plain filled bar. Fifty separate marks in 292 pixels
    /// is a dither pattern, not a readout.</para>
    /// </summary>
    private static void DrawWaveBar(Descent run)
    {
        int total = Math.Max(1, run.WaveTotal);
        int left = total - run.Killed;

        // Label and count both on the left, one after the other. The count used to be
        // right-aligned to the screen edge, which put it underneath the radar.
        // PixelFont.Draw hands back the ADVANCE WIDTH, not the pen position it finished at, so
        // the next string starts at Margin + that. Adding the gap to the return value alone
        // printed "WAVE 110/10".
        int labelW = PixelFont.Draw($"WAVE {run.Wave}", Margin, Top, 1, Palette.HudChrome);
        PixelFont.Draw($"{left}/{total}", Margin + labelW + 6, Top, 1,
            left <= total / 5 ? Palette.Flag : Scale(Palette.HudChrome, 0.8f));

        int y = Top + 10, h = 5;
        Raylib.DrawRectangle(Margin - 1, y - 1, BarW + 2, h + 2, new Color(5, 7, 10, 200));

        // The colour warms as the crowd thins, so the last few contacts of a wave of fifty read
        // as an ending rather than as the same bar slightly shorter.
        float f = run.WaveFraction;
        Color fill = f > 0.5f ? Palette.EnemyFill
            : f > 0.2f ? Palette.EliteFill : Palette.Flag;

        if (total <= NotchLimit)
        {
            int gap = 1;
            int notch = Math.Max(1, (BarW - gap * (total - 1)) / total);
            int used = notch * total + gap * (total - 1);
            int x0 = Margin + (BarW - used) / 2;
            for (int i = 0; i < total; i++)
            {
                if (i >= left) break;   // dead: nothing drawn at all, so the bar visibly shortens
                // The ones already on the field burn brighter than the ones still to come.
                bool live = i >= left - Math.Min(left, run.WaveTotal - run.Reserve - run.Killed);
                Raylib.DrawRectangle(x0 + i * (notch + gap), y, notch, h,
                    live ? fill : Scale(fill, 0.45f));
            }
        }
        else
        {
            Raylib.DrawRectangle(Margin, y, (int)(BarW * f), h, fill);
            // And the live share as a brighter inset, since the notches are gone.
            int onField = Math.Max(0, run.WaveTotal - run.Reserve - run.Killed);
            Raylib.DrawRectangle(Margin, y + 1, (int)(BarW * (onField / (float)total)), h - 2,
                Scale(fill, 1.4f));
        }
    }

    /// <summary>Above this many contacts, one notch each stops being a mark and starts being
    /// noise. Wave four (fifty) is deliberately over the line.</summary>
    private const int NotchLimit = 36;

    // --- The boss's layers ----------------------------------------------------------

    /// <summary>
    /// One bar per shell, stacked, emptying from the top down as the thing is peeled. Five of
    /// them for the Colossus, which is the whole point of the fight's shape: you are not
    /// grinding one long bar, you are breaking five things in a row, and between each of them
    /// the boss on screen visibly loses a layer of itself.
    ///
    /// <para>The name is rolled, and it carries information — SEALED means the plates hold,
    /// LEECHING means kill the adds — so it is printed at double size and never abbreviated.</para>
    /// </summary>
    private static void DrawBossBars(ModularBoss boss, Descent run)
    {
        BossGenome g = boss.Gene;
        bool colossus = run.Phase == DescentPhase.Colossus;

        PixelFont.DrawCentered(g.FullName, Mid, Top, colossus ? 2 : 1,
            colossus ? Palette.NeonRed : Palette.HudChrome);

        int y = Top + (colossus ? 16 : 10);
        // Two pixels of void between bars, not one. At a single pixel the Colossus's five bars
        // stack into what reads as one thick striped block — and the whole point of the fight's
        // shape is that you can see there are five of them.
        const int h = 3, gap = 2;

        // Drawn outermost first, which is the highest index — see ModularBoss.LayerFraction.
        for (int i = g.Layers - 1; i >= 0; i--)
        {
            float f = boss.LayerFraction(i);
            bool active = i == boss.LayersLeft - 1;

            Raylib.DrawRectangle(Margin - 1, y - 1, BarW + 2, h + 2, new Color(5, 7, 10, 200));
            if (f > 0f)
            {
                // The bar being fought is the boss's own core colour; the ones behind it are
                // dimmed, so at a glance you can see how much of the thing is still to come.
                Color col = active ? g.Core : Scale(g.Core, 0.4f);
                if (active && boss.Exposed) col = Color.White;   // the window, said outright
                Raylib.DrawRectangle(Margin, y, (int)(BarW * f), h, col);
            }
            y += h + gap;
        }

        // The two states where a shot does nothing, said out loud. A round that quietly fails
        // to register reads as the game being broken rather than as armour working.
        string? note =
            boss.Untouchable ? "PHASED - NOTHING WILL LAND"
            : boss.PlatesUp ? "PLATED - HIT IT FROM BEHIND"
            : boss.Exposed ? "OPEN"
            : null;
        if (note is not null)
            PixelFont.DrawCentered(note, Mid, y + 2, 1,
                boss.Exposed ? Palette.Flag : Palette.Warning);
    }

    // --- The salvage window ---------------------------------------------------------

    /// <summary>
    /// The break. Ninety seconds with salvage in it and a way out — the clock, what is coming
    /// next, and the READY ring filling as somebody holds the key down.
    /// </summary>
    private static void DrawIntermission(Descent run)
    {
        int secs = (int)MathF.Ceiling(MathF.Max(0f, run.Clock));
        PixelFont.DrawCentered("SALVAGE WINDOW", Mid, Top, 1, Palette.BatteryCore);
        PixelFont.DrawCentered($"{secs / 60}:{secs % 60:00}", Mid, Top + 10, 2,
            secs <= 10 ? Palette.Flag : Palette.HudChrome);

        // What is coming, which is genuinely useful while deciding what to craft: the family of
        // the next fight is known, even though what it will actually be is not.
        string next = run.Wave > Descent.HeraldWaves
            ? "NEXT: THE DESCENT"
            : $"NEXT: WAVE {run.Wave} - {Descent.WaveSizes[run.Wave - 1]} CONTACTS";
        PixelFont.DrawCentered(next, Mid, Top + 28, 1, Scale(Palette.HudChrome, 0.8f));

        DrawReadyRing(run.Ready);
    }

    /// <summary>
    /// The hold, drawn as a ring that fills clockwise. A ring rather than a bar on purpose: in a
    /// room this is a commitment several people can watch building, and a filling circle reads
    /// as "somebody is doing something" from the corner of the eye in a way a growing rectangle
    /// does not.
    /// </summary>
    private static void DrawReadyRing(float ready)
    {
        int cx = Mid, cy = Top + 44;
        const int r = 9;

        Color rim = ready > 0f ? Palette.Flag : Scale(Palette.HudChrome, 0.5f);
        Raylib.DrawCircleLines(cx, cy, r, rim);

        if (ready > 0f)
        {
            // Raylib's sector runs from an angle to an angle; starting at −90° puts the fill's
            // origin at the top of the ring, which is where an eye expects a clock to begin.
            Raylib.DrawCircleSector(new System.Numerics.Vector2(cx, cy), r - 1.5f,
                -90f, -90f + 360f * Math.Clamp(ready, 0f, 1f), 24,
                new Color(Palette.Flag.R, Palette.Flag.G, Palette.Flag.B, (byte)190));
        }

        PixelFont.DrawCentered(ready > 0f ? "HOLD" : "HOLD V", cx, cy + r + 3, 1,
            ready > 0f ? Palette.Flag : Scale(Palette.HudChrome, 0.7f));
    }

    // --- The endings -----------------------------------------------------------------

    private static void DrawEnding(Descent run)
    {
        bool won = run.Phase == DescentPhase.Cleared;
        PixelFont.DrawCentered(won ? Planet.Get(run.Destination).Name : "THE DESCENT ENDS HERE",
            Mid, Top, 2, won ? Palette.BatteryCore : Palette.Warning);
        if (won) PixelFont.DrawCentered("IS CLEAR", Mid, Top + 18, 1, Palette.HudChrome);
    }

    // --- What people are carrying -----------------------------------------------------

    /// <summary>
    /// This machine's own fragment count, low on the left where nothing else lives. Everybody
    /// else's is worn under their nickname out in the world (see
    /// <c>Renderer.DrawPlayerTags</c>) — which is the whole point of the tag, and the reason
    /// this one is a small tally rather than the same badge again.
    /// </summary>
    private static void DrawFragmentTally(World.World world, Descent run)
    {
        int seat = world.LocalIndex;
        int suns = run.SunsOf(seat), moons = run.MoonsOf(seat);
        if (suns + moons == 0) return;

        int y = H - 52;
        if (suns > 0)
        {
            PixelFont.Draw(suns == 1 ? "FRAGMENT OF THE SUN" : $"FRAGMENT OF THE SUN X{suns}",
                8, y, 1, Palette.Flag);
            y += 9;
        }
        if (moons > 0)
            PixelFont.Draw(moons == 1 ? "FRAGMENT OF THE MOON" : $"FRAGMENT OF THE MOON X{moons}",
                8, y, 1, Palette.HudChrome);
    }

    private static Color Scale(Color c, float t) => new(
        (int)Math.Clamp(c.R * t, 0, 255),
        (int)Math.Clamp(c.G * t, 0, 255),
        (int)Math.Clamp(c.B * t, 0, 255),
        (int)c.A);
}
