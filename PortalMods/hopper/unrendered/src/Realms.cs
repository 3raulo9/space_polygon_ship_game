using System.Reflection;
using Unrendered.Core;
using GameWorld = Unrendered.World.World;

namespace DimensionHopper;

/// <summary>
/// Where the gun can go. UNRENDERED's own five worlds always; other games' realms when a mod
/// that reaches them is installed. Such a mod is found by name — a public static class
/// <c>HopperRealms</c> with <c>List()</c>, <c>Connect(id)</c> and <c>Enter(id, x, z, facing)</c>
/// (the glowstone rift has one) — so this mod depends on nothing but the game.
/// </summary>
public static class Realms
{
    public sealed record Entry(string Id, string Name, string Game, bool Clear, string Status, bool Here, PlanetId? Planet);

    private static Type? _bridge;
    private static double _looked = -100;

    /// <summary>Another game's realms are within reach (a bridge mod is installed).</summary>
    public static bool Bridged => Bridge() != null;

    private static Type? Bridge()
    {
        double now = Raylib_cs.Raylib.GetTime();
        if (_bridge == null && now - _looked > 2)
        {
            _looked = now;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                Type?[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }   // some of a game's own refuse; skip them
                var t = types.FirstOrDefault(x => x is { Name: "HopperRealms", IsAbstract: true, IsSealed: true }
                    && x.GetMethod("List", Type.EmptyTypes) != null);
                if (t != null) { _bridge = t; break; }
            }
        }
        return _bridge;
    }

    public static List<Entry> All(GameWorld w)
    {
        var list = new List<Entry>();
        foreach (var p in Planet.All)
            list.Add(new Entry("planet:" + p.Id, p.Name, "UNRENDERED", true,
                p.Id == w.Match.Destination ? "YOU ARE HERE" : "OPEN", p.Id == w.Match.Destination, p.Id));
        if (Bridge() is { } b && Call(b, "List") is IEnumerable<string[]> realms)
            foreach (var r in realms)
                if (r.Length >= 5) list.Add(new Entry(r[0], r[1], r[2], r[3] == "1", r[4], false, null));
        return list;
    }

    public static string Connect(string id) => Bridge() is { } b ? Call(b, "Connect", id) as string ?? "" : "NO WAY ACROSS";

    public static string Enter(string id, float x, float z, float facing)
        => Bridge() is { } b ? Call(b, "Enter", id, x, z, facing) as string ?? "" : "NO WAY ACROSS";

    /// <summary>Somebody from another realm is standing in UNRENDERED.</summary>
    public static int Guests => Bridge() is { } b && b.GetProperty("Guests")?.GetValue(null) is int n ? n : 0;

    /// <summary>The craft is standing in another realm right now.</summary>
    public static bool Away => Bridge() is { } b && b.GetProperty("Away")?.GetValue(null) is true;

    private static object? Call(Type t, string name, params object[] args)
    {
        try
        {
            return t.GetMethod(name, args.Select(a => a.GetType()).ToArray())?.Invoke(null, args);
        }
        catch (TargetInvocationException e)
        {
            Hopper.Log($"{name}: {e.InnerException?.Message}");
            return null;
        }
    }
}
