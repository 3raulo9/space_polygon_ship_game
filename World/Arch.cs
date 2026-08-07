using System.Numerics;
using Unrendered.Core;

namespace Unrendered.World;

/// <summary>
/// What one of the city's arcs is doing, once a DESCENT has decided it is a way off the planet.
///
/// <para><b>This is not a new building.</b> The city has always had ten arcs standing in it —
/// two pylons and a span, tall enough to drive under, scattered by the same seeded layout as
/// the towers (<see cref="StructureField"/>). They were scenery. A run picks a few of them at
/// the drop and makes them gates, and the whole of what this class holds is the difference
/// between an arc you drive under and an arc you leave through.</para>
///
/// <para>Which is why the foreshadowing is free, and why it is worth having: a player spends
/// five waves fighting under these things without knowing which of them matter. Nothing is
/// added to the skyline when the Colossus dies — something that was already there takes
/// power.</para>
///
/// <para>Pure state, like <see cref="Descent"/> itself: no world, no renderer, no audio. The
/// world drives it and reads the results; <c>Core/SelfTestArch.cs</c> drives a whole gate from
/// dark to open in milliseconds with none of those things present.</para>
/// </summary>
public sealed class Arch
{
    /// <summary>How many keys a span takes. Five, because a planet gives up five.</summary>
    public const int SocketCount = 5;

    /// <summary>
    /// Which socket each fragment goes into, in the order they are fed.
    ///
    /// <para>Sockets are numbered left to right along the span — 0 and 4 are the feet, 2 is the
    /// keystone. This is the order they <em>fill</em>: both feet, then both haunches, then the
    /// crown. So the arch closes from the outside in, the two halves stay balanced the whole
    /// way, and the fifth fragment is visibly the one that completes the span rather than the
    /// one that happened to be last.</para>
    ///
    /// <para>The alternative — filling left to right — was rejected for exactly that: it makes
    /// the fifth insert the least interesting of the five, and it makes a half-full arch look
    /// broken rather than half-built.</para>
    /// </summary>
    public static readonly int[] FillOrder = { 0, 4, 1, 3, 2 };

    /// <summary>Where along the span each socket sits, 0 at the left foot and 1 at the right.
    /// Read by the sim to fly a fragment to the right place and by the renderer to draw it
    /// there — one table, both sides, because a socket the renderer puts somewhere the sim
    /// does not is a fragment that arrives inside the stonework.</summary>
    public static readonly float[] SocketAlong = { 0.08f, 0.29f, 0.5f, 0.71f, 0.92f };

    /// <summary>Where a gate is in its life.</summary>
    public enum Phase : byte
    {
        /// <summary>Standing, unpowered, indistinguishable from the nine other arcs on the map
        /// except by the dead panel on its right-hand leg. Every gate is this for most of a run.</summary>
        Dark,

        /// <summary>The Colossus is down and this one has power in it. Throwing a light column
        /// and marked on every radar in the match — as are all the other gates, until one of
        /// them is claimed.</summary>
        Lit,

        /// <summary>Somebody opened this one's panel first. It is now the only gate on the
        /// planet; the others went dark the same instant and stay that way.</summary>
        Claimed,

        /// <summary>All five are seated and the span is winding itself up.</summary>
        Charging,

        /// <summary>Open. Walk into it.</summary>
        Open,
    }

    /// <summary>The building this gate lives on — <see cref="Structure.Index"/>, which is the
    /// name a structure goes by on the wire. Two bytes tells twenty machines which of the ten
    /// arcs on a fixed layout is the one that matters.</summary>
    public readonly int StructureIndex;

    /// <summary>Geometry, copied off the structure once. Copied rather than followed because
    /// these are the numbers both the sim and the renderer place sockets from, and a gate has
    /// to keep answering after its structure has been swept off the field.</summary>
    public readonly Vector2 Position;
    public readonly float Heading;
    public readonly float Scale;

    public Phase State { get; private set; } = Phase.Dark;

    /// <summary>What is seated in each socket, indexed by socket rather than by fill order.
    /// <see cref="Fragment.None"/> is an empty recess.</summary>
    private readonly Fragment[] _socket = new Fragment[SocketCount];

    public Fragment SocketAt(int i) => (uint)i < SocketCount ? _socket[i] : Fragment.None;

    /// <summary>How many are in. 0..5.</summary>
    public int Filled { get; private set; }

    /// <summary>Where this gate is going, once the room has decided. Null until the chart
    /// closes — and a gate refuses fragments until it is set, which is deliberate: choosing
    /// the destination first means the five inserts are the last thing that happens before the
    /// portal, rather than a chore done while an argument about the chart drags on.</summary>
    public PlanetId? Destination { get; private set; }

    // --- The haul in flight -------------------------------------------------------------

    /// <summary>The fragment currently being carried out along the span, or None. One at a
    /// time across the whole gate — five people cannot all be feeding it at once, and a queue
    /// is what makes the five inserts read as five events instead of a pile.</summary>
    public Fragment Carrying { get; private set; }

    /// <summary>Which socket the haul in flight is bound for.</summary>
    public int CarryingTo { get; private set; } = -1;

    /// <summary>Which seat fed it, so the announce can say who.</summary>
    public int CarryingSeat { get; private set; } = -1;

    /// <summary>0..1 along the haul. The renderer rides the fragment on this.</summary>
    public float CarryT { get; private set; }

    /// <summary>How long a haul takes at its longest — a foot socket, the full length of the
    /// span. The keystone is a shorter trip and takes proportionally less, which is the whole
    /// reason the five inserts do not sound and look identical.</summary>
    public const float HaulTime = 2.4f;

    /// <summary>The shortest a haul may take, so the keystone is still an event rather than a
    /// blink. Reached at zero distance, which never happens — it is a floor, not a case.</summary>
    private const float HaulFloor = 0.7f;

    // --- Opening -------------------------------------------------------------------------

    /// <summary>0..1 of the way to open, once all five are seated.</summary>
    public float Charge { get; private set; }

    /// <summary>How long the span takes to wind up with all five in. Long, on purpose: it is
    /// the only stretch of a DESCENT where the room has done everything it can and is standing
    /// still watching something happen. That is worth eleven seconds.</summary>
    public const float ChargeTime = 11f;

    /// <summary>Seats that have stepped through, as a bitmask. Entering is committing — there
    /// is no bit that ever gets cleared here except by the whole gate resetting.</summary>
    public int Entered { get; private set; }

    /// <summary>Seconds left on the grace clock, or 0 when it is not running. Started by the
    /// FIRST player through, and when it expires everybody still outside goes with them.
    ///
    /// <para>This exists because "everyone must enter" is otherwise a soft-lock waiting for one
    /// person to walk away from the keyboard. Nineteen people cannot be held on a cleared
    /// planet by somebody who is making a cup of tea.</para></summary>
    public float Grace { get; private set; }

    /// <summary>How long the room has once the first craft is through.</summary>
    public const float GraceTime = 30f;

    /// <summary>True once the gate has taken everybody it is going to take and the world should
    /// hand the room to the next planet. Latched — the world reads it on whatever tick it
    /// notices, and it must not have gone away by then.</summary>
    public bool Departed { get; private set; }

    public Arch(Structure on)
    {
        StructureIndex = on.Index;
        Position = on.Position;
        Heading = on.Heading;
        Scale = on.Scale;
    }

    /// <summary>Test/wire constructor: a gate with no building behind it.</summary>
    public Arch(int index, Vector2 at, float heading, float scale)
    {
        StructureIndex = index;
        Position = at;
        Heading = heading;
        Scale = scale;
    }

    // --- Geometry, shared with the renderer ------------------------------------------------

    /// <summary>Half the span, in world units, for this instance's scale.</summary>
    public float HalfSpan => Structure.ArchHalfSpan * Scale;

    /// <summary>The direction the span runs, on the plane. The same expression
    /// <see cref="Structure.Blockers"/> uses to put the legs down, so the sockets are strung
    /// between the two feet the player actually drives around.</summary>
    public Vector2 Along => new(MathF.Cos(Heading), -MathF.Sin(Heading));

    /// <summary>
    /// Where socket <paramref name="i"/> is, in world space with Y up.
    ///
    /// <para>The curve is the same half-ellipse the mesh is built from — springing at
    /// <see cref="SpringHeight"/>, rising by <see cref="ArchRise"/> — because a socket drawn on
    /// the stonework and a socket the sim flies a fragment to have to be the same place. The
    /// last time an entity and its renderer each derived a position from their own idea of the
    /// geometry, a boss's core ended up buried inside its own chest.</para>
    /// </summary>
    public Vector3 SocketPoint(int i)
        => CurvePoint(SocketAlong[Math.Clamp(i, 0, SocketCount - 1)]);

    /// <summary>
    /// A point on the span, <paramref name="along"/> running 0 at the left foot to 1 at the
    /// right. The same half-ellipse the mesh is built from.
    ///
    /// <para>Public because the <em>haul</em> rides it: a fragment fed in at the panel travels
    /// from the right foot up over the crown and down to wherever it is going, following the
    /// stonework rather than flying through it. That is the difference between a rail and a
    /// thing being teleported into place, and it is the whole of why the insert reads as
    /// mechanical.</para>
    /// </summary>
    public Vector3 CurvePoint(float along)
    {
        // 0..1 along the span → the ellipse's parameter, π at the left foot and 0 at the right.
        float t = MathF.PI * (1f - Math.Clamp(along, 0f, 1f));
        Vector2 flat = Position + Along * (HalfSpan * MathF.Cos(t));
        float y = (SpringHeight + ArchRise * MathF.Sin(t)) * Scale;
        Vector2 w = Torus.Wrap(flat);
        return new Vector3(w.X, y, w.Y);
    }

    /// <summary>Where the fragment in flight is right now, or null when nothing is moving.
    /// Read by the renderer; derived here so there is one answer to "where is it".</summary>
    public Vector3? CarryPoint()
    {
        if (Carrying == Fragment.None || CarryingTo < 0) return null;
        // Starts at the right-hand foot, where the panel is, and eases out along the span.
        // Smoothstepped rather than linear: a rail with a load on it takes up slack, hauls,
        // and sets down — it does not start and stop at full speed.
        float e = CarryT * CarryT * (3f - 2f * CarryT);
        return CurvePoint(1f + (SocketAlong[CarryingTo] - 1f) * e);
    }

    /// <summary>Where the panel hangs: on the right-hand leg, at something like eye height for
    /// a craft standing in front of it. Everything a player does to a gate happens here.</summary>
    public Vector3 PanelPoint()
    {
        Vector2 flat = Torus.Wrap(Position + Along * (HalfSpan - 2.2f * Scale));
        return new Vector3(flat.X, PanelHeight * Scale, flat.Y);
    }

    /// <summary>Where the portal opens: under the middle of the span, standing on the grid.
    /// Not at the keystone — the keystone is fifteen units up and the thing has to be walked
    /// into.</summary>
    public Vector2 MouthPoint() => Torus.Wrap(Position);

    /// <summary>These mirror <c>StructureMeshes</c>' own numbers for the arc's curve. Duplicated
    /// deliberately and guarded by a self-test: the renderer's copy describes a mesh and this
    /// copy describes where things are, and the alternative to two named constants that must
    /// agree is one constant in a rendering namespace that the simulation has to reach into.</summary>
    public const float SpringHeight = 7f;
    public const float ArchRise = 15f;

    /// <summary>How far up the leg the panel sits, at scale 1.</summary>
    public const float PanelHeight = 3.2f;

    /// <summary>How close a craft has to be to the panel to work it. Generous — a gate is a
    /// landmark you drive up to, not a switch you have to line up on.</summary>
    public const float Reach = 9f;

    /// <summary>Whether <paramref name="at"/> is close enough to the panel to use it.</summary>
    public bool InReach(Vector2 at)
    {
        var p = PanelPoint();
        return Torus.DistanceSquared(at, new Vector2(p.X, p.Z)) <= Reach * Reach;
    }

    /// <summary>How far a craft is from the mouth, for the step-through test.</summary>
    public float DistanceToMouth(Vector2 at) => Torus.Distance(at, MouthPoint());

    /// <summary>
    /// How wide the opening is. A craft inside this and the gate is open has gone through —
    /// there is no button, you drive into it.
    ///
    /// <para>Deliberately much smaller than the arc it hangs under. The first build made it
    /// seven units at scale — half the span — and the result was a translucent dome that filled
    /// the entire frame from thirty units out: you could not see the arch, the fragments on the
    /// ground beside it, or where its edge was. A portal has to be a door in a building, not
    /// weather.</para>
    /// </summary>
    public float MouthRadius => 4.2f * Scale;

    // --- The life of a gate ----------------------------------------------------------------

    /// <summary>Takes power. Every gate on the planet does this at once when the Colossus goes
    /// down; nothing distinguishes them until one is claimed.</summary>
    public void Light()
    {
        if (State == Phase.Dark) State = Phase.Lit;
    }

    /// <summary>Goes back to being scenery — what happens to every gate that was not the one
    /// somebody opened first.</summary>
    public void Douse()
    {
        if (State == Phase.Lit) State = Phase.Dark;
    }

    /// <summary>
    /// Claims this gate. Returns false if it was not available to claim, which is the whole
    /// race: twenty people can be reaching for four panels and exactly one of them wins each.
    /// </summary>
    public bool Claim()
    {
        if (State != Phase.Lit) return false;
        State = Phase.Claimed;
        return true;
    }

    /// <summary>Sets where this gate goes. Called once, when the chart closes.</summary>
    public void SetDestination(PlanetId where)
    {
        if (State == Phase.Claimed) Destination = where;
    }

    /// <summary>Which socket the next fragment fed in will fill, or -1 when the span is full.</summary>
    public int NextSocket() => Filled >= SocketCount ? -1 : FillOrder[Filled];

    /// <summary>Whether the gate will take a fragment from somebody right now: claimed, aimed
    /// at somewhere, not already hauling one, and with a recess left to fill.</summary>
    public bool CanAccept
        => State == Phase.Claimed && Destination is not null
           && Carrying == Fragment.None && Filled < SocketCount;

    /// <summary>
    /// Takes one fragment off a player and starts it out along the span. The pack is emptied by
    /// the caller — this half is only the gate's, so both ends of the wire can run it.
    /// </summary>
    public bool Feed(Fragment kind, int seat)
    {
        if (!CanAccept || kind == Fragment.None) return false;
        Carrying = kind;
        CarryingTo = NextSocket();
        CarryingSeat = seat;
        CarryT = 0f;
        return true;
    }

    /// <summary>How long the haul in flight takes, from how far out its socket is. The feet are
    /// the full trip; the keystone is barely half of it.</summary>
    public float HaulDuration(int socket)
    {
        float along = SocketAlong[Math.Clamp(socket, 0, SocketCount - 1)];
        // Distance from the panel, which is at the right-hand foot — so socket 4 is the short
        // trip and socket 0 is the long one, all the way across.
        float reach = MathF.Abs(1f - along);
        return HaulFloor + (HaulTime - HaulFloor) * reach;
    }

    /// <summary>0..1 of how far this haul has to travel, for the rail's own noise.</summary>
    public float HaulReach(int socket)
        => MathF.Abs(1f - SocketAlong[Math.Clamp(socket, 0, SocketCount - 1)]);

    /// <summary>
    /// One tick. Returns what just happened so the world can make a noise about it — the gate
    /// itself has no idea what a cue is.
    /// </summary>
    public ArchEvent Step(float dt, int liveSeats)
    {
        // A haul in flight.
        if (Carrying != Fragment.None)
        {
            CarryT += dt / MathF.Max(0.01f, HaulDuration(CarryingTo));
            if (CarryT < 1f) return ArchEvent.None;

            _socket[CarryingTo] = Carrying;
            Filled++;
            Carrying = Fragment.None;
            CarryT = 0f;
            int landed = CarryingTo;
            CarryingTo = -1;

            // The fifth is not a fifth socket closing, it is the span taking its own weight.
            if (Filled >= SocketCount)
            {
                State = Phase.Charging;
                Charge = 0f;
                return ArchEvent.Keystone;
            }
            _ = landed;
            return ArchEvent.Seated;
        }

        if (State == Phase.Charging)
        {
            Charge = MathF.Min(1f, Charge + dt / ChargeTime);
            if (Charge < 1f) return ArchEvent.None;
            State = Phase.Open;
            return ArchEvent.Opened;
        }

        if (State == Phase.Open && Grace > 0f)
        {
            Grace = MathF.Max(0f, Grace - dt);
            if (Grace <= 0f && !Departed)
            {
                // Time is up. Everybody outside comes too — nobody is left behind on a planet
                // that has already been left.
                Departed = true;
                return ArchEvent.Departed;
            }
        }

        // Everybody who could go, has. Checked here rather than in Enter so a seat going out
        // of play while others wait — dying out, disconnecting — is what closes the gate,
        // which is the second half of the anti-soft-lock rule.
        if (State == Phase.Open && !Departed && liveSeats > 0 && EnteredCount >= liveSeats)
        {
            Departed = true;
            return ArchEvent.Departed;
        }

        return ArchEvent.None;
    }

    /// <summary>How many seats are through.</summary>
    public int EnteredCount => System.Numerics.BitOperations.PopCount((uint)Entered);

    /// <summary>Whether a seat has already gone through.</summary>
    public bool HasEntered(int seat) => (uint)seat < 32 && (Entered & (1 << seat)) != 0;

    /// <summary>
    /// Puts a seat through. Returns false if it was already in or the gate is not open —
    /// entering is committing, and a seat that is through never comes back out.
    /// </summary>
    public bool Enter(int seat)
    {
        if (State != Phase.Open || (uint)seat >= 32 || HasEntered(seat)) return false;
        Entered |= 1 << seat;
        // The first one through starts the clock on everybody else.
        if (Grace <= 0f && EnteredCount == 1) Grace = GraceTime;
        return true;
    }

    /// <summary>
    /// Client-side: takes the host's whole account of this gate at once.
    ///
    /// <para>A straight overwrite, and the only setter on this class that does not go through
    /// the rules above. That is on purpose: every rule here — you cannot claim a claimed gate,
    /// you cannot feed a busy rail, the fifth one closes the span — exists to arbitrate between
    /// twenty people, and arbitration happens in exactly one place. A client re-deciding any of
    /// it locally would be a second referee.
    /// </para>
    ///
    /// <para>Notably it does <em>not</em> re-raise events: the seat, the keystone and the boom
    /// arrive on this machine as ordinary cues through the sound channel, positioned against
    /// this listener, the same as every other noise in the game.</para>
    /// </summary>
    public void NetSet(Phase phase, PlanetId? destination, ReadOnlySpan<Fragment> sockets,
        Fragment carrying, int carryingTo, float carryT, float charge, int entered, float grace)
    {
        State = phase;
        Destination = destination;

        int filled = 0;
        for (int i = 0; i < SocketCount; i++)
        {
            _socket[i] = i < sockets.Length ? sockets[i] : Fragment.None;
            if (_socket[i] != Fragment.None) filled++;
        }
        Filled = filled;

        Carrying = carrying;
        CarryingTo = carryingTo;
        CarryT = carryT;
        Charge = charge;
        Entered = entered;
        Grace = grace;
    }

    /// <summary>What the panel says about how many suns and moons went into it — the mix that
    /// decides what colour the thing opens.</summary>
    public (int Suns, int Moons) Mix()
    {
        int s = 0, m = 0;
        foreach (var f in _socket)
        {
            if (f == Fragment.Sun) s++;
            else if (f == Fragment.Moon) m++;
        }
        return (s, m);
    }

    /// <summary>
    /// 0 for an all-moon gate, 1 for an all-sun one, and the straight proportion between. The
    /// portal's colour is read off this and nothing else — which is what finally gives the
    /// fifty-fifty a consequence, three hours after the game started rolling it.
    ///
    /// <para>Half on an empty span, so a gate that has not been fed yet is not silently a moon
    /// gate: the recesses glow at the midpoint and drift toward whichever way it is being fed
    /// as the fragments go in.</para>
    /// </summary>
    public float Warmth
    {
        get
        {
            var (s, m) = Mix();
            return s + m == 0 ? 0.5f : s / (float)(s + m);
        }
    }
}

/// <summary>What a gate just did, handed back from <see cref="Arch.Step"/> so the world can
/// sound it. The gate makes no noise itself, which is what lets a whole run of one be driven
/// in a headless test.</summary>
public enum ArchEvent : byte
{
    None = 0,
    /// <summary>A fragment reached its recess and the contactors closed.</summary>
    Seated,
    /// <summary>The fifth one landed and the span took its own weight.</summary>
    Keystone,
    /// <summary>It is open.</summary>
    Opened,
    /// <summary>Everybody who is coming is through. Hand the room to the next planet.</summary>
    Departed,
}
