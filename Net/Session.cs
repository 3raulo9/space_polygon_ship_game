using Unrendered.Core;
using Unrendered.Entities;

namespace Unrendered.Net;

/// <summary>What kind of packet this is. One byte, first in every payload.</summary>
public enum Msg : byte
{
    /// <summary>Client → host, on connecting: which chassis they picked, and their name.</summary>
    Hello = 1,
    /// <summary>Host → client: your seat, the rules, and the state to open your craft in
    /// (which, for a rejoin, is the state it was held in).</summary>
    Welcome = 2,
    /// <summary>Client → host, every tick: what their hands are doing.</summary>
    Input = 3,
    /// <summary>Host → client, at <see cref="Session.SnapshotHz"/>: every player. Small, and
    /// its own packet so the field's size can never cost a craft its update.</summary>
    State = 4,
    /// <summary>Host → client: the host pressed LAUNCH, come in.</summary>
    Start = 5,
    /// <summary>Host → client, at <see cref="Session.SnapshotHz"/>: the nearby field. Allowed
    /// to be lost — a client that misses one keeps the enemies it had.</summary>
    Field = 6,
    /// <summary>Host → all: one line for the join/quit feed, already formatted.</summary>
    Notice = 7,

    // --- The 3D lobby room -------------------------------------------------------
    /// <summary>Client → host, reliable: the chassis this player chose at the pod, and that
    /// they are now ready. Reliable because a lost pick would leave a player un-launchable.</summary>
    Pick = 8,
    /// <summary>Client → host, reliable: the nickname they typed.</summary>
    Name = 9,
    /// <summary>Host → all, reliable: the match rules, whenever the host edits them at the
    /// console — so every client's lobby shows the same map, seats, friendly-fire and revives
    /// the host will launch with.</summary>
    Rules = 10,
    /// <summary>Host → clients, at <see cref="Session.SnapshotHz"/>: every avatar in the room —
    /// seat, chassis, ready, where they stand, and their name. Small and unreliable; the next
    /// one supersedes it.</summary>
    RoomState = 11,
    /// <summary>Client → host, every lobby tick, unreliable: this player's own avatar transform.
    /// The room is client-authoritative over each figure's position, so the host just relays it.</summary>
    RoomMove = 12,

    /// <summary>Host → clients, at <see cref="Session.SnapshotHz"/>: the one-shot sounds the sim
    /// raised near that client, each with a world position so it can be heard from the right
    /// place. Unreliable — a missed gunshot is gone by the time it could be resent.</summary>
    Sound = 13,

    /// <summary>Host → clients, at <see cref="Session.SnapshotHz"/>: the Crab-Core and Maw-Core
    /// near that client, so their fight is drawn and not just heard. Keep-last like the field.</summary>
    Bosses = 14,

    /// <summary>Host → clients, at <see cref="Session.SnapshotHz"/>: the particle bursts the sim
    /// threw near that client — deaths, hits, footfalls, laid smoke — so a client that runs no
    /// combat still sees the debris. Unreliable; a missed burst is a spray gone by the next tick.</summary>
    Effect = 15,

    /// <summary>Host → clients, at <see cref="Session.SnapshotHz"/>: the damaged buildings near
    /// that client — which cells of each cut tower still stand, what is coming down, what is
    /// gone. Keep-last like the field: the city's layout is identical everywhere and only the
    /// <em>damage</em> has to cross, so this is the packet that stops two players standing in
    /// front of the same tower and disagreeing about whether it exists.</summary>
    Structures = 16,

    /// <summary>Host → one client, reliable, whenever it changes: that seat's own pack. Reliable
    /// because an inventory is not a picture — a lost salvage pickup would simply never appear,
    /// and there is no next packet to correct it until something else changes.</summary>
    Inventory = 17,

    /// <summary>Client → host, reliable: one thing the player did in their inventory panel. The
    /// host replays it against the real pack and the echo above settles it.</summary>
    InvAct = 18,

    /// <summary>Host → clients, at <see cref="Session.SnapshotHz"/>: the transient combat light
    /// around that client — grapple cables, the VIRUS's stolen lance, the SPIDER's charged beam.
    /// Purely cosmetic and keep-nothing; a missed one costs a frame of light.</summary>
    Rigs = 19,

    /// <summary>Host → one client, reliable: there is no seat for you. A joiner has to be
    /// <em>told</em> it was refused — before this a full match simply ignored the hello, and
    /// the dial sat on CONNECTING for ever with nothing ever coming to say why.</summary>
    Full = 20,

    /// <summary>
    /// Host → all, reliable, whenever the roster changes: one seat's display name.
    ///
    /// The room's names only ever reached the match by being copied onto the world at LAUNCH,
    /// which means everyone who arrives <em>after</em> that — every rejoin, every late joiner —
    /// is a nameless craft on every screen but the host's, for the rest of the match. This is
    /// the packet that keeps the roster honest once the lobby is gone.
    /// </summary>
    SeatName = 21,

    /// <summary>
    /// Host → all, unreliable, about once a second: every seat's kills, deaths and round trip.
    ///
    /// Its own packet and its own slow clock because a scoreboard is not the world: nobody
    /// reads it sixty times a second, nothing about it has to be smooth, and a lost one is
    /// corrected a second later. The ping it carries is the same measurement the rewind buffer
    /// already makes from each client's acknowledgement, rather than a second timing channel
    /// that could only ever disagree with the first.
    /// </summary>
    Scores = 22,

    /// <summary>
    /// A mark on the world. Client → host it is a request ("I pointed here"); host → all it is
    /// the fact ("seat N pointed here"), which is why one id serves both directions — the
    /// payload differs by exactly the seat byte the host is the only one entitled to write.
    ///
    /// Reliable. It is a person trying to say something to nineteen other people, it happens
    /// perhaps once every few seconds, and there is no next packet to correct a lost one.
    /// </summary>
    Mark = 23,

    /// <summary>
    /// The room's argument about where to land. One id in both directions, like
    /// <see cref="Mark"/>, with a kind byte saying which: a client sends CAST ("I am for
    /// THALOS"), the host sends TALLY (the clock, and who is behind what).
    ///
    /// Reliable. A vote is twenty seconds long and a lost ballot is a person's say silently
    /// not counting — there is no next packet that would fix it in time, and a countdown
    /// nobody can see move is worse than one that arrives a frame late.
    /// </summary>
    Vote = 24,

    /// <summary>
    /// Host → all, reliable: one seat's whole hangar build — chassis, the four spent tracks,
    /// and every part's paint.
    ///
    /// <para>A pick used to be a single chassis byte, so a player's points and colours died on
    /// the machine that chose them: everyone else saw a default-painted craft with the
    /// machine's default build, and the host simulated that default. This is the packet that
    /// makes the bench mean something to anybody but its owner. Reliable and rare — one per
    /// player per time they change their mind — and re-sent to a late joiner with the roster,
    /// because a build is state, not an event.</para>
    /// </summary>
    Build = 25,

    /// <summary>
    /// The run: which phase a DESCENT is in, how far through its wave it is, and — the part
    /// that matters most — the seed and the leg of the crossing it is on.
    ///
    /// <para><b>The seed is the packet.</b> Everything about a planet that a client needs is
    /// derived from it and not streamed: the five bosses, their bodies and their names, and
    /// which three of the city's ten arcs are the way off it. Every machine already generates
    /// the identical building layout, so "gate" is a function of (layout, seed) and costs four
    /// bytes rather than a description of three structures. A client that hears this stands its
    /// own run up and arrives at the same three arches.</para>
    ///
    /// <para>Host → all, reliable. Rare — a handful per planet, on phase changes — because
    /// everything continuous about a wave is already implied by what the field packet carries.</para>
    /// </summary>
    Run = 26,

    /// <summary>
    /// A gate. One id in both directions with a kind byte, exactly like <see cref="Mark"/> and
    /// <see cref="Vote"/>: a client sends CLAIM ("let me open that one"), CAST ("I am for
    /// KIRENE") and FEED ("take the fragment in my slot 4"); the host sends TALLY and STATE.
    ///
    /// <para>Reliable, all of it. A gate is the one object in the game where a lost packet
    /// cannot be corrected by the next one: which arch the room is using, and how many of the
    /// five are in, are facts twenty people navigate by — and the whole exchange is perhaps
    /// fifteen packets across a planet.</para>
    /// </summary>
    Arch = 27,

    /// <summary>
    /// The rolled bosses near a client — the modular ones a DESCENT raises, as opposed to the
    /// two hand-built monsters <see cref="Bosses"/> has always carried.
    ///
    /// <para>Its own id because a rolled boss is a fundamentally different thing to describe: a
    /// Crab-Core is a singleton whose shape every machine knows, and one of these is a body
    /// built from a genome. The genome rides the seed in <see cref="Run"/> and this packet
    /// carries only what moves — where it is, what it is doing, and how much of it is left.</para>
    ///
    /// <para>Keep-last like the field, at the snapshot rate.</para>
    /// </summary>
    Rolled = 28,

    /// <summary>
    /// Somebody talking. One id in both directions with a kind byte, like <see cref="Mark"/>
    /// and <see cref="Arch"/>: a client sends SAY ("this is what I typed") and the host sends
    /// SAID ("seat N said this") to everyone, or TOLD to exactly one person — which is what a
    /// command's answer and a "you were given 100 bullets" are.
    ///
    /// <para>Reliable. A line of chat happens seconds apart at worst, it is a person trying to
    /// reach nineteen other people, and there is no next packet that would fix a lost one — the
    /// same reasoning as the mark, and for the same reason.</para>
    ///
    /// <para><b>The command is never sent.</b> A client types <c>{skip wave}</c> and what
    /// crosses the wire is the text; the host parses it, decides whether that seat is allowed,
    /// and acts. Nothing about a command's meaning is decided on the machine that typed it.</para>
    /// </summary>
    Chat = 29,
}

/// <summary>
/// The thing that turns a transport into a game: it decides who simulates, what goes out
/// each tick, and what to do with what comes back.
///
/// One class for both ends, because they are the same loop seen from two sides and splitting
/// them into Host and Client would duplicate the parts that matter (the tick counter, the
/// buffers, the ordering rules) to separate the parts that do not. <see cref="IsHost"/> is
/// the only branch.
///
/// It owns no window, no keyboard and no Steam handle — the transport does the talking and
/// the loop hands it input. That is what lets the whole thing be tested against
/// <see cref="LoopbackNet"/> with no Steam client running at all.
/// </summary>
public sealed class Session
{
    /// <summary>How often the host describes the world. Not 60: a snapshot per simulated
    /// tick is three times the traffic for something no player can perceive, and clients
    /// interpolate between them anyway.</summary>
    public const int SnapshotHz = 20;

    private const int TicksPerSnapshot = 60 / SnapshotHz;

    private readonly INetTransport _net;
    private readonly byte[] _out = new byte[Snapshot.MaxSize + 8];
    private readonly byte[] _field = new byte[Snapshot.MaxSize + 8];
    private readonly byte[] _sound = new byte[1200];
    private readonly byte[] _bosses = new byte[512];

    /// <summary>The rolled bosses' buffer. Bigger than the hand-built pair's, because this one
    /// is a list — a TWINNED Colossus ends its fight as two bodies, and the capture harness can
    /// stand several on a field.</summary>
    private readonly byte[] _rolled = new byte[512];
    private readonly byte[] _effect = new byte[1200];
    private readonly byte[] _structures = new byte[Snapshot.MaxStructureSize + 8];
    private readonly byte[] _rigs = new byte[Snapshot.MaxRigSize + 8];
    private readonly byte[] _inv = new byte[Core.Inventory.WireSize + 8];

    /// <summary>The digest of each seat's pack the last time it was mirrored to its owner, so
    /// the reliable echo only goes out when something actually changed. Host-side.</summary>
    private readonly Dictionary<int, int> _invSent = new();
    private uint _tick;
    private int _sinceSnapshot;

    /// <summary>Which snapshot this is, counted since the session opened. What the packets that
    /// do not need the full rate are tiered against — see <see cref="Broadcast"/>.</summary>
    private uint _snapshotSeq;

    // --- Input, in both directions -------------------------------------------------
    //
    // A tick of intent is twelve bytes and it is gone in a sixtieth of a second, so it goes
    // out unreliable — retransmitting one would land long after the tick it belonged to. That
    // reasoning is sound and it is not what was wrong. What was wrong is what it left behind:
    // a frame carries EDGES (the tick a grenade key went down and no other), and an edge that
    // is dropped is an action the player took that simply never happened. Worse, the host held
    // the last frame it received and re-ran it when nothing arrived — edges and all — so a
    // single lost packet could just as easily fire the grenade twice.
    //
    // The fix is the standard one and it is nearly free: every packet carries the last dozen
    // frames, not just the newest. One hundred and sixty bytes at sixty hertz, on the one link
    // in this game that has bandwidth to spare (a client sends to exactly one machine), and a
    // burst of eleven consecutive lost packets is now needed before a single tick of input is
    // actually gone.

    /// <summary>How many past frames ride on every input packet. Twelve ticks is a fifth of a
    /// second of history — far past any loss burst a playable link produces.</summary>
    private const int RedundantInputs = 12;

    /// <summary>Client-side: the frames this machine has sent, by tick, so each packet can
    /// repeat the recent ones behind the new one.</summary>
    private readonly InputFrame[] _sentInputs = new InputFrame[RedundantInputs];

    /// <summary>The buffer of one seat's input as it arrives, host-side.</summary>
    private sealed class SeatInput
    {
        /// <summary>The highest client tick ever taken in. Everything at or below it has
        /// already been queued, which is what makes the redundant copies free to ignore and
        /// what stops a packet that overtook a newer one from winding the seat backwards.</summary>
        public uint Newest;

        /// <summary>The highest client tick actually fed to the simulation. Echoed back to that
        /// client on every players packet, and the anchor its reconciliation measures against.</summary>
        public uint Consumed;

        /// <summary>Frames taken in but not yet stepped, oldest first. Shallow by construction
        /// — the client produces sixty a second and the host eats sixty a second — so this is a
        /// jitter absorber rather than a delay: it only holds anything when the wire has just
        /// delivered late, which is exactly when holding something is the point.</summary>
        public readonly Queue<(uint Tick, InputFrame Frame)> Pending = new();

        /// <summary>The last frame stepped, held so a tick with nothing to eat can repeat the
        /// held keys without repeating the edges.</summary>
        public InputFrame Last;
    }

    /// <summary>How deep the pending queue may get before the host eats two frames in a tick to
    /// catch up. Three ticks is fifty milliseconds of slack — enough to ride out ordinary
    /// jitter, short enough that a client whose packets arrive in clumps does not accumulate a
    /// permanent delay nobody asked for.</summary>
    private const int JitterSlack = 3;

    private readonly Dictionary<int, SeatInput> _seatInput = new();

    private SeatInput InputOf(int seat)
    {
        if (!_seatInput.TryGetValue(seat, out var si)) _seatInput[seat] = si = new SeatInput();
        return si;
    }

    /// <summary>
    /// Forgets everything buffered for a seat, and everything known about how far its client's
    /// clock had got. Called whenever a seat is bound to a peer for real — a fresh join or a
    /// rejoin — and it is not optional: the buffer discards any frame at or below the highest
    /// tick it has already seen, and a machine coming back is a NEW session whose tick counter
    /// starts at one again. Carried over, the old high-water mark would silently reject every
    /// frame that player sent for the next several minutes, and their craft would sit there
    /// with its controls apparently dead.
    /// </summary>
    private void ResetSeatInput(int seat) => _seatInput.Remove(seat);

    /// <summary>The world this session is driving. Set once the match starts.</summary>
    public World.World? World { get; private set; }

    /// <summary>True on the machine that owns the simulation.</summary>
    public bool IsHost { get; }

    /// <summary>Which seat this machine drives. Always 0 on the host.</summary>
    public int LocalSeat { get; private set; }

    /// <summary>
    /// True once the host has actually started the match. A client is seated the moment it
    /// says hello, which is not the same thing — the host may still be sitting in the lobby
    /// deciding the rules, and a client that walked in on being seated would find itself
    /// standing in a world nobody is stepping yet.
    /// </summary>
    public bool MatchStarted { get; private set; }

    /// <summary>Host-side: LAUNCH. Tells everyone to come in, reliably — a client that
    /// missed this would sit in the lobby while the match ran without them.</summary>
    public void StartMatch()
    {
        MatchStarted = true;
        Span<byte> p = stackalloc byte[1];
        p[0] = (byte)Msg.Start;
        _net.Broadcast(p, reliable: true);
    }

    /// <summary>Sends one seat its Welcome: seat number, the match rules, and the craft's
    /// current state — which for a rejoin is the state the host held it in.</summary>
    private void SendWelcome(int peer, int seat)
    {
        PlayerTank p = World!.Players[seat];
        Span<byte> w = stackalloc byte[2 + MatchSettings.Size + 12];
        int at = 0;
        w[at++] = (byte)Msg.Welcome;
        w[at++] = (byte)seat;
        World.Match.Write(w.Slice(at, MatchSettings.Size)); at += MatchSettings.Size;
        WriteShort(w, ref at, p.Position.X * 64f);
        WriteShort(w, ref at, p.Position.Y * 64f);
        WriteShort(w, ref at, p.Height * 64f);
        WriteShort(w, ref at, MathF.IEEERemainder(p.Heading, MathF.Tau) * (65536f / MathF.Tau));
        w[at++] = (byte)Math.Clamp(p.Lives, 0, 255);
        WriteShort(w, ref at, p.Shield * 4f);
        w[at++] = (byte)Math.Clamp(p.Ammo, 0, 255);
        _net.Send(peer, w.Slice(0, at), reliable: true);
    }

    private static void WriteShort(Span<byte> dst, ref int at, float v)
    {
        BitConverter.TryWriteBytes(dst.Slice(at, 2),
            (short)Math.Clamp(MathF.Round(v), short.MinValue, short.MaxValue));
        at += 2;
    }

    /// <summary>Tells one peer the match has begun — for a late or rejoining player, who has
    /// missed the LAUNCH broadcast that everyone already in the room got.</summary>
    private void SendStartTo(int peer)
    {
        Span<byte> p = stackalloc byte[1];
        p[0] = (byte)Msg.Start;
        _net.Send(peer, p, reliable: true);
    }

    /// <summary>
    /// Network without simulation: drains the socket and answers what is on it, and nothing
    /// else. This is what the lobby runs — the handshake that seats a joiner has to happen
    /// while both machines are still on the lobby screen, long before either has a world
    /// worth describing.
    /// </summary>
    public void PumpLobby()
    {
        _net.Pump();
        while (_net.TryReceive(out int from, out byte[] payload))
            Handle(from, payload);
        ReapDeparted();
    }

    // --- The 3D lobby room --------------------------------------------------------

    private const float RoomPos = 64f;
    private const float RoomAng = 65536f / MathF.Tau;

    /// <summary>
    /// One frame of room networking, called from the loop while the walkable lobby is up in
    /// place of a world's <see cref="Pump"/>. Drains the socket (which seats hellos and applies
    /// picks, names and moves), then — host — broadcasts the room's state at the snapshot rate
    /// and pushes any rules edit, or — client — sends its own transform and anything it just
    /// chose.
    /// </summary>
    public void LobbyTick(World.LobbyRoom room)
    {
        Room = room;
        PumpLobby();

        if (IsHost)
        {
            if (room.PickDirty && LocalSeat >= 0)
                ApplyPick(LocalSeat, room.MyBuild.Clone());
            if (room.NameDirty) { LocalName = room.MyName; Rename(LocalSeat, room.MyName); }
            if (room.Chart.CastDirty) room.Chart.Cast(LocalSeat, room.Chart.Cursor);

            // The clock, before the rules go out: a countdown that expires this frame settles
            // the destination, and that new destination should ride the same rules packet
            // rather than waiting a tick behind the result everyone has already seen.
            bool settled = room.TickVote((float)Core.Config.FixedDt) is not null;

            if (room.RulesDirty) BroadcastRules(room.Match);
            // Only on the edges that a client cannot work out for itself: the vote opening,
            // somebody's ballot moving, and the result. The countdown itself is NOT streamed —
            // every machine runs the same twenty seconds off the opening packet, so a vote is
            // three or four packets rather than four hundred reliable ones.
            if (room.VoteDirty || room.Chart.CastDirty || settled) BroadcastVote(room);
            room.ClearDirty();

            if (++_sinceRoom >= TicksPerSnapshot)
            {
                _sinceRoom = 0;
                BroadcastRoomState(room);
            }
        }
        else if (LocalSeat < 0)
        {
            // Still waiting on a seat: keep asking. Everything below needs one.
            RetryHello();
        }
        else
        {
            if (room.PickDirty)
            {
                Loadout chosen = room.MyBuild.Clone();
                _buildOfSeat[LocalSeat] = chosen;
                SendPick(chosen);
                // Install the pick on this client's OWN craft immediately. The host is
                // authoritative on this seat for everyone else and its snapshot names the
                // chassis, but a client never overwrites its own seat from a snapshot — so
                // without this the player keeps driving the placeholder tank they were seated
                // as while the host simulates the chassis they actually picked, and the two
                // move so differently that the craft is never where anyone thinks it is.
                if (World is { } w && LocalSeat < w.Players.Count
                    && !w.Players[LocalSeat].Build.SameAs(chosen))
                    w.ReplacePlayer(LocalSeat, chosen);
            }
            if (room.NameDirty) SendName(room.MyName);
            // A client runs the same twenty seconds the host does, off the packet that opened
            // the vote, and simply stops when it reaches zero — it never decides anything. The
            // result arrives as a rules change, which is the only account of it that counts.
            room.Chart.Tick((float)Core.Config.FixedDt);
            if (room.Chart.CastDirty) SendVoteCast(room.Chart.Cursor);
            room.ClearDirty();
            // At the same rate the host describes the room back, rather than sixty times a
            // second. The room is people strolling about a floor; twenty transforms a second
            // is already more than the renderer can show, and the old per-tick send was three
            // times the traffic of the match itself for a screen nobody is playing on.
            if (++_sinceRoom >= TicksPerSnapshot)
            {
                _sinceRoom = 0;
                SendRoomMove(room.Position, room.Heading, room.Pitch, room.Height);
            }
        }
    }

    /// <summary>
    /// Host-side: records a seat's chosen chassis, marks it ready, and rebuilds that seat's
    /// craft in the world it is holding so the eventual match opens with the right one.
    ///
    /// Deliberately not gated on the lobby: a pick that lands mid-match is honoured too, and
    /// the next players packet carries the new chassis to every other machine. That is what
    /// lets somebody who joined a match already in progress choose a craft at all, rather than
    /// being stuck for ever in the placeholder tank they were seated as.
    /// </summary>
    /// <param name="build">The whole bench: chassis, points and paint. Every seat's arrives
    /// this way now — the host's own from its pod, everyone else's off the wire — so there is
    /// one path that installs a craft and it is the one that carries all of it.</param>
    private void ApplyPick(int seat, Loadout build)
    {
        if (seat < 0) return;
        PlayerClass chassis = build.Class;
        if (!Enum.IsDefined(chassis)) return;   // a malformed byte is not a chassis

        _pickOfSeat[seat] = (chassis, true);
        _buildOfSeat[seat] = build;
        if (World is { } known) known.SeatBuilds[seat] = build;
        Room?.ApplyPick(seat, chassis, ready: true);

        // The host is the only one entitled to say what anybody is flying, so it passes the
        // build on to the rest of the room. Everyone in it — not just the pod's owner — has to
        // be able to draw that craft, in the lobby and in the match.
        if (IsHost) BroadcastBuild(seat, build);

        if (World is not { } w || seat >= w.Players.Count) return;
        // Rebuilt whenever anything about the craft changed, not just its chassis: a player
        // who only repainted, or only moved a point off SPEED onto HULL, has changed the craft
        // and the old test (class differs?) would have kept the old one for the whole match.
        if (w.Players[seat].Build.SameAs(build)) return;
        w.ReplacePlayer(seat, build);
    }

    /// <summary>Host → all: this seat is flying this build. Also the packet a late joiner is
    /// caught up with, so it arrives already knowing what everyone looks like.</summary>
    private void BroadcastBuild(int seat, Loadout build)
    {
        if (!IsHost) return;
        Span<byte> p = stackalloc byte[2 + 64];
        p[0] = (byte)Msg.Build;
        p[1] = (byte)seat;
        build.Write(p.Slice(2));
        _net.Broadcast(p.Slice(0, 2 + Loadout.Bytes), reliable: true);
    }

    private void SendPick(Loadout build)
    {
        Span<byte> p = stackalloc byte[2 + 64];
        p[0] = (byte)Msg.Pick;
        p[1] = 1;   // ready
        build.Write(p.Slice(2));
        _net.Send(0, p.Slice(0, 2 + Loadout.Bytes), reliable: true);
    }

    private void SendName(string name)
    {
        byte[] text = Encode(name);
        Span<byte> p = stackalloc byte[2 + 32];
        p[0] = (byte)Msg.Name;
        p[1] = (byte)text.Length;
        text.CopyTo(p.Slice(2, text.Length));
        _net.Send(0, p.Slice(0, 2 + text.Length), reliable: true);
    }

    private void SendRoomMove(System.Numerics.Vector2 pos, float heading, float pitch, float height)
    {
        Span<byte> p = stackalloc byte[1 + 10];
        int at = 0;
        p[at++] = (byte)Msg.RoomMove;
        WriteShort(p, ref at, pos.X * RoomPos);
        WriteShort(p, ref at, pos.Y * RoomPos);
        WriteShort(p, ref at, heading * RoomAng);
        WriteShort(p, ref at, pitch * RoomAng);
        WriteShort(p, ref at, height * RoomPos);
        _net.Send(0, p.Slice(0, at), reliable: false);
    }

    // --- The run and its gates ------------------------------------------------------------

    private const byte ArchClaimKind = 0;   // client → host: let me open that one / host → all: it is claimed
    private const byte ArchCastKind = 1;    // client → host: my ballot
    private const byte ArchTallyKind = 2;   // host → all: the clock and every ballot
    private const byte ArchFeedKind = 3;    // client → host: take the fragment in my slot N
    private const byte ArchStateKind = 4;   // host → all: the whole gate

    /// <summary>
    /// Host → all: the run's seed, its leg of the crossing, and where it has got to.
    ///
    /// <para>Sent whenever any of it changes and to every late joiner, which is a handful of
    /// packets per planet. The seed is the expensive-looking part and is the reason the rest of
    /// it is cheap: a client hearing a seed it does not already have builds its own director and
    /// its own gate list off it, so twenty-five bosses and fifteen arches cost four bytes.</para>
    /// </summary>
    public void BroadcastRun()
    {
        if (!IsHost || World is not { Run: { } run }) return;

        Span<byte> p = stackalloc byte[16];
        int at = 0;
        p[at++] = (byte)Msg.Run;
        BitConverter.TryWriteBytes(p.Slice(at, 4), run.Seed); at += 4;
        p[at++] = (byte)Math.Clamp(run.Hop, 0, 255);
        p[at++] = (byte)run.Destination;
        p[at++] = (byte)World.VisitedMask;
        p[at++] = (byte)run.Phase;
        p[at++] = (byte)Math.Clamp(run.Wave, 0, 255);
        BitConverter.TryWriteBytes(p.Slice(at, 2), (ushort)Math.Clamp(run.Killed, 0, 65535)); at += 2;
        BitConverter.TryWriteBytes(p.Slice(at, 2), (ushort)Math.Clamp(run.WaveTotal, 0, 65535)); at += 2;
        // Tenths of a second. The salvage clock is drawn to the second and nothing else reads it.
        p[at++] = (byte)Math.Clamp((int)MathF.Round(run.Clock * 4f), 0, 255);
        p[at++] = (byte)(World.GatesLit ? 1 : 0);

        _net.Broadcast(p.Slice(0, at), reliable: true);
        _runFingerprint = RunFingerprint();
    }

    /// <summary>A cheap digest of everything <see cref="BroadcastRun"/> carries, so the host can
    /// send it only when it actually changed rather than every tick. The same approach the
    /// inventory mirror already takes.</summary>
    private int RunFingerprint()
    {
        if (World is not { Run: { } run }) return 0;
        int h = 17;
        h = h * 31 + run.Seed;
        h = h * 31 + run.Hop;
        h = h * 31 + (int)run.Phase;
        h = h * 31 + run.Wave;
        h = h * 31 + run.Killed;
        h = h * 31 + run.WaveTotal;
        h = h * 31 + (int)(run.Clock * 4f);
        h = h * 31 + (World.GatesLit ? 1 : 0);
        h = h * 31 + World.VisitedMask;
        return h;
    }

    private int _runFingerprint;
    private int _gateFingerprint;

    /// <summary>
    /// Host → all: the claimed gate, whole. Which arch, where it points, what is in each of the
    /// five recesses, what is on the rail, how far the charge has run, who is through, and how
    /// long the stragglers have left.
    ///
    /// <para>Sent whole rather than as a delta for the same reason the destination tally is: it
    /// is about twenty bytes, it changes perhaps thirty times across a planet, and a gate that
    /// could disagree with itself after one lost packet would strand a room.</para>
    /// </summary>
    public void BroadcastGate()
    {
        if (!IsHost || World?.ClaimedGate is not { } g) return;

        Span<byte> p = stackalloc byte[24];
        int at = 0;
        p[at++] = (byte)Msg.Arch;
        p[at++] = ArchStateKind;
        BitConverter.TryWriteBytes(p.Slice(at, 2), (ushort)g.StructureIndex); at += 2;
        p[at++] = (byte)g.State;
        // 255 is "not decided yet", which is a real state a gate sits in for a whole vote.
        p[at++] = g.Destination is { } d ? (byte)d : (byte)255;
        for (int i = 0; i < Unrendered.World.Arch.SocketCount; i++) p[at++] = (byte)g.SocketAt(i);
        p[at++] = (byte)g.Carrying;
        p[at++] = (byte)(g.CarryingTo < 0 ? 255 : g.CarryingTo);
        p[at++] = (byte)Math.Clamp((int)(g.CarryT * 255f), 0, 255);
        p[at++] = (byte)Math.Clamp((int)(g.Charge * 255f), 0, 255);
        BitConverter.TryWriteBytes(p.Slice(at, 4), g.Entered); at += 4;
        p[at++] = (byte)Math.Clamp((int)MathF.Round(g.Grace), 0, 255);

        _net.Broadcast(p.Slice(0, at), reliable: true);
        _gateFingerprint = GateFingerprint();
    }

    private int GateFingerprint()
    {
        if (World?.ClaimedGate is not { } g) return 0;
        int h = 17;
        h = h * 31 + g.StructureIndex;
        h = h * 31 + (int)g.State;
        h = h * 31 + (g.Destination is { } d ? (int)d + 1 : 0);
        for (int i = 0; i < Unrendered.World.Arch.SocketCount; i++) h = h * 31 + (int)g.SocketAt(i);
        h = h * 31 + (int)g.Carrying;
        h = h * 31 + g.CarryingTo;
        // The rail's progress and the charge are quantised into the digest at the same
        // resolution the packet carries them, so a haul streams smoothly without a packet per
        // frame: it changes the digest about twenty times over its two seconds.
        h = h * 31 + (int)(g.CarryT * 20f);
        h = h * 31 + (int)(g.Charge * 20f);
        h = h * 31 + g.Entered;
        h = h * 31 + (int)g.Grace;
        return h;
    }

    /// <summary>Host-side, once a tick: puts the run and the gate on the wire if either
    /// actually moved. Cheap enough to ask every tick and quiet the rest of the time.</summary>
    private void PushRunState()
    {
        if (!IsHost || World is not { IsDescent: true }) return;
        if (RunFingerprint() != _runFingerprint) BroadcastRun();
        if (GateFingerprint() != _gateFingerprint) BroadcastGate();
    }

    /// <summary>Client → host: let me open the arch on structure <paramref name="index"/>.</summary>
    public void RequestArchClaim(int index)
    {
        Span<byte> p = stackalloc byte[4];
        p[0] = (byte)Msg.Arch;
        p[1] = ArchClaimKind;
        BitConverter.TryWriteBytes(p.Slice(2, 2), (ushort)index);
        _net.Send(0, p, reliable: true);
    }

    /// <summary>Host → all: that one is claimed and the rest are dark.</summary>
    public void BroadcastArchClaim(int index)
    {
        if (!IsHost) return;
        Span<byte> p = stackalloc byte[4];
        p[0] = (byte)Msg.Arch;
        p[1] = ArchClaimKind;
        BitConverter.TryWriteBytes(p.Slice(2, 2), (ushort)index);
        _net.Broadcast(p, reliable: true);
    }

    /// <summary>Client → host: take the fragment in my grid slot <paramref name="slot"/>.</summary>
    public void RequestArchFeed(int slot)
    {
        Span<byte> p = stackalloc byte[3];
        p[0] = (byte)Msg.Arch;
        p[1] = ArchFeedKind;
        p[2] = (byte)Math.Clamp(slot, 0, 255);
        _net.Send(0, p, reliable: true);
    }

    /// <summary>Client → host: my ballot at the arch's chart.</summary>
    public void SendArchVote(UI.StarMap chart)
    {
        if (IsHost) return;
        Span<byte> p = stackalloc byte[3];
        p[0] = (byte)Msg.Arch;
        p[1] = ArchCastKind;
        p[2] = (byte)chart.Cursor;
        _net.Send(0, p, reliable: true);
    }

    /// <summary>Host → all: the arch's clock and every ballot in it. Same shape as the lobby
    /// table's tally, because it is the same vote held somewhere else.</summary>
    public void BroadcastArchVote(UI.StarMap chart)
    {
        if (!IsHost) return;
        Span<byte> p = stackalloc byte[5 + MatchSettings.MaxSeats * 2];
        int at = 0;
        p[at++] = (byte)Msg.Arch;
        p[at++] = ArchTallyKind;
        p[at++] = (byte)(chart.VoteOpen ? 1 : 0);
        p[at++] = (byte)Math.Clamp((int)MathF.Round(chart.SecondsLeft * 10f), 0, 255);

        int countAt = at++;
        int written = 0;
        foreach (var (seat, choice) in chart.Votes)
        {
            if (written >= MatchSettings.MaxSeats) break;
            if ((uint)seat >= MatchSettings.MaxSeats) continue;
            p[at++] = (byte)seat;
            p[at++] = (byte)choice;
            written++;
        }
        p[countAt] = (byte)written;

        _net.Broadcast(p.Slice(0, at), reliable: true);
    }

    /// <summary>The arch panel this machine has open, so a TALLY off the wire lands in the chart
    /// the player is actually looking at. Set by the game loop when it opens one.</summary>
    public UI.ArchPanel? ArchPanel { get; set; }

    // --- Chat -------------------------------------------------------------------------------

    private const byte ChatSay = 0;    // client → host: this is what I typed
    private const byte ChatSaid = 1;   // host → all: seat N said this
    private const byte ChatTold = 2;   // host → one: something only you should read

    /// <summary>
    /// Says something. On the host this is settled here and now; on a client it is a request,
    /// and the host's echo is what actually reaches the room — including this player's own
    /// screen, so a message everybody else lost is a message you can see you did not send.
    /// </summary>
    public void Say(string text)
    {
        if (IsHost) { Said(LocalSeat, text); return; }

        Span<byte> p = stackalloc byte[3 + 256];
        byte[] body = EncodeLine(text);
        p[0] = (byte)Msg.Chat;
        p[1] = ChatSay;
        p[2] = (byte)body.Length;
        body.CopyTo(p.Slice(3, body.Length));
        _net.Send(0, p.Slice(0, 3 + body.Length), reliable: true);
    }

    /// <summary>Host-side: puts a line in front of the whole room, this machine included.</summary>
    private void Said(int seat, string text)
    {
        string line = Core.ChatCommand.Spoken(NameOfSeat(seat), text);
        Notices.Push(line, NoticeFeed.Kind.Chat);

        Span<byte> p = stackalloc byte[3 + 256];
        byte[] body = EncodeLine(line);
        p[0] = (byte)Msg.Chat;
        p[1] = ChatSaid;
        p[2] = (byte)body.Length;
        body.CopyTo(p.Slice(3, body.Length));
        _net.Broadcast(p.Slice(0, 3 + body.Length), reliable: true);
    }

    /// <summary>
    /// Host-side: something only one seat should read — the answer to a command they typed, or
    /// the news that somebody just filled their pack.
    ///
    /// <para>Sent to that seat's peer alone rather than broadcast with a "for you" byte, so a
    /// refusal is genuinely private: the point of telling somebody their command was refused
    /// rather than announcing it is that a typo should not become a callout.</para>
    /// </summary>
    public void Tell(int seat, string text)
    {
        if (seat == LocalSeat) { Notices.Push(text, NoticeFeed.Kind.Console); return; }
        if (!IsHost) return;

        foreach (var (peer, s) in _seatOfPeer)
        {
            if (s != seat) continue;
            Span<byte> p = stackalloc byte[3 + 256];
            byte[] body = EncodeLine(text);
            p[0] = (byte)Msg.Chat;
            p[1] = ChatTold;
            p[2] = (byte)body.Length;
            body.CopyTo(p.Slice(3, body.Length));
            _net.Send(peer, p.Slice(0, 3 + body.Length), reliable: true);
            return;
        }
    }

    /// <summary>
    /// The whole of what a typed line does, once the host has it. One method for a line typed
    /// here and a line that arrived off the wire, deliberately: the host must treat its own
    /// commands exactly as it treats everybody else's, or the two paths drift and the rules
    /// only really hold for other people.
    /// </summary>
    public void HandleTyped(string text, int seat)
    {
        var cmd = Core.ChatCommand.Parse(text);

        switch (cmd.Verb)
        {
            case Core.ChatVerb.Say:
                Said(seat, cmd.Text);
                break;

            case Core.ChatVerb.Unknown:
                // Never said out loud. A mistyped command becoming a message the whole room
                // reads is the single most embarrassing thing an in-game console can do.
                Tell(seat, cmd.Complaint ?? "THAT IS NOT A COMMAND");
                break;

            default:
                // Commands reach into the run's phase and into other people's packs, so in a
                // room they belong to whoever is hosting it. Refused privately — the person who
                // tried is told and nobody else is, because a typo should not become a callout.
                //
                // A solo player passes this trivially: they are the host of a room of one,
                // which is exactly what makes the same code path serve as the single-player
                // console rather than needing a second one.
                if (cmd.NeedsAuthority && seat != LocalSeat)
                {
                    Tell(seat, "THAT IS THE HOST'S TO GIVE");
                    break;
                }
                if (World is null) { Tell(seat, "NOT IN A RUN"); break; }
                Tell(seat, World.RunCommand(cmd, seat));
                break;
        }
    }

    /// <summary>Every nickname in the room, for the chat's completion. Seat order, which is
    /// join order, so the list a player walks with Up is stable across a session.</summary>
    public IReadOnlyList<string> Nicknames
    {
        get
        {
            var names = new List<string>();
            for (int seat = 0; seat < MatchSettings.MaxSeats; seat++)
            {
                string n = NameOfSeat(seat);
                if (n.Length > 0) names.Add(n);
            }
            return names;
        }
    }

    private string NameOfSeat(int seat)
        => _nameOfSeat.TryGetValue(seat, out var n) ? n : "";

    // --- The destination vote -----------------------------------------------------------

    private const byte VoteCast = 0;
    private const byte VoteTally = 1;

    /// <summary>Client → host: this player is for the world under their cursor.</summary>
    private void SendVoteCast(PlanetId choice)
    {
        Span<byte> p = stackalloc byte[3];
        p[0] = (byte)Msg.Vote;
        p[1] = VoteCast;
        p[2] = (byte)choice;
        _net.Send(0, p, reliable: true);
    }

    /// <summary>
    /// Host → all: whether a vote is running, how long is left of it, and every ballot cast so
    /// far. Sent whole rather than as a delta — twenty seats is twenty bytes, and a tally that
    /// could disagree with itself after one lost packet would be worse than useless.
    /// </summary>
    private void BroadcastVote(World.LobbyRoom room)
    {
        Span<byte> p = stackalloc byte[4 + MatchSettings.MaxSeats * 2];
        int at = 0;
        p[at++] = (byte)Msg.Vote;
        p[at++] = VoteTally;
        p[at++] = (byte)(room.Chart.VoteOpen ? 1 : 0);
        // Tenths of a second, which is all a countdown on a 320x240 panel can show anyway.
        p[at++] = (byte)Math.Clamp((int)MathF.Round(room.Chart.SecondsLeft * 10f), 0, 255);

        int countAt = at++;
        int written = 0;
        foreach (var (seat, choice) in room.Chart.Votes)
        {
            if (written >= MatchSettings.MaxSeats) break;
            if ((uint)seat >= MatchSettings.MaxSeats) continue;
            p[at++] = (byte)seat;
            p[at++] = (byte)choice;
            written++;
        }
        p[countAt] = (byte)written;

        _net.Broadcast(p.Slice(0, at), reliable: true);
    }

    public void BroadcastRules(MatchSettings m)
    {
        Span<byte> p = stackalloc byte[1 + MatchSettings.Size];
        p[0] = (byte)Msg.Rules;
        m.Write(p.Slice(1, MatchSettings.Size));
        _net.Broadcast(p, reliable: true);
    }

    /// <summary>Host-side: writes every avatar — seat, chassis (255 = none yet), ready, where
    /// they stand, and their name — into one unreliable packet and sends it to every client.</summary>
    private void BroadcastRoomState(World.LobbyRoom room)
    {
        Span<byte> buf = stackalloc byte[1200];
        int at = 0;
        buf[at++] = (byte)Msg.RoomState;
        int countAt = at++;
        int written = 0;

        foreach (var a in room.Avatars.Values)
        {
            if (at + 14 + 32 > buf.Length) break;
            buf[at++] = (byte)a.Seat;
            buf[at++] = a.Chassis.HasValue ? (byte)a.Chassis.Value : (byte)255;
            buf[at++] = (byte)(a.Ready ? 1 : 0);
            WriteShort(buf, ref at, a.Position.X * RoomPos);
            WriteShort(buf, ref at, a.Position.Y * RoomPos);
            WriteShort(buf, ref at, a.Heading * RoomAng);
            WriteShort(buf, ref at, a.Pitch * RoomAng);
            WriteShort(buf, ref at, a.Height * RoomPos);
            byte[] name = Encode(a.Name);
            buf[at++] = (byte)name.Length;
            name.CopyTo(buf.Slice(at, name.Length)); at += name.Length;
            written++;
        }
        buf[countAt] = (byte)written;
        _net.Broadcast(buf.Slice(0, at), reliable: false);
    }

    /// <summary>The most recent tick a client has heard about, so a packet that overtook a
    /// newer one on the wire is dropped rather than snapping the world backwards.</summary>
    public uint LastAppliedTick { get; private set; }

    /// <summary>Peers the host has seated, keyed by transport peer id. Host-side only, and
    /// rebuilt on a reconnect since the peer id is not stable across one.</summary>
    private readonly Dictionary<int, int> _seatOfPeer = new();

    /// <summary>Seat kept for a Steam account across a disconnect, so a rejoin from the same
    /// person lands back in the seat it left — the basis of "restore everything". Host-side.</summary>
    private readonly Dictionary<long, int> _seatOfIdentity = new();

    /// <summary>Each seat's display name, for the feed. Host-side.</summary>
    private readonly Dictionary<int, string> _nameOfSeat = new();

    /// <summary>The join/quit lines, filled on the host as people come and go and mirrored to
    /// every client. Read by the HUD; drawn bottom-right under the instruments.</summary>
    public NoticeFeed Notices { get; } = new();

    /// <summary>This machine's own display name, sent in HELLO. Set by the loop from Steam.</summary>
    public string LocalName { get; set; } = "PLAYER";

    /// <summary>The lobby room this session is driving, while one is up. Set by the loop when
    /// the room scene is active and cleared when the match starts. Host and client both read
    /// and write it through <see cref="LobbyTick"/>.</summary>
    public World.LobbyRoom? Room { get; set; }

    /// <summary>Throttles the host's RoomState to <see cref="SnapshotHz"/> the same way the
    /// match's snapshots are throttled — the room does not need sixty transforms a second any
    /// more than the world does.</summary>
    private int _sinceRoom;

    /// <summary>Each seat's chosen chassis and ready flag in the room. Host-side, so the host
    /// can build the match with the right craft in each seat and gate LAUNCH on all-ready.</summary>
    private readonly Dictionary<int, (PlayerClass Chassis, bool Ready)> _pickOfSeat = new();

    /// <summary>
    /// Each seat's whole hangar build, on <em>every</em> machine — the host learns them from
    /// picks and relays them, and a client keeps the copy it was told so it can rebuild that
    /// craft in the right colours whenever the snapshot names a new chassis for the seat.
    ///
    /// <para>Without this a client that rebuilt a seat from a snapshot's chassis byte threw
    /// the paint away the moment the craft changed, which is exactly when it is most obvious.</para>
    /// </summary>
    private readonly Dictionary<int, Loadout> _buildOfSeat = new();

    /// <summary>The build this machine has been told a seat is flying, or null.</summary>
    public Loadout? BuildOf(int seat) => _buildOfSeat.GetValueOrDefault(seat);

    public Session(INetTransport net, bool host)
    {
        _net = net;
        IsHost = host;
        LocalSeat = host ? 0 : -1;
    }

    /// <summary>Host-side: begins the match on a world it already built.</summary>
    public void HostMatch(World.World world)
    {
        World = world;
        World.LocalIndex = 0;
        World.CollectSoundCues = true;   // the host gathers cues to broadcast; solo runs do not
        LocalSeat = 0;
        Rename(0, LocalName);             // the host's own name, for the roster and the tags
        // The combat feed. The sim raises a line; the feed is already mirrored to every
        // client, so a kill announced on the host is a kill everybody reads — no new packet
        // and no second formatting of the same sentence.
        World.Announce = Announce;
        // And the private channel: a command's answer, or the news that somebody just filled
        // your pack, goes to one seat rather than to the room.
        World.Tell = Tell;
    }

    /// <summary>
    /// Swaps the world under a running session — the crossing.
    ///
    /// <para>A planet is a whole world object: its own city, its own director, its own seats.
    /// Going through an arch means throwing one away and building the next, and the session
    /// has to be handed the new one without any of the rest of it — the peers, the seat map,
    /// the names, the rewind buffer — being disturbed, because the room is exactly the same
    /// twenty people it was a second ago. That is the only difference between this and
    /// <see cref="HostMatch"/>, which also seats and renames.</para>
    ///
    /// <para>The seat roster and every name are re-applied, because the new world's roster was
    /// built from scratch and knows nobody.</para>
    /// </summary>
    public void ReplaceWorld(World.World world)
    {
        World = world;
        World.LocalIndex = Math.Max(0, LocalSeat);
        World.CollectSoundCues = IsHost;
        World.Announce = Announce;
        // And the private channel: a command's answer, or the news that somebody just filled
        // your pack, goes to one seat rather than to the room.
        World.Tell = Tell;
        foreach (var (seat, name) in _nameOfSeat) World.SeatNames[seat] = name;

        // The fingerprints describe a planet that no longer exists, so clearing them is what
        // forces the first push on the new one to actually go out rather than being mistaken
        // for "nothing changed".
        _runFingerprint = 0;
        _gateFingerprint = 0;
    }

    /// <summary>Every seat's name the host knows, for the loop to copy onto the world at launch
    /// so team-mates carry a floating tag into the match.</summary>
    public IReadOnlyDictionary<int, string> SeatNames => _nameOfSeat;

    /// <summary>
    /// Client-side: adopts the world the host has described. The seat is not known until the
    /// Welcome arrives, so the world is held with a placeholder roster until then.
    /// </summary>
    public void JoinMatch(World.World world) => World = world;

    /// <summary>
    /// Client-side: announces the chassis this player picked and their name.
    ///
    /// This used to be a single send, made the instant <c>ConnectP2P</c> handed back a
    /// connection handle — which is long before that connection exists. A handle is not a
    /// link: Steam is still punching through to the other machine, and a message pushed into
    /// a socket in that state has nowhere to go. When it went nowhere there was nothing to
    /// notice it and nothing to try again, so the joiner sat on CONNECTING for ever and the
    /// only cure was to back out and re-dial. So the hello is now <em>repeated</em> until the
    /// host answers with a seat — see <see cref="RetryHello"/>, which the lobby tick drives.
    /// </summary>
    public void SendHello(PlayerClass chassis)
    {
        _helloChassis = chassis;
        _helloWanted = true;
        _sinceHello = 0;
        _helloTicks = 0;
        PushHello();
    }

    private void PushHello()
    {
        byte[] name = Encode(LocalName);
        Span<byte> p = stackalloc byte[3 + 32];
        p[0] = (byte)Msg.Hello;
        p[1] = (byte)_helloChassis;
        p[2] = (byte)name.Length;
        name.CopyTo(p.Slice(3, name.Length));
        _net.Send(0, p.Slice(0, 3 + name.Length), reliable: true);
    }

    private PlayerClass _helloChassis;
    private bool _helloWanted;
    private int _sinceHello;
    private int _helloTicks;

    /// <summary>How often an unanswered hello is repeated, in lobby ticks (60 = once a
    /// second). Slow enough to be nothing on the wire, fast enough that a joiner whose first
    /// attempt landed on a half-open socket is seated well inside the time it takes them to
    /// wonder whether it worked.</summary>
    private const int HelloEvery = 45;

    /// <summary>How long a dial is given before it is called a failure, in lobby ticks. Ten
    /// seconds is comfortably past Steam's own relay handshake and well short of the point a
    /// person decides the game is broken.</summary>
    private const int HelloPatience = 60 * 10;

    /// <summary>Why the join failed, for the room to show instead of the CONNECTING banner:
    /// the host refused us (no seat), or nobody ever answered. Null while all is well.</summary>
    public string? Rejected { get; private set; }

    /// <summary>Client-side: repeats an unanswered hello, and gives up eventually. Called from
    /// the lobby tick, which is the only place a client is ever waiting to be seated.</summary>
    private void RetryHello()
    {
        if (!_helloWanted || LocalSeat >= 0) { _helloWanted = false; return; }
        if (++_helloTicks > HelloPatience)
        {
            _helloWanted = false;
            Rejected ??= "NO ANSWER FROM THAT CODE";
            return;
        }
        if (++_sinceHello < HelloEvery) return;
        _sinceHello = 0;
        PushHello();
    }

    /// <summary>A display name as at most 31 bytes of UTF-8 — long enough for any real Steam
    /// name, short enough that a hostile one cannot bloat a packet.</summary>
    private static byte[] Encode(string name)
    {
        byte[] all = System.Text.Encoding.UTF8.GetBytes(name);
        return all.Length <= 31 ? all : all[..31];
    }

    private static string DecodeName(ReadOnlySpan<byte> src)
        => System.Text.Encoding.UTF8.GetString(src).Trim();

    /// <summary>
    /// A line of chat on the wire. Its own encoder because <see cref="Encode"/> is the name
    /// encoder and clips at thirty-one bytes — which is right for a nickname and would cut a
    /// sentence off mid-word.
    ///
    /// <para>The ceiling is a byte, since that is what the length field is. A message is capped
    /// at <see cref="Core.ChatCommand.MaxLength"/> before it ever gets here and the speaker's
    /// name is bounded too, so the clip is a guard rather than a thing that happens.</para>
    /// </summary>
    private static byte[] EncodeLine(string text)
    {
        byte[] all = System.Text.Encoding.UTF8.GetBytes(text);
        return all.Length <= 200 ? all : all[..200];
    }

    /// <summary>
    /// Host-side: records one seat's display name and tells everybody — the room, the world
    /// (so the craft wears the tag in-match), and every client.
    ///
    /// The names used to reach the match only by being copied off the room at LAUNCH, which
    /// left everyone who arrived after that — every rejoin, every late joiner — a nameless
    /// craft on every screen but this one, for the rest of the match.
    /// </summary>
    public void Rename(int seat, string name)
    {
        if (seat < 0) return;
        if (_nameOfSeat.TryGetValue(seat, out var was) && was == name) return;
        _nameOfSeat[seat] = name;
        Room?.ApplyName(seat, name);
        if (World is { } w) w.SeatNames[seat] = name;
        if (!IsHost) return;
        Span<byte> p = stackalloc byte[3 + 32];
        int at = WriteSeatName(p, seat, name);
        _net.Broadcast(p.Slice(0, at), reliable: true);
    }

    private static int WriteSeatName(Span<byte> p, int seat, string name)
    {
        byte[] text = Encode(name);
        p[0] = (byte)Msg.SeatName;
        p[1] = (byte)seat;
        p[2] = (byte)text.Length;
        text.CopyTo(p.Slice(3, text.Length));
        return 3 + text.Length;
    }

    /// <summary>How many snapshots pass between scoreboards. About a second: nobody reads a
    /// tally sixty times a second, and it is the one packet in the game that can be late
    /// without anybody noticing.</summary>
    private const int SnapshotsPerScoreboard = Session.SnapshotHz;

    private int _sinceScores;

    /// <summary>
    /// Host → all: every seat's kills, deaths and round trip, on its own slow clock.
    ///
    /// Four bytes a seat, so twenty players is under a hundred bytes once a second — small
    /// enough that it never has to be interest-culled or fitted around anything.
    /// </summary>
    private void BroadcastScores()
    {
        if (World == null) return;
        if (++_sinceScores < SnapshotsPerScoreboard) return;
        _sinceScores = 0;

        int seats = World.Players.Count;
        Span<byte> p = stackalloc byte[2 + MatchSettings.MaxSeats * 4];
        p[0] = (byte)Msg.Scores;
        p[1] = (byte)seats;
        int at = 2;
        for (int seat = 0; seat < seats; seat++)
        {
            p[at++] = (byte)Math.Clamp(World.KillsOf(seat), 0, 255);
            p[at++] = (byte)Math.Clamp(World.DeathsOf(seat), 0, 255);
            // Ping in tens of milliseconds. One byte reaches 2.5 seconds, which is far past
            // the point at which the number stops being a number and becomes a diagnosis.
            BitConverter.TryWriteBytes(p.Slice(at, 2),
                (ushort)Math.Clamp(World.PingOf(seat), 0, ushort.MaxValue)); at += 2;
        }
        _net.Broadcast(p.Slice(0, at), reliable: false);
    }

    /// <summary>
    /// This machine's player pointed at a place. On the host it plants at once and tells the
    /// room; on a client it plants at once <em>and</em> asks — the mark is this player's own
    /// statement about their own screen, so making them wait a round trip to see it would be
    /// the one thing that stops a coordination tool feeling like one.
    /// </summary>
    public void Mark(System.Numerics.Vector2 at)
    {
        if (World == null) return;
        World.PlaceMarker(LocalSeat, at);
        if (IsHost) { BroadcastMark(LocalSeat, at); return; }

        Span<byte> p = stackalloc byte[9];
        p[0] = (byte)Msg.Mark;
        BitConverter.TryWriteBytes(p.Slice(1, 4), at.X);
        BitConverter.TryWriteBytes(p.Slice(5, 4), at.Y);
        _net.Send(0, p, reliable: true);   // peer 0 is the host, on a client
    }

    /// <summary>Host-side: plants a client's mark and passes it on to everybody.</summary>
    private void PlantMark(int seat, System.Numerics.Vector2 at)
    {
        World?.PlaceMarker(seat, at);
        BroadcastMark(seat, at);
    }

    private void BroadcastMark(int seat, System.Numerics.Vector2 at)
    {
        Span<byte> p = stackalloc byte[10];
        p[0] = (byte)Msg.Mark;
        p[1] = (byte)seat;
        BitConverter.TryWriteBytes(p.Slice(2, 4), at.X);
        BitConverter.TryWriteBytes(p.Slice(6, 4), at.Y);
        _net.Broadcast(p, reliable: true);
    }

    /// <summary>Host-side: the whole roster, by name, to one peer. What a late arrival needs —
    /// they have missed every <see cref="Msg.SeatName"/> that went out before they existed.</summary>
    private void SendRosterTo(int peer)
    {
        if (!IsHost) return;
        Span<byte> p = stackalloc byte[3 + 32];
        foreach (var kv in _nameOfSeat)
            _net.Send(peer, p.Slice(0, WriteSeatName(p, kv.Key, kv.Value)), reliable: true);

        // And what everyone is flying. A build is state rather than an event, so a machine
        // that was not listening when a pick was made has no other way to ever learn it —
        // before this, a late joiner saw every craft that had already been chosen in the
        // machine's default paint for the rest of the match.
        Span<byte> b = stackalloc byte[2 + 64];
        foreach (var kv in _buildOfSeat)
        {
            b[0] = (byte)Msg.Build;
            b[1] = (byte)kv.Key;
            kv.Value.Write(b.Slice(2));
            _net.Send(peer, b.Slice(0, 2 + Loadout.Bytes), reliable: true);
        }
    }

    /// <summary>Posts a line to this machine's feed and, on the host, mirrors it to everyone
    /// else so the whole room sees the same comings and goings.</summary>
    private void Announce(string line)
    {
        Notices.Push(line);
        if (!IsHost) return;
        byte[] text = Encode(line);
        Span<byte> p = stackalloc byte[2 + 32];
        p[0] = (byte)Msg.Notice;
        p[1] = (byte)text.Length;
        text.CopyTo(p.Slice(2, text.Length));
        _net.Broadcast(p.Slice(0, 2 + text.Length), reliable: true);
    }

    /// <summary>Host-side: notices a peer has dropped, frees nothing (the seat is held for a
    /// rejoin) but marks the craft away and tells the room. Called from both pump paths so a
    /// drop is caught whether it happens in the lobby or mid-match.</summary>
    private void ReapDeparted()
    {
        if (!IsHost || World is null) return;
        while (_net.TryTakeDeparted(out int peer))
        {
            if (!_seatOfPeer.TryGetValue(peer, out int seat)) continue;
            _seatOfPeer.Remove(peer);   // the peer id is dead; the identity mapping is kept
            string name = _nameOfSeat.TryGetValue(seat, out var n) ? n : "A PLAYER";

            if (MatchStarted)
            {
                // Mid-match: the craft is held exactly where it stood, frozen and unhurt, so
                // a reconnection from the same Steam account steps straight back into it.
                if (seat >= 0 && seat < World.Players.Count) World.Players[seat].Away = true;
            }
            else
            {
                // Before LAUNCH there is nothing worth holding — they had a spawn point and a
                // chassis and no history — so the seat is genuinely given up and handed to the
                // next person through the door. Holding it instead meant a lobby people came
                // and went from opened its match with a row of abandoned craft on the grid,
                // each one still counting against the seat limit.
                World.VacateSeat(seat);
                // Forget who held it, so a return through the front door is an ordinary
                // join into whatever seat is free rather than a "rejoin" that would restore
                // the emptied craft they just walked away from.
                foreach (var kv in _seatOfIdentity)
                    if (kv.Value == seat) { _seatOfIdentity.Remove(kv.Key); break; }
                _nameOfSeat.Remove(seat);
                World.SeatNames.Remove(seat);
                _invSent.Remove(seat);
                ResetSeatInput(seat);
            }

            _pickOfSeat.Remove(seat);
            Room?.Remove(seat);
            Announce($"{name} LEFT");
        }
    }

    /// <summary>
    /// One tick of network. Called once per fixed step, after the local input is sampled and
    /// before the world steps: a client's own frame goes up, the host's account comes down,
    /// and the host files everyone's input for the step that is about to run.
    /// </summary>
    public void Pump(in InputFrame localInput)
    {
        _net.Pump();
        _tick++;

        // Where this machine thinks its own craft ended up at the end of the previous tick,
        // filed before anything the host has said is folded in. This is the record the host's
        // account is later checked against — see World.RecordPrediction.
        if (!IsHost && World is not null && LocalSeat >= 0 && _tick > 1)
            World.RecordPrediction(_tick - 1);

        // Everything waiting, whatever it is. Drained fully every tick so a stall never
        // leaves a backlog to work through later.
        while (_net.TryReceive(out int from, out byte[] payload))
            Handle(from, payload);
        ReapDeparted();

        if (World is null) return;

        if (IsHost)
        {
            // The host's own hands go straight into the world; everyone else's are fed one
            // tick at a time out of the buffers the packets above filled.
            World.SetInput(0, localInput);
            DriveSeatInputs();

            if (++_sinceSnapshot >= TicksPerSnapshot)
            {
                _sinceSnapshot = 0;
                Broadcast();
            }
        }
        else if (LocalSeat >= 0)
        {
            // The craft's own aim goes on the frame as an absolute angle. It is read off the
            // craft rather than out of the sampler because the sampler only has a mouse delta,
            // and a delta is precisely what must not cross the wire: the host integrated it, so
            // every lost packet took a permanent bite out of where the host thought this player
            // was looking, and the only thing that ever put the two back in agreement was this
            // client's reconciliation dragging its own camera round.
            // Guarded rather than indexed outright: a seat is assigned by Welcome and the
            // roster is grown in the same breath, so this always holds — but a client that
            // somehow sent input before its craft existed would take the whole game down, and
            // an unstamped frame is a far better outcome than that.
            InputFrame framed = (uint)LocalSeat < (uint)World.Players.Count
                ? localInput.WithAim(World.Players[LocalSeat].Heading,
                                     World.Players[LocalSeat].Pitch)
                : localInput;
            _sentInputs[_tick % RedundantInputs] = framed;

            // Unreliable, every tick, carrying the last dozen frames rather than only this
            // one. A dropped packet is a sixtieth of a second of intent and retransmitting it
            // would arrive long after the tick it belonged to — but the frames it held are
            // still in the next packet, so nothing is actually lost until a dozen in a row are.
            //
            // The packet also carries the last snapshot tick this client applied. That one
            // number is the whole of the host's ping measurement: the gap between it and the
            // host's own tick when this packet lands is a full round trip, and it is what
            // lets the host rewind the world to what this player could actually see when
            // deciding whether their shot connected (see World.Rewound). Piggybacked rather
            // than pinged separately because it costs four bytes on a packet already going.
            int carry = (int)Math.Min(RedundantInputs, _tick);
            Span<byte> p = stackalloc byte[10 + RedundantInputs * InputFrame.Size];
            int at = 0;
            p[at++] = (byte)Msg.Input;
            BitConverter.TryWriteBytes(p.Slice(at, 4), _tick); at += 4;          // newest tick
            BitConverter.TryWriteBytes(p.Slice(at, 4), LastAppliedTick); at += 4;
            p[at++] = (byte)carry;
            // Newest first, so a reader that trusts the count can walk them without knowing
            // the tick each one belongs to: frame i is tick (newest - i).
            for (int i = 0; i < carry; i++)
            {
                _sentInputs[(_tick - (uint)i) % RedundantInputs].Write(p.Slice(at, InputFrame.Size));
                at += InputFrame.Size;
            }
            _net.Send(0, p[..at], reliable: false);

            // A client still drives its own craft locally so the controls feel attached to
            // something; the host's next packet is what settles where it actually is.
            World.SetInput(LocalSeat, localInput);

            // Anything the player did in their inventory panel this tick. Its own reliable
            // messages, not folded into the input frame: a drag is an event that must arrive
            // exactly once, which is the opposite of what the input channel promises.
            SendInvIntents();
        }
    }

    /// <summary>
    /// Host-side: feeds every seated client exactly one tick of its own input into the world.
    ///
    /// This is where a lost packet stops being a lost action. A seat with something buffered
    /// eats the oldest frame it has — in order, once each, edges intact. A seat with nothing
    /// gets its last frame <em>repeated</em>, which carries the held keys forward (a player
    /// leaning on the throttle is still leaning on it) but drops the edges, because a grenade
    /// key that went down on one tick did not go down again on the next. Repeating the frame
    /// whole, which is what this did before, is how a hiccup used to throw two grenades.
    ///
    /// A seat that has fallen behind eats two frames rather than one, absorbing the edges of
    /// the one it skips, so a clump of late packets is caught up within a few ticks instead of
    /// becoming a delay that seat never gets back.
    /// </summary>
    private void DriveSeatInputs()
    {
        foreach (var kv in _seatOfPeer)
        {
            SeatInput si = InputOf(kv.Value);
            bool ate = false;
            int take = si.Pending.Count > JitterSlack ? 2 : 1;
            while (take-- > 0 && si.Pending.Count > 0)
            {
                var (t, f) = si.Pending.Dequeue();
                si.Last = ate ? f.Absorbing(si.Last) : f;
                si.Consumed = t;
                ate = true;
            }
            World!.SetInput(kv.Value, ate ? si.Last : si.Last.Repeat());
        }
    }

    /// <summary>
    /// Host-side: the input tick last fed to the sim for a seat, which is what its own players
    /// packet carries back so that client can measure its prediction error against the right
    /// moment. Zero before a seat has ever been stepped, which the client reads as "no anchor
    /// yet" and falls back on.
    /// </summary>
    private uint AckFor(int seat)
        => _seatInput.TryGetValue(seat, out var si) ? si.Consumed : 0u;

    /// <summary>
    /// Host-side: two packets per client — the players, then the field near that client. Both
    /// unreliable. The split is the fix for craft vanishing under load: the players packet is
    /// small and always fits a single datagram, so it arrives even when a busy field's packet
    /// is too big and drops.
    /// </summary>
    private void Broadcast()
    {
        BroadcastScores();

        // The run and its gate, when either actually moved. Reliable and change-driven rather
        // than rate-driven — a planet's whole DESCENT is a few dozen of these, against the
        // twenty-a-second everything below runs at, because nothing in it is continuous except
        // the rail and the charge and both of those are quantised into the digest.
        PushRunState();

        _snapshotSeq++;

        // Not everything in here needs describing twenty times a second, and until now
        // everything was. The host's uplink is the one link in this game that carries the whole
        // room — every packet below goes out once PER CLIENT, so a byte here is twenty bytes on
        // the wire in a full match — and at the full rate a busy nineteen-client session asks
        // for something like ten megabits a second upstream. It does not get it; the congestion
        // control backs off, the queue grows, and every client but the host plays a game that
        // arrives late. That is the whole of "laggy for everyone except the host".
        //
        // So the two packets the game is actually made of — where the players are, and what is
        // shooting at them — keep the full rate, and the three that are scenery drop to half of
        // it. None of them is anything a player can perceive at 20 Hz and not at 10: a boss's
        // walk and a cable's arc are already eased between packets by the client, and a
        // building that has come down has come down.
        bool scenery = (_snapshotSeq & 1) == 0;

        // The players packet is the same for everyone but four bytes of it, so the body is
        // written once and only the header is restamped per recipient.
        _out[0] = (byte)Msg.State;
        int np = Snapshot.WritePlayers(World!, _tick, _out.AsSpan(5));

        foreach (int peer in _net.Peers)
        {
            if (!_seatOfPeer.TryGetValue(peer, out int seat)) continue;

            // The four bytes that differ per client: which of THEIR input ticks this account of
            // the world was computed from. It is what lets them measure their own prediction
            // error against the right moment instead of against a position a round trip stale —
            // the difference between a craft that is corrected when it is wrong and one that is
            // dragged backwards the entire time it drives. It has to ride on this packet rather
            // than any other: paired with a different packet, a loss would marry a fresh
            // position to a stale anchor and invent an error that was never there.
            BitConverter.TryWriteBytes(_out.AsSpan(1, 4), AckFor(seat));
            _net.Send(peer, _out.AsSpan(0, np + 5), reliable: false);

            _field[0] = (byte)Msg.Field;
            int nf = Snapshot.WriteField(World!, seat, _tick, _field.AsSpan(1));
            _net.Send(peer, _field.AsSpan(0, nf + 1), reliable: false);

            // The bosses near this client, so their fight is seen and not only heard. Its own
            // small packet for the same reason the field is split out — a busy field must never
            // cost the boss its update.
            //
            // An empty one still has to go sometimes: it is also how a client is told to DROP
            // the puppets when a boss dies or walks out of range, and on an unreliable channel
            // a transition sent once can simply not arrive, leaving a dead Crab-Core standing on
            // somebody's screen for the rest of the match. So an empty packet goes at a slow
            // couple of hertz rather than never — six bytes, and the drop always lands.
            _bosses[0] = (byte)Msg.Bosses;
            int nb = Snapshot.WriteBosses(World!, seat, _tick, _bosses.AsSpan(1));
            bool anyBoss = Snapshot.BossesCarryAnything(_bosses.AsSpan(1, nb));
            if (anyBoss ? scenery : _snapshotSeq % 10 == 0)
                _net.Send(peer, _bosses.AsSpan(0, nb + 1), reliable: false);

            // And the rolled ones a DESCENT raises. Its own packet rather than a third flag on
            // the one above, because these are a list rather than two singletons — a TWINNED
            // roll ends its fight as two bodies — and because a rolled boss is described
            // entirely differently: its shape comes off the run's seed, which every machine
            // already has, so only what moves is here.
            //
            // Sent on the same empty-packet rule for the same reason: it is also how a client
            // is told to drop the body when one dies.
            _rolled[0] = (byte)Msg.Rolled;
            int nr = Snapshot.WriteRolled(World!, seat, _tick, _rolled.AsSpan(1));
            bool anyRolled = Snapshot.RolledCarryAnything(_rolled.AsSpan(1, nr));
            if (anyRolled ? scenery : _snapshotSeq % 10 == 0)
                _net.Send(peer, _rolled.AsSpan(0, nr + 1), reliable: false);

            // The sounds raised near this client since the last snapshot, so it hears the
            // fights around it. Its own cues rode local for the instant feel and are skipped on
            // the far end; everything else it plays positioned to its own craft.
            if (World!.SoundCues.Count > 0)
            {
                _sound[0] = (byte)Msg.Sound;
                int ns = WriteSounds(seat, _sound.AsSpan(1));
                _net.Send(peer, _sound.AsSpan(0, ns + 1), reliable: false);
            }

            // The particle bursts near this client, so it sees the field's debris and not only
            // its own. Same interest cull and keep-nothing model as the sounds beside them.
            if (World!.EffectCues.Count > 0)
            {
                _effect[0] = (byte)Msg.Effect;
                int nx = WriteEffects(seat, _effect.AsSpan(1));
                _net.Send(peer, _effect.AsSpan(0, nx + 1), reliable: false);
            }

            // The damaged skyline near this client. Its own packet for the same reason the
            // bosses are: a busy field must never cost a client the fact that the tower it is
            // taking cover behind is no longer there. Both of the next two write nothing at
            // all when there is nothing to say, which is most of a match.
            if (scenery)
            {
                _structures[0] = (byte)Msg.Structures;
                // The start index walks with the tick, so a district holding more ruins than one
                // packet can carry is described in full over a few snapshots instead of the first
                // twenty being repeated for ever while the rest are never mentioned at all.
                int nst = Snapshot.WriteStructures(World!, seat, _tick, _structures.AsSpan(1),
                    rotation: (int)(_tick * (uint)Snapshot.MaxStructuresPerPacket));
                if (nst > 0) _net.Send(peer, _structures.AsSpan(0, nst + 1), reliable: false);

                // The cables and beams around this client — the light a fight throws off.
                _rigs[0] = (byte)Msg.Rigs;
                int nrg = Snapshot.WriteRigs(World!, seat, _tick, _rigs.AsSpan(1));
                if (nrg > 0) _net.Send(peer, _rigs.AsSpan(0, nrg + 1), reliable: false);
            }

            // And this seat's own pack, when and only when it has changed. Reliable: salvage
            // that fell off the wire would simply never arrive, since nothing re-sends it.
            SendInventoryIfChanged(peer, seat);

            // Everything this client is getting has now been queued. Push it: the transport
            // coalesces small sends into one datagram, which is exactly what should happen to
            // seven packets written back to back — but it does so by WAITING a few milliseconds
            // for more, and there is no more coming until the next snapshot. Flushing here buys
            // the coalescing without buying the delay.
            _net.Flush(peer);
        }

        // The cues are spent once described to everyone — clear them whether or not anyone was
        // listening, so a host alone in a room never lets either list grow.
        World!.SoundCues.Clear();
        World!.EffectCues.Clear();
    }

    /// <summary>
    /// Host-side: mirrors one seat its own pack, if anything in it has moved since the last
    /// time. Reliable and change-driven rather than streamed — an inventory is small, it
    /// changes a handful of times a minute, and every one of those changes matters exactly
    /// once, which is the opposite of the keep-last packets around it.
    /// </summary>
    private void SendInventoryIfChanged(int peer, int seat)
    {
        Core.Inventory pack = World!.InventoryOf(seat);
        int digest = pack.Fingerprint();
        if (_invSent.TryGetValue(seat, out int was) && was == digest) return;
        _invSent[seat] = digest;

        _inv[0] = (byte)Msg.Inventory;
        pack.Write(_inv.AsSpan(1, Core.Inventory.WireSize));
        _net.Send(peer, _inv.AsSpan(0, 1 + Core.Inventory.WireSize), reliable: true);
    }

    /// <summary>Client-side: sends everything the player has done to their pack since the last
    /// tick. Reliable and in order — a dropped move would leave the client's mirror and the
    /// host's pack permanently disagreeing until something else happened to change it.</summary>
    private void SendInvIntents()
    {
        if (World!.InvIntents.Count == 0) return;
        Span<byte> p = stackalloc byte[1 + Core.InvIntent.Size];
        p[0] = (byte)Msg.InvAct;
        foreach (var intent in World.InvIntents)
        {
            intent.Write(p.Slice(1, Core.InvIntent.Size));
            _net.Send(0, p, reliable: true);
        }
        World.InvIntents.Clear();
    }

    /// <summary>How many cues one sound packet carries at most — plenty for a busy fight, and
    /// small enough that the packet stays inside one datagram (48·7 = 336 bytes + a header).</summary>
    private const int MaxSounds = 48;

    /// <summary>Writes the tick's cues within interest range of <paramref name="forSeat"/>, each
    /// as id, position, packed parameter and owning seat. Returns the bytes written.</summary>
    private int WriteSounds(int forSeat, Span<byte> dst)
    {
        System.Numerics.Vector2 eye =
            World!.Players[Math.Clamp(forSeat, 0, World.Players.Count - 1)].Position;
        int at = 0;
        int countAt = at++;
        int n = 0;
        foreach (var c in World.SoundCues)
        {
            if (n >= MaxSounds) break;
            if (Torus.DistanceSquared(c.Pos, eye) > Snapshot.InterestRadius * Snapshot.InterestRadius)
                continue;
            dst[at++] = (byte)c.Id;
            WriteShort(dst, ref at, c.Pos.X * RoomPos);   // same 1/64 world-position scale
            WriteShort(dst, ref at, c.Pos.Y * RoomPos);
            dst[at++] = CueBank.IsFraction(c.Id)
                ? (byte)Math.Clamp(c.Param * 255f, 0f, 255f)
                : (byte)Math.Clamp(c.Param, 0f, 255f);
            dst[at++] = unchecked((byte)(sbyte)Math.Clamp(c.Owner, -128, 127));
            n++;
        }
        dst[countAt] = (byte)n;
        return at;
    }

    /// <summary>How many bursts one effect packet carries at most — capped so the packet stays
    /// inside one datagram (40·10 = 400 bytes + a header) even when a fight is throwing debris
    /// everywhere. The interest cull below rarely reaches it near any one client.</summary>
    private const int MaxEffects = 40;

    /// <summary>Writes the tick's particle bursts within interest range of <paramref name="forSeat"/>,
    /// each as kind, world position and colour. Returns the bytes written.</summary>
    private int WriteEffects(int forSeat, Span<byte> dst)
    {
        System.Numerics.Vector2 eye =
            World!.Players[Math.Clamp(forSeat, 0, World.Players.Count - 1)].Position;
        int at = 0;
        int countAt = at++;
        int n = 0;
        foreach (var e in World.EffectCues)
        {
            if (n >= MaxEffects) break;
            var plane = new System.Numerics.Vector2(e.Origin.X, e.Origin.Z);
            if (Torus.DistanceSquared(plane, eye) > Snapshot.InterestRadius * Snapshot.InterestRadius)
                continue;
            dst[at++] = (byte)e.Kind;
            WriteShort(dst, ref at, e.Origin.X * RoomPos);   // same 1/64 world-position scale
            WriteShort(dst, ref at, e.Origin.Z * RoomPos);
            WriteShort(dst, ref at, e.Origin.Y * RoomPos);   // height
            dst[at++] = e.Color.R;
            dst[at++] = e.Color.G;
            dst[at++] = e.Color.B;
            n++;
        }
        dst[countAt] = (byte)n;
        return at;
    }

    private void Handle(int from, byte[] payload)
    {
        if (payload.Length < 1 || World is null) return;

        switch ((Msg)payload[0])
        {
            case Msg.Hello when IsHost:
            {
                if (payload.Length < 3) return;
                var chassis = (PlayerClass)payload[1];
                int nameLen = payload[2];
                if (payload.Length < 3 + nameLen) return;
                string name = DecodeName(payload.AsSpan(3, nameLen));
                if (name.Length == 0) name = "A PLAYER";

                long identity = _net.IdentityOf(from);

                // A known account is a rejoin: hand them back the very seat they left, with the
                // craft the host has been holding in place — same chassis, lives and position.
                if (identity != 0 && _seatOfIdentity.TryGetValue(identity, out int kept)
                    && kept < World.Players.Count)
                {
                    bool wasHere = _seatOfPeer.ContainsValue(kept);
                    // A binding this peer did not already have is a machine that has genuinely
                    // just arrived on this seat, and its input clock starts from scratch.
                    if (!_seatOfPeer.TryGetValue(from, out int had) || had != kept)
                        ResetSeatInput(kept);
                    _seatOfPeer[from] = kept;
                    Rename(kept, name);
                    World.Players[kept].Away = false;
                    // Their pack has to be told again from scratch: the machine coming back is
                    // holding an empty mirror, and the change-driven echo would say nothing at
                    // all because the pack itself has not moved since they left.
                    _invSent.Remove(kept);
                    if (!wasHere) Announce($"{name} REJOINED");
                    SendWelcome(from, kept);
                    SendRosterTo(from);
                    if (MatchStarted) SendStartTo(from);
                    break;
                }

                // Otherwise a new player. A repeat hello from a peer already seated (its Welcome
                // was lost, or it was sent again while the link was still coming up) just gets
                // another Welcome, not a second seat.
                if (_seatOfPeer.TryGetValue(from, out int have))
                {
                    Rename(have, name);
                    SendWelcome(from, have);
                    SendRosterTo(from);
                    if (MatchStarted) SendStartTo(from);
                    break;
                }

                var craft = World.AddPlayer(new Loadout { Class = chassis });
                if (craft is null)
                {
                    // No seat for them. Say so: a joiner that is merely ignored waits on the
                    // CONNECTING banner until they give up, with nothing to tell them the room
                    // was simply full.
                    Span<byte> no = stackalloc byte[1];
                    no[0] = (byte)Msg.Full;
                    _net.Send(from, no, reliable: true);
                    break;
                }

                int seat = World.Seat(craft);
                _seatOfPeer[from] = seat;
                if (identity != 0) _seatOfIdentity[identity] = seat;
                _invSent.Remove(seat);
                ResetSeatInput(seat);
                Rename(seat, name);
                Announce($"{name} JOINED");
                SendWelcome(from, seat);
                // Everyone already here, by name — a late arrival has missed every roster
                // update the room ever sent.
                SendRosterTo(from);
                // A player who arrives after LAUNCH is dropped straight into the running match.
                if (MatchStarted) SendStartTo(from);
                break;
            }

            case Msg.Welcome when !IsHost:
            {
                const int fixedLen = 2 + MatchSettings.Size + 12;
                if (payload.Length < fixedLen) return;
                int at = 1;
                int given = payload[at++];
                if (given < 0 || given >= MatchSettings.MaxSeats) return;
                // Everything below rebuilds our craft from scratch and puts it where the host
                // says. That is exactly right for a seating and for a rejoin, and exactly
                // wrong for the extra Welcomes a repeated hello earns (see SendHello): once
                // seated, another one would tear down the craft we are driving and snap it
                // back to a transform from a hundred milliseconds ago. So it applies only when
                // we were actually asking — which a rejoin is, since it sends a fresh hello.
                bool asked = _helloWanted || LocalSeat != given;
                LocalSeat = given;
                _helloWanted = false;
                Rejected = null;
                World.Match = MatchSettings.Read(payload.AsSpan(at, MatchSettings.Size));
                at += MatchSettings.Size;
                // The rules the host is actually holding, onto the lobby's own copy — so a
                // player who joins after the host has finished at the console sees the map,
                // seats and revives they will really be playing with, rather than the
                // defaults they started the room with.
                Room?.AdoptRules(World.Match);
                if (!asked) break;

                // Fill the roster out to our own seat with placeholders the host's snapshots
                // will overwrite, then install our own chosen craft — full build, not a
                // placeholder tank — at the seat we were actually given.
                if (!World.EnsureSeat(LocalSeat)) return;
                World.ReplacePlayer(LocalSeat, World.Loadout);
                World.LocalIndex = LocalSeat;

                // Open the craft in the state the host sent — for a rejoin that is the state it
                // was held in, which is what makes "restore everything" true; for a fresh join
                // it is simply where the seat opens.
                PlayerTank me = World.Players[LocalSeat];
                float x = BitConverter.ToInt16(payload.AsSpan(at, 2)) / 64f; at += 2;
                float y = BitConverter.ToInt16(payload.AsSpan(at, 2)) / 64f; at += 2;
                float h = BitConverter.ToInt16(payload.AsSpan(at, 2)) / 64f; at += 2;
                float head = BitConverter.ToInt16(payload.AsSpan(at, 2)) * (MathF.Tau / 65536f); at += 2;
                int lives = payload[at++];
                float shield = BitConverter.ToInt16(payload.AsSpan(at, 2)) / 4f; at += 2;
                int ammo = payload[at++];
                me.Position = Torus.Wrap(new System.Numerics.Vector2(x, y));
                me.Height = h;
                me.Heading = head;
                me.Lives = lives;
                me.Shield = shield;
                me.Ammo = ammo;
                break;
            }

            case Msg.Notice when !IsHost:
            {
                if (payload.Length < 2) return;
                int len = payload[1];
                if (payload.Length < 2 + len) return;
                Notices.Push(DecodeName(payload.AsSpan(2, len)));
                break;
            }

            case Msg.Input when IsHost:
            {
                if (payload.Length < 10) return;
                if (!_seatOfPeer.TryGetValue(from, out int seat)) return;

                uint newest = BitConverter.ToUInt32(payload.AsSpan(1, 4));
                uint ack = BitConverter.ToUInt32(payload.AsSpan(5, 4));
                int carry = payload[9];
                if (payload.Length < 10 + carry * InputFrame.Size) return;

                // Oldest first, so the queue comes out in the order the player's hands went
                // through it. Frame i is tick (newest - i); anything at or below what has
                // already been taken in is a redundant copy or a packet that overtook a newer
                // one, and either way it is a tick this seat has already accounted for.
                SeatInput si = InputOf(seat);
                for (int i = carry - 1; i >= 0; i--)
                {
                    if (newest <= (uint)i) continue;      // before the session's first tick
                    uint t = newest - (uint)i;
                    if (t <= si.Newest) continue;
                    si.Pending.Enqueue((t,
                        InputFrame.Read(payload.AsSpan(10 + i * InputFrame.Size, InputFrame.Size))));
                    si.Newest = t;
                }

                // The acknowledgement riding on the back of the packet: how far behind this
                // client's view of the world runs, in ticks. A packet from a build that does
                // not carry one simply leaves the seat's lag as it was — worst case zero,
                // which is the uncompensated behaviour this replaces.
                if (ack == 0 || ack > _tick) break;   // not seen a snapshot yet, or nonsense
                World.SetSeatLag(seat, (int)(_tick - ack));
                break;
            }

            case Msg.Start when !IsHost:
                MatchStarted = true;
                break;

            case Msg.State when !IsHost:
            {
                if (payload.Length < 5) break;
                // The four bytes ahead of the body: which of our own input ticks the host had
                // stepped when it wrote this. The anchor the local craft's prediction error is
                // measured against.
                uint acked = BitConverter.ToUInt32(payload.AsSpan(1, 4));
                uint tick = Snapshot.ApplyPlayers(World, payload.AsSpan(5), acked);
                // An older packet that overtook a newer one is worse than no packet at all.
                if (tick != 0 && tick > LastAppliedTick) LastAppliedTick = tick;
                break;
            }

            case Msg.Field when !IsHost:
            {
                // No ordering guard: a field packet is scenery, and the freshest one to land
                // wins by simply being applied last. Missing one keeps the last field.
                Snapshot.ApplyField(World, payload.AsSpan(1));
                break;
            }

            case Msg.Bosses when !IsHost:
                Snapshot.ApplyBosses(World, payload.AsSpan(1));
                break;

            case Msg.Rolled when !IsHost:
                Snapshot.ApplyRolled(World, payload.AsSpan(1));
                break;

            case Msg.Structures when !IsHost:
                Snapshot.ApplyStructures(World, payload.AsSpan(1));
                break;

            case Msg.Rigs when !IsHost:
                Snapshot.ApplyRigs(World, payload.AsSpan(1));
                break;

            case Msg.Inventory when !IsHost:
            {
                if (payload.Length < 1 + Core.Inventory.WireSize) return;
                if (LocalSeat < 0) return;
                // Straight over the mirror. Whatever the panel optimistically did to it, this
                // is what the player actually has.
                World.InventoryOf(LocalSeat).Read(payload.AsSpan(1, Core.Inventory.WireSize));
                break;
            }

            case Msg.InvAct when IsHost:
            {
                if (payload.Length < 1 + Core.InvIntent.Size) return;
                if (!_seatOfPeer.TryGetValue(from, out int seat)) return;
                World.ApplyInvIntent(seat, Core.InvIntent.Read(payload.AsSpan(1, Core.InvIntent.Size)));
                // Every intent gets an answer, including the ones the host threw out. A
                // refused move leaves the pack byte-for-byte identical, so the change-driven
                // echo would say nothing — and the client would be left holding the item it
                // invented, with nothing ever coming along to take it back.
                _invSent.Remove(seat);
                break;
            }

            case Msg.Effect when !IsHost:
            {
                if (payload.Length < 2) break;
                int at = 1;
                int count = payload[at++];
                for (int i = 0; i < count; i++)
                {
                    if (at + 10 > payload.Length) break;
                    var kind = (Entities.EffectKind)payload[at++];
                    float x = BitConverter.ToInt16(payload.AsSpan(at, 2)) / 64f; at += 2;
                    float z = BitConverter.ToInt16(payload.AsSpan(at, 2)) / 64f; at += 2;
                    float y = BitConverter.ToInt16(payload.AsSpan(at, 2)) / 64f; at += 2;
                    byte r = payload[at++], g = payload[at++], b = payload[at++];
                    World.PlayRemoteEffect(kind, new System.Numerics.Vector3(x, y, z),
                        new Raylib_cs.Color(r, g, b, (byte)255));
                }
                break;
            }

            case Msg.Sound when !IsHost:
            {
                if (payload.Length < 2) break;
                int at = 1;
                int count = payload[at++];
                for (int i = 0; i < count; i++)
                {
                    if (at + 7 > payload.Length) break;
                    var id = (Cue)payload[at++];
                    float x = BitConverter.ToInt16(payload.AsSpan(at, 2)) / 64f; at += 2;
                    float y = BitConverter.ToInt16(payload.AsSpan(at, 2)) / 64f; at += 2;
                    byte pb = payload[at++];
                    int owner = unchecked((sbyte)payload[at++]);
                    float param = CueBank.IsFraction(id) ? pb / 255f : pb;
                    World.PlayRemoteCue(id, Torus.Wrap(new System.Numerics.Vector2(x, y)), param, owner);
                }
                break;
            }

            // --- The 3D lobby room ------------------------------------------------
            case Msg.Pick when IsHost:
            {
                if (payload.Length < 2 + Loadout.Bytes) break;
                if (!_seatOfPeer.TryGetValue(from, out int seat)) break;
                // Read, not trusted: Loadout.Read clamps every track and walks an over-budget
                // build back down, so a hand-written packet cannot buy a craft the hangar
                // would refuse to make.
                ApplyPick(seat, Loadout.Read(payload.AsSpan(2)));
                break;
            }

            case Msg.Build when !IsHost:
            {
                // The host telling this machine what somebody is flying. Applies to the room's
                // avatar and to the seat's craft alike — the same fact, and both places draw it.
                if (payload.Length < 2 + Loadout.Bytes) break;
                int seat = payload[1];
                Loadout build = Loadout.Read(payload.AsSpan(2));
                _buildOfSeat[seat] = build;
                if (World is { } known) known.SeatBuilds[seat] = build;
                Room?.ApplyPick(seat, build.Class, ready: true);
                // Never over our own craft: this machine's build is the one at its own pod,
                // and the echo of our own pick coming back would rebuild the craft we are
                // flying — dropping whatever the sim had already given it — for no gain.
                if (seat == LocalSeat) break;
                if (World is { } w && seat < w.Players.Count && !w.Players[seat].Build.SameAs(build))
                    w.ReplacePlayer(seat, build);
                break;
            }

            case Msg.Name when IsHost:
            {
                if (payload.Length < 2) break;
                if (!_seatOfPeer.TryGetValue(from, out int seat)) break;
                int len = payload[1];
                if (payload.Length < 2 + len) break;
                string name = DecodeName(payload.AsSpan(2, len));
                if (name.Length == 0) name = "A PLAYER";
                Rename(seat, name);
                break;
            }

            case Msg.RoomMove when IsHost:
            {
                if (payload.Length < 11) break;
                if (!_seatOfPeer.TryGetValue(from, out int seat)) break;
                int at = 1;
                float x = BitConverter.ToInt16(payload.AsSpan(at, 2)) / RoomPos; at += 2;
                float y = BitConverter.ToInt16(payload.AsSpan(at, 2)) / RoomPos; at += 2;
                float head = BitConverter.ToInt16(payload.AsSpan(at, 2)) / RoomAng; at += 2;
                float pitch = BitConverter.ToInt16(payload.AsSpan(at, 2)) / RoomAng; at += 2;
                float height = BitConverter.ToInt16(payload.AsSpan(at, 2)) / RoomPos; at += 2;
                Room?.ApplyTransform(seat, new System.Numerics.Vector2(x, y), head, pitch, height,
                    _nameOfSeat.TryGetValue(seat, out var n) ? n : "PLAYER");
                break;
            }

            case Msg.Vote when IsHost:
            {
                // A ballot. Only counted while a vote is actually running — a client that
                // pressed Enter as the clock ran out must not land a late vote on a room that
                // has already been told where it is going.
                if (payload.Length < 3 || payload[1] != VoteCast) break;
                if (Room is not { } r || !r.Chart.VoteOpen) break;
                if (!_seatOfPeer.TryGetValue(from, out int voter)) break;
                var choice = (PlanetId)payload[2];
                if (!Enum.IsDefined(choice)) break;   // a malformed byte is not a world
                r.Chart.Cast(voter, choice);
                BroadcastVote(r);                     // everyone watches the pips move at once
                break;
            }

            case Msg.Vote when !IsHost:
            {
                if (payload.Length < 5 || payload[1] != VoteTally) break;
                if (Room is not { } r) break;
                r.Chart.AdoptClock(payload[2] != 0, payload[3] / 10f);
                int n = payload[4];
                if (payload.Length < 5 + n * 2) break;
                r.Chart.Votes.Clear();
                for (int i = 0; i < n; i++)
                {
                    var pick = (PlanetId)payload[6 + i * 2];
                    if (Enum.IsDefined(pick)) r.Chart.Cast(payload[5 + i * 2], pick);
                }
                break;
            }

            case Msg.Run when !IsHost:
            {
                if (payload.Length < 15 || World is null) break;
                int seed = BitConverter.ToInt32(payload, 1);
                int hop = payload[5];
                var dest = (PlanetId)payload[6];
                if (!Enum.IsDefined(dest)) break;      // a malformed byte is not a world
                int visited = payload[7];
                var phase = (DescentPhase)payload[8];
                if (!Enum.IsDefined(phase)) break;

                // Builds the run and picks the gates on the first packet carrying a new seed,
                // and does nothing at all on the dozens that carry the same one.
                World.NetOpenRun(seed, hop, dest, visited);
                World.NetAdoptRun(phase, payload[9],
                    BitConverter.ToUInt16(payload, 10), BitConverter.ToUInt16(payload, 12),
                    payload[14] / 4f, gatesLit: payload.Length > 15 && payload[15] != 0);
                break;
            }

            case Msg.Arch when IsHost:
            {
                if (payload.Length < 2 || World is null) break;
                if (!_seatOfPeer.TryGetValue(from, out int seat)) break;

                switch (payload[1])
                {
                    case ArchClaimKind:
                    {
                        // The race, arbitrated. Twenty people can be reaching for three panels
                        // and the host is the only machine entitled to say who got there — a
                        // client that claimed locally would have to be contradicted, and
                        // "which arch" is the one fact the whole room navigates by.
                        if (payload.Length < 4 || World.ClaimedGate is not null) break;
                        int index = BitConverter.ToUInt16(payload, 2);
                        foreach (var g in World.Gates)
                        {
                            if (g.StructureIndex != index) continue;
                            if (World.ClaimGate(g, seat)) BroadcastArchClaim(index);
                            break;
                        }
                        break;
                    }

                    case ArchCastKind:
                    {
                        if (payload.Length < 3 || ArchPanel is not { } panel) break;
                        if (!panel.Chart.VoteOpen) break;   // no late ballots
                        var choice = (PlanetId)payload[2];
                        if (!Enum.IsDefined(choice)) break;
                        panel.Chart.Cast(seat, choice);
                        BroadcastArchVote(panel.Chart);     // everyone watches the pips move
                        break;
                    }

                    case ArchFeedKind:
                        if (payload.Length < 3) break;
                        // Replayed against the authoritative pack and the authoritative gate,
                        // which is what stops a client feeding a fragment it does not have, or
                        // feeding one from the other side of the city.
                        World.FeedGate(seat, payload[2]);
                        break;
                }
                break;
            }

            case Msg.Arch when !IsHost:
            {
                if (payload.Length < 2 || World is null) break;

                switch (payload[1])
                {
                    case ArchClaimKind:
                    {
                        if (payload.Length < 4) break;
                        int index = BitConverter.ToUInt16(payload, 2);
                        foreach (var g in World.Gates)
                        {
                            if (g.StructureIndex != index) continue;
                            if (World.ClaimedGate is null) World.ClaimGate(g, LocalSeat);
                            break;
                        }
                        break;
                    }

                    case ArchTallyKind:
                    {
                        if (payload.Length < 5 || ArchPanel is not { } panel) break;
                        panel.Chart.AdoptClock(payload[2] != 0, payload[3] / 10f);
                        int n = payload[4];
                        if (payload.Length < 5 + n * 2) break;
                        panel.Chart.Votes.Clear();
                        for (int i = 0; i < n; i++)
                        {
                            var pick = (PlanetId)payload[6 + i * 2];
                            if (Enum.IsDefined(pick)) panel.Chart.Cast(payload[5 + i * 2], pick);
                        }
                        break;
                    }

                    case ArchStateKind:
                    {
                        if (payload.Length < 17 + Unrendered.World.Arch.SocketCount) break;
                        int at = 2;
                        int index = BitConverter.ToUInt16(payload, at); at += 2;
                        var phase = (Unrendered.World.Arch.Phase)payload[at++];
                        if (!Enum.IsDefined(phase)) break;

                        byte destByte = payload[at++];
                        PlanetId? dest = destByte == 255 ? null : (PlanetId)destByte;
                        if (dest is { } d && !Enum.IsDefined(d)) dest = null;

                        Span<Fragment> sockets = stackalloc Fragment[Unrendered.World.Arch.SocketCount];
                        for (int i = 0; i < Unrendered.World.Arch.SocketCount; i++)
                        {
                            var f = (Fragment)payload[at++];
                            sockets[i] = Enum.IsDefined(f) ? f : Fragment.None;
                        }

                        var carrying = (Fragment)payload[at++];
                        if (!Enum.IsDefined(carrying)) carrying = Fragment.None;
                        byte toByte = payload[at++];
                        int carryingTo = toByte == 255 ? -1 : toByte;
                        float carryT = payload[at++] / 255f;
                        float charge = payload[at++] / 255f;
                        int entered = BitConverter.ToInt32(payload, at); at += 4;
                        float grace = payload[at];

                        World.NetAdoptGate(index, phase, dest, sockets, carrying, carryingTo,
                            carryT, charge, entered, grace);
                        break;
                    }
                }
                break;
            }

            case Msg.Chat when IsHost:
            {
                // A client said something. What arrives is text and only text — the host is the
                // one that decides whether it was a command, whether that seat may run it, and
                // what it does.
                if (payload.Length < 3 || payload[1] != ChatSay) break;
                if (!_seatOfPeer.TryGetValue(from, out int who)) break;
                int n = Math.Min(payload[2], payload.Length - 3);
                if (n <= 0) break;
                HandleTyped(DecodeName(payload.AsSpan(3, n)), who);
                break;
            }

            case Msg.Chat when !IsHost:
            {
                if (payload.Length < 3) break;
                int n = Math.Min(payload[2], payload.Length - 3);
                if (n <= 0) break;
                string line = DecodeName(payload.AsSpan(3, n));
                Notices.Push(line, payload[1] == ChatTold
                    ? NoticeFeed.Kind.Console
                    : NoticeFeed.Kind.Chat);
                break;
            }

            case Msg.Rules when !IsHost:
            {
                if (payload.Length < 1 + MatchSettings.Size) break;
                MatchSettings rules = MatchSettings.Read(payload.AsSpan(1, MatchSettings.Size));
                Room?.AdoptRules(rules);
                // And onto the world, not only the lobby's readout. The seat count in
                // particular is load-bearing: a client's roster refuses to grow past its own
                // MaxPlayers, so a host who widened the room after this client joined would
                // have had every player past the old limit be named by the snapshot and
                // created by none — invisible, on that machine alone.
                World.Match = rules;
                break;
            }

            case Msg.Full when !IsHost:
                Rejected = "THAT MATCH IS FULL";
                _helloWanted = false;
                break;

            // Client → host: somebody pointed at something. The host is the only thing that
            // may say which seat did it — a payload naming its own seat is a payload that
            // could name somebody else's.
            case Msg.Mark when IsHost:
            {
                if (World == null || payload.Length < 9) break;
                if (!_seatOfPeer.TryGetValue(from, out int seat)) break;
                float mx = BitConverter.ToSingle(payload.AsSpan(1, 4));
                float mz = BitConverter.ToSingle(payload.AsSpan(5, 4));
                PlantMark(seat, new System.Numerics.Vector2(mx, mz));
                break;
            }

            // Host → all: seat N pointed here. Our own comes back to us and is dropped — we
            // planted it the instant we pressed the key.
            case Msg.Mark when !IsHost:
            {
                if (World == null || payload.Length < 10) break;
                int seat = payload[1];
                if (seat == LocalSeat) break;
                float mx = BitConverter.ToSingle(payload.AsSpan(2, 4));
                float mz = BitConverter.ToSingle(payload.AsSpan(6, 4));
                World.PlaceMarker(seat, new System.Numerics.Vector2(mx, mz));
                break;
            }

            case Msg.Scores when !IsHost:
            {
                if (World == null || payload.Length < 2) break;
                int seats = payload[1];
                if (payload.Length < 2 + seats * 4) break;
                int at = 2;
                for (int seat = 0; seat < seats; seat++)
                {
                    int kills = payload[at++];
                    int deaths = payload[at++];
                    int ping = BitConverter.ToUInt16(payload.AsSpan(at, 2)); at += 2;
                    World.SetScore(seat, kills, deaths, ping);
                }
                break;
            }

            case Msg.SeatName when !IsHost:
            {
                if (payload.Length < 3) break;
                int seat = payload[1];
                int len = payload[2];
                if (payload.Length < 3 + len) break;
                string who = DecodeName(payload.AsSpan(3, len));
                if (who.Length == 0) who = "A PLAYER";
                World.SeatNames[seat] = who;
                Room?.ApplyName(seat, who);
                break;
            }

            case Msg.RoomState when !IsHost:
            {
                if (Room is not { } room || payload.Length < 2) break;
                int at = 1;
                int count = payload[at++];
                for (int i = 0; i < count; i++)
                {
                    if (at + 14 > payload.Length) break;
                    int seat = payload[at++];
                    byte cb = payload[at++];
                    PlayerClass? chassis = cb == 255 ? null : (PlayerClass)cb;
                    bool ready = payload[at++] != 0;
                    float x = BitConverter.ToInt16(payload.AsSpan(at, 2)) / RoomPos; at += 2;
                    float y = BitConverter.ToInt16(payload.AsSpan(at, 2)) / RoomPos; at += 2;
                    float head = BitConverter.ToInt16(payload.AsSpan(at, 2)) / RoomAng; at += 2;
                    float pitch = BitConverter.ToInt16(payload.AsSpan(at, 2)) / RoomAng; at += 2;
                    float height = BitConverter.ToInt16(payload.AsSpan(at, 2)) / RoomPos; at += 2;
                    int nlen = payload[at++];
                    if (at + nlen > payload.Length) break;
                    string name = DecodeName(payload.AsSpan(at, nlen)); at += nlen;
                    room.ApplyAvatar(seat, name.Length == 0 ? "PLAYER" : name, chassis, ready,
                        new System.Numerics.Vector2(x, y), head, pitch, height);
                }
                break;
            }
        }
    }
}
