using System.Numerics;
using Raylib_cs;
using Unrendered.Core;

namespace DimensionHopper;

/// <summary>
/// A hole the gun has torn in the world: an upright oval of green light, swirling, standing on
/// the floor and facing whoever made it. It opens in a breath, stays a minute, and shrinks
/// shut. Walk into it and you are wherever the gun was set to.
/// </summary>
public sealed class Doorway
{
    public Vector2 At;
    public float Facing;            // the way its face looks: back toward the one who opened it
    public string Destination = "";
    public double Opened;
    public double Life = 60;
    public bool Arrival;            // the far end, opened where somebody came out

    public const float HalfWidth = 1.15f, HalfHeight = 1.55f, CenterY = 1.6f;

    public float Scale(double now)
    {
        double age = now - Opened;
        double open = Math.Clamp(age / 0.45, 0, 1);
        double close = Math.Clamp((Life - age) / 0.6, 0, 1);
        return (float)(Math.Sin(open * Math.PI / 2) * close);
    }

    public bool Done(double now) => now - Opened > Life;

    /// <summary>Has this craft, at this spot, stepped into it?</summary>
    public bool Takes(Vector2 craft, double now)
    {
        if (Arrival || Scale(now) < 0.8f) return false;
        Vector2 n = new(MathF.Sin(Facing), MathF.Cos(Facing));
        Vector2 d = Torus.NearestImage(craft, At) - At;
        float across = MathF.Abs(d.X * n.Y - d.Y * n.X);
        float through = MathF.Abs(Vector2.Dot(d, n));
        return across < HalfWidth * 0.9f && through < 0.9f;
    }

    /// <summary>
    /// The swirl: rings of green from near-black at the rim to white-hot at the eye, each turned
    /// a little further than the one outside it and banded so the whole disc reads as turning.
    /// </summary>
    public void Draw(Vector3 eye, double now)
    {
        float s = Scale(now);
        if (s <= 0.01f) return;
        var at = Torus.NearestImage(At, new Vector2(eye.X, eye.Z));
        var c = new Vector3(at.X, CenterY * s + (1 - s) * 0.2f, at.Y);
        Vector3 right = new(MathF.Cos(Facing), 0, -MathF.Sin(Facing));
        Vector3 n = new(MathF.Sin(Facing), 0, MathF.Cos(Facing));
        float t = (float)now;
        const int Seg = 40, Rings = 7;
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();
        for (int r = Rings; r >= 1; r--)
        {
            float k0 = (float)(r - 1) / Rings, k1 = (float)r / Rings;
            float spin = t * (1.4f + (Rings - r) * 0.55f);
            for (int i = 0; i < Seg; i++)
            {
                float a0 = MathF.Tau * i / Seg, a1 = MathF.Tau * (i + 1) / Seg;
                float band = 0.5f + 0.5f * MathF.Sin(a0 * 3 + spin * 2.2f + r * 1.7f);
                float heat = 1f - k1;                        // brighter toward the eye
                byte g = (byte)Math.Clamp(90 + 150 * heat + 40 * band, 0, 255);
                byte rr = (byte)Math.Clamp(20 + 200 * heat * heat + 20 * band, 0, 255);
                byte b = (byte)Math.Clamp(30 + 120 * heat * heat + 25 * band, 0, 255);
                var col = new Color(rr, g, b, (byte)255);
                Vector3 P(float a, float k)
                {
                    float wob = 1f + 0.05f * MathF.Sin(a * 5 + t * 3 + r);
                    float ar = a + spin * 0.4f;
                    return c + right * (MathF.Cos(ar) * HalfWidth * k * s * wob) + Vector3.UnitY * (MathF.Sin(ar) * HalfHeight * k * s * wob);
                }
                // A hair proud of the plane on the side being looked at, ring by ring, so the
                // inner rings sit over the outer ones from either side.
                float side = Vector3.Dot(eye - c, n) >= 0 ? 1f : -1f;
                Vector3 lift = n * side * (Rings - r) * 0.004f;
                Vector3 p00 = P(a0, k0) + lift, p01 = P(a1, k0) + lift, p10 = P(a0, k1) + lift, p11 = P(a1, k1) + lift;
                Raylib.DrawTriangle3D(p00, p10, p11, col);
                Raylib.DrawTriangle3D(p00, p11, p01, col);
            }
        }
        // The rim: a bright lip of light.
        for (int i = 0; i < Seg; i++)
        {
            float a0 = MathF.Tau * i / Seg + t * 0.8f, a1 = MathF.Tau * (i + 1) / Seg + t * 0.8f;
            var p0 = c + right * (MathF.Cos(a0) * HalfWidth * s) + Vector3.UnitY * (MathF.Sin(a0) * HalfHeight * s);
            var p1 = c + right * (MathF.Cos(a1) * HalfWidth * s) + Vector3.UnitY * (MathF.Sin(a1) * HalfHeight * s);
            Raylib.DrawLine3D(p0, p1, Gun.Glow);
        }
        Rlgl.DrawRenderBatchActive();
    }
}
