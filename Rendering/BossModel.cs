using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Entities;

namespace Unrendered.Rendering;

/// <summary>
/// The parts one rolled boss is built out of, baked once from its <see cref="BossGenome"/>.
///
/// <para>Everything in this game is a stack of hard-facet primitives — that rule is older than
/// any of the monsters and it holds here (Doc 02, and the Crab-Core's own build). What is new
/// is that the stack is <em>assembled from a genome</em> rather than typed out: the body's
/// proportions, how many limbs it has and how long they are, whether it has spines, and which
/// four swatches it wears all come off the roll. Two bosses of the same lineage are the same
/// construction and never the same object.</para>
///
/// <para>Baked once and held, not rebuilt per frame. A boss is drawn thousands of times and
/// rolled once — the same bargain the player's paint job strikes in
/// <see cref="CrabRenderer"/>.</para>
/// </summary>
public sealed class BossModel
{
    public readonly BossGenome Gene;

    /// <summary>The central mass. What a lineage <em>is</em>, before any of the roll's jitter:
    /// a BULWARK's long low hull, a STALKER's carapace, a COLUMN's torso.</summary>
    public readonly PolyMesh Body;

    /// <summary>The living core, spun and tinted per frame. Every lineage has one, because the
    /// one bright wrong thing in the middle is this game's whole convention for something
    /// alive — and because a procedurally-built silhouette needs one fixed landmark on it or
    /// the eye has nothing to hold.</summary>
    public readonly PolyMesh Core;

    /// <summary>One limb, drawn once per <see cref="BossGenome.LimbCount"/>. Shape follows the
    /// carriage: a splayed spider limb, a straight strut, a trailing tendril.</summary>
    public readonly PolyMesh Limb;

    /// <summary>What it stands on — a tread plate, a hover skirt, a husk. Null for a body that
    /// meets the ground with nothing but its limbs.</summary>
    public readonly PolyMesh? Stand;

    /// <summary>
    /// One armour plate per layer, outermost first. These are the shed layers: as
    /// <see cref="ModularBoss.ShedLayers"/> rises the renderer simply stops drawing the front
    /// of this array, so a five-bar Colossus is visibly stripped down to its frame across the
    /// fight. Nothing about that is a HUD effect — the model genuinely changes five times.
    /// </summary>
    public readonly PolyMesh[] Plates;

    /// <summary>A spine, drawn <see cref="BossGenome.SpineCount"/> times around the back.
    /// Null on the roughly half of bosses that rolled none.</summary>
    public readonly PolyMesh? Spine;

    /// <summary>Where each limb bolts on, in the body's own frame, and which way it points.
    /// Solved once from the roll so the renderer and anything that wants a limb's tip agree.</summary>
    public readonly (Vector3 Mount, float Yaw, float Phase)[] Limbs;

    public BossModel(BossGenome gene)
    {
        Gene = gene;

        Color shell = gene.Shell;
        Color deep = gene.Deep;
        Color limbCol = gene.Limb;

        Body = BuildBody(gene, shell, deep);
        Core = Meshes.CrabCoreGem(Color.White);   // tinted per frame, like the crab's
        Limb = BuildLimb(gene, limbCol);
        Stand = BuildCarriage(gene, deep, limbCol);
        Spine = gene.SpineCount > 0 ? BuildSpine(deep) : null;

        Plates = new PolyMesh[gene.Layers];
        for (int i = 0; i < gene.Layers; i++)
        {
            // Outer plates are the biggest and the palest; the ones underneath are tighter and
            // darker, so stripping the thing reads as getting closer to something that was
            // never meant to be seen.
            float t = gene.Layers <= 1 ? 0f : i / (float)(gene.Layers - 1);
            Plates[i] = BuildPlate(gene, GridRenderer.LerpColor(shell, deep, t), 1f + 0.34f * (1f - t));
        }

        Limbs = BuildLimbMounts(gene);
    }

    // --- The body ------------------------------------------------------------------

    /// <summary>
    /// The central mass, per lineage. Each is a small stack of boxes rather than one, because a
    /// single box at any proportion reads as a crate: it is the step between two masses that
    /// makes a silhouette, which is the same lesson the tank learned when it stopped being one
    /// low wedge (see Doc 02 and the tank's rebuild).
    /// </summary>
    private static PolyMesh BuildBody(BossGenome g, Color shell, Color deep)
    {
        var m = new PolyMesh();
        float w = g.BodyWidth, d = g.BodyDepth, h = g.BodyRise;

        switch (g.Lineage)
        {
            case BossLineage.Bulwark:
                // A siege hull: a long low glacis under a squat casemate, and a blunt snout
                // that is plainly the end you do not want to be standing at.
                m.AddBox(deep, w, d, w * 0.92f, d * 0.94f, 0f, h * 0.55f);
                m.AddBox(shell, w * 0.86f, d * 0.72f, w * 0.6f, d * 0.5f, h * 0.55f, h * 1.5f);
                m.AddBoxSpan(deep, -w * 0.32f, w * 0.32f, d * 0.7f, d * 1.35f, h * 0.2f, h * 0.85f);
                break;

            case BossLineage.Stalker:
                // A carapace: a wide lower tier with a domed lid over it and a well cut in the
                // top for the core to stand in. The Crab-Core's own construction, rolled.
                m.AddBox(deep, w, d, w * 0.94f, d * 0.94f, 0f, h * 0.5f);
                m.AddBox(shell, w * 0.94f, d * 0.94f, w * 0.5f, d * 0.5f, h * 0.5f, h * 1.35f);
                m.AddBox(deep, w * 0.4f, d * 0.4f, w * 0.3f, d * 0.3f, h * 1.35f, h * 1.55f);
                break;

            case BossLineage.Wearer:
                // A stack of things it is wearing. Three tiers, each a little askew of the one
                // under it, so the whole reads as a pile that has been assembled rather than a
                // body that grew.
                m.AddBox(deep, w, d, w * 0.85f, d * 0.85f, 0f, h * 0.4f);
                m.AddBoxSpan(shell, -w * 0.9f, w * 0.7f, -d * 0.7f, d * 0.9f, h * 0.4f, h * 0.78f);
                m.AddBoxSpan(deep, -w * 0.6f, w * 0.8f, -d * 0.85f, d * 0.6f, h * 0.78f, h * 1.14f);
                m.AddBox(shell, w * 0.55f, d * 0.55f, w * 0.35f, d * 0.35f, h * 1.14f, h * 1.5f);
                break;

            case BossLineage.Drowner:
                // Long, tapered at both ends, deepest a third of the way back — a body built to
                // move through something, hung in air that is not what it was built for.
                m.AddBox(shell, w * 0.25f, d * 0.2f, w * 0.9f, d * 0.55f, 0f, h * 0.5f);
                m.AddBox(shell, w * 0.9f, d * 0.55f, w * 0.7f, d * 0.9f, h * 0.5f, h * 1.1f);
                m.AddBox(deep, w * 0.7f, d * 0.9f, w * 0.2f, d * 0.25f, h * 1.1f, h * 1.45f);
                break;

            default:
                // A person's proportions at four times a person: hips, a long trunk, shoulders
                // too wide, and a small head that is deliberately not where the core is.
                //
                // The shoulder span is held to about one and a quarter body widths and given
                // real depth. At the 1.6× and paper thinness it was first built with, a COLUMN
                // seen from the front was a plank — the widest thing on the model by a factor of
                // three, with no thickness to catch the light, so the whole figure read as a
                // billboard rather than as a body.
                m.AddBox(deep, w * 1.15f, d * 1.3f, w * 0.95f, d * 1.1f, 0f, h * 0.22f);
                m.AddBox(shell, w * 0.95f, d * 1.1f, w * 1.15f, d * 1.05f, h * 0.22f, h * 0.8f);
                m.AddBoxSpan(deep, -w * 1.25f, w * 1.25f, -d * 1.1f, d * 1.1f, h * 0.78f, h * 0.98f);
                m.AddBox(shell, w * 0.45f, d * 0.5f, w * 0.36f, d * 0.4f, h * 1.0f, h * 1.34f);
                break;
        }
        return m;
    }

    /// <summary>
    /// One armour plate ring, drawn around the body at <paramref name="spread"/>. Four slabs
    /// rather than a shell, so what comes off when a layer breaks is plainly <em>plating</em>
    /// and the frame underneath is plainly a different object.
    /// </summary>
    private static PolyMesh BuildPlate(BossGenome g, Color col, float spread)
    {
        var m = new PolyMesh();
        float w = g.BodyWidth * spread, d = g.BodyDepth * spread, h = g.BodyRise;
        float t = 0.16f * g.BodyWidth;   // how thick a slab is

        // Left and right flanks, and a shorter pair fore and aft — an armoured thing viewed
        // from any bearing has something in the way.
        m.AddBoxSpan(col, w, w + t, -d * 0.8f, d * 0.8f, h * 0.15f, h * 1.15f);
        m.AddBoxSpan(col, -w - t, -w, -d * 0.8f, d * 0.8f, h * 0.15f, h * 1.15f);
        m.AddBoxSpan(col, -w * 0.6f, w * 0.6f, d, d + t, h * 0.25f, h * 1.0f);
        m.AddBoxSpan(col, -w * 0.6f, w * 0.6f, -d - t, -d, h * 0.25f, h * 1.0f);
        return m;
    }

    /// <summary>
    /// One limb. A splayed two-segment leg for anything that walks, a straight strut for the
    /// things that do not, and a thin tapering tendril for a hovering body — three shapes,
    /// which is enough, because what varies between two bosses of the same carriage is how many
    /// there are and how long they run, not what one looks like.
    /// </summary>
    private static PolyMesh BuildLimb(BossGenome g, Color col)
    {
        var m = new PolyMesh();
        float len = g.LimbLength;

        switch (g.Carriage)
        {
            case Carriage.Legs:
            case Carriage.Husks:
            {
                // Up and out to a high knee, then down past the body to a foot on the grid. The
                // raised-knee stance the Crab-Core established for everything that walks here.
                float kx = 2.2f * len, ky = 2.8f * len;
                float fx = 4.4f * len, fy = -3.4f * len;
                Segment(m, col, Vector3.Zero, new Vector3(kx, ky, 0f), 0.34f);
                Segment(m, col, new Vector3(kx, ky, 0f), new Vector3(fx, fy, 0f), 0.26f);
                m.AddBox(GridRenderer.LerpColor(col, Color.Black, 0.3f), 0.42f, 0.42f, 0.3f, 0.3f,
                    fy - 0.36f, fy);
                break;
            }

            case Carriage.Biped:
            {
                // An arm: shoulder out, elbow down, and a heavy fist. Longer than a person's
                // ought to be, which is most of what makes the COLUMN read wrong.
                float ex = 1.5f * len, ey = -2.4f * len;
                float hx = 2.1f * len, hy = -5.2f * len;
                Segment(m, col, Vector3.Zero, new Vector3(ex, ey, 0f), 0.42f);
                Segment(m, col, new Vector3(ex, ey, 0f), new Vector3(hx, hy, 0f), 0.34f);
                m.AddBox(col, 0.62f, 0.62f, 0.45f, 0.45f, hy - 0.7f, hy);
                break;
            }

            case Carriage.Hover:
            {
                // A tendril hanging under the body, tapering to nothing. Drawn in three falling
                // sections so it can be swayed per-segment rather than swung as one rod.
                float step = -1.6f * len;
                float r = 0.38f;
                for (int i = 0; i < 3; i++)
                {
                    m.AddBox(col, r, r, r * 0.7f, r * 0.7f, step * (i + 1), step * i);
                    r *= 0.7f;
                }
                break;
            }

            default:
            {
                // A gun mount on a tread hull: a stub barrel on a small collar. Points forward
                // rather than down, because it is not holding anything up.
                m.AddBox(col, 0.6f, 0.6f, 0.5f, 0.5f, -0.4f, 0.4f);
                m.AddBox(GridRenderer.LerpColor(col, Color.White, 0.15f),
                    0.34f, 0.34f, 0.22f, 0.22f, 0.4f, 0.4f + 3.2f * len);
                break;
            }
        }
        return m;
    }

    /// <summary>A tapered box laid between two points in the limb's own X/Y plane. What both
    /// limb segments are built out of, so a knee is a genuine crease rather than two boxes that
    /// happen to overlap.</summary>
    private static void Segment(PolyMesh m, Color col, Vector3 a, Vector3 b, float r)
    {
        Vector3 mid = (a + b) * 0.5f;
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy) * 0.5f;
        // Built along its own length and then folded into place by hand: the mesh has no
        // rotation of its own, so the segment is drawn as an axis-aligned box between the two
        // extremes and given its taper across the long axis.
        float ang = MathF.Atan2(dy, dx);
        float c = MathF.Cos(ang), s = MathF.Sin(ang);
        Vector3 P(float along, float across, float z) => new(
            mid.X + along * c - across * s,
            mid.Y + along * s + across * c,
            z);

        // Four long faces plus two caps, wound outward.
        for (int face = 0; face < 4; face++)
        {
            float a0 = face * MathF.PI * 0.5f, a1 = a0 + MathF.PI * 0.5f;
            m.AddFace(col,
                P(-len, r * MathF.Cos(a0), r * MathF.Sin(a0)),
                P(len, r * 0.7f * MathF.Cos(a0), r * 0.7f * MathF.Sin(a0)),
                P(len, r * 0.7f * MathF.Cos(a1), r * 0.7f * MathF.Sin(a1)),
                P(-len, r * MathF.Cos(a1), r * MathF.Sin(a1)));
        }
    }

    /// <summary>
    /// What the body meets the ground with. Built to exactly <see cref="BossGenome.CarriageLift"/>
    /// tall wherever it holds the body up, because that is the gap the entity has already put
    /// between the grid and the body's own origin — a carriage shorter than the lift leaves the
    /// thing hovering, and a taller one drives it into the floor.
    /// </summary>
    private static PolyMesh? BuildCarriage(BossGenome g, Color deep, Color limb)
    {
        var m = new PolyMesh();
        float w = g.BodyWidth, d = g.BodyDepth;
        float lift = g.CarriageLift;

        switch (g.Carriage)
        {
            case Carriage.Biped:
            {
                // Two legs, thigh over shin over foot, filling the whole lift. These were
                // missing outright in the first build: a biped's LIMBS are its arms, so nothing
                // was ever built for the pair it stands on and a COLUMN was a torso hanging in
                // mid-air over two disconnected fists.
                float stance = w * 0.55f;      // how far apart the feet are
                float thigh = lift * 0.52f;
                for (int side = 0; side < 2; side++)
                {
                    float x = side == 0 ? stance : -stance;
                    float hw = w * 0.34f;
                    // Shin: narrow, from the foot to the knee.
                    m.AddBoxSpan(limb, x - hw * 0.7f, x + hw * 0.7f, -d * 0.5f, d * 0.5f,
                        lift * 0.16f, thigh);
                    // Thigh: heavier, from the knee up into the hips.
                    m.AddBoxSpan(deep, x - hw, x + hw, -d * 0.6f, d * 0.6f, thigh, lift);
                    // Foot: forward of the ankle, so the figure reads as standing rather than
                    // as balanced on two poles.
                    m.AddBoxSpan(deep, x - hw, x + hw, -d * 0.4f, d * 1.15f, 0f, lift * 0.16f);
                }
                return m;
            }
            case Carriage.Treads:
                // Two long plates either side, sitting in the dirt. The one carriage that says
                // "machine" before anything else about the body does.
                m.AddBoxSpan(deep, -w - 0.6f, -w * 0.55f, -d * 1.05f, d * 1.05f, 0f, 1.1f);
                m.AddBoxSpan(deep, w * 0.55f, w + 0.6f, -d * 1.05f, d * 1.05f, 0f, 1.1f);
                return m;

            case Carriage.Hover:
                // A skirt: a thin inverted plate under the body with nothing beneath it, so the
                // gap between it and the grid is the whole read.
                m.AddBox(deep, w * 0.9f, d * 0.9f, w * 1.05f, d * 1.05f, -0.7f, -0.1f);
                return m;

            case Carriage.Husks:
                // A heap of dead hunters, stacked and fused. Deliberately the hunters' own
                // dried-blood red rather than anything the genome rolled — these are not part of
                // it, they are what it is standing on, and the palette has to say so.
                for (int i = 0; i < 3; i++)
                {
                    // Sized off the lift so the heap actually reaches the body it is holding up,
                    // whatever the roll made that lift.
                    float y0 = lift * (i / 3f);
                    float off = (i % 2 == 0 ? 1f : -1f) * w * 0.35f;
                    m.AddBoxSpan(i % 2 == 0 ? Palette.EnemyFill : Palette.EliteFill,
                        off - w * 0.7f, off + w * 0.7f, -d * 0.7f, d * 0.7f,
                        y0, y0 + lift * 0.46f);
                }
                return m;

            default:
                return null;   // legs and a biped meet the ground with limbs and nothing else
        }
    }

    private static PolyMesh BuildSpine(Color col)
    {
        var m = new PolyMesh();
        // A blade rather than a needle: at 320 pixels across, a spike thin enough to be a spike
        // simply disappears, and a flattened one still reads as one from every bearing.
        m.AddBox(col, 0.34f, 0.12f, 0.02f, 0.02f, 0f, 1.9f);
        return m;
    }

    /// <summary>
    /// Where the limbs bolt on. Legs splay around the body's flanks in mirrored pairs; a
    /// biped's two arms hang off the shoulder line; tendrils hang under; gun mounts sit on top.
    /// Each carries a gait phase so a walk is a scuttle rather than a march — alternating pairs,
    /// exactly as the Crab-Core's six do.
    /// </summary>
    private static (Vector3, float, float)[] BuildLimbMounts(BossGenome g)
    {
        int n = Math.Max(0, g.LimbCount);
        var mounts = new (Vector3, float, float)[n];
        float w = g.BodyWidth, d = g.BodyDepth, h = g.BodyRise;

        for (int i = 0; i < n; i++)
        {
            bool right = i % 2 == 0;
            int pair = i / 2;
            int pairs = Math.Max(1, (n + 1) / 2);
            // Spread the pairs fore and aft along the body rather than bunching them at the
            // middle, so an eight-legged boss is long-bodied rather than a spider on a plate.
            float along = pairs == 1 ? 0f : (pair / (pairs - 1f) - 0.5f) * 1.7f * d;

            switch (g.Carriage)
            {
                case Carriage.Hover:
                    mounts[i] = (new Vector3(right ? w * 0.55f : -w * 0.55f, -0.2f, along),
                        right ? 0f : MathF.PI, i * 0.7f);
                    break;
                case Carriage.Treads:
                    mounts[i] = (new Vector3(right ? w * 0.5f : -w * 0.5f, h * 1.2f, d * 0.4f),
                        0f, 0f);
                    break;
                case Carriage.Biped:
                    mounts[i] = (new Vector3(right ? w * 1.45f : -w * 1.45f, h * 0.88f, 0f),
                        right ? 0f : MathF.PI, right ? 0f : MathF.PI);
                    break;
                default:
                    // The splay: front limbs angled forward, back limbs angled back, so the
                    // footprint is a spread rather than a row.
                    float splay = pairs == 1 ? 0f : (0.5f - pair / (pairs - 1f)) * 0.9f;
                    mounts[i] = (new Vector3(right ? w * 0.85f : -w * 0.85f, h * 0.6f, along),
                        right ? -splay : MathF.PI + splay,
                        (pair + (right ? 0f : 1f)) * MathF.PI);
                    break;
            }
        }
        return mounts;
    }
}
