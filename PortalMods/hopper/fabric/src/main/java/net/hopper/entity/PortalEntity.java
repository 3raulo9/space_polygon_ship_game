package net.hopper.entity;

import net.hopper.HopperMod;
import net.hopper.Realms;
import net.minecraft.entity.Entity;
import net.minecraft.entity.EntityType;
import net.minecraft.entity.data.DataTracker;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.nbt.NbtCompound;
import net.minecraft.registry.RegistryKey;
import net.minecraft.registry.RegistryKeys;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.sound.SoundCategory;
import net.minecraft.sound.SoundEvents;
import net.minecraft.util.Identifier;
import net.minecraft.util.math.Box;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.TeleportTarget;
import net.minecraft.world.World;

import java.util.UUID;

/**
 * One end of a hop: an upright oval of green swirl standing on the ground (its position is
 * the middle of its bottom edge), facing along its yaw. Whatever walks, falls or flies into
 * it comes out of its twin — in another dimension or another part of this one — keeping its
 * speed and facing out of the far portal. A portal aimed at another game's realm has no twin;
 * a player who walks in is handed to that realm. Both ends close together after a minute.
 */
public class PortalEntity extends Entity {
    public static final float WIDTH = 2.0f, HEIGHT = 2.6f;
    public static final int LIFE = 20 * 60;

    // The other end (server only).
    private RegistryKey<World> twinWorld;
    private Vec3d twinPos;
    private float twinYaw;
    private UUID twinId;
    private String realm;
    private UUID gun;

    public PortalEntity(EntityType<? extends PortalEntity> type, World world) {
        super(type, world);
        noClip = true;
    }

    public static PortalEntity at(ServerWorld world, Vec3d pos, float yaw, UUID gun) {
        PortalEntity p = new PortalEntity(HopperMod.PORTAL, world);
        p.refreshPositionAndAngles(pos.x, pos.y, pos.z, yaw, 0f);
        p.setYaw(yaw);
        p.gun = gun;
        return p;
    }

    public void pairWith(PortalEntity twin, ServerWorld twinWorld) {
        this.twinWorld = twinWorld.getRegistryKey();
        this.twinPos = twin.getPos();
        this.twinYaw = twin.getYaw();
        this.twinId = twin.getUuid();
    }

    public void aimAt(String realmId) {
        this.realm = realmId;
    }

    public UUID gun() {
        return gun;
    }

    /** The way out of its face: where things leave it and the way they go. */
    public static Vec3d facing(float yaw) {
        double r = Math.toRadians(yaw);
        return new Vec3d(-Math.sin(r), 0, Math.cos(r));
    }

    /** The client's swirl clock: ticks since it opened. */
    public int openFor() {
        return age;
    }

    @Override
    public void tick() {
        super.tick();
        if (!(getWorld() instanceof ServerWorld world)) return;
        if (age > LIFE) {
            close(world);
            return;
        }
        if (age % 2 != 0) return;
        Vec3d n = facing(getYaw());
        Vec3d right = new Vec3d(n.z, 0, -n.x);
        Vec3d c = getPos();
        Box box = new Box(c.x - 2, c.y - 0.5, c.z - 2, c.x + 2, c.y + HEIGHT, c.z + 2);
        for (Entity e : world.getOtherEntities(this, box, e -> !(e instanceof PortalEntity) && !(e instanceof HopperShotEntity))) {
            if (e.hasPortalCooldown() || e.hasVehicle() || e.isSpectator()) continue;
            Vec3d d = e.getPos().add(0, e.getHeight() / 2, 0).subtract(c);
            double across = d.dotProduct(right), through = d.dotProduct(n), up = d.y;
            // Inside the oval, within a hand's breadth of its face.
            double ex = across / (WIDTH / 2), ey = (up - HEIGHT / 2) / (HEIGHT / 2);
            if (ex * ex + ey * ey > 1.0 || Math.abs(through) > 0.55) continue;
            send(world, e);
        }
    }

    private void send(ServerWorld here, Entity e) {
        e.setPortalCooldown(40);
        if (realm != null) {
            // Another game's realm: only a player can be taken there, by that game.
            if (!(e instanceof ServerPlayerEntity player)) return;
            Realms.Realm r = Realms.find(here.getServer(), realm);
            if (r == null || r.enter() == null) return;
            if (!r.isClear()) {
                player.sendMessage(net.minecraft.text.Text.literal("ESTABLISH A CLEAR CONNECTION WITH THIS REALM")
                    .formatted(net.minecraft.util.Formatting.GREEN), true);
                return;
            }
            here.playSound(null, getX(), getY() + 1.3, getZ(), SoundEvents.BLOCK_PORTAL_TRAVEL, SoundCategory.PLAYERS, 0.25f, 1.6f);
            r.enter().accept(player, getPos().add(facing(getYaw()).multiply(1.5)));
            return;
        }
        if (twinWorld == null || twinPos == null) return;
        ServerWorld there = here.getServer().getWorld(twinWorld);
        if (there == null) return;
        there.getChunk(net.minecraft.util.math.BlockPos.ofFloored(twinPos));
        Vec3d out = facing(twinYaw);
        Vec3d at = twinPos.add(out.multiply(1.2)).add(0, 0.05, 0);
        Vec3d v = e.getVelocity();
        double speed = Math.sqrt(v.x * v.x + v.z * v.z);
        Vec3d kept = new Vec3d(out.x * speed, v.y, out.z * speed);
        float yaw = twinYaw;
        float pitch = e.getPitch();
        here.playSound(null, e.getX(), e.getY(), e.getZ(), SoundEvents.ENTITY_ENDERMAN_TELEPORT, SoundCategory.NEUTRAL, 0.6f, 1.7f);
        Entity moved;
        if (there == here) {
            if (e instanceof ServerPlayerEntity p) {
                p.networkHandler.requestTeleport(at.x, at.y, at.z, yaw, pitch);
                moved = p;
            } else {
                e.refreshPositionAndAngles(at.x, at.y, at.z, yaw, pitch);
                e.setVelocity(kept);
                e.velocityModified = true;
                moved = e;
            }
        } else {
            moved = e.teleportTo(new TeleportTarget(there, at, kept, yaw, pitch, TeleportTarget.NO_OP));
        }
        if (moved != null) {
            moved.setPortalCooldown(40);
            there.playSound(null, at.x, at.y, at.z, SoundEvents.ENTITY_ENDERMAN_TELEPORT, SoundCategory.NEUTRAL, 0.6f, 1.4f);
            if (moved instanceof PlayerEntity) HopperMod.LOG.info("Dimension Hopper: {} hopped to {} at {}",
                moved.getName().getString(), there.getRegistryKey().getValue(), at);
        }
    }

    /** Shuts this end and its twin. */
    public void close(ServerWorld world) {
        if (twinWorld != null && twinId != null) {
            ServerWorld there = world.getServer().getWorld(twinWorld);
            if (there != null && there.getEntity(twinId) instanceof PortalEntity twin && !twin.isRemoved()) twin.discard();
        }
        world.playSound(null, getX(), getY() + 1.3, getZ(), SoundEvents.BLOCK_BEACON_DEACTIVATE, SoundCategory.BLOCKS, 0.8f, 1.6f);
        discard();
    }

    @Override
    public boolean shouldRender(double distance) {
        return distance < 128 * 128;
    }

    @Override
    protected void initDataTracker(DataTracker.Builder builder) {
    }

    @Override
    protected void readCustomDataFromNbt(NbtCompound nbt) {
        age = nbt.getInt("Open");
        if (nbt.contains("TwinWorld")) {
            twinWorld = RegistryKey.of(RegistryKeys.WORLD, Identifier.of(nbt.getString("TwinWorld")));
            twinPos = new Vec3d(nbt.getDouble("TwinX"), nbt.getDouble("TwinY"), nbt.getDouble("TwinZ"));
            twinYaw = nbt.getFloat("TwinYaw");
        }
        if (nbt.containsUuid("Twin")) twinId = nbt.getUuid("Twin");
        if (nbt.containsUuid("Gun")) gun = nbt.getUuid("Gun");
        if (nbt.contains("Realm")) realm = nbt.getString("Realm");
    }

    @Override
    protected void writeCustomDataToNbt(NbtCompound nbt) {
        nbt.putInt("Open", age);
        if (twinWorld != null && twinPos != null) {
            nbt.putString("TwinWorld", twinWorld.getValue().toString());
            nbt.putDouble("TwinX", twinPos.x);
            nbt.putDouble("TwinY", twinPos.y);
            nbt.putDouble("TwinZ", twinPos.z);
            nbt.putFloat("TwinYaw", twinYaw);
        }
        if (twinId != null) nbt.putUuid("Twin", twinId);
        if (gun != null) nbt.putUuid("Gun", gun);
        if (realm != null) nbt.putString("Realm", realm);
    }
}
