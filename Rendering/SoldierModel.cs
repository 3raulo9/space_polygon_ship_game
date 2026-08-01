using System.Numerics;
using Raylib_cs;
using Unrendered.Core;

namespace Unrendered.Rendering;

/// <summary>
/// The figure itself: the person the SOLDIER chassis is. The only humanoid in this game,
/// and the only model here that is <em>articulated</em> — every other thing on the roster
/// is one rigid mesh turned on the spot, and a rigid person is a statue.
///
/// Proportions are lifted straight off the blocky-avatar convention everyone already reads
/// as "a person": a body eight wide, four deep and twelve tall, arms and legs four by four
/// by twelve, the whole figure thirty-two units to the crown. The measurements live in
/// <see cref="SoldierSkeleton"/> because the solver needs them too, and the two must agree
/// exactly.
///
/// Three deliberate departures from that convention:
///
/// <list type="bullet">
/// <item>The head is a <em>pyramid</em>, not a cube. A cube head would make this a mascot;
/// a hard four-faced wedge keeps it in the same family as the tank's pyramidal cap and the
/// elite's cone, and — because a pyramid has a nose — it points, so the figure has a facing
/// you can read from any angle.</item>
/// <item>The limbs have <em>elbows and knees</em>. Each is two segments about a joint,
/// which costs one extra mesh per limb and buys every pose worth having.</item>
/// <item>The torso is a <em>pelvis and a chest</em> rather than one block. This is the
/// newest of the three and the one that changed the most: a single rigid torso can be
/// leaned and it can be tilted, but it cannot disagree with itself, and nearly everything
/// that reads as a body carrying its own weight is the shoulders disagreeing with the hips
/// — counter-rotating through a stride, curling away from a hit, twisting into a swing
/// ahead of the legs that follow it.</item>
/// </list>
///
/// This file only <em>draws</em>. Where every joint should be is decided by
/// <see cref="SoldierAnimator"/> and arrives as a finished <see cref="SoldierPose"/>, so
/// the model knows nothing about momentum, states or the simulation — and the same pose
/// drives the hangar turntable, an enemy squad, a team-mate on the wire and the pair of
/// arms held in the player's own view.
///
/// Every part is built white and tinted at draw time, so the paint bay repaints the whole
/// figure for free — see <see cref="Loadout.PartColor"/>.
/// </summary>
public sealed class SoldierModel
{
    // Pulled in from the shared skeleton so the names below read as they always did.
    private const float U = SoldierSkeleton.U;
    private const float BodyHalfW = SoldierSkeleton.BodyHalfW;
    private const float BodyHalfD = SoldierSkeleton.BodyHalfD;
    private const float WaistY = SoldierSkeleton.WaistY;
    private const float NeckY = SoldierSkeleton.NeckY;
    private const float LimbHalf = SoldierSkeleton.LimbHalf;
    private const float SegLen = SoldierSkeleton.SegLen;
    private const float HeadHalf = SoldierSkeleton.HeadHalf;
    private const float HeadTall = SoldierSkeleton.HeadTall;

    /// <summary>The waist half-size, solved from the original single torso's taper so the
    /// cut is invisible: the block used to widen from the hips to the shoulders, and the
    /// two halves still meet at exactly the width it had reached by the waist.</summary>
    private const float WaistHalfW = 3.4f * U + (BodyHalfW - 3.4f * U) * (WaistY / NeckY);
    private const float WaistHalfD = 1.8f * U + (BodyHalfD - 1.8f * U) * (WaistY / NeckY);

    // --- Parts ------------------------------------------------------------------
    // Each is built with its pivot at the model origin — a limb hangs from y = 0 down to
    // y = −SegLen — because PolyMesh rotates about that origin. Getting this right is the
    // whole trick: it means a shoulder angle is just a pitch on the upper arm, and an elbow
    // is a pitch on the forearm placed at wherever the upper arm's end landed.

    private readonly PolyMesh _head = BuildHead();
    private readonly PolyMesh _pelvis = BuildPelvis();
    private readonly PolyMesh _chest = BuildChest();
    private readonly PolyMesh _limb = BuildLimb();       // one segment: arm or leg
    private readonly PolyMesh _boot = BuildBoot();
    private readonly PolyMesh _glove = BuildGlove();
    private readonly PolyMesh _belt = BuildBelt();
    private readonly PolyMesh _straps = BuildStraps();
    private readonly PolyMesh _bottle = BuildBottle();
    private readonly PolyMesh _launcher = BuildLauncher();
    private readonly PolyMesh _hook = BuildHook();
    private readonly PolyMesh _blade = BuildBlade();

    /// <summary>
    /// Draws the figure on the hangar's turntable, painted out of the build. The turntable's
    /// own rotation comes in as <paramref name="heading"/>; every joint comes in already
    /// posed.
    /// </summary>
    public void Draw(Loadout loadout, Vector2 pos, float heading, Vector3 cameraPos,
        in SoldierPose pose)
        => DrawPosed(pos, 0f, heading, cameraPos,
            loadout.PartColor(PlayerClass.Soldier, 0),
            loadout.PartColor(PlayerClass.Soldier, 1),
            loadout.PartColor(PlayerClass.Soldier, 2),
            loadout.PartColor(PlayerClass.Soldier, 3),
            pose, blades: false, Color.White);

    /// <summary>
    /// Draws a soldier out in the world at <paramref name="height"/> metres off the grid,
    /// posed for whatever they are currently doing.
    ///
    /// The same skeleton the hangar turntable shows, which is the whole reason it is here
    /// rather than in a renderer of its own: the thing hunting the player is built out of
    /// exactly the parts the player's own chassis is built out of, and it should be — they
    /// are wearing the same kit and flying it the same way. Only the pose and the colours
    /// differ, and the pose is where all the character is.
    /// </summary>
    public void DrawFlier(Vector2 pos, float height, float heading, Vector3 cameraPos,
        Color cloth, Color webbing, Color steel, Color cable, Color blade,
        in SoldierPose pose, bool blades, float scale = 1f)
        => DrawPosed(pos, height, heading, cameraPos, cloth, webbing, steel, cable,
            pose, blades, blade, scale);

    /// <summary>
    /// Draws the same figure as an outline — the shape a VIRUS with no body of its own
    /// perceives when a person moves near it. Every joint angle and the whole pose come
    /// through unchanged; only the surface is gone. A person is by some distance the most
    /// legible thing in that mode, because a wireframe of something articulated still reads
    /// as a person moving, and a wireframe of a tank reads as a box.
    /// </summary>
    public void DrawGhost(Vector2 pos, float height, float heading, Vector3 cameraPos,
        Color edge, in SoldierPose pose, bool blades, float scale = 1f)
    {
        _wire = true;
        _edge = edge;
        DrawPosed(pos, height, heading, cameraPos, edge, edge, edge, edge,
            pose, blades, edge, scale);
        _wire = false;
    }

    private void DrawPosed(Vector2 pos, float height, float heading, Vector3 cameraPos,
        Color cloth, Color webbing, Color steel, Color cable, in SoldierPose pose,
        bool blades, Color bladeTint, float scale = 1f)
    {
        // Everything below is laid out in the model's own metres and blown up by
        // <paramref name="scale"/> at draw time — which is why the world height is *not*
        // folded into the root the way the sway and the breath are: those are part of the
        // body and scale with it, and being thirty metres up a tower is not.
        _scale = scale;
        _baseY = height;

        // --- The spine: pelvis, then chest, each about its own joint. ---
        //
        // Both pivots and both sets of angles come out of the skeleton rather than being
        // worked out here, because the solver that decided where the hands and feet go
        // used those same functions to find the shoulders and hips. Two answers to "where
        // is the shoulder" is two answers to where the arm starts.

        Vector3 pelvisAt = SoldierSkeleton.Pelvis(pose);
        DrawPart(_pelvis, pos, heading, cameraPos, pelvisAt, cloth,
            pose.PelvisPitch, pose.PelvisRoll, pose.PelvisYaw);
        DrawPart(_belt, pos, heading, cameraPos, pelvisAt, webbing,
            pose.PelvisPitch, pose.PelvisRoll, pose.PelvisYaw);

        Vector3 waistAt = SoldierSkeleton.Waist(pose);
        var (chestPitch, chestRoll, chestYaw) = SoldierSkeleton.ChestAngles(pose);
        DrawPart(_chest, pos, heading, cameraPos, waistAt, cloth, chestPitch, chestRoll, chestYaw);
        DrawPart(_straps, pos, heading, cameraPos, waistAt, webbing, chestPitch, chestRoll, chestYaw);

        // The bottle rides the chest but settles a beat behind it — the loose layer, which
        // on this figure is worn rather than grown.
        DrawPart(_bottle, pos, heading, cameraPos, waistAt, webbing,
            chestPitch + pose.BottlePitch, chestRoll + pose.BottleRoll, chestYaw);

        // The head sits on the neck and turns on its own, on top of whatever the chest is
        // already doing — which is what makes the figure look like it is watching something
        // rather than facing wherever the body happens to have swung it.
        DrawPart(_head, pos, heading, cameraPos, SoldierSkeleton.Neck(pose), cloth,
            chestPitch + pose.HeadNod, chestRoll + pose.HeadTilt, chestYaw + pose.HeadYaw);

        // --- Arms. Left first, then right, each shoulder → elbow → hand. ---
        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            LimbPose arm = left ? pose.LeftArm : pose.RightArm;
            Vector3 shoulder = SoldierSkeleton.Shoulder(pose, left);

            DrawPart(_limb, pos, heading, cameraPos, shoulder, cloth, arm.UpperPitch, arm.UpperRoll);

            // The elbow is wherever the upper arm's far end ended up. Solved rather than
            // eyeballed, so any shoulder angle at all keeps the forearm attached.
            Vector3 elbow = shoulder + SoldierSkeleton.Segment(SegLen, arm.UpperPitch, arm.UpperRoll);
            DrawPart(_limb, pos, heading, cameraPos, elbow, cloth, arm.LowerPitch, arm.LowerRoll);

            // A glove on the end of it, in the webbing's colour. The figure is otherwise one
            // flat colour from the neck down, and at this size a silhouette needs something
            // breaking it up at the ends of the limbs or the arms disappear into the torso.
            Vector3 wrist = elbow + SoldierSkeleton.Segment(SegLen, arm.LowerPitch, arm.LowerRoll);
            DrawPart(_glove, pos, heading, cameraPos, wrist, webbing, arm.LowerPitch, arm.LowerRoll);

            // And the blade, when there is one drawn. It runs out of the fist along the
            // forearm's own line, which is what makes an enemy on a committed run readable
            // at any range: the one bright thing in the frame, held out ahead of a body
            // that is already pointed at you.
            if (blades)
                DrawPart(_blade, pos, heading, cameraPos, wrist, bladeTint,
                    arm.LowerPitch, arm.LowerRoll);
        }

        // --- Legs: hip → knee → boot. ---
        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            LimbPose leg = left ? pose.LeftLeg : pose.RightLeg;
            Vector3 hip = SoldierSkeleton.Hip(pose, left);

            DrawPart(_limb, pos, heading, cameraPos, hip, cloth, leg.UpperPitch, leg.UpperRoll);
            Vector3 knee = hip + SoldierSkeleton.Segment(SegLen, leg.UpperPitch, leg.UpperRoll);
            DrawPart(_limb, pos, heading, cameraPos, knee, cloth, leg.LowerPitch, leg.LowerRoll);

            // Boots in the harness colour for the same reason the gloves are: kit, not
            // clothing, and the contrast is what keeps the legs readable.
            Vector3 ankle = knee + SoldierSkeleton.Segment(SegLen, leg.LowerPitch, leg.LowerRoll);
            DrawPart(_boot, pos, heading, cameraPos, ankle, webbing, leg.LowerPitch, leg.LowerRoll);
        }

        // --- The rig: a launcher on each hip, with its hook seated in it. ---
        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            float side = left ? 1f : -1f;
            Vector3 at = SoldierSkeleton.Launcher(pose, left);

            // Its own swing on the mount, on top of the mounting angle it has always had.
            float pitch = left ? pose.LeftRigSwing : pose.RightRigSwing;
            float roll = side * -0.12f;

            DrawPart(_launcher, pos, heading, cameraPos, at, steel, pitch, roll);
            DrawPart(_hook, pos, heading, cameraPos,
                at + SoldierSkeleton.Rotate(new Vector3(0f, 0f, 5f * U), pitch, roll), cable,
                pitch, roll);
        }
    }

    /// <summary>
    /// Draws one part at a pivot given in the <em>model's</em> frame, turning that pivot
    /// into a world placement. The mesh's own draw handles the rest, so a part is placed and
    /// posed in one call and nothing here has to know about matrices.
    ///
    /// <paramref name="yaw"/> turns the part about its own vertical — the chest twisting
    /// against the hips, the head turning on the neck. It is added to the heading the mesh
    /// is drawn at but <em>not</em> to the heading the pivot is rotated by, because the
    /// pivot arrived already in model space with every parent's rotation folded into it.
    ///
    /// The two things that are the same for every part of one figure — how big it is and how
    /// far off the grid it stands — are held on the model rather than threaded through a
    /// dozen call sites.
    /// </summary>
    private void DrawPart(PolyMesh mesh, Vector2 pos, float heading, Vector3 cameraPos,
        Vector3 pivot, Color tint, float pitch = 0f, float roll = 0f, float yaw = 0f)
    {
        pivot *= _scale;

        float c = MathF.Cos(heading), s = MathF.Sin(heading);
        // The same X/Z rotation PolyMesh.Transform applies, so a pivot offset turns with the
        // figure instead of staying pinned to the world's axes.
        var at = new Vector2(
            pos.X + pivot.X * c + pivot.Z * s,
            pos.Y - pivot.X * s + pivot.Z * c);

        if (_wire)
            mesh.DrawWire(at, heading + yaw, _baseY + pivot.Y, cameraPos, _scale, _edge, pitch, roll);
        else
            mesh.Draw(at, heading + yaw, _baseY + pivot.Y, cameraPos, _scale, tint, pitch, roll);
    }

    /// <summary>Whether this draw is an outline, and what colour its edges are. Set for the
    /// duration of one figure, like the scale and the base height below it.</summary>
    private bool _wire;
    private Color _edge = Color.White;

    /// <summary>How big this figure is drawn, and how far off the grid its boots are. Set
    /// once at the top of a draw and read by every part of it.</summary>
    private float _scale = 1f;
    private float _baseY;

    // --- Geometry ---------------------------------------------------------------

    /// <summary>
    /// The head: a four-faced pyramid on a square base, apex up. The one part of the figure
    /// that isn't a box, and the reason it reads as this game's person rather than as a
    /// generic blocky avatar — it is the same hard wedge the tank wears as a cap and the
    /// elite is made of, at head size.
    /// </summary>
    private static PolyMesh BuildHead()
    {
        var m = new PolyMesh();
        const float h = HeadHalf;

        Vector3 fl = new(-h, 0f, h), fr = new(h, 0f, h);
        Vector3 br = new(h, 0f, -h), bl = new(-h, 0f, -h);
        // The apex leans a little forward, which gives the wedge a nose — and with it a
        // direction the figure is unmistakably facing from any angle.
        Vector3 apex = new(0f, HeadTall, 1.2f * U);

        m.AddFace(Color.White, bl, br, fr, fl);   // the underside, closing it off
        m.AddFace(Color.White, fl, fr, apex);
        m.AddFace(Color.White, fr, br, apex);
        m.AddFace(Color.White, br, bl, apex);
        m.AddFace(Color.White, bl, fl, apex);

        // A narrow band across the front where a visor would be — one box, and the pyramid
        // stops being an abstract shape and becomes a helmet. Set at the depth the sloping
        // face has actually reached by that height, so it sits *in* the front of the wedge
        // rather than floating out ahead of it.
        const float visorY = 3f * U;
        float faceZ = h * (1f - visorY / HeadTall) + 1.2f * U * (visorY / HeadTall);
        m.AddBoxSpan(Color.White, -2.2f * U, 2.2f * U,
            faceZ - 0.6f * U, faceZ + 0.35f * U, visorY - 0.8f * U, visorY + 0.6f * U);
        return m;
    }

    /// <summary>The lower half of the torso, from the hips to the waist. Tapered outward
    /// as it rises, exactly as the single block it was cut out of used to be.</summary>
    private static PolyMesh BuildPelvis()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, 3.4f * U, 1.8f * U, WaistHalfW, WaistHalfD, 0f, WaistY);
        return m;
    }

    /// <summary>The upper half, from the waist to the neck, pivoting at the waist. This is
    /// the part that gets to disagree with the hips.</summary>
    private static PolyMesh BuildChest()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, WaistHalfW, WaistHalfD, BodyHalfW, BodyHalfD, 0f, NeckY - WaistY);
        return m;
    }

    /// <summary>
    /// One limb segment — an upper arm, a forearm, a thigh or a shin, which in this
    /// convention are all the same four-by-four-by-six box. Built hanging from its pivot, so
    /// rotating it about the origin is exactly rotating it about its joint.
    /// </summary>
    private static PolyMesh BuildLimb()
    {
        var m = new PolyMesh();
        // Very slightly tapered toward the far end, which reads as a joint when two of them
        // meet and costs nothing.
        m.AddBox(Color.White, LimbHalf, LimbHalf, LimbHalf * 0.86f, LimbHalf * 0.86f,
            -SegLen, 0f);
        return m;
    }

    /// <summary>A boot: wider than the shin and pushed forward, so the figure stands on
    /// something rather than balancing on the end of a stick. Its sole sits
    /// <see cref="SoldierSkeleton.SoleDrop"/> below the ankle it hangs from — which is the
    /// number the solver plants feet with.</summary>
    private static PolyMesh BuildBoot()
    {
        var m = new PolyMesh();
        m.AddBoxSpan(Color.White, -2.4f * U, 2.4f * U, -2f * U, 3.4f * U,
            -SoldierSkeleton.SoleDrop, 0.4f * U);
        return m;
    }

    /// <summary>A glove: a cube slightly fatter than the forearm, closing off the end of the
    /// arm. Pivots at the wrist so it swings with whatever the elbow is doing.</summary>
    private static PolyMesh BuildGlove()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, 2.3f * U, 2.3f * U, -2.4f * U, 0.2f * U);
        return m;
    }

    /// <summary>The belt, straddling the hip joint the pelvis pivots on.</summary>
    private static PolyMesh BuildBelt()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, BodyHalfW + 0.3f * U, BodyHalfD + 0.3f * U, -1.4f * U, 0.4f * U);
        return m;
    }

    /// <summary>
    /// Two straps front and back, running from the belt up over the shoulders. Built about
    /// the <em>waist</em>, since that is the joint they are now drawn from — a Y written
    /// about the hips comes out a body-segment too high and hangs the webbing over the
    /// figure's head.
    /// </summary>
    private static PolyMesh BuildStraps()
    {
        var m = new PolyMesh();
        foreach (float x in new[] { -2.6f * U, 1.4f * U })
        {
            m.AddBoxSpan(Color.White, x, x + 1.2f * U,
                BodyHalfD - 0.1f * U, BodyHalfD + 0.4f * U, -1f * U - WaistY, 11.5f * U - WaistY);
            m.AddBoxSpan(Color.White, x, x + 1.2f * U,
                -BodyHalfD - 0.4f * U, -BodyHalfD + 0.1f * U, -1f * U - WaistY, 11.5f * U - WaistY);
        }
        return m;
    }

    /// <summary>The bottle on the back: the reserve every jump and every reel spends. Its
    /// own mesh rather than part of the webbing, so it can settle a beat behind the chest
    /// it is strapped to instead of being welded to it.</summary>
    private static PolyMesh BuildBottle()
    {
        var m = new PolyMesh();
        m.AddBoxSpan(Color.White, -2.2f * U, 2.2f * U,
            -BodyHalfD - 1.8f * U, -BodyHalfD - 0.3f * U, 1f * U - WaistY, 10f * U - WaistY);
        return m;
    }

    /// <summary>One hip launcher: a blunt pressure housing with a short muzzle aimed
    /// forward. Pivots at its mount so it can be raised by the hand holding it, and swung
    /// on that mount by everything the body does.</summary>
    private static PolyMesh BuildLauncher()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, 1.4f * U, 1.8f * U, -2f * U, 1.6f * U);
        m.AddBoxSpan(Color.White, -0.9f * U, 0.9f * U, 1.8f * U, 5f * U, -1.2f * U, 0.6f * U);
        return m;
    }

    /// <summary>
    /// One blade: a long flat wedge running out of the fist, tapering to a point. Only the
    /// enemy squads draw these — the player's own chassis has a rifle instead — and it is
    /// deliberately the largest single thing on the figure, because it is the one part that
    /// has to be legible against a city at sixty metres.
    /// </summary>
    private static PolyMesh BuildBlade()
    {
        var m = new PolyMesh();
        // Held pointing forward from the wrist, out along the forearm's line.
        m.AddBox(Color.White, 0.55f * U, 3.2f * U, 0.25f * U, 0.9f * U, -9f * U, -1.5f * U);
        // The hilt, so it reads as held rather than as growing out of the glove.
        m.AddBox(Color.White, 0.9f * U, 1.1f * U, -1.6f * U, 0.4f * U);
        return m;
    }

    /// <summary>The hook seated in a launcher: a shank with two splayed flukes. Small, but
    /// it is the part of this chassis the whole class is about.</summary>
    private static PolyMesh BuildHook()
    {
        var m = new PolyMesh();
        m.AddBoxSpan(Color.White, -0.5f * U, 0.5f * U, 0f, 2.4f * U, -0.8f * U, 0.2f * U);
        m.AddBoxSpan(Color.White, -1.6f * U, -0.4f * U, 1.8f * U, 2.6f * U, -0.6f * U, 0.4f * U);
        m.AddBoxSpan(Color.White, 0.4f * U, 1.6f * U, 1.8f * U, 2.6f * U, -0.6f * U, 0.4f * U);
        return m;
    }
}
