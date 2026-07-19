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
        // cartwheels outward from the body. During a seizure the two front limbs stop
        // being legs and become hands — see PoseArm.
        var legs = CrabRig.Legs;
        for (int i = 0; i < legs.Length; i++)
        {
            var leg = legs[i];
            if (Flicker(death)) continue;

            float phase = pose.LegPhase + leg.PhaseOffset;
            float footLift = MathF.Max(0f, MathF.Sin(phase)) * CrabRig.LegBob;
            float sweep = MathF.Cos(phase) * 0.12f;
            float yaw = leg.BaseYaw + sweep;

            // Front-right holds the player, front-left clubs them. Both fall back to
            // ordinary walking legs the moment their channel returns to zero.
            if (i == GrabLeg && pose.GrabArm > 0f)
                PoseArm(leg, pose.GrabArm, GripYawRight, 0f, ref yaw, ref footLift);
            else if (i == StrikeLeg && pose.StrikeArm > 0f)
                PoseStrikeArm(leg, pose.StrikeArm, ref yaw, ref footLift);

            var mount = new Vector3(leg.Mount.X, baseY + leg.Mount.Y + footLift, leg.Mount.Z);

            Vector3 offset = Jitter(death, 1.4f);
            if (death > 0f)
            {
                Vector2 outward = LegOutward(leg, heading);
                offset += new Vector3(outward.X * fling * 16f, fling * 6f, outward.Y * fling * 16f);
            }
            DrawPart(_leg, mount, yaw + fling * 4f, bodyPos, heading, cameraPos,
                DeathTint(Palette.CrabChassis, death), offset);
        }
    }

    // --- The seizure's two hands ---------------------------------------------
    // The rig has no arms — it was built as a six-legged walker. Rather than bolt on
    // limbs that would never be seen for the other 99% of the fight, the seizure
    // presses the two front legs into service: they swing up off the floor, point
    // forward, and read as hands precisely because a walking leg doing that is
    // obviously wrong. The maths is the same for both; only the target angles differ.

    private const int GrabLeg = 0;    // right front, in CrabRig.Legs order
    private const int StrikeLeg = 3;  // left front

    // A leg mesh is modelled pointing along its own +X, and PolyMesh's rotation maps
    // local +X onto world +Z at a yaw of -PI/2 — so these are the angles at which a
    // limb points straight out in front of the chassis. The two hands approach that
    // heading from opposite sides, hence the two windings: lerping the left arm
    // toward -PI/2 would take the short way round and barely move it at all.
    private const float GripYawRight = -MathF.PI / 2f;          // right limb, swinging in
    private const float GripYawLeft = 3f * MathF.PI / 2f;       // left limb, swinging across

    /// <summary>
    /// How far up (in the rig's local units) a limb rises to hold the player clear of
    /// the grid. The rig's geometry makes this exact rather than eyeballed: a leg's
    /// shoulder mounts at <c>BodyHeight + Mount.Y</c> and its foot hangs
    /// <c>-Foot.Y</c> below that, and those two happen to cancel — so the claw's world
    /// height is simply this value times <see cref="CrabRig.Scale"/>. At 2.5 that puts
    /// the hand at world y=6, which is precisely where CrabSeizure parks the craft, so
    /// the claw and the thing it is supposedly gripping occupy the same space.
    /// </summary>
    private const float ArmLift = 2.5f;

    /// <summary>
    /// Swings one limb from a walking pose into an outstretched arm by
    /// <paramref name="amount"/> (0 leg, 1 fully committed): its yaw turns toward the
    /// front of the chassis, its skitter bob is blended out — a hand should be steady
    /// — and it rises off the floor to the given extra lift.
    /// </summary>
    private static void PoseArm(in CrabRig.Leg leg, float amount, float targetYaw,
        float extraLift, ref float yaw, ref float footLift)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        yaw = Lerp(yaw, targetYaw, amount);
        footLift = Lerp(footLift, ArmLift + extraLift, amount);
    }

    /// <summary>
    /// The striking limb, which moves in two beats rather than one. It first winds
    /// back and high — away from the front of the chassis, so the cocked claw sits in
    /// the held player's peripheral vision as the telegraph — and only then swings
    /// through and down onto them. <paramref name="amount"/> runs 0 (at rest) through
    /// <see cref="CockPoint"/> (fully wound) to 1 (struck through).
    /// </summary>
    private static void PoseStrikeArm(in CrabRig.Leg leg, float amount,
        ref float yaw, ref float footLift)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        float cockedYaw = leg.BaseYaw - 0.9f;     // drawn back and outward

        if (amount <= CockPoint)
        {
            // Winding up: back, and rising well above the grip so the blow has
            // somewhere to fall from.
            float w = amount / CockPoint;
            yaw = Lerp(yaw, cockedYaw, w);
            footLift = Lerp(footLift, ArmLift + CockRise, w);
            return;
        }

        // Coming through: across the front of the chassis and down onto the player.
        float s = (amount - CockPoint) / (1f - CockPoint);
        s = s * s;                                 // accelerates into the impact
        yaw = Lerp(cockedYaw, GripYawLeft, s);
        footLift = Lerp(ArmLift + CockRise, ArmLift, s);
    }

    /// <summary>Where in the strike channel the limb is fully wound back — matched to
    /// the value the cinematic holds through its own wind-up beat.</summary>
    private const float CockPoint = 0.35f;

    /// <summary>Extra height the striking limb gains at the top of its wind-up, above
    /// the grip. This is the whole telegraph: the claw visibly gets further away
    /// before it comes down.</summary>
    private const float CockRise = 1.8f;

    private static float Lerp(float a, float b, float f) => a + (b - a) * Math.Clamp(f, 0f, 1f);

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
