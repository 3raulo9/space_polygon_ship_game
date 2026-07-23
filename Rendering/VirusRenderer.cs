using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.Entities;

namespace VoidTanks.Rendering;

/// <summary>
/// The VIRUS's screen effects — the flat pass over the finished 3D frame that carries the
/// one thing the class is about and the HUD can only put into words: which of its two states
/// you are in, and how much of it is left.
///
/// The whole run is a swing between wearing a body and being a naked mote, and each has its
/// own look because each is its own kind of danger:
/// <list type="bullet">
/// <item><b>Hosted</b>, the infection you are is visibly eating the body you are in — veins
/// creep in from the edges of the frame, thicker as the <see cref="VirusRig.Decay"/> meter
/// empties, and once the host is nearly spent the picture starts tearing apart in digital
/// bands. It is a countdown you can see without reading a number.</item>
/// <item><b>Exposed</b>, the frame breathes a cold warning red and fills with corruption
/// static — the picture itself saying you have no armour and every hit is most of a life,
/// which is exactly the state the whole class is built around escaping.</item>
/// </list>
///
/// There are no shaders anywhere in this game, so every one of these is built out of
/// rectangles and lines. At 320×240 that is not a compromise — it is the medium.
/// </summary>
internal static class VirusRenderer
{
    private const int W = Config.InternalWidth;
    private const int H = Config.InternalHeight;

    public static void DrawScreenEffects(World.World world, float elapsed)
    {
        if (world.Player.Virus is not { } v) return;

        if (v.Hosted)
        {
            DrawHostVeins(v.Corruption, elapsed);
            // Past the two-thirds mark the host is visibly failing: the frame starts to
            // tear. Scaled so it is a flicker at the threshold and a seizure at the end.
            if (v.Decay < GlitchThreshold)
                DrawGlitch(1f - v.Decay / GlitchThreshold, elapsed);
        }
        else
        {
            DrawExposed(elapsed);
        }
    }

    /// <summary>Where, in the decay meter, the host starts visibly tearing apart.</summary>
    private const float GlitchThreshold = 0.34f;

    // --- Hosted: the body rotting under you ---------------------------------------

    /// <summary>
    /// The infection creeping in from the edges as the host decays. Concentric frames in the
    /// mote's own magenta, rising in reach and alpha as the meter empties — the same "closing
    /// in" language the fish's murk uses, in the colour that means corruption rather than
    /// water. A player never has to glance at the gauge to know a host is nearly spent; the
    /// frame has been telling them for seconds.
    /// </summary>
    private static void DrawHostVeins(float corruption, float elapsed)
    {
        if (corruption <= 0.01f) return;

        const int bands = 9;
        for (int i = 0; i < bands; i++)
        {
            int inset = i * 4;
            int alpha = (int)(corruption * (bands - i) * 4.2f);
            if (alpha <= 0) continue;
            var col = new Color(Palette.NeonMagenta.R, Palette.NeonMagenta.G, Palette.NeonMagenta.B,
                Math.Min(alpha, 200));
            Raylib.DrawRectangleLines(inset, inset, W - inset * 2, H - inset * 2, col);
        }

        // A handful of filaments reaching in from the border, deterministic so they crawl
        // rather than boil. Longer and more of them the worse the rot.
        int veins = (int)(4 + corruption * 10);
        for (int i = 0; i < veins; i++)
        {
            int hash = (i * 2654435761u).GetHashCode();
            bool horizontal = (hash & 1) == 0;
            float reach = (0.06f + 0.20f * corruption) * (0.6f + 0.4f * MathF.Sin(elapsed * 2f + i));

            var col = new Color(226, 96, 178, (int)(70 + 120 * corruption));
            if (horizontal)
            {
                int y = (int)(((uint)hash >> 6) % (uint)H);
                int len = (int)(W * reach);
                bool left = (hash & 2) == 0;
                Raylib.DrawRectangle(left ? 0 : W - len, y, len, 1, col);
            }
            else
            {
                int x = (int)(((uint)hash >> 6) % (uint)W);
                int len = (int)(H * reach);
                bool top = (hash & 2) == 0;
                Raylib.DrawRectangle(x, top ? 0 : H - len, 1, len, col);
            }
        }
    }

    /// <summary>
    /// The host tearing apart in its last third: bands of the frame slam sideways and flash,
    /// the picture glitching like corrupted signal. <paramref name="amount"/> (0..1) is how
    /// far past the threshold the decay has gone, so this ramps from a stutter to a fit right
    /// as the body is about to burst and spit the player out.
    /// </summary>
    private static void DrawGlitch(float amount, float elapsed)
    {
        int bars = (int)(2 + amount * 6);
        int tick = (int)(elapsed * 24f);

        for (int i = 0; i < bars; i++)
        {
            int hash = ((i + tick) * 40503).GetHashCode();
            int y = (int)(((uint)hash >> 4) % (uint)H);
            int th = 1 + ((hash >> 9) & 3);
            // A slab of glitch: a dark tear with a hot magenta edge, thrown a few pixels off.
            int shove = (int)((((hash >> 12) & 15) - 7) * amount);
            Raylib.DrawRectangle(shove, y, W, th, new Color(10, 4, 12, (int)(150 * amount)));
            Raylib.DrawRectangle(shove, y, W, 1,
                new Color(Palette.NeonRed.R, Palette.NeonRed.G, Palette.NeonRed.B, (int)(180 * amount)));
        }
    }

    // --- Exposed: the naked mote --------------------------------------------------

    /// <summary>
    /// The mote's own state: a cold red pulse around the frame and a scatter of corruption
    /// static, the picture saying you are code with no armour and the only fix is to reach a
    /// body. Deliberately kept survivable to look at — the player has to be able to fight and
    /// fly through it — but never comfortable, because being a mote never is.
    /// </summary>
    private static void DrawExposed(float elapsed)
    {
        float pulse = 0.55f + 0.45f * MathF.Sin(elapsed * 3.4f);

        const int bands = 8;
        for (int i = 0; i < bands; i++)
        {
            int inset = i * 4;
            int alpha = (int)((bands - i) * 3.0f * pulse);
            if (alpha <= 0) continue;
            var col = new Color(Palette.NeonRed.R, Palette.NeonRed.G, Palette.NeonRed.B,
                Math.Min(alpha, 150));
            Raylib.DrawRectangleLines(inset, inset, W - inset * 2, H - inset * 2, col);
        }

        // Corruption static: sparse, deterministic specks scrolling past, so the exposed
        // frame reads as unstable signal rather than a clean picture with a red border.
        int motes = 26;
        int scroll = (int)(elapsed * 90f);
        for (int i = 0; i < motes; i++)
        {
            int hash = (i * 374761393).GetHashCode();
            int x = (int)((((uint)hash >> 3) + (uint)scroll) % (uint)W);
            int y = (int)(((uint)hash >> 15) % (uint)H);
            var col = new Color(255, 120, 140, (int)(60 + 60 * pulse));
            Raylib.DrawRectangle(x, y, 1, 1, col);
        }
    }
}
