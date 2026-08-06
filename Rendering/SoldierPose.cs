using System.Numerics;

namespace Unrendered.Rendering;

/// <summary>
/// One limb — an arm or a leg — as two segments, each with its own absolute orientation in
/// the body's frame rather than as a joint angle relative to the segment above it.
///
/// Absolute is the awkward choice to author by hand and the only one that works. A forearm
/// stored as "the upper arm's angle plus a bend" can only ever bend in the one axis the
/// shoulder is already swinging in, which is fine for a marching stride and useless the
/// moment anything <em>solves</em> for where a hand has to be: reaching across the body for
/// a grip, planting a foot on a surface the hip is not square to. Stored absolutely, a
/// solver writes both segments directly and the hand-authored poses use
/// <see cref="FromBend"/> to say the same thing they always said.
/// </summary>
public struct LimbPose
{
    /// <summary>The upper segment — thigh or upper arm — swung from the hip or shoulder.
    /// Pitch swings it fore and aft (positive is behind the body); roll swings it out to
    /// the side.</summary>
    public float UpperPitch, UpperRoll;

    /// <summary>The lower segment — shin or forearm — in the same absolute frame, hung
    /// from wherever the upper one ended.</summary>
    public float LowerPitch, LowerRoll;

    /// <summary>
    /// The old way of saying it, kept because it is how a pose is naturally written by
    /// hand: an upper angle, a sideways splay both segments share, and a bend at the
    /// joint. A knee bends one way and an elbow the other, which is carried entirely by
    /// the sign of <paramref name="bend"/>.
    /// </summary>
    public static LimbPose FromBend(float upper, float splay, float bend) => new()
    {
        UpperPitch = upper,
        UpperRoll = splay,
        LowerPitch = upper + bend,
        LowerRoll = splay,
    };

    /// <summary>
    /// The same, mirrored for whichever side of the body it is on: a positive
    /// <paramref name="splay"/> swings the limb <em>outward</em>, away from the centre line,
    /// on both sides.
    ///
    /// The angles themselves are absolute — they have to be, or no solver could write them —
    /// so the mirroring cannot live in the draw the way it used to. It lives here instead,
    /// which keeps every hand-authored pose readable as "both arms out by this much" rather
    /// than as two numbers that happen to be negatives of each other.
    /// </summary>
    public static LimbPose FromBend(float upper, float splay, float bend, bool left)
        => FromBend(upper, left ? splay : -splay, bend);

    public static LimbPose Lerp(in LimbPose a, in LimbPose b, float t) => new()
    {
        UpperPitch = a.UpperPitch + (b.UpperPitch - a.UpperPitch) * t,
        UpperRoll = a.UpperRoll + (b.UpperRoll - a.UpperRoll) * t,
        LowerPitch = a.LowerPitch + (b.LowerPitch - a.LowerPitch) * t,
        LowerRoll = a.LowerRoll + (b.LowerRoll - a.LowerRoll) * t,
    };

    /// <summary>Accumulates a weighted share of another limb — the inner loop of blending
    /// several states at once, where the weights are known to sum to one.</summary>
    public void AddScaled(in LimbPose o, float w)
    {
        UpperPitch += o.UpperPitch * w;
        UpperRoll += o.UpperRoll * w;
        LowerPitch += o.LowerPitch * w;
        LowerRoll += o.LowerRoll * w;
    }

    /// <summary>Adds another limb's angles outright — an additive layer (a flinch, a swing
    /// of a blade) laid over whatever the locomotion beneath it is doing.</summary>
    public void Add(in LimbPose o)
    {
        UpperPitch += o.UpperPitch;
        UpperRoll += o.UpperRoll;
        LowerPitch += o.LowerPitch;
        LowerRoll += o.LowerRoll;
    }
}

/// <summary>
/// Every joint in the figure for one frame, in the body's own frame.
///
/// This replaces the single flat list of angles the model used to be posed by, and the
/// difference that matters is the spine: the torso is now a pelvis and a chest that can
/// disagree with each other. Nearly everything that makes a walking body read as carrying
/// its own weight — hips leading, shoulders counter-rotating, a curl away from a hit —
/// is that one disagreement, and a figure built from a single rigid torso block cannot
/// express any of it however many limbs are hung off it.
///
/// Angles are radians. Positive pitch swings a part <em>backward</em> (a limb's far end
/// travels toward −Z, the torso's top travels toward +Z, forward); positive roll tips a
/// part toward the figure's right; positive yaw turns it to the figure's left. Those
/// conventions are the mesh transform's, not a choice made here — see
/// <see cref="PolyMesh"/>.
/// </summary>
public struct SoldierPose
{
    // --- The root ---------------------------------------------------------------
    // Where the whole body sits relative to where the simulation says it is. Breathing,
    // the transfer of weight from foot to foot, and the compression of a landing all live
    // here, in metres of the model's own scale.

    /// <summary>Up. Negative is a body compressed into its own knees.</summary>
    public float Rise;

    /// <summary>Sideways, toward the figure's left.</summary>
    public float Sway;

    /// <summary>Fore and aft. Forward is positive — the body carried past its feet by its
    /// own momentum before the legs catch up with it.</summary>
    public float Surge;

    // --- The spine --------------------------------------------------------------

    /// <summary>The pelvis, about the hip joint: the base of everything above it.</summary>
    public float PelvisPitch, PelvisRoll, PelvisYaw;

    /// <summary>The chest, <em>relative to the pelvis</em>, about the waist. The whole
    /// point of splitting the torso: a chest that can twist against the hips.</summary>
    public float ChestPitch, ChestRoll, ChestYaw;

    // --- The head ---------------------------------------------------------------

    /// <summary>The head, relative to the chest. Yaw and nod carry the look-at; tilt is
    /// the small sideways cock of a head that is bracing or has just been hit.</summary>
    public float HeadYaw, HeadNod, HeadTilt;

    // --- The limbs --------------------------------------------------------------

    public LimbPose LeftArm, RightArm, LeftLeg, RightLeg;

    // --- The kit ----------------------------------------------------------------

    /// <summary>How far each hip launcher has been lifted by the hand holding it.</summary>
    public float LeftRigLift, RightRigLift;

    /// <summary>And how far each has swung on its mount — the secondary motion layer,
    /// which on this figure is worn rather than grown: there is no cape and no hair, so
    /// what lags the body is the equipment strapped to it.</summary>
    public float LeftRigSwing, RightRigSwing;

    /// <summary>The bottle on the back, settling a beat behind the chest it is strapped
    /// to. Pitch and roll, in the chest's frame.</summary>
    public float BottlePitch, BottleRoll;

    /// <summary>A neutral figure: everything at zero, standing straight. The base every
    /// pose here is written as a departure from.</summary>
    public static SoldierPose Rest => default;

    /// <summary>Accumulates a weighted share of another pose. Used to fold several
    /// simultaneous states into one figure — the crossfade that means this system never
    /// cuts from one animation to another.</summary>
    public void AddScaled(in SoldierPose o, float w)
    {
        Rise += o.Rise * w; Sway += o.Sway * w; Surge += o.Surge * w;

        PelvisPitch += o.PelvisPitch * w; PelvisRoll += o.PelvisRoll * w; PelvisYaw += o.PelvisYaw * w;
        ChestPitch += o.ChestPitch * w; ChestRoll += o.ChestRoll * w; ChestYaw += o.ChestYaw * w;
        HeadYaw += o.HeadYaw * w; HeadNod += o.HeadNod * w; HeadTilt += o.HeadTilt * w;

        LeftArm.AddScaled(o.LeftArm, w);
        RightArm.AddScaled(o.RightArm, w);
        LeftLeg.AddScaled(o.LeftLeg, w);
        RightLeg.AddScaled(o.RightLeg, w);

        LeftRigLift += o.LeftRigLift * w; RightRigLift += o.RightRigLift * w;
        LeftRigSwing += o.LeftRigSwing * w; RightRigSwing += o.RightRigSwing * w;
        BottlePitch += o.BottlePitch * w; BottleRoll += o.BottleRoll * w;
    }

    /// <summary>Lays another pose over this one outright, joint for joint. This is what
    /// makes the look-at, the flinch and the attacks <em>layers</em>: they are written as
    /// departures from zero and added on top of whatever the body was already doing,
    /// rather than as complete poses that would throw the locomotion away.</summary>
    public void Add(in SoldierPose o)
    {
        Rise += o.Rise; Sway += o.Sway; Surge += o.Surge;

        PelvisPitch += o.PelvisPitch; PelvisRoll += o.PelvisRoll; PelvisYaw += o.PelvisYaw;
        ChestPitch += o.ChestPitch; ChestRoll += o.ChestRoll; ChestYaw += o.ChestYaw;
        HeadYaw += o.HeadYaw; HeadNod += o.HeadNod; HeadTilt += o.HeadTilt;

        LeftArm.Add(o.LeftArm);
        RightArm.Add(o.RightArm);
        LeftLeg.Add(o.LeftLeg);
        RightLeg.Add(o.RightLeg);

        LeftRigLift += o.LeftRigLift; RightRigLift += o.RightRigLift;
        LeftRigSwing += o.LeftRigSwing; RightRigSwing += o.RightRigSwing;
        BottlePitch += o.BottlePitch; BottleRoll += o.BottleRoll;
    }
}
