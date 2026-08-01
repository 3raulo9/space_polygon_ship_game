using System.Numerics;
using Raylib_cs;
using Unrendered.Core;

namespace Unrendered.Rendering;

/// <summary>
/// The sky above the horizon: a glow that is brightest right at the ground line and dies out
/// into the void overhead, with a field of faint stars drifting slowly across it. Drawn flat
/// in 2D, *behind* the 3D floor pass, so the checker plane paints over the lower half and
/// only the true sky survives. The horizon line is found by projecting a far ground point
/// through the live camera, so it tracks whatever pitch the caller is using.
///
/// The three colours of the ramp come from the <see cref="Atmosphere"/>, which is to say from
/// whichever of the five worlds is underfoot — and on SOLUNE, from what time of day it is
/// there. That world also gets the two things no other one has: a sun and a moon, drawn as
/// hard-edged discs riding the sky at the same bearing the light is coming from.
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

        var look = Atmosphere.Look;

        // The glow at the horizon fading up into the dark overhead. The ramp runs
        // top -> mid -> horizon and is quantised into a handful of levels, then
        // ordered-dithered per pixel so the band boundaries break up into the
        // speckled 8-bit texture instead of hard lines.
        if (horizonY > 0)
        {
            for (int y = 0; y < horizonY; y++)
            {
                // f: 0 up top .. 1 at the ground line. Powered so the black keeps
                // the top of the sky and the purple gathers toward the horizon. A
                // steeper exponent pushes the glow lower and holds the darkness up
                // top, so the sky reads deeper and colder overhead.
                float f = (float)y / horizonY;
                float t = MathF.Pow(f, 6.5f);

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
                        ? GridRenderer.LerpColor(look.Top, look.Mid, tq * 2f)
                        : GridRenderer.LerpColor(look.Mid, look.Horizon, (tq - 0.5f) * 2f);
                    Raylib.DrawPixel(x, y, cq);
                }
            }
        }

        // Carry the bright band all the way down past the horizon so the sky is
        // solid everywhere the floor doesn't cover it — jump or pitch up and the
        // glow is still there instead of a void gap.
        if (horizonY < h)
            Raylib.DrawRectangle(0, horizonY, w, h - horizonY, look.Horizon);

        if (horizonY <= 0) return; // no sky band left for discs or stars

        // The two bodies, before the stars so a star never sits on top of a disc.
        DrawBody(camera, look.SunAltitude, look.SunAzimuth, SunRadius, SunColor, w, horizonY);
        DrawBody(camera, look.MoonAltitude, look.MoonAzimuth, MoonRadius, MoonColor, w, horizonY);

        // Stars drift slowly and wrap; brightness lerps the horizon glow toward a
        // pale point so they read as cold specks, dimmer low near the glow. Daylight
        // washes them out entirely — on a world with a cycle they come back as the
        // sun goes down, and on the four without one they are simply always there.
        float starlight = look.SunAltitude <= -1f ? 1f : look.Night;
        if (starlight <= 0.02f) return;

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
            float a = sb * glow * starlight;        // brightest high up, gone in daylight
            byte val = (byte)(150 + (int)(105 * sb));
            var c = new Color(val, val, (byte)Math.Min(255, val + 20), (byte)(255 * a));
            Raylib.DrawPixel((int)px, y, c);
        }
    }

    // --- The sun and the moon ---------------------------------------------------------

    // Both are drawn as low-sided polygons rather than circles, for the same reason nothing
    // else in this game is round: at 320x240 a hard-edged octagon reads as a deliberate shape
    // and a rasterised circle reads as a mistake.
    private const int Sides = 8;
    private const float SunRadius = 9f;
    private const float MoonRadius = 7f;

    // Neither is white. The sun is the one warm thing the game allows itself and it is still
    // a sickly one; the moon is the same cold grey as the HUD.
    private static readonly Color SunColor = new(226, 196, 150, 255);
    private static readonly Color MoonColor = new(176, 190, 198, 255);

    /// <summary>
    /// Puts one body in the sky at a bearing and a height. Below the horizon it is simply not
    /// drawn — there is no dipping half-disc, the world eats it whole — and behind the camera
    /// it is culled explicitly, because projecting a point that is behind the eye gives a
    /// perfectly plausible screen position on the wrong side of the view.
    /// </summary>
    private static void DrawBody(Camera3D camera, float altitude, float azimuth,
        float radius, Color color, int w, int horizonY)
    {
        if (altitude <= 0.02f) return;

        // A point on a very large dome around the eye. Altitude is the sine of the angle above
        // the horizon, so the flat component is what is left of it.
        float flatLen = MathF.Sqrt(MathF.Max(0f, 1f - altitude * altitude));
        var dir = new Vector3(MathF.Sin(azimuth) * flatLen, altitude, MathF.Cos(azimuth) * flatLen);

        var fwd = camera.Target - camera.Position;
        if (Vector3.Dot(Vector3.Normalize(fwd), dir) <= 0.05f) return; // behind us

        var scr = Raylib.GetWorldToScreenEx(camera.Position + dir * 8000f, camera,
            Config.InternalWidth, Config.InternalHeight);
        if (float.IsNaN(scr.X) || float.IsNaN(scr.Y)) return;
        if (scr.X < -radius || scr.X > w + radius) return;
        if (scr.Y > horizonY + radius) return; // sunk into the floor

        // Rotated a little off-axis so the flat of the octagon never lines up with the
        // scanlines and turns into a rectangle.
        Raylib.DrawPoly(scr, Sides, radius, 22.5f, color);
    }

    private static float Mod(float a, float m)
    {
        float r = a % m;
        return r < 0 ? r + m : r;
    }
}
