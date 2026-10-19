package net.unrendered.rift.entity;

import net.minecraft.entity.Entity;
import net.minecraft.entity.EntityType;
import net.minecraft.entity.LivingEntity;
import net.minecraft.entity.projectile.thrown.ThrownItemEntity;
import net.minecraft.item.Item;
import net.minecraft.particle.ParticleTypes;
import net.minecraft.util.hit.EntityHitResult;
import net.minecraft.util.hit.HitResult;
import net.minecraft.world.World;
import net.unrendered.rift.RiftEntities;
import net.unrendered.rift.RiftItems;

/** A hunter's round: slow, straight, glowing, and worth what an UNRENDERED round is worth. */
public class UnrBoltEntity extends ThrownItemEntity {
    /** An enemy round against a player, in MC hit points (UNRENDERED's 12 of 100). */
    public static final float DAMAGE = 2.4f;

    public UnrBoltEntity(EntityType<? extends UnrBoltEntity> type, World world) {
        super(type, world);
        setNoGravity(true);
    }

    public UnrBoltEntity(World world, LivingEntity owner) {
        super(RiftEntities.UNR_BOLT, owner, world);
        setNoGravity(true);
        setGlowing(true);
    }

    @Override
    protected Item getDefaultItem() {
        return RiftItems.BULLET;
    }

    @Override
    public void tick() {
        super.tick();
        if (getWorld().isClient) getWorld().addParticle(ParticleTypes.END_ROD, getX(), getY(), getZ(), 0.0, 0.0, 0.0);
        else if (age > 120) discard();
    }

    @Override
    protected void onEntityHit(EntityHitResult hit) {
        super.onEntityHit(hit);
        Entity target = hit.getEntity();
        Entity owner = getOwner();
        if (target == owner || target instanceof UnrEnemyEntity) return;
        target.damage(getDamageSources().mobProjectile(this, owner instanceof LivingEntity l ? l : null), DAMAGE);
    }

    @Override
    protected void onCollision(HitResult hit) {
        super.onCollision(hit);
        if (!getWorld().isClient) discard();
    }
}
