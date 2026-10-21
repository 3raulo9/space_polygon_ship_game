package net.unrendered.rift.entity;

import net.minecraft.entity.Entity;
import net.minecraft.entity.EntityDimensions;
import net.minecraft.entity.EntityPose;
import net.minecraft.entity.EntityType;
import net.minecraft.entity.LivingEntity;
import net.minecraft.entity.damage.DamageSource;
import net.minecraft.entity.data.DataTracker;
import net.minecraft.entity.data.TrackedData;
import net.minecraft.entity.data.TrackedDataHandlerRegistry;
import net.minecraft.world.World;
import net.unrendered.rift.RiftSeats;

/**
 * One of UNRENDERED's own players, as the rift dimension feels them: an invisible body the
 * size of their craft, standing where UNRENDERED says they stand. It's a pilot as far as
 * Minecraft's monsters are concerned, so they hunt it. Any blow it takes (a zombie, a
 * skeleton's arrow, a visitor's sword) goes back to UNRENDERED as `hurt` for that seat.
 * What anyone sees there is UNRENDERED's own drawing of the craft.
 */
public class SeatEchoEntity extends PilotEntity {
    private static final TrackedData<Float> WIDTH = DataTracker.registerData(SeatEchoEntity.class, TrackedDataHandlerRegistry.FLOAT);
    private static final TrackedData<Float> TALL = DataTracker.registerData(SeatEchoEntity.class, TrackedDataHandlerRegistry.FLOAT);

    /** UNRENDERED's seat number. */
    public int seat = -1;
    /** Ticks since UNRENDERED last listed it. */
    public int unseen;

    public SeatEchoEntity(EntityType<? extends SeatEchoEntity> type, World world) {
        super(type, world);
        setInvisible(true);
        setSilent(true);
        setCustomNameVisible(false);
    }

    @Override
    protected void initDataTracker(DataTracker.Builder builder) {
        super.initDataTracker(builder);
        builder.add(WIDTH, 0.8f);
        builder.add(TALL, 1.9f);
    }

    public void setBox(float width, float height) {
        width = Math.max(0.4f, Math.min(width, 8.0f));
        height = Math.max(0.6f, Math.min(height, 12.0f));
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
        if (getWorld().isClient || isInvulnerableTo(source) || amount <= 0f) return false;
        amount = through(source, amount);
        if (amount <= 0f) return false;
        RiftSeats.onHurt(this, source, amount);
        getWorld().sendEntityDamage(this, source);
        hurtTime = 10;
        maxHurtTime = 10;
        Entity attacker = source.getAttacker();
        if (attacker instanceof LivingEntity l) setAttacker(l);
        return true;
    }
}
