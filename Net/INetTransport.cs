namespace VoidTanks.Net;

/// <summary>
/// A way to get bytes to the other machines in a session, and nothing else.
///
/// Deliberately the narrowest interface that can carry the game: no lobbies, no identity,
/// no notion of who is hosting. All of that is Steam's business and lives in the layer
/// above. What sits below this is either Steam's P2P sockets or, for every test worth
/// writing, a queue in this process with a fake wire in it — see <see cref="LoopbackNet"/>.
///
/// Peers are small integers, not SteamIDs. The session assigns them (0 is always the host)
/// and the transport maps them onto whatever it actually addresses. That keeps SteamID out
/// of the simulation and out of every test.
/// </summary>
public interface INetTransport
{
    /// <summary>This machine's slot in the session. 0 is the host.</summary>
    int LocalPeer { get; }

    /// <summary>Everyone currently reachable, not counting <see cref="LocalPeer"/>.</summary>
    IReadOnlyList<int> Peers { get; }

    /// <summary>
    /// Queues a payload for one peer. <paramref name="reliable"/> costs ordering and
    /// retransmission, so it is for things that would break the game if they went missing —
    /// a spawn, a death, a chassis choice. Per-tick input and world snapshots are
    /// emphatically not that: they are superseded by the next one 16ms later, and a
    /// reliable stream of them would head-of-line block the moment a packet dropped.
    /// </summary>
    void Send(int peer, ReadOnlySpan<byte> payload, bool reliable);

    /// <summary>The same, to everyone.</summary>
    void Broadcast(ReadOnlySpan<byte> payload, bool reliable);

    /// <summary>
    /// Pushes whatever is queued for one peer onto the wire now.
    ///
    /// A transport is entitled to hold a small send back for a few milliseconds hoping another
    /// will follow, so the two can share a datagram — and for the run of packets a snapshot
    /// writes back to back that is exactly the right thing to do. What it cannot know is when
    /// the run has ended, so the last one of a batch sits there waiting for company that will
    /// not arrive until the next snapshot. This says: that was the last one.
    ///
    /// Doing nothing is a correct implementation — a transport with no such buffer (the
    /// loopback) has nothing to push — which is why it is defaulted here rather than forced on
    /// everything that implements the interface.
    /// </summary>
    void Flush(int peer) { }

    /// <summary>
    /// Takes the next payload that has arrived, or returns false when there are none
    /// left this tick. Drain it in a loop; the transport never blocks.
    /// </summary>
    bool TryReceive(out int from, out byte[] payload);

    /// <summary>
    /// Lets the transport do its own housekeeping — Steam's callbacks, the loopback's
    /// clock. Called once per fixed step, before anything drains the queue.
    /// </summary>
    void Pump();

    /// <summary>
    /// A stable identity for a peer that survives a disconnect and reconnect — the Steam
    /// account behind them. The transport peer id is reused freely and means nothing across a
    /// drop; this does not, which is what lets a rejoining player be recognised as the same
    /// person and handed their seat back. Zero when unknown (the loopback, or a peer not yet
    /// fully connected).
    /// </summary>
    long IdentityOf(int peer);

    /// <summary>
    /// Takes the next peer that has dropped since the last call, or false when there are
    /// none. Host-side this is how a seat learns its player left. Drain it in a loop.
    /// </summary>
    bool TryTakeDeparted(out int peer);
}
