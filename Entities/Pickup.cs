using System.Numerics;

namespace VoidTanks.Entities;

/// <summary>What a floating pickup does when the craft drives over it.</summary>
public enum PickupKind
{
    /// <summary>A polygon battery cell — recharges shield and the Hyper reserve.</summary>
    Battery,
    /// <summary>A stray round — restocks ammo.</summary>
    Ammo,
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

    public Pickup(Vector2 position, PickupKind kind)
    {
        Position = position;
        Kind = kind;
        _phase = Random.Shared.NextSingle() * MathF.Tau;
    }

    public void Update(float dt) => Age += dt;

    /// <summary>Slow idle spin about Y (radians) so the facets catch the light.</summary>
    public float Spin => Age * 1.4f + _phase;

    /// <summary>Height above the grid — floats around waist height with a gentle bob.</summary>
    public float BobHeight => 1.4f + MathF.Sin(Age * 2f + _phase) * 0.35f;
}
