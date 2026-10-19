using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Entities;
using Unrendered.Rendering;
using Unrendered.World;
using GameWorld = Unrendered.World.World;

namespace UnrenderedPortal;

/// <summary>
/// Minecraft players standing in UNRENDERED.
///
/// Each one is still a Minecraft client on their own machine — their hotbar, their hearts,
/// their inventory, their sword. On this side each takes a real seat in the world as a
/// <see cref="PlayerTank.Puppet"/>: the hunters pick them as quarry, the rounds strike them,
/// the salvage they walk over is theirs, the kill feed names them. Where they are and how they
/// look comes from Minecraft; what happens to them goes back to Minecraft. And the world they
/// are looking at is UNRENDERED's own frame, drawn by UNRENDERED's renderer from where they
/// stand and streamed to them several times a second.
/// </summary>
public static class Visitors
{
    public sealed class Visitor
    {
        public required string Uuid;
        public required string Name;
        public required GameWorld World;
        public required PlayerTank Seat;
        public float Y;               // height above the grid
        public float Hp = 20, Max = 20;
        public McEntity? Look;        // how Minecraft draws them, refreshed ~10 Hz
        public double SceneClock;
        public Vector2 Pos => Seat.Position;
        public int SeatIndex => World.Players.IndexOf(Seat);
    }

    private static readonly Dictionary<string, Visitor> _all = new();
    public static IReadOnlyCollection<Visitor> All => _all.Values;

    /// <summary>The world whose skyline Minecraft has already been sent.</summary>
    private static GameWorld? _citySent;
    private static readonly HashSet<int> _fellSent = new();
    private static readonly Dictionary<int, int> _mined = new();
    private static double _fieldClock;

    /// <summary>Their capture runs on a renderer of its own, so the extra passes never touch
    /// the animation clocks of the one drawing the player's screen.</summary>
    private static EntityRenderer? _eye;

    public static bool AnyIn(GameWorld w) => _all.Values.Any(v => ReferenceEquals(v.World, w));

    public static void Clear()
    {
        foreach (var v in _all.Values) v.World.RemovePuppet(v.Seat);
        _all.Clear();
        _citySent = null;
        _fellSent.Clear();
        _mined.Clear();
    }

    /// <summary>A world ended with Minecraft players in it: they go home on the Minecraft side.</summary>
    internal static void WorldGone(GameWorld w)
    {
        foreach (var v in _all.Values.Where(v => ReferenceEquals(v.World, w)).ToList())
        {
            Rift.Live?.Send("harm", "uuid", v.Uuid, "amount", 0f, "by", "THE RIFT CLOSING", "eject", true);
            _all.Remove(v.Uuid);
        }
        if (ReferenceEquals(_citySent, w)) _citySent = null;
    }

    /// <summary>The visitors' home: the world the pilot left behind if they are in Minecraft,
    /// otherwise the one on screen.</summary>
    private static GameWorld? Home => Realm.Active ? Realm.HomeWorld : Rift.World;

    // --- Messages ------------------------------------------------------------------------

    internal static bool Handle(Msg m)
    {
        switch (m.T)
        {
            case "arrive": Arrive(m); return true;
            case "visitor": Move(m); return true;
            case "avatar": Avatar(m); return true;
            case "strike": Strike(m); return true;
            case "mine": Mine(m); return true;
            case "use": Use(m); return true;
            case "depart":
                if (_all.Remove(m.Str("uuid"), out var gone))
                {
                    gone.World.RemovePuppet(gone.Seat);
                    Rift.Notice($"{gone.Name} WENT HOME");
                }
                return true;
        }
        return false;
    }

    private static void Arrive(Msg m)
    {
        if (Home is not { } home || Rift.Live is not { } live) return;
        if (home.Networked)
        {
            live.Send("harm", "uuid", m.Str("uuid"), "amount", 0f, "by", "A MATCH", "eject", true);
            return;
        }
        string uuid = m.Str("uuid");
        if (_all.ContainsKey(uuid)) return;
        string name = m.Str("name", "STEVE").ToUpperInvariant();

        // They step out of UNRENDERED's own portal if one is standing, facing into the city;
        // otherwise beside the craft.
        Vector2 at; float facing;
        if (Portal.Open && ReferenceEquals(Portal.World, home))
        {
            facing = Portal.Facing;
            at = Torus.Wrap(Portal.Position + new Vector2(MathF.Sin(facing), MathF.Cos(facing)) * 3f);
        }
        else
        {
            var p = home.Player;
            facing = p.Heading + MathF.PI;
            at = Torus.Wrap(p.Position + p.Forward * 5f);
        }

        // A plain machine build: it carries no viewmodel and no rig, so nothing of
        // UNRENDERED's own first-person kit can ever be drawn on a Minecraft body.
        var seat = home.AddPuppet(new Loadout { Class = PlayerClass.Tank }, name, at, facing);
        if (seat == null)
        {
            live.Send("harm", "uuid", uuid, "amount", 0f, "by", "A FULL WORLD", "eject", true);
            return;
        }
        var v = new Visitor { Uuid = uuid, Name = name, World = home, Seat = seat };
        // Their health lives in Minecraft. A hit here is a hit there.
        seat.DamageHandler = (amount, from) =>
        {
            Rift.Live?.Send("harm", "uuid", uuid, "amount", amount * UnrToMc, "by", "UNRENDERED");
            return true;
        };
        _all[uuid] = v;

        SendCity(home, live);
        var portal = new JsonObject
        {
            ["x"] = Portal.Open ? Portal.Position.X : at.X,
            ["z"] = Portal.Open ? Portal.Position.Y : at.Y,
            ["facing"] = Portal.Open ? Portal.Facing : facing,
        };
        live.Send("place", "uuid", uuid, "x", at.X, "z", at.Y, "yaw", facing, "portal", portal);
        Rift.Notice($"{name} CAME THROUGH");
        CueBank.Play(Cue.PortalEnter, at, 0f);
    }

    private static void Move(Msg m)
    {
        if (!_all.TryGetValue(m.Str("uuid"), out var v)) return;
        var s = v.Seat;
        s.Position = Torus.Wrap(new Vector2(m.F("x"), m.F("z")));
        v.Y = MathF.Max(0f, m.F("y"));
        s.Height = v.Y;
        // UNR convention on this message: heading and pitch in radians, pitch positive up.
        s.Heading = m.F("yaw");
        s.Pitch = m.F("pitch");
        v.Hp = m.F("hp", v.Hp);
        v.Max = m.F("max", v.Max);
        // The roster's shield bar reads the seat; keep it saying what Minecraft says.
        s.Health = s.MaxHealth * Math.Clamp(v.Hp / MathF.Max(1f, v.Max), 0.01f, 1f);
        s.Shield = 0f;
    }

    private static void Avatar(Msg m)
    {
        if (m.Bin == null || !_all.TryGetValue(m.Str("uuid"), out var v)) return;
        using var r = new BinaryReader(new MemoryStream(m.Bin));
        v.Look = McEntity.Read(r);
        v.Look.Warm();
    }

    // --- The skyline, for Minecraft's collision --------------------------------------

    private static void SendCity(GameWorld w, Link live)
    {
        if (ReferenceEquals(_citySent, w)) return;
        _citySent = w;
        _fellSent.Clear();
        var list = new JsonArray();
        Span<(Vector2 At, float Radius)> legs = stackalloc (Vector2, float)[Structure.MaxBlockers];
        int hash = 17;
        foreach (var s in w.Structures)
        {
            int n = s.Blockers(legs);
            if (n == 0) continue;
            var arr = new JsonArray();
            for (int i = 0; i < n; i++)
                arr.Add(new JsonArray(legs[i].At.X, legs[i].At.Y, legs[i].Radius));
            list.Add(new JsonObject { ["i"] = s.Index, ["h"] = s.BlockHeight, ["legs"] = arr });
            hash = hash * 31 + s.Index * 7 + (int)(s.Position.X * 10) + (int)(s.Position.Y * 13);
        }
        live.Send("city", "hash", hash, "size", Torus.Size, "structures", list);
        Rift.Log($"city sent: {list.Count} structures");
    }

    // --- Ids for UNRENDERED's enemies, as Minecraft sees them -------------------------

    private sealed class Box { public int V; }
    private static readonly ConditionalWeakTable<object, Box> _ids = new();
    private static readonly Dictionary<int, WeakReference<object>> _byId = new();
    private static int _next = 1;

    private static int IdOf(object o)
    {
        var b = _ids.GetValue(o, _ => new Box { V = _next++ });
        _byId[b.V] = new WeakReference<object>(o);
        return b.V;
    }

    public const float McToUnr = 0.15f;    // MC weapon → UNR enemy
    public const float UnrToMc = 0.2f;     // UNR damage → MC player

    /// <summary>A Minecraft weapon landed on one of UNRENDERED's enemies. It goes in through
    /// the game's own damage path, credited to the visitor's seat — the kill feed names them,
    /// the salvage drops, a DESCENT counts it.</summary>
    private static void Strike(Msg m)
    {
        if (Home is not { } w) return;
        int id = m.I("id");
        float dmg = m.F("dmg") * McToUnr;
        _all.TryGetValue(m.Str("uuid"), out var who);
        int by = who?.SeatIndex ?? Projectile.NoOwner;
        if (!_byId.TryGetValue(id, out var wr) || !wr.TryGetTarget(out var target)) return;

        switch (target)
        {
            case EnemyTank e when e.Alive:
                w.HurtEnemy(e, dmg, by);
                w.Emit(Cue.Hit, e.Position);
                break;
            case EnemySoldier s when s.Alive:
                w.HurtSoldier(s, dmg, who?.Pos, by);
                w.Emit(Cue.Hit, s.Position);
                break;
            default:
                w.HurtBoss(target, dmg, by, who?.Pos);
                break;
        }
    }

    private static readonly System.Reflection.MethodInfo? FractureDebris =
        typeof(GameWorld).GetMethod("EmitTowerFractureDebris",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    private static readonly List<DebrisSpawn> _detached = new();

    /// <summary>
    /// A Minecraft player broke a piece of a building — with a pickaxe, a sword, TNT, a creeper
    /// going off beside it, whatever broke the block. The building takes it the way it takes a
    /// lance: its own fracture model loses the cells around that point (they fall as the game's
    /// own rubble), a section left unsupported comes down with them, and a base cut through
    /// brings the whole tower down. Minecraft is told which pieces went, so its collision
    /// follows. Arches have no cells: enough broken pieces and they give way whole.
    /// </summary>
    private static void Mine(Msg m)
    {
        if (Home is not { } w) return;
        var at = new Vector3(m.F("x"), m.F("y"), m.F("z"));
        var flat = new Vector2(at.X, at.Z);
        Structure? best = null;
        float bestD = float.MaxValue;
        Span<(Vector2 At, float Radius)> legs = stackalloc (Vector2, float)[Structure.MaxBlockers];
        foreach (var s in w.Structures)
        {
            int n = s.Blockers(legs);
            for (int i = 0; i < n; i++)
            {
                float d = Torus.Distance(legs[i].At, flat) - legs[i].Radius;
                if (d < bestD) { bestD = d; best = s; }
            }
        }
        if (best == null || bestD > 2f || best.Falling) return;
        _all.TryGetValue(m.Str("uuid"), out var who);

        if (best.Kind == StructureKind.Tower)
        {
            var impact = new Vector3(Torus.NearestImage(flat, best.Position).X, at.Y, Torus.NearestImage(flat, best.Position).Y);
            _detached.Clear();
            // A cell of the fracture model is a few metres across; one Minecraft block mined out of
            // it chips the cells nearest it, and a handful bring one chunk of the tower away.
            bool felled = best.CutTower(impact, 4f, 12f, _detached, out bool fresh);
            FractureDebris?.Invoke(w, new object[] { best, impact, _detached, fresh, felled });
            if (_detached.Count > 0 && Rift.Live is { } live)
            {
                var boxes = new JsonArray();
                foreach (var d in _detached) boxes.Add(new JsonArray(d.Pos.X, d.Pos.Y, d.Pos.Z, MathF.Max(0.75f, d.Size)));
                live.Send("cut", "i", best.Index, "boxes", boxes);
            }
            if (felled && who != null) Rift.Notice($"{who.Name} BROUGHT DOWN A TOWER");
            return;
        }

        int count = _mined.TryGetValue(best.Index, out int c) ? c + 1 : 1;
        _mined[best.Index] = count;
        w.Emit(Cue.StructureCrack, flat);
        if (count >= (int)(6 + best.BlockHeight * 0.5f))
        {
            best.Strike();
            w.Emit(Cue.StructureGroan, best.Position);
            if (who != null) Rift.Notice($"{who.Name} BROUGHT DOWN AN ARCH");
        }
    }

    /// <summary>Something in the rift dimension hit one of UNRENDERED's own players.</summary>
    internal static void HurtSeat(int seat, float amount)
    {
        if (Home is not { } w || (uint)seat >= (uint)w.Players.Count) return;
        var craft = w.Players[seat];
        if (craft.Puppet || !craft.Alive) return;
        w.HurtCraft(craft, amount, craft.Position);
    }

    /// <summary>
    /// A Minecraft player used one of UNRENDERED's own items inside UNRENDERED. It does what it
    /// does here: a crab core is lobbed from their seat, along where they are looking, by the
    /// game's own throw — the same bomb, the same beams, the kill credited to them.
    /// </summary>
    private static void Use(Msg m)
    {
        if (Home is not { } w || !_all.TryGetValue(m.Str("uuid"), out var v)) return;
        if (m.Str("item") != "crab_core") return;
        int seat = v.SeatIndex;
        if (seat < 0) return;
        var pack = w.InventoryOf(seat);
        pack.Weapons[0] = new ItemStack(ItemKind.CrabCore, 1);
        w.UseWeaponSlot(0, v.Seat);
        pack.Weapons[0] = ItemStack.Empty;
    }

    /// <summary>20 Hz: where UNRENDERED's own players stand, so the rift dimension can hold
    /// a stand-in for each that Minecraft's mobs go for and Minecraft's weapons can hit.</summary>
    private static double _seatClock;

    private static void SendSeats(GameWorld w, Link live)
    {
        double now = Raylib.GetTime();
        if (now - _seatClock < 0.05) return;
        _seatClock = now;
        var list = new JsonArray();
        for (int i = 0; i < w.Players.Count; i++)
        {
            var p = w.Players[i];
            if (p.Puppet || p.Away || !p.Alive) continue;
            list.Add(new JsonObject
            {
                ["seat"] = i, ["name"] = i == w.LocalIndex ? Rift.Cfg.Name : w.NameOrSeat(i),
                ["x"] = p.Position.X, ["y"] = p.Height, ["z"] = p.Position.Y, ["yaw"] = p.Heading,
                ["hp"] = p.Health + p.Shield, ["max"] = p.MaxHealth + p.MaxShield,
                ["r"] = p.IsMachine ? PlayerTank.Radius : 0.4f, ["h"] = p.EyeHeight + 0.3f,
            });
        }
        live.Send("seats", "list", list);
    }

    // --- Each tick ---------------------------------------------------------------------

    internal static void Tick(GameWorld w, float dt)
    {
        if (Rift.Live is not { } live || !ReferenceEquals(w, Home)) return;
        if (!w.Networked) SendSeats(w, live);
        if (_all.Count == 0) return;
        bool any = false;
        foreach (var v in _all.Values)
        {
            if (!ReferenceEquals(v.World, w)) continue;
            any = true;
            HandOver(w, v, live);
        }
        if (!any) return;

        double now = Raylib.GetTime();
        if (now - _fieldClock > 0.05)
        {
            _fieldClock = now;
            SendField(w, live);
            SendFallen(w, live);
        }
    }

    /// <summary>Whatever salvage the visitor's seat picked up goes on to their Minecraft
    /// inventory, as the rift's relic items. Their seat's pack is only a hand passing it on.</summary>
    private static void HandOver(GameWorld w, Visitor v, Link live)
    {
        int seat = v.SeatIndex;
        if (seat < 0) return;
        var pack = w.InventoryOf(seat);
        for (int i = 0; i < pack.Slots.Length; i++)
        {
            ref var stack = ref pack.Slots[i];
            if (stack.IsEmpty) continue;
            live.Send("give", "uuid", v.Uuid, "item", Items.Id(stack.Kind), "count", stack.Count);
            stack = ItemStack.Empty;
        }
    }

    private static void SendField(GameWorld w, Link live)
    {
        var ents = new JsonArray();
        void Add(object o, Vector2 at, float y, float r, float h, float hp, float max)
        {
            foreach (var v in _all.Values)
            {
                if (!ReferenceEquals(v.World, w) || Torus.Distance(v.Pos, at) > 96f) continue;
                Vector2 near = Torus.NearestImage(at, v.Pos);
                ents.Add(new JsonObject
                {
                    ["id"] = IdOf(o), ["x"] = near.X, ["y"] = y, ["z"] = near.Y,
                    ["r"] = r, ["h"] = h, ["hp"] = hp, ["max"] = max,
                });
                return;
            }
        }

        foreach (var e in w.Enemies)
            if (e.Alive) Add(e, e.Position, e.Height, EnemyTank.Radius, EnemyTank.BodyHeight, e.Shield, e.IsElite ? 5 : 3);
        foreach (var s in w.Soldiers)
            if (s.Alive) Add(s, s.Position, s.Height, 0.5f, 1.9f, s.Shield, 2);
        if (w.Boss is { Alive: true } crab) Add(crab, crab.Position, 0f, 5f, 14f, crab.CoreFraction, 1);
        if (w.Maw is { Alive: true } maw) Add(maw, maw.Position, maw.BodyY - 4f, 4f, 8f, maw.CrystalFraction, 1);
        foreach (var b in w.Bosses)
            if (b.Alive) Add(b, b.Position, b.Height, 6f, 16f, 1, 1);
        live.Send("field", "ents", ents);
    }

    private static void SendFallen(GameWorld w, Link live)
    {
        foreach (var s in w.Structures)
            if (s.Falling && _fellSent.Add(s.Index)) live.Send("fell", "i", s.Index);
        foreach (var s in w.RazedStructures)
            if (_fellSent.Add(s.Index)) live.Send("fell", "i", s.Index);
    }

    // --- The view they are standing in ------------------------------------------------

    /// <summary>
    /// Once a rendered frame, outside any draw: for each visitor whose turn it is, run
    /// UNRENDERED's own world pass from their eye and send the result. The sky, the floor,
    /// the city, the hunters, the rounds in the air — all of it exactly as this game drew it.
    /// </summary>
    internal static void SendScenes()
    {
        if (_all.Count == 0 || Rift.Live is not { } live) return;
        double now = Raylib.GetTime();
        double period = 1.0 / Rift.Cfg.SceneHz;
        foreach (var v in _all.Values)
        {
            if (now - v.SceneClock < period) continue;
            if (live.Backlog > 4) return;
            v.SceneClock = now;

            var w = v.World;
            var eye = new Vector3(v.Pos.X, v.Y + 1.62f, v.Pos.Y);
            var sky = w.Sky;
            Atmosphere.Adopt(sky);
            using var ms = new MemoryStream(64 << 10);
            using var bw = new BinaryWriter(ms);
            Rgba(bw, sky.Top); Rgba(bw, sky.Mid); Rgba(bw, sky.Horizon); Rgba(bw, sky.Fog);
            bw.Write(sky.Murk);
            bw.Write(sky.LightDir.X); bw.Write(sky.LightDir.Y); bw.Write(sky.LightDir.Z);
            bw.Write(sky.SunAltitude); bw.Write(sky.SunAzimuth);
            bw.Write(sky.MoonAltitude); bw.Write(sky.MoonAzimuth);
            bw.Write(sky.Night);
            bw.Write(eye.X); bw.Write(eye.Y); bw.Write(eye.Z);

            _eye ??= new EntityRenderer();
            // Drawn as if the visitor's own seat were the eye: the game then leaves that seat
            // out (Minecraft draws the visitor) and draws everybody else — the pilot's craft
            // included — as bodies seen from outside, with no first-person arms anywhere.
            int was = w.ViewSeat;
            SetViewSeat(w, v.SeatIndex);
            try
            {
                Capture.DrawList(() =>
                {
                    GridRenderer.Draw(v.Pos);
                    _eye.Draw(w, eye);
                }, bw);
            }
            finally { SetViewSeat(w, was); }
            bw.Flush();
            live.SendBin("scene", ms.GetBuffer().AsSpan(0, (int)ms.Length), droppable: true, "uuid", v.Uuid);
        }
    }

    private static readonly System.Reflection.PropertyInfo? ViewSeatProp = typeof(GameWorld).GetProperty(nameof(GameWorld.ViewSeat));

    private static void SetViewSeat(GameWorld w, int seat)
    {
        if (seat >= 0) ViewSeatProp?.SetValue(w, seat);
    }

    private static void Rgba(BinaryWriter w, Color c) { w.Write(c.R); w.Write(c.G); w.Write(c.B); w.Write(c.A); }

    // --- Drawing them, in UNRENDERED --------------------------------------------------

    public static bool AnyLookIn(GameWorld w) => _all.Values.Any(v => ReferenceEquals(v.World, w));

    /// <summary>Each visitor exactly as Minecraft draws them (keyed: as the composite's mask).</summary>
    internal static void Draw(GameWorld w, Vector3 cam, bool keyed = false)
    {
        var eyeXZ = new Vector2(cam.X, cam.Z);
        foreach (var v in _all.Values)
        {
            if (!ReferenceEquals(v.World, w)) continue;
            Vector2 at = Torus.NearestImage(v.Pos, eyeXZ);
            float fog = GridRenderer.FogFactor(Vector2.Distance(at, eyeXZ));
            if (fog >= 0.99f) continue;
            var feet = new Vector3(at.X, v.Y, at.Y);
            if (v.Look is { } look) look.Draw(feet, fog, Atmosphere.FogColor, keyed);
            else if (!keyed) Raylib.DrawCubeWires(feet + new Vector3(0, 0.9f, 0), 0.6f, 1.8f, 0.6f, Color.White);
        }
    }

    /// <summary>Flat, after the 3D pass: their name over their head, like Minecraft's.</summary>
    internal static void DrawTags(GameWorld w, Camera3D cam)
    {
        var eyeXZ = new Vector2(cam.Position.X, cam.Position.Z);
        Vector3 fwd = Vector3.Normalize(cam.Target - cam.Position);
        foreach (var v in _all.Values)
        {
            if (!ReferenceEquals(v.World, w)) continue;
            Vector2 at = Torus.NearestImage(v.Pos, eyeXZ);
            var head = new Vector3(at.X, v.Y + 2.2f, at.Y);
            if (Vector3.Dot(fwd, head - cam.Position) <= 0.2f) continue;
            if (Vector3.Distance(cam.Position, head) > 64f) continue;
            Vector2 s = Raylib.GetWorldToScreenEx(head, cam, Config.InternalWidth, Config.InternalHeight);
            int wpx = Font.Measure(v.Name, 1);
            Raylib.DrawRectangle((int)s.X - wpx / 2 - 2, (int)s.Y - 2, wpx + 4, 11, new Color(0, 0, 0, 96));
            Font.Centered(v.Name, (int)s.X, (int)s.Y, 1, Color.White);
        }
    }
}
