using Unrendered.Core;

namespace UnrenderedPortal;

/// <summary>
/// The two games' items, side by side. UNRENDERED salvage that crosses into Minecraft becomes
/// one of the rift mod's relic items (<c>unrendered_portal:scrap_metal</c>…), which Minecraft
/// can craft with, use and carry home. A relic that comes back through the rift becomes the
/// salvage it always was. And a handful of plain Minecraft materials are close enough to
/// UNRENDERED's own that a pilot's pack simply accepts them.
/// </summary>
public static class Items
{
    private static readonly (ItemKind Kind, string Id)[] Relics =
    {
        (ItemKind.Battery, "battery"),
        (ItemKind.Bullet, "bullet"),
        (ItemKind.CrabFragment, "crab_fragment"),
        (ItemKind.CrabCore, "crab_core"),
        (ItemKind.ScrapMetal, "scrap_metal"),
        (ItemKind.CopperWire, "copper_wire"),
        (ItemKind.SpaceGunpowder, "space_gunpowder"),
        (ItemKind.DenseAlloy, "dense_alloy"),
        (ItemKind.Lead, "lead"),
        (ItemKind.Zinc, "zinc"),
        (ItemKind.Lithium, "lithium"),
        (ItemKind.RepairKit, "repair_kit"),
        (ItemKind.Moonstone, "moonstone"),
        (ItemKind.SunFragment, "sun_fragment"),
        (ItemKind.MoonFragment, "moon_fragment"),
    };

    /// <summary>The relic's id (without namespace) for a piece of UNRENDERED salvage.</summary>
    public static string Id(ItemKind kind)
    {
        foreach (var (k, id) in Relics) if (k == kind) return id;
        return "scrap_metal";
    }

    /// <summary>
    /// What a Minecraft item is, in UNRENDERED, if it is anything at all. Relics are
    /// themselves; a few vanilla materials map onto the salvage they plainly are. Everything
    /// else — swords, food, blocks, modded items — stays a Minecraft item in the pilot's
    /// pocket.
    /// </summary>
    public static ItemKind? Salvage(string mcId)
    {
        const string Ns = "unrendered_portal:";
        if (mcId.StartsWith(Ns))
        {
            string id = mcId[Ns.Length..];
            foreach (var (k, rid) in Relics) if (rid == id) return k;
        }
        return mcId switch
        {
            "minecraft:iron_nugget" or "minecraft:raw_iron" => ItemKind.ScrapMetal,
            "minecraft:iron_ingot" or "minecraft:netherite_scrap" => ItemKind.DenseAlloy,
            "minecraft:copper_ingot" or "minecraft:raw_copper" => ItemKind.CopperWire,
            "minecraft:gunpowder" => ItemKind.SpaceGunpowder,
            "minecraft:amethyst_shard" => ItemKind.Moonstone,
            _ => null,
        };
    }
}
