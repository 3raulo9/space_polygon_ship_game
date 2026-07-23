using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.World;

namespace VoidTanks.Rendering;

/// <summary>
/// The skyline's geometry: dead alien towers and the arcs thrown between them.
///
/// Everything here obeys the same rules as the rest of the game's solids (Doc 02) —
/// flat-shaded convex faces, hard creases, no curves. An arc looks curved only because
/// it is ten straight beams pretending; at 320×240 that is more than enough, and it
/// keeps a whole city inside the same handful of triangles-per-shape budget a tank has.
///
/// Shapes are built at a nominal scale — a tower is roughly 45 units tall on a 3.5-unit
/// footprint, an arc spans <see cref="Structure.ArchHalfSpan"/> either side of its
/// centre — and the field scales each instance uniformly from there. Local convention
/// matches <see cref="Meshes"/>: +Z forward, +Y up, and for an arc the span runs along
/// local X, which is what puts its legs where <see cref="Structure.Blockers"/> says
/// they are.
/// </summary>
public static class StructureMeshes
{
    /// <summary>How many distinct towers the renderer prebuilds. Four families, each
    /// jittered three ways, which is enough that a skyline stops reading as a repeated
    /// prop without any of them needing to be interesting on its own.</summary>
    public const int TowerVariants = 12;

    /// <summary>How many distinct arcs. Fewer, because they are far bigger and far
    /// rarer — you rarely have two in view at once to compare.</summary>
    public const int ArchVariants = 3;

    /// <summary>
    /// One tower. <paramref name="variant"/> picks the family (needle, slab, bundle,
    /// lean) and seeds the jitter inside it, so the same number always builds the same
    /// building — which is what lets the field store a plain int and get its shape back.
    /// </summary>
    public static PolyMesh Tower(int variant)
    {
        var rng = new Random(variant * 7919 + 13);
        var m = new PolyMesh();

        switch (variant & 3)
        {
            case 0: BuildNeedle(m, rng); break;
            case 1: BuildSlab(m, rng); break;
            case 2: BuildBundle(m, rng); break;
            default: BuildLean(m, rng); break;
        }

        return m;
    }

    /// <summary>
    /// A needle: tapering tiers stacked to a shaft so thin it nearly vanishes at the
    /// top. The one shape in the family that reads at any distance, because a point
    /// against the sky survives being three pixels wide.
    /// </summary>
    private static void BuildNeedle(PolyMesh m, Random rng)
    {
        float hw = 3.4f + rng.NextSingle() * 0.8f;
        float y = 0f;
        int tiers = 5 + rng.Next(2);

        for (int i = 0; i < tiers; i++)
        {
            float h = 6.5f + rng.NextSingle() * 4.5f;
            float top = hw * (0.62f + rng.NextSingle() * 0.12f);
            // Alternate tiers sit in the deep tone, which cuts the shaft into visible
            // storeys without another vertex of geometry.
            m.AddBox(i % 2 == 0 ? Palette.StructureShell : Palette.StructureDeep,
                hw, hw * 0.85f, top, top * 0.85f, y, y + h);

            // A window band on the seam: a hair wider than the shaft so it catches the
            // light as a lip rather than being buried inside the face it sits on.
            Band(m, top * 1.08f, top * 0.92f, y + h, 0.28f);

            y += h;
            hw = top;
        }

        // The spire itself, run out to a near-point.
        m.AddBox(Palette.StructureShell, hw, hw * 0.85f, 0.07f, 0.07f, y, y + 11f + rng.NextSingle() * 7f);
    }

    /// <summary>
    /// A slab: one flat monolith, far wider than it is deep, crowned with two prongs.
    /// Turned side-on it is a wall; turned face-on it is a blade — the same building
    /// gives two silhouettes depending on where the player drives, which is most of
    /// why the field bothers to store a heading.
    /// </summary>
    private static void BuildSlab(PolyMesh m, Random rng)
    {
        float hw = 3.6f + rng.NextSingle() * 1.0f;
        float hd = 1.3f + rng.NextSingle() * 0.6f;
        float h = 32f + rng.NextSingle() * 14f;

        m.AddBox(Palette.StructureShell, hw, hd, hw * 0.9f, hd, 0f, h);

        // A recessed spine down the middle of the broad face, in the deep tone.
        m.AddBox(Palette.StructureDeep, hw * 0.35f, hd * 1.06f, 0f, h * 0.96f);

        // Window bands up the slab, thinning out toward the top the way lit floors do.
        for (float b = 4f; b < h - 3f; b += 5.5f)
            Band(m, hw * 1.04f, hd * 1.12f, b, 0.22f);

        // Two prongs off the crown — the tell that this was built by something with an
        // opinion, rather than being a box someone stood on its end.
        float prongTop = h + 7f + rng.NextSingle() * 5f;
        m.AddBoxSpan(Palette.StructureShell, -hw * 0.85f, -hw * 0.35f, -hd * 0.5f, hd * 0.5f, h, prongTop);
        m.AddBoxSpan(Palette.StructureShell, hw * 0.35f, hw * 0.85f, -hd * 0.5f, hd * 0.5f, h,
            prongTop - 3f - rng.NextSingle() * 5f);
    }

    /// <summary>
    /// A bundle: three shafts of different heights strapped together by two collars.
    /// The uneven tops are the point — from the fog it reads as one broken mass rather
    /// than as three thin towers that happen to be standing near each other.
    /// </summary>
    private static void BuildBundle(PolyMesh m, Random rng)
    {
        var shafts = new[]
        {
            new Vector3(-1.9f, 0f, -0.8f),
            new Vector3(1.7f, 0f, 1.1f),
            new Vector3(0.15f, 0f, -2.05f),
        };

        float tallest = 0f;
        foreach (var s in shafts)
        {
            float h = 22f + rng.NextSingle() * 20f;
            float hw = 1.3f + rng.NextSingle() * 0.5f;
            tallest = MathF.Max(tallest, h);
            m.AddBoxSpan(Palette.StructureShell,
                s.X - hw, s.X + hw, s.Z - hw, s.Z + hw, 0f, h);
            // Each shaft is capped by a short deep-toned head, so the three tops read
            // as three separate tops instead of one flat shear.
            m.AddBoxSpan(Palette.StructureDeep,
                s.X - hw * 0.7f, s.X + hw * 0.7f, s.Z - hw * 0.7f, s.Z + hw * 0.7f, h, h + 2.4f);
        }

        // The collars: flat plates through all three, which is what makes it a bundle.
        foreach (float y in new[] { tallest * 0.35f, tallest * 0.68f })
            m.AddBox(Palette.StructureGlow, 3.7f, 3.4f, 3.7f, 3.4f, y, y + 0.55f);
    }

    /// <summary>
    /// A lean: a stack that walks sideways as it climbs and finishes in an overhang
    /// hanging out over nothing. Nothing in this world holds a plumb line, and one
    /// building per family that is visibly falling over sells that better than fog does.
    /// </summary>
    private static void BuildLean(PolyMesh m, Random rng)
    {
        float drift = (0.55f + rng.NextSingle() * 0.4f) * (rng.Next(2) == 0 ? 1f : -1f);
        float hw = 3.1f + rng.NextSingle() * 0.7f;
        float x = 0f, y = 0f;
        int tiers = 5 + rng.Next(3);

        for (int i = 0; i < tiers; i++)
        {
            float h = 6f + rng.NextSingle() * 4f;
            m.AddBoxSpan(i % 2 == 0 ? Palette.StructureShell : Palette.StructureDeep,
                x - hw, x + hw, -hw * 0.8f, hw * 0.8f, y, y + h);
            x += drift;
            y += h;
            hw *= 0.88f;
        }

        // The cantilever: a long slab thrown out past the last tier, unsupported.
        float reach = 5f + rng.NextSingle() * 3.5f;
        float x0 = MathF.Min(x - hw, x + drift * reach);
        float x1 = MathF.Max(x + hw, x + drift * reach);
        m.AddBoxSpan(Palette.StructureShell, x0, x1, -hw * 0.7f, hw * 0.7f, y, y + 2.4f);
        m.AddBoxSpan(Palette.StructureGlow, x0, x1, -hw * 0.3f, hw * 0.3f, y + 2.4f, y + 2.75f);
    }

    /// <summary>A window band: a thin lipped plate ringing the shaft at a height.</summary>
    private static void Band(PolyMesh m, float hw, float hd, float y, float thickness)
        => m.AddBox(Palette.StructureGlow, hw, hd, hw, hd, y, y + thickness);

    // --- The arcs ---------------------------------------------------------------

    /// <summary>Height of the leg an arc springs from, at scale 1. Everything above
    /// this is curve; everything below is the pylon you can drive into.</summary>
    private const float SpringHeight = 7f;

    /// <summary>How far the apex rises above the springing point, at scale 1.</summary>
    private const float ArchRise = 15f;

    /// <summary>Straight beams in the curve. Ten is the number where the arc stops
    /// reading as a polygon and starts reading as a curve at this resolution — more is
    /// triangles nobody can see.</summary>
    private const int ArchSegments = 10;

    /// <summary>
    /// An arc: two pylons and a half-ellipse of straight beams sprung between them,
    /// spanning <see cref="Structure.ArchHalfSpan"/> either side of the origin along
    /// local X. Tall enough to drive under at every scale the field uses, so meeting one
    /// is a landmark rather than a wall.
    /// </summary>
    public static PolyMesh Arch(int variant)
    {
        var rng = new Random(variant * 5171 + 29);
        var m = new PolyMesh();

        float half = Structure.ArchHalfSpan;
        float rise = ArchRise * (0.85f + rng.NextSingle() * 0.45f);
        float thick = 0.7f + rng.NextSingle() * 0.35f;
        float depth = 1.1f + rng.NextSingle() * 0.4f;

        // The two pylons, tapering as they rise so the arc looks carried rather than
        // balanced. Each stands where Structure.Blockers puts its footprint.
        foreach (float side in new[] { -1f, 1f })
        {
            m.AddBoxSpan(Palette.StructureShell,
                side * half - 1.7f, side * half + 1.7f, -1.5f, 1.5f, 0f, SpringHeight * 0.55f);
            m.AddBoxSpan(Palette.StructureDeep,
                side * half - 1.25f, side * half + 1.25f, -1.15f, 1.15f,
                SpringHeight * 0.55f, SpringHeight);
        }

        // The curve, walked as a chain of beams between successive samples of a
        // half-ellipse springing from one pylon top to the other.
        Vector3 prev = ArchPoint(0, half, rise);
        for (int i = 1; i <= ArchSegments; i++)
        {
            Vector3 next = ArchPoint(i, half, rise);
            Beam(m, Palette.StructureShell, prev, next, thick, depth);

            // A lit node every third joint, which is what stops a long dark span from
            // disappearing entirely against the black above the horizon glow.
            if (i % 3 == 0)
                Beam(m, Palette.StructureGlow, prev, next, thick * 0.45f, depth * 1.15f);

            prev = next;
        }

        return m;
    }

    /// <summary>Sample <paramref name="i"/> of the springing curve, 0 at the -X pylon
    /// and <see cref="ArchSegments"/> at the +X one.</summary>
    private static Vector3 ArchPoint(int i, float half, float rise)
    {
        float t = MathF.PI * (1f - i / (float)ArchSegments);   // π → 0, i.e. -X → +X
        return new Vector3(half * MathF.Cos(t), SpringHeight + rise * MathF.Sin(t), 0f);
    }

    /// <summary>
    /// A square prism running between two points in the local XY plane — the primitive
    /// the arcs are made of, since <see cref="PolyMesh.AddBoxSpan"/> can only build
    /// axis-aligned masses and every beam in a curve is at its own angle. Thickness is
    /// measured perpendicular to the run within the plane; depth is along Z.
    /// </summary>
    private static void Beam(PolyMesh m, Color c, Vector3 p0, Vector3 p1, float halfW, float halfD)
    {
        Vector3 run = p1 - p0;
        float len = run.Length();
        if (len < 1e-4f) return;
        run /= len;

        // Perpendicular to the run, inside the XY plane the arc is drawn in.
        var perp = new Vector3(-run.Y, run.X, 0f) * halfW;
        var side = new Vector3(0f, 0f, halfD);

        Vector3 a0 = p0 - perp - side, a1 = p0 + perp - side;
        Vector3 a2 = p0 + perp + side, a3 = p0 - perp + side;
        Vector3 b0 = p1 - perp - side, b1 = p1 + perp - side;
        Vector3 b2 = p1 + perp + side, b3 = p1 - perp + side;

        m.AddFace(c, a0, a1, a2, a3);   // cap at p0
        m.AddFace(c, b3, b2, b1, b0);   // cap at p1
        m.AddFace(c, a0, b0, b1, a1);   // -Z flank
        m.AddFace(c, a2, b2, b3, a3);   // +Z flank
        m.AddFace(c, a1, b1, b2, a2);   // +perp flank
        m.AddFace(c, a3, b3, b0, a0);   // -perp flank
    }
}
