using System.Numerics;
using Raylib_cs;
using Unrendered.Core;

namespace Unrendered.Rendering;

/// <summary>
/// The figure itself: the person the SOLDIER chassis is. The only humanoid in this game,
/// and the only model here that is <em>articulated</em> — every other thing on the roster
/// is one rigid mesh turned on the spot, and a rigid person is a statue.
///
/// Built straight off the blocky-avatar convention everyone already reads as "a person",
/// and built to it strictly: a torso eight wide, four deep and twelve tall, arms and legs
/// four by four by twelve, every one of them a plain straight slab with nothing tapered,
/// rounded or bolted on to blur it. The arms hang flush with the top of the shoulders and
/// end at the hips; the legs stand under their own halves of the torso and touch. That
/// silhouette is the whole point — it is recognisable as a body from across the city at
/// a dozen pixels tall, which nothing more realistic at this resolution would be. The
/// measurements live in <see cref="SoldierSkeleton"/> because the solver needs them too,
/// and the two must agree exactly.
///
/// Four deliberate departures from that convention:
///
/// <list type="bullet">
/// <item>The head is a <em>pyramid</em>, not a cube, on the same eight-by-eight base. A
/// cube head would make this a mascot; a hard four-faced wedge keeps it in the same family
/// as the tank's pyramidal cap and the elite's cone.</item>
/// <item>Across the front of it, where the eyes would be, one <em>black slit</em>. It is
/// the only thing on the figure that is never painted, and it is what turns the wedge into
/// a face: from any angle the eye finds it, and with it which way the figure is
/// looking.</item>
/// <item>The limbs have <em>elbows and knees</em>. Each is two segments about a joint, the
/// lower one reaching up a little way inside the upper so a bent joint closes over itself
/// instead of opening a notch. One extra slab per limb buys every pose worth having.</item>
/// <item>The torso is a <em>pelvis and a chest</em> rather than one block, so the shoulders
/// can disagree with the hips — counter-rotating through a stride, curling away from a
/// hit, twisting into a swing ahead of the legs that follow it.</item>
/// </list>
///
/// The colour is blocked the same way the convention blocks it: shirt over the torso and
/// sleeves, the trousers a shade darker, and the hands, boots, belt and helmet in the
/// harness colour. Every part is built white and tinted at draw time, so the paint bay
/// repaints the whole figure for free — see <see cref="Loadout.PartColor"/>.
///
/// This file only <em>draws</em>. Where every joint should be is decided by
/// <see cref="SoldierAnimator"/> and arrives as a finished <see cref="SoldierPose"/>, so
/// the model knows nothing about momentum, states or the simulation — and the same pose
/// drives the hangar turntable, an enemy squad, a team-mate on the wire, the copy the
/// creator makes, and the pair of arms held in the player's own view.
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
    private const float ArmSeg = SoldierSkeleton.ArmSeg;
    private const float LegSeg = SoldierSkeleton.LegSeg;
    private const float HeadHalf = SoldierSkeleton.HeadHalf;
    private const float HeadTall = SoldierSkeleton.HeadTall;

    /// <summary>The eye slit's colour: the void's own near-black, which is as black as this
    /// game ever gets.</summary>
    private static readonly Color Slit = Palette.Void;

    /// <summary>How much darker the trousers are than the shirt. Enough that the legs
    /// separate from the torso at a glance — the convention's shirt-and-trousers split —
    /// without needing a paint slot of their own.</summary>
    private const float TrouserShade = 0.68f;

    // --- Parts ------------------------------------------------------------------
    // Each is built with its pivot at the model origin — a limb hangs from y = 0 down —
    // because PolyMesh rotates about that origin. Getting this right is the whole trick:
    // it means a shoulder angle is just a pitch on the upper arm, and an elbow is a pitch
    // on the forearm placed at wherever the upper arm's end landed.

    private readonly PolyMesh _head = BuildHead();
    private readonly PolyMesh _eyes = BuildEyes();
    private readonly PolyMesh _pelvis = BuildPelvis();
    private readonly PolyMesh _chest = BuildChest();
    private readonly PolyMesh _upperArm = BuildUpperArm();
    private readonly PolyMesh _forearm = BuildLower(ArmSeg);
    private readonly PolyMesh _thigh = BuildThigh();
    private readonly PolyMesh _shin = BuildLower(LegSeg);
    private readonly PolyMesh _boot = BuildBoot();
    private readonly PolyMesh _glove = BuildGlove();
    private readonly PolyMesh _belt = BuildBelt();
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

    /// <summary>The trousers' colour, worked out from the shirt's.</summary>
    public static Color Trousers(Color cloth) => new(
        (byte)(cloth.R * TrouserShade), (byte)(cloth.G * TrouserShade),
        (byte)(cloth.B * TrouserShade), cloth.A);

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
        Color trousers = Trousers(cloth);

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

        // The bottle rides the chest but settles a beat behind it — the loose layer, which
        // on this figure is worn rather than grown.
        DrawPart(_bottle, pos, heading, cameraPos, waistAt, webbing,
            chestPitch + pose.BottlePitch, chestRoll + pose.BottleRoll, chestYaw);

        // The head sits on the neck and turns on its own, on top of whatever the chest is
        // already doing — which is what makes the figure look like it is watching something
        // rather than facing wherever the body happens to have swung it. The slit goes
        // with it, unpainted.
        Vector3 neck = SoldierSkeleton.Neck(pose);
        float headPitch = chestPitch + pose.HeadNod;
        float headRoll = chestRoll + pose.HeadTilt;
        float headYaw = chestYaw + pose.HeadYaw;
        DrawPart(_head, pos, heading, cameraPos, neck, webbing, headPitch, headRoll, headYaw);
        DrawPart(_eyes, pos, heading, cameraPos, neck, null, headPitch, headRoll, headYaw);

        // --- Arms. Left first, then right, each shoulder → elbow → hand. ---
        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            LimbPose arm = left ? pose.LeftArm : pose.RightArm;
            Vector3 shoulder = SoldierSkeleton.Shoulder(pose, left);

            DrawPart(_upperArm, pos, heading, cameraPos, shoulder, cloth, arm.UpperPitch, arm.UpperRoll);

            // The elbow is wherever the upper arm's far end ended up. Solved rather than
            // eyeballed, so any shoulder angle at all keeps the forearm attached.
            Vector3 elbow = shoulder + SoldierSkeleton.Segment(ArmSeg, arm.UpperPitch, arm.UpperRoll);
            DrawPart(_forearm, pos, heading, cameraPos, elbow, cloth, arm.LowerPitch, arm.LowerRoll);

            // The hand: the last stretch of the arm in the harness colour, flush with the
            // sleeve rather than a fist stuck on the end — the convention's bare hand, worn
            // here as a glove.
            Vector3 wrist = elbow + SoldierSkeleton.Segment(ArmSeg, arm.LowerPitch, arm.LowerRoll);
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

            DrawPart(_thigh, pos, heading, cameraPos, hip, trousers, leg.UpperPitch, leg.UpperRoll);
            Vector3 knee = hip + SoldierSkeleton.Segment(LegSeg, leg.UpperPitch, leg.UpperRoll);
            DrawPart(_shin, pos, heading, cameraPos, knee, trousers, leg.LowerPitch, leg.LowerRoll);

            // Boots in the harness colour for the same reason the gloves are: kit, not
            // clothing, and the contrast is what keeps the feet readable — and they tip
            // with the shin, so a foot leaving the ground pushes off its toe and one
            // arriving comes down on its heel without anybody animating either.
            Vector3 ankle = knee + SoldierSkeleton.Segment(LegSeg, leg.LowerPitch, leg.LowerRoll);
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
            float roll = side * -0.08f;

            DrawPart(_launcher, pos, heading, cameraPos, at, steel, pitch, roll);
            DrawPart(_hook, pos, heading, cameraPos,
                at + SoldierSkeleton.Rotate(new Vector3(0f, -0.3f * U, 3.4f * U), pitch, roll), cable,
                pitch, roll);
        }
    }

    /// <summary>
    /// Something held in one fist, drawn after the figure it belongs to with the same pose,
    /// scale and height. Placed at the wrist and turned with the forearm, exactly as the
    /// squads' blades are — so a modded weapon follows every swing, flinch and slash the
    /// animator puts the arm through. <paramref name="mesh"/> keeps its own colours: it is
    /// whatever the mod file said it was made of, not part of the figure's paint.
    /// </summary>
    public void DrawInHand(PolyMesh mesh, Vector2 pos, float height, float heading,
        Vector3 cameraPos, in SoldierPose pose, bool right, float scale = 1f)
    {
        _scale = scale;
        _baseY = height;

        bool left = !right;
        LimbPose arm = left ? pose.LeftArm : pose.RightArm;
        DrawPart(mesh, pos, heading, cameraPos, SoldierSkeleton.Wrist(pose, left), null,
            arm.LowerPitch, arm.LowerRoll);
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
        Vector3 pivot, Color? tint, float pitch = 0f, float roll = 0f, float yaw = 0f)
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
    /// The head: a four-faced pyramid on a square base, apex straight up over the middle.
    /// The one part of the figure that isn't a box, and the reason it reads as this game's
    /// person rather than as a generic blocky avatar — it is the same hard wedge the tank
    /// wears as a cap and the elite is made of, at head size. Symmetric, so the slit across
    /// the front is the only thing that says which way it faces.
    /// </summary>
    private static PolyMesh BuildHead()
    {
        var m = new PolyMesh();
        const float h = HeadHalf;

        Vector3 fl = new(-h, 0f, h), fr = new(h, 0f, h);
        Vector3 br = new(h, 0f, -h), bl = new(-h, 0f, -h);
        Vector3 apex = new(0f, HeadTall, 0f);

        m.AddFace(Color.White, bl, br, fr, fl);   // the underside, closing it off
        m.AddFace(Color.White, fl, fr, apex);
        m.AddFace(Color.White, fr, br, apex);
        m.AddFace(Color.White, br, bl, apex);
        m.AddFace(Color.White, bl, fl, apex);
        return m;
    }

    /// <summary>Where the slit sits on the front face, measured up from the head's base, and
    /// how far it stands proud of the face.</summary>
    private const float SlitLow = 2.5f * U;
    private const float SlitHigh = 3.5f * U;
    private const float SlitProud = 0.2f * U;

    /// <summary>
    /// The black line across the front of the head where the eyes would be. A raised band
    /// lying on the pyramid's sloping front face — its own mesh, because it is the one part
    /// of the figure the paint never reaches and a tint is all-or-nothing per mesh. Cut to
    /// the face's own taper, so it runs edge to edge as a single slit rather than a bar
    /// floating in front of a triangle.
    /// </summary>
    private static PolyMesh BuildEyes()
    {
        var m = new PolyMesh();

        // The front face is the plane through the two front base corners and the apex:
        // z = h·(1 − y / tall). Its outward normal leans up off horizontal by the slope.
        static Vector3 OnFace(float x, float y) => new(x, y, HeadHalf * (1f - y / HeadTall));
        Vector3 normal = Vector3.Normalize(new Vector3(0f, HeadHalf / HeadTall, 1f));
        Vector3 proud = normal * SlitProud;

        // Inset from the face's edges, so the slit reads as cut into a face and not as a
        // band wrapped round the corners.
        const float inset = 0.45f * U;
        float lowHalf = HeadHalf * (1f - SlitLow / HeadTall) - inset;
        float highHalf = HeadHalf * (1f - SlitHigh / HeadTall) - inset;

        Vector3 bl = OnFace(-lowHalf, SlitLow), br = OnFace(lowHalf, SlitLow);
        Vector3 tr = OnFace(highHalf, SlitHigh), tl = OnFace(-highHalf, SlitHigh);

        // The front of the band, and the four edges that stand it off the face.
        m.AddFace(Slit, bl + proud, br + proud, tr + proud, tl + proud);
        m.AddFace(Slit, bl, br, br + proud, bl + proud);
        m.AddFace(Slit, tl + proud, tr + proud, tr, tl);
        m.AddFace(Slit, bl, bl + proud, tl + proud, tl);
        m.AddFace(Slit, br + proud, br, tr, tr + proud);
        return m;
    }

    /// <summary>The lower part of the torso, from the hips to the waist. A straight slab,
    /// the same footprint as the chest above it.</summary>
    private static PolyMesh BuildPelvis()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, BodyHalfW, BodyHalfD, 0f, WaistY);
        return m;
    }

    /// <summary>The upper part, from the waist to the neck, pivoting at the waist. This is
    /// the part that gets to disagree with the hips.</summary>
    private static PolyMesh BuildChest()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, BodyHalfW, BodyHalfD, 0f, NeckY - WaistY);
        return m;
    }

    /// <summary>
    /// An upper arm: the four-by-four column hanging from the shoulder, with the shoulder's
    /// cap riding above the pivot so the top of the arm is flush with the top of the torso.
    /// Which is also how the convention's arm swings — about a point inside it, two units
    /// down — and why a raised arm tucks its shoulder into the body instead of lifting off
    /// it.
    /// </summary>
    private static PolyMesh BuildUpperArm()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, LimbHalf, LimbHalf, -ArmSeg, SoldierSkeleton.ShoulderCap);
        return m;
    }

    /// <summary>
    /// A thigh. Reaches a little way up past the hip, inside the pelvis and under the belt,
    /// so a leg swung forward closes the corner it opens rather than showing daylight
    /// between the trousers and the torso.
    /// </summary>
    private static PolyMesh BuildThigh()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, LimbHalf, LimbHalf, -LegSeg, 0.8f * U);
        return m;
    }

    /// <summary>
    /// A forearm or a shin: the lower half of a limb, hanging from the elbow or knee. Its top
    /// runs on up inside the segment above, a hair narrower so the two never fight over the
    /// same faces — hidden while the limb is straight, and exactly what fills the outside of
    /// the joint when it bends. Without it a bent knee is two boxes meeting at a corner with
    /// a wedge of nothing between them.
    /// </summary>
    private static PolyMesh BuildLower(float length)
    {
        var m = new PolyMesh();
        const float half = LimbHalf * 0.95f;
        m.AddBox(Color.White, half, half, -length, LimbHalf * 0.9f);
        return m;
    }

    /// <summary>A boot: the bottom of the leg in the harness colour, a touch proud of the
    /// shin and a touch longer at the toe so the foot has a front. Its sole sits
    /// <see cref="SoldierSkeleton.SoleDrop"/> below the ankle it hangs from — which is the
    /// number the solver plants feet with.</summary>
    private static PolyMesh BuildBoot()
    {
        var m = new PolyMesh();
        m.AddBoxSpan(Color.White, -2.15f * U, 2.15f * U, -2.15f * U, 2.75f * U,
            -SoldierSkeleton.SoleDrop, 2.2f * U);
        return m;
    }

    /// <summary>A glove: the bottom of the arm in the harness colour, just proud of the
    /// sleeve. Pivots at the wrist so it turns with whatever the elbow is doing.</summary>
    private static PolyMesh BuildGlove()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, 2.12f * U, 2.12f * U, -0.35f * U, 2.4f * U);
        return m;
    }

    /// <summary>The belt, straddling the hip joint the pelvis pivots on — the line where
    /// the shirt stops and the trousers start.</summary>
    private static PolyMesh BuildBelt()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, BodyHalfW + 0.25f * U, BodyHalfD + 0.25f * U, -0.7f * U, 0.9f * U);
        return m;
    }

    /// <summary>The bottle on the back: the reserve every jump and every reel spends. Its
    /// own mesh rather than part of the torso, so it can settle a beat behind the chest it
    /// is strapped to instead of being welded to it.</summary>
    private static PolyMesh BuildBottle()
    {
        var m = new PolyMesh();
        m.AddBoxSpan(Color.White, -1.9f * U, 1.9f * U,
            -BodyHalfD - 1.6f * U, -BodyHalfD, 0.6f * U, 6.8f * U);
        return m;
    }

    /// <summary>One hip launcher: a blunt pressure housing on the belt with a short muzzle
    /// aimed forward. Small, so it reads as kit worn on the body and not as a second pair of
    /// hips. Pivots at its mount so it can be raised by the hand holding it, and swung on
    /// that mount by everything the body does.</summary>
    private static PolyMesh BuildLauncher()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, 1.05f * U, 1.5f * U, -1.6f * U, 1.1f * U);
        m.AddBoxSpan(Color.White, -0.7f * U, 0.7f * U, 1.5f * U, 3.4f * U, -1.1f * U, 0.4f * U);
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
        m.AddBoxSpan(Color.White, -0.45f * U, 0.45f * U, 0f, 2f * U, -0.7f * U, 0.2f * U);
        m.AddBoxSpan(Color.White, -1.4f * U, -0.35f * U, 1.4f * U, 2.1f * U, -0.55f * U, 0.35f * U);
        m.AddBoxSpan(Color.White, 0.35f * U, 1.4f * U, 1.4f * U, 2.1f * U, -0.55f * U, 0.35f * U);
        return m;
    }
}
