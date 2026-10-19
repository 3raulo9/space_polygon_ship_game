package net.unrendered.rift;

import net.fabricmc.fabric.api.itemgroup.v1.FabricItemGroup;
import net.minecraft.item.Item;
import net.minecraft.item.ItemGroup;
import net.minecraft.item.ItemStack;
import net.minecraft.registry.Registries;
import net.minecraft.registry.Registry;
import net.minecraft.text.Text;
import net.minecraft.util.Rarity;
import net.unrendered.rift.item.BatteryItem;
import net.unrendered.rift.item.CrabCoreItem;
import net.unrendered.rift.item.RelicItem;
import net.unrendered.rift.item.RepairKitItem;
import net.unrendered.rift.item.RoundsItem;

import java.util.LinkedHashMap;
import java.util.Map;

/**
 * Everything UNRENDERED's salvage becomes on this side: one item for each of its
 * ItemKinds, named for them. A Minecraft player who picks salvage up in UNRENDERED
 * walks home carrying these. They craft into each other the way UNRENDERED's bench
 * crafts them, and the useful ones do in Minecraft roughly what they do there.
 */
public final class RiftItems {
    /** UNRENDERED item id (as it travels in {@code give}) to the item. */
    public static final Map<String, Item> BY_UNR_ID = new LinkedHashMap<>();

    public static final Item SCRAP_METAL = relic("scrap_metal", Rarity.COMMON);
    public static final Item COPPER_WIRE = relic("copper_wire", Rarity.COMMON);
    public static final Item SPACE_GUNPOWDER = relic("space_gunpowder", Rarity.COMMON);
    public static final Item DENSE_ALLOY = relic("dense_alloy", Rarity.COMMON);
    public static final Item LEAD = relic("lead", Rarity.COMMON);
    public static final Item ZINC = relic("zinc", Rarity.COMMON);
    public static final Item LITHIUM = relic("lithium", Rarity.COMMON);
    public static final Item BATTERY = add("battery", new BatteryItem(new Item.Settings().maxCount(16).rarity(Rarity.UNCOMMON)));
    public static final Item BULLET = add("bullet", new RoundsItem(new Item.Settings().maxCount(64)));
    public static final Item CRAB_FRAGMENT = relic("crab_fragment", Rarity.RARE);
    public static final Item CRAB_CORE = add("crab_core", new CrabCoreItem(new Item.Settings().maxCount(4).rarity(Rarity.EPIC)));
    public static final Item REPAIR_KIT = add("repair_kit", new RepairKitItem(new Item.Settings().maxCount(8).rarity(Rarity.UNCOMMON)));
    public static final Item MOONSTONE = relic("moonstone", Rarity.RARE);
    public static final Item SUN_FRAGMENT = relic("sun_fragment", Rarity.EPIC);
    public static final Item MOON_FRAGMENT = relic("moon_fragment", Rarity.EPIC);

    public static final ItemGroup GROUP = FabricItemGroup.builder()
        .icon(() -> new ItemStack(CRAB_CORE))
        .displayName(Text.translatable("itemGroup.unrendered_portal.rift"))
        .entries((ctx, entries) -> BY_UNR_ID.values().forEach(entries::add))
        .build();

    private RiftItems() {}

    private static Item relic(String id, Rarity rarity) {
        return add(id, new RelicItem(new Item.Settings().maxCount(64).rarity(rarity)));
    }

    private static Item add(String id, Item item) {
        BY_UNR_ID.put(id, item);
        return item;
    }

    public static void register() {
        BY_UNR_ID.forEach((id, item) -> Registry.register(Registries.ITEM, RiftMod.id(id), item));
        Registry.register(Registries.ITEM_GROUP, RiftMod.id("rift"), GROUP);
    }

    /** UNRENDERED's ItemKind name (any case, with or without underscores) to its item. */
    public static Item fromUnrendered(String kind) {
        if (kind == null) return null;
        String k = kind.trim().toLowerCase();
        Item direct = BY_UNR_ID.get(k);
        if (direct != null) return direct;
        String flat = k.replace("_", "");
        for (Map.Entry<String, Item> e : BY_UNR_ID.entrySet()) {
            if (e.getKey().replace("_", "").equals(flat)) return e.getValue();
        }
        return switch (flat) {
            case "rounds", "ammo" -> BULLET;
            case "cell" -> BATTERY;
            default -> null;
        };
    }
}
