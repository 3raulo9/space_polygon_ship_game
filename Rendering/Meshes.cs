using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;

namespace VoidTanks.Rendering;

/// <summary>
/// Prebuilt flat-shaded shapes (Doc 02). Keep poly counts tiny and shapes
/// readable as silhouettes: a tank is a low wedge, an elite is a cone. Low
/// fidelity reads as menace when it emerges from fog. Meshes are immutable and
/// shared — build once at startup, never per frame.
///
/// Local convention: +Z is forward (matches heading 0), +Y is up. Units are
/// world units on the grid; a tank is a couple of units across.
/// </summary>
public static class Meshes
{
    /// <summary>
    /// The Grinder: a squat box-pyramid hunter. A wide flat base carries a stepped
    /// stack of ever-smaller boxes — a blunt ziggurat — topped by a hard pyramidal
    /// cap, with a single tapering gun jutting forward. Zero curves, every facet a
    /// flat colour with a hard crease, so it reads as a heavy chess-piece pyramid
    /// climbing out of the fog. Nose points +Z (heading 0).
    /// </summary>
    public static PolyMesh Tank(Color fill)
    {
        var m = new PolyMesh();

        // Stepped tiers of the box-pyramid: each box is centred on the axis, wider
        // and deeper at the bottom, shrinking as it climbs. (hw, hd, y0, y1) — shared
        // with the player's paintable hull, see HullTiers.
        foreach (var (hw, hd, y0, y1) in HullTiers)
            m.AddBox(fill, hw, hd, y0, y1);

        // Pyramidal cap: four hard facets rising from the top step to a single
        // apex point — the tell that makes the whole stack read as a pyramid.
        const float capHw = 0.75f, capHd = 0.8f, capY = 1.4f, apexY = 2.15f;
        Vector3 fl = new(-capHw, capY, capHd), fr = new(capHw, capY, capHd);
        Vector3 br = new(capHw, capY, -capHd), bl = new(-capHw, capY, -capHd);
        Vector3 apex = new(0f, apexY, 0f);
        m.AddFace(fill, fl, fr, apex); // front
        m.AddFace(fill, fr, br, apex); // right
        m.AddFace(fill, br, bl, apex); // back
        m.AddFace(fill, bl, fl, apex); // left

        // Gun barrel — a tapering square prism from the pyramid's flank, reaching
        // past the nose. Kept low on the second step so it clears the base and
        // still reads as a weapon rather than a spire.
        const float barrelY = 0.7f;
        const float baseHalf = 0.18f;    // half-thickness at the body
        const float muzzleHalf = 0.1f;   // narrower at the tip
        const float bz0 = 0.4f;          // starts inside the stack
        const float bz1 = 2.9f;          // pokes well past the base front
        AddTaperedBarrel(m, fill, barrelY, baseHalf, muzzleHalf, bz0, bz1);

        return m;
    }

    // --- The player's tank, broken into paintable parts ------------------------
    // Exactly the same geometry as Tank() above, split at its three natural seams so
    // the hangar can paint each one on its own. Kept as three separate meshes rather
    // than one multi-colour mesh because the colours are chosen at runtime and a
    // PolyMesh bakes its face colours at build time — rebuilding a whole tank every
    // time the player nudges a swatch would be silly, whereas re-tinting three parts
    // at draw time costs nothing (PolyMesh.Draw already takes a per-instance tint).
    // Every part is built white so that tint is the only thing deciding its colour.

    /// <summary>The stepped ziggurat body: the three boxes of <see cref="Tank"/>.</summary>
    public static PolyMesh TankHull(Color fill)
    {
        var m = new PolyMesh();
        foreach (var (hw, hd, y0, y1) in HullTiers)
            m.AddBox(fill, hw, hd, y0, y1);
        return m;
    }

    /// <summary>The pyramidal cap that tops the stack.</summary>
    public static PolyMesh TankCap(Color fill)
    {
        var m = new PolyMesh();
        const float capHw = 0.75f, capHd = 0.8f, capY = 1.4f, apexY = 2.15f;
        Vector3 fl = new(-capHw, capY, capHd), fr = new(capHw, capY, capHd);
        Vector3 br = new(capHw, capY, -capHd), bl = new(-capHw, capY, -capHd);
        Vector3 apex = new(0f, apexY, 0f);
        m.AddFace(fill, fl, fr, apex);
        m.AddFace(fill, fr, br, apex);
        m.AddFace(fill, br, bl, apex);
        m.AddFace(fill, bl, fl, apex);
        return m;
    }

    /// <summary>The tapering gun jutting past the nose.</summary>
    public static PolyMesh TankBarrel(Color fill)
    {
        var m = new PolyMesh();
        AddTaperedBarrel(m, fill, 0.7f, 0.18f, 0.1f, 0.4f, 2.9f);
        return m;
    }

    /// <summary>The hull's stepped tiers — the one definition <see cref="Tank"/> and
    /// <see cref="TankHull"/> both build from, so the enemy and the player's craft can
    /// never drift into being different shapes.</summary>
    private static readonly (float hw, float hd, float y0, float y1)[] HullTiers =
    {
        (1.5f,  1.6f,  0.0f,  0.45f),
        (1.12f, 1.2f,  0.45f, 0.95f),
        (0.75f, 0.8f,  0.95f, 1.4f),
    };

    /// <summary>
    /// A barrel as a tapering box along +Z: a square cross-section shrinking from
    /// <paramref name="baseHalf"/> at z0 to <paramref name="muzzleHalf"/> at z1,
    /// centred at height <paramref name="y"/>. Built by hand so all four sides
    /// taper and the muzzle caps flat.
    /// </summary>
    private static void AddTaperedBarrel(PolyMesh m, Color fill,
        float y, float baseHalf, float muzzleHalf, float z0, float z1)
    {
        float b = baseHalf, t = muzzleHalf;
        // Base ring (at turret), muzzle ring (at tip).
        Vector3 b0 = new(-b, y - b, z0), b1 = new(b, y - b, z0);
        Vector3 b2 = new(b, y + b, z0), b3 = new(-b, y + b, z0);
        Vector3 t0 = new(-t, y - t, z1), t1 = new(t, y - t, z1);
        Vector3 t2 = new(t, y + t, z1), t3 = new(-t, y + t, z1);

        m.AddFace(fill, t0, t1, t2, t3);   // muzzle cap
        m.AddFace(fill, b3, b2, b1, b0);   // base cap (into the turret; harmless)
        m.AddFace(fill, b3, b0, t0, t3);   // left
        m.AddFace(fill, b1, b2, t2, t1);   // right
        m.AddFace(fill, b2, b3, t3, t2);   // top
        m.AddFace(fill, b0, b1, t1, t0);   // bottom
    }

    /// <summary>
    /// An elite: a squat pyramidal cone. Its distinct silhouette emerging from
    /// fog should read as "this one is worse" (Doc 03). Apex points up; a slight
    /// forward lean is unnecessary — the cone alone is the tell.
    /// </summary>
    public static PolyMesh EliteCone(Color fill)
    {
        const float r = 1.4f;
        const float h = 2.4f;
        const int sides = 6; // hexagonal cone: still cheap, clearly "cone"

        var m = new PolyMesh();
        Vector3 apex = new(0f, h, 0f);

        var ring = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float ang = MathF.Tau * i / sides;
            ring[i] = new Vector3(MathF.Cos(ang) * r, 0f, MathF.Sin(ang) * r);
        }

        for (int i = 0; i < sides; i++)
        {
            Vector3 p0 = ring[i];
            Vector3 p1 = ring[(i + 1) % sides];
            m.AddFace(fill, p0, p1, apex);          // side face
        }
        // Base (wound so the normal faces down).
        for (int i = 1; i < sides - 1; i++)
            m.AddFace(fill, ring[0], ring[i + 1], ring[i]);

        return m;
    }

    // --- Placeholder polygon spaceships (test screen only) --------------------
    // Built in the same hard-facet, no-curves spirit as the tanks, but airborne:
    // these are the 90's vector-craft the test screen shows off. Nose points +Z.

    /// <summary>
    /// A sleek interceptor: a flat arrowhead delta with a raised dorsal spine and
    /// a belly keel, so it's a diamond head-on and a dart from above. Zero curves,
    /// eight facets — the cheapest thing that still reads as "fast".
    /// </summary>
    public static PolyMesh ShipInterceptor(Color fill)
    {
        var m = new PolyMesh();

        Vector3 nose = new(0f, 0.05f, 2.4f);
        Vector3 wingL = new(-1.8f, 0f, -1.0f);
        Vector3 wingR = new(1.8f, 0f, -1.0f);
        Vector3 tail = new(0f, 0f, -1.4f);
        Vector3 spine = new(0f, 0.55f, -0.1f);   // dorsal peak
        Vector3 keel = new(0f, -0.32f, -0.1f);   // belly point

        // Top surface: four panels meeting at the spine (wound CCW seen from above).
        m.AddFace(fill, nose, spine, wingR);
        m.AddFace(fill, wingR, spine, tail);
        m.AddFace(fill, spine, nose, wingL);
        m.AddFace(fill, spine, wingL, tail);

        // Belly: the same four panels mirrored down to the keel.
        m.AddFace(fill, nose, wingR, keel);
        m.AddFace(fill, wingR, tail, keel);
        m.AddFace(fill, nose, keel, wingL);
        m.AddFace(fill, wingL, tail, keel);

        return m;
    }

    /// <summary>
    /// A heavy gunship: a blunt slab hull with a raised rear bridge, a stubby nose
    /// block, and two engine nacelles slung off the flanks. Chunky and grounded-
    /// looking — the silhouette should read "this one is armoured".
    /// </summary>
    public static PolyMesh ShipGunship(Color fill)
    {
        var m = new PolyMesh();

        // Main hull slab.
        m.AddBox(fill, 0.78f, 1.7f, -0.36f, 0.34f);
        // Rear bridge perched on the deck.
        m.AddBoxSpan(fill, -0.42f, 0.42f, -1.45f, -0.25f, 0.34f, 0.9f);
        // Blunt nose block poking forward.
        m.AddBoxSpan(fill, -0.5f, 0.5f, 1.7f, 2.25f, -0.18f, 0.18f);
        // Two engine nacelles off the flanks, riding a touch lower than the hull.
        m.AddBoxSpan(fill, -1.55f, -0.9f, -1.25f, 0.85f, -0.3f, 0.22f);   // left
        m.AddBoxSpan(fill, 0.9f, 1.55f, -1.25f, 0.85f, -0.3f, 0.22f);     // right

        return m;
    }

    /// <summary>
    /// A tiny scout: a slim spindle fuselage with two swept-back winglets and a
    /// single upright tail fin — a needle, not a slab. Small and spindly so it
    /// reads as fragile, and it keeps a ship silhouette from any turn of the
    /// turntable (the fuselage never collapses to a flat wall the way panels do).
    /// </summary>
    public static PolyMesh ShipScout(Color fill)
    {
        var m = new PolyMesh();

        // Spindle fuselage: a diamond cross-section tapering to a nose and tail.
        Vector3 nose = new(0f, 0f, 1.35f);
        Vector3 tail = new(0f, 0f, -1.15f);
        const float r = 0.26f;
        Vector3 rTop = new(0f, r, 0f), rBot = new(0f, -r, 0f);
        Vector3 rL = new(-r, 0f, 0f), rR = new(r, 0f, 0f);

        // Nose cone (four facets) then tail cone (four facets).
        m.AddFace(fill, nose, rR, rTop); m.AddFace(fill, nose, rTop, rL);
        m.AddFace(fill, nose, rL, rBot); m.AddFace(fill, nose, rBot, rR);
        m.AddFace(fill, tail, rTop, rR); m.AddFace(fill, tail, rL, rTop);
        m.AddFace(fill, tail, rBot, rL); m.AddFace(fill, tail, rR, rBot);

        // Two swept-back winglets — flat triangles raking out and aft from mid-body.
        m.AddFace(fill, new Vector3(-0.15f, 0f, 0.25f),
                        new Vector3(-1.05f, 0f, -0.75f),
                        new Vector3(-0.15f, 0f, -0.55f)); // left
        m.AddFace(fill, new Vector3(0.15f, 0f, 0.25f),
                        new Vector3(0.15f, 0f, -0.55f),
                        new Vector3(1.05f, 0f, -0.75f));   // right

        // Upright tail fin so it never goes fully edge-on and invisible.
        m.AddFace(fill, new Vector3(0f, 0f, -0.35f),
                        new Vector3(0f, 0.7f, -1.05f),
                        new Vector3(0f, 0f, -1.05f));

        return m;
    }

    // --- Crab-Core boss parts -------------------------------------------------
    // The Stalker is a towering spider-thing: an octagonal open bowl carried high
    // on six long, raised-knee legs with clawed feet, a neon pyramid gem sitting in
    // the bowl's well. It's drawn as separate rigid parts the CrabRenderer poses
    // each frame — the carapace lid snaps down on the base (the "clamp"), the gem
    // spins, the legs skitter. Convention: +Z forward, +Y up, pivots at the origin.

    /// <summary>
    /// The upper carapace: an octagonal bowl with an open, magenta-dark well at its
    /// centre where the core sits. This is the "lid" — during the clamp it lifts and
    /// slams back onto the lower base, the two plates snapping shut around the core.
    /// Built about the origin so the renderer can raise it straight up.
    /// </summary>
    public static PolyMesh CrabBodyUpper(Color fill)
    {
        var m = new PolyMesh();
        Color wellDark = new(70, 22, 62, 255); // recessed, magenta-lit interior

        Vector3[] lipTop = Ngon(8, 2.7f, 1.6f);   // wide flat rim
        Vector3[] lipBot = Ngon(8, 2.3f, 0.85f);  // rim skirt bottom
        Vector3[] wellTop = Ngon(8, 1.7f, 1.6f);  // inner edge of the rim
        Vector3[] wellBot = Ngon(8, 1.25f, 0.7f); // floor of the well

        RingWall(m, fill, lipBot, lipTop);        // outer bevel of the rim
        RingCap(m, fill, lipTop, wellTop);        // flat top annulus
        RingWall(m, wellDark, wellBot, wellTop);  // inner well wall, dark
        NgonCap(m, wellDark, wellBot, up: true);  // well floor
        return m;
    }

    /// <summary>
    /// The lower base: a narrower octagonal tier under the lid that the legs bolt
    /// onto and the core stands on. Sits just below the lid's skirt, leaving the
    /// hard dark slot between the two plates that closes when the boss clamps.
    /// </summary>
    public static PolyMesh CrabBodyLower(Color fill)
    {
        var m = new PolyMesh();
        Vector3[] top = Ngon(8, 2.1f, 0.55f);
        Vector3[] bot = Ngon(8, 1.75f, -0.5f);
        RingWall(m, fill, bot, top);
        NgonCap(m, fill, top, up: true);
        NgonCap(m, fill, bot, up: false);
        return m;
    }

    /// <summary>
    /// The neon core: an apex-up square pyramid standing in the well, ringed by a
    /// few small shard bits that hang in the air around it (the reference's floating
    /// splinters). Built white via <paramref name="fill"/>; the renderer tints the
    /// whole thing magenta, or flashing red, per frame. Pivot at its base centre.
    /// </summary>
    public static PolyMesh CrabCoreGem(Color fill)
    {
        var m = new PolyMesh();
        const float r = 1.15f, h = 2.5f;
        Vector3 b0 = new(-r, 0f, -r), b1 = new(r, 0f, -r);
        Vector3 b2 = new(r, 0f, r), b3 = new(-r, 0f, r);
        Vector3 apex = new(0f, h, 0f);

        m.AddFace(fill, b0, b1, apex); m.AddFace(fill, b1, b2, apex);
        m.AddFace(fill, b2, b3, apex); m.AddFace(fill, b3, b0, apex);
        m.AddFace(fill, b3, b2, b1, b0); // base

        // A little constellation of shrapnel hanging around the upper pyramid.
        Vector3[] bits = { new(1.1f, 1.6f, 0.3f), new(-1.0f, 1.9f, -0.4f),
                           new(0.2f, 2.5f, 0.9f), new(-0.4f, 1.4f, 1.1f) };
        foreach (var p in bits) AddTetra(m, fill, p, 0.28f);
        return m;
    }

    /// <summary>
    /// One long spider leg: a femur strut rising up-and-out from the shoulder to a
    /// high knee, a tibia dropping back down-and-out to a foot far from the body,
    /// and a three-pronged claw at the foot. Built pointing +X about the shoulder
    /// (origin); the renderer fans six around the body and bobs them for the skitter.
    /// </summary>
    public static PolyMesh CrabLeg(Color fill)
    {
        var m = new PolyMesh();
        Vector3 shoulder = Vector3.Zero;
        Vector3 knee = Entities.CrabRig.Knee;
        Vector3 foot = Entities.CrabRig.Foot;

        AddStrut(m, fill, new Vector3(-0.25f, 0.1f, 0f), new Vector3(0.5f, 0.2f, 0f), 0.42f); // coxa
        AddStrut(m, fill, shoulder, knee, 0.34f);                     // femur
        AddTetra(m, fill, knee, 0.5f);                                // knee joint
        AddStrut(m, fill, knee, foot, 0.28f);                         // tibia

        // Foot claw: three short prongs raking down and out from the ankle.
        Vector3[] prongs = { new(0.5f, -1f, 0.4f), new(0.65f, -1f, 0f), new(0.5f, -1f, -0.4f) };
        foreach (var d in prongs)
            AddStrut(m, fill, foot, foot + Vector3.Normalize(d) * 0.95f, 0.11f);
        return m;
    }

    // --- Maw-Core parts -------------------------------------------------------
    // The Maw-Core is the Crab-Core with its middle torn out: it keeps the octagonal
    // carapace lid and the neon gem standing in the well (both reused verbatim from
    // the parts above — it is the same machine, which is the point), and where the
    // lower tier and the six legs used to bolt on there is now a throat. The pieces
    // below are only the new bottom half.

    /// <summary>
    /// The jaw: an octagonal funnel hanging under the carapace, tapering outward to a
    /// hard lip, with a dark gullet bored up through the middle of it. The gullet is
    /// modelled rather than faked because the player spends the digestion looking
    /// straight up it — from underneath, this is the whole silhouette of the thing.
    /// Pivot at the carapace's underside so the renderer can hang it off the body.
    /// </summary>
    public static PolyMesh MawJaw(Color fill)
    {
        var m = new PolyMesh();
        Color gullet = new(22, 14, 26, 255);   // wet, unlit throat

        Vector3[] shoulder = Ngon(8, 1.9f, 0f);      // where it meets the shell
        Vector3[] lipOut = Ngon(8, 2.35f, -0.75f);   // flared hard rim
        Vector3[] lipIn = Ngon(8, 1.75f, -0.9f);     // underside of that rim

        RingWall(m, fill, lipOut, shoulder);   // outer flank of the funnel
        RingCap(m, fill, lipOut, lipIn);       // the lip's flat underside

        // The gullet, bored back up into where the body's middle tier used to be. It
        // narrows as it climbs, so from below the eye is drawn up into a vanishing
        // point instead of stopping on a flat plate.
        Vector3[] throatLow = Ngon(8, 1.6f, -0.85f);
        Vector3[] throatMid = Ngon(8, 1.15f, 0.35f);
        Vector3[] throatTop = Ngon(8, 0.55f, 1.4f);
        RingWall(m, gullet, throatLow, throatMid);
        RingWall(m, gullet, throatMid, throatTop);
        NgonCap(m, gullet, throatTop, up: true);     // the dead end you get swallowed into
        return m;
    }

    /// <summary>
    /// One tooth: a slightly hooked spike pointing straight down, built about the
    /// origin so the renderer can fan a ring of them around the jaw and turn the whole
    /// ring. Hooked rather than straight because a cone reads as a spike and a spike
    /// reads as scenery — the kink is what makes it read as something meant to hold
    /// prey in.
    /// </summary>
    public static PolyMesh MawTooth(Color fill)
    {
        var m = new PolyMesh();
        const float r = 0.18f;
        Vector3 root0 = new(-r, 0f, -r), root1 = new(r, 0f, -r);
        Vector3 root2 = new(r, 0f, r), root3 = new(-r, 0f, r);
        // The tip is pulled inward on Z as well as down, so the ring of teeth curls
        // toward the throat's centre line like a trap rather than a crown.
        Vector3 tip = new(0f, -1.15f, -0.32f);

        m.AddFace(fill, root0, root1, tip); m.AddFace(fill, root1, root2, tip);
        m.AddFace(fill, root2, root3, tip); m.AddFace(fill, root3, root0, tip);
        m.AddFace(fill, root3, root2, root1, root0);   // cap where it sockets in
        return m;
    }

    /// <summary>
    /// A bead of the black stuff the thing leaks: a stretched octahedron, longer than
    /// it is wide so a falling drip reads as liquid rather than as another shard of
    /// debris. Drawn near-black, so what the player actually sees is a hole in the
    /// grid behind it falling to the floor.
    /// </summary>
    public static PolyMesh Ichor(Color fill)
    {
        var m = new PolyMesh();
        // Small. A bead of drool that reads correctly hanging off a monster twenty
        // units up is enormous by the time it falls past the player's face, so this is
        // sized for the near view — where it is at its most misleading — rather than
        // for the silhouette, where it barely registers anyway.
        const float w = 0.08f, h = 0.22f;
        Vector3 up = new(0f, h, 0f), dn = new(0f, -h * 1.4f, 0f);   // teardrop: pointed below
        Vector3 px = new(w, 0f, 0f), nx = new(-w, 0f, 0f);
        Vector3 pz = new(0f, 0f, w), nz = new(0f, 0f, -w);
        m.AddFace(fill, up, px, pz); m.AddFace(fill, up, pz, nx);
        m.AddFace(fill, up, nx, nz); m.AddFace(fill, up, nz, px);
        m.AddFace(fill, dn, pz, px); m.AddFace(fill, dn, nx, pz);
        m.AddFace(fill, dn, nz, nx); m.AddFace(fill, dn, px, nz);
        return m;
    }

    // --- Low-poly building blocks for the boss --------------------------------

    /// <summary>A square-section strut (prism) between two points — the leg segments.</summary>
    private static void AddStrut(PolyMesh m, Color c, Vector3 a, Vector3 b, float half)
    {
        Vector3 d = b - a;
        float len = d.Length();
        if (len < 1e-4f) return;
        d /= len;
        Vector3 refUp = MathF.Abs(d.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 u = Vector3.Normalize(Vector3.Cross(d, refUp)) * half;
        Vector3 v = Vector3.Normalize(Vector3.Cross(d, u)) * half;

        Vector3 a0 = a - u - v, a1 = a + u - v, a2 = a + u + v, a3 = a - u + v;
        Vector3 b0 = b - u - v, b1 = b + u - v, b2 = b + u + v, b3 = b - u + v;
        m.AddFace(c, a0, a1, b1, b0); m.AddFace(c, a1, a2, b2, b1);
        m.AddFace(c, a2, a3, b3, b2); m.AddFace(c, a3, a0, b0, b3);
        m.AddFace(c, a3, a2, a1, a0); m.AddFace(c, b0, b1, b2, b3);
    }

    /// <summary>A small hard chunk (irregular tetra) at a point — joints and shards.</summary>
    private static void AddTetra(PolyMesh m, Color c, Vector3 o, float s)
    {
        Vector3 a = o + new Vector3(0f, s, 0.15f * s);
        Vector3 b = o + new Vector3(0.9f * s, -0.4f * s, 0.8f * s);
        Vector3 d = o + new Vector3(-0.95f * s, -0.35f * s, 0.5f * s);
        Vector3 e = o + new Vector3(0.1f * s, -0.5f * s, -0.95f * s);
        m.AddFace(c, a, b, d); m.AddFace(c, a, d, e);
        m.AddFace(c, a, e, b); m.AddFace(c, b, e, d);
    }

    /// <summary>Ring of n verts at radius r, height y, with a flat top edge forward.</summary>
    private static Vector3[] Ngon(int n, float r, float y)
    {
        var ring = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float ang = MathF.Tau * i / n + MathF.PI / n;
            ring[i] = new Vector3(MathF.Cos(ang) * r, y, MathF.Sin(ang) * r);
        }
        return ring;
    }

    /// <summary>Quad wall connecting a lower ring to an upper ring (same vert count).</summary>
    private static void RingWall(PolyMesh m, Color c, Vector3[] lower, Vector3[] upper)
    {
        int n = lower.Length;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            m.AddFace(c, lower[i], lower[j], upper[j], upper[i]);
        }
    }

    /// <summary>Flat annulus between an outer ring and an inner ring at the same height.</summary>
    private static void RingCap(PolyMesh m, Color c, Vector3[] outer, Vector3[] inner)
    {
        int n = outer.Length;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            m.AddFace(c, outer[i], outer[j], inner[j], inner[i]);
        }
    }

    /// <summary>Solid n-gon cap, wound up or down.</summary>
    private static void NgonCap(PolyMesh m, Color c, Vector3[] ring, bool up)
    {
        int n = ring.Length;
        for (int i = 1; i < n - 1; i++)
            if (up) m.AddFace(c, ring[0], ring[i], ring[i + 1]);
            else m.AddFace(c, ring[0], ring[i + 1], ring[i]);
    }

    /// <summary>
    /// A jagged debris chunk — an irregular tetrahedron, the cheapest thing that
    /// still reads as a hard broken-off piece tumbling through the air. Built at
    /// unit-ish size; the debris system scales and tints each instance.
    /// </summary>
    public static PolyMesh Shard(Color fill)
    {
        var m = new PolyMesh();
        Vector3 a = new(0f, 0.55f, 0.1f);
        Vector3 b = new(0.5f, -0.25f, 0.45f);
        Vector3 c = new(-0.55f, -0.2f, 0.3f);
        Vector3 d = new(0.05f, -0.3f, -0.6f);
        m.AddFace(fill, a, b, c);
        m.AddFace(fill, a, c, d);
        m.AddFace(fill, a, d, b);
        m.AddFace(fill, b, d, c);
        return m;
    }

    // --- Floating pickups -----------------------------------------------------
    // Salvage drifting on the grid, built in the same hard-facet spirit as the rest.
    // Both stand upright about the origin and are spun/bobbed by the renderer.

    /// <summary>
    /// A polygon battery cell: an upright hexagonal can with a bright charge band
    /// wrapping its middle and a small positive terminal nub on the crown. Reads as
    /// a chunky power cell from any angle. Refuels shield + hyper when driven over.
    /// </summary>
    public static PolyMesh Battery(Color body, Color core)
    {
        var m = new PolyMesh();
        const int sides = 6;
        const float r = 0.6f;

        // Four stacked rings split the can into three bands: base, a bright charge
        // stripe, then the crown — the stripe is the tell that it's a battery.
        float[] ys = { 0f, 0.55f, 1.05f, 1.5f };
        Color[] bands = { body, core, body };
        var rings = new Vector3[ys.Length][];
        for (int k = 0; k < ys.Length; k++)
        {
            rings[k] = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = MathF.Tau * i / sides + MathF.PI / sides;
                rings[k][i] = new Vector3(MathF.Cos(a) * r, ys[k], MathF.Sin(a) * r);
            }
        }
        for (int k = 0; k < ys.Length - 1; k++)
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                m.AddFace(bands[k], rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]);
            }
        // End caps: base down, crown up.
        for (int i = 1; i < sides - 1; i++)
        {
            m.AddFace(body, rings[0][0], rings[0][i + 1], rings[0][i]);
            m.AddFace(body, rings[^1][0], rings[^1][i], rings[^1][i + 1]);
        }
        // Positive terminal: a small bright box on the crown.
        m.AddBox(core, 0.22f, 0.22f, 1.5f, 1.78f);
        return m;
    }

    /// <summary>
    /// A stray round: a hexagonal casing capped by an ogive (pointed) jacket —
    /// a floating bullet. Stands tip-up about the origin. Restocks ammo when
    /// driven over.
    /// </summary>
    public static PolyMesh Bullet(Color jacket, Color casing)
    {
        var m = new PolyMesh();
        const int sides = 6;
        const float r = 0.34f;
        const float y0 = 0f, yShoulder = 0.95f, apexY = 1.7f;

        var bot = new Vector3[sides];
        var sh = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = MathF.Tau * i / sides + MathF.PI / sides;
            float cx = MathF.Cos(a) * r, cz = MathF.Sin(a) * r;
            bot[i] = new Vector3(cx, y0, cz);
            sh[i] = new Vector3(cx, yShoulder, cz);
        }
        // Casing wall + base cap.
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            m.AddFace(casing, bot[i], bot[j], sh[j], sh[i]);
        }
        for (int i = 1; i < sides - 1; i++)
            m.AddFace(casing, bot[0], bot[i + 1], bot[i]);
        // Ogive jacket: facets rising from the shoulder to a single tip.
        Vector3 tip = new(0f, apexY, 0f);
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            m.AddFace(jacket, sh[i], sh[j], tip);
        }
        return m;
    }

    /// <summary>A tiny projectile bolt — a small bright shard, barely a shape.</summary>
    public static PolyMesh Bolt(Color fill) => Octahedron(fill, 0.22f);

    /// <summary>The heavy grenade round — a chunkier shard than a bolt, same form.</summary>
    public static PolyMesh Grenade(Color fill) => Octahedron(fill, 0.5f);

    private static PolyMesh Octahedron(Color fill, float s)
    {
        var m = new PolyMesh();
        // A little octahedron: reads as a hot point from any angle.
        Vector3 up = new(0f, s, 0f), dn = new(0f, -s, 0f);
        Vector3 px = new(s, 0f, 0f), nx = new(-s, 0f, 0f);
        Vector3 pz = new(0f, 0f, s), nz = new(0f, 0f, -s);
        m.AddFace(fill, up, px, pz);
        m.AddFace(fill, up, pz, nx);
        m.AddFace(fill, up, nx, nz);
        m.AddFace(fill, up, nz, px);
        m.AddFace(fill, dn, pz, px);
        m.AddFace(fill, dn, nx, pz);
        m.AddFace(fill, dn, nz, nx);
        m.AddFace(fill, dn, px, nz);
        return m;
    }
}
