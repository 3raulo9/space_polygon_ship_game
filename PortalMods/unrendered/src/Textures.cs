using System.IO.Compression;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace UnrenderedPortal;

/// <summary>
/// Minecraft's own textures, wherever they can honestly come from:
/// <list type="number">
/// <item>over the link, from the Minecraft that is running (which means resource packs and
/// modded blocks look the way that player sees them);</item>
/// <item>before any world is linked, out of the Minecraft install on this machine — the
/// version jar that already sits in .minecraft/versions;</item>
/// <item>failing both, Minecraft's own answer to a texture it cannot find: the magenta and
/// black checker.</item>
/// </list>
/// Nothing of Mojang's is shipped with the mod.
/// </summary>
public static class Textures
{
    public sealed class Tex
    {
        public required string Id;
        public Texture2D Gpu;
        public Image Cpu;          // the whole strip, kept for the atlas and for animation
        public int W, H;           // one frame
        public int Frames = 1;
        public float FrameTime = 0.05f;
        public bool Missing;
        public Color Average;
    }

    private static readonly Dictionary<string, Tex> _cache = new();
    private static readonly HashSet<string> _asked = new();
    private static readonly HashSet<string> _localTried = new();
    private static readonly List<string> _pending = new();

    /// <summary>Raised when a texture finishes arriving, so the terrain atlas can paint its slot.</summary>
    public static event Action<Tex>? Loaded;

    public static Tex? Get(string id)
    {
        if (_cache.TryGetValue(id, out var t)) return t;
        if (_localTried.Add(id) && TryLocal(id) is { } local) return local;
        // Only counted as asked once there is someone to ask: a texture wanted before the link
        // was up is asked for again the moment it is.
        if (Rift.Live != null && _asked.Add(id)) _pending.Add(id);
        return null;
    }

    /// <summary>A texture, or the checker while it is on its way.</summary>
    public static Tex Or(string id) => Get(id) ?? Missing;

    /// <summary>Batches the outstanding requests into one message a frame.</summary>
    public static void Pump()
    {
        if (_pending.Count == 0 || Rift.Live is not { } live) return;
        var ids = new System.Text.Json.Nodes.JsonArray();
        foreach (var id in _pending.Take(64)) ids.Add(id);
        _pending.RemoveRange(0, Math.Min(64, _pending.Count));
        live.Send("tex", "ids", ids);
    }

    /// <summary>The link is new: anything that was asked for and never came, ask again.</summary>
    public static void Relink()
    {
        foreach (var id in _asked)
            if (!_cache.ContainsKey(id) || _cache[id].Missing) _pending.Add(id);
    }

    internal static void Arrived(Msg m)
    {
        string id = m.Str("id");
        string? b64 = m.J["png"]?.GetValue<string>();
        float ft = Math.Max(1, m.F("frametime", 1)) / 20f;
        if (b64 == null)
        {
            _cache[id] = MakeMissing(id);
            return;
        }
        var tex = FromPng(id, Convert.FromBase64String(b64), ft);
        if (tex == null) return;
        if (_cache.TryGetValue(id, out var old) && !old.Missing && !ReferenceEquals(old, MissingTex))
            Unload(old);
        _cache[id] = tex;
        Loaded?.Invoke(tex);
    }

    public static Tex? FromPng(string id, byte[] png, float frameTime = 0.05f)
    {
        Image img = Raylib.LoadImageFromMemory(".png", png);
        if (img.Width <= 0) return null;
        Raylib.ImageFormat(ref img, PixelFormat.UncompressedR8G8B8A8);
        int w = img.Width, h = img.Height;
        // Animated textures are a vertical strip of square frames (water, lava, fire, the
        // portal). Entity skins are 64x64 or 64x32 and are not strips.
        int frames = id.Contains("/block/") || id.Contains("/item/") ? Math.Max(1, h / w) : 1;
        int fh = frames > 1 ? w : h;
        var t = new Tex
        {
            Id = id, Cpu = img, W = w, H = fh, Frames = frames, FrameTime = frameTime,
        };
        Image first = Raylib.ImageFromImage(img, new Rectangle(0, 0, w, fh));
        t.Gpu = Raylib.LoadTextureFromImage(first);
        t.Average = AverageOf(first);
        Raylib.UnloadImage(first);
        Raylib.SetTextureFilter(t.Gpu, TextureFilter.Point);
        Raylib.SetTextureWrap(t.Gpu, TextureWrap.Clamp);
        return t;
    }

    private static unsafe Color AverageOf(Image img)
    {
        long r = 0, g = 0, b = 0, n = 0;
        byte* p = (byte*)img.Data;
        for (int i = 0; i < img.Width * img.Height; i++, p += 4)
        {
            if (p[3] < 32) continue;
            r += p[0]; g += p[1]; b += p[2]; n++;
        }
        return n == 0 ? new Color(128, 128, 128, 255)
            : new Color((int)(r / n), (int)(g / n), (int)(b / n), 255);
    }

    private static void Unload(Tex t)
    {
        Raylib.UnloadTexture(t.Gpu);
        Raylib.UnloadImage(t.Cpu);
    }

    // --- Minecraft's own missing texture ----------------------------------------------

    private static Tex? MissingTex;
    public static Tex Missing => MissingTex ??= MakeMissing("missing");

    private static Tex MakeMissing(string id)
    {
        Image img = Raylib.GenImageChecked(16, 16, 8, 8, new Color(248, 0, 248, 255), Color.Black);
        var t = new Tex { Id = id, Cpu = img, W = 16, H = 16, Missing = true, Average = new Color(124, 0, 124, 255) };
        t.Gpu = Raylib.LoadTextureFromImage(img);
        Raylib.SetTextureFilter(t.Gpu, TextureFilter.Point);
        return t;
    }

    // --- The local Minecraft install ------------------------------------------------

    private static ZipArchive? _jar;
    private static bool _jarLooked;

    private static Tex? TryLocal(string id)
    {
        // minecraft:textures/block/glowstone.png -> assets/minecraft/textures/block/glowstone.png
        int colon = id.IndexOf(':');
        if (colon < 0) return null;
        string entry = $"assets/{id[..colon]}/{id[(colon + 1)..]}";
        var jar = Jar();
        var e = jar?.GetEntry(entry);
        if (e == null) return null;
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        float ft = 0.05f;
        var meta = jar!.GetEntry(entry + ".mcmeta");
        if (meta != null)
        {
            using var r = new StreamReader(meta.Open());
            string json = r.ReadToEnd();
            int at = json.IndexOf("\"frametime\"", StringComparison.Ordinal);
            if (at >= 0)
            {
                string digits = new(json[(json.IndexOf(':', at) + 1)..].TakeWhile(c => char.IsWhiteSpace(c) || char.IsDigit(c)).ToArray());
                if (int.TryParse(digits.Trim(), out int ticks)) ft = ticks / 20f;
            }
        }
        var t = FromPng(id, ms.ToArray(), ft);
        if (t != null)
        {
            _cache[id] = t;
            Loaded?.Invoke(t);
        }
        return t;
    }

    private static ZipArchive? Jar()
    {
        if (_jarLooked) return _jar;
        _jarLooked = true;
        foreach (string dir in MinecraftDirs())
        {
            string versions = Path.Combine(dir, "versions");
            if (!Directory.Exists(versions)) continue;
            // 1.21.1 first, as that is what the rift was built against; any release after.
            string? jar = Path.Combine(versions, "1.21.1", "1.21.1.jar");
            if (!File.Exists(jar))
                jar = Directory.GetDirectories(versions)
                    .Select(v => Path.Combine(v, Path.GetFileName(v) + ".jar"))
                    .Where(File.Exists)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            if (jar == null) continue;
            try
            {
                _jar = ZipFile.OpenRead(jar);
                Rift.Log("textures: using " + jar);
                return _jar;
            }
            catch (Exception e) { Rift.Log($"textures: {jar}: {e.Message}"); }
        }
        Rift.Log("textures: no Minecraft install found; the portal wears the missing texture until a world is linked");
        return null;
    }

    private static IEnumerable<string> MinecraftDirs()
    {
        if (Rift.Cfg.MinecraftDir is { } cfg) yield return cfg;
        string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(appdata, ".minecraft");
        yield return Path.Combine(home, ".minecraft");
        yield return Path.Combine(home, "Library", "Application Support", "minecraft");
    }

    /// <summary>The frame an animated texture is on right now.</summary>
    public static int FrameOf(Tex t, double time)
        => t.Frames <= 1 ? 0 : (int)(time / t.FrameTime) % t.Frames;

    /// <summary>The raw RGBA of one frame of a strip, for pushing into an atlas slot.</summary>
    public static unsafe ReadOnlySpan<byte> FramePixels(Tex t, int frame)
        => new((byte*)t.Cpu.Data + (long)frame * t.W * t.H * 4, t.W * t.H * 4);
}
