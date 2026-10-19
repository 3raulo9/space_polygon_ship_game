package net.unrendered.rift.entity;

import net.minecraft.entity.Entity;
import net.minecraft.entity.EntityType;
import net.minecraft.entity.attribute.DefaultAttributeContainer;
import net.minecraft.entity.attribute.EntityAttributes;
import net.minecraft.entity.damage.DamageSource;
import net.minecraft.entity.mob.MobEntity;
import net.minecraft.entity.mob.PathAwareEntity;
import net.minecraft.world.World;
import net.unrendered.rift.RiftPilot;

/**
 * An UNRENDERED pilot in Minecraft. UNRENDERED simulates them; this body only stands where
 * they say, draws the craft they send, and is something Minecraft can aim at. Zombies chase
 * it and skeletons shoot it. Every blow it takes is passed back to UNRENDERED as `hurt`, and
 * its health here only mirrors the shield and hull UNRENDERED reports.
 */
public class PilotEntity extends PathAwareEntity {
    public PilotEntity(EntityType<? extends PilotEntity> type, World world) {
        super(type, world);
        setAiDisabled(true);
        setNoGravity(true);
        setPersistent();
        setCustomNameVisible(true);
    }

    public static DefaultAttributeContainer.Builder createAttributes() {
        return MobEntity.createMobAttributes()
            .add(EntityAttributes.GENERIC_MAX_HEALTH, 20.0)
            .add(EntityAttributes.GENERIC_KNOCKBACK_RESISTANCE, 1.0)
            .add(EntityAttributes.GENERIC_FOLLOW_RANGE, 32.0);
    }

    @Override
    public boolean damage(DamageSource source, float amount) {
        if (getWorld().isClient || isInvulnerableTo(source) || amount <= 0f) return false;
        if (hurtTime > 0 && amount < 0.5f) return false;
        RiftPilot.onPilotHurt(this, source, amount);
        getWorld().sendEntityDamage(this, source);
        hurtTime = 10;
        maxHurtTime = 10;
        Entity attacker = source.getAttacker();
        if (attacker != null) setAttacker(attacker instanceof net.minecraft.entity.LivingEntity l ? l : null);
        return true;
    }

    /**
     * The body never moves itself (UNRENDERED moves it), so Minecraft never asks what it is
     * standing in. Ask every tick: pressure plates press, tripwires trip, the portal's water
     * notices it.
     */
    @Override
    public void tick() {
        super.tick();
        if (!getWorld().isClient) checkBlockCollision();
    }

    @Override
    public boolean isPushable() {
        return false;
    }

    @Override
    protected void pushAway(Entity entity) {
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
