package net.unrendered.rift;

import net.fabricmc.fabric.api.object.builder.v1.entity.FabricDefaultAttributeRegistry;
import net.minecraft.entity.EntityType;
import net.minecraft.entity.SpawnGroup;
import net.minecraft.registry.Registries;
import net.minecraft.registry.Registry;
import net.unrendered.rift.entity.CrabCoreEntity;
import net.unrendered.rift.entity.EchoEntity;
import net.unrendered.rift.entity.PilotEntity;

public final class RiftEntities {
    /** An UNRENDERED hostile's body, as Minecraft feels it: an invisible hitbox. */
    public static final EntityType<EchoEntity> ECHO = EntityType.Builder.<EchoEntity>create(EchoEntity::new, SpawnGroup.MISC)
        .dimensions(1.0f, 1.0f)
        .maxTrackingRange(10)
        .trackingTickInterval(1)
        .disableSummon()
        .build("echo");

    /** An UNRENDERED pilot standing in Minecraft, drawn as UNRENDERED draws their craft. */
    public static final EntityType<PilotEntity> PILOT = EntityType.Builder.<PilotEntity>create(PilotEntity::new, SpawnGroup.MISC)
        .dimensions(1.6f, 2.2f)
        .maxTrackingRange(10)
        .trackingTickInterval(1)
        .disableSummon()
        .build("pilot");

    public static final EntityType<CrabCoreEntity> CRAB_CORE = EntityType.Builder.<CrabCoreEntity>create(CrabCoreEntity::new, SpawnGroup.MISC)
        .dimensions(0.4f, 0.4f)
        .maxTrackingRange(4)
        .trackingTickInterval(10)
        .build("crab_core");

    private RiftEntities() {}

    public static void register() {
        Registry.register(Registries.ENTITY_TYPE, RiftMod.id("echo"), ECHO);
        Registry.register(Registries.ENTITY_TYPE, RiftMod.id("pilot"), PILOT);
        Registry.register(Registries.ENTITY_TYPE, RiftMod.id("crab_core"), CRAB_CORE);
        FabricDefaultAttributeRegistry.register(ECHO, EchoEntity.createAttributes());
        FabricDefaultAttributeRegistry.register(PILOT, PilotEntity.createAttributes());
    }
}
