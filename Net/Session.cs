using VoidTanks.Core;
using VoidTanks.Entities;

namespace VoidTanks.Net;

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
    private uint _tick;
    private int _sinceSnapshot;

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
                ApplyPick(LocalSeat, room.MyChassis ?? PlayerClass.Tank);
            if (room.NameDirty) { LocalName = room.MyName; _nameOfSeat[LocalSeat] = room.MyName; }
            if (room.RulesDirty) BroadcastRules(room.Match);
            room.ClearDirty();

            if (++_sinceRoom >= TicksPerSnapshot)
            {
                _sinceRoom = 0;
                BroadcastRoomState(room);
            }
        }
        else if (LocalSeat >= 0)
        {
            if (room.PickDirty)
            {
                PlayerClass chosen = room.MyChassis ?? PlayerClass.Tank;
                SendPick(chosen);
                // Install the pick on this client's OWN craft immediately. The host is
                // authoritative on this seat for everyone else and its snapshot names the
                // chassis, but a client never overwrites its own seat from a snapshot — so
                // without this the player keeps driving the placeholder tank they were seated
                // as while the host simulates the chassis they actually picked, and the two
                // move so differently that the craft is never where anyone thinks it is.
                if (World is { } w && LocalSeat < w.Players.Count && w.Players[LocalSeat].Class != chosen)
                    w.ReplacePlayer(LocalSeat, chosen);
            }
            if (room.NameDirty) SendName(room.MyName);
            room.ClearDirty();
            SendRoomMove(room.Position, room.Heading, room.Pitch, room.Height);
        }
    }

    /// <summary>Host-side: records a seat's chosen chassis, marks it ready, and rebuilds that
    /// seat's craft in the world it is holding so the eventual match opens with the right one.</summary>
    private void ApplyPick(int seat, PlayerClass chassis)
    {
        _pickOfSeat[seat] = (chassis, true);
        Room?.ApplyPick(seat, chassis, ready: true);
        if (World is { } w && seat >= 0 && seat < w.Players.Count && w.Players[seat].Class != chassis)
            w.ReplacePlayer(seat, chassis);
    }

    private void SendPick(PlayerClass chassis)
    {
        Span<byte> p = stackalloc byte[3];
        p[0] = (byte)Msg.Pick;
        p[1] = (byte)chassis;
        p[2] = 1;   // ready
        _net.Send(0, p, reliable: true);
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

    private void BroadcastRules(MatchSettings m)
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
        _nameOfSeat[0] = LocalName;       // the host's own name, for the roster and the tags
    }

    /// <summary>Every seat's name the host knows, for the loop to copy onto the world at launch
    /// so team-mates carry a floating tag into the match.</summary>
    public IReadOnlyDictionary<int, string> SeatNames => _nameOfSeat;

    /// <summary>
    /// Client-side: adopts the world the host has described. The seat is not known until the
    /// Welcome arrives, so the world is held with a placeholder roster until then.
    /// </summary>
    public void JoinMatch(World.World world) => World = world;

    /// <summary>Client-side: announces the chassis this player picked and their name. Reliable
    /// — a lost hello would seat somebody as a tank they did not choose.</summary>
    public void SendHello(PlayerClass chassis)
    {
        byte[] name = Encode(LocalName);
        Span<byte> p = stackalloc byte[3 + 32];
        p[0] = (byte)Msg.Hello;
        p[1] = (byte)chassis;
        p[2] = (byte)name.Length;
        name.CopyTo(p.Slice(3, name.Length));
        _net.Send(0, p.Slice(0, 3 + name.Length), reliable: true);
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
            if (seat >= 0 && seat < World.Players.Count) World.Players[seat].Away = true;
            // In the room (before the match) a leaver simply vanishes from the floor and stops
            // blocking the launch gate; mid-match the seat is held for a rejoin as before.
            _pickOfSeat.Remove(seat);
            Room?.Remove(seat);
            string name = _nameOfSeat.TryGetValue(seat, out var n) ? n : "A PLAYER";
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

        // Everything waiting, whatever it is. Drained fully every tick so a stall never
        // leaves a backlog to work through later.
        while (_net.TryReceive(out int from, out byte[] payload))
            Handle(from, payload);
        ReapDeparted();

        if (World is null) return;

        if (IsHost)
        {
            // The host's own hands go straight into the world; everyone else's arrived above.
            World.SetInput(0, localInput);

            if (++_sinceSnapshot >= TicksPerSnapshot)
            {
                _sinceSnapshot = 0;
                Broadcast();
            }
        }
        else if (LocalSeat >= 0)
        {
            // Unreliable, every tick. A dropped input frame is a sixtieth of a second of
            // one player's intent and the next one supersedes it — retransmitting would
            // arrive too late to matter and block everything behind it.
            Span<byte> p = stackalloc byte[1 + 4 + InputFrame.Size];
            p[0] = (byte)Msg.Input;
            BitConverter.TryWriteBytes(p.Slice(1, 4), _tick);
            localInput.Write(p.Slice(5, InputFrame.Size));
            _net.Send(0, p, reliable: false);

            // A client still drives its own craft locally so the controls feel attached to
            // something; the host's next packet is what settles where it actually is.
            World.SetInput(LocalSeat, localInput);
        }
    }

    /// <summary>
    /// Host-side: two packets per client — the players, then the field near that client. Both
    /// unreliable. The split is the fix for craft vanishing under load: the players packet is
    /// small and always fits a single datagram, so it arrives even when a busy field's packet
    /// is too big and drops.
    /// </summary>
    private void Broadcast()
    {
        // The players packet is the same for everyone, so it is written once.
        _out[0] = (byte)Msg.State;
        int np = Snapshot.WritePlayers(World!, _tick, _out.AsSpan(1));

        foreach (int peer in _net.Peers)
        {
            if (!_seatOfPeer.TryGetValue(peer, out int seat)) continue;
            _net.Send(peer, _out.AsSpan(0, np + 1), reliable: false);

            _field[0] = (byte)Msg.Field;
            int nf = Snapshot.WriteField(World!, seat, _tick, _field.AsSpan(1));
            _net.Send(peer, _field.AsSpan(0, nf + 1), reliable: false);

            // The bosses near this client, so their fight is seen and not only heard. Its own
            // small packet for the same reason the field is split out — a busy field must never
            // cost the boss its update.
            _bosses[0] = (byte)Msg.Bosses;
            int nb = Snapshot.WriteBosses(World!, seat, _tick, _bosses.AsSpan(1));
            _net.Send(peer, _bosses.AsSpan(0, nb + 1), reliable: false);

            // The sounds raised near this client since the last snapshot, so it hears the
            // fights around it. Its own cues rode local for the instant feel and are skipped on
            // the far end; everything else it plays positioned to its own craft.
            if (World!.SoundCues.Count > 0)
            {
                _sound[0] = (byte)Msg.Sound;
                int ns = WriteSounds(seat, _sound.AsSpan(1));
                _net.Send(peer, _sound.AsSpan(0, ns + 1), reliable: false);
            }
        }

        // The cues are spent once described to everyone — clear them whether or not anyone was
        // listening, so a host alone in a room never lets the list grow.
        World!.SoundCues.Clear();
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
                    _seatOfPeer[from] = kept;
                    _nameOfSeat[kept] = name;
                    World.Players[kept].Away = false;
                    Announce($"{name} REJOINED");
                    SendWelcome(from, kept);
                    if (MatchStarted) SendStartTo(from);
                    break;
                }

                // Otherwise a new player. A repeat hello from a peer already seated (its Welcome
                // was lost and it asked again) just gets another Welcome, not a second seat.
                if (_seatOfPeer.TryGetValue(from, out int have))
                {
                    SendWelcome(from, have);
                    break;
                }

                var craft = World.AddPlayer(new Loadout { Class = chassis });
                if (craft is null) return;   // match full — they stay out

                int seat = World.Seat(craft);
                _seatOfPeer[from] = seat;
                if (identity != 0) _seatOfIdentity[identity] = seat;
                _nameOfSeat[seat] = name;
                Announce($"{name} JOINED");
                SendWelcome(from, seat);
                // A player who arrives after LAUNCH is dropped straight into the running match.
                if (MatchStarted) SendStartTo(from);
                break;
            }

            case Msg.Welcome when !IsHost:
            {
                const int fixedLen = 2 + MatchSettings.Size + 12;
                if (payload.Length < fixedLen) return;
                int at = 1;
                LocalSeat = payload[at++];
                World.Match = MatchSettings.Read(payload.AsSpan(at, MatchSettings.Size));
                at += MatchSettings.Size;

                // Fill the roster out to our own seat with placeholders the host's snapshots
                // will overwrite, then install our own chosen craft — full build, not a
                // placeholder tank — at the seat we were actually given.
                while (World.Players.Count <= LocalSeat && World.AddPlayer() != null) { }
                if (LocalSeat < 0 || LocalSeat >= World.Players.Count) return;
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
                if (payload.Length < 5 + InputFrame.Size) return;
                if (!_seatOfPeer.TryGetValue(from, out int seat)) return;
                World.SetInput(seat, InputFrame.Read(payload.AsSpan(5, InputFrame.Size)));
                break;
            }

            case Msg.Start when !IsHost:
                MatchStarted = true;
                break;

            case Msg.State when !IsHost:
            {
                uint tick = Snapshot.ApplyPlayers(World, payload.AsSpan(1));
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
                if (payload.Length < 3) break;
                if (!_seatOfPeer.TryGetValue(from, out int seat)) break;
                ApplyPick(seat, (PlayerClass)payload[1]);
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
                _nameOfSeat[seat] = name;
                Room?.ApplyName(seat, name);
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

            case Msg.Rules when !IsHost:
                if (payload.Length >= 1 + MatchSettings.Size)
                    Room?.AdoptRules(MatchSettings.Read(payload.AsSpan(1, MatchSettings.Size)));
                break;

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
