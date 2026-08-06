using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Entities;

namespace Unrendered.Rendering;

/// <summary>
/// The plant. A sunflower, built out of the same hard facets everything else in this game is
/// built out of — no curves, no smooth shading, a head that is a hexagon because a hexagon is
/// what a disc reads as when it is forty pixels across.
///
/// <para>The model is a chain, like the fish's, and for the same reason: the physics care. A
/// flower's whole vocabulary of movement is <em>bending</em>, and a rigid stalk with a head
/// bolted on top is a lamp post. So the stem is four segments, each pivoting where the last one
/// ended, and the lean the rig computes is distributed down them with the tip taking most of
/// it — which is what makes a stalk read as a stalk rather than as a hinge with a stick in it.
/// The head is <em>not</em> in the chain past its own collar: a real sunflower's head is heavy
/// and hangs, so it counter-rotates against the bend and lags behind it.</para>
///
/// <para>The petals are drawn individually rather than as one ring mesh, which is the whole
/// reason the class reads at a glance: a petal that is out leaves a <em>gap</em>, and a player
/// can count what they have left off their own shadow. Each one also flutters on its own phase
/// offset, so the ring breathes instead of spinning. How many there are is
/// <see cref="FlowerRig.PetalCount"/>'s business and never this file's — the ring is always
/// walked, never counted out — which is what let it go from six to twelve without a single
/// change to the geometry beyond making each blade narrower.</para>
///
/// <para>Everything is built white and tinted at draw time, like every other chassis, so the
/// paint bay repaints the whole plant for free.</para>
/// </summary>
public sealed class FlowerModel
{
    // --- Proportions --------------------------------------------------------------
    // In metres, laid out up the model's own +Y. Heading 0 faces +Z, which is where the face
    // looks — a sunflower has a front, and it is the side the disc is on.

    /// <summary>How tall the plant stands at rest, root to collar. Deliberately just over head
    /// height on a person: this thing looks down at a soldier and up at a hunter.</summary>
    private const float StalkHeight = 2.95f;

    /// <summary>How many segments the stem bends through. Four is the fewest that reads as a
    /// curve rather than as an elbow, and every extra one costs a draw for no silhouette.</summary>
    public const int StemSegments = 4;

    private const float StemHalfW = 0.115f;    // at the base
    private const float StemTaper = 0.55f;     // how much thinner the top is than the bottom

    // The head is deliberately large against the stalk — larger than a real sunflower's, by
    // some way. At 320 pixels across, a head in honest proportion to a three-metre stem is a
    // dozen pixels of disc with six two-pixel petals on it, which reads as a pin. Everything a
    // player has to be able to see about this chassis (how many petals are left, which way it
    // is facing, whether the core is showing) lives on the head, so the head gets the room.
    private const float DiscRadius = 0.80f;
    private const float DiscDepth = 0.22f;

    /// <summary>How far up the collar the face is centred. Shared by the disc's own geometry
    /// and by the ring placement, so the petals cannot end up rooted off the face they belong
    /// to — which is exactly what happened while these were two separate numbers.</summary>
    private const float DiscCentreY = 0.26f;

    // Narrowed when the ring went from six to twelve. At the old width, twelve blades set
    // 30° apart overlapped into a continuous yellow annulus — which is what a sunflower
    // actually looks like, and is exactly wrong here: the whole readout of this class is
    // *counting the gaps*, and a ring with no space between its petals has no gaps to see.
    // Slightly longer at the same time, so the head keeps its reach against the disc.
    private const float PetalLength = 1.12f;
    private const float PetalHalfW = 0.135f;
    private const float SeedRadius = 0.24f;

    // --- Parts ---------------------------------------------------------------------
    // Each stem link spans y ∈ [0, len] from its own pivot, so a joint angle is a plain tilt on
    // the link and the next one starts wherever this one finished — the soldier's limbs and the
    // fish's tail chain are laid out the same way.

    private readonly PolyMesh _stem = BuildStemLink();
    private readonly PolyMesh _root = BuildRoot();
    private readonly PolyMesh _collar = BuildCollar();
    private readonly PolyMesh _disc = BuildDisc();
    private readonly PolyMesh _seed = BuildSeed();
    private readonly PolyMesh _leaf = BuildLeaf();
    private readonly PolyMesh _petal = BuildPetal();

    /// <summary>The petal on its own, for the renderer that draws the ones in flight. Shared
    /// geometry on purpose: the thing spinning across the field has to be recognisably the
    /// thing missing from the ring, and building it twice is how those quietly diverge.</summary>
    public PolyMesh PetalMesh => _petal;

    // --- The pose --------------------------------------------------------------------

    /// <summary>
    /// Every angle the plant needs for one frame. Public because the in-world draw builds one
    /// from the live rig rather than from a clock — the stalk the player sees bent is the stalk
    /// that actually moved their head off the root.
    /// </summary>
    public readonly record struct Pose(
        /// <summary>Which way the stem is bent, in the model's own frame, as (side, forward)
        /// radians of total bend spread down the chain.</summary>
        Vector2 Bend,
        /// <summary>How far the head hangs forward off the collar — the weight of it.</summary>
        float HeadDroop,
        /// <summary>How far the head is turned relative to the stem. The head aims; the stalk
        /// only leans, so on a plant that has just swung round to face something these two
        /// disagree for about a third of a second, which is exactly the lag that reads as
        /// mass.</summary>
        float HeadYaw,
        /// <summary>0..1. How far out of the ground the plant is: 1 is standing, 0 is a seed
        /// under the plate. The replant's whole animation is this one number.</summary>
        float Growth,
        /// <summary>Per-petal openness, 0..1. Zero is a slot with nothing in it; the sprout
        /// unfurls them one at a time by walking this array.</summary>
        float[] Petals,
        /// <summary>The idle clock, for the flutter and the breath.</summary>
        float Time);

    /// <summary>
    /// The hangar's idle: a slow sway, the head describing a lazy figure-of-eight, the whole
    /// ring fluttering out of phase with itself, and the plant breathing.
    ///
    /// The figure-of-eight is worth the two lines it costs. A plant that swayed on one axis
    /// reads as a metronome; two axes at different rates never repeat inside the time anybody
    /// looks at the turntable, and the thing appears to be idly <em>looking around</em>.
    /// </summary>
    public static Pose Idle(float elapsed, float turntable = 0f)
    {
        var open = new float[FlowerRig.PetalCount];
        for (int i = 0; i < open.Length; i++) open[i] = 1f;

        return new Pose(
            Bend: new Vector2(MathF.Sin(elapsed * 0.55f) * 0.10f,
                              MathF.Sin(elapsed * 0.83f + 1.1f) * 0.07f),
            HeadDroop: 0.16f + MathF.Sin(elapsed * 0.9f) * 0.03f,
            // Heliotropism, and the reason this chassis gets an idle nobody else has: the head
            // holds its face toward the viewer while the turntable carries the stalk round
            // underneath it. It is the one thing everybody already knows about a sunflower, it
            // is free (the head yaw already exists, for the lean's lag), and it fixes the real
            // problem the turntable has with this model — a disc is the whole read, and a disc
            // seen edge-on is a line.
            //
            // Deliberately imperfect: it lags the turn and drifts a few degrees either side, so
            // the plant reads as something *following* rather than as a billboard welded to the
            // camera.
            HeadYaw: -turntable + MathF.Sin(elapsed * 0.41f) * 0.20f,
            Growth: 1f,
            Petals: open,
            Time: elapsed);
    }

    /// <summary>
    /// The pose a live plant is actually in, read off its rig. Everything here is a direct
    /// consequence of something the simulation did, which is the whole bargain: nothing about
    /// how this thing looks is invented by the renderer.
    /// </summary>
    public static Pose From(FlowerRig stalk, float heading, float elapsed)
    {
        // The lean is in world space; the model bends in its own, so it has to be rotated into
        // the plant's frame or a flower facing south would bend north.
        float c = MathF.Cos(heading), s = MathF.Sin(heading);
        Vector2 lean = stalk.Lean;
        var local = new Vector2(lean.X * c - lean.Y * s, lean.X * s + lean.Y * c);

        // Bend angle from offset: the tip of a four-link chain of this length reaches its full
        // lean at roughly this much total tilt. Solved rather than eyeballed so retuning the
        // rig's MaxLean moves the drawn stalk with it instead of quietly desyncing the two.
        Vector2 bend = local / StalkHeight;

        var open = new float[FlowerRig.PetalCount];
        var ring = stalk.Petals;
        for (int i = 0; i < open.Length && i < ring.Count; i++)
            open[i] = ring[i].State == FlowerRig.PetalState.Seated ? 1f : 0f;

        float growth = GrowthOf(stalk);

        // A sprouting plant unfurls its ring one petal at a time across the back half of the
        // rise. This is the money shot of the whole class and it is four lines: each petal
        // opens over its own share of the window, so they come out in order rather than
        // together, and the last one lands exactly as the plant finishes standing up.
        //
        // At twelve the shares overlap heavily, and that is better than it was at six: what
        // used to be six distinct pops is now a wave running round the head, which is what
        // unfurling actually looks like.
        if (stalk.State == FlowerRig.Stance.Sprouting)
            for (int i = 0; i < open.Length; i++)
            {
                float from = 0.35f + 0.55f * (i / (float)FlowerRig.PetalCount);
                open[i] = Math.Clamp((stalk.StancePhase - from) / 0.16f, 0f, 1f);
            }

        return new Pose(
            Bend: bend,
            // The head hangs by its own weight, and hangs *further* the harder the stalk is
            // bent — a bent stem cannot hold a heavy head as level as a straight one. Plus a
            // slow breath so a plant standing perfectly still is never perfectly still.
            HeadDroop: 0.16f + bend.Length() * 0.55f + MathF.Sin(elapsed * 0.9f) * 0.02f,
            // The head lags the lean rather than following it: a mass on the end of a spring
            // arrives late. Negative, because a stalk bent to the player's left swings the head
            // to the right of where the stalk is pointing before it catches up.
            HeadYaw: -local.X * 0.10f,
            Growth: growth,
            Petals: open,
            Time: elapsed);
    }

    /// <summary>
    /// How far out of the ground a plant in each stance is. The wilt is eased hard at the end
    /// (a plant lets go of the plate all at once, having resisted) and the sprout is eased at
    /// the start (a shoot pushes slowly and then unrolls), which is most of what makes the two
    /// halves of a replant feel like different events rather than one animation run backwards.
    /// </summary>
    private static float GrowthOf(FlowerRig stalk) => stalk.State switch
    {
        FlowerRig.Stance.Rooted => 1f,
        FlowerRig.Stance.Wilting => 1f - stalk.StancePhase * stalk.StancePhase,
        FlowerRig.Stance.Seeded => 0f,
        _ => 1f - (1f - stalk.StancePhase) * (1f - stalk.StancePhase),
    };

    // --- Drawing -----------------------------------------------------------------------

    /// <summary>
    /// How far down the plant is scaled for the turntable.
    ///
    /// <para>It is the only chassis that needs this, and the reason is that it is the only one
    /// built at world scale to begin with: a tank's hangar model is about a metre long and gets
    /// grown three times over out in the run, whereas a flower is honestly three metres of stem
    /// in its own units because that is how tall it stands next to a hunter. Handed to the
    /// hangar unchanged it runs straight off the top of a frame the other five sit comfortably
    /// inside.</para>
    /// </summary>
    private const float HangarScale = 0.62f;

    /// <summary>The hangar turntable's draw: the plant standing in place, idling, brought down
    /// to a size the shared camera can actually frame.</summary>
    public void Draw(Loadout loadout, Vector2 pos, float heading, Vector3 cameraPos, float elapsed)
    {
        Rlgl.PushMatrix();
        Rlgl.Translatef(pos.X, 0f, pos.Y);
        Rlgl.Scalef(HangarScale, HangarScale, HangarScale);
        Rlgl.Translatef(-pos.X, 0f, -pos.Y);
        DrawPosed(loadout, pos, heading, 0f, cameraPos, Idle(elapsed, heading));
        Rlgl.PopMatrix();
    }

    /// <summary>
    /// The plant in whatever pose it is actually in. Walks the stem chain from the root up,
    /// carrying a running pivot and a running tilt exactly the way the fish's tail chain does,
    /// then puts the head on the end of it.
    /// </summary>
    public void DrawPosed(Loadout loadout, Vector2 pos, float heading, float baseHeight,
        Vector3 cameraPos, in Pose pose)
    {
        Color petals = loadout.PartColor(PlayerClass.Flower, 0);
        Color disc = loadout.PartColor(PlayerClass.Flower, 1);
        Color stem = loadout.PartColor(PlayerClass.Flower, 2);
        Color seed = loadout.PartColor(PlayerClass.Flower, 3);

        // A seed under the plate is nothing but a mound. Drawn rather than skipped, so the
        // ground a player is about to come up out of is visible to whoever is standing on it —
        // which is the only warning anybody gets, and they have earned it.
        if (pose.Growth <= 0.02f)
        {
            _root.Draw(pos, heading, baseHeight, cameraPos, 1f, stem);
            return;
        }

        _root.Draw(pos, heading, baseHeight, cameraPos, 1f, stem);

        // Up the chain. Each link takes an equal share of the bend, which puts the *tip* where
        // the rig said the head is (angles compound) while the base stays nearly upright —
        // a real stem's shape, and not one you get by tilting a single box.
        float linkLen = StalkHeight / StemSegments;
        var pivot = new Vector3(0f, baseHeight, 0f);
        float pitch = 0f, roll = 0f;
        float shareX = pose.Bend.Y / StemSegments;   // forward bend, about the model's X
        float shareZ = pose.Bend.X / StemSegments;   // side bend, about its Z

        for (int i = 0; i < StemSegments; i++)
        {
            // The lower links resist and the upper ones give — a stem is stiffer at the base,
            // and weighting the share is the difference between a curve and a fan.
            float give = 0.55f + 0.9f * (i / (float)(StemSegments - 1));
            pitch += shareX * give;
            roll += shareZ * give;

            float grow = Math.Clamp((pose.Growth - i * 0.16f) / 0.5f, 0f, 1f);
            if (grow <= 0.01f) break;

            DrawPart(_stem, pos, heading, 0f, cameraPos, pivot, stem, pitch, roll, grow);

            // The leaves, on the two middle links and on opposite sides, which is how a real
            // stem alternates them. They are the only thing on the plant that is wider than it
            // is tall, and at this resolution that is what stops the silhouette reading as a
            // pin with a badge on it.
            if (i == 1 || i == 2)
            {
                float side = i == 1 ? 1f : -1f;
                float flutter = MathF.Sin(pose.Time * 1.6f + i * 2.1f) * 0.12f;
                DrawPart(_leaf, pos, heading, side > 0f ? 0.9f : -0.9f, cameraPos,
                    pivot + new Vector3(0f, linkLen * 0.5f, 0f), stem,
                    pitch + flutter, roll, grow);
            }

            // Walk the pivot to the end of this link, along the link's own tilted axis.
            pivot += LinkStep(linkLen * grow, pitch, roll, heading);
        }

        if (pose.Growth < 0.35f) return;

        // --- The head -----------------------------------------------------------------
        // Everything from here hangs off the collar and takes the head's own droop and yaw on
        // top of the stem's bend, so it visibly lags what the stalk is doing.
        float headPitch = pitch + pose.HeadDroop;
        float headYaw = pose.HeadYaw;
        float headGrow = Math.Clamp((pose.Growth - 0.35f) / 0.4f, 0f, 1f);

        DrawPart(_collar, pos, heading, headYaw, cameraPos, pivot, stem, headPitch, roll, headGrow);
        DrawPart(_disc, pos, heading, headYaw, cameraPos, pivot, disc, headPitch, roll, headGrow);

        // The ring, in slot order, each on its own flutter phase. A slot whose petal is out
        // draws nothing at all — the gap is the readout.
        for (int i = 0; i < FlowerRig.PetalCount; i++)
        {
            float open = i < pose.Petals.Length ? pose.Petals[i] : 1f;
            if (open <= 0.02f) continue;

            float ring = MathF.Tau * i / FlowerRig.PetalCount;
            // A petal that is half-open is folded back along the disc rather than shrunk: a
            // scaled-down petal reads as a small petal, and a tilted one reads as one opening.
            float fold = (1f - open) * 1.5f;
            float flutter = MathF.Sin(pose.Time * 2.3f + i * 1.7f) * 0.06f * open;

            DrawPetalAt(pos, heading, headYaw, cameraPos, pivot, petals,
                headPitch, roll, ring, fold + flutter, headGrow * (0.4f + 0.6f * open));
        }

        // The one bright thing on it, and the last thing to arrive: a plant is not finished
        // until its core is showing. This is the game's whole convention for a living core and
        // this chassis is the only one wearing it honestly.
        if (headGrow > 0.7f)
            DrawPart(_seed, pos, heading, headYaw, cameraPos, pivot, seed, headPitch, roll,
                (headGrow - 0.7f) / 0.3f);
    }

    /// <summary>
    /// One petal seated in the ring at <paramref name="ringAngle"/> radians around the disc's
    /// face. Placed by hand rather than by a second mesh per slot: the petal is built lying
    /// along +Y from its own root, so rotating it about the face's normal and then tipping it
    /// out by the fold puts it exactly where a petal goes for any number of them.
    /// </summary>
    private void DrawPetalAt(Vector2 pos, float heading, float headYaw, Vector3 cameraPos,
        Vector3 pivot, Color tint, float pitch, float roll, float ringAngle, float fold, float scale)
    {
        // Around the face: the ring angle rolls the petal about the head's own forward axis.
        // The fold tips it back toward the disc, which is what an unopened one does.
        //
        // The X is NEGATED, and that is not a taste choice. PolyMesh's roll rotates a vector
        // about Z, so a petal built along +Y and rolled by r ends up pointing (−sin r, cos r).
        // Rooting it at (+sin r, cos r) put every petal on the opposite side of the disc from
        // the one it was pointing at — the ring turned inside out and folded into the face,
        // which at this resolution reads as a plant with no petals at all.
        float sr = MathF.Sin(ringAngle), cr = MathF.Cos(ringAngle);
        var offset = new Vector3(-sr * DiscRadius * 0.78f,
                                  cr * DiscRadius * 0.78f + DiscCentreY,
                                  DiscDepth * 0.4f);

        DrawPart(_petal, pos, heading, headYaw, cameraPos, pivot + RotateOffset(offset, pitch, roll),
            tint, pitch - fold * cr, roll + ringAngle - fold * sr, scale);
    }

    /// <summary>Where the end of a tilted link lands, in the model's own frame. The heading is
    /// not applied here — <see cref="DrawPart"/> does that — so a pivot accumulated up the
    /// chain stays in model space the whole way up.</summary>
    private static Vector3 LinkStep(float length, float pitch, float roll, float heading)
        => new(-MathF.Sin(roll) * length,
                MathF.Cos(pitch) * MathF.Cos(roll) * length,
                MathF.Sin(pitch) * length);

    /// <summary>An offset in the head's frame, brought back into the model's. Only the two
    /// tilts, because the heading is applied downstream.</summary>
    private static Vector3 RotateOffset(Vector3 v, float pitch, float roll)
    {
        float cr = MathF.Cos(roll), sr = MathF.Sin(roll);
        v = new Vector3(v.X * cr - v.Y * sr, v.X * sr + v.Y * cr, v.Z);
        float cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        return new Vector3(v.X, v.Y * cp - v.Z * sp, v.Y * sp + v.Z * cp);
    }

    /// <summary>
    /// Draws one part at a pivot expressed in the model's own frame — the same helper the fish
    /// uses, with a scale added, because this model grows out of the ground and every part of
    /// it has to be able to arrive at a fraction of its size.
    /// </summary>
    private static void DrawPart(PolyMesh mesh, Vector2 pos, float heading, float yaw,
        Vector3 cameraPos, Vector3 pivot, Color tint, float pitch, float roll, float scale)
    {
        float c = MathF.Cos(heading), s = MathF.Sin(heading);
        var at = new Vector2(
            pos.X + pivot.X * c + pivot.Z * s,
            pos.Y - pivot.X * s + pivot.Z * c);

        mesh.Draw(at, heading + yaw, pivot.Y, cameraPos, scale, tint, pitch, roll);
    }

    // --- Geometry -------------------------------------------------------------------
    // All of it built white and tinted at draw time. Hard facets only, per Doc 02 — there is
    // not a single curve in a plant that lives in this world.

    /// <summary>One link of the stem: a tapering square prism standing on its own pivot. Square
    /// rather than round on purpose — at 320 pixels across, a four-sided stem catches the
    /// directional light on two faces and reads as solid, and a rounder one reads as a smear.</summary>
    private static PolyMesh BuildStemLink()
    {
        float len = StalkHeight / StemSegments;
        return new PolyMesh().AddBox(Color.White,
            StemHalfW, StemHalfW,
            StemHalfW * StemTaper, StemHalfW * StemTaper,
            0f, len);
    }

    /// <summary>
    /// What is holding it down: a low mound with three root spurs pushed out of it. This is the
    /// part that says the thing is <em>attached</em>, and it is the reason a player looking at
    /// a flower understands the class before reading a word of the briefing.
    /// </summary>
    private static PolyMesh BuildRoot()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, 0.42f, 0.42f, 0.20f, 0.20f, 0f, 0.28f);

        // Three spurs, splayed at even bearings and biting into the plate. Deliberately not
        // six — a symmetric root looks machined, and this is the one thing in the game that
        // grew rather than being built.
        for (int i = 0; i < 3; i++)
        {
            float a = MathF.Tau * i / 3f + 0.4f;
            float sx = MathF.Sin(a), cz = MathF.Cos(a);
            var root = new Vector3(sx * 0.16f, 0.16f, cz * 0.16f);
            var toe = new Vector3(sx * 0.62f, 0.02f, cz * 0.62f);
            var side = new Vector3(cz * 0.10f, 0f, -sx * 0.10f);

            m.AddFace(Color.White, root + side, root - side, toe);
            m.AddFace(Color.White, root - side, root + side, toe + new Vector3(0f, 0.07f, 0f));
        }
        return m;
    }

    /// <summary>The collar the head sits in — a short flare where the stem widens to carry the
    /// disc. Small, and load-bearing for the silhouette: without it the head reads as balanced
    /// on a wire.</summary>
    private static PolyMesh BuildCollar()
        => new PolyMesh().AddBox(Color.White, 0.10f, 0.10f, 0.26f, 0.26f, -0.05f, 0.18f);

    /// <summary>
    /// The face: a hexagonal plate standing upright, looking out along +Z.
    ///
    /// Six sides for twelve petals, which is deliberate on both counts. Every other petal roots
    /// on a facet corner and the ones between root on a face, so the ring still visibly belongs
    /// to the disc rather than being a badge with petals glued round it — and the disc stays a
    /// hexagon, because a twelve-sided one at this resolution is a circle, and there are no
    /// circles in this game.
    /// </summary>
    private static PolyMesh BuildDisc()
    {
        var m = new PolyMesh();
        const int sides = 6;
        var front = new Vector3[sides];
        var back = new Vector3[sides];

        for (int i = 0; i < sides; i++)
        {
            float a = MathF.Tau * i / sides;
            float x = MathF.Sin(a) * DiscRadius, y = MathF.Cos(a) * DiscRadius;
            front[i] = new Vector3(x, y + DiscCentreY, DiscDepth);
            back[i] = new Vector3(x * 0.86f, y * 0.86f + DiscCentreY, -DiscDepth * 0.5f);
        }

        m.AddFace(Color.White, front);
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            m.AddFace(Color.White, front[j], front[i], back[i], back[j]);
        }
        m.AddFace(Color.White, back[5], back[4], back[3], back[2], back[1], back[0]);
        return m;
    }

    /// <summary>The core in the middle of the face: a small octahedron, the same solid every
    /// bright thing in this game is — the bolt, the grenade, the crab's gem. It is the one part
    /// of the plant that is unmistakably a <em>weak point</em>, and it is pointed at whatever
    /// the player is looking at, which is the class's own version of the spider's bargain.</summary>
    private static PolyMesh BuildSeed()
    {
        var m = new PolyMesh();
        var c = new Vector3(0f, DiscCentreY, DiscDepth + SeedRadius * 0.6f);
        Vector3 up = new(0f, SeedRadius, 0f), fwd = new(0f, 0f, SeedRadius * 0.8f);
        Vector3 side = new(SeedRadius, 0f, 0f);

        for (int i = 0; i < 4; i++)
        {
            Vector3 a = (i & 1) == 0 ? side : fwd;
            Vector3 b = (i & 2) == 0 ? fwd : -side;
            if (i == 1) { a = fwd; b = -side; }
            if (i == 2) { a = -side; b = -fwd; }
            if (i == 3) { a = -fwd; b = side; }
            m.AddFace(Color.White, c + up, c + a, c + b);
            m.AddFace(Color.White, c - up, c + b, c + a);
        }
        return m;
    }

    /// <summary>
    /// One petal, lying along +Y from its own root: a long flat blade that widens, then comes
    /// to a point. Two faces with a fold down the middle rather than a flat quad, so it catches
    /// the light on one half and not the other — which is the only thing that keeps a dozen
    /// identical petals from reading as one solid disc when the plant turns side-on.
    /// </summary>
    private static PolyMesh BuildPetal()
    {
        var m = new PolyMesh();

        var root = new Vector3(0f, 0f, 0f);
        var tip = new Vector3(0f, PetalLength, 0.02f);
        var wideL = new Vector3(-PetalHalfW, PetalLength * 0.42f, 0f);
        var wideR = new Vector3(PetalHalfW, PetalLength * 0.42f, 0f);
        // The fold: the spine runs slightly proud of the plane the edges sit in.
        var spine = new Vector3(0f, PetalLength * 0.42f, 0.09f);
        var spineNear = new Vector3(0f, PetalLength * 0.14f, 0.05f);

        m.AddFace(Color.White, root, wideR, spine, spineNear);
        m.AddFace(Color.White, root, spineNear, spine, wideL);
        m.AddFace(Color.White, spine, wideR, tip);
        m.AddFace(Color.White, spine, tip, wideL);
        return m;
    }

    /// <summary>A leaf: the same blade as a petal, shorter, much wider, and drooping. Built
    /// separately rather than reusing the petal at a scale, because a leaf that is only a fat
    /// petal makes the plant look like it has twelve petals, two of which fell down.</summary>
    private static PolyMesh BuildLeaf()
    {
        var m = new PolyMesh();

        var root = new Vector3(0f, 0f, 0f);
        var tip = new Vector3(0f, 0.30f, 0.92f);
        var wideL = new Vector3(-0.30f, 0.10f, 0.44f);
        var wideR = new Vector3(0.30f, 0.06f, 0.44f);
        var mid = new Vector3(0f, 0.16f, 0.40f);

        m.AddFace(Color.White, root, wideR, mid);
        m.AddFace(Color.White, root, mid, wideL);
        m.AddFace(Color.White, mid, wideR, tip);
        m.AddFace(Color.White, mid, tip, wideL);
        return m;
    }
}
