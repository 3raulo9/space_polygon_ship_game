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
    /// A low, wide combat wedge: a hull box with a sloped front glacis and a
    /// stubby raised barrel. The wedge nose points +Z. One colour, hard facets.
    /// </summary>
    public static PolyMesh Tank(Color fill)
    {
        const float hw = 1.1f;   // half width
        const float hl = 1.6f;   // half length
        const float bodyH = 0.9f;
        const float noseY = 0.35f; // front is lower than the back — the wedge

        var m = new PolyMesh();

        // Body corners. Back is taller (bodyH), front glacis slopes down to noseY.
        Vector3 blB = new(-hw, 0f, -hl);   // back-left bottom
        Vector3 brB = new(hw, 0f, -hl);    // back-right bottom
        Vector3 flB = new(-hw, 0f, hl);    // front-left bottom
        Vector3 frB = new(hw, 0f, hl);     // front-right bottom

        Vector3 blT = new(-hw, bodyH, -hl);
        Vector3 brT = new(hw, bodyH, -hl);
        Vector3 flT = new(-hw, noseY, hl); // front top is lower → sloped glacis
        Vector3 frT = new(hw, noseY, hl);

        // Top (sloping forward), back, front glacis, two sides, bottom.
        m.AddFace(fill, blT, brT, frT, flT);            // top / glacis
        m.AddFace(fill, brB, blB, blT, brT);            // back
        m.AddFace(fill, flB, frB, frT, flT);            // front (short face)
        m.AddFace(fill, blB, flB, flT, blT);            // left
        m.AddFace(fill, frB, brB, brT, frT);            // right
        m.AddFace(fill, blB, brB, frB, flB);            // bottom

        // Stubby barrel: a thin box jutting from the front, slightly raised.
        const float bw = 0.16f;
        float by = 0.55f;                 // barrel centre height
        float bz0 = hl - 0.2f;            // starts near the nose
        float bz1 = hl + 1.0f;            // pokes out front
        Vector3 g0 = new(-bw, by - bw, bz0);
        Vector3 g1 = new(bw, by - bw, bz0);
        Vector3 g2 = new(bw, by + bw, bz0);
        Vector3 g3 = new(-bw, by + bw, bz0);
        Vector3 h0 = new(-bw, by - bw, bz1);
        Vector3 h1 = new(bw, by - bw, bz1);
        Vector3 h2 = new(bw, by + bw, bz1);
        Vector3 h3 = new(-bw, by + bw, bz1);
        m.AddFace(fill, h0, h1, h2, h3);   // muzzle cap
        m.AddFace(fill, g3, g2, h2, h3);   // top
        m.AddFace(fill, g0, h0, h3, g3);   // left
        m.AddFace(fill, g1, g2, h2, h1);   // right (wound for outward normal)
        m.AddFace(fill, g0, g1, h1, h0);   // bottom

        return m;
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
    public static PolyMesh Bolt(Color fill)
    {
        const float s = 0.22f;
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
