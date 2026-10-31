using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Raylib_cs;
using Unrendered.Core;

namespace Unrendered.Modding;

/// <summary>
/// The code half of the mods folder: every <c>.dll</c> in it that holds an
/// <see cref="IGameMod"/>, loaded once and called from the game's loop.
///
/// <para><b>Loaded, never unloaded.</b> .NET cannot take code back out of a running process
/// that the rest of the game has started calling into, so a code mod stays for the session.
/// The MODS page's RELOAD picks up a file that is new since boot; one that was changed or
/// removed needs a restart, and the page says which files those are rather than pretending.
/// Read into memory rather than from the file, so the file itself is never locked and can be
/// swapped while the game runs.</para>
///
/// <para><b>Never fatal.</b> Everything a mod does is called through <see cref="Each"/>, which
/// catches. A mod that throws is logged; one that keeps throwing is switched off for the
/// session with a line in the feed, because a game that dies of somebody else's bug is a game
/// whose player blames the game.</para>
///
/// <para>A <c>.dll</c> with no mod in it is listed as failed, so dropping the wrong file in is
/// visible. A library a mod needs goes in <c>Mods/lib/</c>, which is not scanned for mods but
/// is searched when a mod asks for an assembly the game does not have.</para>
/// </summary>
public static class CodeMods
{
    private sealed class Entry(IGameMod mod, ModHost host, ModInfo info)
    {
        public readonly IGameMod Mod = mod;
        public readonly ModHost Host = host;
        public readonly ModInfo Info = info;
        public int Faults;
        public bool Off;
    }

    /// <summary>How many throws a mod gets before it is switched off. Enough that one bad
    /// frame is forgiven; few enough that a hook throwing sixty times a second is stopped
    /// inside a second rather than filling the log for an hour.</summary>
    private const int FaultLimit = 30;

    private static readonly List<Entry> _entries = new();

    /// <summary>Every file looked at so far, by full path, and what it became. A file is read
    /// once per session; a later scan reports it as it was.</summary>
    private static readonly Dictionary<string, ModInfo> _files = new(StringComparer.OrdinalIgnoreCase);

    private static bool _resolverHooked;

    /// <summary>Every code mod that loaded and is still switched on.</summary>
    public static IEnumerable<IGameMod> Active => _entries.Where(e => !e.Off).Select(e => e.Mod);

    /// <summary>The world on screen, as of the last frame — what <see cref="ModHost.World"/>
    /// answers with.</summary>
    public static World.World? CurrentWorld { get; private set; }

    /// <summary>Where a mod's lines go. Set by the game to whichever feed is live.</summary>
    internal static Action<string, Net.NoticeFeed.Kind>? Post;

    /// <summary>True while any mod has an overlay up that owns the mouse.</summary>
    public static bool HoldsMouse => _entries.Any(e => !e.Off && e.Host.HoldsMouse);

    // --- Loading ----------------------------------------------------------------------------

    /// <summary>
    /// Looks through <paramref name="folder"/> for <c>.dll</c> files, loads any not seen
    /// before, and adds what became of every one of them to <paramref name="into"/>. Called
    /// by <see cref="Mods.Reload"/>, so the .json and the .dll mods are listed together.
    /// </summary>
    internal static void Scan(string folder, List<ModInfo> into)
    {
        HookResolver(folder);

        string[] files;
        try { files = Directory.GetFiles(folder, "*.dll"); }
        catch (Exception e)
        {
            Console.WriteLine($"[mods] could not list code mods in {folder}: {e.Message}");
            return;
        }

        foreach (string path in files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            string full = Path.GetFullPath(path);
            if (!_files.TryGetValue(full, out ModInfo? info))
            {
                info = Load(full);
                _files[full] = info;
            }
            into.Add(info);
        }
    }

    private static ModInfo Load(string path)
    {
        string name = Path.GetFileName(path);
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception e) { return ModInfo.Failed(name, "COULD NOT READ: " + e.Message); }

        Assembly asm;
        try
        {
            // Symbols too, when they sit beside it, so a mod's stack traces name its lines.
            string pdb = Path.ChangeExtension(path, ".pdb");
            using var code = new MemoryStream(bytes);
            using var symbols = File.Exists(pdb) ? new MemoryStream(File.ReadAllBytes(pdb)) : null;
            asm = AssemblyLoadContext.Default.LoadFromStream(code, symbols);
        }
        catch (BadImageFormatException) { return ModInfo.Failed(name, "NOT A .NET ASSEMBLY"); }
        catch (FileLoadException e) { return ModInfo.Failed(name, "COULD NOT LOAD: " + e.Message); }
        catch (Exception e) { return ModInfo.Failed(name, "COULD NOT LOAD: " + e.Message); }

        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException e)
        {
            // Nearly always a mod built against a different version of the game: a type or a
            // member it was compiled to call is not in this one.
            string why = e.LoaderExceptions.FirstOrDefault(x => x != null)?.Message ?? e.Message;
            return ModInfo.Failed(name, "BUILT FOR A DIFFERENT GAME: " + why);
        }

        Type? type = types.FirstOrDefault(t =>
            typeof(IGameMod).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false }
            && t.GetConstructor(Type.EmptyTypes) != null);
        if (type == null)
            return ModInfo.Failed(name, "NO MOD IN THIS FILE (A LIBRARY GOES IN MODS/LIB)");

        IGameMod mod;
        try { mod = (IGameMod)Activator.CreateInstance(type)!; }
        catch (Exception e) { return ModInfo.Failed(name, "COULD NOT START: " + Inner(e).Message); }

        string id = (mod.Id ?? "").Trim().ToLowerInvariant();
        if (!Mods.IsId(id)) return ModInfo.Failed(name, "ID MAY ONLY USE a-z 0-9 _ -");
        if (_entries.Any(x => x.Info.Id == id))
            return ModInfo.Failed(name, $"ANOTHER FILE IS ALREADY \"{id}\"");

        var host = new ModHost(id, name);
        try { mod.Load(host); }
        catch (Exception e) { return ModInfo.Failed(name, "FAILED TO LOAD: " + Inner(e).Message); }

        var info = new ModInfo
        {
            File = name,
            Id = id,
            Name = Mods.Upper(mod.Name ?? id, 24),
            Version = Mods.Upper(mod.Version ?? "", 10),
            Author = Mods.Upper(mod.Author ?? "", 24),
            Description = Mods.Upper(mod.Description ?? "", 160),
            Code = true,
            Digest = Convert.ToHexString(SHA256.HashData(bytes), 0, 8),
        };
        _entries.Add(new Entry(mod, host, info));
        return info;
    }

    /// <summary>Lets a mod's own dependencies come from Mods/lib/ (or Mods/ itself) when the
    /// game does not carry them.</summary>
    private static void HookResolver(string folder)
    {
        if (_resolverHooked) return;
        _resolverHooked = true;
        AssemblyLoadContext.Default.Resolving += (ctx, wanted) =>
        {
            foreach (string dir in new[] { Path.Combine(folder, "lib"), folder })
            {
                string candidate = Path.Combine(dir, wanted.Name + ".dll");
                if (!File.Exists(candidate)) continue;
                try { return ctx.LoadFromStream(new MemoryStream(File.ReadAllBytes(candidate))); }
                catch { /* try the next place */ }
            }
            return null;
        };
    }

    private static Exception Inner(Exception e)
        => e is TargetInvocationException { InnerException: { } inner } ? inner : e;

    // --- Calling ----------------------------------------------------------------------------

    private static void Each(string hook, Action<IGameMod> call)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            Entry e = _entries[i];
            if (e.Off) continue;
            try { call(e.Mod); }
            catch (Exception ex) { Fault(e, hook, ex); }
        }
    }

    private static void Fault(Entry e, string hook, Exception ex)
    {
        e.Faults++;
        if (e.Faults <= 3)
            Console.WriteLine($"[mods] {e.Info.File} threw in {hook}: {ex}");
        if (e.Faults < FaultLimit) return;
        e.Off = true;
        e.Host.HoldsMouse = false;
        Console.WriteLine($"[mods] {e.Info.File} switched off after {e.Faults} errors");
        Post?.Invoke($"{e.Info.Name} STOPPED: IT KEPT FAILING", Net.NoticeFeed.Kind.Notice);
    }

    /// <summary>
    /// Notices which world is on screen, once per frame, and tells every mod when it
    /// changes. Watching rather than being told at each of the dozen places the game swaps a
    /// world means a new way of swapping one can never forget to say so.
    /// </summary>
    internal static void Track(World.World? world)
    {
        if (ReferenceEquals(world, CurrentWorld)) return;
        World.World? old = CurrentWorld;
        CurrentWorld = world;
        if (old != null) Each(nameof(IGameMod.WorldClosed), m => m.WorldClosed(old));
        if (world != null) Each(nameof(IGameMod.WorldOpened), m => m.WorldOpened(world));
    }

    internal static void Step(World.World world, float dt, in InputFrame input)
    {
        if (_entries.Count == 0) return;
        InputFrame frame = input;
        Each(nameof(IGameMod.Step), m => m.Step(world, dt, frame));
    }

    internal static void Frame(World.World world, float dt)
    {
        if (_entries.Count == 0) return;
        Each(nameof(IGameMod.Frame), m => m.Frame(world, dt));
    }

    private static readonly ModScene _scene = new();

    /// <summary>Builds the frame's scene from the game's camera and lets every mod at it.</summary>
    internal static ModScene BeforeDraw(World.World world, Camera3D camera)
    {
        _scene.Reset(camera);
        if (_entries.Count > 0) Each(nameof(IGameMod.BeforeDraw), m => m.BeforeDraw(world, _scene));
        return _scene;
    }

    internal static void Draw3D(World.World world, ModScene scene)
    {
        if (_entries.Count == 0) return;
        Each(nameof(IGameMod.Draw3D), m => m.Draw3D(world, scene));
    }

    internal static void DrawHud(World.World world, ModScene scene)
    {
        if (_entries.Count == 0) return;
        Each(nameof(IGameMod.DrawHud), m => m.DrawHud(world, scene));
    }

    /// <summary>Offers a chat line to each mod in turn. The first answer wins.</summary>
    internal static string? Command(World.World? world, string line)
    {
        string? reply = null;
        Each(nameof(IGameMod.Command), m => reply ??= m.Command(world, line));
        return reply;
    }
}
