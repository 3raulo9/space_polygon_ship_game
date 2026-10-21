package net.hopper.entity;

import net.hopper.HopperMod;
import net.minecraft.entity.EntityType;
import net.minecraft.entity.LivingEntity;
import net.minecraft.entity.data.DataTracker;
import net.minecraft.entity.projectile.thrown.ThrownEntity;
import net.minecraft.nbt.NbtCompound;
import net.minecraft.particle.DustParticleEffect;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.util.hit.BlockHitResult;
import net.minecraft.util.hit.EntityHitResult;
import net.minecraft.util.hit.HitResult;
import net.minecraft.util.math.Direction;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.RaycastContext;
import net.minecraft.world.World;
import org.joml.Vector3f;

import java.util.UUID;

/**
 * The gun's shot: a small green glow flying dead straight. Where it lands a portal opens —
 * on a floor, standing on it facing whoever fired; on a wall, standing out from it; under a
 * ceiling, hanging from it. If it flies its full reach without landing, it opens where it
 * drops to, or fizzles if there is nothing below.
 */
public class HopperShotEntity extends ThrownEntity {
    public static final double SPEED = 2.5;
    public static final int REACH_TICKS = 26;   // ~64 blocks

    private String destination;
    private String coords = "";
    private UUID gun;
    private float shooterYaw;

    public HopperShotEntity(EntityType<? extends HopperShotEntity> type, World world) {
        super(type, world);
    }

    public static HopperShotEntity fire(ServerWorld world, LivingEntity by, String destination, String coords, UUID gun) {
        HopperShotEntity shot = new HopperShotEntity(HopperMod.SHOT, world);
        shot.setOwner(by);
        Vec3d look = by.getRotationVector();
        shot.setPosition(by.getEyePos().add(look.multiply(0.8)).subtract(0, 0.15, 0));
        shot.setVelocity(look.multiply(SPEED));
        shot.destination = destination;
        shot.coords = coords == null ? "" : coords;
        shot.gun = gun;
        shot.shooterYaw = by.getYaw();
        return shot;
    }

    @Override
    protected double getGravity() {
        return 0.0;
    }

    @Override
    protected void initDataTracker(DataTracker.Builder builder) {
    }

    @Override
    public void tick() {
        Vec3d v = getVelocity();
        super.tick();
        if (!isRemoved() && v.lengthSquared() > 0) setVelocity(v);   // no drag
        World w = getWorld();
        if (w.isClient) {
            w.addParticle(new DustParticleEffect(new Vector3f(0.4f, 1.0f, 0.35f), 1.2f), getX(), getY(), getZ(), 0, 0, 0);
            return;
        }
        if (!isRemoved() && age > REACH_TICKS) {
            // Out of reach: drop to whatever is below and open there.
            BlockHitResult down = w.raycast(new RaycastContext(getPos(), getPos().add(0, -24, 0),
                RaycastContext.ShapeType.COLLIDER, RaycastContext.FluidHandling.NONE, this));
            if (down.getType() == HitResult.Type.BLOCK) land(down.getPos(), Direction.UP);
            else fizzle();
        }
    }

    @Override
    protected void onCollision(HitResult hit) {
        super.onCollision(hit);
        if (getWorld().isClient || isRemoved()) return;
        if (hit instanceof BlockHitResult b) land(b.getPos(), b.getSide());
        else if (hit instanceof EntityHitResult e) land(e.getEntity().getPos(), Direction.UP);
    }

    private void land(Vec3d at, Direction side) {
        if (getWorld() instanceof ServerWorld world && destination != null) {
            HopperMod.openPortals(world, at, side, shooterYaw, destination, coords, gun, getOwner());
        }
        discard();
    }

    private void fizzle() {
        getWorld().playSound(null, getX(), getY(), getZ(), net.minecraft.sound.SoundEvents.BLOCK_FIRE_EXTINGUISH,
            net.minecraft.sound.SoundCategory.PLAYERS, 0.5f, 1.8f);
        discard();
    }

    @Override
    public void readCustomDataFromNbt(NbtCompound nbt) {
        super.readCustomDataFromNbt(nbt);
        if (nbt.contains("Destination")) destination = nbt.getString("Destination");
        if (nbt.containsUuid("Gun")) gun = nbt.getUuid("Gun");
        coords = nbt.getString("Coords");
        shooterYaw = nbt.getFloat("ShooterYaw");
    }

    @Override
    public void writeCustomDataToNbt(NbtCompound nbt) {
        super.writeCustomDataToNbt(nbt);
        if (destination != null) nbt.putString("Destination", destination);
        if (gun != null) nbt.putUuid("Gun", gun);
        nbt.putString("Coords", coords);
        nbt.putFloat("ShooterYaw", shooterYaw);
    }
}
