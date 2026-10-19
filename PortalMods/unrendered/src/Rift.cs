using System.Globalization;
using System.Reflection;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Modding;
using GameWorld = Unrendered.World.World;

namespace UnrenderedPortal;

/// <summary>
/// THE RIFT, UNRENDERED side: shared state and the plumbing every part uses — the log, the
/// settings, the link to Minecraft, and the one reach past the mod API it needs (putting the
/// pilot's craft into the stand-in world Minecraft is drawn around; see <see cref="World"/>).
/// </summary>
public static class Rift
{
    public const string Version = "1.0.0";
    public const int Proto = 1;

    /// <summary>The mod's own folder: Mods/rift beside the game.</summary>
    public static string Dir { get; private set; } = ".";

    public static RiftConfig Cfg { get; private set; } = new();
    public static ModHost? Host { get; private set; }

    private static StreamWriter? _log;
    private static readonly object LogLock = new();

    public static void Log(string line)
    {
        lock (LogLock)
        {
            try
            {
                _log?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
                _log?.Flush();
            }
            catch { /* a log that cannot be written is not worth crashing a game over */ }
        }
    }

    public static void Boot(ModHost host)
    {
        Host = host;
        Dir = host.DataFolder;
        try { _log = new StreamWriter(Path.Combine(Dir, "rift.log"), append: false); }
        catch { _log = null; }
        Log($"THE RIFT {Version} — loaded as {host.File}, data in {Dir}");
        Cfg = RiftConfig.Load(Path.Combine(Dir, "portal.cfg"));
    }

    // --- The game, reached from outside ---------------------------------------------

    /// <summary>
    /// The running game. The mod API hands a mod the world but not the game, and one thing
    /// here needs the game: moving the pilot into the stand-in world (the game draws and steps
    /// whichever world it holds). The game gives the mod system a delegate that speaks into
    /// its feed; that delegate is bound to the game, so the game is found through it.
    /// </summary>
    private static object? Game
    {
        get
        {
            if (_game != null) return _game;
            var post = typeof(CodeMods).GetField("Post", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as Delegate;
            object? t = post?.Target;
            if (t != null && t is not Unrendered.Core.Game)
                t = t.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Select(f => f.GetValue(t)).FirstOrDefault(v => v is Unrendered.Core.Game);
            _game = t;
            if (_game == null) Log("game: not found — crossing into Minecraft is off; visitors still work");
            return _game;
        }
    }
    private static object? _game;

    private static readonly FieldInfo? WorldField =
        typeof(Unrendered.Core.Game).GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>The world the game is showing. Settable: the crossing swaps it.</summary>
    public static GameWorld? World
    {
        get => Game is { } g && WorldField != null ? WorldField.GetValue(g) as GameWorld : CodeMods.CurrentWorld;
        set
        {
            if (Game is { } g && WorldField != null) WorldField.SetValue(g, value);
            else Log("world: cannot swap — no game handle");
        }
    }

    public static bool CanSwap => Game != null && WorldField != null;

    /// <summary>True in a networked match. The rift is a solo affair: swapping the world
    /// under a session would desync every machine in it.</summary>
    public static bool InMatch => World?.Networked ?? false;

    /// <summary>A line in the feed, and large in the middle of the screen for a moment.</summary>
    public static void Notice(string text)
    {
        Log("NOTICE " + text);
        Hud.Toast(text);
        Host?.Notice(Font.Clean(text));
    }

    // --- The parts ------------------------------------------------------------------

    public static Link? Link { get; internal set; }

    /// <summary>The live link, if Minecraft is on the other end and has said hello.</summary>
    public static Link? Live => Link is { Welcomed: true, Closed: false } l ? l : null;

    /// <summary>Once a rendered frame of live play, outside any draw.</summary>
    internal static void Frame()
    {
        Discovery.Ensure();
        Link?.Pump();
        if (Link is { Closed: true } dead)
        {
            Log("link closed: " + dead.CloseReason);
            Notice("THE RIFT CLOSED — " + dead.CloseReason);
            Link = null;
            Realm.Abort("THE LINK WAS LOST");
            Visitors.Clear();
            Portal.Unlink();
        }

        TestHatch.Frame();
        Textures.Pump();

        if (Raylib.IsKeyPressed(Cfg.PortalKey)) PortalKey();
        Pocket.Keys();

        Visitors.SendScenes();
        Realm.BeforeFrame();
    }

    /// <summary>
    /// B. With a Minecraft world on the network it is JOIN WORLDS: connect, ask Minecraft
    /// for its half of the door, and open ours in front of the craft. Without one it is
    /// the test button — the portal opens anyway, glowstone and water, onto nothing.
    /// </summary>
    internal static void PortalKey()
    {
        if (InMatch)
        {
            Notice("THE RIFT ONLY OPENS IN A SOLO RUN");
            return;
        }
        if (Realm.Active)
        {
            Notice("YOU ARE ALREADY ON THE OTHER SIDE");
            return;
        }

        Portal.OpenAhead();

        if (Live is { } live)
        {
            live.Send("join");
            return;
        }

        if (Link == null && Discovery.Best() is { } found)
        {
            Notice($"JOINING WORLDS WITH {found.World}...");
            Link = Link.Connect(found.Address, found.Port, found.World);
        }
        else if (Link == null && Cfg.Connect is { } manual)
        {
            Notice($"JOINING WORLDS WITH {manual.Host}...");
            Link = Link.Connect(manual.Host, manual.Port, manual.Host);
        }
        else if (Link == null)
        {
            Notice("NO MINECRAFT WORLD IS OPEN ON YOUR NETWORK — THE WATER IS STILL");
        }
    }

    /// <summary>After every fixed step of the world on screen.</summary>
    internal static void Step(GameWorld world, float dt)
    {
        Portal.Tick(world, dt);
        Realm.Tick(world, dt);
        Visitors.Tick(world, dt);
        Background.Tick(dt);
    }

    internal static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
