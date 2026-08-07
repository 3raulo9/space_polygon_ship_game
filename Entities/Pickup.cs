using System.Numerics;

namespace Unrendered.Entities;

/// <summary>What a floating pickup is when the craft drives over it — now stowed into
/// the inventory rather than applied on the spot.
///
/// <para><b>Append only.</b> The value is the byte a field packet carries, so reordering
/// these turns every piece of salvage on a client's grid into something else.</para>
/// </summary>
public enum PickupKind
{
    /// <summary>A polygon battery cell — one battery item for the pack.</summary>
    Battery,
    /// <summary>A stray round — a random handful of bullets for the pack.</summary>
    Ammo,
    /// <summary>A shard of a slain Crab-Core — three craft a CRAB CORE.</summary>
    CrabFragment,

    // The materials a kill scatters. Not part of the ambient drip that keeps the field
    // stocked — these exist only where something died, and are gone once picked up.

    /// <summary>Torn plate. Nearly every kill leaves some.</summary>
    ScrapMetal,
    /// <summary>Powder off a spent weapon. Rarer.</summary>
    SpaceGunpowder,
    /// <summary>A coil of wire, out of whatever was driving the thing. Rarest.</summary>
    CopperWire,

    // The rest of what a pack can hold. Nothing on the field ever spawns as one of these —
    // they exist because a player can now throw any item back out of their inventory, and
    // whatever comes out has to be able to lie on the grid like everything else.

    /// <summary>Thrown out of a pack: the mass in front of a round.</summary>
    DenseAlloy,
    /// <summary>Thrown out of a pack: a cell's anode metal.</summary>
    Lead,
    /// <summary>Thrown out of a pack: a cell's anode metal.</summary>
    Zinc,
    /// <summary>Thrown out of a pack: a cell's anode metal.</summary>
    Lithium,
    /// <summary>Thrown out of a pack: a finished CRAB CORE, lying where it was tossed.</summary>
    CrabCore,

    /// <summary>A repair kit — the only thing on the grid that mends hull. Scarcer than a
    /// cell, and worth crossing a street for.</summary>
    RepairKit,

    /// <summary>A piece of the moon, lying where it came down. The rarest thing on any grid,
    /// and the only salvage in the game that arrives from <em>above</em> rather than being left
    /// by something that died.</summary>
    Moonstone,

    // --- The arch's keys ----------------------------------------------------------------
    // The only salvage on the grid that is worth nothing at all to the craft carrying it. It
    // mends no layer, loads no gun and makes no part; it is worth exactly one fifth of the way
    // off this planet, and only once it is standing in front of an arch.

    /// <summary>Half of what a boss leaves. Lies where the body fell until somebody takes it,
    /// and unlike every other piece of salvage on the grid it is never swept, never evicted and
    /// never times out — see <see cref="Unrendered.World.World.IsFragment"/>.</summary>
    SunFragment,

    /// <summary>The other half.</summary>
    MoonFragment,
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

    /// <summary>What it is. Settable only through <see cref="NetSet"/>: on the host this
    /// never changes for the life of a pickup, but a client's list is the host's nearest
    /// salvage re-sorted every snapshot, so slot 3 is routinely a different piece of salvage
    /// from one packet to the next and the kind has to travel with the position.</summary>
    public PickupKind Kind { get; private set; }

    /// <summary>Host-side: set once this has been handed to somebody, so the sweep at the end
    /// of the tick can take it off the field. A flag rather than an immediate removal because
    /// the collection walk is iterating the list it would be mutating — and rather than a
    /// single "the one to remove" field, because two players can clear two pieces of salvage
    /// on the same tick and only one of them used to actually go.</summary>
    public bool Consumed;

    // Collision radius on the plane; paired with the player's radius for the
    // "drive over it" pickup check. A touch generous so grazing it counts.
    public const float Radius = 1.2f;

    // Seconds alive, advanced by the sim — drives the bob and spin. Seeded with a
    // random phase so a field of pickups never pulses in lockstep.
    public float Age;
    private readonly float _phase;

    /// <summary>The <see cref="Age"/> at which this last told somebody their pack was too full
    /// to take it. Host-side only, and only ever set on a fragment — the one kind of salvage
    /// that refuses rather than overflowing.
    ///
    /// <para>It lives on the pickup rather than on the player because the thing being
    /// rate-limited is <em>this rock saying no</em>, not the craft hearing it: a player who
    /// drives across three fragments they cannot lift should be told three times. Seeded
    /// negative so the very first refusal is never swallowed by the gap.</para></summary>
    public float LastRefusal = -999f;

    /// <summary>How many items this pickup yields when collected. Batteries, fragments and
    /// raw materials carry one; a stray round carries a random 5–20 bullets, rolled once at
    /// spawn so the reward is fixed for the life of that particular pickup. Host-side truth:
    /// a client's copy has its own roll, and never gets to act on it.</summary>
    public readonly int Amount;

    /// <summary>
    /// True when a player threw this out of their pack rather than the field putting it there.
    /// Host-side only, like <see cref="Amount"/> — it never travels, because it changes nothing
    /// a client draws.
    ///
    /// <para>It changes two things the host does. A thrown cell is not part of the ambient
    /// drift, so it neither counts against that budget nor teleports back out into the fog
    /// when somebody scoops it up — which would quietly mint a battery every time one was
    /// thrown away. And it is spent when taken, exactly like a part off a body.</para>
    /// </summary>
    public readonly bool Thrown;

    /// <param name="amount">How many items it is worth, or 0 to let the kind decide — which
    /// is what the field's own salvage does. A thrown stack names its own count, so throwing
    /// one round out gives back one round rather than a fresh handful.</param>
    public Pickup(Vector2 position, PickupKind kind, int amount = 0, bool thrown = false)
    {
        Position = position;
        Kind = kind;
        _phase = Random.Shared.NextSingle() * MathF.Tau;
        Amount = amount > 0 ? amount
               : kind == PickupKind.Ammo ? Random.Shared.Next(5, 21) : 1;
        Thrown = thrown;
    }

    public void Update(float dt) => Age += dt;

    /// <summary>Client-side: lays a host-described piece of salvage over this cell. The bob
    /// and spin are deliberately left alone — the phase is this machine's business, and
    /// resetting it twenty times a second is what made a field of salvage strobe.</summary>
    public void NetSet(Vector2 position, PickupKind kind)
    {
        Position = position;
        Kind = kind;
    }

    /// <summary>Slow idle spin about Y (radians) so the facets catch the light.</summary>
    public float Spin => Age * 1.4f + _phase;

    /// <summary>Height above the grid — floats around waist height with a gentle bob.</summary>
    public float BobHeight => 1.4f + MathF.Sin(Age * 2f + _phase) * 0.35f;
}
