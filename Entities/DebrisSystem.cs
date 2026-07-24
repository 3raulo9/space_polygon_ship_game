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

    /// <summary>How heavy this piece is, for the crush test. Zero on cosmetic sparks and
    /// hull shards — they never touch a character; a structural chunk carries real mass and
    /// the world bills it as a hit on whatever it lands on. Spent to zero once it has
    /// crushed something, so one section can't erase a whole crowd.</summary>
    public float Mass;

    public readonly bool Active => Life > 0f;
    /// <summary>1 at birth, 0 at death — drives the fade and shrink.</summary>
    public readonly float LifeFrac => MaxLife > 0f ? Life / MaxLife : 0f;
}

/// <summary>
/// The burst of debris an enemy throws off when it's destroyed: chunks of hull
/// arcing out under gravity and a spray of brighter sparks. A fixed pool, stepped
/// by the sim and drawn in the 3D pass.
///
/// Most of what it throws is cosmetic — sparks and hull shards that never touch anybody.
/// The exception is <em>structural</em> rubble (<see cref="Chips"/>, <see cref="Rubble"/>,
/// <see cref="Collapse"/>): those pieces carry <see cref="Shard.Mass"/>, which the world
/// reads back to crush whatever they land on. The system itself still owns no damage — it
/// only marks the pieces heavy; the billing lives in the world's crush pass.
/// </summary>
public sealed class DebrisSystem
{
    private const int Max = 320;
    private const float Gravity = 22f;   // world units/s^2 — chunks fall back down

    // The masses a falling structural chunk can carry, which the crush test turns into
    // hit points: a chip stings, a chunk staggers, a full section is lethal. Public so the
    // world's tuning and the spawns here read the same three tiers.
    public const float ChipMass = 0.35f;
    public const float ChunkMass = 1.1f;
    public const float SectionMass = 3.2f;

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

    /// <summary>
    /// A burst of rubble blown off a strike point — the chunks an arch throws when a beam
    /// takes its span down (a tower sheds its own cells instead, see <see cref="RubbleChunk"/>).
    /// Some stagger, some kill on contact, all of it crushes.
    /// </summary>
    public void Rubble(Vector3 origin, Color color, int chunks, float scale)
    {
        for (int i = 0; i < chunks; i++)
        {
            bool big = Random.Shared.NextSingle() < 0.3f;
            SpawnChunk(origin, color,
                (big ? ChunkMass : ChipMass) * scale,
                (big ? 1.2f : 0.7f) * scale, speed: 6f, up: 0.9f, life: 1.3f);
        }
    }

    /// <summary>
    /// The wave a collapsing structure throws along the ground it fell across: the mass
    /// hitting the grid, spread down the fall line (<paramref name="along"/> × up to
    /// <paramref name="reach"/>) so standing anywhere under the topple is dangerous, not
    /// just at the base. A third of it is full sections — lethal — the rest staggering chunks.
    /// </summary>
    public void Collapse(Vector3 baseOrigin, Vector2 along, float reach, Color color, float scale)
    {
        int n = 6 + (int)(reach * 0.12f);
        for (int i = 0; i < n; i++)
        {
            float t = Random.Shared.NextSingle();
            var at = new Vector3(
                baseOrigin.X + along.X * reach * t, 0.6f,
                baseOrigin.Z + along.Y * reach * t);
            bool section = Random.Shared.NextSingle() < 0.35f;
            SpawnChunk(at, color,
                (section ? SectionMass : ChunkMass) * scale,
                (section ? 1.9f : 1.1f) * scale, speed: 5f, up: 0.5f, life: 1.5f);
        }
    }

    /// <summary>
    /// One structural cell knocked loose from a fracturing tower, dropped as a crushing chunk.
    /// Its mass follows its size — a big base block is lethal, a small crown flake only
    /// staggers — and it is thrown with a downward bias, since it is a piece coming off a
    /// building onto whatever is beneath it, not a burst fountaining up.
    /// </summary>
    public void RubbleChunk(Vector3 origin, float size, Color color)
    {
        float mass = Math.Clamp(size * 0.9f, ChipMass, SectionMass);
        // Little upward throw and modest scatter: a block coming off a building drops, it
        // doesn't launch, so a collapse reads as mass falling rather than a burst.
        SpawnChunk(origin, color, mass, MathF.Max(0.4f, size * 0.8f), speed: 3.2f, up: 0.12f, life: 2.2f);
    }

    /// <summary>
    /// Spawns one structural chunk at <paramref name="origin"/>, thrown outward with an
    /// upward bias and carrying <paramref name="mass"/> so the crush pass can bill it. The
    /// shared spawn for all the structural throws above.
    /// </summary>
    private void SpawnChunk(Vector3 origin, Color color, float mass, float size,
        float speed, float up, float life)
    {
        int slot = -1;
        for (int i = 0; i < _shards.Length; i++)
            if (!_shards[i].Active) { slot = i; break; }
        if (slot < 0) return;   // pool full — drop the piece

        var r = Random.Shared;
        float az = r.NextSingle() * MathF.Tau;
        float el = up * (0.3f + r.NextSingle() * 0.7f);
        float cosEl = MathF.Cos(el);
        var dir = new Vector3(MathF.Cos(az) * cosEl, MathF.Sin(el), MathF.Sin(az) * cosEl);
        float sp = speed * (0.6f + r.NextSingle() * 0.8f);

        _shards[slot] = new Shard
        {
            Position = origin,
            Velocity = dir * sp + new Vector3(0f, up * 3f, 0f),
            Angle = r.NextSingle() * MathF.Tau,
            Spin = (r.NextSingle() - 0.5f) * 12f,
            MaxLife = life,
            Size = size,
            Color = color,
            IsSpark = false,
            Mass = mass,
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

            // Structural rubble (mass-bearing) settles on the grid rather than sinking through
            // it, so a collapse leaves a moment's heap of blocks at the base that then shrinks
            // away with the piece's life — a pile, not debris vanishing into the floor.
            // Cosmetic sparks and hull shards carry no mass and fall through as before.
            if (s.Mass > 0f && s.Position.Y < 0f)
            {
                s.Position.Y = 0f;
                s.Velocity = Vector3.Zero;
                s.Spin = 0f;
            }
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
