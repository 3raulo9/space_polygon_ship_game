using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.Entities;

namespace VoidTanks.Rendering;

/// <summary>
/// Draws the Crab-Core boss from its posed parts. It's a rig, not one baked mesh:
/// a lower base tier, an upper carapace lid, a spinning neon gem in the well, and
/// six long spider legs — each a separate <see cref="PolyMesh"/> placed per frame
/// from a <see cref="CrabPose"/> and the shared <see cref="CrabRig"/> layout. The
/// whole thing rides <see cref="CrabRig.BodyHeight"/> up so the legs plant on the
/// grid; the lid lifts and slams during the clamp.
/// </summary>
public sealed class CrabRenderer
{
    private readonly PolyMesh _bodyUpper = Meshes.CrabBodyUpper(Palette.CrabChassis);
    private readonly PolyMesh _bodyLower = Meshes.CrabBodyLower(Palette.CrabChassis);
    private readonly PolyMesh _core = Meshes.CrabCoreGem(Color.White); // tinted per frame
    private readonly PolyMesh _leg = Meshes.CrabLeg(Palette.CrabChassis);

    private const float CoreY = 0.5f;   // gem base height on the lower tier
    private const float LidLift = 1.1f; // how far the carapace lid rises when open

    public float Scale = CrabRig.Scale;

    public void Draw(CrabPose pose, Vector2 position, float heading, Vector3 cameraPos)
    {
        // Showcase phases nudge the whole body; the live boss leaves this zero.
        Vector2 bodyPos = position + pose.SlideOffset;
        float baseY = CrabRig.BodyHeight;

        // Lower base sits still; the lid rises by the clamp amount and slams back.
        _bodyLower.Draw(bodyPos, heading, baseY * Scale, cameraPos, Scale);
        float lift = pose.ClawOpen * LidLift;
        _bodyUpper.Draw(bodyPos, heading, (baseY + lift) * Scale, cameraPos, Scale);

        // Neon gem standing in the well, spinning, tinted this frame's colour.
        DrawPart(_core, new Vector3(0f, baseY + CoreY, 0f), pose.CoreSpin,
            bodyPos, heading, cameraPos, pose.CoreColor);

        // Six legs, bobbing out of phase so the gait reads as a skitter.
        foreach (var leg in CrabRig.Legs)
        {
            float phase = pose.LegPhase + leg.PhaseOffset;
            float footLift = MathF.Max(0f, MathF.Sin(phase)) * CrabRig.LegBob;
            float sweep = MathF.Cos(phase) * 0.12f;
            var mount = new Vector3(leg.Mount.X, baseY + leg.Mount.Y + footLift, leg.Mount.Z);
            DrawPart(_leg, mount, leg.BaseYaw + sweep, bodyPos, heading, cameraPos);
        }
    }

    /// <summary>
    /// Draws one rigged part: its local mount is scaled, rotated by the body's
    /// heading and translated onto the body; the part then turns on its own hinge by
    /// <paramref name="localYaw"/>. Height rides above the plane.
    /// </summary>
    private void DrawPart(PolyMesh mesh, Vector3 localMount, float localYaw,
        Vector2 bodyPos, float heading, Vector3 cameraPos, Color? tint = null)
    {
        Vector3 o = localMount * Scale;
        float cos = MathF.Cos(heading), sin = MathF.Sin(heading);
        float rx = o.X * cos + o.Z * sin;
        float rz = -o.X * sin + o.Z * cos;
        var worldXZ = new Vector2(bodyPos.X + rx, bodyPos.Y + rz);
        mesh.Draw(worldXZ, heading + localYaw, o.Y, cameraPos, Scale, tint);
    }
}
