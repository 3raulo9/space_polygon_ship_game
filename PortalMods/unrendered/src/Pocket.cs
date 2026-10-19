using System.Numerics;
using System.Text.Json.Nodes;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Modding;
using Unrendered.Rendering;

namespace UnrenderedPortal;

/// <summary>
/// What the pilot has carried out of Minecraft. Anything UNRENDERED already has a name for
/// (its own salvage come home as relics, a few plain metals) goes straight into the pack.
/// Everything else is a Minecraft item, and stays one: it sits in this pocket with its
/// Minecraft data untouched, it can be eaten, swung or carried, and it can be dropped back
/// into Minecraft exactly as it came out.
///
/// O cycles, P uses, I drops (all rebindable in portal.cfg). The pocket survives quitting —
/// it lives in Mods/Portal/pocket.json.
/// </summary>
public static class Pocket
{
    public sealed class Item
    {
        public required JsonNode Stack;   // Minecraft's own encoding, opaque here
        public required string Id;
        public required string Name;
        public int Count;
        public string Tex = "";
        public float Attack;              // MC attack damage, 0 if not a weapon
        public int Food;                  // MC nutrition, 0 if not food
        public float Speed = 1.6f;        // MC attack speed
    }

    private static readonly List<Item> _items = new();
    private static int _sel;
    private static bool _loaded;

    public static IReadOnlyList<Item> Items => _items;
    public static Item? Selected => _items.Count == 0 ? null : _items[Math.Clamp(_sel, 0, _items.Count - 1)];

    private static string File => Path.Combine(Rift.Dir, "pocket.json");

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!System.IO.File.Exists(File) || JsonNode.Parse(System.IO.File.ReadAllText(File)) is not JsonArray arr) return;
            foreach (var n in arr)
                if (n is JsonObject o && FromJson(o) is { } it) _items.Add(it);
        }
        catch (Exception e) { Rift.Log("pocket: " + e.Message); }
    }

    private static void Save()
    {
        try
        {
            var arr = new JsonArray();
            foreach (var it in _items)
                arr.Add(new JsonObject
                {
                    ["stack"] = it.Stack.DeepClone(), ["item"] = it.Id, ["name"] = it.Name, ["count"] = it.Count,
                    ["tex"] = it.Tex, ["attack"] = it.Attack, ["food"] = it.Food, ["speed"] = it.Speed,
                });
            System.IO.File.WriteAllText(File, arr.ToJsonString());
        }
        catch (Exception e) { Rift.Log("pocket save: " + e.Message); }
    }

    private static Item? FromJson(JsonObject o)
    {
        if (o["stack"] is not { } stack) return null;
        return new Item
        {
            Stack = stack.DeepClone(),
            Id = o["item"]?.GetValue<string>() ?? "minecraft:air",
            Name = (o["name"]?.GetValue<string>() ?? "ITEM").ToUpperInvariant(),
            Count = o["count"]?.GetValue<int>() ?? 1,
            Tex = o["tex"]?.GetValue<string>() ?? "",
            Attack = o["attack"]?.GetValue<float>() ?? 0,
            Food = o["food"]?.GetValue<int>() ?? 0,
            Speed = o["speed"]?.GetValue<float>() ?? 1.6f,
        };
    }

    internal static bool Handle(Msg m)
    {
        if (m.T != "got") return false;
        Load();
        var it = FromJson(m.J);
        if (it == null) return true;

        // Salvage UNRENDERED knows goes into the pack proper.
        if (UnrenderedPortal.Items.Salvage(it.Id) is { } kind && Rift.World is { } w)
        {
            int left = w.Inventory.Add(kind, it.Count);
            int took = it.Count - left;
            if (took > 0)
            {
                CueBank.Play(Cue.Pickup, w.Player.Position, 0f);
                Rift.Notice($"+{took} {ItemNames.Of(kind)}");
            }
            if (left <= 0) return true;
            it.Count = left;
        }

        _items.Add(it);
        _sel = _items.Count - 1;
        Save();
        if (Rift.World is { } world) CueBank.Play(Cue.Pickup, world.Player.Position, 0f);
        Rift.Notice($"+{it.Count} {it.Name} (MINECRAFT)");
        Textures.Get(it.Tex);
        return true;
    }

    internal static void Keys()
    {
        Load();
        if (_items.Count == 0) return;
        if (Raylib.IsKeyPressed(Rift.Cfg.NextKey)) _sel = (_sel + 1) % _items.Count;
        if (Raylib.IsKeyPressed(Rift.Cfg.UseKey)) Use();
        if (Raylib.IsKeyPressed(Rift.Cfg.DropKey)) Drop();
    }

    internal static void UseSelected() => Use();

    private static void Use()
    {
        if (Selected is not { } it || Rift.World is not { } w) return;
        var craft = w.Player;
        if (it.Food > 0)
        {
            // Minecraft food mends the hull: a steak is worth a good chunk of a repair kit.
            float mend = it.Food * 2.5f;
            craft.Health = MathF.Min(craft.MaxHealth, craft.Health + mend);
            Spend(it, 1);
            CueBank.Play(Cue.Pickup, craft.Position, 0f);
            Rift.Notice($"ATE {it.Name}");
            return;
        }
        if (it.Attack > 0)
        {
            if (craft.Soldier == null)
            {
                Rift.Notice($"ONLY A SOLDIER CAN SWING A {it.Name}");
                return;
            }
            var weapon = Arsenal.For(it);
            if (weapon == null) { Rift.Notice($"{it.Name} WILL NOT TAKE SHAPE YET — TRY AGAIN"); return; }
            craft.HeldWeapon = weapon.Index;
            Rift.Notice($"{it.Name} IN HAND — FIRE SWINGS IT");
            return;
        }
        Rift.Notice($"{it.Name} MEANS NOTHING HERE — DROP IT IN MINECRAFT WITH {Rift.Cfg.DropKey}");
    }

    private static void Drop()
    {
        if (Selected is not { } it) return;
        if (!Realm.Active || Rift.Live is not { } live || Rift.World is not { } w)
        {
            Rift.Notice("THERE IS NOWHERE TO PUT A MINECRAFT ITEM DOWN HERE");
            return;
        }
        var craft = w.Player;
        var at = Realm.Mc(craft.Position + craft.Forward * 1.6f, craft.Height + 0.5f);
        live.Send("drop", "stack", it.Stack, "x", (double)at.X, "y", (double)at.Y, "z", (double)at.Z);
        if (craft.HeldWeapon >= 0 && Arsenal.IsFrom(craft.HeldWeapon, it)) craft.HeldWeapon = -1;
        _items.Remove(it);
        _sel = Math.Max(0, _sel - 1);
        Save();
        Rift.Notice($"DROPPED {it.Name} INTO MINECRAFT");
    }

    private static void Spend(Item it, int n)
    {
        it.Count -= n;
        if (it.Count <= 0) _items.Remove(it);
        else if (it.Stack is JsonObject o) o["count"] = it.Count;
        _sel = Math.Clamp(_sel, 0, Math.Max(0, _items.Count - 1));
        Save();
    }

    /// <summary>Bottom-left, over the game's own HUD: the selected Minecraft item, framed
    /// like a slot of Minecraft's hotbar.</summary>
    internal static void Draw()
    {
        Load();
        if (Selected is not { } it) return;
        const int x = 6, w = 156, h = 30;
        int y = Config.InternalHeight - 80;
        Raylib.DrawRectangle(x - 2, y - 2, w, h, new Color(5, 7, 10, 180));
        Raylib.DrawRectangleLines(x - 2, y - 2, w, h, new Color(139, 139, 139, 255));
        Raylib.DrawRectangleLines(x - 1, y - 1, 24, 24, new Color(55, 55, 55, 255));
        if (Textures.Get(it.Tex) is { } tex)
            Raylib.DrawTexturePro(tex.Gpu, new Rectangle(0, 0, tex.W, tex.H), new Rectangle(x + 1, y + 1, 20, 20),
                Vector2.Zero, 0f, Color.White);
        string name = Font.Clean(it.Name);
        if (name.Length > 21) name = name[..21];
        Font.Draw(name, x + 26, y + 2, 1, Color.White);
        Font.Draw($"X{it.Count}  {_sel + 1}/{_items.Count}", x + 26, y + 12, 1, new Color(170, 170, 170, 255));
        Font.Draw($"{Rift.Cfg.NextKey}:NEXT {Rift.Cfg.UseKey}:USE {Rift.Cfg.DropKey}:DROP", x, y + h + 1, 1,
            new Color(120, 130, 140, 255));
    }
}

/// <summary>
/// Minecraft weapons, made into UNRENDERED weapons. A sword carried out of Minecraft — any
/// sword, including one a Minecraft mod added — is rebuilt as one of UNRENDERED's own modded
/// melee weapons: its model voxelised pixel by pixel from Minecraft's own item texture, its
/// damage and swing taken from its Minecraft attributes, and handed to the game's mod
/// registry like any weapon from a .json file. The SOLDIER swings it with the game's own
/// melee code.
/// </summary>
public static class Arsenal
{
    private static readonly List<(string Key, ModWeapon Weapon)> _made = new();

    public static bool IsFrom(int index, Pocket.Item it)
        => _made.Any(m => m.Weapon.Index == index && m.Key == Key(it));

    private static string Key(Pocket.Item it) => it.Id + "|" + it.Tex;

    public static ModWeapon? For(Pocket.Item it)
    {
        string key = Key(it);
        foreach (var (k, w) in _made) if (k == key && Mods.Weapons.Contains(w)) return w;
        var tex = Textures.Get(it.Tex);
        if (tex == null || tex.Missing) return null;

        var parts = Voxelise(tex, out float length, out Color spark);
        string id = "mc" + _made.Count;
        var weapon = new ModWeapon
        {
            ModId = "rift",
            Id = id,
            Name = it.Name.Length > 18 ? it.Name[..18] : it.Name,
            Damage = it.Attack * Visitors.McToUnr * 2f,   // the soldier swings for both hands' worth
            PlayerDamage = it.Attack * 5f,
            Reach = 3f,
            HalfArc = 50f * MathF.PI / 180f,
            Cooldown = Math.Clamp(1f / MathF.Max(0.25f, it.Speed), 0.15f, 3f),
            MaxTargets = 3,
            FieldCount = 0,
            SoldierDrop = 0f,
            Model = parts,
            Length = length,
            Spark = spark,
        };
        _made.Add((key, weapon));

        // Into the game's registry, after whatever the Mods folder installed, so every index
        // the game already handed out stays exactly what it was.
        var rift = new ModInfo
        {
            File = "the-rift (minecraft)", Id = "rift", Name = "THE RIFT", Version = Rift.Version,
            Author = "THE RIFT", Description = "Weapons carried out of Minecraft.",
            Weapons = _made.Select(m => m.Weapon).ToList(),
        };
        Mods.Install(Mods.All.Where(m => m.Id != "rift").Append(rift), log: false);
        Rift.Log($"arsenal: {weapon.Name} from {it.Tex} as weapon #{weapon.Index} ({parts.Length} boxes)");
        return weapon;
    }

    /// <summary>
    /// A 16×16 item sprite, stood on its diagonal and turned into boxes: Minecraft draws a
    /// sword from the bottom-left (the grip) to the top-right (the tip), which is exactly the
    /// weapon frame's +Y. Neighbouring pixels of one colour along a row are merged, so a blade
    /// is tens of boxes rather than hundreds.
    /// </summary>
    private static unsafe ModPart[] Voxelise(Textures.Tex tex, out float length, out Color spark)
    {
        const float Px = 0.062f;        // metres per pixel along the diagonal
        int w = tex.W, h = tex.H;
        byte* d = (byte*)tex.Cpu.Data;
        Color At(int x, int y) { byte* p = d + (y * w + x) * 4; return new Color(p[0], p[1], p[2], p[3]); }

        // The grip: the opaque pixel nearest the bottom-left corner.
        int gx = 0, gy = h - 1, best = int.MaxValue;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (At(x, y).A > 128 && x + (h - 1 - y) < best) { best = x + (h - 1 - y); gx = x; gy = y; }

        var parts = new List<ModPart>();
        length = 0.3f;
        spark = Color.White;
        float far = -1f;
        const float S = 0.70710678f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color c = At(x, y);
                if (c.A <= 128) continue;
                float ux = (x - gx) * Px, uy = ((h - 1 - y) - (h - 1 - gy)) * Px;
                float along = (ux + uy) * S, across = (ux - uy) * S;
                float hs = Px * 0.5f;
                parts.Add(new ModPart(new Vector3(across - hs, along - hs, -0.02f),
                    new Vector3(across + hs, along + hs, 0.02f), new Color(c.R, c.G, c.B, (byte)255)));
                if (along > far) { far = along; spark = c; }
            }
        // The weapon's own model is capped well under what a 16x16 sprite can produce, so
        // keep the most important part — everything — but thin out if it is a huge texture.
        if (parts.Count > 256) parts = parts.Where((_, i) => i % (parts.Count / 256 + 1) == 0).ToList();
        length = MathF.Max(0.3f, far + Px);
        return parts.ToArray();
    }
}
