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

    /// <summary>
    /// The same rig cut down to something a person drives — the player's SPIDER chassis.
    /// Sized off the body rather than the feet: the carapace and the core standing in it
    /// come out about as tall as a hunter tank, which is what makes it read as a
    /// Crab-Core you could look in the eye rather than a scale model of one. The legs
    /// then reach a good way past the craft's own hitbox, because that is simply the
    /// proportion of the thing — a walker's footprint is much wider than its body, and
    /// pulling the legs in to match the hitbox would just make it a bug.
    /// </summary>
    public const float PlayerScale = 0.45f;

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

    /// <summary>The gem mesh's own height in world units, so anything aiming at the
    /// crystal — the seizure's camera, the self-test's framing check — can work from
    /// the pyramid's real extent instead of each guessing at where its apex is.</summary>
    public static float CoreMeshHeight => 2.5f * Scale;

    /// <summary>Peak lift of a skittering foot (raw units, before Scale).</summary>
    public const float LegBob = 0.9f;

    // --- The seizure's hands ---------------------------------------------------
    // During a seizure the two front legs stop walking and become arms. Where a claw
    // actually ends up is pure rig geometry, and it has to be solved rather than
    // guessed: the cinematic parks the player at the point the hand closes on, and
    // the striking hand has to arrive at that same point. Both live here so the pose
    // (renderer) and the hold (CrabSeizure) can never disagree about where the claw is.

    /// <summary>Index into <see cref="Legs"/> of the limb that holds the player.</summary>
    public const int GrabLeg = 0;    // right front

    /// <summary>Index into <see cref="Legs"/> of the limb that clubs them.</summary>
    public const int StrikeLeg = 3;  // left front

    /// <summary>
    /// How high the craft itself is carried, in raw units. Geometry makes the world
    /// height exact rather than eyeballed: a shoulder mounts at
    /// <c>BodyHeight + Mount.Y</c> and its foot hangs <c>-Foot.Y</c> below it, and those
    /// two cancel — so a raised limb's world height is simply its lift times
    /// <see cref="Scale"/>. Set to put the eye a little under the middle of the core
    /// gem, so the held player is looking slightly <em>up</em> into the crystal.
    /// </summary>
    public const float HoldLift = 2.6f;

    /// <summary>World height the cinematic carries the craft at.</summary>
    public static float HoldWorldY => HoldLift * Scale;

    /// <summary>
    /// How far <em>below</em> the craft the holding claw closes, in raw units — the
    /// hand grips from underneath rather than occupying the same point as the player's
    /// eye.
    ///
    /// This gap is the whole difference between reading as held and being blinded. An
    /// arm runs from its shoulder on the boss out to whatever it is gripping, so a claw
    /// level with the eye puts the entire limb across the line of sight to the core —
    /// the player ends up staring down a leg at the one thing the scene exists to show
    /// them. Dropped by this much the shoulder (world y≈8.6) and the claw both sit
    /// under the eye, so the near end of the limb — the part big enough in frame to
    /// blind you — rides along the bottom of the view: plainly there, plainly holding
    /// you, and clear of the crystal. The far end still rises to the shoulder, which is
    /// as it should be; an arm has to come from somewhere.
    /// </summary>
    public const float GripDrop = 0.55f;

    /// <summary>World height of the holding claw.</summary>
    public static float GripWorldY => (HoldLift - GripDrop) * Scale;

    /// <summary>
    /// How far above the craft the striking claw comes to rest, in raw units. Unlike
    /// the holding hand this one is meant to arrive right in the view and fill it — a
    /// blow you never see land is just the screen flashing. Sized to finish a little
    /// over the eye, so the claw comes down across the whole frame and the core is
    /// briefly blotted out behind it at the moment of impact.
    /// </summary>
    public const float StrikeRest = 1.5f;

    /// <summary>How far into its swing the striking limb is fully wound back. Shared,
    /// because the cinematic's timing and the renderer's pose both key off it.</summary>
    public const float StrikeCock = 0.35f;

    /// <summary>
    /// The hinge yaw that swings a leg's claw onto the body's forward centre line —
    /// the pose of a hand reaching out in front of the chassis rather than out to its
    /// own side. A leg's tip sits at <c>Mount + Foot.X·(cos y, −sin y)</c> in the body
    /// frame, so landing on x=0 needs <c>cos y = −Mount.X / Foot.X</c>; the negative
    /// root is the one that also carries the claw forward instead of behind.
    /// </summary>
    public static float CentreGripYaw(in Leg leg)
        => -MathF.Acos(Math.Clamp(-leg.Mount.X / Foot.X, -1f, 1f));

    /// <summary>How far out in front of the body's centre that claw then lands, in
    /// world units. The two front legs mirror each other, so both hands converge on
    /// the same point — which is exactly what lets one hold the player there while
    /// the other swings in and hits them.</summary>
    public static float CentreGripReach(in Leg leg)
        => (leg.Mount.Z - Foot.X * MathF.Sin(CentreGripYaw(leg))) * Scale;

    /// <summary>
    /// How far the <em>holding</em> limb is swung off that centre line, in radians. The
    /// striking hand converges dead ahead — it is supposed to fill the view — but the
    /// holding one must not, and this is what keeps it out of the way.
    ///
    /// Sitting the claw on the centre line puts the whole forearm on the line of sight,
    /// rising steeply from under the frame to over it right in front of the player: a
    /// column straight through the middle of the shot, with the core behind it. Swung
    /// out by this much the same limb crosses the upper corner instead and leaves the
    /// crystal clear, while still obviously arriving from the boss and ending at the
    /// player. The grip reads from the arm sweeping in, not from the claw itself, which
    /// at this angle sits just outside the frame edge.
    /// </summary>
    public const float GripYawOffset = 0.55f;

    /// <summary>The holding limb's actual pose — off to the side, per
    /// <see cref="GripYawOffset"/>.</summary>
    public static float HoldingGripYaw(in Leg leg) => CentreGripYaw(leg) + GripYawOffset;

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
        => TipWorldXZ(leg, leg.BaseYaw, bodyPos, heading);

    /// <summary>The same, for a limb swung to an arbitrary hinge yaw rather than its
    /// walking pose — which is what a seizure's two hands are. This is the one place
    /// the claw's world position is worked out, so a hand and whatever it is supposed
    /// to be holding can be checked against each other.</summary>
    public static Vector2 TipWorldXZ(in Leg leg, float yaw, Vector2 bodyPos, float heading)
    {
        Vector2 mount = Rotate(new Vector2(leg.Mount.X, leg.Mount.Z) * Scale, heading);
        Vector2 tip = Rotate(new Vector2(Foot.X, 0f) * Scale, heading + yaw);
        return bodyPos + mount + tip;
    }

    /// <summary>Rotates a planar (x,z) vector by a heading, matching PolyMesh.Draw.</summary>
    private static Vector2 Rotate(Vector2 v, float h)
    {
        float c = MathF.Cos(h), s = MathF.Sin(h);
        return new Vector2(v.X * c + v.Y * s, -v.X * s + v.Y * c);
    }
}
