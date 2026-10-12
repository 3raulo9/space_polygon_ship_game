using System.Numerics;

namespace Unrendered.Entities;

/// <summary>
/// The shape of the Crab-Core's beam on one frame of its burn — worked out here, once, as
/// plain geometry, so the world's damage test and the renderer's light are reading the same
/// shaft and the same splinters rather than each guessing at them.
///
/// <para>A beam laid along the grid is a clean shaft for its whole length. One fired up into
/// the air has nothing to end on, and it comes apart: past a point the shaft widens, splits
/// into thinner lasers fanning off at angles, and sheds a cloud of sparks, each piece fading
/// to nothing as it runs out. The longer it burns, the closer to the crystal that break
/// creeps. Every beam, grounded or not, finishes by drawing itself back into the gem.</para>
///
/// <para>Everything is a pure function of the locked direction, how far through the burn it
/// is, and a per-shot seed, so a client given those three draws the same splinters the host
/// is hurting people with.</para>
/// </summary>
public static class CrabLance
{
    /// <summary>A straight piece of light: from a point along a direction, for a length,
    /// tapering between two radii and fading between two strengths (0..1).</summary>
    public readonly record struct Segment(Vector3 From, Vector3 Dir, float Length,
        float RadiusFrom, float RadiusTo, float AlphaFrom, float AlphaTo)
    {
        public Vector3 To => From + Dir * Length;

        /// <summary>How far a point sits outside this piece's surface; ≤0 is inside.</summary>
        public float Miss(Vector3 p, float slack)
        {
            float along = Math.Clamp(Vector3.Dot(p - From, Dir), 0f, Length);
            float t = Length > 1e-4f ? along / Length : 0f;
            float r = RadiusFrom + (RadiusTo - RadiusFrom) * t;
            return Vector3.Distance(p, From + Dir * along) - r - slack;
        }
    }

    public sealed class Shape
    {
        /// <summary>The intact shaft out of the crystal. Its far end flares as it starts to
        /// come apart. Zero length once it has drawn back in.</summary>
        public Segment Solid;
        /// <summary>The splinters: thinner lasers fanning off the break.</summary>
        public readonly List<Segment> Fragments = new();
        /// <summary>The spark cloud round the break, as a fat capsule along the beam. Hurts
        /// anything in it, less than the light does. Zero length when the beam is whole.</summary>
        public Segment Sparks;
        /// <summary>0 for a whole beam, rising toward 1 as it comes apart.</summary>
        public float Breakup;
        /// <summary>0 while burning, 0→1 across the closing draw-back into the crystal.</summary>
        public float Retract;
        /// <summary>The shaft's on/off envelope, so neither end lands as a hard pop.</summary>
        public float Envelope;
    }

    /// <summary>How far above level the beam has to point before it starts to break, and
    /// where it is fully committed to breaking — radians of elevation.</summary>
    public const float BreakFrom = 0.04f, BreakFull = 0.22f;

    /// <summary>Where in the burn it starts drawing back into the crystal.</summary>
    public const float RetractFrom = 0.8f;

    /// <summary>Most splinters a beam can shed.</summary>
    public const int MaxFragments = 9;

    /// <summary>
    /// The beam at <paramref name="progress"/> (0..1 through the burn). <paramref name="time"/>
    /// is seconds into the burn and only drives the splinters' writhing, so it never moves
    /// anything the damage relies on by more than a fraction of a radius.
    /// </summary>
    public static Shape Build(Vector3 origin, Vector3 dir, float progress, int seed,
        float length, float radius, Shape? into = null)
    {
        var s = into ?? new Shape();
        s.Fragments.Clear();
        float p = Math.Clamp(progress, 0f, 1f);
        float time = p * CrabCore.BeamTime;

        s.Envelope = Math.Clamp(MathF.Min(p / 0.06f, 1f), 0f, 1f);
        s.Retract = Smooth(RetractFrom, 1f, p);

        // How hard it breaks: by how far it is pointed into empty sky, and by how long it has
        // been burning — a fresh shot holds together a moment before it starts to go.
        float elevation = MathF.Asin(Math.Clamp(dir.Y, -1f, 1f));
        float aerial = Smooth(BreakFrom, BreakFull, elevation);
        float age = Smooth(0f, 0.75f, p);
        s.Breakup = aerial * (0.25f + 0.75f * age);

        // The intact part shortens as the break creeps toward the crystal, then the whole
        // thing — break and all — is drawn back into the gem across the close.
        float keep = 1f - 0.7f * s.Breakup;
        float solidLen = length * keep * (1f - s.Retract);
        // The splinters run on past the break, a longer reach the older the beam.
        float span = length * s.Breakup * (0.25f + 0.3f * age) * (1f - s.Retract);

        float flare = 1f + 1.6f * s.Breakup;            // the shaft swells where it tears
        float r = radius * s.Envelope;
        s.Solid = new Segment(origin, dir, solidLen, r, r * flare, 1f, 1f - 0.35f * s.Breakup);

        if (span < 0.5f || s.Breakup < 0.02f)
        {
            s.Sparks = new Segment(origin + dir * solidLen, dir, 0f, 0f, 0f, 0f, 0f);
            return s;
        }

        // A frame perpendicular to the beam, for fanning splinters round it.
        Vector3 side = Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Y) > 0.95f ? Vector3.UnitX : Vector3.UnitY));
        Vector3 up = Vector3.Cross(side, dir);

        Vector3 breakAt = origin + dir * solidLen;
        float maxSpread = 0.08f + 0.22f * s.Breakup;    // radians a splinter strays off line
        int n = Math.Min(MaxFragments, 3 + (int)MathF.Round(6f * s.Breakup));
        for (int i = 0; i < n; i++)
        {
            float a = Hash(seed, i, 0) * MathF.Tau + time * (0.6f + Hash(seed, i, 1));
            float stray = maxSpread * (0.35f + 0.65f * Hash(seed, i, 2))
                        * (1f + 0.15f * MathF.Sin(time * 7f + i * 1.7f));
            Vector3 radial = side * MathF.Cos(a) + up * MathF.Sin(a);
            Vector3 fd = Vector3.Normalize(dir + radial * MathF.Tan(stray));
            Vector3 from = breakAt + radial * r * flare * 0.6f * Hash(seed, i, 3);
            float len = span * (0.45f + 0.55f * Hash(seed, i, 4));
            float fr = r * (0.22f + 0.2f * Hash(seed, i, 5));
            s.Fragments.Add(new Segment(from, fd, len, fr, fr * 0.25f, 0.9f, 0f));
        }

        // The sparks hang round the break and the splinters' near half, out to the fan's
        // own width, thinning toward the far end.
        float cloud = r * flare + span * MathF.Tan(maxSpread) * 0.6f;
        s.Sparks = new Segment(breakAt, dir, span * 0.8f, cloud * 0.7f, cloud, 1f, 0f);
        return s;
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>A stable 0..1 number from a shot's seed and a couple of indices, so the
    /// same shot always splinters the same way on every machine.</summary>
    public static float Hash(int seed, int i, int k)
    {
        uint h = (uint)seed * 0x9E3779B1u ^ (uint)(i + 1) * 0x85EBCA77u ^ (uint)(k + 7) * 0xC2B2AE3Du;
        h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }
}
