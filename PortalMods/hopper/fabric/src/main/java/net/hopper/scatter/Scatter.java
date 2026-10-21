package net.hopper.scatter;

import net.fabricmc.fabric.api.networking.v1.PlayerLookup;
import net.fabricmc.fabric.api.networking.v1.ServerPlayNetworking;
import net.hopper.HopperMod;
import net.minecraft.entity.LivingEntity;
import net.minecraft.entity.damage.DamageSource;
import net.minecraft.entity.effect.StatusEffectInstance;
import net.minecraft.item.ItemStack;
import net.minecraft.item.Items;
import net.minecraft.particle.DustParticleEffect;
import net.minecraft.particle.ItemStackParticleEffect;
import net.minecraft.particle.ParticleTypes;
import net.minecraft.registry.RegistryKeys;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.sound.SoundCategory;
import net.minecraft.sound.SoundEvents;
import net.minecraft.util.math.MathHelper;
import net.minecraft.util.math.Vec3d;
import net.minecraft.util.math.random.Random;
import org.joml.Vector3f;

import java.util.HashMap;
import java.util.Map;
import java.util.UUID;

/**
 * What a concentrated teleportation potion does to a body, by the minute:
 * <ul>
 *   <li>0–2 min: nothing — a few motes of the End leak off it from 1:30;</li>
 *   <li>2–4 min: it starts jumping, chorus-fruit fashion: every 20 s at first, every 8 s by the
 *       end, over 8 blocks at first and 64 by the end;</li>
 *   <li>4–5 min: every 5 s, out to 200 blocks, and each jump tears: it hurts, and leaves a red
 *       burst where it was. Then the body comes apart a piece at a time — the left arm, the
 *       right arm, the left leg, the right leg — each one bursting away somewhere nearby;</li>
 *   <li>5 min: the rest goes, everywhere at once.</li>
 * </ul>
 * Time is read off the effect's remaining duration ({@link #TOTAL} at the start). With the
 * system property {@code hopper.fastScatter} it runs twenty times faster, for testing.
 */
public final class Scatter {
    public static final int TOTAL = 20 * 60 * 20;   // the potion's duration: 20 minutes
    static final int WARN = 20 * 90, JUMP = 20 * 120, TEAR = 20 * 240, END = 20 * 300, PART = 20 * 15;

    private static final Map<UUID, Integer> nextJump = new HashMap<>();
    private static final Map<UUID, Integer> lastFastJump = new HashMap<>();
    private static final Map<UUID, Integer> stages = new HashMap<>();

    private Scatter() {}

    static boolean fast() {
        return Boolean.getBoolean("hopper.fastScatter");
    }

    /** One tick of the effect. False when it is over. */
    static boolean tick(ServerWorld w, LivingEntity e) {
        StatusEffectInstance inst = e.getStatusEffect(HopperMod.SCATTER);
        if (inst == null) return true;
        int el = Math.max(0, TOTAL - inst.getDuration());
        if (fast()) el *= 20;
        UUID id = e.getUuid();
        Random rnd = e.getRandom();

        if (el < WARN) return true;
        if (el < JUMP) {
            if (e.age % 10 == 0) w.spawnParticles(ParticleTypes.PORTAL, e.getX(), e.getBodyY(0.5), e.getZ(), 6, 0.3, 0.6, 0.3, 0.4);
            return true;
        }
        if (el >= END) {
            gone(w, e);
            return false;
        }

        boolean tearing = el >= TEAR;
        int interval;
        double range;
        if (!tearing) {
            double p = (el - JUMP) / (double) (TEAR - JUMP);
            interval = (int) MathHelper.lerp(p, 400, 160);
            range = MathHelper.lerp(p, 8, 64);
        } else {
            double p = (el - TEAR) / (double) (END - TEAR);
            interval = 100;
            range = MathHelper.lerp(p, 64, 200);
            int stage = Math.min(4, 1 + (el - TEAR) / PART);
            int had = stages.getOrDefault(id, 0);
            if (stage > had) {
                stages.put(id, stage);
                partGone(w, e, stage);
            }
        }
        int due = nextJump.getOrDefault(id, el);
        if (el >= due && (!fast() || e.age - lastFastJump.getOrDefault(id, -100) >= 10)) {
            nextJump.put(id, el + interval);
            lastFastJump.put(id, e.age);
            Vec3d from = e.getPos();
            if (jump(w, e, range, rnd) && tearing) {
                burst(w, from.add(0, e.getHeight() * 0.5, 0), 1f);
                e.damage(source(w), 2f + rnd.nextFloat() * 2f);
            }
        }
        if (e.age % 40 == 0 && stages.getOrDefault(id, 0) > 0) broadcast(e, stages.get(id));
        return true;
    }

    /** A chorus-fruit jump: up to sixteen tries for somewhere to stand within range. */
    private static boolean jump(ServerWorld w, LivingEntity e, double range, Random rnd) {
        double ox = e.getX(), oy = e.getY(), oz = e.getZ();
        int bottom = w.getBottomY(), top = bottom + w.getLogicalHeight() - 1;
        for (int i = 0; i < 16; i++) {
            double x = ox + (rnd.nextDouble() * 2 - 1) * range;
            double z = oz + (rnd.nextDouble() * 2 - 1) * range;
            double y = MathHelper.clamp(oy + (rnd.nextDouble() * 2 - 1) * Math.min(range / 4, 32), bottom, top);
            w.getChunk(MathHelper.floor(x) >> 4, MathHelper.floor(z) >> 4);
            if (e.hasVehicle()) e.stopRiding();
            if (e.teleport(x, y, z, true)) {
                w.playSound(null, ox, oy, oz, SoundEvents.ITEM_CHORUS_FRUIT_TELEPORT, SoundCategory.PLAYERS, 1f, 0.7f);
                w.playSound(null, e.getX(), e.getY(), e.getZ(), SoundEvents.ENTITY_ENDERMAN_TELEPORT, SoundCategory.PLAYERS, 1f, 0.6f);
                return true;
            }
        }
        return false;
    }

    /** A piece of the body teleports away: it bursts somewhere a few blocks off. */
    private static void partGone(ServerWorld w, LivingEntity e, int stage) {
        Random rnd = e.getRandom();
        Vec3d at = e.getPos().add((rnd.nextDouble() * 2 - 1) * 6, e.getHeight() * 0.5 + rnd.nextDouble() * 2, (rnd.nextDouble() * 2 - 1) * 6);
        burst(w, at, 1.2f);
        burst(w, e.getPos().add(0, e.getHeight() * 0.6, 0), 0.5f);
        w.playSound(null, e.getX(), e.getY(), e.getZ(), SoundEvents.ENTITY_SLIME_SQUISH, SoundCategory.PLAYERS, 1.2f, 0.6f);
        w.playSound(null, at.x, at.y, at.z, SoundEvents.ENTITY_ENDERMAN_TELEPORT, SoundCategory.PLAYERS, 1f, 1.4f);
        e.damage(source(w), 3f);
        broadcast(e, stage);
    }

    /** The end of it: the rest of the body, everywhere at once. */
    private static void gone(ServerWorld w, LivingEntity e) {
        Vec3d c = e.getPos().add(0, e.getHeight() * 0.5, 0);
        burst(w, c, 3f);
        Random rnd = e.getRandom();
        for (int i = 0; i < 6; i++)
            burst(w, c.add((rnd.nextDouble() * 2 - 1) * 10, rnd.nextDouble() * 4, (rnd.nextDouble() * 2 - 1) * 10), 1f);
        w.playSound(null, c.x, c.y, c.z, SoundEvents.ENTITY_GENERIC_EXPLODE.value(), SoundCategory.PLAYERS, 0.8f, 1.6f);
        w.playSound(null, c.x, c.y, c.z, SoundEvents.ENTITY_ENDERMAN_DEATH, SoundCategory.PLAYERS, 1f, 0.5f);
        e.damage(source(w), Float.MAX_VALUE);
        clear(e);
    }

    /** Red: blood-coloured dust, and bits of meat. */
    static void burst(ServerWorld w, Vec3d at, float size) {
        int n = Math.round(30 * size);
        w.spawnParticles(new DustParticleEffect(new Vector3f(0.55f, 0.02f, 0.02f), 1.6f), at.x, at.y, at.z, n, 0.35 * size, 0.5 * size, 0.35 * size, 0.05);
        w.spawnParticles(new DustParticleEffect(new Vector3f(0.3f, 0f, 0f), 1.2f), at.x, at.y, at.z, n / 2, 0.5 * size, 0.6 * size, 0.5 * size, 0.08);
        w.spawnParticles(new ItemStackParticleEffect(ParticleTypes.ITEM, new ItemStack(Items.BEEF)), at.x, at.y, at.z, Math.round(8 * size), 0.25, 0.3, 0.25, 0.12);
        w.spawnParticles(new ItemStackParticleEffect(ParticleTypes.ITEM, new ItemStack(Items.ROTTEN_FLESH)), at.x, at.y, at.z, Math.round(5 * size), 0.25, 0.3, 0.25, 0.12);
        w.spawnParticles(ParticleTypes.REVERSE_PORTAL, at.x, at.y, at.z, Math.round(20 * size), 0.3, 0.4, 0.3, 0.1);
    }

    static DamageSource source(ServerWorld w) {
        return new DamageSource(w.getRegistryManager().get(RegistryKeys.DAMAGE_TYPE).entryOf(HopperMod.SCATTERED));
    }

    public static int stage(LivingEntity e) {
        return stages.getOrDefault(e.getUuid(), 0);
    }

    /** Whole again: the effect is gone (milk, death, a command). */
    public static void clear(LivingEntity e) {
        UUID id = e.getUuid();
        nextJump.remove(id);
        lastFastJump.remove(id);
        if (stages.remove(id) != null) broadcast(e, 0);
    }

    /** Every second: anybody who lost the effect some other way gets their body back. */
    public static void sweep(MinecraftServer server) {
        if (stages.isEmpty() && nextJump.isEmpty()) return;
        for (ServerWorld w : server.getWorlds()) {
            for (UUID id : new java.util.ArrayList<>(nextJump.keySet())) {
                if (w.getEntity(id) instanceof LivingEntity e && (!e.isAlive() || !e.hasStatusEffect(HopperMod.SCATTER))) clear(e);
            }
        }
        for (UUID id : new java.util.ArrayList<>(stages.keySet())) {
            boolean found = false;
            for (ServerWorld w : server.getWorlds()) if (w.getEntity(id) != null) found = true;
            if (!found) {
                stages.remove(id);
                nextJump.remove(id);
                lastFastJump.remove(id);
            }
        }
    }

    private static void broadcast(LivingEntity e, int stage) {
        ScatterPayload p = new ScatterPayload(e.getId(), stage);
        for (ServerPlayerEntity sp : PlayerLookup.tracking(e)) ServerPlayNetworking.send(sp, p);
        if (e instanceof ServerPlayerEntity self) ServerPlayNetworking.send(self, p);
    }
}
