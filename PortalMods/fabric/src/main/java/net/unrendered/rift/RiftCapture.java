package net.unrendered.rift;

import net.minecraft.entity.Entity;
import net.minecraft.entity.EquipmentSlot;
import net.minecraft.entity.ItemEntity;
import net.minecraft.entity.LivingEntity;
import net.minecraft.entity.EntityType;
import net.minecraft.entity.mob.Monster;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.item.ItemStack;
import net.minecraft.nbt.NbtCompound;
import net.minecraft.registry.Registries;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.util.math.BlockPos;
import net.unrendered.rift.bridge.Wire;

import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Turns server entities into snapshots for the client to draw. With no client (a
 * dedicated server), it writes a plain coloured box per entity instead.
 */
public final class RiftCapture {
    public static final int HOSTILE = 1, PLAYER = 2, ITEM = 4, PICKABLE = 8;

    private static final EquipmentSlot[] SLOTS = {
        EquipmentSlot.MAINHAND, EquipmentSlot.OFFHAND, EquipmentSlot.FEET,
        EquipmentSlot.LEGS, EquipmentSlot.CHEST, EquipmentSlot.HEAD,
    };

    /** Entity ids whose NBT the client already has. */
    public static final Set<Integer> nbtSent = ConcurrentHashMap.newKeySet();
    /** Entity ids the client asked for NBT again (it lost its stand-in). */
    public static final Set<Integer> needNbt = ConcurrentHashMap.newKeySet();

    private RiftCapture() {}

    public static RiftHooks.EntitySnap snap(Entity e) {
        RiftHooks.EntitySnap s = new RiftHooks.EntitySnap();
        s.id = e.getId();
        s.typeId = Registries.ENTITY_TYPE.getId(e.getType()).toString();
        boolean wantNbt = !(e instanceof PlayerEntity) && (nbtSent.add(e.getId()) | needNbt.remove(e.getId()));
        if (wantNbt) {
            NbtCompound nbt = new NbtCompound();
            try {
                e.writeNbt(nbt);
                s.nbt = nbt;
            } catch (RuntimeException ignored) {
            }
        }
        s.x = e.getX();
        s.y = e.getY();
        s.z = e.getZ();
        s.yaw = e.getYaw();
        s.pitch = e.getPitch();
        s.headYaw = e.getHeadYaw();
        s.bodyYaw = e.getBodyYaw();
        s.w = e.getWidth();
        s.h = e.getHeight();
        s.name = e.getName().getString();
        s.sneaking = e.isSneaking();
        s.speed = (float) e.getVelocity().horizontalLength();
        s.age = e.age;
        if (e.getWorld() instanceof ServerWorld sw) s.light = Math.max(0, Math.min(15, sw.getLightLevel(BlockPos.ofFloored(e.getEyePos()))));
        int flags = 0;
        if (e instanceof Monster) flags |= HOSTILE;
        if (e instanceof LivingEntity living) {
            s.hp = living.getHealth();
            s.max = living.getMaxHealth();
            s.hurtTime = living.hurtTime;
            s.swinging = living.handSwinging;
            s.swingTicks = living.handSwingTicks;
            s.equipment = new ItemStack[SLOTS.length];
            for (int i = 0; i < SLOTS.length; i++) s.equipment[i] = living.getEquippedStack(SLOTS[i]).copy();
        }
        if (e instanceof PlayerEntity p) {
            flags |= PLAYER;
            s.profileId = p.getGameProfile().getId();
            s.profileName = p.getGameProfile().getName();
        }
        if (e instanceof ItemEntity item) {
            flags |= ITEM | PICKABLE;
            s.item = item.getStack().copy();
            s.name = item.getStack().getName().getString();
        }
        s.flags = flags;
        return s;
    }

    /** Should a pilot see this at all? Not their own body, not UNRENDERED's own hitboxes,
     *  and nothing that draws nothing. */
    public static boolean visible(Entity e) {
        if (e.getType() == RiftEntities.PILOT || e.getType() == RiftEntities.ECHO || e.getType() == RiftEntities.SEAT) return false;
        if (e.getType() == EntityType.MARKER || e.getType() == EntityType.AREA_EFFECT_CLOUD) return false;
        return !e.isRemoved();
    }

    /** Should UNRENDERED see this rift-dimension entity? Not the rift's own stand-ins, and not
     *  players (visitors travel as avatars). */
    public static boolean riftVisible(Entity e) {
        return visible(e) && !(e instanceof PlayerEntity);
    }

    /** The entity header in the `ents` layout, up to and including the layer count. */
    public static void writeHeader(Wire.Out out, RiftHooks.EntitySnap s, double ox, double oy, double oz, int layers) {
        out.i32(s.id);
        out.f32((float) (s.x - ox)).f32((float) (s.y - oy)).f32((float) (s.z - oz));
        out.f32(s.w).f32(s.h);
        out.f32(s.hp).f32(s.max);
        out.u8(s.flags);
        out.str(s.name);
        out.u16(layers);
    }

    /** No client to draw it: a box the size of its hitbox, untextured, coloured by kind. */
    public static void writeBox(Wire.Out out, RiftHooks.EntitySnap s) {
        writeHeader(out, s, 0, 0, 0, 1);
        out.str("");
        out.u32(6);
        int hash = s.typeId.hashCode();
        int r = 90 + (hash & 0x7F), g = 90 + ((hash >> 8) & 0x7F), b = 90 + ((hash >> 16) & 0x7F);
        if ((s.flags & ITEM) != 0) { r = 230; g = 200; b = 90; }
        float hw = s.w / 2f, h = s.h;
        float[][] faces = {
            {-hw, 0, -hw, hw, 0, -hw, hw, 0, hw, -hw, 0, hw},
            {-hw, h, -hw, -hw, h, hw, hw, h, hw, hw, h, -hw},
            {-hw, 0, -hw, -hw, h, -hw, hw, h, -hw, hw, 0, -hw},
            {-hw, 0, hw, hw, 0, hw, hw, h, hw, -hw, h, hw},
            {-hw, 0, -hw, -hw, 0, hw, -hw, h, hw, -hw, h, -hw},
            {hw, 0, -hw, hw, h, -hw, hw, h, hw, hw, 0, hw},
        };
        float[] shade = {0.5f, 1.0f, 0.8f, 0.8f, 0.6f, 0.6f};
        float lightMul = 0.25f + 0.75f * s.light / 15f;
        for (int f = 0; f < 6; f++) {
            float k = shade[f] * lightMul;
            for (int v = 0; v < 4; v++) {
                out.f32(faces[f][v * 3]).f32(faces[f][v * 3 + 1]).f32(faces[f][v * 3 + 2]);
                out.f32(v == 1 || v == 2 ? 1 : 0).f32(v >= 2 ? 1 : 0);
                out.u8((int) (r * k)).u8((int) (g * k)).u8((int) (b * k)).u8(255);
            }
        }
    }
}
