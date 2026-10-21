using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using Unrendered.Modding;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Entities;
using Unrendered.Rendering;
using GameWorld = Unrendered.World.World;

namespace UnrenderedPortal;

/// <summary>
/// The pilot's side of the crossing: an UNRENDERED craft standing in Minecraft.
///
/// <para>The craft never stops being itself. The same <see cref="PlayerTank"/> object — its
/// shield, its hull, its ammo, its chassis and every system on it — is lifted out of the
/// world it was in and set down in a stand-in world that has no city, no grid and no
/// hunters, because the place it is standing in is Minecraft's. The game goes on running
/// that craft exactly as it always does: its own physics, its own weapons, its own HUD. All
/// this side adds is the ground: Minecraft's blocks to stand on and bump into, Minecraft's
/// mobs to shoot, Minecraft's dropped items to roll over, and Minecraft's world drawn
/// around it the way Minecraft draws it.</para>
///
/// <para>Height is handled with a sliding floor. Every UNRENDERED chassis believes the
/// ground is a flat plane at zero. So the realm keeps a reference height — the Minecraft
/// Y of whatever the craft is standing on — and everything is measured from it. Walk up a
/// step and the reference rises with you; walk off a ledge and the drop turns into height,
/// which the chassis' own gravity then takes back.</para>
/// </summary>
public static class Realm
{
    public static bool Active { get; private set; }
    public static GameWorld? HomeWorld { get; private set; }
    public static GameWorld? RealmWorld { get; private set; }
    private static PlayerTank? _craft;
    private static PlayerTank? _stand;      // what holds the craft's seat back home
    private static Vector2 _homeAt;          // where the craft stood when it went
    private static float _homeFacing;

    /// <summary>Where the craft comes back out at home when it was not the glowstone portal it
    /// went through (a door some other mod opened), and which way it faces. Cleared on return.</summary>
    public static (Vector2 At, float Facing)? ReturnAt { get; set; }

    public static readonly Terrain Land = new();

    /// <summary>The Minecraft X/Z that the realm's (0, 0) sits on.</summary>
    public static double Ox, Oz;

    /// <summary>Minecraft Y of the floor the craft is standing on.</summary>
    public static float RefY;

    /// <summary>The same, eased, for drawing — a step up rises over a tenth of a second
    /// rather than snapping.</summary>
    public static float ShownRef;

    private static bool _entering;
    private static double _enteringSince;
    private static JsonObject? _spawn;
    private static bool _palette;
    public static long DayTime = 6000;

    public static readonly List<McEntity> Ents = new();
    private static readonly HashSet<int> _askedPickup = new();
    private static double _meClock, _craftClock;
    private static EntityRenderer? _craftEye;

    // --- Crossing ---------------------------------------------------------------------

    /// <summary>The craft touched the water. Ask Minecraft for the ground on the other side;
    /// the swap happens once it is here, so the pilot never stands in a half-loaded world.</summary>
    public static void Enter(GameWorld home)
    {
        if (Active || _entering || Rift.Live is not { } live) return;
        _entering = true;
        _enteringSince = Raylib.GetTime();
        _palette = false;
        _spawn = null;
        Land.Palette.Clear();
        var p = home.Player;
        live.Send("enter", "name", Rift.Cfg.Name, "chassis", p.Class.ToString().ToUpperInvariant(),
            "hp", p.Health + p.Shield, "max", p.MaxHealth + p.MaxShield);
        Rift.Notice("THROUGH THE WATER...");
        CueBank.Play(Cue.PortalEnter, p.Position, 0f);
    }

    internal static bool Handle(Msg m)
    {
        switch (m.T)
        {
            case "palette":
                if (m.J["entries"] is JsonArray entries) Land.AddPalette(entries);
                _palette = true;
                TrySwap();
                return true;
            case "region":
                Land.LoadRegion(m);
                TrySwap();
                return true;
            case "chunk":
                Land.LoadChunk(m);
                TrySwap();
                return true;
            case "spawn":
                _spawn = m.J;
                // Ask for the ground around where they will stand.
                SendView(m.F("x"), m.F("z"), force: true);
                TrySwap();
                return true;
            case "blocks":
                if (m.J["set"] is JsonArray set)
                    foreach (var n in set)
                        if (n is JsonArray a && a.Count >= 4)
                            Land.SetBlock(a[0]!.GetValue<int>(), a[1]!.GetValue<int>(), a[2]!.GetValue<int>(),
                                a[3]!.GetValue<int>(), a.Count > 4 ? a[4]!.GetValue<int>() : -1);
                if (m.J["palette"] is JsonArray more) Land.AddPalette(more);
                return true;
            case "ents":
                ReadEnts(m);
                return true;
            case "hurt":
                if (m.J["seat"] != null) Visitors.HurtSeat(m.I("seat"), m.F("amount") * McMobToUnr);
                else Hurt(m.F("amount"), m.Str("by"));
                return true;
        }
        return false;
    }

    private static void TrySwap()
    {
        if (!_entering || !_palette || _spawn == null) return;
        // The ground under the spawn has to be here before anyone stands on it.
        if (!Land.Has((int)Math.Floor(_spawn["x"]!.GetValue<double>()), (int)Math.Floor(_spawn["z"]!.GetValue<double>()))) return;
        if (Rift.World is not { } home || !ReferenceEquals(home, Portal.World ?? home)) { _entering = false; return; }
        _entering = false;
        Swap(home, _spawn);
    }

    private static readonly FieldInfo? Inventories = typeof(GameWorld).GetField("_inventories", BindingFlags.Instance | BindingFlags.NonPublic);

    private static void Swap(GameWorld home, JsonObject spawn)
    {
        var craft = home.Player;
        int seat = home.LocalIndex;

        // The stand-in world: the same planet's air and gravity, nothing else. No city, no
        // salvage, nobody hunting — Minecraft is what is standing here.
        var match = new MatchSettings
        {
            Destination = home.Match.Destination,
            Mode = GameMode.Sandbox,
            SpawnEnemies = false,
            Revives = home.Match.Revives,
            FriendlyFire = home.Match.FriendlyFire,
        };
        var realm = new GameWorld(home.Loadout, match, null);
        realm.DynamicSpawning = false;
        realm.Structures.Clear();
        realm.Enemies.Clear();
        realm.Soldiers.Clear();
        realm.Squads.Clear();
        realm.Pickups.Clear();
        realm.GroundWeapons.Clear();

        // The craft itself crosses — not a copy. Its pack comes with it.
        realm.Players[0] = craft;
        if (Inventories?.GetValue(realm) is List<Inventory> rInv && Inventories.GetValue(home) is List<Inventory> hInv
            && seat < hInv.Count)
        {
            while (rInv.Count < 1) rInv.Add(new Inventory());
            rInv[0] = hInv[seat];
        }

        // Its seat back home is held by an empty, absent craft, so the world it left keeps
        // running around a gap rather than around a ghost.
        _stand = new PlayerTank(new Vector2(Torus.Half - 1f, Torus.Half - 1f), 0f, home.Loadout)
        {
            Away = true, Lives = 0, Health = 0f, Shield = 0f,
        };
        home.Players[seat] = _stand;

        Ox = spawn["x"]!.GetValue<double>();
        Oz = spawn["z"]!.GetValue<double>();
        float y = (float)spawn["y"]!.GetValue<double>();
        float yaw = (float)(spawn["yaw"]?.GetValue<double>() ?? 0);
        craft.Rig?.ReleaseBoth();
        _homeAt = craft.Position;
        _homeFacing = craft.Heading;
        craft.Position = Vector2.Zero;
        craft.Heading = -yaw * MathF.PI / 180f;
        craft.Height = 0f;
        craft.GroundHeight = 0f;
        RefY = Land.GroundBelow((int)Math.Floor(Ox), (int)Math.Floor(Oz), y + 0.5f);
        ShownRef = RefY;

        Dim = Portal.Linked?["dim"]?.GetValue<string>() ?? "minecraft:overworld";
        HomeWorld = home;
        RealmWorld = realm;
        _craft = craft;
        Active = true;
        Ents.Clear();
        _askedPickup.Clear();
        Rift.World = realm;
        Rift.Notice("MINECRAFT");
        Rift.Log($"realm: crossed at mc {Ox:0.0},{y:0.0},{Oz:0.0} ref {RefY}");
    }

    /// <summary>Back through the water: the craft goes home and stands in front of
    /// UNRENDERED's portal, facing away from it.</summary>
    public static void Leave(string? why = null)
    {
        if (!Active) return;
        Rift.Live?.Send("leave");
        Restore();
        if (why != null) Rift.Notice(why);
        else Rift.Notice("BACK IN UNRENDERED");
    }

    /// <summary>The link died or the run ended under us: put things back if there is still
    /// somewhere to put them.</summary>
    public static void Abort(string why)
    {
        _entering = false;
        if (!Active) return;
        Rift.Log("realm: abort — " + why);
        Rift.Live?.Send("leave");
        if (ReferenceEquals(Rift.World, RealmWorld)) Restore();
        else Cleanup();
        Rift.Notice(why + " — BACK IN UNRENDERED");
    }

    private static void Restore()
    {
        var home = HomeWorld!;
        var craft = _craft!;
        home.Players[home.LocalIndex] = craft;
        craft.Rig?.ReleaseBoth();
        // Out of the door it went in by: another mod's, the glowstone portal, or failing both
        // the spot it left from — never wherever its Minecraft coordinates happen to land.
        var (from, facing) = ReturnAt ?? (Portal.Open ? (Portal.Position, Portal.Facing) : (_homeAt, _homeFacing));
        ReturnAt = null;
        Vector2 n = new(MathF.Sin(facing), MathF.Cos(facing));
        craft.Position = Torus.Wrap(from + n * 4f);
        craft.Heading = facing;
        craft.Height = 0f;
        craft.GroundHeight = 0f;
        Rift.World = home;
        CueBank.Play(Cue.PortalEnter, craft.Position, 0f);

        // Whatever the craft spilled while it was over there (a life spent in Minecraft drops
        // the fragments it carried) lay on a field nobody can see. It comes home with it.
        int brought = 0;
        foreach (var pk in RealmWorld!.Pickups)
        {
            if (pk.Consumed) continue;
            float a = brought * 2.4f;
            home.Pickups.Add(new Pickup(Torus.Wrap(craft.Position + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 2.5f),
                pk.Kind, pk.Amount, thrown: true));
            brought++;
        }
        if (brought > 0) Rift.Notice("WHAT YOU DROPPED OVER THERE CAME BACK WITH YOU");
        Cleanup();
    }

    private static void Cleanup()
    {
        Active = false;
        HomeWorld = RealmWorld = null;
        _craft = _stand = null;
        Ents.Clear();
        Land.Dispose();
    }

    // --- Each tick in Minecraft -------------------------------------------------------

    private static readonly FieldInfo? GroundedField =
        typeof(SoldierRig).GetField("<Grounded>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static void Tick(GameWorld world, float dt)
    {
        if (_entering && Raylib.GetTime() - _enteringSince > 20)
        {
            _entering = false;
            Rift.Notice("MINECRAFT DID NOT ANSWER");
        }
        if (!Active || !ReferenceEquals(world, RealmWorld) || _craft is not { } craft) return;

        Ground(craft);
        Rounds(world, dt);
        Collect(craft);
        Recentre(world, craft);

        var feet = Mc(craft.Position, craft.Height);

        // Spent in Minecraft: the run is lost at home, not over a world that is not UNRENDERED's.
        if (!craft.Alive)
        {
            Leave("YOUR CRAFT FELL IN MINECRAFT");
            return;
        }
        // Out of the bottom of the world: the void puts it back where it came from.
        if (feet.Y < Land.Bottom((int)MathF.Floor(feet.X), (int)MathF.Floor(feet.Z)) - Terrain.Void * 0.6f)
        {
            Leave("THE VOID SPAT YOU BACK OUT");
            return;
        }

        // The way home is the portal Minecraft stands on its side.
        SendView(feet.X, feet.Z, force: false);
        if (Land.PortalNear(feet, craft.Soldier != null ? 0.5f : 1.0f))
        {
            Leave();
            return;
        }

        double now = Raylib.GetTime();
        if (now - _meClock > 0.05 && Rift.Live is { } live)
        {
            _meClock = now;
            live.Send("me", "x", (double)feet.X, "y", (double)feet.Y, "z", (double)feet.Z,
                "yaw", -craft.Heading * 180f / MathF.PI, "pitch", -craft.Pitch * 180f / MathF.PI,
                "hp", craft.Health + craft.Shield, "max", craft.MaxHealth + craft.MaxShield,
                "moving", craft.IsAirborne ? 0 : 1, "chassis", craft.Class.ToString().ToUpperInvariant());
        }
    }

    /// <summary>How many chunks around the pilot Minecraft streams — Minecraft's own render
    /// distance, near enough.</summary>
    public const int ViewChunks = 8;

    private static (int, int) _viewChunk = (int.MinValue, 0);
    private static double _viewClock;

    /// <summary>Tells Minecraft where the pilot is, so it streams the chunks around them; and
    /// lets go of the ones that are now out of sight.</summary>
    private static void SendView(float mcX, float mcZ, bool force)
    {
        if (Rift.Live is not { } live) return;
        var c = ((int)MathF.Floor(mcX) >> 4, (int)MathF.Floor(mcZ) >> 4);
        double now = Raylib.GetTime();
        if (!force && c == _viewChunk && now - _viewClock < 2.0) return;
        _viewChunk = c;
        _viewClock = now;
        live.Send("view", "cx", c.Item1, "cz", c.Item2, "r", ViewChunks);
        Land.Forget(c.Item1, c.Item2, ViewChunks + 2);
    }

    /// <summary>
    /// UNRENDERED's world is a torus 400 metres across; walk 200 from where you came in and it
    /// would fold you back to the far side. So the stand-in world's origin travels with the
    /// pilot: every hundred metres the plane is re-centred under them, and everything on it
    /// (their own rounds in flight) moves with it. Minecraft's world just keeps going.
    /// </summary>
    private static void Recentre(GameWorld world, PlayerTank craft)
    {
        Vector2 d = craft.Position;
        if (MathF.Abs(d.X) < 100f && MathF.Abs(d.Y) < 100f) return;
        d = new Vector2(MathF.Round(d.X), MathF.Round(d.Y));
        Ox += d.X;
        Oz += d.Y;
        craft.Position -= d;
        foreach (var p in world.Projectiles) if (p.Active) p.Position = Torus.Wrap(p.Position - d);
    }

    /// <summary>Puts the craft down on the surface at a Minecraft X/Z (the test hatch's
    /// shortcut; a player walks).</summary>
    internal static void PlaceAt(double mcX, double mcZ, float fromY = 320f)
    {
        if (!Active || _craft is not { } craft) return;
        craft.Position = new Vector2((float)(mcX - Ox), (float)(mcZ - Oz));
        RefY = Land.GroundBelow((int)Math.Floor(mcX), (int)Math.Floor(mcZ), fromY);
        ShownRef = RefY;
        craft.Height = 0f;
    }

    /// <summary>A realm position (plane + height above the floor) in Minecraft coordinates.</summary>
    public static Vector3 Mc(Vector2 plane, float height)
        => new((float)(plane.X + Ox), RefY + height, (float)(plane.Y + Oz));

    private static float BodyRadius(PlayerTank c) => c.IsMachine ? PlayerTank.Radius : 0.4f;

    /// <summary>
    /// The craft against Minecraft's blocks. Walls push it out; the floor under it becomes
    /// the new zero; a ledge becomes a fall the chassis' own gravity finishes.
    /// </summary>
    private static void Ground(PlayerTank craft)
    {
        float r = BodyRadius(craft);
        float step = craft.IsMachine ? 1.05f : 0.6f;
        float body = MathF.Max(1.6f, craft.EyeHeight + 0.2f);

        var mc = Mc(craft.Position, craft.Height);

        float feet = RefY + craft.Height;

        // Walls: every block column the body overlaps that holds something between the
        // step height and the top of the head.
        for (int pass = 0; pass < 2; pass++)
        {
            mc = Mc(craft.Position, craft.Height);
            int x0 = (int)MathF.Floor(mc.X - r), x1 = (int)MathF.Floor(mc.X + r);
            int z0 = (int)MathF.Floor(mc.Z - r), z1 = (int)MathF.Floor(mc.Z + r);
            Vector2 push = Vector2.Zero;
            for (int bx = x0; bx <= x1; bx++)
                for (int bz = z0; bz <= z1; bz++)
                {
                    if (!Land.Blocked(bx, bz, feet + step, feet + body)) continue;
                    float cx = Math.Clamp(mc.X, bx, bx + 1), cz = Math.Clamp(mc.Z, bz, bz + 1);
                    var d = new Vector2(mc.X - cx, mc.Z - cz);
                    float len = d.Length();
                    if (len >= r) continue;
                    if (len < 1e-4f)
                    {
                        // Centre inside the block: out the nearest face.
                        float l = mc.X - bx, rr = bx + 1 - mc.X, n = mc.Z - bz, s = bz + 1 - mc.Z;
                        float min = MathF.Min(MathF.Min(l, rr), MathF.Min(n, s));
                        d = min == l ? new(-1, 0) : min == rr ? new(1, 0) : min == n ? new(0, -1) : new(0, 1);
                        push += d * (min + r);
                    }
                    else push += d / len * (r - len);
                }
            if (push == Vector2.Zero) break;
            craft.Position += push;
            craft.Rig?.RegisterWallHit();
        }

        // The floor: the highest standable surface under the body's middle, up to a step above
        // the feet.
        mc = Mc(craft.Position, craft.Height);
        float ground = float.MinValue;
        float probe = r * 0.5f;
        foreach (var (ox, oz) in new[] { (0f, 0f), (probe, 0f), (-probe, 0f), (0f, probe), (0f, -probe) })
            ground = MathF.Max(ground, Land.GroundBelow((int)MathF.Floor(mc.X + ox), (int)MathF.Floor(mc.Z + oz), feet + step));

        if (MathF.Abs(ground - RefY) > 1e-4f)
        {
            float old = RefY;
            RefY = ground;
            craft.Height = MathF.Max(0f, feet - ground);
            // A ledge: the drop is now height, and a soldier has to be told it left the ground.
            if (craft.Height > 0.01f && craft.Rig is { } rig) GroundedField?.SetValue(rig, false);
            // Everything else measured from the old floor moves with it.
            float shift = old - RefY;
            foreach (var p in RealmWorld!.Projectiles) if (p.Active) p.Height += shift;
        }

        ShownRef += (RefY - ShownRef) * MathF.Min(1f, (float)Config.FixedDt * 14f);
        if (MathF.Abs(RefY - ShownRef) > 4f) ShownRef = RefY;
    }

    public const float UnrToMcMob = 6.5f;
    public const float McMobToUnr = 5f;

    /// <summary>The pilot's rounds against Minecraft: mobs take them as damage, blocks stop
    /// them (and break, if both sides allow it).</summary>
    private static void Rounds(GameWorld world, float dt)
    {
        if (Rift.Live is not { } live) return;
        foreach (var p in world.Projectiles)
        {
            if (!p.Active || !p.FromPlayer) continue;
            Vector3 now = Mc(p.Position, p.Height);
            Vector3 was = now - new Vector3(p.Velocity.X, 0, p.Velocity.Y) * dt;

            McEntity? hit = null;
            foreach (var e in Ents)
            {
                if (e.IsItem || e.Max <= 0) continue;
                if (SegmentHitsBox(was, now, e.Pos, e.W * 0.5f + 0.15f, e.H + 0.1f)) { hit = e; break; }
            }
            if (hit != null)
            {
                float dmg = p.IsRocket ? 4f : p.IsGrenade ? 4f : p.IsCrabBomb ? 8f : p.IsLaser ? 1.5f : 1f;
                live.Send("hit", "id", hit.Id, "dmg", dmg * UnrToMcMob, "kind", p.IsRocket ? "rocket" : "round");
                p.Active = false;
                world.Emit(Cue.Hit, p.Position);
                continue;
            }

            if (p.Height < -4f) { p.Active = false; continue; }
            if (Land.Solid(now))
            {
                p.Active = false;
                world.Emit(Cue.Hit, p.Position);
                if (Rift.Cfg.BreakBlocks)
                    live.Send("break", "x", (int)MathF.Floor(now.X), "y", (int)MathF.Floor(now.Y), "z", (int)MathF.Floor(now.Z));
            }
        }
    }

    internal static bool SegmentHitsBox(Vector3 a, Vector3 b, Vector3 feet, float half, float h)
    {
        Vector3 min = new(feet.X - half, feet.Y, feet.Z - half), max = new(feet.X + half, feet.Y + h, feet.Z + half);
        Vector3 d = b - a;
        float t0 = 0f, t1 = 1f;
        for (int i = 0; i < 3; i++)
        {
            float o = i == 0 ? a.X : i == 1 ? a.Y : a.Z;
            float dd = i == 0 ? d.X : i == 1 ? d.Y : d.Z;
            float lo = i == 0 ? min.X : i == 1 ? min.Y : min.Z;
            float hi = i == 0 ? max.X : i == 1 ? max.Y : max.Z;
            if (MathF.Abs(dd) < 1e-6f) { if (o < lo || o > hi) return false; continue; }
            float ta = (lo - o) / dd, tb = (hi - o) / dd;
            if (ta > tb) (ta, tb) = (tb, ta);
            t0 = MathF.Max(t0, ta); t1 = MathF.Min(t1, tb);
            if (t0 > t1) return false;
        }
        return true;
    }

    /// <summary>Dropped Minecraft items the craft rolls over are asked for. Minecraft decides
    /// whether they are still there; the answer comes back as <c>got</c>.</summary>
    private static void Collect(PlayerTank craft)
    {
        if (Rift.Live is not { } live) return;
        // A full pack (or one that just had to give something back) takes nothing: what it
        // gave back would land at its feet and be taken again, forever.
        if (Raylib.GetTime() < Pocket.RefuseUntil || RealmWorld is not { } w || w.Inventory.Slots.All(s => !s.IsEmpty)) return;
        var feet = Mc(craft.Position, craft.Height);
        float reach = BodyRadius(craft) + 0.9f;
        foreach (var e in Ents)
        {
            if (!e.IsItem || !e.Pickable || _askedPickup.Contains(e.Id)) continue;
            var d = new Vector2(e.Pos.X - feet.X, e.Pos.Z - feet.Z);
            if (d.Length() > reach || MathF.Abs(e.Pos.Y - feet.Y) > 2f) continue;
            _askedPickup.Add(e.Id);
            live.Send("pickup", "id", e.Id);
        }
    }

    private static void Hurt(float mcAmount, string by)
    {
        if (!Active || RealmWorld is not { } w || _craft is not { } craft) return;
        float amount = mcAmount * McMobToUnr;
        var at = craft.Position;
        w.HurtCraft(craft, amount, at);
    }

    private static void ReadEnts(Msg m)
    {
        if (m.Bin == null) return;
        DayTime = (long)m.Num("time", DayTime);
        using var r = new BinaryReader(new MemoryStream(m.Bin));
        int n = r.ReadUInt16();
        Ents.Clear();
        var seen = new HashSet<int>();
        for (int i = 0; i < n; i++)
        {
            var e = McEntity.Read(r);
            e.Warm();
            Ents.Add(e);
            seen.Add(e.Id);
        }
        _askedPickup.RemoveWhere(id => !seen.Contains(id));
    }

    // --- Drawing Minecraft --------------------------------------------------------------

    /// <summary>Once a frame, outside any draw: build a few terrain sections, and send
    /// Minecraft the pilot's craft as UNRENDERED draws it.</summary>
    internal static void BeforeFrame()
    {
        if (!Active || _craft is not { } craft) return;
        Land.Rebuild(Mc(craft.Position, craft.Height + craft.EyeHeight), budget: 8);

        double now = Raylib.GetTime();
        if (now - _craftClock < 0.1 || Rift.Live is not { } live || live.Backlog > 4) return;
        _craftClock = now;
        _craftEye ??= new EntityRenderer();
        var behind = craft.Position - craft.Forward * 6f;
        var cam = new Vector3(behind.X, craft.Height + 3f, behind.Y);
        using var ms = new MemoryStream(16 << 10);
        using var bw = new BinaryWriter(ms);
        Atmosphere.Adopt(RealmWorld!.Sky);
        // Drawn at the plane's origin with its feet on zero, so what Minecraft receives is
        // relative to the pilot's feet — it only has to be put where the pilot stands.
        float height = craft.Height;
        craft.Height = 0f;
        try
        {
            Capture.DrawList(() => _craftEye.DrawCraft(craft, Vector2.Zero,
                cam - new Vector3(craft.Position.X, height, craft.Position.Y), (float)now), bw);
        }
        finally { craft.Height = height; }
        bw.Flush();
        live.SendBin("craft", ms.GetBuffer().AsSpan(0, (int)ms.Length), droppable: true);
    }

    /// <summary>Which of Minecraft's dimensions the pilot is in: the one the portal stands in.</summary>
    public static string Dim { get; private set; } = "minecraft:overworld";

    private static bool Nether => Dim == "minecraft:the_nether";
    private static bool End => Dim == "minecraft:the_end";

    /// <summary>Minecraft's daylight: 1 at noon, its night floor at midnight. The Nether and the
    /// End have no day: one even light each, as Minecraft gives them.</summary>
    public static float Daylight
    {
        get
        {
            if (Nether) return 0.6f;
            if (End) return 0.5f;
            float t = (DayTime % 24000) / 24000f;
            float angle = t - 0.25f;
            float c = MathF.Cos(angle * MathF.Tau) * 2f + 0.5f;
            return Math.Clamp(c, 0f, 1f) * 0.85f + 0.15f;
        }
    }

    /// <summary>Where Minecraft's fog closes in: just inside the edge of the slice it sent,
    /// so the end of the world is never seen — the way Minecraft hides its render distance.</summary>
    private static float FogEnd => ViewChunks * 16f - 10f;

    private static Color SkyColor => Nether ? new Color(51, 8, 8, 255) : End ? new Color(22, 12, 30, 255)
        : Lerp(new Color(10, 12, 24, 255), new Color(120, 167, 255, 255), Daylight);
    private static Color FogColor => Nether ? new Color(51, 8, 8, 255) : End ? new Color(15, 10, 20, 255)
        : Lerp(new Color(8, 10, 18, 255), new Color(192, 216, 255, 255), Daylight);

    private static Color Lerp(Color a, Color b, float t)
        => new((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t), 255);

    /// <summary>True when <paramref name="world"/> is the stand-in Minecraft is drawn around.</summary>
    public static bool Owns(GameWorld world) => Active && ReferenceEquals(world, RealmWorld);

    /// <summary>
    /// Before the frame: in Minecraft, the frame is Minecraft's. The game draws none of its own
    /// world — no sky, no grid — only its instruments and whatever the craft itself throws
    /// into the air. The radar shows Minecraft's creatures instead of the field's.
    /// </summary>
    internal static void BeforeDraw(GameWorld world, ModScene scene)
    {
        if (!Owns(world) || _craft is not { } craft) return;
        scene.OwnsWorld = true;
        scene.WorldContacts = false;
        // With the composite, the game's frame is cleared to nothing so Minecraft shows
        // through wherever the game draws nothing.
        scene.Clear = Composite.Enabled ? new Color(0, 0, 0, 0) : FogColor;

        var feet = Mc(craft.Position, craft.Height);
        Vector2 fwd = craft.Forward, right = new(fwd.Y, -fwd.X);
        foreach (var e in Ents)
        {
            if (e.IsItem) continue;
            var rel = new Vector2(e.Pos.X - feet.X, e.Pos.Z - feet.Z);
            if (rel.LengthSquared() > 60f * 60f) continue;
            var col = e.IsPlayer ? Color.White : e.Hostile ? new Color(255, 80, 60, 255) : new Color(120, 220, 120, 255);
            scene.Blips.Add(new RadarBlip(new Vector2(Vector2.Dot(rel, right), Vector2.Dot(rel, fwd)), col));
        }
    }

    /// <summary>
    /// Minecraft, inside UNRENDERED's own 3D pass and through its own lens: Minecraft's sky,
    /// its blocks with its textures and light, and its mobs as its renderer drew them this
    /// tick. Seen the way this game sees everything — through its 320×240 eye.
    /// </summary>
    internal static void Draw3D(ModScene scene)
    {
        if (!Active || _craft is null) return;
        var cam = scene.Camera;
        if (Composite.RealmLayered)
        {
            // Minecraft is drawn at full resolution afterwards (Composite). Here it only
            // writes depth, so what the craft throws into the air goes behind a hill when it
            // is behind a hill.
            Rlgl.DrawRenderBatchActive();
            Rlgl.ColorMask(false, false, false, false);
            DrawMinecraft(cam, sky: false);
            Rlgl.DrawRenderBatchActive();
            Rlgl.ColorMask(true, true, true, true);
        }
        else DrawMinecraft(cam, sky: true);

        // And the craft's own share of the picture, which the game left out with its world:
        // its rounds in flight, its sparks and debris, the soldier's arms and whatever is in
        // their hands. The stand-in world holds nothing else, so this is exactly that.
        _ownEye ??= new EntityRenderer();
        _ownEye.Draw(RealmWorld!, cam.Position);
    }

    /// <summary>Minecraft at the window's own resolution, for the composite.</summary>
    internal static void DrawFull(Camera3D cam) => DrawMinecraft(cam, sky: true);

    /// <summary>Minecraft's sky, blocks and creatures around the given realm-space eye.</summary>
    private static void DrawMinecraft(Camera3D cam, bool sky)
    {
        var origin = new Vector3((float)Ox, ShownRef, (float)Oz);
        var mcEye = cam.Position + origin;
        Medium(mcEye);
        if (sky && _inside == null && !Nether) DrawSky(cam);

        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        Land.Draw(origin, mcEye, Daylight, Fog, _fogStart, _fogEnd);
        Rlgl.DisableBackfaceCulling();

        foreach (var e in Ents)
        {
            float d = Vector2.Distance(new Vector2(e.Pos.X, e.Pos.Z), new Vector2(mcEye.X, mcEye.Z));
            float fog = Math.Clamp((d - _fogStart) / (_fogEnd - _fogStart), 0f, 1f);
            if (fog >= 0.99f) continue;
            e.Draw(e.Pos - origin, fog, Fog);
        }
        Rlgl.DrawRenderBatchActive();
    }

    /// <summary>Minecraft's fog colour — what the composite clears to. The air's at this hour,
    /// or, with the eye under water or in lava, that liquid's, as Minecraft does it.</summary>
    public static Color Fog => _inside switch
    {
        "water" => Lerp(new Color(3, 10, 30, 255), new Color(25, 70, 160, 255), Daylight),
        "lava" => new Color(153, 25, 0, 255),
        _ => FogColor,
    };

    private static string? _inside;
    private static float _fogStart = 70f, _fogEnd = 118f;

    /// <summary>What the eye is in. Under water the world closes in to a blue murk a few dozen
    /// blocks deep instead of going black; in lava, to an orange wall a block away.</summary>
    private static void Medium(Vector3 mcEye)
    {
        _inside = Land.At((int)MathF.Floor(mcEye.X), (int)MathF.Floor(mcEye.Y), (int)MathF.Floor(mcEye.Z))?.Liquid;
        (_fogStart, _fogEnd) = _inside switch
        {
            "water" => (0f, 36f),
            "lava" => (0f, 2f),
            // The Nether's air is thick: it closes in at half the distance.
            _ when Nether => (8f, FogEnd * 0.5f),
            _ => (FogEnd * 0.6f, FogEnd),
        };
    }

    private static EntityRenderer? _ownEye;

    /// <summary>Minecraft's sky: its colour overhead fading into its fog at the horizon — a
    /// dome around the eye, drawn behind everything — and the sun or the moon where
    /// Minecraft's clock puts them.</summary>
    private static void DrawSky(Camera3D cam)
    {
        Vector3 c = cam.Position;
        const float R = 400f;
        const int Seg = 16;
        Color top = SkyColor, hor = FogColor;
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.Begin(UnrenderedPortal.Draw.Triangles);
        for (int i = 0; i < Seg; i++)
        {
            float a0 = MathF.Tau * i / Seg, a1 = MathF.Tau * (i + 1) / Seg;
            var h0 = c + new Vector3(MathF.Sin(a0) * R, 0, MathF.Cos(a0) * R);
            var h1 = c + new Vector3(MathF.Sin(a1) * R, 0, MathF.Cos(a1) * R);
            var m0 = c + new Vector3(MathF.Sin(a0) * R * 0.7f, R * 0.35f, MathF.Cos(a0) * R * 0.7f);
            var m1 = c + new Vector3(MathF.Sin(a1) * R * 0.7f, R * 0.35f, MathF.Cos(a1) * R * 0.7f);
            var apex = c + new Vector3(0, R, 0);
            Color mid = Lerp(hor, top, 0.75f);
            Tri(h0, hor, h1, hor, m1, mid);
            Tri(h0, hor, m1, mid, m0, mid);
            Tri(m0, mid, m1, mid, apex, top);
            var b0 = h0 - new Vector3(0, R, 0);
            var b1 = h1 - new Vector3(0, R, 0);
            Tri(h0, hor, b1, hor, h1, hor);
            Tri(h0, hor, b0, hor, b1, hor);
        }
        Rlgl.End();

        if (End) { Rlgl.DrawRenderBatchActive(); Rlgl.EnableDepthMask(); return; }   // no sun, no moon
        float t = (DayTime % 24000) / 24000f;
        float angle = (t - 0.25f) * MathF.Tau;   // 0 at noon
        var sunDir = new Vector3(-MathF.Sin(angle), MathF.Cos(angle), 0);
        Body(c, sunDir, Textures.Get("minecraft:textures/environment/sun.png"), 30f, false);
        Body(c, -sunDir, Textures.Get("minecraft:textures/environment/moon_phases.png"), 20f, true);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
    }

    private static void Tri(Vector3 a, Color ca, Vector3 b, Color cb, Vector3 cc, Color ccol)
    {
        Rlgl.Color4ub(ca.R, ca.G, ca.B, 255); Rlgl.Vertex3f(a.X, a.Y, a.Z);
        Rlgl.Color4ub(cb.R, cb.G, cb.B, 255); Rlgl.Vertex3f(b.X, b.Y, b.Z);
        Rlgl.Color4ub(ccol.R, ccol.G, ccol.B, 255); Rlgl.Vertex3f(cc.X, cc.Y, cc.Z);
    }

    /// <summary>The sun or the moon: a square of Minecraft's own texture, far off along
    /// <paramref name="dir"/>, facing the eye.</summary>
    private static void Body(Vector3 eye, Vector3 dir, Textures.Tex? tex, float size, bool moon)
    {
        if (tex == null || dir.Y < -0.1f) return;
        const float Dist = 300f;
        Vector3 at = eye + Vector3.Normalize(dir) * Dist;
        Vector3 n = Vector3.Normalize(dir);
        Vector3 u = Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.Z) > 0.9f ? Vector3.UnitX : Vector3.UnitZ));
        Vector3 v = Vector3.Cross(u, n);
        float h = size;
        float u1 = moon ? 0.25f : 1f, v1 = moon ? 0.5f : 1f;
        Rlgl.SetTexture(tex.Gpu.Id);
        Rlgl.Begin(UnrenderedPortal.Draw.Quads);
        Rlgl.Color4ub(255, 255, 255, 255);
        Rlgl.TexCoord2f(0, v1); Rlgl.Vertex3f(at.X - u.X * h - v.X * h, at.Y - u.Y * h - v.Y * h, at.Z - u.Z * h - v.Z * h);
        Rlgl.TexCoord2f(u1, v1); Rlgl.Vertex3f(at.X + u.X * h - v.X * h, at.Y + u.Y * h - v.Y * h, at.Z + u.Z * h - v.Z * h);
        Rlgl.TexCoord2f(u1, 0); Rlgl.Vertex3f(at.X + u.X * h + v.X * h, at.Y + u.Y * h + v.Y * h, at.Z + u.Z * h + v.Z * h);
        Rlgl.TexCoord2f(0, 0); Rlgl.Vertex3f(at.X - u.X * h + v.X * h, at.Y - u.Y * h + v.Y * h, at.Z - u.Z * h + v.Z * h);
        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    /// <summary>Mob names and health over Minecraft's creatures, drawn into UNRENDERED's own
    /// HUD layer — this is the pilot's instrument reading them, not Minecraft.</summary>
    internal static void DrawTags(Camera3D cam)
    {
        if (!Active) return;
        var origin = new Vector3((float)Ox, ShownRef, (float)Oz);
        foreach (var e in Ents)
        {
            if (e.IsItem || e.Max <= 0) continue;
            var head = e.Pos - origin + new Vector3(0, e.H + 0.4f, 0);
            var fwd = Vector3.Normalize(cam.Target - cam.Position);
            if (Vector3.Dot(fwd, head - cam.Position) <= 0.2f) continue;
            float dist = Vector3.Distance(head, cam.Position);
            if (dist > 40f) continue;
            Vector2 s = Raylib.GetWorldToScreenEx(head, cam, Config.InternalWidth, Config.InternalHeight);
            const int bw = 20;
            int bx = (int)s.X - bw / 2, by = (int)s.Y;
            float f = Math.Clamp(e.Hp / e.Max, 0f, 1f);
            Raylib.DrawRectangle(bx - 1, by - 1, bw + 2, 4, new Color(5, 7, 10, 200));
            Raylib.DrawRectangle(bx, by, (int)(bw * f), 2, e.Hostile ? Palette.Warning : Palette.GridNear);
            if (e.IsPlayer && e.Name.Length > 0)
                Font.Centered(e.Name.ToUpperInvariant(), (int)s.X, by - 9, 1, Color.White);
        }
    }
}
