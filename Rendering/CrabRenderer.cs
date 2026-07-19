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

    /// <summary>
    /// Draws the rig. <paramref name="death"/> ramps 0→1 as the boss glitches apart:
    /// at 0 it's the normal posed crab; above 0 the parts fling outward and up, the
    /// whole thing strobes between glitch colours and flickers frames out, and the
    /// neon core erupts skyward — the "all its body parts fly away and explode with a
    /// glitch over it" death.
    /// </summary>
    public void Draw(CrabPose pose, Vector2 position, float heading, Vector3 cameraPos, float death = 0f)
    {
        // Showcase phases nudge the whole body; the live boss leaves this zero.
        Vector2 bodyPos = position + pose.SlideOffset;
        float baseY = CrabRig.BodyHeight;
        float fling = death * death;    // ease-in: a beat of shudder, then it lets go

        // Lower base: grinds down into the grid as it tears.
        if (!Flicker(death))
        {
            Vector3 j = Jitter(death, 1.2f) + new Vector3(0f, -fling * 3f, 0f);
            _bodyLower.Draw(new Vector2(bodyPos.X + j.X, bodyPos.Y + j.Z), heading,
                baseY * Scale + j.Y, cameraPos, Scale, DeathTint(Palette.CrabChassis, death));
        }

        // Upper carapace lid: rises by the clamp amount in life; on death it blows
        // straight up and tumbles off its hinge.
        float lift = pose.ClawOpen * LidLift;
        if (!Flicker(death))
        {
            Vector3 j = Jitter(death, 1.6f) + new Vector3(0f, fling * 12f, 0f);
            float spin = heading + fling * 6f;
            _bodyUpper.Draw(new Vector2(bodyPos.X + j.X, bodyPos.Y + j.Z), spin,
                (baseY + lift) * Scale + j.Y, cameraPos, Scale, DeathTint(Palette.CrabChassis, death));
        }

        // Neon gem in the well, spinning and tinted this frame's colour — on death it
        // erupts upward, spinning wild and strobing red/white/magenta.
        if (!Flicker(death))
        {
            Vector3 j = Jitter(death, 2.2f) + new Vector3(0f, fling * 8f, 0f);
            Color coreTint = death > 0f ? DeathTint(Palette.NeonRed, death) : pose.CoreColor;
            DrawPart(_core, new Vector3(0f, baseY + CoreY, 0f), pose.CoreSpin + fling * 20f,
                bodyPos, heading, cameraPos, coreTint, j);
        }

        // Six legs, bobbing out of phase for the skitter; on death each snaps off and
        // cartwheels outward from the body.
        foreach (var leg in CrabRig.Legs)
        {
            if (Flicker(death)) continue;
            float phase = pose.LegPhase + leg.PhaseOffset;
            float footLift = MathF.Max(0f, MathF.Sin(phase)) * CrabRig.LegBob;
            float sweep = MathF.Cos(phase) * 0.12f;
            var mount = new Vector3(leg.Mount.X, baseY + leg.Mount.Y + footLift, leg.Mount.Z);

            Vector3 offset = Jitter(death, 1.4f);
            if (death > 0f)
            {
                Vector2 outward = LegOutward(leg, heading);
                offset += new Vector3(outward.X * fling * 16f, fling * 6f, outward.Y * fling * 16f);
            }
            DrawPart(_leg, mount, leg.BaseYaw + sweep + fling * 4f, bodyPos, heading, cameraPos,
                DeathTint(Palette.CrabChassis, death), offset);
        }
    }

    /// <summary>
    /// Draws one rigged part: its local mount is scaled, rotated by the body's
    /// heading and translated onto the body; the part then turns on its own hinge by
    /// <paramref name="localYaw"/>. Height rides above the plane. A world-space
    /// <paramref name="offset"/> (used by the death glitch) shoves the part off its rig.
    /// </summary>
    private void DrawPart(PolyMesh mesh, Vector3 localMount, float localYaw,
        Vector2 bodyPos, float heading, Vector3 cameraPos, Color? tint = null, Vector3 offset = default)
    {
        Vector3 o = localMount * Scale;
        float cos = MathF.Cos(heading), sin = MathF.Sin(heading);
        float rx = o.X * cos + o.Z * sin;
        float rz = -o.X * sin + o.Z * cos;
        var worldXZ = new Vector2(bodyPos.X + rx + offset.X, bodyPos.Y + rz + offset.Z);
        mesh.Draw(worldXZ, heading + localYaw, o.Y + offset.Y, cameraPos, Scale, tint);
    }

    // --- Death-glitch helpers -------------------------------------------------
    // All no-ops at death<=0, so a living boss draws exactly as before. They pull
    // fresh randomness each call, so the tearing shifts every frame — the datamosh
    // jitter of an old machine coming apart.

    /// <summary>A world-space glitch shove, growing with death and reseeded per frame.</summary>
    private static Vector3 Jitter(float death, float mag)
    {
        if (death <= 0f) return Vector3.Zero;
        var r = Random.Shared;
        float a = death * mag;
        return new Vector3(
            (r.NextSingle() * 2f - 1f) * a,
            (r.NextSingle() * 2f - 1f) * a,
            (r.NextSingle() * 2f - 1f) * a);
    }

    /// <summary>True on frames a part should blink out entirely — up to a third of
    /// frames near the end, the hard on/off stutter of a corrupted signal.</summary>
    private static bool Flicker(float death)
        => death > 0f && Random.Shared.NextSingle() < death * 0.33f;

    /// <summary>Slams a part's colour between hot glitch tones as it tears apart.</summary>
    private static Color DeathTint(Color baseColor, float death)
    {
        if (death <= 0f) return baseColor;
        if (Random.Shared.NextSingle() < death * 0.6f)
            return Random.Shared.Next(3) switch
            {
                0 => Palette.NeonRed,
                1 => Color.White,
                _ => Palette.NeonMagenta,
            };
        return baseColor;
    }

    /// <summary>The world-plane outward bearing of a leg from the body centre — the
    /// direction it flies when it snaps off in the death glitch.</summary>
    private static Vector2 LegOutward(in CrabRig.Leg leg, float heading)
    {
        float cos = MathF.Cos(heading), sin = MathF.Sin(heading);
        float x = leg.Mount.X, z = leg.Mount.Z;
        var v = new Vector2(x * cos + z * sin, -x * sin + z * cos);
        float len = v.Length();
        return len > 1e-4f ? v / len : new Vector2(1f, 0f);
    }
}
