using UnrenderedPortal;
using GameWorld = Unrendered.World.World;

namespace UnrenderedPortal;

/// <summary>
/// What the rift offers a dimension-hopping mod (the Dimension Hopper finds this class by
/// name; nothing here depends on it): the Minecraft worlds on the network as realms, whether
/// the connection to each is clear, a way to establish one, and a way through once it is.
/// Each realm is <c>[id, name, game, clear ("1"/"0"), status]</c>.
/// </summary>
public static class HopperRealms
{
    public static IReadOnlyList<string[]> List()
    {
        Discovery.Ensure();
        var list = new List<string[]>();
        var live = Rift.Live;
        if (live != null)
            list.Add(new[] { "mc:" + live.Target, live.WorldName.ToUpperInvariant(), "MINECRAFT", "1",
                "CONNECTION CLEAR" + (live.HostName.Length > 0 ? " - " + live.HostName.ToUpperInvariant() : "") });
        foreach (var f in Discovery.All())
        {
            string id = "mc:" + f.Address + ":" + f.Port;
            if (list.Any(e => e[0] == id) || (live != null && live.Target == f.Address + ":" + f.Port)) continue;
            if (f.Rift)
            {
                bool trying = Rift.Link is { Closed: false, Welcomed: false } l && l.Target == f.Address + ":" + f.Port;
                list.Add(new[] { id, f.World.ToUpperInvariant(), "MINECRAFT", "0",
                    trying ? "REACHING ACROSS..." : "SIGNAL FOUND - NO CONNECTION YET" });
            }
            else
                list.Add(new[] { id, f.World.ToUpperInvariant(), "MINECRAFT", "0",
                    "NO RIFT ON THIS WORLD - IT CANNOT ANSWER" });
        }
        return list;
    }

    /// <summary>Reach for the realm: join worlds with it, as JOIN WORLDS does.</summary>
    public static string Connect(string id)
    {
        if (Rift.InMatch) return "SOLO RUNS ONLY";
        if (Rift.Live is { } live && "mc:" + live.Target == id) return "CONNECTION CLEAR";
        var f = Discovery.All().FirstOrDefault(x => "mc:" + x.Address + ":" + x.Port == id);
        if (f == null) return "THE SIGNAL IS GONE";
        if (!f.Rift) return "NO RIFT ON THIS WORLD - IT CANNOT ANSWER";
        if (Rift.Link is { Closed: false } open && open.Target != f.Address + ":" + f.Port) open.Close("another realm called");
        if (Rift.Link is not { Closed: false })
            Rift.Link = Link.Connect(f.Address, f.Port, f.World);
        return "REACHING ACROSS...";
    }

    /// <summary>Through a door at (x, z) facing <paramref name="facing"/>: into the realm if
    /// the connection is clear. Comes home out of the same door.</summary>
    public static string Enter(string id, float x, float z, float facing)
    {
        if (Rift.Live is not { } live || "mc:" + live.Target != id) return "ESTABLISH A CLEAR CONNECTION WITH THIS REALM";
        if (Realm.Active) return "ALREADY THERE";
        if (Rift.World is not GameWorld home) return "NO WORLD";
        Realm.ReturnAt = (new System.Numerics.Vector2(x, z), facing);
        Realm.Enter(home);
        return "THROUGH";
    }

    /// <summary>How many from another realm are standing in UNRENDERED right now.</summary>
    public static int Guests => Visitors.All.Count;

    /// <summary>True while the craft is standing in another realm.</summary>
    public static bool Away => Realm.Active;
}
