using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;

namespace VoidTanks.Rendering;

/// <summary>
/// The creature itself: the thing the FISH chassis is, hanging in the hangar's air with
/// nothing to stand on. The second articulated model in this game and the only one that
/// is articulated for a reason the <em>physics</em> care about — a fish is a travelling
/// wave, and a rigid one is a prop.
///
/// So the back half is a chain: trunk → mid → peduncle → tail, each pivoting where the
/// last one ended, driven by one sine with a phase delay per joint. That delay is the
/// whole effect. Swing all three together and the model wags like a dog; delay each one
/// behind the one in front of it and the bend visibly travels backward down the body and
/// throws the tail out at the end of its run, which is what swimming <em>is</em>.
///
/// The head is deliberately not in the chain. Real carangiform swimmers hold their skull
/// nearly still and do all the work behind the gills, and a head that waved about would
/// read as a snake rather than as a fish.
///
/// Everything is built white and tinted at draw time, so the paint bay repaints the whole
/// animal for free — see <see cref="Loadout.PartColor"/>.
/// </summary>
public sealed class FishModel
{
    // --- Proportions -------------------------------------------------------------
    // In metres, laid out along the model's own +Z (which is where heading 0 faces). The
    // nose is at +Z and the tail trails off behind into -Z. About two and a half metres
    // nose to fluke, which sits it just inside the craft's own collision radius — a body
    // the size of the thing the rest of the game shoots at.

    private const float NoseZ = 1.30f;      // the point of the snout
    private const float TrunkBackZ = -0.55f; // where the rigid body ends and the chain starts

    private const float MidLen = 0.52f;
    private const float PeduncleLen = 0.42f;

    // Slimmer than it is deep and much longer than either, which is the whole silhouette:
    // a body this shape reads as built for travelling forward, and a fatter one reads as a
    // balloon with fins on it.
    private const float BodyHalfW = 0.21f;   // widest, at the shoulder
    private const float BodyHalfH = 0.34f;   // deepest, at the same place
    private const float ShoulderZ = 0.35f;   // where the body is fattest

    // --- Parts -------------------------------------------------------------------
    // Each chain link is built spanning z ∈ [−len, 0] from its own pivot, exactly the way
    // the soldier's limbs hang from theirs, so a joint angle is a plain yaw on the link
    // and the next one down starts wherever this one finished.

    private readonly PolyMesh _head = BuildHead();
    private readonly PolyMesh _jaw = BuildJaw();
    private readonly PolyMesh _teeth = BuildTeeth();
    private readonly PolyMesh _trunk = BuildTrunk();
    private readonly PolyMesh _belly = BuildBelly();
    private readonly PolyMesh _mid = BuildTaper(MidLen, BodyHalfW * 0.78f, BodyHalfH * 0.80f,
                                                       BodyHalfW * 0.42f, BodyHalfH * 0.52f);
    private readonly PolyMesh _peduncle = BuildTaper(PeduncleLen, BodyHalfW * 0.42f, BodyHalfH * 0.52f,
                                                                  BodyHalfW * 0.13f, BodyHalfH * 0.20f);
    private readonly PolyMesh _tail = BuildTail();
    private readonly PolyMesh _dorsal = BuildDorsal();
    private readonly PolyMesh _pectoral = BuildPectoral();
    private readonly PolyMesh _anal = BuildAnal();
    private readonly PolyMesh _stalk = BuildStalk();
    private readonly PolyMesh _bulb = BuildBulb();
    private readonly PolyMesh _socket = BuildSocket();
    private readonly PolyMesh _pupil = BuildPupil();

    /// <summary>
    /// Draws the animal, swimming in place for <paramref name="elapsed"/>. The turntable's
    /// rotation comes in as <paramref name="heading"/>; everything else here is the idle —
    /// the wave down the body, the pectorals sculling to hold station, a slow roll, and
    /// every few seconds a gulp that opens the jaw and swings the lure round in front of
    /// its own eyes.
    /// </summary>
    public void Draw(Loadout loadout, Vector2 pos, float heading, Vector3 cameraPos, float elapsed)
    {
        Color hide = loadout.PartColor(PlayerClass.Fish, 0);
        Color fin = loadout.PartColor(PlayerClass.Fish, 1);
        Color belly = loadout.PartColor(PlayerClass.Fish, 2);
        Color lure = loadout.PartColor(PlayerClass.Fish, 3);

        var pose = Pose.Swim(elapsed);
        DrawPosed(pos, heading, cameraPos, pose, hide, fin, belly, lure, HangarHover);
    }

    /// <summary>
    /// How high the specimen hangs in the hangar. Every other chassis on the roster stands
    /// on the turntable; this one has nothing to stand on and never will, so it floats — a
    /// full body's height clear of the plate, which is also the one detail that tells a
    /// player what the class is before they have read a word of the briefing.
    /// </summary>
    private const float HangarHover = 1.15f;

    /// <summary>
    /// The same animal, posed by the caller rather than by the idle. The in-world
    /// viewmodel drives this with a pose built from the live rig, so the body the player
    /// glimpses at the edges of their own view is the same geometry the hangar showed them
    /// rather than a second, differently-shaped fish.
    /// </summary>
    public void DrawPosed(Vector2 pos, float heading, Vector3 cameraPos, in Pose pose,
        Color hide, Color fin, Color belly, Color lure,
        float baseHeight = 0f, float pitch = 0f, float roll = 0f)
    {
        float y = baseHeight + pose.Rise;

        // --- The rigid front. Head, jaw, body and the fins hanging off it. ---
        var origin = new Vector3(0f, y, 0f);

        DrawPart(_trunk, pos, heading, 0f, cameraPos, origin, hide, pitch, roll);
        DrawPart(_belly, pos, heading, 0f, cameraPos, origin, belly, pitch, roll);
        DrawPart(_head, pos, heading, 0f, cameraPos, origin, hide, pitch, roll);

        // The jaw hinges at the back of the mouth, so a gulp swings it down and open
        // rather than sliding it apart.
        DrawPart(_jaw, pos, heading, 0f, cameraPos,
            origin + new Vector3(0f, -0.06f, 0.55f), belly, pitch + pose.Gape, roll);
        // Teeth in the belly's bone rather than the fins' dark, so the mouth is the pale
        // thing on a dark head — the same reading the Maw-Core's own tooth ring gets.
        DrawPart(_teeth, pos, heading, 0f, cameraPos,
            origin + new Vector3(0f, -0.06f, 0.55f), belly, pitch + pose.Gape, roll);

        // Eyes: a socket set into each side of the head with a bright pip in it. The pip
        // is in the lure's colour on purpose — on a thing that lives where no light
        // reaches, the parts that glow should all be the same kind of wrong.
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? 1f : -1f;
            var at = origin + new Vector3(side * 0.19f, 0.13f, 0.78f);
            DrawPart(_socket, pos, heading, 0f, cameraPos, at, belly, pitch, roll);
            DrawPart(_pupil, pos, heading, 0f, cameraPos,
                at + new Vector3(side * 0.035f, 0f, 0.02f), lure, pitch, roll);
        }

        // --- The lantern. A stalk off the crown, arcing out over the snout. ---
        // Two links rather than one so it reads as something dangling and led by its own
        // weight, which is the difference between a lure and an antenna.
        var crown = origin + new Vector3(0f, BodyHalfH * 0.78f, 0.60f);
        DrawPart(_stalk, pos, heading, pose.LureYaw, cameraPos, crown, hide,
            pitch + pose.LureBend, roll);

        Vector3 tip = crown + Bend(StalkLen, pose.LureYaw, pose.LureBend);
        DrawPart(_bulb, pos, heading, pose.LureYaw, cameraPos, tip, lure, pitch, roll);

        DrawPart(_dorsal, pos, heading, 0f, cameraPos,
            origin + new Vector3(0f, BodyHalfH * 0.92f, 0.1f), fin, pitch, roll);

        // --- Pectorals. One a side, sculling out of phase with each other. ---
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? 1f : -1f;
            float scull = i == 0 ? pose.LeftFin : pose.RightFin;
            DrawPart(_pectoral, pos, heading, side * pose.FinSweep, cameraPos,
                origin + new Vector3(side * BodyHalfW * 0.85f, -0.06f, 0.42f),
                fin, pitch, roll + side * scull);
        }

        // --- The chain. Each link starts where the last one ended. ---
        Vector3 midPivot = origin + new Vector3(0f, 0f, TrunkBackZ);
        DrawPart(_mid, pos, heading, pose.MidYaw, cameraPos, midPivot, hide, pitch, roll);

        Vector3 pedPivot = midPivot + Along(MidLen, pose.MidYaw);
        DrawPart(_peduncle, pos, heading, pose.TailYaw, cameraPos, pedPivot, hide, pitch, roll);
        DrawPart(_anal, pos, heading, pose.TailYaw, cameraPos,
            pedPivot + new Vector3(0f, -BodyHalfH * 0.42f, -0.06f), fin, pitch, roll);

        Vector3 flukePivot = pedPivot + Along(PeduncleLen, pose.TailYaw);
        DrawPart(_tail, pos, heading, pose.FlukeYaw, cameraPos, flukePivot, fin, pitch, roll);
    }

    /// <summary>
    /// Where a link's far end lands, given its length and its yaw — the model-frame twin
    /// of <see cref="PolyMesh"/>'s own rotation, so the next link down starts exactly
    /// where this one finished. A joint that disagreed by a few degrees would leave a
    /// visible gap at every vertebra.
    /// </summary>
    private static Vector3 Along(float length, float yaw)
        => new(-length * MathF.Sin(yaw), 0f, -length * MathF.Cos(yaw));

    /// <summary>
    /// Where the lure's stalk ends up. Unlike <see cref="Along"/> this one runs forward
    /// (+Z) and is pitched as well as yawed, so it mirrors <see cref="PolyMesh"/>'s full
    /// transform order — pitch about the local X, then the yaw about Y — which is the only
    /// way the lantern lands exactly on the end of the rod it is supposed to be attached to
    /// rather than floating somewhere near it.
    /// </summary>
    private static Vector3 Bend(float length, float yaw, float pitch)
    {
        float cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        // (0, 0, length) pitched about X. A negative pitch lifts it, which is why the pose
        // hands one in.
        var v = new Vector3(0f, -length * sp, length * cp);

        float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw);
        return new Vector3(v.X * cy + v.Z * sy, v.Y, -v.X * sy + v.Z * cy);
    }

    /// <summary>
    /// Draws one part at a pivot given in the model's own frame, turning that pivot into a
    /// world placement.
    ///
    /// The two headings are the point of this and are why it isn't the soldier's version:
    /// the <em>pivot</em> turns with the body's overall facing, while the <em>part</em>
    /// turns with that plus its own joint angle. Folding them together (which is safe for
    /// the soldier, whose only yawing part sits on the centreline) would swing every link
    /// of the chain around the model origin instead of around its own joint, and the fish
    /// would come apart the first time it flexed.
    /// </summary>
    private static void DrawPart(PolyMesh mesh, Vector2 pos, float heading, float yaw,
        Vector3 cameraPos, Vector3 pivot, Color tint, float pitch = 0f, float roll = 0f)
    {
        float c = MathF.Cos(heading), s = MathF.Sin(heading);
        var at = new Vector2(
            pos.X + pivot.X * c + pivot.Z * s,
            pos.Y - pivot.X * s + pivot.Z * c);

        mesh.Draw(at, heading + yaw, pivot.Y, cameraPos, 1f, tint, pitch, roll);
    }

    // --- The pose ----------------------------------------------------------------

    /// <summary>
    /// Every joint angle for one frame. Public because the in-world viewmodel builds one
    /// from the live rig rather than from a clock — the tail the player sees flick is the
    /// tail that actually pushed them forward.
    /// </summary>
    public readonly record struct Pose(
        float Rise,
        float MidYaw, float TailYaw, float FlukeYaw,
        float LeftFin, float RightFin, float FinSweep,
        float Gape, float LureYaw, float LureBend)
    {
        /// <summary>
        /// Holding station. The wave runs down the body at a fixed phase delay per joint
        /// and grows as it goes, which is what throws the fluke out hardest at the very end
        /// of its travel; the pectorals scull against each other to stay level; and every
        /// few seconds the animal gulps and its lantern swings round in front of it.
        /// </summary>
        public static Pose Swim(float t) => Build(
            wave: t * 2.1f,
            amplitude: 1f,
            // Two unrelated slow cycles so the idle never visibly loops.
            rise: MathF.Sin(t * 0.7f) * 0.045f,
            scull: t * 1.3f,
            sweep: 0f,
            gape: Gulp(t),
            lureSway: t * 0.55f);

        /// <summary>
        /// One gulp every five seconds: a single raised-cosine window, so the jaw eases
        /// open and shut rather than snapping. Same shape the soldier's rig-check uses,
        /// because it is the right shape for any occasional gesture.
        /// </summary>
        private static float Gulp(float t)
        {
            float phase = (t % 5f) / 5f;
            if (phase < 0.62f || phase > 0.86f) return 0f;
            return (0.5f - 0.5f * MathF.Cos((phase - 0.62f) / 0.24f * MathF.Tau)) * 0.42f;
        }

        /// <summary>
        /// The whole animal from five numbers. <paramref name="wave"/> is the tail's own
        /// phase in turns, <paramref name="amplitude"/> scales how hard it is working,
        /// <paramref name="sweep"/> pins the pectorals back against the body (a dive or a
        /// strike) and <paramref name="gape"/> opens the jaw.
        ///
        /// The phase delays — a fifth and two fifths of a cycle — are the load-bearing
        /// numbers here. They are what make the bend travel backward instead of the body
        /// bending all at once, and they are the only reason this reads as swimming.
        /// </summary>
        public static Pose Build(float wave, float amplitude, float rise, float scull,
            float sweep, float gape, float lureSway)
        {
            float a = Math.Clamp(amplitude, 0f, 1.6f);
            float w = wave * MathF.Tau;

            return new Pose(
                Rise: rise,
                // Growing as it goes: the mid barely moves, the fluke swings hard.
                MidYaw: MathF.Sin(w) * 0.10f * a,
                TailYaw: MathF.Sin(w - 1.26f) * 0.24f * a,
                FlukeYaw: MathF.Sin(w - 2.51f) * 0.42f * a,

                // The pectorals scull in opposition — one down as the other comes up —
                // which is how a fish holds a depth without going anywhere.
                LeftFin: MathF.Sin(scull * MathF.Tau) * 0.30f,
                RightFin: MathF.Sin(scull * MathF.Tau + MathF.PI) * 0.30f,
                FinSweep: sweep,

                Gape: gape,

                // The lantern trails: it is on the end of a stalk, so it always arrives a
                // beat after the head has finished moving. The bend is negative because
                // negative pitch is <em>up</em> — the rod lifts off the crown and carries
                // the light out over the snout, which is where an angler keeps it.
                LureYaw: MathF.Sin(lureSway * MathF.Tau) * 0.30f,
                LureBend: -0.34f + MathF.Sin(lureSway * MathF.Tau * 1.3f) * 0.10f);
        }
    }

    // --- Geometry ----------------------------------------------------------------

    /// <summary>
    /// The rigid forebody: a fat lozenge tapering from the shoulder back to where the
    /// chain takes over. Built as two sections meeting at the widest point, which is what
    /// gives the animal a shoulder crease instead of being a single smooth spindle — the
    /// flat shading needs a hard edge there or the whole flank reads as one facet.
    /// </summary>
    private static PolyMesh BuildTrunk()
    {
        var m = new PolyMesh();
        // Snout root → shoulder → the start of the chain. Three stations, two sections.
        Section(m, Color.White, 0.62f, BodyHalfW * 0.66f, BodyHalfH * 0.72f,
            ShoulderZ, BodyHalfW, BodyHalfH);
        Section(m, Color.White, ShoulderZ, BodyHalfW, BodyHalfH,
            TrunkBackZ, BodyHalfW * 0.78f, BodyHalfH * 0.80f);
        return m;
    }

    /// <summary>A pale underside, floated just below the flank so it reads as a second
    /// surface rather than as a stripe painted on the first. Countershading is the one
    /// marking every swimming thing has, and at this resolution it is worth more than any
    /// amount of extra geometry.</summary>
    private static PolyMesh BuildBelly()
    {
        var m = new PolyMesh();
        Section(m, Color.White, 0.70f, BodyHalfW * 0.44f, 0.05f,
            ShoulderZ, BodyHalfW * 0.66f, 0.06f, yOffset: -BodyHalfH * 0.80f);
        Section(m, Color.White, ShoulderZ, BodyHalfW * 0.66f, 0.06f,
            TrunkBackZ, BodyHalfW * 0.46f, 0.05f, yOffset: -BodyHalfH * 0.68f);
        return m;
    }

    /// <summary>
    /// The head: a hard four-sided wedge running out to a point. The same shape language
    /// as the tank's cap, the elite's cone and the soldier's pyramid head — this game's
    /// register for "the front of something" — and, like the soldier's, it means the
    /// animal has a facing readable from any angle.
    /// </summary>
    private static PolyMesh BuildHead()
    {
        var m = new PolyMesh();
        const float hw = BodyHalfW * 0.66f;
        const float hh = BodyHalfH * 0.72f;
        const float z0 = 0.62f;

        // The four faces of the wedge, from the head's root out to the snout. Nosed
        // slightly downward, which is what stops the profile reading as a dart.
        var tip = new Vector3(0f, -0.06f, NoseZ);
        Vector3 tl = new(-hw, hh, z0), tr = new(hw, hh, z0);
        Vector3 bl = new(-hw, -hh, z0), br = new(hw, -hh, z0);

        m.AddFace(Color.White, tl, tr, tip);
        m.AddFace(Color.White, tr, br, tip);
        m.AddFace(Color.White, br, bl, tip);
        m.AddFace(Color.White, bl, tl, tip);

        // The gill plate: one shallow step across the flank behind the eye. A single
        // crease, and the head stops being an abstract cone.
        m.AddBoxSpan(Color.White, -hw - 0.015f, hw + 0.015f, 0.60f, 0.66f, -hh * 0.7f, hh * 0.7f);
        return m;
    }

    /// <summary>The lower jaw, hinged at the back of the mouth so a gulp drops it open.
    /// Undershot, because a mouth that closes flush reads as a beak.</summary>
    private static PolyMesh BuildJaw()
    {
        var m = new PolyMesh();
        m.AddBoxSpan(Color.White, -BodyHalfW * 0.5f, BodyHalfW * 0.5f,
            0f, 0.66f, -0.12f, -0.02f);
        return m;
    }

    /// <summary>
    /// Teeth: a row of small spikes along the jaw. Eight of them, alternating up and down
    /// so the mouth reads as full rather than as a comb — and they are the one warm-toned
    /// part of the animal, which is exactly why the eye goes to them. The same trick the
    /// Maw-Core's bone ring plays, at a twentieth the size.
    /// </summary>
    private static PolyMesh BuildTeeth()
    {
        var m = new PolyMesh();
        for (int i = 0; i < 8; i++)
        {
            float t = i / 7f;
            float x = (t - 0.5f) * BodyHalfW * 0.9f;
            float z = 0.12f + t * 0.46f;
            bool upper = (i & 1) == 0;
            float y0 = upper ? -0.02f : -0.10f;
            float y1 = upper ? 0.07f : -0.02f;
            m.AddBoxSpan(Color.White, x - 0.016f, x + 0.016f, z - 0.02f, z + 0.02f, y0, y1);
        }
        return m;
    }

    /// <summary>One chain link: a slab tapering along its own length, pivoting at its
    /// front so a yaw on it is a yaw about its joint.</summary>
    private static PolyMesh BuildTaper(float length, float hw0, float hh0, float hw1, float hh1)
    {
        var m = new PolyMesh();
        Section(m, Color.White, 0f, hw0, hh0, -length, hw1, hh1);
        return m;
    }

    /// <summary>
    /// The tail: two flukes raked back off the peduncle, upper a little larger than lower.
    /// Asymmetric on purpose — a perfectly symmetric tail reads as a dart's fletching, and
    /// the whole point of this shape is that it is the thing doing the work.
    /// </summary>
    private static PolyMesh BuildTail()
    {
        var m = new PolyMesh();
        const float t = 0.022f;   // the flukes are membranes, near enough

        foreach (int sign in new[] { 1, -1 })
        {
            float span = sign > 0 ? 0.50f : 0.40f;
            // Root at the peduncle, sweeping back and out to a raked point.
            Vector3 root0 = new(-t, 0f, 0f), root1 = new(t, 0f, 0f);
            Vector3 mid0 = new(-t, sign * span * 0.55f, -0.16f);
            Vector3 mid1 = new(t, sign * span * 0.55f, -0.16f);
            Vector3 tip0 = new(-t, sign * span, -0.42f);
            Vector3 tip1 = new(t, sign * span, -0.42f);

            m.AddFace(Color.White, root0, mid0, tip0);
            m.AddFace(Color.White, tip1, mid1, root1);
            m.AddFace(Color.White, root0, root1, mid1, mid0);
            m.AddFace(Color.White, mid0, mid1, tip1, tip0);
        }
        return m;
    }

    /// <summary>The dorsal blade: a swept triangle standing off the back. Rides the trunk
    /// rather than the chain, so it stays a fixed landmark while everything behind it
    /// waves.</summary>
    private static PolyMesh BuildDorsal()
    {
        var m = new PolyMesh();
        const float t = 0.022f;

        // Kept low and long. A tall dorsal turns the silhouette into a shark fin and pulls
        // the eye off the body entirely; a shallow one raked backward reads as a keel,
        // which is what it is.
        Vector3 front0 = new(-t, 0f, 0.30f), front1 = new(t, 0f, 0.30f);
        Vector3 peak0 = new(-t, 0.21f, -0.04f), peak1 = new(t, 0.21f, -0.04f);
        Vector3 back0 = new(-t, 0.01f, -0.50f), back1 = new(t, 0.01f, -0.50f);

        m.AddFace(Color.White, front0, peak0, back0);
        m.AddFace(Color.White, back1, peak1, front1);
        m.AddFace(Color.White, front0, front1, peak1, peak0);
        m.AddFace(Color.White, peak0, peak1, back1, back0);
        return m;
    }

    /// <summary>A pectoral fin: a swept blade off the flank, pivoting at its root so it
    /// can scull and — on a dive or a strike — pin flat against the body.</summary>
    private static PolyMesh BuildPectoral()
    {
        var m = new PolyMesh();
        const float t = 0.018f;

        Vector3 root0 = new(0f, t, 0.12f), root1 = new(0f, -t, 0.12f);
        Vector3 tip0 = new(0.44f, t, -0.20f), tip1 = new(0.44f, -t, -0.20f);
        Vector3 heel0 = new(0.12f, t, -0.24f), heel1 = new(0.12f, -t, -0.24f);

        m.AddFace(Color.White, root0, tip0, heel0);
        m.AddFace(Color.White, heel1, tip1, root1);
        m.AddFace(Color.White, root0, root1, tip1, tip0);
        m.AddFace(Color.White, tip0, tip1, heel1, heel0);
        return m;
    }

    /// <summary>A small anal fin under the peduncle — the one part here that exists purely
    /// so the silhouette from below isn't a bare tube.</summary>
    private static PolyMesh BuildAnal()
    {
        var m = new PolyMesh();
        const float t = 0.016f;
        Vector3 f0 = new(-t, 0f, 0f), f1 = new(t, 0f, 0f);
        Vector3 p0 = new(-t, -0.17f, -0.10f), p1 = new(t, -0.17f, -0.10f);
        Vector3 b0 = new(-t, 0f, -0.26f), b1 = new(t, 0f, -0.26f);
        m.AddFace(Color.White, f0, p0, b0);
        m.AddFace(Color.White, b1, p1, f1);
        m.AddFace(Color.White, f0, f1, p1, p0);
        m.AddFace(Color.White, p0, p1, b1, b0);
        return m;
    }

    /// <summary>
    /// The lure's stalk: a thin rod running <em>forward</em> from its pivot on the crown.
    /// The one part here built along +Z rather than −Z, and deliberately so — every other
    /// link trails behind the thing it hangs off, whereas this one has to reach out over
    /// the snout and dangle the lantern where the animal's own eyes can see it. That is
    /// what an angler's illicium does and it is the whole silhouette.
    /// </summary>
    private static PolyMesh BuildStalk()
    {
        var m = new PolyMesh();
        m.AddBoxSpan(Color.White, -0.018f, 0.018f, 0f, StalkLen, -0.018f, 0.018f);
        return m;
    }

    /// <summary>How far the lantern is carried out in front of the crown.</summary>
    private const float StalkLen = 0.62f;

    /// <summary>
    /// The lantern on the end of it: an octahedron, which is this game's shape for a thing
    /// that is light rather than matter — the same solid the cannon's own bolt is. It is
    /// the single brightest object on the animal and the only one that isn't dead.
    /// </summary>
    private static PolyMesh BuildBulb()
    {
        var m = new PolyMesh();
        // Generous for its job. At 320 pixels across, the whole animal is barely a hundred
        // of them, so a lantern sized "correctly" against the body would be two pixels and
        // the one part that has to be unmistakable would be the one part nobody could see.
        const float r = 0.115f;
        Vector3 up = new(0f, r, 0f), down = new(0f, -r, 0f);
        Vector3 px = new(r, 0f, 0f), nx = new(-r, 0f, 0f);
        Vector3 pz = new(0f, 0f, r), nz = new(0f, 0f, -r);

        m.AddFace(Color.White, up, pz, px).AddFace(Color.White, up, px, nz);
        m.AddFace(Color.White, up, nz, nx).AddFace(Color.White, up, nx, pz);
        m.AddFace(Color.White, down, px, pz).AddFace(Color.White, down, nz, px);
        m.AddFace(Color.White, down, nx, nz).AddFace(Color.White, down, pz, nx);
        return m;
    }

    /// <summary>The eye socket: a shallow dish set into the head.</summary>
    private static PolyMesh BuildSocket()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, 0.075f, 0.075f, 0.055f, 0.055f, -0.075f, 0.055f);
        return m;
    }

    /// <summary>And the pip in it. Tiny, and lit — an eye that reads at ten metres on a
    /// 320-pixel-wide frame has to be a bright dot, not a dark one.</summary>
    private static PolyMesh BuildPupil()
    {
        var m = new PolyMesh();
        m.AddBox(Color.White, 0.032f, 0.032f, -0.032f, 0.032f);
        return m;
    }

    /// <summary>
    /// One section of body: a closed slab between two stations on the Z axis, each with its
    /// own half-width and half-height. This is the primitive the whole animal is built out
    /// of, and it exists because <see cref="PolyMesh.AddBox"/> tapers along Y — fine for a
    /// tank turret, useless for a body whose long axis is Z.
    /// </summary>
    private static void Section(PolyMesh m, Color c, float z0, float hw0, float hh0,
        float z1, float hw1, float hh1, float yOffset = 0f)
    {
        Vector3 a0 = new(-hw0, yOffset - hh0, z0), a1 = new(hw0, yOffset - hh0, z0);
        Vector3 a2 = new(hw0, yOffset + hh0, z0), a3 = new(-hw0, yOffset + hh0, z0);
        Vector3 b0 = new(-hw1, yOffset - hh1, z1), b1 = new(hw1, yOffset - hh1, z1);
        Vector3 b2 = new(hw1, yOffset + hh1, z1), b3 = new(-hw1, yOffset + hh1, z1);

        m.AddFace(c, a3, a2, a1, a0);   // the fore cap
        m.AddFace(c, b0, b1, b2, b3);   // the aft cap
        m.AddFace(c, a0, a1, b1, b0);   // underside
        m.AddFace(c, a2, a3, b3, b2);   // back
        m.AddFace(c, a1, a2, b2, b1);   // right flank
        m.AddFace(c, a3, a0, b0, b3);   // left flank
    }
}
