using System.Runtime.InteropServices;
using Steamworks;

namespace VoidTanks.Net;

/// <summary>
/// The real wire: Steam's peer-to-peer sockets behind the same interface the loopback rig
/// implements, so everything above this line has already been tested without it.
///
/// <para><b>No lobby.</b> A lobby exists to let strangers find each other and to carry Steam
/// invites, and neither applies yet: on the shared test App ID 480 an invite launches the
/// real Spacewar app rather than this game, so invites cannot work until there is a paid App
/// ID. What is left is two people who can tell each other a code, and for that the code can
/// simply <em>be</em> the host — <see cref="LocalCode"/> encodes their Steam account and
/// <see cref="Connect"/> dials it. Steam's relay network does the rest: no lobby callbacks,
/// no IP addresses, no port forwarding, and nobody's address on screen.</para>
///
/// <para>Peers are the small integers the session deals in. The host is always 0; joiners are
/// numbered as they arrive.</para>
/// </summary>
public sealed class SteamNet : INetTransport, IDisposable
{
    /// <summary>Every code is these characters. No vowels, so a code can never come out as a
    /// word, and no 0/O/1/I, which are the pairs people mistype when reading one aloud.</summary>
    private const string Alphabet = "23456789BCDFGHJKLMNPQRSTVWXYZ";

    private const int VirtualPort = 0;

    private static bool _started;

    /// <summary>True once Steam is up and this process can host or join.</summary>
    public static bool Available { get; private set; }

    /// <summary>Why Steam is unavailable, for the lobby screen to show rather than failing
    /// silently — almost always "the client isn't running" or "steam_api64.dll is missing".</summary>
    public static string? Trouble { get; private set; }

    /// <summary>
    /// Brings Steam up. Safe to call more than once and safe to fail: the game runs fine
    /// without it, single-player never touches this, and the multiplayer screen shows
    /// <see cref="Trouble"/> instead of a working code.
    /// </summary>
    public static bool Start()
    {
        if (_started) return Available;
        _started = true;
        try
        {
            if (!SteamAPI.Init())
            {
                Trouble = "STEAM NOT RUNNING";
                return false;
            }
            // Warms the relay network up now rather than costing a second on first connect.
            SteamNetworkingUtils.InitRelayNetworkAccess();
            Available = true;
            return true;
        }
        catch (DllNotFoundException)
        {
            Trouble = "STEAM_API64.DLL MISSING";
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            // The DLL loaded but does not export something the wrapper expects, which only
            // ever means the two came from different Steamworks SDKs. Valve's partner site
            // always serves the newest SDK while the C# wrapper trails it by a release or
            // two, so pairing them by hand is the normal way to get here — the fix is to
            // take steam_api64.dll from the Steamworks.NET release itself, where the two are
            // shipped together. Worth naming outright: the raw exception says "unable to
            // find an entry point", which tells nobody anything.
            Trouble = "STEAM SDK VERSION MISMATCH — SEE STEAMWORKS.NET RELEASE";
            return false;
        }
        catch (Exception e)
        {
            Trouble = e.Message.ToUpperInvariant();
            return false;
        }
    }

    public static void Stop()
    {
        if (Available) SteamAPI.Shutdown();
        Available = false;
        _started = false;
    }

    /// <summary>Steam's callbacks. Pumped once per rendered frame from the loop.</summary>
    public static void RunCallbacks()
    {
        if (Available) SteamAPI.RunCallbacks();
    }

    /// <summary>
    /// This machine's join code — the seven characters a host reads out. It is the account
    /// half of their Steam ID in <see cref="Alphabet"/>: the other half is the same constant
    /// for every individual account on Steam, so sending it would be seven wasted characters
    /// of something the other end already knows.
    /// </summary>
    public static string LocalCode => Available ? Encode(SteamUser.GetSteamID().GetAccountID().m_AccountID) : "--------";

    /// <summary>Turns an account number into a code. Internal rather than private so the
    /// self-test can prove it round-trips: a code that does not decode back to the account it
    /// came from means nobody can ever connect, and that has to be caught without Steam.</summary>
    internal static string Encode(uint id)
    {
        Span<char> outp = stackalloc char[7];
        for (int i = 6; i >= 0; i--)
        {
            outp[i] = Alphabet[(int)(id % (uint)Alphabet.Length)];
            id /= (uint)Alphabet.Length;
        }
        return new string(outp);
    }

    /// <summary>Turns a typed code back into a Steam account, or null if it is not one.
    /// Case and stray spaces are forgiven; an unknown character is not.</summary>
    public static CSteamID? Decode(string code)
    {
        string c = code.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");
        if (c.Length != 7) return null;
        uint id = 0;
        foreach (char ch in c)
        {
            int v = Alphabet.IndexOf(ch);
            if (v < 0) return null;
            id = id * (uint)Alphabet.Length + (uint)v;
        }
        return new CSteamID(new AccountID_t(id), EUniverse.k_EUniversePublic,
                            EAccountType.k_EAccountTypeIndividual);
    }

    // --- The instance ------------------------------------------------------------

    private readonly bool _isHost;
    private HSteamListenSocket _listen;
    private HSteamNetPollGroup _poll;
    private readonly Dictionary<int, HSteamNetConnection> _conns = new();
    private readonly Dictionary<HSteamNetConnection, int> _peerOf = new();
    private readonly List<int> _peers = new();
    private readonly Queue<(int From, byte[] Payload)> _inbox = new();
    private Callback<SteamNetConnectionStatusChangedCallback_t>? _statusCb;
    private int _nextPeer = 1;
    private IntPtr _send = IntPtr.Zero;
    private int _sendCap;

    public int LocalPeer { get; }
    public IReadOnlyList<int> Peers => _peers;

    /// <summary>True on a client once the host has actually accepted it.</summary>
    public bool Connected => _isHost || _peers.Count > 0;

    /// <summary>Set when the connection dies, so the lobby screen can say why.</summary>
    public string? Dropped { get; private set; }

    private SteamNet(bool host)
    {
        _isHost = host;
        LocalPeer = host ? 0 : 1;
        _poll = SteamNetworkingSockets.CreatePollGroup();
        _statusCb = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatusChanged);
    }

    /// <summary>Opens this machine up to joiners. The code to hand out is
    /// <see cref="LocalCode"/>.</summary>
    public static SteamNet? Host()
    {
        if (!Available) return null;
        var net = new SteamNet(host: true);
        net._listen = SteamNetworkingSockets.CreateListenSocketP2P(VirtualPort, 0, null);
        return net;
    }

    /// <summary>Dials a host by their code. Returns null if the code is not a code; a
    /// connection that is refused or never answers surfaces through
    /// <see cref="Dropped"/>.</summary>
    public static SteamNet? Connect(string code)
    {
        if (!Available) return null;
        if (Decode(code) is not { } hostId) return null;

        var net = new SteamNet(host: false);
        var identity = new SteamNetworkingIdentity();
        identity.SetSteamID(hostId);
        HSteamNetConnection conn = SteamNetworkingSockets.ConnectP2P(ref identity, VirtualPort, 0, null);
        net.Bind(0, conn);
        return net;
    }

    private void Bind(int peer, HSteamNetConnection conn)
    {
        _conns[peer] = conn;
        _peerOf[conn] = peer;
        if (!_peers.Contains(peer)) _peers.Add(peer);
        SteamNetworkingSockets.SetConnectionPollGroup(conn, _poll);
    }

    private void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t e)
    {
        switch (e.m_info.m_eState)
        {
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                // Only a host has anything to accept; a client's outgoing connection reaches
                // this state too and must not try to accept itself.
                if (_isHost)
                {
                    if (SteamNetworkingSockets.AcceptConnection(e.m_hConn) == EResult.k_EResultOK)
                        Bind(_nextPeer++, e.m_hConn);
                    else
                        SteamNetworkingSockets.CloseConnection(e.m_hConn, 0, null, false);
                }
                break;

            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                if (_peerOf.TryGetValue(e.m_hConn, out int gone))
                {
                    _peers.Remove(gone);
                    _conns.Remove(gone);
                    _peerOf.Remove(e.m_hConn);
                }
                Dropped = e.m_info.m_szEndDebug is { Length: > 0 } why
                    ? why.ToUpperInvariant() : "CONNECTION LOST";
                SteamNetworkingSockets.CloseConnection(e.m_hConn, 0, null, false);
                break;
        }
    }

    public void Send(int peer, ReadOnlySpan<byte> payload, bool reliable)
    {
        if (!_conns.TryGetValue(peer, out HSteamNetConnection conn)) return;

        // One reusable unmanaged buffer: Steam wants a pointer, and allocating per packet at
        // sixty packets a second per peer would be a lot of garbage for no reason.
        if (payload.Length > _sendCap)
        {
            if (_send != IntPtr.Zero) Marshal.FreeHGlobal(_send);
            _sendCap = Math.Max(payload.Length, 2048);
            _send = Marshal.AllocHGlobal(_sendCap);
        }
        Marshal.Copy(payload.ToArray(), 0, _send, payload.Length);

        int flags = reliable
            ? Constants.k_nSteamNetworkingSend_Reliable
            : Constants.k_nSteamNetworkingSend_Unreliable;
        SteamNetworkingSockets.SendMessageToConnection(conn, _send, (uint)payload.Length, flags, out _);
    }

    public void Broadcast(ReadOnlySpan<byte> payload, bool reliable)
    {
        foreach (int p in _peers) Send(p, payload, reliable);
    }

    public void Pump()
    {
        if (!Available) return;

        var msgs = new IntPtr[32];
        int got = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(_poll, msgs, msgs.Length);
        for (int i = 0; i < got; i++)
        {
            var m = Marshal.PtrToStructure<SteamNetworkingMessage_t>(msgs[i]);
            var bytes = new byte[m.m_cbSize];
            Marshal.Copy(m.m_pData, bytes, 0, m.m_cbSize);
            int from = _peerOf.TryGetValue(m.m_conn, out int p) ? p : 0;
            _inbox.Enqueue((from, bytes));
            SteamNetworkingMessage_t.Release(msgs[i]);
        }
    }

    public bool TryReceive(out int from, out byte[] payload)
    {
        if (_inbox.Count == 0)
        {
            from = -1;
            payload = [];
            return false;
        }
        (from, payload) = _inbox.Dequeue();
        return true;
    }

    public void Dispose()
    {
        foreach (var c in _conns.Values)
            SteamNetworkingSockets.CloseConnection(c, 0, null, false);
        _conns.Clear();
        _peerOf.Clear();
        _peers.Clear();

        if (_listen != HSteamListenSocket.Invalid)
            SteamNetworkingSockets.CloseListenSocket(_listen);
        if (_poll != HSteamNetPollGroup.Invalid)
            SteamNetworkingSockets.DestroyPollGroup(_poll);
        if (_send != IntPtr.Zero) Marshal.FreeHGlobal(_send);
        _send = IntPtr.Zero;
        _statusCb?.Dispose();
        _statusCb = null;
    }
}
