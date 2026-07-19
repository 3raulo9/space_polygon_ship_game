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

        // Bright pink band at the horizon, stepping up into pure black overhead.
        // Hard bands rather than a smooth blend give the chunky 90's sky look.
        if (horizonY > 0)
        {
            const int Bands = 8;
            for (int b = 0; b < Bands; b++)
            {
                int y0 = b * horizonY / Bands;
                int y1 = (b + 1) * horizonY / Bands;
                // f: 1 at the ground line, 0 up top. Raised to a power so the
                // pink glow collapses into a thin band right on the horizon and
                // the black takes over the sky far lower down.
                float f = (y0 + y1) * 0.5f / horizonY;
                float t = MathF.Pow(f, 4f);
                Color c = GridRenderer.LerpColor(Palette.SkyTop, Palette.SkyHorizon, t);
                Raylib.DrawRectangle(0, y0, w, y1 - y0, c);
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
