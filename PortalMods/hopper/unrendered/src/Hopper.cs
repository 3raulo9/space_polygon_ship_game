using System.Numerics;
using System.Reflection;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Modding;
using GameWorld = Unrendered.World.World;

namespace DimensionHopper;

/// <summary>
/// DIMENSION HOPPER: a gun that tears a door to somewhere else. H brings up its dial — every
/// world it can reach — and J fires: a bolt of green flies out and a swirling door opens where
/// it lands. Walk through. UNRENDERED's five worlds are always on the dial; with the glowstone
/// rift installed, so are the Minecraft worlds it can hear, each of which has to answer before
/// the gun will open onto it.
/// </summary>
public sealed class HopperMod : IGameMod
{
    public string Id => "dimension-hopper";
    public string Name => "DIMENSION HOPPER";
    public string Version => Hopper.Version;
    public string Author => "DIMENSION HOPPER";
    public string Description => "H: the dial. J: fire. A green door to any world it can reach.";

    public void Load(ModHost host) => Hopper.Host = host;

    public void WorldClosed(GameWorld world) => Hopper.Closed(world);

    public void Step(GameWorld world, float dt, in InputFrame input) => Hopper.Guard(() => Hopper.Step(world));

    public void Frame(GameWorld world, float dt) => Hopper.Guard(() => Hopper.Frame(world));

    public void Draw3D(GameWorld world, ModScene scene) => Hopper.Guard(() => Hopper.Draw3D(world, scene));

    public void DrawHud(GameWorld world, ModScene scene) => Hopper.Guard(() => Dial.Draw(world));

    public string? Command(GameWorld? world, string line)
    {
        string l = line.Trim().Trim('{', '}').Trim().ToLowerInvariant();
        if (!l.StartsWith("hopper")) return null;
        return world == null ? "NO WORLD" : $"HOPPER SET TO {Hopper.DestinationName(world)}";
    }
}

public static class Hopper
{
    public const string Version = "1.0.0";
    internal static ModHost? Host;

    /// <summary>Where the next door opens onto: an id from <see cref="Realms.All"/>.</summary>
    public static string? Destination;
    internal static readonly List<Doorway> Doors = new();
    internal static double FiredAt = -100, RaisedUntil = -100;
    private static (Vector3 From, Vector3 To, double At, string Dest)? _bolt;
    private static GameWorld? _world;

    private static double Now => Raylib.GetTime();
    public static bool Raised => Dial.Open || Now < RaisedUntil;

    internal static void Log(string s) => Host?.Log(s);
    internal static void Notice(string s) => Host?.Notice(s);

    internal static void Guard(Action a)
    {
        try { a(); }
        catch (Exception e) { Log("error: " + e); }
    }

    internal static void Closed(GameWorld w)
    {
        if (ReferenceEquals(w, _world)) { Doors.Clear(); _bolt = null; }
    }

    public static string DestinationName(GameWorld w)
        => Realms.All(w).FirstOrDefault(e => e.Id == Destination)?.Name ?? "NOWHERE";

    // --- Keys ----------------------------------------------------------------------------

    internal static void Frame(GameWorld w)
    {
        if (!ReferenceEquals(w, _world)) { _world = w; Doors.Clear(); _bolt = null; }
        TestHatch.Frame(w);
        if (Raylib.IsKeyPressed(KeyboardKey.H)) { if (Dial.Open) Dial.Close(); else Dial.Show(w); }
        if (Dial.Open) { Dial.Keys(w); return; }
        if (Raylib.IsKeyPressed(KeyboardKey.J)) Fire(w);
    }

    /// <summary>J with the dial down: a bolt out of the muzzle, and a door where it lands.</summary>
    public static void Fire(GameWorld w)
    {
        RaisedUntil = Now + 4;
        if (Destination == null) { Notice("SET A DESTINATION FIRST - H"); Dial.Show(w); return; }
        var dest = Realms.All(w).FirstOrDefault(e => e.Id == Destination);
        if (dest == null) { Notice("THAT REALM HAS GONE QUIET"); Destination = null; return; }
        if (!dest.Clear) { Notice("ESTABLISH A CLEAR CONNECTION WITH THIS REALM"); Dial.Show(w); return; }
        if (dest.Here) { Notice("YOU ARE ALREADY HERE"); return; }

        var craft = w.Player;
        FiredAt = Now;
        Vector2 fwd = craft.Forward;
        Vector2 land = Torus.Wrap(craft.Position + fwd * 9f);
        var from = new Vector3(craft.Position.X, craft.Height + craft.EyeHeight - 0.25f, craft.Position.Y) + new Vector3(fwd.X, 0, fwd.Y) * 0.8f;
        _bolt = (from, new Vector3(land.X, Doorway.CenterY, land.Y), Now, dest.Id);
        // Its one-a-gun rule: the old door shuts when a new one is shot.
        foreach (var d in Doors.Where(d => !d.Arrival)) d.Life = Math.Min(d.Life, Now - d.Opened + 0.6);
        CueBank.Play(Cue.ThrowWhoosh, craft.Position, 0f);
    }

    // --- Each step -----------------------------------------------------------------------

    internal static void Step(GameWorld w)
    {
        double now = Now;
        if (_bolt is { } b && now - b.At > 0.22)
        {
            _bolt = null;
            var at = new Vector2(b.To.X, b.To.Z);
            float facing = w.Player.Heading + MathF.PI;   // its face looks back at the shooter
            Doors.Add(new Doorway { At = at, Facing = facing, Destination = b.Dest, Opened = now });
            CueBank.Play(Cue.PortalEnter, at, 0f);
            Log($"a door to {b.Dest} opened at {at.X:0.0},{at.Y:0.0}");
        }
        Doors.RemoveAll(d => d.Done(now));

        foreach (var d in Doors)
        {
            if (!d.Takes(w.Player.Position, now)) continue;
            Through(w, d);
            break;
        }
    }

    private static void Through(GameWorld w, Doorway d)
    {
        d.Life = Math.Min(d.Life, Now - d.Opened + 0.6);
        if (d.Destination.StartsWith("planet:") && Enum.TryParse<PlanetId>(d.Destination[7..], out var planet))
        {
            Hop(w, planet, d);
            return;
        }
        string said = Realms.Enter(d.Destination, d.At.X, d.At.Y, d.Facing);
        Log($"into {d.Destination}: {said}");
        if (said != "THROUGH") Notice(said);
    }

    /// <summary>
    /// To another of UNRENDERED's worlds, the way the game's own arches cross: the next planet
    /// is built and the craft carried onto it whole — hull, shields, reserve, magazine, lives and
    /// the entire pack — coming out of the same spot (every world shares the city's layout) with
    /// the door still open behind it.
    /// </summary>
    private static void Hop(GameWorld old, PlanetId to, Doorway d)
    {
        if (old.Networked) { Notice("THE DOOR WON'T HOLD IN A SHARED MATCH"); return; }
        if (Realms.Guests > 0) { Notice("SOMEONE FROM ANOTHER REALM IS STANDING HERE - THEY WOULD BE LEFT BEHIND"); return; }
        if (Realms.Away) { Notice("COME HOME FIRST"); return; }
        if (GameField == null || Game is not { } game) { Notice("THE GUN CAN'T FIND THIS WORLD'S EDGES"); return; }

        var rules = old.Match.Clamped();
        rules.Destination = to;
        var next = new GameWorld(old.Loadout, rules, old.Session);
        next.CarryOverFrom(old);
        var craft = next.Player;
        Vector2 n = new(MathF.Sin(d.Facing), MathF.Cos(d.Facing));
        craft.Position = Torus.Wrap(d.At - n * 1.6f);
        craft.Heading = d.Facing + MathF.PI;
        GameField.SetValue(game, next);
        _world = next;
        Doors.Clear();
        Doors.Add(new Doorway { At = d.At, Facing = d.Facing, Destination = "", Opened = Now, Life = 3.5, Arrival = true });
        Notice($"{Planet.Get(to).Name}");
        CueBank.Play(Cue.PortalEnter, craft.Position, 0f);
        Log($"hopped to {Planet.Get(to).Name}");
    }

    private static readonly FieldInfo? GameField =
        typeof(Unrendered.Core.Game).GetField("_world", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>The game itself. The mod API hands a mod the world, not the game; the game is
    /// found through the feed delegate it gives the mod system, which is bound to it.</summary>
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
            return _game = t;
        }
    }
    private static object? _game;

    // --- Drawing ---------------------------------------------------------------------------

    internal static void Draw3D(GameWorld w, ModScene scene)
    {
        double now = Now;
        foreach (var d in Doors) d.Draw(scene.Eye, now);
        if (_bolt is { } b)
        {
            float t = (float)Math.Clamp((now - b.At) / 0.22, 0, 1);
            var p = Vector3.Lerp(b.From, b.To, t);
            Raylib.DrawSphere(p, 0.18f, Gun.Glow);
            Raylib.DrawSphere(p, 0.09f, Color.White);
        }
        if (Raised)
        {
            float kick = (float)Math.Clamp(1 - (now - FiredAt) / 0.3, 0, 1);
            float raise = (float)Math.Clamp((RaisedUntil - now) / 0.3, 0, 1);
            if (Dial.Open) raise = 1;
            Gun.DrawHeld(scene.Camera, kick, kick, raise);
        }
    }
}
