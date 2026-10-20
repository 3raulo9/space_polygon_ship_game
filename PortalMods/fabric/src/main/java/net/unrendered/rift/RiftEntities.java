package net.unrendered.rift;

import net.fabricmc.fabric.api.object.builder.v1.entity.FabricDefaultAttributeRegistry;
import net.minecraft.entity.EntityType;
import net.minecraft.entity.SpawnGroup;
import net.minecraft.registry.Registries;
import net.minecraft.registry.Registry;
import net.unrendered.rift.entity.CrabCoreBlastEntity;
import net.unrendered.rift.entity.CrabCoreEntity;
import net.unrendered.rift.entity.EchoEntity;
import net.unrendered.rift.entity.PilotEntity;
import net.unrendered.rift.entity.SeatEchoEntity;
import net.unrendered.rift.entity.UnrBoltEntity;
import net.unrendered.rift.entity.UnrEnemyEntity;

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

    /** One of UNRENDERED's own players, standing in the rift: invisible, and hunted. */
    public static final EntityType<SeatEchoEntity> SEAT = EntityType.Builder.<SeatEchoEntity>create(SeatEchoEntity::new, SpawnGroup.MISC)
        .dimensions(0.8f, 1.9f)
        .maxTrackingRange(10)
        .trackingTickInterval(1)
        .disableSummon()
        .build("seat");

    /** An UNRENDERED hunter, elite or soldier that walked out into Minecraft. */
    public static final EntityType<UnrEnemyEntity> UNR_ENEMY = EntityType.Builder.<UnrEnemyEntity>create(UnrEnemyEntity::new, SpawnGroup.MONSTER)
        .dimensions(2.2f, 2.6f)
        .maxTrackingRange(10)
        .build("unrendered_enemy");

    /** A hunter's round. */
    public static final EntityType<UnrBoltEntity> UNR_BOLT = EntityType.Builder.<UnrBoltEntity>create(UnrBoltEntity::new, SpawnGroup.MISC)
        .dimensions(0.3f, 0.3f)
        .maxTrackingRange(6)
        .trackingTickInterval(2)
        .build("unrendered_bolt");

    /** A thrown CRAB CORE in flight. Fast, so it is sent every tick. */
    public static final EntityType<CrabCoreEntity> CRAB_CORE = EntityType.Builder.<CrabCoreEntity>create(CrabCoreEntity::new, SpawnGroup.MISC)
        .dimensions(0.5f, 0.5f)
        .maxTrackingRange(8)
        .trackingTickInterval(1)
        .build("crab_core");

    /** One going off: UNRENDERED's three-second burst of lances. */
    public static final EntityType<CrabCoreBlastEntity> CRAB_CORE_BLAST = EntityType.Builder.<CrabCoreBlastEntity>create(CrabCoreBlastEntity::new, SpawnGroup.MISC)
        .dimensions(0.5f, 0.5f)
        .maxTrackingRange(16)
        .trackingTickInterval(20)
        .disableSummon()
        .build("crab_core_blast");

    private RiftEntities() {}

    public static void register() {
        Registry.register(Registries.ENTITY_TYPE, RiftMod.id("echo"), ECHO);
        Registry.register(Registries.ENTITY_TYPE, RiftMod.id("pilot"), PILOT);
        Registry.register(Registries.ENTITY_TYPE, RiftMod.id("crab_core"), CRAB_CORE);
        Registry.register(Registries.ENTITY_TYPE, RiftMod.id("crab_core_blast"), CRAB_CORE_BLAST);
        Registry.register(Registries.ENTITY_TYPE, RiftMod.id("seat"), SEAT);
        Registry.register(Registries.ENTITY_TYPE, RiftMod.id("unrendered_enemy"), UNR_ENEMY);
        Registry.register(Registries.ENTITY_TYPE, RiftMod.id("unrendered_bolt"), UNR_BOLT);
        FabricDefaultAttributeRegistry.register(SEAT, PilotEntity.createAttributes());
        FabricDefaultAttributeRegistry.register(UNR_ENEMY, UnrEnemyEntity.createAttributes());
        FabricDefaultAttributeRegistry.register(ECHO, EchoEntity.createAttributes());
        FabricDefaultAttributeRegistry.register(PILOT, PilotEntity.createAttributes());
    }
}
