package net.unrendered.rift;

import net.minecraft.entity.damage.DamageSource;
import net.minecraft.entity.damage.DamageType;
import net.minecraft.registry.RegistryKey;
import net.minecraft.registry.RegistryKeys;
import net.minecraft.server.world.ServerWorld;

/** "X was unrendered": the damage type for whatever UNRENDERED does to a Minecraft player. */
public final class RiftDamage {
    public static final RegistryKey<DamageType> UNRENDERED = RegistryKey.of(RegistryKeys.DAMAGE_TYPE, RiftMod.id("unrendered"));

    private RiftDamage() {}

    public static void register() {
        // Data-driven: data/unrendered_portal/damage_type/unrendered.json.
    }

    public static DamageSource source(ServerWorld world) {
        return new DamageSource(world.getRegistryManager().get(RegistryKeys.DAMAGE_TYPE).entryOf(UNRENDERED));
    }
}
