using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;

namespace VoidTanks.Rendering;

/// <summary>
/// The figure itself: the person the SOLDIER chassis is, standing on the hangar's
/// turntable. The only humanoid in this game, and the only model here that is
/// <em>articulated</em> — every other thing on the roster is one rigid mesh turned on
/// the spot, and a rigid person is a statue.
///
/// Proportions are lifted straight off the blocky-avatar convention everyone already
/// reads as "a person": a body eight wide, four deep and twelve tall, arms and legs four
/// by four by twelve, the whole figure thirty-two units to the crown. Everything below
/// is measured in those units (<see cref="U"/>) rather than in metres, so the shape can
/// be reasoned about in the grid it was designed in and comes out at 1.86m.
///
/// Two deliberate departures from that convention:
///
/// <list type="bullet">
/// <item>The head is a <em>pyramid</em>, not a cube. A cube head would make this a
/// mascot; a hard four-faced wedge keeps it in the same family as the tank's pyramidal
/// cap and the elite's cone, and — because a pyramid has a nose — it points, so the
/// figure has a facing you can read from any angle.</item>
/// <item>The limbs have <em>elbows and knees</em>. Each is two segments about a joint,
/// which costs one extra mesh per limb and buys every pose worth having: a stance with
/// weight on one leg, arms that hang with a bend in them rather than as planks, and a
/// gesture where the figure lifts a launcher and looks at it.</item>
/// </list>
///
/// Every part is built white and tinted at draw time, so the paint bay repaints the
/// whole figure for free — see <see cref="Loadout.PartColor"/>.
/// </summary>
public sealed class SoldierModel
{
    /// <summary>One unit of the blocky-avatar grid, in world metres. Thirty-two of them
    /// is the figure's full height.</summary>
    private const float U = 0.058f;

    // The skeleton's fixed measurements, in grid units.
    private const float BodyHalfW = 4f * U;
    private const float BodyHalfD = 2f * U;
    private const float HipY = 12f * U;      // where the legs meet the body
    private const float NeckY = 24f * U;     // top of the torso
    private const float LimbHalf = 2f * U;   // arms and legs are four across
    private const float SegLen = 6f * U;     // upper arm, forearm, thigh and shin alike
    private const float HeadHalf = 4f * U;
    private const float HeadTall = 8f * U;

    // Shoulders sit just outside the torso and just under the neck; hips sit inside it.
    private const float ShoulderX = BodyHalfW + LimbHalf;
    private const float ShoulderY = NeckY - 1f * U;
    private const float HipX = 2f * U;

    // --- Parts ------------------------------------------------------------------
    // Each is built with its pivot at the model origin — a limb hangs from y = 0 down to
    // y = −SegLen — because PolyMesh rotates about that origin. Getting this right is
    // the whole trick: it means a shoulder angle is just a pitch on the upper arm, and
    // an elbow is a pitch on the forearm placed at wherever the upper arm's end landed.

    private readonly PolyMesh _head = BuildHead();
    private readonly PolyMesh _torso = BuildTorso();
    private readonly PolyMesh _limb = BuildLimb();       // one segment: arm or leg
    private readonly PolyMesh _boot = BuildBoot();
    private readonly PolyMesh _glove = BuildGlove();
    private readonly PolyMesh _harness = BuildHarness();
    private readonly PolyMesh _launcher = BuildLauncher();
    private readonly PolyMesh _hook = BuildHook();

    /// <summary>
    /// Draws the figure, posed for <paramref name="elapsed"/>. The turntable's own
    /// rotation comes in as <paramref name="heading"/>; everything else here is the
    /// idle: breathing, a slow shift of weight from one leg to the other, a head that
    /// scans the hangar, and — every few seconds — the figure raising a launcher to
    /// check the hook seated in it.
    /// </summary>
    public void Draw(Loadout loadout, Vector2 pos, float heading, Vector3 cameraPos, float elapsed)
    {
        Color cloth = loadout.PartColor(PlayerClass.Soldier, 0);
        Color webbing = loadout.PartColor(PlayerClass.Soldier, 1);
        Color steel = loadout.PartColor(PlayerClass.Soldier, 2);
        Color cable = loadout.PartColor(PlayerClass.Soldier, 3);

        var pose = Pose.Idle(elapsed);

        // The torso and everything hanging off it ride the breath and the weight shift.
        var root = new Vector3(pose.Sway, pose.Rise, 0f);

        DrawPart(_torso, pos, heading, cameraPos, root + new Vector3(0f, HipY, 0f),
            cloth, pitch: pose.Lean, roll: pose.Tilt);
        DrawPart(_harness, pos, heading, cameraPos, root + new Vector3(0f, HipY, 0f),
            webbing, pitch: pose.Lean, roll: pose.Tilt);

        // The head sits on the neck and turns on its own — the one part with a yaw of
        // its own, which is what makes the figure look like it is watching the hangar
        // rather than facing wherever the turntable happens to have swung it.
        DrawPart(_head, pos, heading + pose.HeadYaw, cameraPos,
            root + new Vector3(0f, NeckY, 0f), cloth, pitch: pose.HeadNod);

        // --- Arms. Left first, then right, each shoulder → elbow → hand. ---
        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            float side = left ? 1f : -1f;
            var shoulder = root + new Vector3(side * ShoulderX, ShoulderY, 0f);

            float upper = left ? pose.LeftArm : pose.RightArm;
            float bend = left ? pose.LeftElbow : pose.RightElbow;
            float splay = side * (left ? pose.LeftSplay : pose.RightSplay);

            DrawPart(_limb, pos, heading, cameraPos, shoulder, cloth, upper, splay);

            // The elbow is wherever the upper arm's far end ended up. Solved rather than
            // eyeballed, so any shoulder angle at all keeps the forearm attached.
            Vector3 elbow = shoulder + Swing(SegLen, upper, splay);
            DrawPart(_limb, pos, heading, cameraPos, elbow, cloth, upper + bend, splay);

            // A glove on the end of it, in the webbing's colour. The figure is otherwise
            // one flat colour from the neck down, and at this size a silhouette needs
            // something breaking it up at the ends of the limbs or the arms disappear
            // into the torso entirely.
            DrawPart(_glove, pos, heading, cameraPos,
                elbow + Swing(SegLen, upper + bend, splay), webbing, upper + bend, splay);
        }

        // --- Legs: hip → knee → boot. ---
        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            float side = left ? 1f : -1f;
            var hip = root + new Vector3(side * HipX, HipY, 0f);

            float thigh = left ? pose.LeftThigh : pose.RightThigh;
            float knee = left ? pose.LeftKnee : pose.RightKnee;

            DrawPart(_limb, pos, heading, cameraPos, hip, cloth, thigh);
            Vector3 kneeAt = hip + Swing(SegLen, thigh, 0f);
            DrawPart(_limb, pos, heading, cameraPos, kneeAt, cloth, thigh + knee);
            // Boots in the harness colour for the same reason the gloves are: kit, not
            // clothing, and the contrast is what keeps the legs readable.
            DrawPart(_boot, pos, heading, cameraPos,
                kneeAt + Swing(SegLen, thigh + knee, 0f), webbing, thigh + knee);
        }

        // --- The rig: a launcher on each hip, with its hook seated in it. ---
        for (int i = 0; i < 2; i++)
        {
            bool left = i == 0;
            float side = left ? 1f : -1f;
            // Rides the launcher up on the arm that is doing the checking gesture, so
            // the thing being inspected is genuinely in the figure's hand.
            float lift = left ? pose.LeftRig : pose.RightRig;
            var at = root + new Vector3(side * (BodyHalfW + 1f * U), HipY + 1f * U + lift, 0f);

            DrawPart(_launcher, pos, heading, cameraPos, at, steel, roll: side * -0.12f);
            DrawPart(_hook, pos, heading, cameraPos,
                at + new Vector3(0f, 0f, 5f * U), cable);
        }
    }

    /// <summary>
    /// Where a limb segment's far end lands, given its length and the two angles it was
    /// swung by. Mirrors <see cref="PolyMesh"/>'s own transform exactly — roll about the
    /// local Z, then pitch about the local X — because the next segment down has to
    /// start where this one finished, and a joint that disagrees by a few degrees leaves
    /// a visible gap at every elbow.
    /// </summary>
    private static Vector3 Swing(float length, float pitch, float roll)
    {
        // The segment hangs along −Y before either rotation.
        var v = new Vector3(0f, -length, 0f);

        float cr = MathF.Cos(roll), sr = MathF.Sin(roll);
        v = new Vector3(v.X * cr - v.Y * sr, v.X * sr + v.Y * cr, v.Z);

        float cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        return new Vector3(v.X, v.Y * cp - v.Z * sp, v.Y * sp + v.Z * cp);
    }

    /// <summary>
    /// Draws one part at a pivot given in the <em>model's</em> frame, turning that pivot
    /// into a world placement. The mesh's own draw handles the rest, so a part is placed
    /// and posed in one call and nothing here has to know about matrices.
    /// </summary>
    private static void DrawPart(PolyMesh mesh, Vector2 pos, float heading, Vector3 cameraPos,
        Vector3 pivot, Color tint, float pitch = 0f, float roll = 0f)
    {
        float c = MathF.Cos(heading), s = MathF.Sin(heading);
        // The same X/Z rotation PolyMesh.Transform applies, so a pivot offset turns with
        // the figure instead of staying pinned to the world's axes.
        var at = new Vector2(
            pos.X + pivot.X * c + pivot.Z * s,
            pos.Y - pivot.X * s + pivot.Z * c);

        mesh.Draw(at, heading, pivot.Y, cameraPos, 1f, tint, pitch, roll);
    }

    // --- The idle pose ----------------------------------------------------------

    /// <summary>
    /// Every joint angle for one frame. Kept as a plain struct built by one function so
    /// the whole performance can be read in one place, and so a second pose (a walk, a
    /// death) is a second factory rather than a rewrite of the draw.
    /// </summary>
    private readonly record struct Pose(
        float Rise, float Sway, float Lean, float Tilt,
        float HeadYaw, float HeadNod,
        float LeftArm, float RightArm, float LeftElbow, float RightElbow,
        float LeftSplay, float RightSplay,
        float LeftThigh, float RightThigh, float LeftKnee, float RightKnee,
        float LeftRig, float RightRig)
    {
        /// <summary>
        /// Standing at ease. Four cycles running at deliberately unrelated rates — a
        /// breath, a weight shift, a head scan and a periodic check of the rig — so the
        /// idle never visibly loops even though every part of it is a sine.
        /// </summary>
        public static Pose Idle(float t)
        {
            // Breathing, and the slow transfer of weight from one leg to the other.
            float breath = MathF.Sin(t * 1.5f);
            float weight = MathF.Sin(t * 0.55f);

            // The gesture: every seven seconds the figure brings its right launcher up
            // and looks down at it, then lets it drop back. A single raised-cosine
            // window, so it eases in and out rather than snapping.
            float phase = (t % 7f) / 7f;
            float check = phase > 0.55f && phase < 0.95f
                ? 0.5f - 0.5f * MathF.Cos((phase - 0.55f) / 0.4f * MathF.Tau)
                : 0f;

            return new Pose(
                // Root: the breath lifts the chest, the weight shift slides the hips.
                Rise: breath * 0.012f,
                Sway: weight * 0.035f,
                Lean: breath * 0.015f,
                Tilt: -weight * 0.04f,

                // The head scans the hangar, and drops to watch the rig during a check.
                HeadYaw: MathF.Sin(t * 0.42f) * 0.4f - check * 0.5f,
                HeadNod: MathF.Sin(t * 1.1f) * 0.03f + check * 0.30f,

                // Arms hang with a real bend in them — the elbows are the point of this
                // model, so the resting pose is the one that shows them.
                LeftArm: -0.10f + breath * 0.03f,
                RightArm: -0.10f + breath * 0.03f - check * 0.95f,
                LeftElbow: 0.42f + breath * 0.04f,
                RightElbow: 0.42f + breath * 0.04f + check * 0.55f,
                LeftSplay: -0.10f - weight * 0.03f,
                RightSplay: -0.10f + weight * 0.03f - check * 0.18f,

                // Legs take the weight alternately: one straightens as the other bends.
                LeftThigh: 0.05f - weight * 0.04f,
                RightThigh: 0.05f + weight * 0.04f,
                LeftKnee: -0.14f - MathF.Max(0f, weight) * 0.1f,
                RightKnee: -0.14f - MathF.Max(0f, -weight) * 0.1f,

                // And the checked launcher rides up with the hand holding it.
                LeftRig: 0f,
                RightRig: check * 0.30f);
        }
    }

    // --- Geometry ---------------------------------------------------------------

    /// <summary>
    /// The head: a four-faced pyramid on a square base, apex up. The one part of the
    /// figure that isn't a box, and the reason it reads as this game's person rather
    /// than as a generic blocky avatar — it is the same hard wedge the tank wears as a
    /// cap and the elite is made of, at head size.
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

        // A narrow band across the front where a visor would be — one box, and the
        // pyramid stops being an abstract shape and becomes a helmet. Set at the depth
        // the sloping face has actually reached by that height, so it sits *in* the
        // front of the wedge rather than floating out ahead of it.
        const float visorY = 3f * U;
        float faceZ = h * (1f - visorY / HeadTall) + 1.2f * U * (visorY / HeadTall);
        m.AddBoxSpan(Color.White, -2.2f * U, 2.2f * U,
            faceZ - 0.6f * U, faceZ + 0.35f * U, visorY - 0.8f * U, visorY + 0.6f * U);
        return m;
    }

    /// <summary>The torso: eight across, four deep, twelve tall, pivoting at the hips.
    /// Tapered a touch inward at the waist so the figure has a shape rather than being a
    /// slab.</summary>
    private static PolyMesh BuildTorso()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, 3.4f * U, 1.8f * U, BodyHalfW, BodyHalfD, 0f, 12f * U);
        return m;
    }

    /// <summary>
    /// One limb segment — an upper arm, a forearm, a thigh or a shin, which in this
    /// convention are all the same four-by-four-by-six box. Built hanging from its
    /// pivot, so rotating it about the origin is exactly rotating it about its joint.
    /// </summary>
    private static PolyMesh BuildLimb()
    {
        var m = new PolyMesh();
        // Very slightly tapered toward the far end, which reads as a joint when two of
        // them meet and costs nothing.
        m.AddBox(Color.White, LimbHalf, LimbHalf, LimbHalf * 0.86f, LimbHalf * 0.86f,
            -SegLen, 0f);
        return m;
    }

    /// <summary>A boot: wider than the shin and pushed forward, so the figure stands on
    /// something rather than balancing on the end of a stick.</summary>
    private static PolyMesh BuildBoot()
    {
        var m = new PolyMesh();
        m.AddBoxSpan(Color.White, -2.4f * U, 2.4f * U, -2f * U, 3.4f * U, -1.6f * U, 0.4f * U);
        return m;
    }

    /// <summary>A glove: a cube slightly fatter than the forearm, closing off the end of
    /// the arm. Pivots at the wrist so it swings with whatever the elbow is doing.</summary>
    private static PolyMesh BuildGlove()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, 2.3f * U, 2.3f * U, -2.4f * U, 0.2f * U);
        return m;
    }

    /// <summary>
    /// The harness: a belt at the hips, two straps up over the chest, and the gas bottle
    /// on the back. Everything that makes the figure read as kitted rather than as a
    /// person standing in the hangar in their clothes.
    ///
    /// Built <em>hip-relative</em>, like every other part here — it is drawn from the
    /// same pivot as the torso, so a Y written in absolute model coordinates comes out
    /// twelve units too high and hangs the webbing over the figure's head.
    /// </summary>
    private static PolyMesh BuildHarness()
    {
        var m = new PolyMesh();

        // Belt, straddling the hip joint the whole thing pivots on.
        m.AddBox(Color.White, BodyHalfW + 0.3f * U, BodyHalfD + 0.3f * U,
            -1.4f * U, 0.4f * U);

        // Two straps front and back, running from the belt up over the shoulders.
        foreach (float x in new[] { -2.6f * U, 1.4f * U })
        {
            m.AddBoxSpan(Color.White, x, x + 1.2f * U,
                BodyHalfD - 0.1f * U, BodyHalfD + 0.4f * U, -1f * U, 11.5f * U);
            m.AddBoxSpan(Color.White, x, x + 1.2f * U,
                -BodyHalfD - 0.4f * U, -BodyHalfD + 0.1f * U, -1f * U, 11.5f * U);
        }

        // The bottle on the back: the reserve every jump and every reel spends.
        m.AddBoxSpan(Color.White, -2.2f * U, 2.2f * U,
            -BodyHalfD - 1.8f * U, -BodyHalfD - 0.3f * U, 1f * U, 10f * U);
        return m;
    }

    /// <summary>One hip launcher: a blunt pressure housing with a short muzzle aimed
    /// forward. Pivots at its mount so it can be raised by the hand holding it.</summary>
    private static PolyMesh BuildLauncher()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, 1.4f * U, 1.8f * U, -2f * U, 1.6f * U);
        m.AddBoxSpan(Color.White, -0.9f * U, 0.9f * U, 1.8f * U, 5f * U, -1.2f * U, 0.6f * U);
        return m;
    }

    /// <summary>The hook seated in a launcher: a shank with two splayed flukes. Small,
    /// but it is the part of this chassis the whole class is about.</summary>
    private static PolyMesh BuildHook()
    {
        var m = new PolyMesh();
        m.AddBoxSpan(Color.White, -0.5f * U, 0.5f * U, 0f, 2.4f * U, -0.8f * U, 0.2f * U);
        m.AddBoxSpan(Color.White, -1.6f * U, -0.4f * U, 1.8f * U, 2.6f * U, -0.6f * U, 0.4f * U);
        m.AddBoxSpan(Color.White, 0.4f * U, 1.6f * U, 1.8f * U, 2.6f * U, -0.6f * U, 0.4f * U);
        return m;
    }
}
