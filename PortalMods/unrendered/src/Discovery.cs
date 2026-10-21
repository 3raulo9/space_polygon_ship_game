using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace UnrenderedPortal;

/// <summary>
/// Watches the network for Minecraft worlds. Two ears:
/// <list type="bullet">
/// <item>the rift's own beacon (UDP 4446), sent by worlds running the Fabric mod — these
/// can be joined;</item>
/// <item>vanilla Minecraft's LAN announcement (UDP 4445), sent by every world opened to
/// LAN — these can only be noticed, so the pilot can be told what is missing.</item>
/// </list>
/// </summary>
public static class Discovery
{
    public sealed record Found(string World, string Host, string Address, int Port, bool Rift, double Seen);

    private static readonly ConcurrentDictionary<string, Found> _found = new();
    private static bool _started;

    public const int RiftPort = 4446;
    public const int VanillaPort = 4445;
    private static readonly IPAddress RiftGroup = IPAddress.Parse("224.0.2.61");
    private static readonly IPAddress VanillaGroup = IPAddress.Parse("224.0.2.60");

    private static double Now => Environment.TickCount64 / 1000.0;

    public static void Ensure()
    {
        if (_started) return;
        _started = true;
        Listen(RiftPort, RiftGroup, OnRift);
        Listen(VanillaPort, VanillaGroup, OnVanilla);
    }

    private static void Listen(int port, IPAddress group, Action<string, IPEndPoint> handle)
    {
        new Thread(() =>
        {
            try
            {
                using var udp = new UdpClient();
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                try { udp.JoinMulticastGroup(group); }
                catch (Exception e) { Rift.Log($"discovery: no multicast on {port} ({e.Message}); broadcast only"); }
                Rift.Log($"discovery: listening on {port}");
                while (true)
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = udp.Receive(ref from);
                    try { handle(Encoding.UTF8.GetString(data), from); }
                    catch (Exception e) { Rift.Log("discovery: bad packet: " + e.Message); }
                }
            }
            catch (Exception e) { Rift.Log($"discovery {port}: {e.Message}"); }
        }) { IsBackground = true, Name = "rift-ear-" + port }.Start();
    }

    private static void OnRift(string text, IPEndPoint from)
    {
        const string Head = "UNRENDERED-RIFT/";
        if (!text.StartsWith(Head)) return;
        int brace = text.IndexOf('{');
        if (brace < 0 || JsonNode.Parse(text[brace..]) is not JsonObject o) return;
        int port = o["port"]?.GetValue<int>() ?? 25570;
        string world = o["world"]?.GetValue<string>() ?? "MINECRAFT";
        string host = o["host"]?.GetValue<string>() ?? "";
        string addr = from.Address.ToString();
        bool fresh = !_found.ContainsKey(addr + ":rift");
        _found[addr + ":rift"] = new Found(world, host, addr, port, true, Now);
        if (fresh) Rift.Log($"discovery: rift world '{world}' by {host} at {addr}:{port}");
    }

    private static void OnVanilla(string text, IPEndPoint from)
    {
        // [MOTD]Steve - New World[/MOTD][AD]51234[/AD]
        string motd = Between(text, "[MOTD]", "[/MOTD]") ?? "MINECRAFT";
        if (!int.TryParse(Between(text, "[AD]", "[/AD]"), out int port)) return;
        string addr = from.Address.ToString();
        _found[addr + ":lan"] = new Found(motd, "", addr, port, false, Now);
    }

    private static string? Between(string s, string a, string b)
    {
        int i = s.IndexOf(a, StringComparison.Ordinal);
        if (i < 0) return null;
        i += a.Length;
        int j = s.IndexOf(b, i, StringComparison.Ordinal);
        return j < 0 ? null : s[i..j];
    }

    /// <summary>Every world heard in the last few seconds, rift or not.</summary>
    public static IReadOnlyList<Found> All()
        => _found.Values.Where(f => Now - f.Seen < 6).GroupBy(f => f.World + ":" + f.Port + f.Rift)
            .Select(g => g.OrderByDescending(f => f.Seen).First()).ToList();

    /// <summary>The freshest joinable world, if any has been heard in the last few seconds.</summary>
    public static Found? Best()
        => _found.Values.Where(f => f.Rift && Now - f.Seen < 6).OrderByDescending(f => f.Seen).FirstOrDefault();

    /// <summary>A world open to LAN that has no rift on it — worth a line saying so.</summary>
    public static Found? Vanilla()
        => _found.Values.Where(f => !f.Rift && Now - f.Seen < 6
                && !_found.Values.Any(r => r.Rift && r.Address == f.Address && Now - r.Seen < 6))
            .OrderByDescending(f => f.Seen).FirstOrDefault();
}
