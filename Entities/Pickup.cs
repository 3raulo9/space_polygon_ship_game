using System.Numerics;

namespace VoidTanks.Entities;

/// <summary>What a floating pickup is when the craft drives over it — now stowed into
/// the inventory rather than applied on the spot.</summary>
public enum PickupKind
{
    /// <summary>A polygon battery cell — one battery item for the pack.</summary>
    Battery,
    /// <summary>A stray round — a random handful of bullets for the pack.</summary>
    Ammo,
    /// <summary>A shard of a slain Crab-Core — three craft a CRAB CORE.</summary>
    CrabFragment,
}

/// <summary>
/// A piece of salvage drifting on the grid: it never fights, never blocks — it
/// just hangs a little above the floor, bobbing and slowly turning, until the
/// craft touches it and takes the charge. Purely a position + a kind + a clock
/// for the idle animation; the world owns spawning, collision and the effect, and
/// the renderer reads <see cref="BobHeight"/> / <see cref="Spin"/> for the drift.
/// </summary>
public sealed class Pickup
{
    public Vector2 Position;
    public readonly PickupKind Kind;

    // Collision radius on the plane; paired with the player's radius for the
    // "drive over it" pickup check. A touch generous so grazing it counts.
    public const float Radius = 1.2f;

    // Seconds alive, advanced by the sim — drives the bob and spin. Seeded with a
    // random phase so a field of pickups never pulses in lockstep.
    public float Age;
    private readonly float _phase;

    /// <summary>How many items this pickup yields when collected. Batteries and
    /// fragments carry one; a stray round carries a random 5–20 bullets, rolled once
    /// at spawn so the reward is fixed for the life of that particular pickup.</summary>
    public readonly int Amount;

    public Pickup(Vector2 position, PickupKind kind)
    {
        Position = position;
        Kind = kind;
        _phase = Random.Shared.NextSingle() * MathF.Tau;
        Amount = kind == PickupKind.Ammo ? Random.Shared.Next(5, 21) : 1;
    }

    public void Update(float dt) => Age += dt;

    /// <summary>Slow idle spin about Y (radians) so the facets catch the light.</summary>
    public float Spin => Age * 1.4f + _phase;

    /// <summary>Height above the grid — floats around waist height with a gentle bob.</summary>
    public float BobHeight => 1.4f + MathF.Sin(Age * 2f + _phase) * 0.35f;
}
