using System.Numerics;

namespace Unrendered.Rendering;

/// <summary>
/// The figure's fixed measurements, in the blocky-avatar grid the shape was designed in.
///
/// Held apart from the model that draws them because two things now need them and they
/// have to agree exactly: <see cref="SoldierModel"/> places the parts, and
/// <see cref="SoldierAnimator"/> solves for where a hand or a foot has to be, which it
/// cannot do without knowing how long a forearm is. A solver working off its own copy of
/// these numbers is a solver that puts a foot half a centimetre through the floor.
/// </summary>
public static class SoldierSkeleton
{
    /// <summary>One unit of the blocky-avatar grid, in world metres. Thirty-two and a bit of
    /// them is the figure's full height — 1.89m.</summary>
    public const float U = 0.058f;

    /// <summary>The torso: eight wide and four deep, straight up and down. It used to taper
    /// in to the hips; the blocky convention is a plain slab, and a plain slab is what makes
    /// the figure read as one at a glance.</summary>
    public const float BodyHalfW = 4f * U;
    public const float BodyHalfD = 2f * U;

    /// <summary>How far the sole is below the ankle the shin pivots the boot from. The
    /// solver plants soles and the chain ends at ankles, so somebody has to know this or
    /// every planted foot is buried to the laces.</summary>
    public const float SoleDrop = 0.6f * U;

    /// <summary>A thigh and a shin. Equal lengths, which makes the two-bone solve well
    /// conditioned everywhere except dead straight.</summary>
    public const float LegSeg = 6f * U;

    /// <summary>Where the legs meet the pelvis, and the pivot the whole upper body turns
    /// about: exactly one locked leg and a sole off the floor, so a figure standing
    /// straight is standing on the grid rather than kneeling into it.</summary>
    public const float HipY = 2f * LegSeg + SoleDrop;

    /// <summary>Where the pelvis ends and the chest begins. The torso used to be one block
    /// from the hips to the neck; the waist is the cut that lets the shoulders disagree
    /// with the hips, and everything in the base layer that reads as weight comes from
    /// that disagreement.</summary>
    public const float WaistY = 4.5f * U;

    /// <summary>Top of the torso, measured from the hips. Twelve, like the legs.</summary>
    public const float NeckY = 12f * U;

    public const float LimbHalf = 2f * U;

    /// <summary>An upper arm and a forearm. Shorter than the leg's segments because the
    /// arm's pivot is two units down inside it — the shoulder's cap rides above the joint —
    /// and the convention is that the whole arm is exactly as long as the torso: flush with
    /// the top of the shoulders, ending at the hips.</summary>
    public const float ArmSeg = 5f * U;

    /// <summary>The part of the upper arm above its pivot — the shoulder.</summary>
    public const float ShoulderCap = 2f * U;

    /// <summary>The head: a pyramid on an eight-by-eight base, as wide as the torso and
    /// twice as deep, so it overhangs front and back the way the blocky head does.</summary>
    public const float HeadHalf = 4f * U;
    public const float HeadTall = 8f * U;

    /// <summary>Shoulders sit just outside the chest, two units under its top, so the cap of
    /// each arm is flush with the shoulder line — measured from the <em>waist</em>, since
    /// that is the joint they now hang from.</summary>
    public const float ShoulderX = BodyHalfW + LimbHalf;
    public const float ShoulderY = NeckY - WaistY - ShoulderCap;

    /// <summary>Hips sit inside the pelvis, measured from the hip pivot itself: each leg
    /// directly under its own half of the torso.</summary>
    public const float HipX = 2f * U;

    /// <summary>Neck height above the waist, where the head is hung.</summary>
    public const float HeadY = NeckY - WaistY;

    /// <summary>How far a leg reaches with the knee locked. The ceiling on any foot the
    /// solver is asked to plant.</summary>
    public const float LegReach = LegSeg * 2f;

    /// <summary>And an arm, from the shoulder. Nothing can be gripped past this.</summary>
    public const float ArmReach = ArmSeg * 2f;

    // ================================================================================
    //  Forward kinematics
    // ================================================================================
    //
    // Where each joint ends up, given a pose. Both halves of the system need this and they
    // must not each have their own version of it: the model places parts with it, and the
    // solver needs the same answers to know where a shoulder is before it can work out
    // where that shoulder's hand has to reach. Two implementations that agree to within a
    // degree are two implementations that leave a visible gap at every joint.

    /// <summary>
    /// The mesh transform's own rotation, exactly: roll about the local Z, then pitch about
    /// the local X, then yaw about Y. Order is not negotiable — it is the order
    /// <see cref="PolyMesh"/> applies, and a chain built in any other one comes apart.
    /// </summary>
    public static Vector3 Rotate(Vector3 v, float pitch, float roll, float yaw = 0f)
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
        if (yaw != 0f)
        {
            float c = MathF.Cos(yaw), s = MathF.Sin(yaw);
            v = new Vector3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
        }
        return v;
    }

    /// <summary>Where a limb segment's far end lands, hung from its pivot and swung by the
    /// two angles. The one piece of geometry every joint in the figure is built on.</summary>
    public static Vector3 Segment(float length, float pitch, float roll)
        => Rotate(new Vector3(0f, -length, 0f), pitch, roll);

    /// <summary>The whole body's offset from where the simulation says it is: breath,
    /// weight shift, and the compression of a landing.</summary>
    public static Vector3 Root(in SoldierPose p) => new(p.Sway, p.Rise, p.Surge);

    /// <summary>The hip joint the pelvis turns about — and, since the legs hang from the
    /// root rather than from the pelvis, the point both of them swing from too. That is
    /// deliberate: turning the hips is meant to turn the body <em>against</em> the legs,
    /// and legs parented to the pelvis would be carried round with it and cancel the
    /// whole effect out.</summary>
    public static Vector3 Pelvis(in SoldierPose p) => Root(p) + new Vector3(0f, HipY, 0f);

    /// <summary>The waist: the top of the pelvis, and what the chest turns about.</summary>
    public static Vector3 Waist(in SoldierPose p)
        => Pelvis(p) + Rotate(new Vector3(0f, WaistY, 0f), p.PelvisPitch, p.PelvisRoll, p.PelvisYaw);

    /// <summary>The chest's total rotation — its own, on top of the pelvis it sits on.</summary>
    public static (float Pitch, float Roll, float Yaw) ChestAngles(in SoldierPose p)
        => (p.PelvisPitch + p.ChestPitch, p.PelvisRoll + p.ChestRoll, p.PelvisYaw + p.ChestYaw);

    /// <summary>One shoulder, carried wherever the chest has taken it.</summary>
    public static Vector3 Shoulder(in SoldierPose p, bool left)
    {
        var (pitch, roll, yaw) = ChestAngles(p);
        return Waist(p) + Rotate(new Vector3(left ? ShoulderX : -ShoulderX, ShoulderY, 0f),
            pitch, roll, yaw);
    }

    /// <summary>The base of the neck, where the head is hung.</summary>
    public static Vector3 Neck(in SoldierPose p)
    {
        var (pitch, roll, yaw) = ChestAngles(p);
        return Waist(p) + Rotate(new Vector3(0f, HeadY, 0f), pitch, roll, yaw);
    }

    /// <summary>One hip. On the root, for the reason <see cref="Pelvis"/> gives.</summary>
    public static Vector3 Hip(in SoldierPose p, bool left)
        => Root(p) + new Vector3(left ? HipX : -HipX, HipY, 0f);

    /// <summary>Where one hip launcher is mounted — and so where that hand has to be for
    /// the figure to be holding it. On the belt, clear of the torso's side and a touch
    /// behind the hip line — right where a hanging hand comes to rest on it.</summary>
    public static Vector3 Launcher(in SoldierPose p, bool left)
    {
        float lift = left ? p.LeftRigLift : p.RightRigLift;
        return Root(p) + new Vector3(
            (left ? 1f : -1f) * (BodyHalfW + 1.4f * U), HipY + 0.5f * U + lift, -0.5f * U);
    }

    /// <summary>One wrist, at the far end of an arm. The model hangs the glove here and a
    /// held weapon from it; the solver reaches it toward a launcher.</summary>
    public static Vector3 Wrist(in SoldierPose p, bool left)
    {
        LimbPose arm = left ? p.LeftArm : p.RightArm;
        Vector3 elbow = Shoulder(p, left) + Segment(ArmSeg, arm.UpperPitch, arm.UpperRoll);
        return elbow + Segment(ArmSeg, arm.LowerPitch, arm.LowerRoll);
    }

    /// <summary>One ankle, at the far end of a leg — where the boot hangs, and the end of the
    /// chain the solver plants.</summary>
    public static Vector3 Ankle(in SoldierPose p, bool left)
    {
        LimbPose leg = left ? p.LeftLeg : p.RightLeg;
        Vector3 knee = Hip(p, left) + Segment(LegSeg, leg.UpperPitch, leg.UpperRoll);
        return knee + Segment(LegSeg, leg.LowerPitch, leg.LowerRoll);
    }
}
