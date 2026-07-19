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
        // and deeper at the bottom, shrinking as it climbs. (hw, hd, y0, y1)
        (float hw, float hd, float y0, float y1)[] tiers =
        {
            (1.5f,  1.6f,  0.0f, 0.45f),  // base slab — the wide, grounded footing
            (1.12f, 1.2f,  0.45f, 0.95f), // second step
            (0.75f, 0.8f,  0.95f, 1.4f),  // third step
        };
        foreach (var (hw, hd, y0, y1) in tiers)
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
