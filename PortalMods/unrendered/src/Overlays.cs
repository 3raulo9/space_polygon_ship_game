using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Entities;
using Unrendered.Rendering;
using GameWorld = Unrendered.World.World;

namespace UnrenderedPortal;

/// <summary>
/// Minecraft in UNRENDERED's world: the visitors themselves, every Minecraft entity standing
/// in the rift dimension (mobs that walked through the portal, items somebody dropped,
/// arrows in flight) and the blocks Minecraft players built there. All of it drawn as
/// Minecraft drew it, all of it in the same place in both games — the rift dimension is
/// UNRENDERED's world as Minecraft holds it.
/// </summary>
public static class Overlays
{
    /// <summary>Rift-dimension entities, positions in UNR coordinates.</summary>
    public static readonly List<McEntity> RiftEnts = new();
    private static readonly HashSet<int> _askedPickup = new();

    /// <summary>The world the rift dimension mirrors.</summary>
    public static GameWorld? Home => Realm.Active ? Realm.HomeWorld : Rift.World;

    public static bool Any(GameWorld w, Vector3 eye)
        => ReferenceEquals(w, Home)
           && (Visitors.AnyLookIn(w) || RiftEnts.Count > 0 || Built.Any || ReferenceEquals(Portal.World, w));

    public static void Clear()
    {
        RiftEnts.Clear();
        _askedPickup.Clear();
        Built.Clear();
    }

    internal static bool Handle(Msg m)
    {
        switch (m.T)
        {
            case "rents":
                if (m.Bin == null) return true;
                using (var r = new BinaryReader(new MemoryStream(m.Bin)))
                {
                    int n = r.ReadUInt16();
                    RiftEnts.Clear();
                    var seen = new HashSet<int>();
                    for (int i = 0; i < n; i++)
                    {
                        var e = McEntity.Read(r);
                        e.Warm();
                        RiftEnts.Add(e);
                        seen.Add(e.Id);
                    }
                    _askedPickup.RemoveWhere(id => !seen.Contains(id));
                }
                return true;
            case "built":
                Built.Handle(m);
                return true;
        }
        return false;
    }

    /// <summary>Inside the 3D pass (keyed, when the composite will fill them in at full
    /// resolution), or into the composite's own full-resolution layer.</summary>
    internal static void Draw(GameWorld w, Vector3 eye, bool keyed)
    {
        if (!ReferenceEquals(w, Home)) return;
        if (keyed) Composite.BeginMask();
        Portal.Draw(w, eye, keyed);
        Built.Draw(eye, keyed);
        Visitors.Draw(w, eye, keyed);
        var eyeXZ = new Vector2(eye.X, eye.Z);
        foreach (var e in RiftEnts)
        {
            Vector2 at = Torus.NearestImage(new Vector2(e.Pos.X, e.Pos.Z), eyeXZ);
            float fog = GridRenderer.FogFactor(Vector2.Distance(at, eyeXZ));
            if (fog >= 0.99f) continue;
            e.Draw(new Vector3(at.X, e.Pos.Y, at.Y), fog, Atmosphere.FogColor, keyed);
        }
        Rlgl.DrawRenderBatchActive();
        if (keyed) Composite.EndMask();
    }

    /// <summary>
    /// Each tick of the home world: the pilot's rounds against Minecraft's creatures standing in
    /// it, and Minecraft's dropped items under the craft — the same rules as in Minecraft.
    /// </summary>
    internal static void Tick(GameWorld w, float dt)
    {
        if (!ReferenceEquals(w, Home) || Rift.Live is not { } live) return;
        Built.Collide(w);
        if (RiftEnts.Count == 0) return;

        foreach (var p in w.Projectiles)
        {
            if (!p.Active || !p.FromPlayer) continue;
            if (p.Owner >= 0 && p.Owner < w.Players.Count && w.Players[p.Owner].Puppet) continue;
            var now = new Vector3(p.Position.X, p.Height, p.Position.Y);
            var was = now - new Vector3(p.Velocity.X, 0, p.Velocity.Y) * dt;
            foreach (var e in RiftEnts)
            {
                if (e.IsItem || e.Max <= 0) continue;
                Vector2 at = Torus.NearestImage(new Vector2(e.Pos.X, e.Pos.Z), p.Position);
                if (!Realm.SegmentHitsBox(was, now, new Vector3(at.X, e.Pos.Y, at.Y), e.W * 0.5f + 0.15f, e.H + 0.1f)) continue;
                float dmg = p.IsRocket ? 4f : p.IsGrenade ? 4f : p.IsCrabBomb ? 8f : p.IsLaser ? 1.5f : 1f;
                live.Send("hit", "id", e.Id, "dmg", dmg * Realm.UnrToMcMob, "kind", p.IsRocket ? "rocket" : "round");
                p.Active = false;
                w.Emit(Cue.Hit, p.Position);
                break;
            }
        }

        foreach (var craft in w.Players)
        {
            if (craft.Puppet || craft.Away || !craft.Alive) continue;
            float reach = (craft.IsMachine ? PlayerTank.Radius : 0.4f) + 0.9f;
            foreach (var e in RiftEnts)
            {
                if (!e.IsItem || !e.Pickable || _askedPickup.Contains(e.Id)) continue;
                if (Torus.Distance(new Vector2(e.Pos.X, e.Pos.Z), craft.Position) > reach) continue;
                if (MathF.Abs(e.Pos.Y - craft.Height) > 2f) continue;
                _askedPickup.Add(e.Id);
                live.Send("pickup", "id", e.Id);
            }
        }
    }
}
