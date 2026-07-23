using System.Numerics;
using Raylib_cs;

namespace VoidTanks.Entities;

/// <summary>
/// One flying piece of a destroyed enemy: either a chunky broken-off shard in the
/// enemy's own colour, or a small hot spark. Struct-of-arrays would be faster but
/// there are only ever a couple hundred, so a plain pooled struct reads clearer.
/// </summary>
public struct Shard
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Angle;   // current spin about Y
    public float Spin;    // spin rate, rad/s
    public float Life;    // seconds left
    public float MaxLife;
    public float Size;
    public Color Color;
    public bool IsSpark;

    public readonly bool Active => Life > 0f;
    /// <summary>1 at birth, 0 at death — drives the fade and shrink.</summary>
    public readonly float LifeFrac => MaxLife > 0f ? Life / MaxLife : 0f;
}

/// <summary>
/// The burst of debris an enemy throws off when it's destroyed: chunks of hull
/// arcing out under gravity and a spray of brighter sparks. A fixed pool, stepped
/// by the sim and drawn in the 3D pass. Purely cosmetic — it never touches damage
/// or collision, so the headless self-test can run it or ignore it freely.
/// </summary>
public sealed class DebrisSystem
{
    private const int Max = 320;
    private const float Gravity = 22f;   // world units/s^2 — chunks fall back down

    // Hot spark colour: a bright ember, warmer than anything in the cold palette,
    // so the spray reads as a flash against the void.
    private static readonly Color SparkColor = new(255, 208, 120, 255);

    // Kicked-up floor dust: a dim, cold grey so it reads as grime off the grid
    // rather than fire — for the boss's feet slamming down.
    private static readonly Color DustColor = new(120, 132, 138, 255);

    private readonly Shard[] _shards = new Shard[Max];

    /// <summary>The pool for the renderer to walk; skip entries where !Active.</summary>
    public Shard[] Shards => _shards;

    /// <summary>
    /// Blows an enemy apart at <paramref name="origin"/>: a handful of hull chunks
    /// in <paramref name="bodyColor"/> plus a spray of sparks. Elites throw a
    /// bigger, faster burst so the worse kill reads as worse.
    /// </summary>
    public void Burst(Vector3 origin, Color bodyColor, bool elite)
    {
        int chunks = elite ? 16 : 11;
        int sparks = elite ? 22 : 15;

        for (int i = 0; i < chunks; i++)
            Spawn(origin, bodyColor, isSpark: false, elite);
        for (int i = 0; i < sparks; i++)
            Spawn(origin, SparkColor, isSpark: true, elite);
    }

    /// <summary>
    /// A low puff of grid dust where a boss foot has just struck the floor — a few
    /// grey chunks kicked out sideways with little upward throw, gone in a moment.
    /// Purely cosmetic, like <see cref="Burst"/>.
    /// </summary>
    public void FootPuff(Vector3 origin)
    {
        var r = Random.Shared;
        int puffs = 3 + r.Next(2);
        for (int i = 0; i < puffs; i++)
        {
            int slot = -1;
            for (int j = 0; j < _shards.Length; j++)
                if (!_shards[j].Active) { slot = j; break; }
            if (slot < 0) return;

            // Fan out low across the ground, only a small upward kick.
            float az = r.NextSingle() * MathF.Tau;
            float el = 0.05f + r.NextSingle() * 0.3f;
            float cosEl = MathF.Cos(el);
            var dir = new Vector3(MathF.Cos(az) * cosEl, MathF.Sin(el), MathF.Sin(az) * cosEl);
            float speed = 4f + r.NextSingle() * 4f;

            _shards[slot] = new Shard
            {
                Position = origin + new Vector3(0f, 0.1f, 0f),
                Velocity = dir * speed,
                Angle = r.NextSingle() * MathF.Tau,
                Spin = (r.NextSingle() - 0.5f) * 10f,
                MaxLife = 0.3f + r.NextSingle() * 0.35f,
                Size = 0.7f + r.NextSingle() * 0.9f,
                Color = DustColor,
                IsSpark = false,
            };
            _shards[slot].Life = _shards[slot].MaxLife;
        }
    }

    /// <summary>
    /// A single piece thrown along a chosen line, rather than a burst fanning out in
    /// every direction: the SOLDIER's brass ejecting past the camera, and anything else
    /// that wants one flying object it has picked the trajectory of.
    ///
    /// Short-lived and small on purpose — a shell case tumbling past the eye at six
    /// hundred rounds a minute is a flicker in the corner of the frame, and anything
    /// with more presence than that turns the view into a snowstorm.
    /// </summary>
    public void Fleck(Vector3 origin, Vector3 velocity, Color color, float life = 0.5f)
    {
        int slot = -1;
        for (int i = 0; i < _shards.Length; i++)
            if (!_shards[i].Active) { slot = i; break; }
        if (slot < 0) return;

        var r = Random.Shared;
        _shards[slot] = new Shard
        {
            Position = origin,
            Velocity = velocity,
            Angle = r.NextSingle() * MathF.Tau,
            Spin = (r.NextSingle() - 0.5f) * 26f,   // tumbling hard: it is a small thing
            MaxLife = life,
            Size = 0.28f + r.NextSingle() * 0.14f,
            Color = color,
            IsSpark = true,
        };
        _shards[slot].Life = life;
    }

    public void Update(float dt)
    {
        for (int i = 0; i < _shards.Length; i++)
        {
            ref Shard s = ref _shards[i];
            if (!s.Active) continue;

            s.Life -= dt;
            s.Velocity.Y -= Gravity * dt;
            s.Position += s.Velocity * dt;
            s.Angle += s.Spin * dt;
        }
    }

    /// <summary>Clears every live piece — used when leaving a run back to the menu.</summary>
    public void Clear()
    {
        for (int i = 0; i < _shards.Length; i++)
            _shards[i].Life = 0f;
    }

    private void Spawn(Vector3 origin, Color color, bool isSpark, bool elite)
    {
        int slot = -1;
        for (int i = 0; i < _shards.Length; i++)
        {
            if (!_shards[i].Active) { slot = i; break; }
        }
        if (slot < 0) return; // pool full — drop the piece rather than allocate

        var r = Random.Shared;

        // Direction: outward on the ground plane with an upward bias, so the burst
        // fountains up and out before gravity drags the chunks back down.
        float az = r.NextSingle() * MathF.Tau;
        float el = 0.15f + r.NextSingle() * 0.85f;          // elevation, radians
        float cosEl = MathF.Cos(el);
        var dir = new Vector3(MathF.Cos(az) * cosEl, MathF.Sin(el), MathF.Sin(az) * cosEl);

        float baseSpeed = isSpark ? 11f : 6.5f;
        float speed = baseSpeed * (0.6f + r.NextSingle() * 0.8f) * (elite ? 1.25f : 1f);

        _shards[slot] = new Shard
        {
            Position = origin + dir * 0.4f,   // start just off the centre
            Velocity = dir * speed,
            Angle = r.NextSingle() * MathF.Tau,
            Spin = (r.NextSingle() - 0.5f) * 18f,
            MaxLife = isSpark ? 0.35f + r.NextSingle() * 0.35f
                              : 0.7f + r.NextSingle() * 0.6f,
            Size = isSpark ? 0.5f + r.NextSingle() * 0.3f
                           : 0.6f + r.NextSingle() * 0.6f,
            Color = color,
            IsSpark = isSpark,
        };
        _shards[slot].Life = _shards[slot].MaxLife;
    }
}
