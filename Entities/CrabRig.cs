using System.Numerics;

namespace VoidTanks.Entities;

/// <summary>
/// The shared physical layout of the Crab-Core boss — the one source of truth for
/// how big it is, where its legs bolt on, and where each foot plants. Both the
/// renderer (to draw the rig) and the entity (to spawn dust where a foot strikes
/// the floor) read from here, so the visible legs and the particle contacts can
/// never drift apart.
///
/// Local convention matches the meshes: a leg is built pointing +X ("outward"),
/// its shoulder at the origin, rising to a high knee and back down to a foot well
/// below the body — the tall, raised-knee spider stance of the reference. The body
/// rides <see cref="BodyHeight"/> units up so those feet reach the grid.
/// </summary>
public static class CrabRig
{
    /// <summary>Overall size multiplier — the boss towers, ~10× a hunter tank.</summary>
    public const float Scale = 2.4f;

    // Leg segment endpoints in the leg's own +X frame (shoulder at origin).
    public static readonly Vector3 Knee = new(2.4f, 3.0f, 0f);   // up and out
    public static readonly Vector3 Foot = new(4.6f, -3.6f, 0f);  // far out, on the floor

    /// <summary>How far the body origin sits above the feet, so the feet touch y=0.</summary>
    public const float BodyHeight = 2.3f;

    /// <summary>The neon core gem's base height in the body's local frame (pre-scale)
    /// — it stands <c>CoreLocalY</c> up in the well. Mirrors the renderer's placement
    /// so the entity's hit-test on the core lines up with where it's drawn.</summary>
    public const float CoreLocalY = BodyHeight + 0.5f;

    /// <summary>World-space height of the core gem's base, once <see cref="Scale"/>
    /// is applied — the anchor for the "shoot the red core mid-jump" strike zone.</summary>
    public static float CoreWorldY => CoreLocalY * Scale;

    /// <summary>Peak lift of a skittering foot (raw units, before Scale).</summary>
    public const float LegBob = 0.9f;

    /// <summary>One leg mount: where it bolts on, which way it points, and its gait phase.</summary>
    public readonly record struct Leg(Vector3 Mount, float BaseYaw, float PhaseOffset);

    // Six legs, three a side, splayed fore/aft. Left legs mirror the right by
    // pointing the opposite way (BaseYaw ≈ π). Phase offsets alternate so the gait
    // reads as a scuttling tripod, not a march.
    public static readonly Leg[] Legs =
    {
        new(new Vector3( 1.7f, 1.3f,  1.9f), -0.5f,          0f),        // right front
        new(new Vector3( 2.0f, 1.3f,  0.0f),  0.0f,          MathF.PI),  // right mid
        new(new Vector3( 1.7f, 1.3f, -1.9f),  0.5f,          0f),        // right back
        new(new Vector3(-1.7f, 1.3f,  1.9f),  MathF.PI + 0.5f, MathF.PI),// left front
        new(new Vector3(-2.0f, 1.3f,  0.0f),  MathF.PI,        0f),      // left mid
        new(new Vector3(-1.7f, 1.3f, -1.9f),  MathF.PI - 0.5f, MathF.PI),// left back
    };

    /// <summary>
    /// World XZ where a given leg's foot plants, given the body's position and
    /// heading. Mirrors exactly how the renderer places the leg: rotate the mount
    /// into the body frame, then rotate the foot tip by the leg's own yaw — both by
    /// the shared <see cref="Scale"/>.
    /// </summary>
    public static Vector2 FootWorldXZ(in Leg leg, Vector2 bodyPos, float heading)
    {
        Vector2 mount = Rotate(new Vector2(leg.Mount.X, leg.Mount.Z) * Scale, heading);
        Vector2 foot = Rotate(new Vector2(Foot.X, 0f) * Scale, heading + leg.BaseYaw);
        return bodyPos + mount + foot;
    }

    /// <summary>Rotates a planar (x,z) vector by a heading, matching PolyMesh.Draw.</summary>
    private static Vector2 Rotate(Vector2 v, float h)
    {
        float c = MathF.Cos(h), s = MathF.Sin(h);
        return new Vector2(v.X * c + v.Y * s, -v.X * s + v.Y * c);
    }
}
