package net.hopper;

import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.itemgroup.v1.ItemGroupEvents;
import net.fabricmc.fabric.api.networking.v1.PayloadTypeRegistry;
import net.fabricmc.fabric.api.networking.v1.ServerPlayNetworking;
import net.hopper.entity.HopperShotEntity;
import net.hopper.entity.PortalEntity;
import net.hopper.net.DialPayload;
import net.hopper.net.RealmsPayload;
import net.minecraft.block.BlockState;
import net.minecraft.block.Blocks;
import net.minecraft.component.DataComponentTypes;
import net.minecraft.component.type.NbtComponent;
import net.minecraft.entity.Entity;
import net.minecraft.entity.EntityType;
import net.minecraft.entity.SpawnGroup;
import net.minecraft.item.Item;
import net.minecraft.item.ItemGroups;
import net.minecraft.item.ItemStack;
import net.minecraft.nbt.NbtCompound;
import net.minecraft.registry.Registries;
import net.minecraft.registry.Registry;
import net.minecraft.registry.tag.FluidTags;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.sound.SoundCategory;
import net.minecraft.sound.SoundEvents;
import net.minecraft.text.Text;
import net.minecraft.util.Formatting;
import net.minecraft.util.Hand;
import net.minecraft.util.Identifier;
import net.minecraft.util.Rarity;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Direction;
import net.minecraft.util.math.MathHelper;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.Heightmap;
import net.minecraft.world.World;
import net.minecraft.world.dimension.DimensionType;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.UUID;

/**
 * The Dimension Hopper: a handheld portal gun. Sneak-use (or V) dials a destination, use
 * fires; where the shot lands a portal opens, and its twin opens somewhere safe at the
 * destination. Works on its own; another game's mod can add its realm to the dial (see
 * {@link Realms}).
 */
public final class HopperMod implements ModInitializer {
    public static final String MOD_ID = "dimension_hopper";
    public static final Logger LOG = LoggerFactory.getLogger("dimension_hopper");

    public static final Item HOPPER = new HopperItem(new Item.Settings().maxCount(1).rarity(Rarity.EPIC));

    public static final EntityType<PortalEntity> PORTAL = EntityType.Builder.<PortalEntity>create(PortalEntity::new, SpawnGroup.MISC)
        .dimensions(PortalEntity.WIDTH, PortalEntity.HEIGHT)
        .maxTrackingRange(10)
        .trackingTickInterval(20)
        .disableSummon()
        .build("portal");

    public static final EntityType<HopperShotEntity> SHOT = EntityType.Builder.<HopperShotEntity>create(HopperShotEntity::new, SpawnGroup.MISC)
        .dimensions(0.3f, 0.3f)
        .maxTrackingRange(8)
        .trackingTickInterval(1)
        .build("shot");

    public static Identifier id(String path) {
        return Identifier.of(MOD_ID, path);
    }

    @Override
    public void onInitialize() {
        Registry.register(Registries.ITEM, id("hopper"), HOPPER);
        Registry.register(Registries.ENTITY_TYPE, id("portal"), PORTAL);
        Registry.register(Registries.ENTITY_TYPE, id("shot"), SHOT);
        ItemGroupEvents.modifyEntriesEvent(ItemGroups.TOOLS).register(entries -> entries.add(HOPPER));

        PayloadTypeRegistry.playC2S().register(DialPayload.ID, DialPayload.CODEC);
        PayloadTypeRegistry.playS2C().register(RealmsPayload.ID, RealmsPayload.CODEC);
        ServerPlayNetworking.registerGlobalReceiver(DialPayload.ID, (payload, ctx) -> onDial(ctx.player(), payload));
    }

    // --- The gun's own settings, kept on the stack ------------------------------------

    public static String destination(ItemStack stack) {
        NbtComponent c = stack.get(DataComponentTypes.CUSTOM_DATA);
        return c == null ? "" : c.copyNbt().getString("Destination");
    }

    private static void edit(ItemStack stack, java.util.function.Consumer<NbtCompound> how) {
        NbtComponent c = stack.get(DataComponentTypes.CUSTOM_DATA);
        NbtCompound nbt = c == null ? new NbtCompound() : c.copyNbt();
        how.accept(nbt);
        stack.set(DataComponentTypes.CUSTOM_DATA, NbtComponent.of(nbt));
    }

    public static UUID gunId(ItemStack stack) {
        NbtComponent c = stack.get(DataComponentTypes.CUSTOM_DATA);
        if (c != null && c.copyNbt().containsUuid("Gun")) return c.copyNbt().getUuid("Gun");
        UUID id = UUID.randomUUID();
        edit(stack, nbt -> nbt.putUuid("Gun", id));
        return id;
    }

    static ItemStack held(ServerPlayerEntity player) {
        for (Hand h : Hand.values()) if (player.getStackInHand(h).isOf(HOPPER)) return player.getStackInHand(h);
        return ItemStack.EMPTY;
    }

    /** The dial: shown, refreshed, set, or a realm asked to connect. */
    private static void onDial(ServerPlayerEntity player, DialPayload p) {
        ItemStack gun = held(player);
        switch (p.action()) {
            case "select" -> {
                if (!gun.isEmpty() && Realms.find(player.getServer(), p.id()) != null) {
                    edit(gun, nbt -> nbt.putString("Destination", p.id()));
                    player.getServerWorld().playSound(null, player.getX(), player.getY(), player.getZ(),
                        SoundEvents.BLOCK_NOTE_BLOCK_BIT.value(), SoundCategory.PLAYERS, 0.6f, 1.8f);
                }
            }
            case "connect" -> {
                Realms.Realm r = Realms.find(player.getServer(), p.id());
                if (r != null && r.connect() != null) {
                    try {
                        r.connect().run();
                    } catch (RuntimeException e) {
                        LOG.warn("Dimension Hopper: connecting to {} failed: {}", r.name(), e.toString());
                    }
                }
            }
            default -> { }
        }
        if (gun.isEmpty() && !"list".equals(p.action())) return;
        ServerPlayNetworking.send(player, Realms.payload(player.getServer(), "open".equals(p.action()), destination(gun)));
    }

    /** Opens the dial on a player's screen. */
    public static void showDial(ServerPlayerEntity player, ItemStack gun) {
        ServerPlayNetworking.send(player, Realms.payload(player.getServer(), true, destination(gun)));
    }

    // --- Firing ------------------------------------------------------------------------

    /** The destination a gun fires at when it has not been dialled: out of the overworld to
     *  the Nether, anywhere else home to the overworld. */
    static String fallback(ServerWorld here) {
        return here.getRegistryKey() == World.OVERWORLD ? World.NETHER.getValue().toString() : World.OVERWORLD.getValue().toString();
    }

    /** True when it fired. */
    static boolean fire(ServerWorld world, ServerPlayerEntity player, ItemStack gun) {
        String dest = destination(gun);
        if (dest.isEmpty() || Realms.find(world.getServer(), dest) == null) dest = fallback(world);
        Realms.Realm r = Realms.find(world.getServer(), dest);
        if (r == null) return false;
        if (r.realm() && !r.isClear()) {
            player.sendMessage(Text.literal("ESTABLISH A CLEAR CONNECTION WITH THIS REALM").formatted(Formatting.GREEN), true);
            world.playSound(null, player.getX(), player.getY(), player.getZ(), SoundEvents.BLOCK_BEACON_DEACTIVATE,
                SoundCategory.PLAYERS, 0.7f, 1.9f);
            return false;
        }
        world.spawnEntity(HopperShotEntity.fire(world, player, dest, gunId(gun)));
        world.playSound(null, player.getX(), player.getY(), player.getZ(), SoundEvents.BLOCK_BEACON_ACTIVATE,
            SoundCategory.PLAYERS, 0.6f, 2.0f);
        // Green sparks off the muzzle, spat forward.
        net.minecraft.util.math.Vec3d look = player.getRotationVec(1f);
        net.minecraft.util.math.Vec3d muzzle = player.getEyePos().add(look.multiply(0.9)).add(0, -0.15, 0);
        world.spawnParticles(new net.minecraft.particle.DustParticleEffect(new org.joml.Vector3f(0.35f, 1f, 0.4f), 1.1f),
            muzzle.x, muzzle.y, muzzle.z, 14, 0.08, 0.08, 0.08, 0.02);
        world.spawnParticles(net.minecraft.particle.ParticleTypes.GLOW, muzzle.x, muzzle.y, muzzle.z, 4,
            look.x * 0.2, look.y * 0.2, look.z * 0.2, 0.15);
        return true;
    }

    /**
     * The shot landed: open this end facing back toward whoever fired (or out of the wall it
     * hit), close the gun's last pair, and open the twin at a safe spot at the destination.
     */
    public static void openPortals(ServerWorld world, Vec3d hit, Direction side, float shooterYaw, String destination,
                                   UUID gun, Entity owner) {
        Realms.Realm r = Realms.find(world.getServer(), destination);
        if (r == null) return;
        closePair(world, gun);

        float yaw;
        Vec3d base;
        if (side.getAxis().isHorizontal()) {
            Vec3d out = Vec3d.of(side.getVector());
            yaw = (float) Math.toDegrees(Math.atan2(-out.x, out.z));
            base = hit.add(out.multiply(0.6)).subtract(0, PortalEntity.HEIGHT / 2, 0);
        } else if (side == Direction.DOWN) {
            yaw = shooterYaw + 180f;
            base = hit.subtract(0, PortalEntity.HEIGHT + 0.05, 0);
        } else {
            yaw = shooterYaw + 180f;
            base = hit;
        }
        yaw = MathHelper.wrapDegrees(yaw);
        PortalEntity here = PortalEntity.at(world, base, yaw, gun);

        if (r.realm()) {
            here.aimAt(r.id());
            world.spawnEntity(here);
        } else {
            ServerWorld there = world.getServer().getWorld(r.dimension());
            if (there == null) return;
            BlockPos spot = safeSpot(world, there, base, yaw);
            PortalEntity twin = PortalEntity.at(there, Vec3d.ofBottomCenter(spot), yaw, gun);
            here.pairWith(twin, there);
            twin.pairWith(here, world);
            world.spawnEntity(here);
            there.spawnEntity(twin);
            there.playSound(null, spot, SoundEvents.BLOCK_END_PORTAL_SPAWN, SoundCategory.BLOCKS, 0.4f, 2.0f);
            LOG.info("Dimension Hopper: a portal opened in {} at {} to {} at {}", world.getRegistryKey().getValue(),
                base, there.getRegistryKey().getValue(), spot);
        }
        world.playSound(null, base.x, base.y + 1.3, base.z, SoundEvents.BLOCK_END_PORTAL_SPAWN, SoundCategory.BLOCKS, 0.4f, 2.0f);
        world.playSound(null, base.x, base.y + 1.3, base.z, SoundEvents.BLOCK_PORTAL_TRIGGER, SoundCategory.BLOCKS, 0.35f, 1.8f);
    }

    /** One pair per gun: firing again closes the last one wherever it is. */
    private static void closePair(ServerWorld any, UUID gun) {
        if (gun == null) return;
        for (ServerWorld w : any.getServer().getWorlds()) {
            for (PortalEntity p : w.getEntitiesByType(PORTAL, p -> gun.equals(p.gun()))) {
                if (!p.isRemoved()) p.close(w);
            }
        }
    }

    // --- Where the far end may open ------------------------------------------------------

    /**
     * Somewhere a body can stand at the destination: the same place scaled the way the two
     * dimensions scale (eight to one into the Nether), two blocks of air over something solid
     * that is not lava; under the Nether's roof, not on it; on the End's own platform. Within
     * the same dimension a hop goes 64 blocks the way the portal was fired. Failing all that,
     * a small platform is built to stand on.
     */
    static BlockPos safeSpot(ServerWorld from, ServerWorld to, Vec3d at, float portalYaw) {
        if (to.getRegistryKey() == World.END) {
            BlockPos p = ServerWorld.END_SPAWN_POS;
            platform(to, p, 2, Blocks.OBSIDIAN.getDefaultState());
            return p;
        }
        double x = at.x, z = at.z;
        if (to == from) {
            Vec3d away = PortalEntity.facing(portalYaw).multiply(-64);
            x += away.x;
            z += away.z;
        } else {
            double scale = DimensionType.getCoordinateScaleFactor(from.getDimension(), to.getDimension());
            x *= scale;
            z *= scale;
        }
        BlockPos c = to.getWorldBorder().clamp(x, at.y, z);
        boolean roof = to.getDimension().hasCeiling();
        for (int r = 0; r <= 24; r += 4) {
            for (int dx = -r; dx <= r; dx += 4) {
                for (int dz = -r; dz <= r; dz += 4) {
                    if (r > 0 && Math.abs(dx) != r && Math.abs(dz) != r) continue;
                    BlockPos found = standable(to, c.getX() + dx, c.getZ() + dz, roof);
                    if (found != null) return found;
                }
            }
        }
        int y = MathHelper.clamp(roof ? 70 : Math.max(to.getSeaLevel() + 1, (int) at.y), to.getBottomY() + 2, to.getTopY() - 4);
        BlockPos p = new BlockPos(c.getX(), y, c.getZ());
        platform(to, p, 1, roof ? Blocks.NETHERRACK.getDefaultState() : Blocks.COBBLESTONE.getDefaultState());
        return p;
    }

    private static BlockPos standable(ServerWorld w, int x, int z, boolean roof) {
        w.getChunk(x >> 4, z >> 4);
        int top = roof ? Math.min(w.getTopY() - 1, w.getBottomY() + w.getLogicalHeight() - 2)
            : w.getTopY(Heightmap.Type.MOTION_BLOCKING_NO_LEAVES, x, z) + 1;
        int bottom = w.getBottomY() + 1;
        for (int y = top; y > bottom; y--) {
            BlockPos feet = new BlockPos(x, y, z);
            if (!clear(w, feet) || !clear(w, feet.up()) || !clear(w, feet.up(2))) continue;
            BlockState floor = w.getBlockState(feet.down());
            if (!floor.isSolidBlock(w, feet.down()) || !floor.getFluidState().isEmpty()) continue;
            if (w.getFluidState(feet).isIn(FluidTags.LAVA)) continue;
            return feet;
        }
        return null;
    }

    private static boolean clear(ServerWorld w, BlockPos p) {
        BlockState s = w.getBlockState(p);
        return s.getCollisionShape(w, p).isEmpty() && s.getFluidState().isEmpty();
    }

    /** A square to stand on, with the air above it cleared. */
    private static void platform(ServerWorld w, BlockPos feet, int radius, BlockState floor) {
        for (int dx = -radius; dx <= radius; dx++) {
            for (int dz = -radius; dz <= radius; dz++) {
                w.setBlockState(feet.add(dx, -1, dz), floor);
                for (int dy = 0; dy < 3; dy++) w.setBlockState(feet.add(dx, dy, dz), Blocks.AIR.getDefaultState());
            }
        }
    }
}
