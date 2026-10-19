package net.unrendered.rift.entity;

import net.minecraft.entity.Entity;
import net.minecraft.entity.EntityDimensions;
import net.minecraft.entity.EntityPose;
import net.minecraft.entity.EntityType;
import net.minecraft.entity.attribute.DefaultAttributeContainer;
import net.minecraft.entity.attribute.EntityAttributes;
import net.minecraft.entity.damage.DamageSource;
import net.minecraft.entity.data.DataTracker;
import net.minecraft.entity.data.TrackedData;
import net.minecraft.entity.data.TrackedDataHandlerRegistry;
import net.minecraft.entity.mob.MobEntity;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.world.World;
import net.unrendered.rift.RiftVisitors;

/**
 * One of UNRENDERED's hostiles as Minecraft feels it: an invisible hitbox the size of the
 * real thing. What you see is UNRENDERED's own polygons, streamed in. What you hit is this.
 * Any weapon works, a modded one included, because the blow arrives here as ordinary
 * damage. It never loses health here. It reports the blow, and UNRENDERED decides.
 */
public class EchoEntity extends MobEntity {
    private static final TrackedData<Float> WIDTH = DataTracker.registerData(EchoEntity.class, TrackedDataHandlerRegistry.FLOAT);
    private static final TrackedData<Float> TALL = DataTracker.registerData(EchoEntity.class, TrackedDataHandlerRegistry.FLOAT);

    /** UNRENDERED's id for this hostile. */
    public int unrId = -1;
    /** Ticks since UNRENDERED last mentioned it. */
    public int unseen;

    public EchoEntity(EntityType<? extends EchoEntity> type, World world) {
        super(type, world);
        setAiDisabled(true);
        setNoGravity(true);
        setSilent(true);
        setInvisible(true);
        setPersistent();
    }

    public static DefaultAttributeContainer.Builder createAttributes() {
        return MobEntity.createMobAttributes()
            .add(EntityAttributes.GENERIC_MAX_HEALTH, 20.0)
            .add(EntityAttributes.GENERIC_KNOCKBACK_RESISTANCE, 1.0);
    }

    @Override
    protected void initDataTracker(DataTracker.Builder builder) {
        super.initDataTracker(builder);
        builder.add(WIDTH, 1.0f);
        builder.add(TALL, 1.0f);
    }

    public void setBox(float width, float height) {
        width = Math.max(0.3f, Math.min(width, 30.0f));
        height = Math.max(0.3f, Math.min(height, 60.0f));
        if (Math.abs(width - dataTracker.get(WIDTH)) > 0.01f || Math.abs(height - dataTracker.get(TALL)) > 0.01f) {
            dataTracker.set(WIDTH, width);
            dataTracker.set(TALL, height);
            calculateDimensions();
        }
    }

    @Override
    public void onTrackedDataSet(TrackedData<?> data) {
        super.onTrackedDataSet(data);
        if (WIDTH.equals(data) || TALL.equals(data)) calculateDimensions();
    }

    @Override
    public EntityDimensions getBaseDimensions(EntityPose pose) {
        return EntityDimensions.changing(dataTracker.get(WIDTH), dataTracker.get(TALL));
    }

    @Override
    public boolean damage(DamageSource source, float amount) {
        if (getWorld().isClient || isInvulnerableTo(source)) return false;
        Entity attacker = source.getAttacker();
        if (attacker instanceof ServerPlayerEntity player) {
            RiftVisitors.onStrike(this, player, amount);
            getWorld().sendEntityDamage(this, source);
            return true;
        }
        return false;
    }

    @Override
    public boolean isAttackable() {
        return true;
    }

    @Override
    public boolean canHit() {
        return isAlive();
    }

    @Override
    public boolean isPushable() {
        return false;
    }

    @Override
    protected void pushAway(Entity entity) {
    }

    @Override
    public boolean isCollidable() {
        return false;
    }

    @Override
    public boolean canImmediatelyDespawn(double distanceSquared) {
        return false;
    }

    @Override
    public boolean shouldSave() {
        return false;
    }

}
