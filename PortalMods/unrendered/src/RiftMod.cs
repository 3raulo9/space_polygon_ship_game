using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Modding;
using GameWorld = Unrendered.World.World;

namespace UnrenderedPortal;

/// <summary>
/// THE RIFT, as UNRENDERED sees it: one code mod in one file. Everything the rift does on
/// this side happens from these hooks; the game calls them from its own loop.
/// </summary>
public sealed class RiftMod : IGameMod
{
    public string Id => "rift";
    public string Name => "THE RIFT";
    public string Version => Rift.Version;
    public string Author => "THE RIFT";
    public string Description => "A glowstone portal to Minecraft. Press B in a solo run. "
        + "Minecraft players come through as themselves; you go through as yourself.";

    public void Load(ModHost host)
    {
        Rift.Boot(host);
        RiftConfig.WriteDefault(Path.Combine(Rift.Dir, "portal.cfg"));
        Hooks.Install();
    }

    public void WorldOpened(GameWorld world) { }

    public void WorldClosed(GameWorld world)
    {
        // The rift's own swap closes one world and opens the other; that is not an ending.
        if (Realm.Active && ReferenceEquals(world, Realm.HomeWorld) && ReferenceEquals(Rift.World, Realm.RealmWorld)) return;
        if (Realm.Active && ReferenceEquals(world, Realm.RealmWorld) && ReferenceEquals(Rift.World, Realm.HomeWorld)) return;

        if (Realm.Active && (ReferenceEquals(world, Realm.RealmWorld) || ReferenceEquals(world, Realm.HomeWorld)))
            Realm.Abort("THE RUN ENDED");
        if (ReferenceEquals(world, Portal.World)) Portal.Forget();
        Visitors.WorldGone(world);
    }

    public void Step(GameWorld world, float dt, in InputFrame input) => Guard("step", () => Rift.Step(world, dt));

    public void Frame(GameWorld world, float dt) => Guard("frame", Rift.Frame);

    public void BeforeDraw(GameWorld world, ModScene scene) => Guard("before-draw", () =>
    {
        Realm.BeforeDraw(world, scene);
        Composite.Begin(world, scene);
    });

    public void Draw3D(GameWorld world, ModScene scene) => Guard("draw", () =>
    {
        if (Realm.Owns(world)) Realm.Draw3D(scene);
        else
        {
            // Minecraft's things: as the composite's mask when it will fill them in at full
            // resolution, as themselves when there is no composite.
            Overlays.Draw(world, scene.Eye, keyed: Composite.OverlayKeyed);
        }
    });

    public void DrawHud(GameWorld world, ModScene scene) => Guard("hud", () =>
    {
        if (Realm.Owns(world)) Realm.DrawTags(scene.Camera);
        Hud.Draw(world);
    });

    /// <summary>{rift} in the chat: status. {rift join}, {rift leave}, {rift connect host:port}.</summary>
    public string? Command(GameWorld? world, string line)
    {
        string l = line.Trim().Trim('{', '}').Trim().ToLowerInvariant();
        if (!l.StartsWith("rift")) return null;
        string rest = l[4..].Trim();
        switch (rest)
        {
            case "":
            case "status":
                return Rift.Live is { } live ? $"RIFT OPEN TO {live.WorldName.ToUpperInvariant()}"
                    : Discovery.Best() is { } f ? $"MINECRAFT WORLD {f.World.ToUpperInvariant()} FOUND - PRESS {Rift.Cfg.PortalKey}"
                    : "NO MINECRAFT WORLD FOUND ON YOUR NETWORK";
            case "join":
                Rift.PortalKey();
                return "JOINING WORLDS";
            case "leave":
                if (!Realm.Active) return "YOU ARE NOT IN MINECRAFT";
                Realm.Leave();
                return "BACK THROUGH THE WATER";
        }
        if (rest.StartsWith("connect "))
        {
            string target = rest[8..].Trim();
            int colon = target.LastIndexOf(':');
            int port = colon > 0 && int.TryParse(target[(colon + 1)..], out int p) ? p : 25570;
            string host = colon > 0 ? target[..colon] : target;
            Rift.Link?.Close("REPLACED");
            Rift.Link = Link.Connect(host, port, host);
            return $"REACHING {host}:{port}";
        }
        return "RIFT: STATUS, JOIN, LEAVE, CONNECT HOST:PORT";
    }

    private static void Guard(string where, Action a)
    {
        try { a(); }
        catch (Exception e)
        {
            Rift.Log($"{where}: {e}");
            throw;   // the game counts it, and switches the mod off if it keeps happening
        }
    }
}
