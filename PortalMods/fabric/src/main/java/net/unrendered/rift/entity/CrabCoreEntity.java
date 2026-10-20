package net.unrendered.rift.entity;

import net.minecraft.entity.EntityType;
import net.minecraft.entity.LivingEntity;
import net.minecraft.entity.projectile.thrown.ThrownItemEntity;
import net.minecraft.item.Item;
import net.minecraft.sound.SoundCategory;
import net.minecraft.util.hit.HitResult;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.World;
import net.unrendered.rift.RiftEntities;
import net.unrendered.rift.RiftItems;
import net.unrendered.rift.RiftSounds;
import net.unrendered.rift.UnrModels;

/**
 * A thrown CRAB CORE, flying the way UNRENDERED throws one (Projectile.FireCrabBomb): dead
 * straight, no drop and no drag, at the slug's own speed, for the slug's own life. It goes
 * off where it hits something, or where its life runs out — whichever comes first — and
 * what goes off is UNRENDERED's blast ({@link CrabCoreBlastEntity}).
 */
public class CrabCoreEntity extends ThrownItemEntity {
    private Vec3d flight;

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

    /** Along {@code dir} at UNRENDERED's throw speed (metres a second there, blocks here). */
    public void launch(Vec3d dir) {
        flight = dir.normalize().multiply(UnrModels.throwSpeed / 20.0);
        setVelocity(flight);
    }

    @Override
    protected double getGravity() {
        return 0.0;
    }

    @Override
    public void tick() {
        if (flight != null) setVelocity(flight);   // no drag: UNRENDERED's slug keeps its speed
        super.tick();
        if (!getWorld().isClient && !isRemoved() && age >= Math.round(UnrModels.throwLife * 20f)) goOff();
    }

    @Override
    protected void onCollision(HitResult hit) {
        super.onCollision(hit);
        if (!getWorld().isClient && !isRemoved()) goOff();
    }

    private void goOff() {
        World w = getWorld();
        // UNRENDERED's slug flies this high and the light core of the blast hangs this high
        // over the same floor: the core goes up by the difference.
        Vec3d at = getPos().add(0, UnrModels.blastCoreHeight - UnrModels.throwHeight, 0);
        w.spawnEntity(new CrabCoreBlastEntity(w, at, getOwner()));
        w.playSound(null, at.x, at.y, at.z, RiftSounds.CRAB_CORE_BLAST, SoundCategory.PLAYERS, 4.0f, 1.0f);
        discard();
    }
}
