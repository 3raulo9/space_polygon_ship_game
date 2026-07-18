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
    /// An abstract combat tank built the old way: a stack of hard geometric
    /// primitives, zero curves. Two flat track plates carry a wedged hull box;
    /// a smaller turret box sits on top; a long tapering barrel juts forward.
    /// Every facet is one flat colour with a hard crease at each edge, so it
    /// reads as a menacing chess-piece silhouette from across the void — not a
    /// blob hugging the floor. Nose points +Z (heading 0).
    /// </summary>
    public static PolyMesh Tank(Color fill)
    {
        var m = new PolyMesh();

        // Overall footprint. The tank stands ~2 units tall so it has real
        // vertical presence at the camera's eye height.
        const float hw = 1.15f;   // half width (outer edge of the tracks)
        const float hl = 1.7f;    // half length (front-to-back)

        // 1) Track plates — two flat rectangular boxes flanking the centreline,
        //    sitting on the grid. They give the tank its wide, grounded base.
        const float trackH = 0.5f;        // top of the tracks
        const float trackW = 0.42f;       // width of each plate
        const float trackZ = hl;          // run the full length
        m.AddBoxSpan(fill, -hw, -hw + trackW, -trackZ, trackZ, 0f, trackH);       // left track
        m.AddBoxSpan(fill, hw - trackW, hw, -trackZ, trackZ, 0f, trackH);         // right track

        // 2) Hull — a wedged box raised on the tracks, narrower than the track
        //    span. The front face is a sloped glacis (a single hard flat facet).
        const float hullHw = 0.9f;
        const float hullHd = 1.45f;
        const float hullBottom = trackH - 0.05f; // sits just into the tracks
        const float hullBackTop = 1.35f;         // tall at the back
        const float hullNoseTop = 0.85f;         // lower at the front → glacis slope

        // Hull as a box with a lowered front-top edge (built explicitly so the
        // glacis is its own clean facet).
        Vector3 hb_bl = new(-hullHw, hullBottom, -hullHd); // back-left bottom
        Vector3 hb_br = new(hullHw, hullBottom, -hullHd);
        Vector3 hf_bl = new(-hullHw, hullBottom, hullHd);  // front-left bottom
        Vector3 hf_br = new(hullHw, hullBottom, hullHd);
        Vector3 hb_tl = new(-hullHw, hullBackTop, -hullHd); // back-left top
        Vector3 hb_tr = new(hullHw, hullBackTop, -hullHd);
        Vector3 hf_tl = new(-hullHw, hullNoseTop, hullHd);  // front-left top (lower)
        Vector3 hf_tr = new(hullHw, hullNoseTop, hullHd);

        m.AddFace(fill, hb_tl, hb_tr, hf_tr, hf_tl); // top deck (slopes forward)
        m.AddFace(fill, hb_br, hb_bl, hb_tl, hb_tr); // back
        m.AddFace(fill, hf_bl, hf_br, hf_tr, hf_tl); // front glacis
        m.AddFace(fill, hb_bl, hf_bl, hf_tl, hb_tl); // left
        m.AddFace(fill, hf_br, hb_br, hb_tr, hf_tr); // right
        m.AddFace(fill, hb_bl, hb_br, hf_br, hf_bl); // bottom

        // 3) Turret — a smaller box perched on the rear of the hull deck. Sitting
        //    it back leaves the sloped glacis clear and makes the barrel reach.
        const float turHw = 0.62f;
        const float turHd = 0.7f;
        const float turZ = -0.35f;        // shifted toward the back
        const float turBottom = hullBackTop - 0.05f;
        const float turTop = turBottom + 0.55f;
        m.AddBoxSpan(fill, -turHw, turHw, turZ - turHd, turZ + turHd, turBottom, turTop);

        // 4) Gun barrel — a long tapering rectangular prism from the turret face.
        //    Wider at the base, narrowing to the muzzle: an elongated pyramid-ish
        //    prism, no cylinder. Reaches out past the nose.
        float barrelY = (turBottom + turTop) * 0.5f;
        const float baseHalf = 0.16f;     // half-thickness at the turret
        const float muzzleHalf = 0.09f;   // narrower at the tip
        float bz0 = turZ + turHd - 0.1f;  // starts at the turret front
        float bz1 = hl + 1.15f;           // pokes well past the nose
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
