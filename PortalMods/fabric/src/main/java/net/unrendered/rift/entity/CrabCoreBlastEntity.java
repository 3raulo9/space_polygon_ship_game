package net.unrendered.rift.entity;

import net.minecraft.entity.Entity;
import net.minecraft.entity.EntityType;
import net.minecraft.entity.LivingEntity;
import net.minecraft.entity.damage.DamageSource;
import net.minecraft.entity.data.DataTracker;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.nbt.NbtCompound;
import net.minecraft.registry.RegistryKeys;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.util.math.Box;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.World;
import net.unrendered.rift.RiftDamage;
import net.unrendered.rift.RiftEntities;
import net.unrendered.rift.UnrModels;
import org.joml.Quaternionf;
import org.joml.Vector3f;

import java.util.UUID;

/**
 * A thrown CRAB CORE going off: UNRENDERED's CrabCoreBlast, line for line. A core of light
 * hangs where it went off and throws lances out in every direction at once while the whole
 * spray tumbles; a bubble of energy breathes around it; the lances swell in, hold, and
 * shrink back to nothing over three seconds. Every third of a second the field bites
 * everything inside its current reach — at UNRENDERED's own damage, put into Minecraft's
 * scale the way every other UNRENDERED weapon is — except whoever threw it. It leaves the
 * ground alone.
 */
public class CrabCoreBlastEntity extends Entity {
    /** UNRENDERED's damage, in Minecraft: to mobs as the pilot's rounds are scaled, to a
     *  player as everything UNRENDERED does to a Minecraft player is. */
    public static final float TO_MOB = 6.5f, TO_PLAYER = 0.2f;

    private UUID owner;
    private Vector3f[] base;

    public CrabCoreBlastEntity(EntityType<? extends CrabCoreBlastEntity> type, World world) {
        super(type, world);
        noClip = true;
    }

    public CrabCoreBlastEntity(World world, Vec3d at, Entity thrower) {
        this(RiftEntities.CRAB_CORE_BLAST, world);
        setPosition(at);
        owner = thrower != null ? thrower.getUuid() : null;
    }

    /** Seconds since it went off. */
    public float age(float tickDelta) {
        return (age + tickDelta) / 20f;
    }

    /** CrabCoreBlast.Envelope: a 0.18 s attack, then a curved decay to nothing. */
    public static float envelope(float t) {
        float attack = Math.min(1f, Math.max(0f, t / 0.18f));
        float p = Math.min(1f, Math.max(0f, t / UnrModels.blastLife));
        return attack * (1f - p * p);
    }

    /** CrabCoreBlast.BubbleRadius. */
    public static float bubble(float t) {
        float breathe = 0.75f + 0.25f * (float) Math.sin(t * 9f);
        return UnrModels.blastLength * 0.32f * envelope(t) * breathe;
    }

    /** CrabCoreBlast.Direction: the Fibonacci spray, tumbled by a yaw/pitch/roll off the clock. */
    public Vector3f direction(int i, float t) {
        if (base == null) {
            int n = UnrModels.blastBeams;
            base = new Vector3f[n];
            float golden = (float) (Math.PI * (3.0 - Math.sqrt(5.0)));
            for (int k = 0; k < n; k++) {
                float y = 1f - (k + 0.5f) / n * 2f;
                float r = (float) Math.sqrt(Math.max(0f, 1f - y * y));
                float theta = golden * k;
                base[k] = new Vector3f((float) Math.cos(theta) * r, y, (float) Math.sin(theta) * r);
            }
        }
        // System.Numerics' CreateFromYawPitchRoll is JOML's rotationYXZ.
        Quaternionf spin = new Quaternionf().rotationYXZ(t * 1.3f, t * 0.9f, t * 0.6f);
        return base[i].rotate(spin, new Vector3f()).normalize();
    }

    @Override
    public void tick() {
        super.tick();
        if (getWorld().isClient) return;
        float t = age(0f);
        if (t >= UnrModels.blastLife) {
            discard();
            return;
        }
        int every = Math.max(1, Math.round(UnrModels.blastTick * 20f));
        if (age % every == 0) bite((ServerWorld) getWorld(), t);
    }

    private void bite(ServerWorld w, float t) {
        float reach = UnrModels.blastRadius * Math.min(1f, Math.max(0f, envelope(t) * 1.4f));
        if (reach <= 0.01f) return;
        Entity thrower = owner != null ? w.getEntity(owner) : null;
        DamageSource source = new DamageSource(
            w.getRegistryManager().get(RegistryKeys.DAMAGE_TYPE).entryOf(RiftDamage.UNRENDERED), this, thrower);
        Vec3d c = getPos();
        // The field is planar in UNRENDERED (its world is a floor); here it reaches as far up
        // and down as the lances do.
        Box box = new Box(c.x - reach - 2, c.y - UnrModels.blastLength, c.z - reach - 2,
            c.x + reach + 2, c.y + UnrModels.blastLength, c.z + reach + 2);
        for (LivingEntity e : w.getEntitiesByClass(LivingEntity.class, box, LivingEntity::isAlive)) {
            if (e == thrower) continue;
            double dx = e.getX() - c.x, dz = e.getZ() - c.z;
            if (Math.sqrt(dx * dx + dz * dz) > reach + e.getWidth() / 2f) continue;
            float dmg = UnrModels.blastDamage * (e instanceof PlayerEntity ? TO_PLAYER : TO_MOB);
            e.timeUntilRegen = 0;   // its own pace, not Minecraft's half-second of grace
            e.damage(source, dmg);
        }
    }

    @Override
    public boolean shouldRender(double distance) {
        return true;
    }

    @Override
    protected void initDataTracker(DataTracker.Builder builder) {
    }

    @Override
    protected void readCustomDataFromNbt(NbtCompound nbt) {
        if (nbt.containsUuid("Owner")) owner = nbt.getUuid("Owner");
        age = nbt.getInt("Age");
    }

    @Override
    protected void writeCustomDataToNbt(NbtCompound nbt) {
        if (owner != null) nbt.putUuid("Owner", owner);
        nbt.putInt("Age", age);
    }
}
