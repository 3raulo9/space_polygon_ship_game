using System.Globalization;
using Raylib_cs;

namespace UnrenderedPortal;

/// <summary>
/// Mods/Portal/portal.cfg — <c>key = value</c> lines, <c>#</c> comments. Every line is
/// optional; a missing or broken file just means the defaults.
/// </summary>
public sealed class RiftConfig
{
    /// <summary>Opens the portal / JOIN WORLDS. B by default, as asked for.</summary>
    public KeyboardKey PortalKey = KeyboardKey.B;

    /// <summary>Skip discovery and go straight to this Minecraft bridge.</summary>
    public (string Host, int Port)? Connect;

    /// <summary>The name Minecraft knows the pilot by.</summary>
    public string Name = Environment.UserName.ToUpperInvariant();

    /// <summary>How often UNRENDERED's view is sent to each Minecraft visitor.</summary>
    public int SceneHz = 12;

    /// <summary>Whether the pilot's rounds break Minecraft blocks. The Minecraft side has
    /// the final say (it can refuse in its own config).</summary>
    public bool BreakBlocks = true;

    /// <summary>Where Minecraft lives, for the glowstone and water textures the portal
    /// wears before any world is linked. Found automatically on the usual paths.</summary>
    public string? MinecraftDir;

    /// <summary>Writes the commented default file the first time, so there is something to
    /// open and edit in Mods/rift.</summary>
    public static void WriteDefault(string path)
    {
        if (File.Exists(path)) return;
        try { File.WriteAllText(path, Default); }
        catch (Exception e) { Rift.Log("portal.cfg: " + e.Message); }
    }

    private const string Default = """
        # THE RIFT - settings. Every line is optional.

        # Keys (a letter, or a raylib key name such as F6).
        portal_key = B        # open the portal / JOIN WORLDS

        # The name Minecraft players see over your craft.
        # name = PILOT

        # Skip LAN discovery and go straight to a Minecraft world running the rift mod.
        # connect = 192.168.1.20:25570

        # Whether your rounds break Minecraft blocks (Minecraft can still refuse).
        break_blocks = true

        # How many times a second Minecraft visitors are sent UNRENDERED's view (2-30).
        scene_hz = 12

        # Where Minecraft is installed, if not in the usual place. Only used for the portal's
        # glowstone and water before a world is linked.
        # minecraft_dir = C:\Users\you\AppData\Roaming\.minecraft
        """;

    public static RiftConfig Load(string path)
    {
        var c = new RiftConfig();
        if (!File.Exists(path)) return c;
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim().ToLowerInvariant();
            string val = line[(eq + 1)..].Trim();
            if (val.Length == 0) continue;
            try
            {
                switch (key)
                {
                    case "portal_key": c.PortalKey = Key(val); break;
                    case "name": c.Name = val.ToUpperInvariant(); break;
                    case "scene_hz": c.SceneHz = Math.Clamp(int.Parse(val, CultureInfo.InvariantCulture), 2, 30); break;
                    case "break_blocks": c.BreakBlocks = val is "1" or "true" or "yes"; break;
                    case "minecraft_dir": c.MinecraftDir = val; break;
                    case "connect":
                        int colon = val.LastIndexOf(':');
                        c.Connect = colon > 0
                            ? (val[..colon], int.Parse(val[(colon + 1)..], CultureInfo.InvariantCulture))
                            : (val, 25570);
                        break;
                }
            }
            catch (Exception e) { Rift.Log($"portal.cfg: '{raw}' — {e.Message}"); }
        }
        return c;
    }

    private static KeyboardKey Key(string name)
    {
        if (name.Length == 1 && char.IsLetterOrDigit(name[0]))
            return (KeyboardKey)char.ToUpperInvariant(name[0]);
        return Enum.Parse<KeyboardKey>(name, ignoreCase: true);
    }
}
