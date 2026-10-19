using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace UnrenderedPortal;

/// <summary>One message off the wire: its JSON line, and for a binary frame the inflated
/// payload that followed it.</summary>
public sealed record Msg(string T, JsonObject J, byte[]? Bin = null)
{
    public string Str(string k, string d = "") => J[k]?.GetValue<string>() ?? d;
    public double Num(string k, double d = 0) => J[k] is JsonValue v && v.TryGetValue(out double x) ? x : d;
    public float F(string k, float d = 0) => (float)Num(k, d);
    public int I(string k, int d = 0) => (int)Num(k, d);
    public bool B(string k) => J[k] is JsonValue v && v.TryGetValue(out bool b) && b;
}

/// <summary>
/// The TCP link to a Minecraft world running the rift mod. JSON lines and DEFLATEd
/// binary frames (see PROTOCOL.md). Reading and writing each have their own thread; the
/// game only ever sees whole messages, handed over in <see cref="Pump"/> on its own thread.
/// </summary>
public sealed class Link
{
    private readonly TcpClient _tcp = new() { NoDelay = true };
    private NetworkStream? _stream;
    private readonly ConcurrentQueue<Msg> _in = new();
    private readonly BlockingCollection<byte[]> _out = new(new ConcurrentQueue<byte[]>());
    private volatile bool _closed;
    private long _lastHeard = Environment.TickCount64;

    public string Target { get; }
    public bool Welcomed { get; private set; }
    public bool Closed => _closed;
    public string CloseReason { get; private set; } = "";
    public string WorldName { get; private set; } = "";
    public string HostName { get; private set; } = "";

    /// <summary>Bytes waiting to go out. Scenes are skipped while this is high, so a slow
    /// link shows an older picture rather than an ever-growing delay.</summary>
    public int Backlog => _out.Count;

    public static Link Connect(string host, int port, string label)
    {
        var l = new Link(host + ":" + port);
        new Thread(() => l.Run(host, port)) { IsBackground = true, Name = "rift-read" }.Start();
        return l;
    }

    private Link(string target) => Target = target;

    private void Run(string host, int port)
    {
        try
        {
            _tcp.Connect(host, port);
            _stream = _tcp.GetStream();
            new Thread(WriteLoop) { IsBackground = true, Name = "rift-write" }.Start();
            new Thread(KeepAlive) { IsBackground = true, Name = "rift-ping" }.Start();
            Send("hello", "proto", Rift.Proto, "name", Rift.Cfg.Name,
                "chassis", Rift.World?.Player.Class.ToString().ToUpperInvariant() ?? "TANK",
                "version", Rift.Version, "game", "UNRENDERED");
            ReadLoop(_stream);
        }
        catch (Exception e) { Close(e is SocketException se ? se.SocketErrorCode.ToString() : e.Message); }
    }

    private void ReadLoop(Stream s)
    {
        var line = new MemoryStream();
        var buf = new byte[1];
        while (!_closed)
        {
            line.SetLength(0);
            while (true)
            {
                int b = s.ReadByte();
                if (b < 0) { Close("MINECRAFT HUNG UP"); return; }
                if (b == '\n') break;
                line.WriteByte((byte)b);
                if (line.Length > 64 << 20) { Close("A LINE TOO LONG TO BE TRUE"); return; }
            }
            Interlocked.Exchange(ref _lastHeard, Environment.TickCount64);
            if (line.Length == 0) continue;
            var json = JsonNode.Parse(Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length)) as JsonObject;
            if (json == null) continue;
            string t = json["t"]?.GetValue<string>() ?? "";
            if (t == "bin")
            {
                int n = json["n"]?.GetValue<int>() ?? 0;
                var raw = new byte[n];
                s.ReadExactly(raw, 0, n);
                string kind = json["k"]?.GetValue<string>() ?? "";
                _in.Enqueue(new Msg(kind, json, Inflate(raw)));
            }
            else _in.Enqueue(new Msg(t, json));
        }
    }

    private void WriteLoop()
    {
        try
        {
            foreach (var chunk in _out.GetConsumingEnumerable())
            {
                if (_closed || _stream == null) break;
                _stream.Write(chunk);
            }
        }
        catch (Exception e) { Close(e.Message); }
    }

    /// <summary>Pings on its own clock, and gives up on a silent Minecraft — off the game's
    /// thread, so a link stays up while the game sits on a menu or a pause panel.</summary>
    private void KeepAlive()
    {
        while (!_closed)
        {
            Thread.Sleep(2000);
            Send("ping");
            if (Environment.TickCount64 - Interlocked.Read(ref _lastHeard) > 15000) Close("MINECRAFT STOPPED ANSWERING");
        }
    }

    public void Close(string why)
    {
        if (_closed) return;
        CloseReason = why.ToUpperInvariant();
        _closed = true;
        try { _out.CompleteAdding(); } catch { }
        try { _tcp.Close(); } catch { }
    }

    // --- Sending -----------------------------------------------------------------------

    /// <summary>Sends <c>{"t": t, k1: v1, k2: v2, …}</c>.</summary>
    public void Send(string t, params object?[] kv) => Send(Obj(t, kv));

    public void Send(JsonObject o)
    {
        if (_closed) return;
        try { _out.Add(Encoding.UTF8.GetBytes(o.ToJsonString() + "\n")); }
        catch (InvalidOperationException) { }
    }

    /// <summary>A binary frame. <paramref name="droppable"/> frames (scenes, crafts) are
    /// simply not sent while the link is behind.</summary>
    public bool SendBin(string kind, ReadOnlySpan<byte> payload, bool droppable, params object?[] kv)
    {
        if (_closed) return false;
        if (droppable && _out.Count > 6) return false;
        byte[] z = Deflate(payload);
        var head = Obj("bin", kv);
        head["k"] = kind;
        head["n"] = z.Length;
        try
        {
            _out.Add(Encoding.UTF8.GetBytes(head.ToJsonString() + "\n"));
            _out.Add(z);
        }
        catch (InvalidOperationException) { return false; }
        return true;
    }

    public static JsonObject Obj(string t, params object?[] kv)
    {
        var o = new JsonObject { ["t"] = t };
        for (int i = 0; i + 1 < kv.Length; i += 2)
            o[(string)kv[i]!] = Node(kv[i + 1]);
        return o;
    }

    public static JsonNode? Node(object? v) => v switch
    {
        null => null,
        JsonNode n => n.DeepClone(),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        float f => JsonValue.Create(float.IsFinite(f) ? f : 0f),
        double d => JsonValue.Create(double.IsFinite(d) ? d : 0d),
        _ => JsonValue.Create(v.ToString()),
    };

    // --- Receiving ---------------------------------------------------------------------

    /// <summary>Hands every message that arrived since the last frame to whoever handles
    /// its type. On the game's own thread, so handlers may touch the world freely.</summary>
    public void Pump()
    {
        int budget = 256;
        while (budget-- > 0 && _in.TryDequeue(out var m))
        {
            try { Dispatch(m); }
            catch (Exception e) { Rift.Log($"dispatch {m.T}: {e}"); }
        }
    }

    private void Dispatch(Msg m)
    {
        switch (m.T)
        {
            case "ping": return;
            case "welcome":
                Welcomed = true;
                WorldName = m.Str("world");
                HostName = m.Str("host");
                Rift.Log($"welcomed by {WorldName} ({HostName}, mc {m.Str("mc")})");
                Rift.Notice($"WORLDS JOINED: {WorldName.ToUpperInvariant()}");
                if (m.J["portal"] is JsonObject p) Portal.LinkTo(p);
                Textures.Relink();
                Crossings.Relink();
                // The join that started all this: ask Minecraft for its half of the door.
                if (Portal.Open) Send("join");
                return;
            case "refuse":
                Rift.Notice("MINECRAFT REFUSED THE RIFT: " + m.Str("reason").ToUpperInvariant());
                Close("REFUSED");
                return;
            case "bye":
                Close(m.Str("reason", "MINECRAFT CLOSED THE RIFT"));
                return;
            case "chat":
                Rift.Notice(m.Str("text"));
                return;
            case "portal":
                Portal.LinkTo(m.J);
                return;
            case "texdata":
                Textures.Arrived(m);
                return;
        }

        if (Realm.Handle(m)) return;
        if (Overlays.Handle(m)) return;
        if (Crossings.Handle(m)) return;
        if (Visitors.Handle(m)) return;
        if (Pocket.Handle(m)) return;
    }

    // --- DEFLATE ------------------------------------------------------------------------

    public static byte[] Deflate(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            z.Write(data);
        return ms.ToArray();
    }

    public static byte[] Inflate(byte[] data)
    {
        using var z = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        z.CopyTo(ms);
        return ms.ToArray();
    }
}
