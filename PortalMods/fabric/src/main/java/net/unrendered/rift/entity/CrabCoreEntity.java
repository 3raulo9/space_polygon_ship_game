package net.unrendered.rift.entity;

import net.minecraft.entity.EntityType;
import net.minecraft.entity.LivingEntity;
import net.minecraft.entity.projectile.thrown.ThrownItemEntity;
import net.minecraft.item.Item;
import net.minecraft.particle.ParticleTypes;
import net.minecraft.util.hit.HitResult;
import net.minecraft.world.World;
import net.unrendered.rift.RiftEntities;
import net.unrendered.rift.RiftItems;

/** A thrown Crab-Core. It goes off where it lands and leaves the blocks standing. */
public class CrabCoreEntity extends ThrownItemEntity {
    public CrabCoreEntity(EntityType<? extends CrabCoreEntity> type, World world) {
        super(type, world);
    }

    public CrabCoreEntity(World world, LivingEntity owner) {
        super(RiftEntities.CRAB_CORE, owner, world);
    }

    @Override
    protected Item getDefaultItem() {
        return RiftItems.CRAB_CORE;
    }

    @Override
    public void tick() {
        super.tick();
        if (getWorld().isClient) {
            getWorld().addParticle(ParticleTypes.END_ROD, getX(), getY(), getZ(), 0.0, 0.0, 0.0);
        }
    }

    @Override
    protected void onCollision(HitResult hit) {
        super.onCollision(hit);
        if (!getWorld().isClient) {
            getWorld().createExplosion(this, getX(), getY(), getZ(), 3.5f, World.ExplosionSourceType.NONE);
            discard();
        }
    }
}
