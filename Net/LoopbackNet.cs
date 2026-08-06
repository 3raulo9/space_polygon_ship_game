namespace Unrendered.Net;

/// <summary>
/// How bad the pretend wire is. Everything is in fixed steps rather than milliseconds,
/// because the sim's clock is the only clock the loopback has — one step is 1/60s, so
/// six steps is a hundred milliseconds each way.
/// </summary>
public readonly record struct LinkQuality(int LatencySteps, int JitterSteps, float LossPercent)
{
    /// <summary>A perfect wire: same-step delivery, nothing lost. What the first netcode
    /// test should use, so a failure is a bug in the game rather than in the network.</summary>
    public static readonly LinkQuality Perfect = new(0, 0, 0f);

    /// <summary>Roughly a hundred milliseconds each way with a little jitter and the odd
    /// packet gone — an ordinary domestic connection between two people in one country.</summary>
    public static readonly LinkQuality Typical = new(6, 2, 2f);

    /// <summary>A quarter of a second, lumpy, one packet in twenty gone. If the game is
    /// playable across this it is playable.</summary>
    public static readonly LinkQuality Awful = new(15, 6, 5f);
}

/// <summary>
/// Every peer in a session, in this process, joined by a wire whose latency and packet
/// loss you choose.
///
/// This is the rig nearly all the multiplayer work gets done against. A desync becomes a
/// breakpoint with both worlds live in the debugger instead of a phone call to somebody
/// else's house, and "does this survive 250ms and 5% loss" becomes a check that runs on
/// every build rather than a thing discovered after release.
///
/// It is deterministic on purpose: the loss and jitter come from a seeded generator, not
/// <c>Random.Shared</c>, so a test that fails fails the same way next time. That is worth
/// insisting on — the sim itself is full of <c>Random.Shared</c> and cannot make the same
/// promise, which is exactly why the host owns the simulation and the clients are told
/// what happened rather than working it out for themselves.
/// </summary>
public sealed class LoopbackNet
{
    private readonly Endpoint[] _ends;
    private readonly LinkQuality _quality;
    private readonly Random _rng;
    private long _step;

    /// <summary>A payload in flight, with the step it is due to land on.</summary>
    private readonly record struct InFlight(long DueStep, int From, byte[] Payload);

    public LoopbackNet(int peerCount, LinkQuality quality = default, int seed = 12345)
    {
        if (peerCount < 1) throw new ArgumentOutOfRangeException(nameof(peerCount));
        _quality = quality;
        _rng = new Random(seed);
        _ends = new Endpoint[peerCount];
        for (int i = 0; i < peerCount; i++) _ends[i] = new Endpoint(this, i, peerCount);
    }

    /// <summary>The transport belonging to one peer. Hand it to that peer's session and it
    /// cannot tell the difference between this and Steam.</summary>
    public INetTransport this[int peer] => _ends[peer];

    /// <summary>Test hook: makes <paramref name="atEndpoint"/> see <paramref name="peer"/>
    /// drop, so a disconnect can be exercised without a real socket.</summary>
    public void DropPeer(int atEndpoint, int peer) => _ends[atEndpoint].SimulateDeparture(peer);

    /// <summary>
    /// Test hook: everything <paramref name="peer"/> sends goes nowhere while this is set —
    /// reliable messages included.
    ///
    /// This is not "a lossy wire", which the quality settings already model. It is the state a
    /// real P2P connection is in for its first moment of life: <c>ConnectP2P</c> hands back a
    /// handle immediately and Steam is still punching through to the other machine, so anything
    /// pushed into it in that window has no route and no retransmit will ever save it. The
    /// joining handshake has to survive that on its own.
    /// </summary>
    private readonly HashSet<int> _muted = new();
    public void Mute(int peer, bool muted)
    {
        if (muted) _muted.Add(peer); else _muted.Remove(peer);
    }

    /// <summary>Test hook: the reconnection half — a real transport re-adds the peer on a
    /// fresh connection, which this stands in for.</summary>
    public void Readmit(int atEndpoint, int peer) => _ends[atEndpoint].Readmit(peer);

    /// <summary>Total payloads dropped so far, across the whole rig. Handy for asserting a
    /// test actually exercised the loss it asked for.</summary>
    public int Dropped { get; private set; }

    /// <summary>
    /// Advances the shared clock one fixed step. Call it once per simulated tick, before
    /// the peers pump — a payload sent on step N with zero latency is readable on step N.
    /// </summary>
    public void Advance() => _step++;

    private void Dispatch(int from, int to, ReadOnlySpan<byte> payload, bool reliable)
    {
        if (_muted.Contains(from)) { Dropped++; return; }
        Endpoint dst = _ends[to];

        // Unreliable payloads are allowed to vanish. Reliable ones never do — a real
        // reliable channel retransmits, which the receiver experiences as a late packet
        // rather than a missing one, so that is what is modelled here.
        bool lost = !reliable && _rng.NextDouble() * 100.0 < _quality.LossPercent;
        int delay = _quality.LatencySteps;
        if (lost)
        {
            Dropped++;
            return;
        }
        if (reliable && _rng.NextDouble() * 100.0 < _quality.LossPercent)
            delay += _quality.LatencySteps + 1;   // the retransmit costs another round trip

        if (_quality.JitterSteps > 0)
            delay += _rng.Next(0, _quality.JitterSteps + 1);

        // .ToArray() because the caller owns its span and will reuse the buffer; the wire
        // has to take a copy exactly as a real socket would.
        dst.Deliver(new InFlight(_step + delay, from, payload.ToArray()), reliable);
    }

    /// <summary>One peer's end of the wire.</summary>
    private sealed class Endpoint : INetTransport
    {
        private readonly LoopbackNet _net;
        private readonly List<InFlight> _pending = new();
        private readonly Queue<InFlight> _ready = new();
        private readonly List<int> _peers;

        public Endpoint(LoopbackNet net, int local, int peerCount)
        {
            _net = net;
            LocalPeer = local;
            _peers = Enumerable.Range(0, peerCount).Where(p => p != local).ToList();
        }

        public int LocalPeer { get; }
        public IReadOnlyList<int> Peers => _peers;

        private readonly Queue<int> _departed = new();

        /// <summary>In the loopback a peer's identity is simply its id — stable, which is all
        /// the rejoin logic needs; the tests drive reconnection at the session level.</summary>
        public long IdentityOf(int peer) => peer + 1;   // +1 so nobody is identity 0 ("unknown")

        public bool TryTakeDeparted(out int peer)
        {
            if (_departed.Count > 0) { peer = _departed.Dequeue(); return true; }
            peer = -1;
            return false;
        }

        /// <summary>Test hook: drops a peer from this endpoint's view and queues the departure,
        /// so a test can exercise a disconnect without a real socket.</summary>
        public void SimulateDeparture(int peer)
        {
            if (_peers.Remove(peer)) _departed.Enqueue(peer);
        }

        public void Readmit(int peer)
        {
            if (!_peers.Contains(peer)) _peers.Add(peer);
        }

        public void Send(int peer, ReadOnlySpan<byte> payload, bool reliable)
            => _net.Dispatch(LocalPeer, peer, payload, reliable);

        public void Broadcast(ReadOnlySpan<byte> payload, bool reliable)
        {
            foreach (int p in _peers) _net.Dispatch(LocalPeer, p, payload, reliable);
        }

        public void Deliver(InFlight msg, bool reliable)
        {
            // A reliable channel is ordered, so a message that jittered ahead of one sent
            // before it has to wait its turn. Pushing its due step out to match keeps the
            // queue monotonic without a sequence number the loopback doesn't need.
            if (reliable)
            {
                long last = 0;
                for (int i = 0; i < _pending.Count; i++)
                    if (_pending[i].From == msg.From && _pending[i].DueStep > last)
                        last = _pending[i].DueStep;
                if (last >= msg.DueStep) msg = msg with { DueStep = last + 1 };
            }
            _pending.Add(msg);
        }

        public void Pump()
        {
            // Forward order, compacting in place: everything due this step is released in
            // the order it was sent. Walking backwards and removing would have been shorter
            // and would have handed the reliable channel its messages inside out.
            int keep = 0;
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].DueStep <= _net._step) _ready.Enqueue(_pending[i]);
                else _pending[keep++] = _pending[i];
            }
            _pending.RemoveRange(keep, _pending.Count - keep);
        }

        public bool TryReceive(out int from, out byte[] payload)
        {
            if (_ready.Count == 0)
            {
                from = -1;
                payload = [];
                return false;
            }
            InFlight m = _ready.Dequeue();
            from = m.From;
            payload = m.Payload;
            return true;
        }
    }
}
