using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.Entities;

namespace VoidTanks.Rendering;

/// <summary>
/// The VIRUS's instruments, laid <em>over</em> the standard dashboard the way the fish's and
/// the soldier's are. The vitals, the equip row and the radar are all still there and still
/// mean what they always meant — it is the same run and the same reserves — and this adds the
/// two readouts that only exist on a chassis with no body of its own:
///
///   the DECAY meter, which is the whole hosted phase in one gauge — how much host is left
///   before it bursts and spits you out — and
///   the state shout in the middle of the frame, because the single most important thing this
///   class has to tell the player at a glance is which of its two states they are in: safe in
///   a body, or naked and one hit from dead.
///
/// It also draws its own crosshair. Unlike the tank and the spider this chassis is not a
/// machine (<see cref="PlayerTank.IsMachine"/> is false for it), so the dashboard's centre
/// sight is skipped and this puts its own there — one that says, on top of where the gun
/// points, whether there is a host between the player and the dark.
/// </summary>
internal static class VirusHud
{
    private const int W = Config.InternalWidth;
    private const int H = Config.InternalHeight;
    private const int Cx = W / 2;
    private const int Cy = H / 2;

    /// <summary>Where the dashboard's top strip ends — everything below is ours.</summary>
    private const int StripH = 40;

    private static readonly Color Ink = new(196, 216, 224, 220);
    private static readonly Color Dim = new(120, 140, 148, 150);
    private static readonly Color Host = new(226, 96, 178, 235);  // the mote's magenta
    private static readonly Color Bad = new(220, 70, 70, 235);

    public static void DrawOverlay(World.World world, VirusRig v, PlayerTank p)
    {
        DrawDecayMeter(v);
        DrawCrosshair(v, p);
        DrawState(v, (float)Raylib.GetTime());
    }

    // --- The DECAY meter ----------------------------------------------------------
    // Down the right-hand edge, where the SPIDER's lance charge lives — for the same reason.
    // It is the one gauge the player has to watch continuously while it drains, so it wants
    // to be tall, off to the side and unmissable rather than another stub in a row of vitals.

    private const int MeterX = W - 12;
    private const int MeterW = 8;
    private const int MeterTop = StripH + 24;
    private const int MeterBottom = H - 40;

    /// <summary>
    /// The host's integrity, 0 (about to burst) .. full (just taken), as a bar that empties
    /// from the top down. Its fill rides from the mote's magenta at full toward a blown-out
    /// red as it runs low, so a nearly-spent host reads as "this is about to come out of you"
    /// rather than as a bar that has merely got short. Empty and dark whenever there is no
    /// host, which is itself the readout that says you are exposed.
    /// </summary>
    private static void DrawDecayMeter(VirusRig v)
    {
        int barH = MeterBottom - MeterTop;

        Raylib.DrawRectangle(MeterX, MeterTop, MeterW, barH, new Color(20, 10, 20, 220));

        // The label sits centred a few pixels left of the meter's own axis: it is four
        // glyphs wide and the meter hugs the frame's right edge, so centring it on the bar
        // itself runs the last glyph off the screen. It also names what is being worn —
        // wearing the Crab-Core is worth being reminded of.
        int labelX = MeterX + MeterW / 2 - 6;
        string label = v.HostKind switch
        {
            Entities.VirusHost.Crab => "CRAB",
            Entities.VirusHost.Maw => "MAW",
            _ => "HOST",
        };

        if (v.Hosted)
        {
            float f = Math.Clamp(v.Decay, 0f, 1f);
            int filled = (int)MathF.Round(barH * f);
            if (filled > 0)
            {
                Color col = Lerp(Bad, Host, f);
                Raylib.DrawRectangle(MeterX, MeterBottom - filled, MeterW, filled, col);
            }

            int pct = (int)MathF.Round(v.Decay * 100f);
            PixelFont.DrawCentered(label, labelX, MeterTop - 9, 1, Host);
            PixelFont.DrawCentered(pct.ToString(), labelX, MeterBottom + 4, 1,
                f < 0.34f ? Bad : Ink);
        }
        else
        {
            // No host: a struck-through empty well, so the missing gauge is a state and not
            // a bar someone forgot to fill.
            PixelFont.DrawCentered(label, labelX, MeterTop - 9, 1, Dim);
            Raylib.DrawLine(MeterX, MeterTop + barH / 2, MeterX + MeterW, MeterTop + barH / 2, Bad);
        }

        Raylib.DrawRectangleLines(MeterX, MeterTop, MeterW, barH, Scale(Palette.HudChrome, 0.5f));
    }

    // --- The crosshair ------------------------------------------------------------

    /// <summary>
    /// A centre mark that says the one binary this chassis lives on: whether there is a host
    /// between the player and death. Hosted, it is a tight armed reticle in the mote's own
    /// colour; exposed, it opens up and reddens — a warning worn at the exact spot the player
    /// is already looking. Dimmed with the magazine empty, like every other sight in the game.
    /// </summary>
    private static void DrawCrosshair(VirusRig v, PlayerTank p)
    {
        bool dry = p.Ammo <= 0;
        Color col = v.Hosted ? (dry ? Scale(Host, 0.5f) : Host)
                             : (dry ? Scale(Bad, 0.5f) : Bad);

        Raylib.DrawRectangle(Cx, Cy, 1, 1, col);

        if (v.Hosted)
        {
            // Armed and armoured: four ticks drawn in close and tight.
            const int gap = 4, len = 3;
            Raylib.DrawRectangle(Cx - gap - len, Cy, len, 1, col);
            Raylib.DrawRectangle(Cx + gap + 1, Cy, len, 1, col);
            Raylib.DrawRectangle(Cx, Cy - gap - len, 1, len, col);
            Raylib.DrawRectangle(Cx, Cy + gap + 1, 1, len, col);
            return;
        }

        // Exposed: the same ticks pushed wide open, so the naked state reads at a glance as a
        // sight that has come apart.
        const int wide = 7, seg = 2;
        Raylib.DrawRectangle(Cx - wide - seg, Cy, seg, 1, col);
        Raylib.DrawRectangle(Cx + wide + 1, Cy, seg, 1, col);
        Raylib.DrawRectangle(Cx, Cy - wide - seg, 1, seg, col);
        Raylib.DrawRectangle(Cx, Cy + wide + 1, 1, seg, col);
    }

    // --- The state shout ----------------------------------------------------------

    /// <summary>
    /// The one line the class most needs to put in front of the player, placed below the
    /// crosshair so it never covers what they are aiming at. Exposed, it flashes the danger
    /// and says what to do about it; hosted and failing, it says the body is nearly gone and
    /// points at the overload. Hosted and healthy, it says nothing at all — the meter has it.
    /// </summary>
    private static void DrawState(VirusRig v, float elapsed)
    {
        if (!v.Hosted)
        {
            if (v.Withering)
            {
                // Past the grace and coming apart. Flashed at the withering's own bite
                // rate, so the sign and the shield bar are counting the same clock.
                if (((int)(elapsed * 4f) & 1) == 0) return;
                PixelFont.DrawCentered("WITHERING", Cx, Cy + 22, 2, Bad);
                PixelFont.DrawCentered("FIND A BODY NOW", Cx, Cy + 40, 1, Bad);
                return;
            }

            // Inside the grace: the state, the instruction, and — once it is short —
            // the countdown, so the first bite is never a surprise.
            if (((int)(elapsed * 4f) & 1) == 0) return;
            PixelFont.DrawCentered("EXPOSED", Cx, Cy + 22, 2, Bad);
            PixelFont.DrawCentered("FLY INTO A BODY TO WEAR IT", Cx, Cy + 40, 1, Ink);
            if (v.GraceRemaining < 10f)
                PixelFont.DrawCentered($"WITHER IN {(int)MathF.Ceiling(v.GraceRemaining)}",
                    Cx, Cy + 52, 1, Bad);
            return;
        }

        if (v.Decay < 0.34f)
        {
            if (((int)(elapsed * 4f) & 1) == 0) return;
            PixelFont.DrawCentered("HOST FAILING", Cx, Cy + 22, 2, Host);
            PixelFont.DrawCentered("OVERLOAD OR HOP", Cx, Cy + 40, 1, Bad);
        }
    }

    // --- helpers (mirror HudRenderer's) -------------------------------------------

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
