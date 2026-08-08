using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Entities;

namespace Unrendered.World;

/// <summary>
/// The DESCENT half of the world: the run director, the modular bosses it raises, and the
/// translation of a boss's decisions into things that actually happen on the grid.
///
/// <para>Split out of <c>World.cs</c> deliberately. That file is the sandbox — the hunters, the
/// squads, the two hand-built monsters, the salvage drip — and it is already seven thousand
/// lines of it. DESCENT is a different game running on the same field, and threading four
/// hundred more lines through the middle of the sandbox's loop would leave neither readable.
/// Everything here is additive: with <see cref="MatchSettings.Mode"/> on SANDBOX, not one line
/// of this file executes and the world behaves exactly as it always has.</para>
/// </summary>
public sealed partial class World : IDescentField
{
    /// <summary>The run, on a DESCENT world. Null in SANDBOX, and every read of it is guarded —
    /// which is the seam that keeps the two modes from becoming one mode with a flag.</summary>
    public Descent? Run { get; private set; }

    /// <summary>Whether this world is running a descent at all. Read by the HUD, the pause
    /// panel and the spawn gate.</summary>
    public bool IsDescent => Run is not null;

    /// <summary>
    /// The rolled bosses on the field. A list rather than the single slot the Crab-Core and the
    /// Maw-Core each get, because the TWINNED quirk ends a fight with two of them — and because
    /// a boss that arrives while another is dying should not silently delete the corpse.
    /// </summary>
    public readonly List<ModularBoss> Bosses = new();

    /// <summary>The one currently being fought, for the HUD's layer bars. The first living
    /// entry, so a TWINNED pair shows the half that is still up.</summary>
    public ModularBoss? Headline
    {
        get
        {
            foreach (var b in Bosses) if (b.Alive) return b;
            return Bosses.Count > 0 ? Bosses[^1] : null;
        }
    }

    /// <summary>
    /// A shard rising out of a corpse. Purely a thing to look at: the fragment is awarded to
    /// the seat that landed the killing blow the instant the boss dies, so it can never be
    /// missed, lost in the fog, or picked up by the wrong person. What rises here is the
    /// <em>ceremony</em> — the moment that tells the room what just dropped and who has it.
    /// </summary>
    public sealed class FragmentShard
    {
        public required Vector2 Position { get; init; }
        public required Fragment Kind { get; init; }
        public required int Seat { get; init; }
        public float Age;

        /// <summary>How long the rise lasts. Long enough to read the tag that just appeared
        /// under somebody's name and connect the two.</summary>
        public const float Life = 5.5f;
        public float Rise => MathF.Min(1f, Age / (Life * 0.55f));
        public bool Spent => Age >= Life;
    }

    private readonly List<FragmentShard> _shards = new();
    public IReadOnlyList<FragmentShard> Shards => _shards;

    /// <summary>Which seat last landed a blow on the boss. It no longer decides who gets the
    /// fragment — nobody does, it falls on the ground — but it still decides whose kill the
    /// ceremony is credited to in the rise. Falls back to seat zero, which in a solo run is the
    /// only answer there is.</summary>
    private int _lastBossHitBy;

    /// <summary>
    /// Lays a fragment on the grid where a boss died. Its own method rather than a
    /// <c>DropSalvage</c> call because the placement rule is different and has to be: ordinary
    /// salvage is nudged clear of walls and then left wherever it lands, and one of these has to
    /// be <em>findable</em>. There are five on a planet and they are the only way off it.
    /// </summary>
    private void DropFragment(Vector2 at, Fragment kind)
    {
        PickupKind rock = kind == Fragment.Sun ? PickupKind.SunFragment : PickupKind.MoonFragment;

        // Never evicted to make room, so this cannot be pushed off the field by the pile of
        // scrap the same corpse is about to drop — DropSalvage protects it, and it is added
        // FIRST so that even a field already at its ceiling has it before the scrap arrives.
        DropSalvage(at, rock);
    }

    /// <summary>
    /// How many of a kind <paramref name="seat"/> is carrying, counted out of the pack.
    ///
    /// <para>The pack is the only honest place to ask. A fragment can be picked up by anyone,
    /// dropped when its carrier dies, thrown away deliberately and handed to somebody else by
    /// doing both — so any counter kept alongside the inventory would be a second truth that
    /// drifts the first time a fragment changes hands, which is a thing that now happens
    /// several times a run.</para>
    /// </summary>
    public int FragmentsOf(int seat, Fragment kind)
    {
        if ((uint)seat >= (uint)Players.Count) return 0;
        ItemKind want = kind == Fragment.Sun ? ItemKind.SunFragment : ItemKind.MoonFragment;
        int n = 0;
        foreach (var slot in InventoryOf(seat).Slots)
            if (!slot.IsEmpty && slot.Kind == want) n += slot.Count;
        return n;
    }

    /// <summary>What <paramref name="seat"/> wears under their nickname. Empty for anybody
    /// holding nothing.</summary>
    public string FragmentTag(int seat)
        => Descent.TagFor(FragmentsOf(seat, Fragment.Sun), FragmentsOf(seat, Fragment.Moon));

    // --- The gates -----------------------------------------------------------------------

    /// <summary>
    /// How many of the city's arcs a run makes into gates. Three, out of the ten standing:
    /// enough that lighting them up is a decision — the room can see three columns come on
    /// across the city and has to converge on one — and few enough that the decision is legible
    /// rather than a menu of ten identical options.
    /// </summary>
    public const int GateCount = 3;

    private readonly List<Arch> _gates = new();

    /// <summary>The gates on this planet. Empty in SANDBOX and until a run has landed.</summary>
    public IReadOnlyList<Arch> Gates => _gates;

    /// <summary>The one somebody opened first, or null while they are all still equal. Once this
    /// is set it never changes for the life of the planet.</summary>
    public Arch? ClaimedGate { get; private set; }

    /// <summary>Whether the gates have taken power — i.e. whether the Colossus is down.</summary>
    public bool GatesLit { get; private set; }

    /// <summary>
    /// Set once the room has finished going through, and never cleared: the game loop reads it
    /// on whatever frame it notices and tears this planet down.
    ///
    /// <para>Latched rather than raised as an event because it fires from inside
    /// <see cref="StepGates"/>, which is inside the world's own step — a world cannot replace
    /// itself halfway through advancing, and an event that had to be caught on exactly the
    /// right frame would be an event that is sometimes missed.</para>
    /// </summary>
    public Arch? Departed { get; private set; }

    /// <summary>Whether this arc is one of the run's gates, and therefore not something anybody
    /// gets to knock over. Cheap enough to ask inside the felling path: there are three of
    /// them.</summary>
    public bool IsGate(Structure s)
    {
        if (s.Kind != StructureKind.Arch) return false;
        foreach (var g in _gates) if (g.StructureIndex == s.Index) return true;
        return false;
    }

    /// <summary>
    /// Picks which arcs this run will leave through, at the drop.
    ///
    /// <para>Chosen from the run's seed rather than at random, so a seed reproduces a whole
    /// session down to which three arcs matter — and chosen <em>at the drop</em> rather than
    /// when the Colossus falls, because a gate has to have been standing there, dark, for the
    /// whole run. That is the entire value of putting them on buildings that already existed:
    /// the player has been driving under the way out for three hours.</para>
    /// </summary>
    private void ChooseGates(int seed)
    {
        _gates.Clear();
        ClaimedGate = null;
        GatesLit = false;

        var arcs = new List<Structure>();
        foreach (var s in Structures)
            if (s.Kind == StructureKind.Arch && !s.Gone && !s.Falling) arcs.Add(s);
        if (arcs.Count == 0) return;

        // A seeded shuffle of the candidates, then take the first few. Not "roll an index three
        // times", which can pick the same arc twice and leave a run with one gate.
        var rng = new Random(seed ^ 0x5EED9A7E);
        for (int i = arcs.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (arcs[i], arcs[j]) = (arcs[j], arcs[i]);
        }
        for (int i = 0; i < Math.Min(GateCount, arcs.Count); i++)
            _gates.Add(new Arch(arcs[i]));
    }

    /// <summary>
    /// Every gate on the planet takes power. Called once, the moment the Colossus is confirmed
    /// down — which is the mode's one unambiguous "it is over, now go" and deserves to be heard
    /// from anywhere on the map.
    /// </summary>
    private void WakeGates()
    {
        if (GatesLit || _gates.Count == 0) return;
        GatesLit = true;
        foreach (var g in _gates) g.Light();

        // Sounded at each gate rather than once at the listener, so the noise arrives from the
        // three directions the player now has to choose between — the cue is a bearing as much
        // as it is an announcement.
        foreach (var g in _gates) Emit(Cue.ArchWake, g.Position);
        Announce?.Invoke(_gates.Count == 1
            ? "AN ARCH HAS POWER IN IT"
            : $"{_gates.Count} ARCHES HAVE POWER IN THEM");
    }

    /// <summary>
    /// Claims a gate for the room. The first call wins and every other gate goes dark; every
    /// call after that is refused, which is what makes the race a race.
    /// </summary>
    public bool ClaimGate(Arch gate, int seat)
    {
        if (ClaimedGate is not null || !gate.Claim()) return false;
        ClaimedGate = gate;

        // The others stop being anything. They were only ever candidates, and leaving three
        // columns burning after the choice is made would leave two of them lying about where
        // the way out is.
        foreach (var g in _gates) if (!ReferenceEquals(g, gate)) g.Douse();

        Emit(Cue.ArchClaim, gate.Position);
        Announce?.Invoke($"{NameOrSeat(seat)} OPENED THE ARCH");
        return true;
    }

    /// <summary>
    /// Hands one fragment out of a seat's pack to the claimed gate, and starts it out along the
    /// span. Returns whether the arch actually took it.
    ///
    /// <para>The slot is emptied only once the gate has accepted, and in that order: a rail
    /// that is already busy, an arch already full, or a gate with no course set all refuse, and
    /// a fragment that vanished out of a pack into a refusal would be one fifth of a run gone
    /// to a mis-click.</para>
    ///
    /// <para>Host-authoritative, like everything else about a gate. A client asks and is told;
    /// it never predicts the haul, because the socket a fragment lands in depends on how many
    /// other people fed the thing in the last two seconds.</para>
    /// </summary>
    public bool FeedGate(int seat, int slot)
    {
        Arch? gate = ClaimedGate;
        if (gate is null || !gate.CanAccept) return false;
        if ((uint)seat >= (uint)Players.Count) return false;
        if (!Inventory.Addressable(InvRegion.Slots, slot)) return false;

        Inventory inv = InventoryOf(seat);
        ref ItemStack s = ref inv.Slots[slot];
        if (s.IsEmpty || !IsFragment(s.Kind)) return false;

        // You have to be standing at it. Otherwise the panel is a remote control and the whole
        // business of carrying the things across a city stops meaning anything.
        if (!gate.InReach(Players[seat].Position)) return false;

        Fragment kind = s.Kind == ItemKind.SunFragment ? Fragment.Sun : Fragment.Moon;
        int socket = gate.NextSocket();
        if (!gate.Feed(kind, seat)) return false;

        s.Count--;
        if (s.Count <= 0) s = ItemStack.Empty;

        Emit(Cue.SocketTravel, gate.Position, gate.HaulReach(socket));
        Announce?.Invoke($"{NameOrSeat(seat)} FED THE ARCH");
        return true;
    }

    /// <summary>
    /// Settles where the claimed gate points. Solo this is simply the player's pick; in a room
    /// it is what the twenty-second ballot came to.
    /// </summary>
    public void SetGateDestination(PlanetId where)
    {
        Arch? gate = ClaimedGate;
        if (gate is null || gate.Destination is not null) return;
        gate.SetDestination(where);
        Announce?.Invoke($"COURSE SET   {Planet.Get(where).Name}");
    }

    /// <summary>
    /// Steps whatever gates this planet has: the haul in flight, the charge, the grace clock,
    /// and the moment the room actually leaves.
    /// </summary>
    private void StepGates(float dt)
    {
        if (_gates.Count == 0) return;

        int live = LiveSeatCount();
        foreach (var g in _gates)
        {
            ArchEvent ev = g.Step(dt, live);
            switch (ev)
            {
                case ArchEvent.Seated:
                    Emit(Cue.SocketSeat, g.Position);
                    Announce?.Invoke($"{g.Filled} OF {Arch.SocketCount} SEATED");
                    break;

                case ArchEvent.Keystone:
                    Emit(Cue.ArchKeystone, g.Position);
                    Announce?.Invoke("THE ARCH IS CLOSED");
                    break;

                case ArchEvent.Opened:
                    Emit(Cue.PortalOpen, g.Position);
                    Announce?.Invoke($"THE WAY TO {Planet.Get(g.Destination ?? Match.Destination).Name} IS OPEN");
                    break;

                case ArchEvent.Departed:
                    // Latched for the game loop, which owns the hand-off — a world cannot
                    // rebuild itself from inside its own step, and this fires deep inside one.
                    Departed = g;
                    break;
            }
        }

        DriveGateSound();
        CollectGateWalkers();
    }

    /// <summary>
    /// How many seats could still walk through a gate: connected, in the match, and not out of
    /// the run. The denominator of the "N of M through" count.
    ///
    /// <para>Spectating and away seats are deliberately excluded. Waiting for somebody who is
    /// dead out or has closed the game is waiting forever, and that is the single most likely
    /// way a three-hour session ends in nobody getting anywhere.</para>
    /// </summary>
    private int LiveSeatCount()
    {
        int n = 0;
        foreach (var p in Players)
            if (!p.Away && !p.Spectating) n++;
        return n;
    }

    /// <summary>The denominator on the transit screen's count — how many seats the gate is
    /// waiting for at all. Public because it is drawn, not only decided with.</summary>
    public int SeatsStillComing => LiveSeatCount();

    /// <summary>Puts anybody standing in an open gate through it. There is no key for this: the
    /// mouth is a place on the grid and you drive into it, which is the only interaction in the
    /// game that needs no button at all.</summary>
    private void CollectGateWalkers()
    {
        Arch? gate = ClaimedGate;
        if (gate is null || gate.State != Arch.Phase.Open) return;

        for (int seat = 0; seat < Players.Count; seat++)
        {
            PlayerTank p = Players[seat];
            if (p.Away || p.Spectating || gate.HasEntered(seat)) continue;
            if (gate.DistanceToMouth(p.Position) > gate.MouthRadius) continue;

            if (!gate.Enter(seat)) continue;
            Emit(Cue.PortalEnter, gate.Position);
            Announce?.Invoke($"{gate.EnteredCount} OF {LiveSeatCount()} THROUGH");
        }
    }

    /// <summary>Voices whichever gate is winding up. One bed for the whole planet — there is
    /// only ever one gate charging, because there is only ever one claimed.</summary>
    private void DriveGateSound()
    {
        Arch? g = ClaimedGate;
        bool charging = g is { State: Arch.Phase.Charging };
        Audio.SetPortalCharge(charging, g?.Position ?? Vector2.Zero, g?.Charge ?? 0f);
    }

    /// <summary>Per-seat snare clocks. A snared craft has <see cref="PlayerTank.Rooted"/> held
    /// down for the duration and released here, rather than by whatever set it — nothing else
    /// in the game roots a player for a fixed time, so the timer has to live somewhere.</summary>
    private readonly float[] _snared = new float[MatchSettings.MaxSeats];

    /// <summary>Whether anybody is holding READY this frame. Filed by the input pass and read
    /// by the director during an intermission.</summary>
    private bool _readyHeld;

    /// <summary>Called from the input pass each tick so the director can see the hold. Any seat
    /// holding it counts — a break ends when <em>somebody</em> is done with it, not when
    /// everybody is, because one person waiting out ninety seconds for a team-mate who has
    /// walked away from the keyboard is the worst version of this.</summary>
    public void FileReady(bool held) => _readyHeld = held;

    /// <summary>
    /// The session this world is one leg of. Handed in when a world is built so it survives the
    /// portal: the world object is thrown away and rebuilt on the far side, and this is the
    /// handful of facts that must not be.
    ///
    /// <para>Null on a SANDBOX world and in the tests that stand a lone DESCENT up, which is
    /// the state everything here already handled before a campaign existed.</para>
    ///
    /// <para>Set by the constructor, not by an initialiser — see the note on the
    /// <see cref="World(Loadout, MatchSettings, Campaign)"/> parameter, which is where that
    /// distinction cost a whole feature quietly not working.</para>
    /// </summary>
    public Campaign? Session { get; }

    /// <summary>Whether the session has crossed a world. Read by the arch's chart, which will
    /// not offer anywhere the room has already been.</summary>
    public bool HasVisited(PlanetId id) => Session?.HasVisited(id) ?? false;

    /// <summary>The visited set as a bitmask, for the panel to lock its chart with.</summary>
    public int VisitedMask => Session?.Visited ?? 0;

    /// <summary>Stands a run up. Called from the constructor on a DESCENT world; the seed is
    /// open so a self-test or a capture can pin the whole session's five bosses.</summary>
    private void OpenDescent(int? seed = null)
    {
        // Standing on a world counts as having been to it, immediately — not when you leave.
        // An arch that offered the planet you are underneath would be a three-hour run ending
        // in a door back into the room you just walked out of.
        Session?.Arrive(Match.Destination);

        Run = new Descent(Match.Destination, seed ?? Session?.SeedFor(Session.Hops))
        {
            Session = Session,
            Hop = Session?.Hops ?? 0,
        };
        // The sandbox's opening hunter and its endless top-ups have no business here: a
        // descent's roster is exact, and something wandering in off the horizon would put the
        // wave bar permanently out of step with what is actually on the field.
        DynamicSpawning = false;

        // Which arcs are the way off this planet, decided now, before a shot is fired. They
        // stand dark for the whole run and the player drives under them without knowing.
        ChooseGates(Run.Seed);

        // UNRENDERED_DESCENT can name a phase rather than just "1", so the layer stack and the
        // salvage clock can be photographed without playing four waves to reach them.
        switch (Environment.GetEnvironmentVariable("UNRENDERED_DESCENT"))
        {
            case "colossus": Run.SkipTo(DescentPhase.Colossus, Descent.WaveCount, this); break;
            case "herald": Run.SkipTo(DescentPhase.Herald, 2, this); break;
            case "intermission": Run.SkipTo(DescentPhase.Intermission, 3, this); break;
        }

        StageGateForCapture();
    }

    /// <summary>
    /// Capture hatch: drops a gate straight into a given state and stands the craft in front of
    /// it, so every stage of the way off a planet can be photographed without playing one.
    ///
    /// <para><c>UNRENDERED_ARCH=lit|claimed|sockets1..4|closed|charging|open</c>. Without this
    /// the only way to see a lit arch is to kill a Colossus, and the only way to see a portal is
    /// to find five fragments first — which is three hours per screenshot, and therefore a part
    /// of the game nobody would ever check.</para>
    /// </summary>
    private void StageGateForCapture()
    {
        string? spec = Environment.GetEnvironmentVariable("UNRENDERED_ARCH");
        if (string.IsNullOrWhiteSpace(spec) || _gates.Count == 0) return;

        spec = spec.ToLowerInvariant();
        WakeGates();
        if (spec == "lit")
        {
            StandTheCraftAt(_gates[0], back: 44f);
            return;
        }

        Arch gate = _gates[0];
        ClaimGate(gate, LocalIndex);
        gate.SetDestination(Match.Destination == PlanetId.Kirene
            ? PlanetId.Abysse : PlanetId.Kirene);

        // How many are already seated. Fed straight in rather than hauled, since a capture is a
        // still and the rail's two seconds would just be two seconds of nothing to photograph.
        int seat = spec switch
        {
            "sockets1" => 1, "sockets2" => 2, "sockets3" => 3, "sockets4" => 4,
            "closed" or "charging" or "open" => Arch.SocketCount,
            _ => 0,
        };
        // A deliberate mix, so the socket colours and the portal's temperature are both
        // exercised rather than a capture only ever showing five of one.
        for (int i = 0; i < seat; i++)
        {
            gate.Feed(i % 3 == 0 ? Fragment.Moon : Fragment.Sun, LocalIndex);
            for (int t = 0; t < 400 && gate.Carrying != Fragment.None; t++) gate.Step(0.05f, 1);
        }

        if (spec == "charging") gate.Step(Arch.ChargeTime * 0.75f, 1);
        else if (spec == "open") gate.Step(Arch.ChargeTime + 0.5f, 1);

        // Stood back far enough to frame the whole span. A gate is the biggest object on the
        // map and the only one worth photographing whole.
        StandTheCraftAt(gate, back: 44f);

        // And a couple of loose fragments on the grid beside it, because half of what this
        // feature looks like is rocks lying about waiting to be carried.
        DropFragment(Torus.Wrap(gate.Position + new Vector2(9f, 7f)), Fragment.Sun);
        DropFragment(Torus.Wrap(gate.Position + new Vector2(-6f, 11f)), Fragment.Moon);
    }

    /// <summary>Stands the craft off a gate, looking down its span.</summary>
    private void StandTheCraftAt(Arch gate, float back)
    {
        // Off the face of the arc rather than along it, so the whole span is across the frame
        // instead of end-on.
        var facing = new Vector2(-MathF.Sin(gate.Heading), -MathF.Cos(gate.Heading));
        Player.Position = Torus.Wrap(gate.Position + facing * back);
        Player.Heading = MathF.Atan2(-facing.X, -facing.Y);
    }

    // --- Crossing --------------------------------------------------------------------------

    /// <summary>
    /// Carries every seat over from the world that was just left behind.
    ///
    /// <para><b>Everything comes through.</b> Hull, shields, reserve, magazine, revives spent
    /// and the whole pack — you arrive on the next planet exactly as you left the last one,
    /// wounds and all. That is what makes the salvage window before a Colossus a decision
    /// rather than a habit: a room deciding whether it is fit to travel is the only moment in a
    /// three-hour crossing where preparation is about something other than the next ninety
    /// seconds.</para>
    ///
    /// <para>Revives come too, and deliberately. They are a resource for the whole crossing:
    /// a room that has burned four lives on THALOS is a room in trouble on KIRENE, and
    /// refilling them at every portal would make dying free after the first world.</para>
    ///
    /// <para>Called on a freshly-built world, before anything has stepped. The seats it copies
    /// into are the ones the constructor already made, so a room that lost somebody between
    /// planets simply has fewer to fill.</para>
    /// </summary>
    public void CarryOverFrom(World old)
    {
        // One buffer for the whole room, outside the loop: a stackalloc per seat is twenty
        // allocations deep on the same frame, which is a stack overflow waiting for a full
        // match to cross a portal.
        Span<byte> buf = stackalloc byte[Inventory.WireSize];

        for (int seat = 0; seat < Players.Count && seat < old.Players.Count; seat++)
        {
            PlayerTank was = old.Players[seat], now = Players[seat];

            // The three layers, as they stood. Clamped into this craft's own maxima rather than
            // copied raw: the build is the same across a crossing, but a chassis whose maximum
            // moved for any reason must not arrive over its own ceiling.
            now.Health = Math.Clamp(was.Health, 0f, now.MaxHealth);
            now.Shield = Math.Clamp(was.Shield, 0f, now.MaxShield);
            now.Hyper = Math.Clamp(was.Hyper, 0f, now.MaxHyper);
            now.Ammo = Math.Clamp(was.Ammo, 0, now.MaxAmmo);
            now.Rockets = Math.Clamp(was.Rockets, 0, now.MaxRockets);
            now.Lives = Math.Max(0, was.Lives);

            // And the pack, whole — round-tripped through the wire format rather than copied
            // slot by slot, so the one description of what an inventory is stays in one place
            // and a crossing can never quietly lose a bench somebody added later.
            Inventory from = old.InventoryOf(seat), into = InventoryOf(seat);
            from.Write(buf);
            into.Read(buf);
        }
    }

    // --- The wire ---------------------------------------------------------------------------

    /// <summary>
    /// Client-side: stands this machine's copy of the run up from what the host said, and picks
    /// the same gates the host did.
    ///
    /// <para>Almost nothing about a planet actually travels. The city's layout is identical on
    /// every machine (one fixed seed, in <see cref="StructureField"/>), and the gates are a
    /// seeded shuffle over it — so "which three arcs" is a function of the run seed and costs
    /// four bytes rather than a description of three structures. The same trick already carries
    /// the five bosses: the genome is rolled from the seed on both ends, and only the body's
    /// position is streamed.</para>
    ///
    /// <para>Idempotent. The host re-sends this on every phase change and to every late joiner,
    /// and a second call with the same seed must not throw the run away and rebuild it — that
    /// would reset the wave bar and re-light the gates every few minutes.</para>
    /// </summary>
    public void NetOpenRun(int seed, int hop, PlanetId destination, int visited)
    {
        if (Run is { } existing && existing.Seed == seed) return;

        // The campaign is rebuilt rather than adopted: a client never runs one of its own, and
        // the only two things it needs out of it are where the room has been — so the arch's
        // chart strikes the right worlds out — and which leg this is, for the difficulty curve
        // its own boss rolls have to match.
        var session = new Campaign(seed);
        foreach (var p in Planet.All)
            if ((visited & (1 << (int)p.Id)) != 0) session.Arrive(p.Id);
        for (int i = 0; i < hop; i++) session.Hop();
        _netSession = session;

        Run = new Descent(destination, seed) { Session = session, Hop = hop };
        DynamicSpawning = false;
        ChooseGates(seed);
    }

    // The client's rolled bodies, rebuilt each snapshot. Kept as a working list so a body that
    // is still on the field keeps its own object — and therefore its animation phase, its gait
    // and its shed plates — rather than being thrown away and remade twenty times a second,
    // which would leave every boss on a client frozen in its arrival pose for ever.
    private readonly List<ModularBoss> _adopting = new();

    /// <summary>Client-side: opens a pass over the rolled bodies a packet describes.</summary>
    public void BeginAdoptRolled() => _adopting.Clear();

    /// <summary>
    /// Client-side: one rolled body, rebuilt from what it was rolled from and posed where the
    /// host says.
    ///
    /// <para>Matched to an existing body by genome seed rather than by list position, because
    /// the host writes only what is near this client and the order changes as people move —
    /// matching by index would have a boss's identity swap the moment a second one came into
    /// range, and the second one is a different animal.</para>
    /// </summary>
    public void AdoptRolled(BossGenome gene, Vector2 at, float heading, float height,
        ModularBoss.State phase, int layersLeft, float topFraction)
    {
        ModularBoss? body = null;
        foreach (var b in Bosses)
            if (b.Gene.Seed == gene.Seed) { body = b; break; }

        if (body is null)
        {
            body = new ModularBoss(gene, at, heading);
            Bosses.Add(body);
        }

        body.NetSet(at, heading, height, phase, layersLeft, topFraction);
        _adopting.Add(body);
    }

    /// <summary>Client-side: closes the pass, dropping any body the host did not mention. That
    /// is how a client is told a boss died or walked out of range — the same keep-last shape the
    /// field packet already uses for hunters.</summary>
    public void EndAdoptRolled()
    {
        for (int i = Bosses.Count - 1; i >= 0; i--)
            if (!_adopting.Contains(Bosses[i])) Bosses.RemoveAt(i);
        _adopting.Clear();
    }

    /// <summary>The session on a client, rebuilt off the wire rather than owned. Kept separate
    /// from the init-only <see cref="Session"/> so a host's world can never have its campaign
    /// quietly replaced by a packet.</summary>
    private Campaign? _netSession;

    /// <summary>Client-side: takes the host's account of where the run is.</summary>
    public void NetAdoptRun(DescentPhase phase, int wave, int killed, int total, float clock,
        bool gatesLit)
    {
        Run?.NetAdopt(phase, wave, killed, total, clock);
        if (gatesLit) WakeGates();
    }

    /// <summary>
    /// Client-side: takes the host's account of the one gate that matters. Everything about a
    /// gate is decided on the host — which arch, where it points, what is seated, how far the
    /// charge has got, who has stepped through — so this is a straight overwrite rather than a
    /// reconciliation. A client predicts none of it, deliberately: the socket a fragment lands
    /// in depends on how many other people fed the thing in the last two seconds, and there is
    /// no honest local guess at that.
    /// </summary>
    public void NetAdoptGate(int structureIndex, Arch.Phase phase, PlanetId? destination,
        ReadOnlySpan<Fragment> sockets, Fragment carrying, int carryingTo, float carryT,
        float charge, int entered, float grace)
    {
        foreach (var g in _gates)
        {
            if (g.StructureIndex != structureIndex) continue;

            // A claim arriving goes through the world's own door rather than being set on the
            // gate, so the other gates go dark and the claim is heard on this machine exactly
            // as it was on the host's.
            if (ClaimedGate is null && phase >= Arch.Phase.Claimed)
            {
                GatesLit = true;
                foreach (var other in _gates) other.Light();
                ClaimGate(g, LocalIndex);
            }

            g.NetSet(phase, destination, sockets, carrying, carryingTo, carryT, charge,
                entered, grace);
            return;
        }
    }

    /// <summary>
    /// Capture hatch: stands one rolled boss on the axis in front of the craft so a
    /// procedurally-built monster can actually be looked at.
    ///
    /// <para>This exists because a generator is only as good as the bodies you have checked, and
    /// a body that only appears three waves into a live run is a body nobody will ever check.
    /// <c>UNRENDERED_BOSS_ROLL=&lt;lineage&gt;[:colossus][:&lt;seed&gt;]</c> — for instance
    /// <c>drowner:colossus:404</c> — puts that exact one in front of the lens, every time, on any
    /// world. The seed makes it reproducible, which is the only way to photograph a thing whose
    /// whole design is being different each time.</para>
    /// </summary>
    private void StageRolledBossForCapture()
    {
        string? spec = Environment.GetEnvironmentVariable("UNRENDERED_BOSS_ROLL");
        if (string.IsNullOrWhiteSpace(spec)) return;

        var parts = spec.Split(':', StringSplitOptions.RemoveEmptyEntries);
        BossLineage lineage = BossLineage.Stalker;
        bool colossus = false;
        int seed = 1;
        // Which pose to freeze in. WALK is the default; the rest exist so the animation work can
        // actually be checked — a wind-up that only happens for a second and a half in every ten
        // is otherwise photographed by luck.
        ModularBoss.State pose = ModularBoss.State.Stalking;
        int shed = 0;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "wind": pose = ModularBoss.State.Winding; continue;
                case "strike": pose = ModularBoss.State.Striking; continue;
                case "recover": pose = ModularBoss.State.Recover; continue;
                case "break": pose = ModularBoss.State.Breaking; continue;
                // How many layers to have already broken off, so the stripped-down late-fight
                // body can be photographed without playing the first four bars.
                case "shed1": shed = 1; continue;
                case "shed2": shed = 2; continue;
                case "shed4": shed = 4; continue;
            }

            // The number is tested FIRST, and deliberately: Enum.TryParse happily accepts a
            // numeric string and hands back BossLineage 1234, so a seed offered after a lineage
            // silently overwrote it with a nonsense value — which is how the first capture of
            // this system came out as a COLUMN when it had asked for a STALKER.
            if (int.TryParse(part, out int n)) seed = n;
            else if (part.Equals("colossus", StringComparison.OrdinalIgnoreCase)) colossus = true;
            else if (Enum.TryParse(part, ignoreCase: true, out BossLineage l)) lineage = l;
        }

        BossGenome gene = colossus
            ? BossGen.Colossus(seed, lineage, 1f)
            : BossGen.Herald(seed, lineage, 0.5f);

        // Pinned at the far end of its band so it holds the distance it is placed at instead of
        // driving into the lens. Without this every capture of a boss is the same picture of a
        // wall of facets: they close on the player, which is what they are for.
        gene = gene with { Standoff = 1f };

        // Stood off by its own size rather than at a fixed distance. A rolled body can be a low
        // seven-unit tread hull or a twenty-five-unit standing figure, and one distance cannot
        // frame both — at 56 units the walkers looked right and the tall ones had their heads
        // out of shot, which is exactly the part you needed to see.
        var probe = new ModularBoss(gene, Vector2.Zero);
        // Height counts the cruise altitude too: a hovering DROWNER's body starts twelve units
        // off the grid before any of it exists, and framing it by its body alone put the whole
        // animal above the top of the shot.
        float span = MathF.Max(probe.Height + probe.CarriageLift * gene.Scale + probe.BodyHeight,
            probe.Radius * 2.2f);
        float back = Math.Clamp(span * 3.6f, 38f, 115f);

        var staged = new ModularBoss(gene, new Vector2(0f, back), MathF.PI);
        // Break the requested number of layers off before anyone looks at it, so the stripped
        // late-fight body is photographable without playing four bars to reach it. Struck from
        // behind, since a SEALED roll would otherwise eat every hit on its plates.
        for (int i = 0; i < shed && staged.LayersLeft > 1; i++)
            staged.Damage(gene.LayerHealth * 2f, fromFront: false);
        staged.Posed = true;
        if (pose != ModularBoss.State.Stalking) staged.PoseAs(pose, gene.Attacks[0]);

        Bosses.Add(staged);
        DynamicSpawning = false;

        // What actually rolled, printed beside the picture. Without this a capture is a body
        // with no account of why it looks like that, and a generator bug is indistinguishable
        // from a renderer bug.
        Console.WriteLine($"BOSS ROLL  {gene.FullName}  seed={gene.Seed} {gene.Lineage}");
        Console.WriteLine($"  carriage={gene.Carriage} limbs={gene.LimbCount} limbLen={gene.LimbLength:0.00} "
            + $"scale={gene.Scale:0.00} spines={gene.SpineCount} oversize={gene.OversizeLimb}");
        Console.WriteLine($"  body={gene.BodyWidth:0.0}x{gene.BodyRise:0.0}x{gene.BodyDepth:0.0} "
            + $"radius={staged.Radius:0.0} height={staged.BodyHeight:0.0} lift={staged.CarriageLift:0.0}");
        Console.WriteLine($"  paint={ClassCatalog.SwatchName(gene.ShellSwatch)}/"
            + $"{ClassCatalog.SwatchName(gene.DeepSwatch)}/{ClassCatalog.SwatchName(gene.LimbSwatch)} "
            + $"core={gene.CoreTone} quirk={gene.Quirk} layers={gene.Layers}");
        Console.WriteLine($"  attacks={string.Join(", ", gene.Attacks)}");
    }

    // --- The tick ------------------------------------------------------------------

    /// <summary>
    /// Steps the whole of DESCENT for one frame: the director, the bosses, everything they
    /// asked for, and the snares they left on people. Called from the host's path only —
    /// a client is shown the result, exactly as it is shown the hunters.
    /// </summary>
    private void StepDescent(float dt)
    {
        if (Run is null) return;

        // The run first: it may raise a boss this tick, and a boss raised now should get its
        // own step rather than standing inert for a frame.
        Run.Update(dt, this, _readyHeld);
        _readyHeld = false;

        // The Colossus is down. This is the mode's one unambiguous "it is over, now go", and
        // it is what the arcs have been standing there waiting for.
        if (Run.Phase == DescentPhase.Cleared) WakeGates();
        StepGates(dt);

        // Solo, the run ends when the craft is out of lives. A room ends it when the room is
        // spent, which PlayerTank.Spectating already tracks for every seat.
        if (!Run.Finished && EveryoneIsSpent()) Run.Fail(this);
    }

    /// <summary>
    /// Steps whatever rolled bosses are on the field, whether or not a run put them there.
    ///
    /// <para>Deliberately <em>not</em> gated on <see cref="IsDescent"/>. A boss is an entity like
    /// any other and has to live wherever it is standing — the capture hatch drops one onto a
    /// SANDBOX world to be photographed, and a boss that is never stepped is one frozen mid-
    /// arrival, sunk halfway into the grid with its limbs unposed. Which is exactly what the
    /// first capture of this system showed.</para>
    /// </summary>
    private void StepRolledBosses(float dt)
    {
        if (Bosses.Count == 0)
        {
            // Let the rotor fade out when the last one goes — but never fight the Crab-Core for
            // the channel, since in SANDBOX that one owns it.
            if (Boss is null) Audio.SetBossHum(false, Vector2.Zero, 0f);
            AgeShards(dt);
            return;
        }

        for (int i = Bosses.Count - 1; i >= 0; i--)
        {
            ModularBoss boss = Bosses[i];
            PlayerTank quarry = NearestPlayer(boss.Position);
            boss.Update(dt, quarry.Position, quarry.Height);

            foreach (var act in boss.Acts) ApplyBossAct(boss, act);
            boss.ClearActs();

            foreach (var cue in boss.Cues) Emit(cue.Id, cue.At, cue.Param);
            boss.ClearCues();

            // Everything the body owes the world, drained rather than sampled. A boss is killed
            // by a round, and rounds are resolved in the projectile pass — which runs earlier in
            // the same frame than this loop — so a flag raised by the hit was wiped by the
            // boss's own Update a few lines above before this ever read it. That is how a
            // fight could end with no fragment on the ground and no line on the screen.
            //
            // Asking the corpse instead makes it independent of where in the frame the killing
            // blow landed: it is dying for the best part of two seconds, and the first tick to
            // notice pays it out. Only the host pays — a client is shown the rock in the field
            // packet like every other piece of salvage, and must not roll one of its own.
            while (boss.TakeLayerBreak()) StageLayerBreak(boss);
            if (Authoritative && !boss.Alive) BossWentDown(boss);

            // The rotor. Driven off whichever boss is loudest — the nearest living one — so a
            // pair of TWINNED halves does not stack two hums on one listener.
            if (boss.Dead) Bosses.RemoveAt(i);
        }

        DriveBossHum();
        AgeShards(dt);
        ReleaseSnares(dt);
    }

    private bool EveryoneIsSpent()
    {
        foreach (var p in Players)
            if (!p.Away && p.Alive) return false;
        return Players.Count > 0;
    }

    private void DriveBossHum()
    {
        ModularBoss? loud = null;
        float best = float.MaxValue;
        foreach (var b in Bosses)
        {
            if (!b.Alive) continue;
            float d = Torus.DistanceSquared(b.Position, Eye.Position);
            if (d < best) { best = d; loud = b; }
        }
        // Never fights the Crab-Core for the channel: a descent has no ambient crab, so
        // whichever of the two is speaking is the only one that can be.
        if (loud is not null)
            Audio.SetBossHum(true, loud.Position, 0.4f + 0.6f * loud.Ruin);
        else if (Boss is null)
            Audio.SetBossHum(false, Vector2.Zero, 0f);
    }

    private void AgeShards(float dt)
    {
        for (int i = _shards.Count - 1; i >= 0; i--)
        {
            _shards[i].Age += dt;
            if (_shards[i].Spent) _shards.RemoveAt(i);
        }
    }

    private void ReleaseSnares(float dt)
    {
        for (int seat = 0; seat < Players.Count && seat < _snared.Length; seat++)
        {
            if (_snared[seat] <= 0f) continue;
            _snared[seat] -= dt;
            if (_snared[seat] <= 0f) Players[seat].Rooted = false;
        }
    }

    // --- Turning a decision into an event ------------------------------------------

    /// <summary>
    /// Carries out one thing a boss asked for. This is the whole of the coupling between the
    /// boss entity and the world: the entity knows what it wants to do and nothing at all about
    /// projectile pools, structure damage or who is standing where.
    /// </summary>
    private void ApplyBossAct(ModularBoss boss, in BossAct act)
    {
        switch (act.Kind)
        {
            case BossActKind.Bolt:
            {
                var dir = new Vector2(act.Dir.X, act.Dir.Z);
                if (dir.LengthSquared() < 1e-6f) break;
                dir = Vector2.Normalize(dir);
                Vector2 from = new Vector2(act.Origin.X, act.Origin.Z) + dir * (boss.Radius + 1f);
                SpawnProjectile(from, dir, owner: Projectile.NoOwner,
                    launchHeight: act.Origin.Y, pitch: act.Dir.Y);
                break;
            }

            case BossActKind.Mortar:
            {
                // Lobbed at a mark on the ground rather than at the player, which is what makes
                // it dodgeable: the shells land where you were, and moving is the answer.
                Vector2 from = new(act.Origin.X, act.Origin.Z);
                Vector2 to = Torus.NearestImage(act.Target, from);
                Vector2 dir = to - from;
                if (dir.LengthSquared() < 1e-6f) break;
                SpawnProjectile(from, Vector2.Normalize(dir), owner: Projectile.NoOwner,
                    grenade: true);
                break;
            }

            case BossActKind.Beam:
            {
                // Reuses the SPIDER's lance burn wholesale — the same shaft that fells towers
                // and rakes hunters, pointed the other way. A boss beam that behaved differently
                // from the one the player carries would be a second physics for the same object.
                BurnBeamAlong(act.Origin, act.Dir, BossBeamLength, act.Power,
                    BossBeamDamage * (act.Power / 2f));
                BurnPlayersAlong(act.Origin, act.Dir, BossBeamLength, act.Power);
                break;
            }

            case BossActKind.Shock:
            {
                // A ring travelling out along the grid. Everything it reaches is thrown and
                // billed, falling off with distance so the edge of it is a shove and the middle
                // of it is a mistake.
                var at = new Vector2(act.Origin.X, act.Origin.Z);
                foreach (var mark in Players)
                {
                    if (!mark.Alive || mark.Away) continue;
                    float d = Torus.Distance(at, mark.Position);
                    if (d > act.Power) continue;
                    // Off the ground is off the shock: a craft in the air genuinely steps over
                    // a ground wave, which gives every chassis that can leave the grid a real
                    // answer to it and leaves the TANK to eat it, as the TANK does.
                    if (mark.Height > 2.5f) continue;
                    float bite = ShockDamage * (1f - d / act.Power);
                    DamagePlayer(bite, mark, at);
                    JoltPlayerView(mark, 0.5f + 0.5f * (1f - d / act.Power));
                }
                Debris.FootPuff(new Vector3(at.X, 0f, at.Y));
                ShakeFromBlast(act.Power);
                break;
            }

            case BossActKind.Summon:
            {
                // It tears open and something walks out. Spawned right at the boss rather than
                // out in the fog, because the whole read of the move is that these came out of
                // it — a hunter fading in on the horizon is the sandbox's director, not this.
                var at = new Vector2(act.Origin.X, act.Origin.Z);
                Enemies.Add(new EnemyTank(Torus.Wrap(at), elite: act.Power >= 1f));
                Debris.Burst(new Vector3(at.X, 1.5f, at.Y), boss.Gene.Core, act.Power >= 1f);
                break;
            }

            case BossActKind.Tether:
            {
                // Reels the nearest craft in toward the boss. Nasty precisely because it is not
                // damage: it puts you where the boss wants you, which is inside whatever it does
                // next.
                PlayerTank mark = NearestPlayer(boss.Position);
                if (!mark.Alive || mark.Away) break;
                Vector2 pull = Torus.Delta(mark.Position, boss.Position);
                float len = pull.Length();
                if (len < 1e-3f) break;
                float reach = MathF.Min(TetherPull, MathF.Max(0f, len - boss.Radius - 3f));
                mark.Position = Torus.Wrap(mark.Position + pull / len * reach);
                Emit(Cue.CableZip, mark.Position);
                JoltPlayerView(mark, 0.4f);
                break;
            }

            case BossActKind.Snare:
            {
                PlayerTank mark = NearestPlayer(boss.Position);
                int seat = Seat(mark);
                if (seat == Projectile.NoOwner || !mark.Alive) break;
                mark.Rooted = true;
                _snared[seat] = MathF.Max(_snared[seat], act.Power);
                Emit(Cue.AnchorBite, mark.Position);
                break;
            }

            case BossActKind.Scream:
            {
                // No damage at all, and that is the point: it wrenches the view, it winds
                // everything else on the field up, and it costs the boss a long recovery to do.
                foreach (var mark in Players)
                {
                    if (!mark.Alive || mark.Away) continue;
                    float d = Torus.Distance(boss.Position, mark.Position);
                    if (d > ScreamRadius) continue;
                    JoltPlayerView(mark, 1f - d / ScreamRadius);
                }
                foreach (var e in Enemies)
                    if (e.Alive) e.PreferredRange = MathF.Max(8f, e.PreferredRange * 0.7f);
                ShakeFromBlast(ScreamRadius);
                break;
            }

            case BossActKind.Shroud:
                LaySmoke(new Vector2(act.Origin.X, act.Origin.Z));
                break;

            case BossActKind.Impact:
            {
                var at = new Vector2(act.Origin.X, act.Origin.Z);
                foreach (var mark in Players)
                {
                    if (!mark.Alive || mark.Away) continue;
                    float d = Torus.Distance(at, mark.Position);
                    if (d > act.Power) continue;
                    DamagePlayer(ImpactDamage * (1f - d / act.Power), mark, at);
                    JoltPlayerView(mark, 0.8f);
                }
                // A body this size coming down moves the floor and everything on it.
                Debris.FootPuff(new Vector3(at.X, 0f, at.Y));
                ShakeFromBlast(act.Power * 1.5f);
                // And it crushes whatever it landed on, which is the reason to bait a leap
                // into a crowd of its own hunters.
                foreach (var e in Enemies)
                    if (e.Alive && Torus.Distance(at, e.Position) < act.Power * 0.5f)
                        DamageEnemy(e, 4f);
                break;
            }
        }
    }

    /// <summary>
    /// The players' half of a beam burn. <c>BurnBeamAlong</c> only ever had hostiles to rake —
    /// it is the SPIDER's weapon — so the shaft coming the other way needs its own pass over
    /// the craft. Same geometry: within the shaft's radius on the plane, and somewhere between
    /// the craft's feet and its head where it crosses.
    /// </summary>
    private void BurnPlayersAlong(Vector3 origin, Vector3 direction, float length, float radius)
    {
        var originXZ = new Vector2(origin.X, origin.Z);
        var dirXZ = new Vector2(direction.X, direction.Z);
        float planar = dirXZ.Length();
        if (planar < 1e-4f) return;
        dirXZ /= planar;
        float slope = direction.Y / planar;

        foreach (var mark in Players)
        {
            if (!mark.Alive || mark.Away || mark.Captured) continue;
            Vector2 near = Torus.NearestImage(mark.Position, originXZ);
            float along = Math.Clamp(Vector2.Dot(near - originXZ, dirXZ), 0f, length);
            if (Vector2.Distance(near, originXZ + dirXZ * along) > radius + PlayerTank.Radius)
                continue;

            float beamY = origin.Y + slope * along;
            if (beamY < mark.Height - radius || beamY > mark.Height + PlayerTank.Radius * 2f + radius)
                continue;

            DamagePlayer(BossBeamPlayerDamage, mark, originXZ);
        }
    }

    // --- Tuning --------------------------------------------------------------------

    /// <summary>How far a boss beam reaches. Shorter than the SPIDER's, because a shaft that
    /// crosses the whole arena is not a thing you dodge, it is a thing that finds you.</summary>
    private const float BossBeamLength = 90f;

    /// <summary>What one tick of beam costs a hunter caught in it. Bosses are not careful about
    /// what else is standing in the line, which is a real tactic against them.</summary>
    private const float BossBeamDamage = 2.5f;

    /// <summary>And what it costs a craft, per tick of contact. Held low per-tick and lethal
    /// over a second: a beam is meant to be something you leave, not something that deletes you
    /// the instant it touches.</summary>
    private const float BossBeamPlayerDamage = 1.6f;

    /// <summary>The ground shock at its centre, falling to nothing at the rim.</summary>
    private const float ShockDamage = 22f;

    /// <summary>A body landing on you.</summary>
    private const float ImpactDamage = 26f;

    /// <summary>How far a scream is felt.</summary>
    private const float ScreamRadius = 55f;

    /// <summary>How far one tether hauls a craft. Enough to be genuinely relocated, not enough
    /// to be teleported into the boss's mouth.</summary>
    private const float TetherPull = 22f;

    // --- Staging the moments -------------------------------------------------------

    /// <summary>
    /// A layer just came off. This is the beat the whole fight is built around, so it gets a
    /// real staging rather than a number changing: the screen washes in the core's own colour,
    /// the plates blow off as debris in the boss's own paint, and the ground clears.
    /// </summary>
    private void StageLayerBreak(ModularBoss boss)
    {
        Vector3 core = boss.CorePoint;

        // The shed plate, thrown outward in the boss's own shell colour so the wreckage is
        // recognisably a piece of *that* monster.
        for (int i = 0; i < 3; i++)
            Debris.Burst(core + new Vector3(
                (Random.Shared.NextSingle() - 0.5f) * boss.Radius * 2f,
                (Random.Shared.NextSingle() - 0.5f) * boss.BodyHeight,
                (Random.Shared.NextSingle() - 0.5f) * boss.Radius * 2f),
                boss.Gene.Shell, elite: true);
        Debris.Burst(core, boss.Gene.Core, elite: true);

        Emit(Cue.CrabScream, boss.Position, 1f);
        ShakeFromBlast(boss.Radius * 5f);

        Announce?.Invoke(boss.LayersLeft == 1
            ? $"{boss.Gene.Name} - LAST LAYER"
            : $"{boss.Gene.Name} - {boss.LayersLeft} LAYERS LEFT");
    }

    /// <summary>
    /// It is down. The rock hits the ground where it fell, the shard rises out of the corpse,
    /// the parts scatter, and the run is told so it can move on to the break.
    ///
    /// <para>Every boss the run raises pays exactly one fragment, always — a fight that ends
    /// with nothing on the ground is a planet nobody can leave. Safe to call more than once and
    /// from anywhere: the corpse itself holds the claim.</para>
    /// </summary>
    private void BossWentDown(ModularBoss boss)
    {
        if (Run is null) return;
        // One payout per body, whoever asks first. The tick asks every frame of the dying and
        // the console asks the instant it kills, so this is the only thing standing between a
        // skipped boss and two fragments for one corpse.
        if (!boss.ClaimDeathPayout()) return;

        Emit(Cue.BossDeath, boss.Position);

        if (boss.PaysFragment)
        {
            int seat = (uint)_lastBossHitBy < (uint)Players.Count ? _lastBossHitBy : 0;
            Fragment got = Run.RollFragment();
            Run.TakePendingFragment();

            // The ceremony — a shard of light standing up out of the corpse — and, underneath
            // it, the rock itself. The light fades after a few seconds; the rock does not fade,
            // ever.
            //
            // Two objects at one place on purpose. The rise is what makes a kill land: it is
            // legible from across the city and it says *a fragment just dropped, and it dropped
            // there*. The pickup is what makes it a thing rather than a medal — somebody now has
            // to walk over and take it, and until they do it is lying in the open.
            _shards.Add(new FragmentShard { Position = boss.Position, Kind = got, Seat = seat });
            DropFragment(boss.Position, got);

            Emit(Cue.MawCrystal, boss.Position, 1f);
            Emit(Cue.FragmentFall, boss.Position);
            // Names what fell and how far along the planet is, rather than who won it — nobody
            // has won anything yet. The count is the line that matters during a run: four of
            // five is the moment a room starts thinking about the arch.
            Announce?.Invoke($"FRAGMENT OF THE {(got == Fragment.Sun ? "SUN" : "MOON")}"
                + $"   {Run.Dropped} OF {Descent.WaveCount}");
        }

        // A boss is worth a real pile of parts — it is the biggest machine on the field and it
        // has just been opened.
        for (int i = 0; i < 5; i++) ScatterMaterials(boss.Position);
        DropSalvage(boss.Position, PickupKind.Battery);
        DropSalvage(boss.Position, PickupKind.Ammo);
        // And a kit. Whoever just fought that has holes in them, and the boss is the one body
        // on the field that is certainly worth enough to have carried one.
        DropSalvage(boss.Position, PickupKind.RepairKit);

        // TWINNED: its death is not the end of it. Two halves, each carrying one layer, rolled
        // off the parent's own seed so they are visibly its children rather than two new
        // strangers. They pay no fragment of their own — the parent already dropped this
        // fight's rock, and a planet has exactly five whatever the quirks roll.
        if (boss.PaysFragment && boss.Gene.Quirk == Quirk.Split && boss.Gene.Layers > 1)
        {
            for (int i = 0; i < 2; i++)
            {
                var half = BossGen.Herald(boss.Gene.Seed + 101 + i, boss.Gene.Lineage) with
                {
                    Scale = boss.Gene.Scale * 0.55f,
                    LayerHealth = boss.Gene.LayerHealth * 0.45f,
                };
                Vector2 at = Torus.Wrap(boss.Position
                    + new Vector2(i == 0 ? 8f : -8f, i == 0 ? 6f : -6f));
                Bosses.Add(new ModularBoss(half, at, boss.Heading) { PaysFragment = false });
            }
            Announce?.Invoke($"{boss.Gene.Name} COMES APART INTO TWO");
        }
    }

    // --- Damage in ------------------------------------------------------------------

    /// <summary>
    /// Puts a player's hit into whichever boss it landed on. Returns true when the round was
    /// consumed, so the projectile pass can stop looking.
    ///
    /// <para>The whole exposure rule lives inside <see cref="ModularBoss.Damage"/> rather than
    /// here — the entity knows whether it is open and whether its plates are in the way, and a
    /// second copy of that reasoning on this side is a second copy that will drift.</para>
    /// </summary>
    private bool StrikeBosses(Vector2 at, float height, float amount, int by, Vector2 from)
    {
        foreach (var boss in Bosses)
        {
            if (!boss.Hits(at, height)) continue;

            float landed = boss.Damage(amount, boss.IsFrontal(from));
            if (by != Projectile.NoOwner) _lastBossHitBy = by;

            if (landed <= 0f)
            {
                // The plates refused it. Said out loud with a hard clang and a spark, because a
                // shot that quietly does nothing reads as the game being broken rather than as
                // armour working.
                Emit(Cue.Clamp, boss.Position);
                Debris.Burst(new Vector3(at.X, height, at.Y), Palette.HudChrome, elite: false);
            }
            else
            {
                Emit(boss.Exposed ? Cue.CoreHit : Cue.Hit, boss.Position,
                    Math.Clamp(boss.Ruin, 0f, 1f));
            }
            return true;
        }
        return false;
    }

    /// <summary>Test hatch: lays one fragment on the grid without a boss having to die for it.</summary>
    public void DropFragmentForTest(Vector2 at, Fragment kind) => DropFragment(at, kind);

    /// <summary>Test hatch: puts one piece of ordinary salvage down, so the eviction rule can be
    /// leaned on hard enough to prove it never releases a fragment.</summary>
    public void DropSalvageForTest(Vector2 at, PickupKind kind) => DropSalvage(at, kind);

    /// <summary>Test hatch: tries to cut a building down, so the guard that keeps a gate
    /// standing can be checked against an ordinary arc that still falls.</summary>
    public bool FellStructureForTest(Structure s) => FellStructure(s);

    /// <summary>Test hatch: puts damage straight into the headline boss, so a self-test can run
    /// a five-layer fight to its end in a few hundred ticks without a projectile pool.</summary>
    public float DamageBossForTest(float amount, bool fromFront = true)
        => Headline?.Damage(amount, fromFront) ?? 0f;

    /// <summary>Test hatch: raises a boss of a known seed and lineage on demand.</summary>
    public ModularBoss SpawnBossForTest(BossGenome gene, Vector2 at)
    {
        var boss = new ModularBoss(gene, at);
        Bosses.Add(boss);
        return boss;
    }

    // --- IDescentField ---------------------------------------------------------------

    void IDescentField.SpawnWaveHunter(bool elite)
    {
        // The wave's own arrivals come in further out than the sandbox's drip, so a crowd
        // resolves on the skyline and walks in rather than appearing at conversational range.
        Enemies.Add(new EnemyTank(RandomPointAroundPlayer(WaveSpawnMin, WaveSpawnMax), elite));
    }

    void IDescentField.SpawnWaveSquad() => SpawnSoldierSquad();

    void IDescentField.RaiseBoss(BossGenome gene)
    {
        // Placed at a fixed, generous distance rather than a rolled one: the arrival is the
        // only clean look a player gets at a procedurally-built silhouette, and it has to land
        // in view rather than behind a tower.
        Vector2 at = RandomPointAroundPlayer(BossSpawnRange, BossSpawnRange + 12f);
        Bosses.Add(new ModularBoss(gene, at));
        _lastBossHitBy = LocalIndex;
    }

    int IDescentField.LiveWaveUnits
    {
        get
        {
            int n = 0;
            foreach (var e in Enemies) if (e.Alive) n++;
            foreach (var s in Soldiers) if (s.Alive) n++;
            return n;
        }
    }

    bool IDescentField.BossAlive
    {
        get
        {
            foreach (var b in Bosses) if (b.Alive) return true;
            return false;
        }
    }

    void IDescentField.DropSalvage(int count)
    {
        for (int i = 0; i < count; i++)
        {
            // The salvage window between waves is where a run is repaired, and hull is the
            // thing a run loses permanently — so this is the one table where a repair kit is
            // reliably on offer rather than a lucky drop off a body.
            PickupKind kind = Random.Shared.NextSingle() switch
            {
                < 0.34f => PickupKind.Battery,
                < 0.50f => PickupKind.RepairKit,
                < 0.78f => PickupKind.Ammo,
                < 0.91f => PickupKind.ScrapMetal,
                < 0.97f => PickupKind.SpaceGunpowder,
                _ => PickupKind.CopperWire,
            };
            DropSalvage(RandomPointAroundPlayer(12f, 55f), kind);
        }
    }

    void IDescentField.SweepStragglers()
    {
        // Nothing hostile survives the end of a phase. A hunter that drifted off during a
        // herald fight and is still plinking at somebody trying to craft is not tension, it is
        // an unfinished chore — and worse, it makes the next wave bar start out of step.
        foreach (var e in Enemies)
            if (e.Alive) Debris.Burst(new Vector3(e.Position.X, 2f, e.Position.Y),
                Palette.EnemyFill, e.IsElite);
        Enemies.Clear();
        Soldiers.Clear();
        Squads.Clear();
    }

    void IDescentField.Announce(string line) => Announce?.Invoke(line);

    void IDescentField.Signal(Cue id, float param) => Emit(id, Eye.Position, param, personal: true);

    /// <summary>Where a wave's hunters fade in. Further out than the sandbox drip so a crowd
    /// arrives as a horizon full of shapes.</summary>
    private const float WaveSpawnMin = 85f;
    private const float WaveSpawnMax = 125f;

    /// <summary>And where a boss stands up. Inside the fog, so the silhouette is legible from
    /// the first frame of its arrival.</summary>
    private const float BossSpawnRange = 62f;

    /// <summary>
    /// Books one wave unit as dead. Called from the hunter and soldier death paths, and a no-op
    /// everywhere but a live wave — a hunter a boss summoned during a herald fight is not part
    /// of the crowd the bar is counting, and the director already refuses to count it.
    /// </summary>
    private void CountDescentKill() => Run?.CountKill();
}
