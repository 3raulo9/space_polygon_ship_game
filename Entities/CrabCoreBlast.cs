using System.Numerics;
using VoidTanks.Core;

namespace VoidTanks.Entities;

/// <summary>
/// The detonation of a thrown CRAB CORE: a cinematic energy burst rather than a flat
/// floor sweep. A core of light hangs a little above where the bomb landed and throws
/// lances out in <em>every</em> direction at once — up, down, sideways, all the diagonals
/// — while the whole spray tumbles in a slow fluid motion and a bubble of energy pulses
/// around it. It runs for three seconds: the lances swell in, hold, then shrink back down
/// until they wink out of existence. Pure geometry + a clock; the world owns the damage
/// the field deals and the renderer reads the tumbling directions and envelope to draw it.
/// </summary>
public sealed class CrabCoreBlast
{
    public readonly Vector2 Position;   // where it landed, on the grid plane (X, Z)
    public float Age;

    /// <summary>The full cinematic runs this long.</summary>
    public const float Life = 3f;

    /// <summary>How many lances spray out, spread evenly over the whole sphere.</summary>
    public const int BeamCount = 32;

    /// <summary>The lance length at full swell — shorter than the boss's aimed shaft,
    /// since these go every direction and are meant to read as a burst, not a rake.</summary>
    public const float MaxBeamLength = 46f;

    /// <summary>The light core hangs this far above the grid, so the sphere of lances
    /// clears the floor and fires downward as well as up.</summary>
    public const float CoreHeight = 5f;

    /// <summary>Planar reach of the energy field that bites enemies, at full swell.</summary>
    public const float DamageRadius = 24f;

    // The base spray: a fixed set of directions spread evenly over the unit sphere
    // (a Fibonacci lattice), rolled once. The live directions are these tumbled by a
    // time-varying rotation so the whole burst churns rather than sitting rigid.
    private readonly Vector3[] _base;

    public CrabCoreBlast(Vector2 position)
    {
        Position = position;
        _base = new Vector3[BeamCount];

        // Fibonacci sphere: walk evenly down the y axis and spin by the golden angle,
        // which scatters the points over the sphere with no clustering at the poles.
        float golden = MathF.PI * (3f - MathF.Sqrt(5f));
        for (int i = 0; i < BeamCount; i++)
        {
            float y = 1f - (i + 0.5f) / BeamCount * 2f;   // 1 .. -1
            float r = MathF.Sqrt(MathF.Max(0f, 1f - y * y));
            float theta = golden * i;
            _base[i] = new Vector3(MathF.Cos(theta) * r, y, MathF.Sin(theta) * r);
        }
    }

    public bool Active => Age < Life;

    public void Update(float dt) => Age += dt;

    /// <summary>Where every lance leaves — the floating light core.</summary>
    public Vector3 Center => new(Position.X, CoreHeight, Position.Y);

    /// <summary>0..1 across the whole cinematic.</summary>
    public float Progress => Math.Clamp(Age / Life, 0f, 1f);

    /// <summary>
    /// The swell envelope, 0..1: lances shoot out fast (a ~0.18s attack), hold near full,
    /// then ease back down to nothing by the end — the "slowly become smaller until they
    /// stop existing" of the brief. Everything visible scales off this.
    /// </summary>
    public float Envelope
    {
        get
        {
            float attack = Math.Clamp(Age / 0.18f, 0f, 1f);
            // A curved decay so it lingers open, then falls away over the back half.
            float p = Progress;
            float decay = 1f - p * p;
            return attack * decay;
        }
    }

    /// <summary>Current lance length — full at the swell, shrinking to zero as it dies.</summary>
    public float BeamLength => MaxBeamLength * Envelope;

    /// <summary>The energy bubble's radius: it breathes as the burst churns, riding the
    /// same envelope so it collapses with the lances.</summary>
    public float BubbleRadius
    {
        get
        {
            float breathe = 0.75f + 0.25f * MathF.Sin(Age * 9f);
            return MaxBeamLength * 0.32f * Envelope * breathe;
        }
    }

    /// <summary>The live (tumbling) direction of lance <paramref name="i"/>. The base
    /// spray is rotated by a slow yaw/pitch/roll driven off the clock, so the whole burst
    /// churns fluidly in every axis instead of firing along fixed lines.</summary>
    public Vector3 Direction(int i)
    {
        Matrix4x4 spin = Matrix4x4.CreateFromYawPitchRoll(Age * 1.3f, Age * 0.9f, Age * 0.6f);
        return Vector3.Normalize(Vector3.Transform(_base[i], spin));
    }

    /// <summary>The damage field's current reach — shrinks with the swell so a dying blast
    /// stops biting as its lances retract.</summary>
    public float CurrentDamageRadius => DamageRadius * Math.Clamp(Envelope * 1.4f, 0f, 1f);
}
