using System.Numerics;
using Raylib_cs;
using Unrendered.Rendering;

namespace DimensionHopper;

/// <summary>
/// The gun itself, in UNRENDERED's own flat-shaded polygons: a pale body with a dark grip, a
/// glowing green vial along the top between two steel caps, a dial on the right flank with a
/// green readout, and an emitter at the front between two prongs that glows when it fires.
/// Built in metres in its own frame: +Z out of the muzzle, +Y up, +X to the right.
/// </summary>
public static class Gun
{
    public static readonly Color Body = new(222, 226, 230, 255);
    public static readonly Color Trim = new(150, 156, 162, 255);
    public static readonly Color Grip = new(52, 56, 62, 255);
    public static readonly Color Steel = new(96, 104, 112, 255);
    public static readonly Color Glow = new(96, 255, 120, 255);
    public static readonly Color Fluid = new(60, 220, 96, 255);

    private static PolyMesh? _mesh, _muzzle;

    public static PolyMesh Mesh => _mesh ??= Build();

    /// <summary>The emitter's tip on its own, so it can be drawn lit when the gun fires.</summary>
    public static PolyMesh Muzzle => _muzzle ??= new PolyMesh().AddBoxSpan(Glow, -0.035f, 0.035f, 0.22f, 0.236f, 0.025f, 0.095f);

    private static PolyMesh Build()
    {
        var m = new PolyMesh();
        m.AddBoxSpan(Body, -0.06f, 0.06f, -0.18f, 0.16f, 0f, 0.12f);          // body
        m.AddBoxSpan(Trim, -0.066f, 0.066f, -0.205f, -0.18f, -0.006f, 0.126f); // back cap
        m.AddBoxSpan(Trim, -0.062f, 0.062f, 0.05f, 0.075f, -0.004f, 0.124f);   // band
        m.AddBoxSpan(Grip, -0.045f, 0.045f, -0.15f, -0.06f, -0.17f, 0f);       // grip
        m.AddBoxSpan(Grip, -0.012f, 0.012f, -0.06f, -0.03f, -0.07f, 0f);       // trigger
        m.AddBoxSpan(Steel, -0.05f, 0.05f, 0.16f, 0.22f, 0.01f, 0.11f);        // emitter housing
        m.AddBoxSpan(Steel, -0.01f, 0.01f, 0.2f, 0.3f, 0.1f, 0.12f);           // top prong
        m.AddBoxSpan(Steel, -0.01f, 0.01f, 0.2f, 0.3f, 0f, 0.02f);             // bottom prong
        m.AddBoxSpan(Steel, -0.04f, 0.04f, -0.12f, -0.095f, 0.12f, 0.2f);      // vial cap, back
        m.AddBoxSpan(Steel, -0.04f, 0.04f, 0.045f, 0.07f, 0.12f, 0.2f);        // vial cap, front
        m.AddBoxSpan(Fluid, -0.03f, 0.03f, -0.095f, 0.045f, 0.125f, 0.192f);   // the vial
        m.AddBoxSpan(Glow, -0.012f, 0.012f, -0.09f, 0.04f, 0.185f, 0.196f);    // its bright core
        m.AddBoxSpan(Grip, 0.06f, 0.078f, -0.06f, 0.01f, 0.03f, 0.095f);       // dial
        m.AddBoxSpan(Glow, 0.078f, 0.082f, -0.05f, 0f, 0.045f, 0.08f);         // dial readout
        return m;
    }

    /// <summary>
    /// In the hand, low on the right of the view, along where the eye looks. <paramref name="kick"/>
    /// is 1 the instant it fires and falls to 0: the gun jumps back and tips up.
    /// </summary>
    public static void DrawHeld(Camera3D cam, float kick, float lit, float raise)
    {
        Vector3 fwd = Vector3.Normalize(cam.Target - cam.Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        if (float.IsNaN(right.X)) right = Vector3.UnitX;
        Vector3 up = Vector3.Cross(right, fwd);

        // Tipped up by the kick, about the gun's own X.
        float tip = kick * 0.35f;
        Vector3 f2 = Vector3.Normalize(fwd * MathF.Cos(tip) + up * MathF.Sin(tip));
        Vector3 u2 = Vector3.Cross(right, f2);

        float bob = MathF.Sin((float)Raylib.GetTime() * 2.1f) * 0.006f;
        Vector3 o = cam.Position + fwd * (0.62f - kick * 0.08f) + right * 0.24f - up * (0.22f + (1f - raise) * 0.35f - bob);
        const float S = 0.85f;
        var world = new Matrix4x4(
            right.X * S, right.Y * S, right.Z * S, 0,
            u2.X * S, u2.Y * S, u2.Z * S, 0,
            f2.X * S, f2.Y * S, f2.Z * S, 0,
            o.X, o.Y, o.Z, 1);
        Rlgl.DrawRenderBatchActive();
        Mesh.Draw(world, cam.Position);
        Muzzle.Draw(world, cam.Position, lit > 0.01f ? Color.White : Glow);
        Rlgl.DrawRenderBatchActive();
    }
}
