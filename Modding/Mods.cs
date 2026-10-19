using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Raylib_cs;
using Unrendered.Core;

namespace Unrendered.Modding;

/// <summary>
/// One file in the mods folder, and what became of it. A file that failed is kept in the
/// list rather than dropped, carrying the reason — the settings page shows it, because a mod
/// that silently does nothing is a mod its author spends an evening on.
/// </summary>
public sealed class ModInfo
{
    /// <summary>The file's own name, e.g. "swords.json".</summary>
    public required string File { get; init; }
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>Why it did not load, or null when it did.</summary>
    public string? Error { get; init; }

    public IReadOnlyList<ModWeapon> Weapons { get; init; } = Array.Empty<ModWeapon>();

    public bool Loaded => Error == null;

    internal static ModInfo Failed(string file, string why) => new() { File = file, Error = why };
}

/// <summary>
/// The mods folder and everything loaded out of it.
///
/// <para><b>A mod is one file.</b> A <c>.json</c> dropped into <see cref="Folder"/>, read at
/// boot and again whenever the MODS page is opened from the title menu. Comments and trailing
/// commas are allowed in it, so the file can document itself — the shipped
/// <c>swords.json</c> is written to be copied and edited.</para>
///
/// <para>What a file can add is what this loader knows how to read: today that is melee
/// weapons for the SOLDIER (<see cref="ModWeapon"/>). A mod cannot run code. That is the
/// price of one-file mods anyone can open in a text editor, and it is also what makes the
/// multiplayer rule simple — two machines with the same files are running the same game,
/// which <see cref="Fingerprint"/> checks at the door.</para>
///
/// <para>Static, like <see cref="Input.InputMap.Active"/>: there is one mods folder per
/// install and every part of the game reads the same registry. Never swapped mid-match —
/// a weapon's index is its byte on the wire, and a match that reloaded under itself would
/// hand players the wrong swords.</para>
/// </summary>
public static class Mods
{
    /// <summary>The current file format. A file asking for a newer one is refused by name
    /// rather than half-read.</summary>
    public const int Format = 1;

    /// <summary>A weapon's index travels as one byte with 0 meaning "nothing", so 254 is the
    /// most a whole install can register.</summary>
    public const int MaxWeapons = 254;

    private const int MaxParts = 64;
    private const float MaxExtent = 3f;

    /// <summary>
    /// Where mods live: a folder named Mods beside the game, created on first use so the
    /// settings page always has somewhere to open. <c>UNRENDERED_MODS_DIR</c> overrides it,
    /// for a test that must not read whatever the player happens to have installed.
    /// </summary>
    public static string Folder
        => Environment.GetEnvironmentVariable("UNRENDERED_MODS_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(AppContext.BaseDirectory, "Mods");

    /// <summary>Every file found on the last scan, loaded or not, in load order.</summary>
    public static IReadOnlyList<ModInfo> All { get; private set; } = Array.Empty<ModInfo>();

    /// <summary>Every weapon from every mod that loaded, indexed by <see cref="ModWeapon.Index"/>.</summary>
    public static IReadOnlyList<ModWeapon> Weapons { get; private set; } = Array.Empty<ModWeapon>();

    /// <summary>
    /// A hash of everything the loaded mods change, or 0 with none — which is also what a
    /// game from before mods existed sends, so an unmodded room still lets an old build in.
    /// Hashed off the <em>parsed</em> content rather than the bytes, so two copies of a file
    /// that differ only in a comment still match.
    /// </summary>
    public static uint Fingerprint { get; private set; }

    /// <summary>A few words saying what is installed — "SWORDS 1.0", "3 MODS", "NO MODS".
    /// Sent to a guest whose files do not match, so the refusal says what they are missing.</summary>
    public static string Summary { get; private set; } = "NO MODS";

    /// <summary>The weapon at a wire index, or null for -1 or anything out of range.</summary>
    public static ModWeapon? Weapon(int index)
        => (uint)index < (uint)Weapons.Count ? Weapons[index] : null;

    /// <summary>Rescans the folder and replaces the registry. Never throws: a folder that
    /// cannot be read is an install with no mods, not a game that will not start.</summary>
    public static void Reload(bool log = true)
    {
        var found = new List<ModInfo>();
        try
        {
            string folder = Folder;
            Directory.CreateDirectory(folder);
            var files = Directory.GetFiles(folder, "*.json")
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);
            foreach (string path in files)
            {
                string name = Path.GetFileName(path);
                string text;
                try { text = System.IO.File.ReadAllText(path); }
                catch (Exception e) { found.Add(ModInfo.Failed(name, "COULD NOT READ: " + e.Message)); continue; }
                found.Add(Parse(text, name));
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[mods] could not scan {Folder}: {e.Message}");
        }
        Install(found, log);
    }

    /// <summary>
    /// Makes a set of parsed files the live registry: numbers the weapons, drops a second
    /// file claiming an id the first already took, and recomputes the fingerprint. Public so
    /// a self-test can stand up an exact set of mods without touching the disk.
    /// </summary>
    public static void Install(IEnumerable<ModInfo> mods, bool log = true)
    {
        var all = new List<ModInfo>();
        var weapons = new List<ModWeapon>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods)
        {
            if (!mod.Loaded) { all.Add(mod); continue; }
            if (!ids.Add(mod.Id))
            {
                all.Add(ModInfo.Failed(mod.File, $"ANOTHER FILE IS ALREADY \"{mod.Id}\""));
                continue;
            }
            if (weapons.Count + mod.Weapons.Count > MaxWeapons)
            {
                all.Add(ModInfo.Failed(mod.File, $"TOO MANY WEAPONS INSTALLED (MAX {MaxWeapons})"));
                continue;
            }
            foreach (var w in mod.Weapons)
            {
                w.Index = weapons.Count;
                weapons.Add(w);
            }
            all.Add(mod);
        }

        All = all;
        Weapons = weapons;
        Fingerprint = Hash(all.Where(m => m.Loaded));

        var loaded = all.Where(m => m.Loaded).ToList();
        Summary = loaded.Count switch
        {
            0 => "NO MODS",
            1 => $"{loaded[0].Name} {loaded[0].Version}".Trim(),
            _ => $"{loaded.Count} MODS",
        };

        if (!log) return;
        foreach (var m in all)
            Console.WriteLine(m.Loaded
                ? $"[mods] loaded {m.File}: {m.Name} {m.Version} ({m.Weapons.Count} weapon(s))"
                : $"[mods] FAILED {m.File}: {m.Error}");
    }

    /// <summary>
    /// Opens the folder in the system's file browser. Returns false if it could not — the
    /// page says so rather than leaving a player clicking a row that does nothing.
    /// </summary>
    public static bool OpenFolder()
    {
        try
        {
            string folder = Folder;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[mods] could not open {Folder}: {e.Message}");
            return false;
        }
    }

    // --- Reading a file ------------------------------------------------------------------

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Reads one mod file. Never throws: anything wrong with it comes back as a failed
    /// <see cref="ModInfo"/> whose error names the field, because "invalid mod" is no use to
    /// somebody looking at forty lines of JSON.
    /// </summary>
    public static ModInfo Parse(string json, string fileName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, ReadOptions);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ModInfo.Failed(fileName, "THE FILE MUST BE ONE { } OBJECT");

            int format = (int)Number(root, "format", 1, 1, 1000);
            if (format > Format)
                return ModInfo.Failed(fileName, $"NEEDS A NEWER GAME (FORMAT {format})");

            string id = Text(root, "id") ?? Path.GetFileNameWithoutExtension(fileName);
            id = id.Trim().ToLowerInvariant();
            if (!IsId(id))
                return ModInfo.Failed(fileName, "ID MAY ONLY USE a-z 0-9 _ -");

            var weapons = new List<ModWeapon>();
            if (root.TryGetProperty("weapons", out JsonElement list))
            {
                if (list.ValueKind != JsonValueKind.Array)
                    return ModInfo.Failed(fileName, "\"weapons\" MUST BE A [ ] LIST");
                int i = 0;
                var seen = new HashSet<string>();
                foreach (JsonElement w in list.EnumerateArray())
                {
                    var weapon = ReadWeapon(w, id, $"weapons[{i}]");
                    if (!seen.Add(weapon.Id))
                        throw new ModError($"weapons[{i}]: \"{weapon.Id}\" IS USED TWICE");
                    weapons.Add(weapon);
                    i++;
                }
            }

            return new ModInfo
            {
                File = fileName,
                Id = id,
                Name = Upper(Text(root, "name") ?? id, 24),
                Version = Upper(Text(root, "version") ?? "", 10),
                Author = Upper(Text(root, "author") ?? "", 24),
                Description = Upper(Text(root, "description") ?? "", 160),
                Weapons = weapons,
            };
        }
        catch (JsonException e)
        {
            // System.Text.Json's own message carries the line and column, which is the most
            // useful thing anybody fixing a missing comma can be told.
            return ModInfo.Failed(fileName, "NOT VALID JSON: " + e.Message);
        }
        catch (ModError e)
        {
            return ModInfo.Failed(fileName, e.Message);
        }
    }

    private static ModWeapon ReadWeapon(JsonElement w, string modId, string where)
    {
        if (w.ValueKind != JsonValueKind.Object)
            throw new ModError($"{where}: MUST BE A {{ }} OBJECT");

        string id = (Text(w, "id") ?? "").Trim().ToLowerInvariant();
        if (!IsId(id)) throw new ModError($"{where}: NEEDS AN \"id\" (a-z 0-9 _ -)");

        int maxTargets = (int)Number(w, "maxTargets", 3, 1, 16, where);
        float arcDegrees = Number(w, "arc", 110f, 10f, 360f, where);

        float field = 0f, drop = 0f;
        if (w.TryGetProperty("spawn", out JsonElement spawn))
        {
            field = Number(spawn, "field", 0f, 0f, 6f, where + ".spawn");
            drop = Number(spawn, "soldierDrop", 0f, 0f, 1f, where + ".spawn");
        }

        Cue swing = Cue.ThrowWhoosh, hit = Cue.ClawSlam;
        if (w.TryGetProperty("sounds", out JsonElement sounds))
        {
            swing = Sound(sounds, "swing", swing, where);
            hit = Sound(sounds, "hit", hit, where);
        }

        ModPart[] model = w.TryGetProperty("model", out JsonElement m)
            ? ReadModel(m, where)
            : DefaultBlade;
        if (model.Length == 0) model = DefaultBlade;

        // The tip is however far the model runs out along the blade's axis, and the spark is
        // the colour of whatever part reaches it.
        float length = 0f;
        Color spark = model[0].Color;
        foreach (var part in model)
        {
            float reach = MathF.Max(part.From.Y, part.To.Y);
            if (reach > length) { length = reach; spark = part.Color; }
        }

        return new ModWeapon
        {
            ModId = modId,
            Id = id,
            Name = Upper(Text(w, "name") ?? id, 16),
            Damage = Number(w, "damage", 3f, 0f, 100f, where),
            PlayerDamage = Number(w, "playerDamage", 25f, 0f, 200f, where),
            Reach = Number(w, "reach", 3.2f, 0.5f, 8f, where),
            HalfArc = arcDegrees * 0.5f * MathF.PI / 180f,
            Cooldown = Number(w, "cooldown", 0.55f, 0.1f, 5f, where),
            MaxTargets = maxTargets,
            FieldCount = (int)field,
            SoldierDrop = drop,
            SwingCue = swing,
            HitCue = hit,
            Model = model,
            Length = MathF.Max(0.1f, length),
            Spark = spark,
        };
    }

    private static ModPart[] ReadModel(JsonElement m, string where)
    {
        if (m.ValueKind != JsonValueKind.Array)
            throw new ModError($"{where}.model: MUST BE A [ ] LIST OF BOXES");

        var parts = new List<ModPart>();
        int i = 0;
        foreach (JsonElement box in m.EnumerateArray())
        {
            string at = $"{where}.model[{i}]";
            if (parts.Count >= MaxParts) throw new ModError($"{where}.model: AT MOST {MaxParts} BOXES");
            if (box.ValueKind != JsonValueKind.Object) throw new ModError($"{at}: MUST BE A {{ }} OBJECT");

            Vector3 from = Point(box, "from", at);
            Vector3 to = Point(box, "to", at);
            Color color = box.TryGetProperty("color", out JsonElement c)
                ? Colour(c, at)
                : Palette.SoldierBlade;
            parts.Add(new ModPart(Vector3.Min(from, to), Vector3.Max(from, to), color));
            i++;
        }
        return parts.ToArray();
    }

    /// <summary>A plain straight blade, for a weapon that names no model of its own.</summary>
    private static readonly ModPart[] DefaultBlade =
    {
        new(new Vector3(-0.025f, -0.12f, -0.025f), new Vector3(0.025f, 0.02f, 0.025f), Palette.SoldierWebbing),
        new(new Vector3(-0.09f, 0.02f, -0.03f), new Vector3(0.09f, 0.05f, 0.03f), Palette.SoldierSteel),
        new(new Vector3(-0.03f, 0.05f, -0.008f), new Vector3(0.03f, 0.62f, 0.008f), Palette.SoldierBlade),
    };

    // --- Field readers ---------------------------------------------------------------------

    private static string? Text(JsonElement o, string name)
        => o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static float Number(JsonElement o, string name, float fallback, float min, float max,
        string where = "")
    {
        if (!o.TryGetProperty(name, out JsonElement v)) return fallback;
        if (v.ValueKind != JsonValueKind.Number)
            throw new ModError($"{Prefix(where)}\"{name}\" MUST BE A NUMBER");
        float f = (float)v.GetDouble();
        if (!float.IsFinite(f) || f < min || f > max)
            throw new ModError($"{Prefix(where)}\"{name}\" MUST BE {Show(min)} TO {Show(max)}");
        return f;
    }

    private static Vector3 Point(JsonElement o, string name, string where)
    {
        if (!o.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.Array
            || v.GetArrayLength() != 3)
            throw new ModError($"{where}: \"{name}\" MUST BE [x, y, z]");

        Span<float> xyz = stackalloc float[3];
        int i = 0;
        foreach (JsonElement n in v.EnumerateArray())
        {
            if (n.ValueKind != JsonValueKind.Number)
                throw new ModError($"{where}: \"{name}\" MUST BE THREE NUMBERS");
            float f = (float)n.GetDouble();
            if (!float.IsFinite(f) || MathF.Abs(f) > MaxExtent)
                throw new ModError($"{where}: \"{name}\" MUST STAY WITHIN {Show(MaxExtent)} OF THE HAND");
            xyz[i++] = f;
        }
        return new Vector3(xyz[0], xyz[1], xyz[2]);
    }

    private static Color Colour(JsonElement v, string where)
    {
        string? hex = v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;
        if (hex is { Length: 7 } && hex[0] == '#'
            && uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            return new Color((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)255);
        throw new ModError($"{where}: \"color\" MUST LOOK LIKE \"#C8D0D8\"");
    }

    private static Cue Sound(JsonElement o, string name, Cue fallback, string where)
    {
        string? text = Text(o, name);
        if (text == null) return fallback;
        if (Enum.TryParse(text, ignoreCase: true, out Cue cue) && cue != Cue.None
            && Enum.IsDefined(cue))
            return cue;
        throw new ModError($"{where}.sounds: NO SOUND CALLED \"{text}\"");
    }

    private static bool IsId(string id)
        => id.Length is > 0 and <= 32
           && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-');

    /// <summary>Upper-cased and cut to length, because the game's font is capitals and the
    /// screen is 320 pixels wide.</summary>
    private static string Upper(string s, int max)
    {
        s = s.Trim().ToUpperInvariant();
        return s.Length <= max ? s : s[..max];
    }

    private static string Prefix(string where) => where.Length > 0 ? where + ": " : "";
    private static string Show(float f) => f.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A problem with a file, already worded for the settings page.</summary>
    private sealed class ModError(string message) : Exception(message);

    // --- The fingerprint ------------------------------------------------------------------

    private static uint Hash(IEnumerable<ModInfo> loaded)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        foreach (var mod in loaded)
        {
            sb.Append(mod.Id).Append('|').Append(mod.Version).Append('|');
            foreach (var w in mod.Weapons)
            {
                sb.Append(w.Id).Append(',').Append(w.Name).Append(',')
                  .Append(w.Damage.ToString("R", inv)).Append(',')
                  .Append(w.PlayerDamage.ToString("R", inv)).Append(',')
                  .Append(w.Reach.ToString("R", inv)).Append(',')
                  .Append(w.HalfArc.ToString("R", inv)).Append(',')
                  .Append(w.Cooldown.ToString("R", inv)).Append(',')
                  .Append(w.MaxTargets).Append(',').Append(w.FieldCount).Append(',')
                  .Append(w.SoldierDrop.ToString("R", inv)).Append(',')
                  .Append((int)w.SwingCue).Append(',').Append((int)w.HitCue).Append(',')
                  .Append(w.Model.Length).Append(';');
            }
            sb.Append('#');
        }
        if (sb.Length == 0) return 0u;

        // FNV-1a. Not cryptographic and not meant to be — it answers "are these the same
        // files", between two people who both want the answer to be yes.
        uint h = 2166136261u;
        foreach (byte b in Encoding.UTF8.GetBytes(sb.ToString()))
        {
            h ^= b;
            h *= 16777619u;
        }
        return h == 0u ? 1u : h;
    }
}
