using VoidTanks.Core;

namespace VoidTanks.Net;

/// <summary>What kind of packet this is. One byte, first in every payload.</summary>
public enum Msg : byte
{
    /// <summary>Client → host, on connecting: which chassis they picked.</summary>
    Hello = 1,
    /// <summary>Host → client: your seat number and the rules of the match.</summary>
    Welcome = 2,
    /// <summary>Client → host, every tick: what their hands are doing.</summary>
    Input = 3,
    /// <summary>Host → client, at <see cref="Session.SnapshotHz"/>: the world.</summary>
    State = 4,
    /// <summary>Host → client: the host pressed LAUNCH, come in.</summary>
    Start = 5,
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
    }

    /// <summary>The most recent tick a client has heard about, so a packet that overtook a
    /// newer one on the wire is dropped rather than snapping the world backwards.</summary>
    public uint LastAppliedTick { get; private set; }

    /// <summary>Peers the host has seated, keyed by transport peer id. Host-side only.</summary>
    private readonly Dictionary<int, int> _seatOfPeer = new();

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
        LocalSeat = 0;
    }

    /// <summary>
    /// Client-side: adopts the world the host has described. The seat is not known until the
    /// Welcome arrives, so the world is held with a placeholder roster until then.
    /// </summary>
    public void JoinMatch(World.World world) => World = world;

    /// <summary>Client-side: announces the chassis this player picked. Reliable — a lost
    /// hello would seat somebody as a tank they did not choose.</summary>
    public void SendHello(PlayerClass chassis)
    {
        Span<byte> p = stackalloc byte[2];
        p[0] = (byte)Msg.Hello;
        p[1] = (byte)chassis;
        _net.Send(0, p, reliable: true);
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

    /// <summary>Host-side: one packet per client, each describing what is near that client.</summary>
    private void Broadcast()
    {
        foreach (int peer in _net.Peers)
        {
            if (!_seatOfPeer.TryGetValue(peer, out int seat)) continue;
            _out[0] = (byte)Msg.State;
            int n = Snapshot.Write(World!, seat, _tick, _out.AsSpan(1));
            _net.Send(peer, _out.AsSpan(0, n + 1), reliable: false);
        }
    }

    private void Handle(int from, byte[] payload)
    {
        if (payload.Length < 1 || World is null) return;

        switch ((Msg)payload[0])
        {
            case Msg.Hello when IsHost:
            {
                if (payload.Length < 2) return;
                if (_seatOfPeer.ContainsKey(from)) return;      // already seated; ignore repeats

                var chassis = (PlayerClass)payload[1];
                var loadout = new Loadout();
                loadout.Class = chassis;
                var craft = World.AddPlayer(loadout);
                if (craft is null) return;                       // match full — they stay out

                int seat = World.Seat(craft);
                _seatOfPeer[from] = seat;

                Span<byte> w = stackalloc byte[2 + MatchSettings.Size];
                w[0] = (byte)Msg.Welcome;
                w[1] = (byte)seat;
                World.Match.Write(w.Slice(2, MatchSettings.Size));
                _net.Send(from, w, reliable: true);
                break;
            }

            case Msg.Welcome when !IsHost:
            {
                if (payload.Length < 2 + MatchSettings.Size) return;
                LocalSeat = payload[1];
                World.Match = MatchSettings.Read(payload.AsSpan(2, MatchSettings.Size));

                // Fill the roster out to our own seat so the snapshot has somewhere to put
                // everyone. The host is the authority on who is actually in them; these are
                // placeholders that its next packet overwrites.
                while (World.Players.Count <= LocalSeat && World.AddPlayer() != null) { }
                World.LocalIndex = Math.Clamp(LocalSeat, 0, World.Players.Count - 1);
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
                uint tick = Snapshot.Apply(World, payload.AsSpan(1));
                // An older packet that overtook a newer one is worse than no packet at all.
                if (tick != 0 && tick > LastAppliedTick) LastAppliedTick = tick;
                break;
            }
        }
    }
}
