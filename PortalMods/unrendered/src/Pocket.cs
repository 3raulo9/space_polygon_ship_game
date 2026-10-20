using System.Numerics;
using System.Text.Json.Nodes;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Modding;
using Unrendered.Rendering;

namespace UnrenderedPortal;

/// <summary>
/// What the pilot carries out of Minecraft goes into UNRENDERED's own pack. Anything
/// UNRENDERED already has a name for (its own salvage come home as relics, a few plain
/// metals) lands as that salvage. Everything else stays a Minecraft item: each kind of it
/// becomes a kind the pack holds (see <see cref="ModItems"/>), with its Minecraft data
/// untouched, Minecraft's own picture in the slot, Minecraft's stack size, and the panel's
/// own moves — drag it, right-click to eat or take up a weapon, throw it out. Thrown, it goes
/// back into Minecraft exactly as it came out.
/// </summary>
public static class Pocket
{
    public sealed class Item
    {
        public required JsonNode Stack;   // Minecraft's own encoding, count left out; opaque here
        public required string Id;
        public required string Name;
        public int Max = 64;
        public string Tex = "";
        public float Attack;              // MC attack damage, 0 if not a weapon
        public int Food;                  // MC nutrition, 0 if not food
        public float Speed = 1.6f;        // MC attack speed
        public ItemKind Kind;
    }

    private static Item? FromJson(JsonObject o)
    {
        if (o["stack"] is not JsonObject stack) return null;
        var bare = (JsonObject)stack.DeepClone();
        bare.Remove("count");
        bool weapon = (o["attack"]?.GetValue<float>() ?? 0) > 0;
        return new Item
        {
            Stack = bare,
            Id = o["item"]?.GetValue<string>() ?? "minecraft:air",
            Name = (o["name"]?.GetValue<string>() ?? "ITEM").ToUpperInvariant(),
            Max = Math.Clamp(o["max"]?.GetValue<int>() ?? (weapon ? 1 : 64), 1, 99),
            Tex = o["tex"]?.GetValue<string>() ?? "",
            Attack = o["attack"]?.GetValue<float>() ?? 0,
            Food = o["food"]?.GetValue<int>() ?? 0,
            Speed = o["speed"]?.GetValue<float>() ?? 1.6f,
        };
    }

    /// <summary>The pack's kind for this Minecraft item — one per item and its data, so two
    /// differently enchanted swords are two kinds and never stack into one.</summary>
    private static ModItem? KindOf(Item it)
    {
        string key = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes(it.Stack.ToJsonString())))[..16];
        if (ModItems.Find("rift", key) is { } had) return had;
        Textures.Get(it.Tex);
        var made = ModItems.Register(new ModItem
        {
            ModId = "rift", Id = key, Name = Font.Clean(it.Name), MaxStack = it.Max, Tag = it,
            DrawIcon = (size, _) => Icon(it, size),
            Use = (w, craft, _) => Use(it, w, craft),
            Thrown = (w, craft, at) => Throw(it, w, craft),
        });
        if (made != null) it.Kind = made.Kind;
        return made;
    }

    /// <summary>Minecraft's own sprite, filling the slot the way Minecraft's GUI fills one.</summary>
    private static void Icon(Item it, int size)
    {
        if (Textures.Get(it.Tex) is not { Missing: false } tex) return;
        int m = size / 14;
        Raylib.DrawTexturePro(tex.Gpu, new Rectangle(0, 0, tex.W, tex.H), new Rectangle(m, m, size - 2 * m, size - 2 * m),
            Vector2.Zero, 0f, Color.White);
    }

    internal static bool Handle(Msg m)
    {
        if (m.T != "got") return false;
        var it = FromJson(m.J);
        int count = m.J["count"]?.GetValue<int>() ?? 1;
        if (it == null || Rift.World is not { } w) return true;

        ItemKind? kind = UnrenderedPortal.Items.Salvage(it.Id) ?? KindOf(it)?.Kind;
        if (kind is not { } k) { Back(it, count, w); return true; }
        int left = w.Inventory.Add(k, count);
        int took = count - left;
        if (took > 0)
        {
            CueBank.Play(Cue.Pickup, w.Player.Position, 0f);
            Rift.Notice($"+{took} {ItemNames.Of(k)}");
        }
        // What the pack has no room for goes back where it came from, not into the void.
        if (left > 0)
        {
            Back(it, left, w);
            Rift.Notice("PACK FULL");
        }
        return true;
    }

    private static bool Use(Item it, Unrendered.World.World w, Unrendered.Entities.PlayerTank craft)
    {
        if (it.Food > 0)
        {
            // Minecraft food mends the hull: a steak is worth a good chunk of a repair kit.
            if (craft.Health >= craft.MaxHealth) { Rift.Notice("NOT HUNGRY"); return false; }
            craft.Health = MathF.Min(craft.MaxHealth, craft.Health + it.Food * 2.5f);
            CueBank.Play(Cue.Pickup, craft.Position, 0f);
            Rift.Notice($"ATE {it.Name}");
            return true;
        }
        if (it.Attack > 0)
        {
            if (craft.Soldier == null) { Rift.Notice($"ONLY A SOLDIER CAN SWING A {it.Name}"); return false; }
            var weapon = Arsenal.For(it);
            if (weapon == null) { Rift.Notice($"{it.Name}: TRY AGAIN"); return false; }
            craft.HeldWeapon = weapon.Index;
            Rift.Notice($"{it.Name} IN HAND");
            return false;   // held, not spent: it stays in the pack
        }
        Rift.Notice($"NO USE FOR {it.Name} HERE");
        return false;
    }

    /// <summary>Thrown out of the pack. In Minecraft it lands at the craft's feet; at home it
    /// lands in the rift, which is this same field as Minecraft holds it.</summary>
    private static void Throw(Item it, Unrendered.World.World w, Unrendered.Entities.PlayerTank craft)
    {
        if (craft.HeldWeapon >= 0 && Arsenal.IsFrom(craft.HeldWeapon, it)
            && !w.Inventory.Slots.Any(s => !s.IsEmpty && s.Kind == it.Kind)) craft.HeldWeapon = -1;
        Back(it, 1, w);
    }

    private static void Back(Item it, int count, Unrendered.World.World w)
    {
        var craft = w.Player;
        var stack = (JsonObject)it.Stack.DeepClone();
        stack["count"] = count;
        if (Rift.Live is not { } live)
        {
            // Nowhere to put it down: it stays in the pack.
            if (it.Kind != default) w.Inventory.Add(it.Kind, count);
            Rift.Notice("NO MINECRAFT TO DROP IT IN");
            return;
        }
        var ahead = craft.Position + craft.Forward * 1.6f;
        if (Realm.Active)
        {
            var at = Realm.Mc(ahead, craft.Height + 0.5f);
            live.Send("drop", "stack", stack, "x", (double)at.X, "y", (double)at.Y, "z", (double)at.Z);
        }
        else
            live.Send("drop", "stack", stack, "rift", true, "x", (double)ahead.X, "y", (double)craft.Height + 0.5, "z", (double)ahead.Y);
    }

    /// <summary>The first Minecraft item in the pack, for the test hatch.</summary>
    internal static int FirstSlot(Unrendered.World.World w)
        => Array.FindIndex(w.Inventory.Slots, s => !s.IsEmpty && ModItems.Get(s.Kind)?.Tag is Item);
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
