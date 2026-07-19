using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;

namespace VoidTanks.Rendering;

/// <summary>
/// The sky above the horizon: a cold bluish glow that is brightest right at the
/// ground line and dies out into the void overhead, with a field of faint stars
/// drifting slowly across it. Drawn flat in 2D, *behind* the 3D floor pass, so
/// the checker plane paints over the lower half and only the true sky survives.
/// The horizon line is found by projecting a far ground point through the live
/// camera, so it tracks whatever pitch the caller is using.
/// </summary>
public static class SkyRenderer
{
    private const int StarCount = 70;
    private const float DriftSpeed = 1.6f; // pixels/sec the field slides sideways

    private const int Levels = 6; // quantisation steps for the dithered gradient

    // 4x4 ordered-dither (Bayer) thresholds, flattened row-major.
    private static readonly int[] Bayer =
    {
        0, 8, 2, 10,
        12, 4, 14, 6,
        3, 11, 1, 9,
        15, 7, 13, 5,
    };

    // Each star: x,y as fractions of the (width x horizon) sky box plus a
    // brightness so the field has depth. Fixed at load; the whole field drifts.
    private static readonly (float X, float Y, float B)[] _stars = BuildStars();

    private static (float, float, float)[] BuildStars()
    {
        var rng = new Random(1337);
        var s = new (float, float, float)[StarCount];
        for (int i = 0; i < StarCount; i++)
        {
            // Bias stars toward the top so the horizon band stays clean-ish.
            float y = (float)(rng.NextDouble() * rng.NextDouble());
            s[i] = ((float)rng.NextDouble(), y, 0.35f + (float)rng.NextDouble() * 0.65f);
        }
        return s;
    }

    /// <summary>
    /// Paints the gradient and stars into the current (2D) target. Call after
    /// ClearBackground and before the 3D floor pass.
    /// </summary>
    public static void Draw(Camera3D camera, float elapsed)
    {
        int w = Config.InternalWidth;
        int h = Config.InternalHeight;

        // Find the horizon in screen space: a ground-plane point far along the
        // camera's flat forward, held at the camera's own height so it lands
        // exactly on the horizon line.
        var dir = camera.Target - camera.Position;
        var flat = new Vector3(dir.X, 0f, dir.Z);
        if (flat.LengthSquared() < 1e-6f) flat = new Vector3(0f, 0f, 1f);
        flat = Vector3.Normalize(flat);
        var far = camera.Position + flat * 10000f;
        var scr = Raylib.GetWorldToScreenEx(far, camera, w, h);

        int horizonY = (int)MathF.Round(scr.Y);
        horizonY = Math.Clamp(horizonY, 0, h);

        // Purple-magenta glow at the horizon fading up into pure black. The ramp
        // runs black -> dark purple -> magenta and is quantised into a handful of
        // levels, then ordered-dithered per pixel so the band boundaries break up
        // into the speckled 8-bit texture instead of hard lines.
        if (horizonY > 0)
        {
            for (int y = 0; y < horizonY; y++)
            {
                // f: 0 up top .. 1 at the ground line. Powered so the black keeps
                // the top of the sky and the purple gathers toward the horizon.
                float f = (float)y / horizonY;
                float t = MathF.Pow(f, 1.6f);

                // Quantise into Levels steps and dither the leftover fraction with a
                // 4x4 Bayer matrix so each row scatters between two adjacent shades.
                float level = t * Levels;
                int lo = (int)level;
                float frac = level - lo;
                for (int x = 0; x < w; x++)
                {
                    float thr = (Bayer[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;
                    float tq = (frac > thr ? lo + 1 : lo) / (float)Levels;
                    Color cq = tq < 0.5f
                        ? GridRenderer.LerpColor(Palette.SkyTop, Palette.SkyMid, tq * 2f)
                        : GridRenderer.LerpColor(Palette.SkyMid, Palette.SkyHorizon, (tq - 0.5f) * 2f);
                    Raylib.DrawPixel(x, y, cq);
                }
            }
        }

        // Carry the bright band all the way down past the horizon so the sky is
        // solid everywhere the floor doesn't cover it — jump or pitch up and the
        // glow is still there instead of a void gap.
        if (horizonY < h)
            Raylib.DrawRectangle(0, horizonY, w, h - horizonY, Palette.SkyHorizon);

        if (horizonY <= 0) return; // no sky band left to star

        // Stars drift slowly and wrap; brightness lerps the horizon glow toward a
        // pale point so they read as cold specks, dimmer low near the glow.
        float drift = elapsed * DriftSpeed;
        for (int i = 0; i < _stars.Length; i++)
        {
            var (sx, sy, sb) = _stars[i];
            // Per-star speed variation via brightness gives a hint of parallax.
            float px = sx * w + drift * (0.5f + sb);
            px = Mod(px, w);
            int y = (int)(sy * horizonY);

            // Fade stars out as they approach the bright horizon glow.
            float glow = (float)y / horizonY;      // 0 top .. 1 horizon
            float a = sb * glow;                    // brightest high up
            byte val = (byte)(150 + (int)(105 * sb));
            var c = new Color(val, val, (byte)Math.Min(255, val + 20), (byte)(255 * a));
            Raylib.DrawPixel((int)px, y, c);
        }
    }

    private static float Mod(float a, float m)
    {
        float r = a % m;
        return r < 0 ? r + m : r;
    }
}
