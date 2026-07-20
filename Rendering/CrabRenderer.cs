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

        // The chassis's lean while it lines up its lance. Zero in every other phase,
        // so all of this collapses back to the flat rig it has always drawn as.
        float pitch = pose.BodyPitch, roll = pose.BodyRoll;

        // Lower base: grinds down into the grid as it tears.
        if (!Flicker(death))
        {
            Vector3 j = Jitter(death, 1.2f) + new Vector3(0f, -fling * 3f, 0f);
            _bodyLower.Draw(new Vector2(bodyPos.X + j.X, bodyPos.Y + j.Z), heading,
                baseY * Scale + j.Y, cameraPos, Scale, DeathTint(Palette.CrabChassis, death),
                pitch, roll);
        }

        // Upper carapace lid: rises by the clamp amount in life; on death it blows
        // straight up and tumbles off its hinge.
        float lift = pose.ClawOpen * LidLift;
        if (!Flicker(death))
        {
            Vector3 j = Jitter(death, 1.6f) + new Vector3(0f, fling * 12f, 0f);
            float spin = heading + fling * 6f;
            _bodyUpper.Draw(new Vector2(bodyPos.X + j.X, bodyPos.Y + j.Z), spin,
                (baseY + lift) * Scale + j.Y, cameraPos, Scale, DeathTint(Palette.CrabChassis, death),
                pitch, roll);
        }

        // Neon gem in the well, spinning and tinted this frame's colour — on death it
        // erupts upward, spinning wild and strobing red/white/magenta.
        if (!Flicker(death))
        {
            Vector3 j = Jitter(death, 2.2f) + new Vector3(0f, fling * 8f, 0f);
            Color coreTint = death > 0f ? DeathTint(Palette.NeonRed, death) : pose.CoreColor;
            // The gem is the emitter, so it takes the full lean: when the body tips
            // onto the player, the crystal is what ends up pointed at them.
            DrawPart(_core, new Vector3(0f, baseY + CoreY, 0f), pose.CoreSpin + fling * 20f,
                bodyPos, heading, cameraPos, coreTint, j, pitch, roll, tiltPart: true);
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
            // The legs' shoulders ride the leaning chassis — that is what makes the
            // tilt read as the body shifting its weight — but each limb itself stays
            // upright, because its foot is on the grid holding the thing up. Tipping
            // the legs too would swing the feet through the floor.
            DrawPart(_leg, mount, yaw + fling * 4f, bodyPos, heading, cameraPos,
                DeathTint(Palette.CrabChassis, death), offset, pitch, roll, tiltPart: false);
        }
    }

    // --- The lance ------------------------------------------------------------

    /// <summary>
    /// Draws whatever the boss's beam attack is doing this frame: a gathering flare
    /// in the crystal across the charge, then the beam itself.
    ///
    /// The beam is white cored inside red, and the layering is what makes that read
    /// rather than the colours: an opaque white shaft is drawn first and a wide
    /// translucent red sheath over it, so the edges bleed red into the dark while the
    /// centre stays blown-out white — a beam too bright to have a colour in the
    /// middle. Drawn as round cylinders rather than the game's flat-shaded facets on
    /// purpose: light is the one thing out here that isn't built out of polygons.
    /// </summary>
    public void DrawLance(Entities.CrabCore boss)
        => DrawLance(boss.BeamOrigin, boss.BeamDirection, boss.ChargeProgress,
            boss.BeamActive ? boss.BeamProgress : -1f);

    /// <summary>
    /// The lance from plain values rather than a live boss, so the test screen can
    /// show the charge and the beam with no brain behind them. A negative
    /// <paramref name="beam"/> means the shaft isn't firing at all.
    /// </summary>
    public void DrawLance(Vector3 origin, Vector3 direction, float charge, float beam)
    {
        float t = (float)Raylib.GetTime();

        // The charge: a ball of light swelling in the gem, pulsing faster as it
        // fills, so the crystal is visibly loading before anything comes out of it.
        if (charge > 0f)
        {
            float pulse = 0.75f + 0.25f * MathF.Sin(t * (14f + 26f * charge));
            float r = (0.8f + 3.2f * charge * charge) * pulse;
            Color hot = GridRenderer.LerpColor(Palette.NeonRed, Color.White, charge * charge);
            Raylib.DrawSphereEx(origin, r * 1.7f, 8, 8,
                new Color(Palette.NeonRed.R, Palette.NeonRed.G, Palette.NeonRed.B, (int)(120 * charge)));
            Raylib.DrawSphereEx(origin, r, 8, 8, hot);
        }

        if (beam < 0f) return;

        float f = beam;
        // Snaps on and cuts out, holding full width almost the whole way between —
        // the beam is either firing or it isn't, and the taper at each end only
        // exists so neither edge lands as a hard pop.
        float env = Math.Clamp(MathF.Min(f / 0.06f, (1f - f) / 0.12f), 0f, 1f);
        if (env <= 0f) return;

        Vector3 from = origin;
        Vector3 to = from + direction * Entities.CrabCore.BeamLength;

        // A fast flutter on the width, so the shaft boils rather than sitting there
        // as a static cone — a still beam reads as geometry, not as energy.
        float flutter = 0.9f + 0.1f * MathF.Sin(t * 37f);
        float core = Entities.CrabCore.BeamRadius * 0.42f * env * flutter;
        float sheath = Entities.CrabCore.BeamRadius * env * flutter;

        // White core first, red sheath over it: the sheath's near face then blends
        // across the white instead of the depth buffer hiding it.
        Raylib.DrawCylinderEx(from, to, core * 1.35f, core, 10, Color.White);
        Raylib.DrawCylinderEx(from, to, sheath * 1.4f, sheath, 10,
            new Color(Palette.NeonRed.R, Palette.NeonRed.G, Palette.NeonRed.B, (byte)130));

        // The muzzle: a small blown-out flare where it leaves the crystal, so the beam
        // has an obvious source and doesn't read as having simply appeared in the air.
        // Kept barely wider than the shaft itself — sized off the beam and not off the
        // rig, because a flare big enough to be seen from across the arena is also big
        // enough to swallow the boss that fired it.
        Raylib.DrawSphereEx(from, sheath * 0.95f, 8, 8, new Color((int)255, 120, 120, 150));
        Raylib.DrawSphereEx(from, core * 1.1f, 8, 8, Color.White);
    }

    // --- The seizure's two hands ---------------------------------------------
    // The rig has no arms — it was built as a six-legged walker. Rather than bolt on
    // limbs that would never be seen for the other 99% of the fight, the seizure
    // presses the two front legs into service: they swing up off the floor, point
    // forward, and read as hands precisely because a walking leg doing that is
    // obviously wrong. The maths is the same for both; only the target angles differ.

    private const int GrabLeg = CrabRig.GrabLeg;      // right front, in CrabRig.Legs order
    private const int StrikeLeg = CrabRig.StrikeLeg;  // left front

    // Where each hand reaches to. A shoulder is mounted off to its own side of the
    // chassis, so a limb pointing bluntly "forward" carries its claw forward *and*
    // sideways and ends up alongside the player rather than on them — the angle has to
    // be solved, and CrabRig does it, converging both hands on one point in front of
    // the body. The right limb takes that yaw directly; the left is offset a full turn
    // so it sweeps across the front of the chassis on the way in, rather than taking
    // the short way round and barely moving.
    private static readonly float GripYawRight = CrabRig.HoldingGripYaw(CrabRig.Legs[GrabLeg]);
    private static readonly float GripYawLeft =
        CrabRig.CentreGripYaw(CrabRig.Legs[StrikeLeg]) + MathF.Tau;

    /// <summary>Where the holding claw rides: under the craft, so the limb stays out of
    /// the player's line of sight to the core. See <see cref="CrabRig.GripDrop"/>.</summary>
    private const float ArmLift = CrabRig.HoldLift - CrabRig.GripDrop;

    /// <summary>Where the striking claw finishes: just above the craft, so unlike the
    /// holding hand this one does come into frame — that is the hit.</summary>
    private const float StrikeLift = CrabRig.HoldLift + CrabRig.StrikeRest;

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
            // Winding up: back, and rising well above the craft so the blow has
            // somewhere to fall from.
            float w = amount / CockPoint;
            yaw = Lerp(yaw, cockedYaw, w);
            footLift = Lerp(footLift, StrikeLift + CockRise, w);
            return;
        }

        // Coming through: across the front of the chassis and down onto the player,
        // finishing just above them rather than under them — this hand is meant to
        // arrive in the view, not skirt the bottom of it like the holding one.
        float s = (amount - CockPoint) / (1f - CockPoint);
        s = s * s;                                 // accelerates into the impact
        yaw = Lerp(cockedYaw, GripYawLeft, s);
        footLift = Lerp(StrikeLift + CockRise, StrikeLift, s);
    }

    /// <summary>Where in the strike channel the limb is fully wound back — shared with
    /// the cinematic, which winds the channel to exactly here across the scream.</summary>
    private const float CockPoint = CrabRig.StrikeCock;

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
        Vector2 bodyPos, float heading, Vector3 cameraPos, Color? tint = null, Vector3 offset = default,
        float pitch = 0f, float roll = 0f, bool tiltPart = false)
    {
        Vector3 o = localMount * Scale;
        // The mount point always rides the lean, so every part stays bolted to the
        // chassis wherever it has tipped to; whether the part itself turns with it is
        // the caller's call (the gem does, the legs don't).
        o = Tilt(o, pitch, roll);
        float cos = MathF.Cos(heading), sin = MathF.Sin(heading);
        float rx = o.X * cos + o.Z * sin;
        float rz = -o.X * sin + o.Z * cos;
        var worldXZ = new Vector2(bodyPos.X + rx + offset.X, bodyPos.Y + rz + offset.Z);
        mesh.Draw(worldXZ, heading + localYaw, o.Y + offset.Y, cameraPos, Scale, tint,
            tiltPart ? pitch : 0f, tiltPart ? roll : 0f);
    }

    /// <summary>Rolls then pitches a vector in the body's own frame — the same order
    /// <see cref="PolyMesh"/> applies to a tilted mesh, so a mount point and the part
    /// standing on it never disagree about where the chassis is leaning.</summary>
    private static Vector3 Tilt(Vector3 v, float pitch, float roll)
    {
        if (roll != 0f)
        {
            float c = MathF.Cos(roll), s = MathF.Sin(roll);
            v = new Vector3(v.X * c - v.Y * s, v.X * s + v.Y * c, v.Z);
        }
        if (pitch != 0f)
        {
            float c = MathF.Cos(pitch), s = MathF.Sin(pitch);
            v = new Vector3(v.X, v.Y * c - v.Z * s, v.Y * s + v.Z * c);
        }
        return v;
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
