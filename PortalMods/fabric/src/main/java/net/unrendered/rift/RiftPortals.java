package net.unrendered.rift;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import net.minecraft.block.BlockState;
import net.minecraft.block.Blocks;
import net.minecraft.entity.Entity;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.world.TeleportTarget;
import net.unrendered.rift.entity.UnrEnemyEntity;
import net.minecraft.item.ItemStack;
import net.minecraft.item.Items;
import net.minecraft.registry.RegistryKey;
import net.minecraft.registry.RegistryKeys;
import net.minecraft.util.Identifier;
import net.minecraft.util.WorldSavePath;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.sound.SoundCategory;
import net.minecraft.sound.SoundEvents;
import net.minecraft.text.ClickEvent;
import net.minecraft.text.HoverEvent;
import net.minecraft.text.MutableText;
import net.minecraft.text.Text;
import net.minecraft.util.ActionResult;
import net.minecraft.util.Formatting;
import net.minecraft.util.Hand;
import net.minecraft.util.hit.BlockHitResult;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Direction;
import net.minecraft.util.math.MathHelper;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.World;
import net.unrendered.rift.bridge.Bridge;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;

/**
 * The glowstone portals. Build a frame of glowstone and pour water into it, and it opens.
 * That's the Aether's ritual with the colours swapped. Or click [JOIN WORLDS] when
 * UNRENDERED asks, and one is built in front of you.
 */
public final class RiftPortals {
    /** One portal: the bottom-left cell of its water, the axis its plane runs along, and its size. */
    public record Spot(RegistryKey<World> dim, BlockPos bottomLeft, Direction.Axis axis, int width, int height) {
        Direction right() {
            return axis == Direction.Axis.X ? Direction.EAST : Direction.SOUTH;
        }

        /** The middle of the water, at its floor. */
        Vec3d floorCentre() {
            Direction r = right();
            double half = width / 2.0;
            return new Vec3d(bottomLeft.getX() + 0.5 + r.getOffsetX() * (half - 0.5),
                bottomLeft.getY(),
                bottomLeft.getZ() + 0.5 + r.getOffsetZ() * (half - 0.5));
        }

        /** The portal's facing: the plane's normal, as a unit (x, z). */
        Vec3d normal() {
            return axis == Direction.Axis.X ? new Vec3d(0, 0, 1) : new Vec3d(1, 0, 0);
        }
    }

    /** The last portal opened outside the rift. It's where pilots come out, and what UNRENDERED is told about. */
    public static Spot lastPortal;

    /** The portal standing in the rift where UNRENDERED's own portal is. */
    public static Spot riftPortal;

    /** Ticks until each player may be carried again. */
    private static final Map<UUID, Integer> cooldown = new HashMap<>();
    /** Ticks since each player was last inside portal water. */
    private static final Map<UUID, Integer> lastTouch = new HashMap<>();

    private RiftPortals() {}

    public static void tick(MinecraftServer server) {
        cooldown.replaceAll((k, v) -> v - 1);
        cooldown.values().removeIf(v -> v <= 0);
        lastTouch.replaceAll((k, v) -> v + 1);
        lastTouch.values().removeIf(v -> v > 100);
    }

    // --- Kept with the world -------------------------------------------------------------
    // Which portal pilots come out of, and which one stands in the rift, outlive a restart:
    // without them a reloaded world would build a second rift portal beside the first and
    // turn every crossing enemy straight back.

    private static MinecraftServer loadedFrom;

    private static Path file(MinecraftServer server) {
        return server.getSavePath(WorldSavePath.ROOT).resolve("unrendered_portal_portals.json");
    }

    public static void load(MinecraftServer server) {
        loadedFrom = server;
        lastPortal = null;
        riftPortal = null;
        Path f = file(server);
        if (!Files.exists(f)) return;
        try {
            JsonObject o = JsonParser.parseString(Files.readString(f)).getAsJsonObject();
            if (o.has("last")) lastPortal = spotOf(o.getAsJsonObject("last"));
            if (o.has("rift")) riftPortal = spotOf(o.getAsJsonObject("rift"));
        } catch (IOException | RuntimeException e) {
            RiftMod.LOG.warn("Rift: could not read the saved portals: {}", e.toString());
        }
    }

    /** After {@link #lastPortal} or {@link #riftPortal} changes. */
    public static void save() {
        MinecraftServer server = loadedFrom;
        if (server == null) return;
        JsonObject o = new JsonObject();
        if (lastPortal != null) o.add("last", jsonOf(lastPortal));
        if (riftPortal != null) o.add("rift", jsonOf(riftPortal));
        try {
            Files.writeString(file(server), o.toString());
        } catch (IOException e) {
            RiftMod.LOG.warn("Rift: could not save the portals: {}", e.toString());
        }
    }

    private static JsonObject jsonOf(Spot s) {
        JsonObject o = new JsonObject();
        o.addProperty("dim", s.dim().getValue().toString());
        o.addProperty("x", s.bottomLeft().getX());
        o.addProperty("y", s.bottomLeft().getY());
        o.addProperty("z", s.bottomLeft().getZ());
        o.addProperty("axis", s.axis() == Direction.Axis.X ? "x" : "z");
        o.addProperty("w", s.width());
        o.addProperty("h", s.height());
        return o;
    }

    private static Spot spotOf(JsonObject o) {
        return new Spot(RegistryKey.of(RegistryKeys.WORLD, Identifier.of(o.get("dim").getAsString())),
            new BlockPos(o.get("x").getAsInt(), o.get("y").getAsInt(), o.get("z").getAsInt()),
            "x".equals(o.get("axis").getAsString()) ? Direction.Axis.X : Direction.Axis.Z,
            o.get("w").getAsInt(), o.get("h").getAsInt());
    }

    public static void broadcast(MinecraftServer server, Text body) {
        Text line = Text.literal("[RIFT] ").formatted(Formatting.LIGHT_PURPLE).append(body);
        for (ServerPlayerEntity p : server.getPlayerManager().getPlayerList()) p.sendMessage(line, false);
    }

    // --- Lighting one by hand -----------------------------------------------------------

    /** A water bucket poured into a glowstone frame lights it. */
    public static ActionResult onUseBlock(PlayerEntity player, World world, Hand hand, BlockHitResult hit) {
        ItemStack stack = player.getStackInHand(hand);
        if (!stack.isOf(Items.WATER_BUCKET)) return ActionResult.PASS;
        BlockPos target = hit.getBlockPos().offset(hit.getSide());
        Spot frame = findFrame(world, target);
        if (frame == null) frame = findFrame(world, hit.getBlockPos());
        if (frame == null) return ActionResult.PASS;
        if (!world.isClient && world instanceof ServerWorld sw) {
            fill(sw, frame);
            if (!player.isCreative()) player.setStackInHand(hand, new ItemStack(Items.BUCKET));
            onOpened(sw, frame);
        }
        return ActionResult.SUCCESS;
    }

    private static boolean empty(World world, BlockPos pos) {
        BlockState s = world.getBlockState(pos);
        return s.isAir() || s.isOf(Blocks.WATER) || s.isOf(RiftBlocks.RIFT_PORTAL);
    }

    private static boolean glow(World world, BlockPos pos) {
        return world.getBlockState(pos).isOf(Blocks.GLOWSTONE);
    }

    /** Finds a closed glowstone frame (inside 2x3 up to 21x21) with {@code pos} inside it. */
    public static Spot findFrame(World world, BlockPos pos) {
        for (Direction.Axis axis : new Direction.Axis[]{Direction.Axis.X, Direction.Axis.Z}) {
            Spot s = tryAxis(world, pos, axis);
            if (s != null) return s;
        }
        return null;
    }

    private static Spot tryAxis(World world, BlockPos pos, Direction.Axis axis) {
        if (!empty(world, pos)) return null;
        Direction right = axis == Direction.Axis.X ? Direction.EAST : Direction.SOUTH;
        Direction left = right.getOpposite();

        BlockPos p = pos;
        for (int n = 0; n < 21 && empty(world, p.down()); n++) p = p.down();
        if (!glow(world, p.down())) return null;
        for (int n = 0; n < 21 && empty(world, p.offset(left)); n++) p = p.offset(left);
        if (!glow(world, p.offset(left))) return null;
        BlockPos bl = p;

        int w = 0;
        while (w < 22 && empty(world, bl.offset(right, w))) {
            if (!glow(world, bl.offset(right, w).down())) return null;
            w++;
        }
        if (w < 2 || w > 21 || !glow(world, bl.offset(right, w))) return null;

        int h = 0;
        rows:
        while (h < 22) {
            for (int i = 0; i < w; i++) {
                if (!empty(world, bl.offset(right, i).up(h))) break rows;
            }
            if (!glow(world, bl.offset(left).up(h)) || !glow(world, bl.offset(right, w).up(h))) return null;
            h++;
        }
        if (h < 3 || h > 21) return null;
        for (int i = 0; i < w; i++) {
            if (!glow(world, bl.offset(right, i).up(h))) return null;
        }
        return new Spot(world.getRegistryKey(), bl, axis, w, h);
    }

    /** Fills a frame's inside with portal water. */
    public static void fill(ServerWorld world, Spot s) {
        BlockState water = RiftBlocks.RIFT_PORTAL.getDefaultState().with(RiftPortalBlock.AXIS, s.axis());
        Direction right = s.right();
        for (int j = 0; j < s.height(); j++) {
            for (int i = 0; i < s.width(); i++) {
                // Flag 2 only: no neighbour updates, so the half-filled portal can't drain itself.
                world.setBlockState(s.bottomLeft().offset(right, i).up(j), water, 2 | 16);
            }
        }
        Vec3d c = s.floorCentre();
        world.playSound(null, c.x, c.y + 1, c.z, SoundEvents.ITEM_BUCKET_EMPTY, SoundCategory.BLOCKS, 1.0f, 0.8f);
        world.playSound(null, c.x, c.y + 1, c.z, SoundEvents.BLOCK_BEACON_ACTIVATE, SoundCategory.BLOCKS, 0.8f, 1.4f);
    }

    /** Builds a whole portal: a 4 x 5 glowstone frame with water inside. {@code bottomLeft}
     *  is the first water cell, and it runs along {@code right} and up. */
    public static Spot build(ServerWorld world, BlockPos bottomLeft, Direction right) {
        Direction.Axis axis = right.getAxis();
        // Normalise so the spot always runs along the positive axis.
        if (right.getDirection() == Direction.AxisDirection.NEGATIVE) {
            bottomLeft = bottomLeft.offset(right, 1);
            right = right.getOpposite();
        }
        for (int j = -1; j <= 3; j++) {
            for (int i = -1; i <= 2; i++) {
                BlockPos p = bottomLeft.offset(right, i).up(j);
                boolean frame = i == -1 || i == 2 || j == -1 || j == 3;
                if (frame) world.setBlockState(p, Blocks.GLOWSTONE.getDefaultState(), 2 | 16);
                else world.setBlockState(p, Blocks.AIR.getDefaultState(), 2 | 16);
            }
        }
        Spot s = new Spot(world.getRegistryKey(), bottomLeft, axis, 2, 3);
        fill(world, s);
        return s;
    }

    /** Clears a portal built by {@link #build}, putting {@code floor} back under it. */
    public static void unbuild(ServerWorld world, Spot s, BlockState floor) {
        Direction right = s.right();
        for (int j = -1; j <= s.height(); j++) {
            for (int i = -1; i <= s.width(); i++) {
                BlockPos p = s.bottomLeft().offset(right, i).up(j);
                world.setBlockState(p, j == -1 ? floor : Blocks.AIR.getDefaultState(), 2 | 16);
            }
        }
    }

    /** A portal outside the rift just opened. It becomes the one UNRENDERED knows about. */
    public static void onOpened(ServerWorld world, Spot s) {
        if (world.getRegistryKey() == RiftMod.RIFT) return;
        lastPortal = s;
        save();
        JsonObject p = portalJson();
        if (p != null) {
            p.addProperty("t", "portal");
            Bridge.send(p);
        }
    }

    /** {@code {x, y, z, axis, dim}} of the last portal, or null. */
    public static JsonObject portalJson() {
        Spot s = lastPortal;
        if (s == null) return null;
        JsonObject o = new JsonObject();
        o.addProperty("x", s.bottomLeft().getX());
        o.addProperty("y", s.bottomLeft().getY());
        o.addProperty("z", s.bottomLeft().getZ());
        o.addProperty("axis", s.axis() == Direction.Axis.X ? "x" : "z");
        o.addProperty("dim", s.dim().getValue().toString());
        return o;
    }

    /** Is the last portal still standing? (Somebody may have broken it.) */
    public static boolean lastPortalStands(MinecraftServer server) {
        Spot s = lastPortal;
        if (s == null) return false;
        ServerWorld w = server.getWorld(s.dim());
        return w != null && w.getBlockState(s.bottomLeft()).isOf(RiftBlocks.RIFT_PORTAL);
    }

    /** Builds a portal three blocks in front of {@code player}, facing them. */
    public static Spot buildInFront(ServerPlayerEntity player) {
        ServerWorld world = player.getServerWorld();
        Direction facing = player.getHorizontalFacing();
        Direction right = facing.rotateYClockwise();
        BlockPos feet = player.getBlockPos();
        BlockPos centre = feet.offset(facing, 3);
        // Two cells wide: the left one sits a cell to the left of centre.
        BlockPos bottomLeft = centre.offset(right, -1);
        Spot s = build(world, bottomLeft, right);
        onOpened(world, s);
        return s;
    }

    // --- JOIN WORLDS --------------------------------------------------------------------

    /** UNRENDERED pressed JOIN WORLDS. Everyone here gets the button. */
    public static void onJoinRequest(MinecraftServer server, String pilot) {
        MutableText button = Text.literal("[JOIN WORLDS]").styled(st -> st
            .withColor(Formatting.GOLD)
            .withBold(true)
            .withClickEvent(new ClickEvent(ClickEvent.Action.RUN_COMMAND, "/rift join"))
            .withHoverEvent(new HoverEvent(HoverEvent.Action.SHOW_TEXT,
                Text.literal("Open a glowstone portal in front of you"))));
        broadcast(server, Text.literal(pilot + " opened a rift from UNRENDERED. ").formatted(Formatting.GRAY).append(button));
        if (lastPortalStands(server)) {
            JsonObject p = portalJson();
            if (p != null) {
                p.addProperty("t", "portal");
                Bridge.send(p);
            }
        }
    }

    // --- Going through ------------------------------------------------------------------

    public static void onEnter(ServerPlayerEntity player, BlockPos pos, Direction.Axis axis) {
        UUID id = player.getUuid();
        Integer last = lastTouch.get(id);
        boolean fresh = last == null || last > 2;
        lastTouch.put(id, 0);
        if (cooldown.containsKey(id)) return;
        // Only on stepping in, not every tick spent standing in the water.
        if (!fresh) return;

        ServerWorld world = player.getServerWorld();
        if (world.getRegistryKey() == RiftMod.RIFT) {
            cooldown.put(id, 60);
            RiftVisitors.sendHome(player);
            return;
        }
        if (!Bridge.linked()) {
            cooldown.put(id, 40);
            player.sendMessage(Text.literal("The water is still. Nothing is on the other side.")
                .formatted(Formatting.DARK_AQUA, Formatting.ITALIC), true);
            return;
        }
        Spot frame = findPortalAround(world, pos, axis);
        if (frame != null) onOpened(world, frame);

        // Back out the way they came in.
        Vec3d normal = axis == Direction.Axis.X ? new Vec3d(0, 0, 1) : new Vec3d(1, 0, 0);
        Vec3d look = player.getRotationVector();
        double side = look.dotProduct(normal) > 0 ? -1 : 1;
        Vec3d c = frame != null ? frame.floorCentre() : Vec3d.ofBottomCenter(pos);
        Vec3d back = c.add(normal.multiply(side * 1.6));
        float yaw = (float) (MathHelper.atan2(-normal.x * side, normal.z * side) * (180.0 / Math.PI));
        cooldown.put(id, 60);
        RiftVisitors.requestArrival(player, world.getRegistryKey(), back, yaw);
    }

    /** A clear spot on whichever side of {@code s} has room, {@code dist} blocks out of the water. */
    public static Vec3d exitBeside(ServerWorld w, Spot s, double dist) {
        Vec3d c = s.floorCentre();
        Vec3d n = s.normal();
        for (double side : new double[]{1, -1}) {
            Vec3d at = c.add(n.multiply(side * dist));
            BlockPos p = BlockPos.ofFloored(at);
            if (w.getBlockState(p).getCollisionShape(w, p).isEmpty() && w.getBlockState(p.up()).getCollisionShape(w, p.up()).isEmpty())
                return at;
        }
        return c.add(n.multiply(dist));
    }

    /**
     * A mob, an animal, a dropped item, an arrow: anything that isn't a player walking into
     * the water goes through, the way things go through a nether portal. Out of the overworld
     * it lands in UNRENDERED's world (the rift), in front of UNRENDERED's own portal. Out of
     * the rift it lands back in front of the Minecraft portal. UNRENDERED's own enemies go
     * home to UNRENDERED instead.
     */
    public static void onEntityEnter(Entity entity, ServerWorld world, BlockPos pos, Direction.Axis axis) {
        if (entity.isRemoved() || entity.hasPortalCooldown() || entity.hasVehicle()) return;
        // A horse, a boat, a minecart carrying somebody: the riders get off in the water and cross
        // on their own, as themselves, so UNRENDERED is told they came. The mount goes on alone.
        for (Entity rider : new java.util.ArrayList<>(entity.getPassengerList()))
            if (rider instanceof PlayerEntity) rider.stopRiding();
        if (entity.getType() == RiftEntities.ECHO || entity.getType() == RiftEntities.SEAT || entity.getType() == RiftEntities.PILOT) return;
        MinecraftServer server = world.getServer();
        if (entity instanceof UnrEnemyEntity enemy) {
            if (Bridge.linked()) RiftEnemies.sendBack(enemy);
            return;
        }
        if (!Bridge.linked()) return;

        Spot here = findPortalAround(world, pos, axis);
        Spot there;
        ServerWorld dest;
        if (world.getRegistryKey() == RiftMod.RIFT) {
            if (!lastPortalStands(server)) return;
            there = lastPortal;
            dest = server.getWorld(there.dim());
        } else {
            there = riftPortal;
            dest = server.getWorld(RiftMod.RIFT);
        }
        if (there == null || dest == null) return;

        // Out the far side: whichever way it was going through this portal, it keeps going.
        Vec3d n = here != null ? here.normal() : (axis == Direction.Axis.X ? new Vec3d(0, 0, 1) : new Vec3d(1, 0, 0));
        Vec3d c = here != null ? here.floorCentre() : Vec3d.ofBottomCenter(pos);
        double side = entity.getPos().subtract(c).dotProduct(n) > 0 ? -1 : 1;
        Vec3d at = there.floorCentre().add(there.normal().multiply(side * 1.8));
        BlockPos ap = BlockPos.ofFloored(at);
        if (!dest.getBlockState(ap).getCollisionShape(dest, ap).isEmpty()) at = exitBeside(dest, there, 1.8);
        dest.getChunk(BlockPos.ofFloored(at));

        // A thrown crab core going into UNRENDERED's world becomes UNRENDERED's own bomb there,
        // flying on from the far side of UNRENDERED's portal on the same line.
        if (entity instanceof net.unrendered.rift.entity.CrabCoreEntity core && dest.getRegistryKey() == RiftMod.RIFT) {
            Vec3d v = core.getVelocity();
            com.google.gson.JsonObject t = Bridge.msg("thrown");
            t.addProperty("item", "crab_core");
            t.addProperty("x", at.x);
            t.addProperty("z", at.z);
            t.addProperty("dx", v.x);
            t.addProperty("dz", v.z);
            if (core.getOwner() != null) t.addProperty("uuid", core.getOwner().getUuidAsString());
            Bridge.send(t);
            core.discard();
            return;
        }

        entity.setPortalCooldown(80);
        Entity moved = entity.teleportTo(new TeleportTarget(dest, at, entity.getVelocity(), entity.getYaw(), entity.getPitch(),
            TeleportTarget.NO_OP));
        if (moved != null) moved.setPortalCooldown(80);
        dest.playSound(null, at.x, at.y, at.z, SoundEvents.BLOCK_PORTAL_TRAVEL, SoundCategory.NEUTRAL, 0.15f, 1.8f);
    }

    /** The portal a given water block belongs to. */
    public static Spot findPortalAround(World world, BlockPos pos, Direction.Axis axis) {
        Direction right = axis == Direction.Axis.X ? Direction.EAST : Direction.SOUTH;
        BlockPos p = pos;
        for (int n = 0; n < 21 && world.getBlockState(p.down()).isOf(RiftBlocks.RIFT_PORTAL); n++) p = p.down();
        for (int n = 0; n < 21 && world.getBlockState(p.offset(right.getOpposite())).isOf(RiftBlocks.RIFT_PORTAL); n++) {
            p = p.offset(right.getOpposite());
        }
        int w = 0;
        while (w < 21 && world.getBlockState(p.offset(right, w)).isOf(RiftBlocks.RIFT_PORTAL)) w++;
        int h = 0;
        while (h < 21 && world.getBlockState(p.up(h)).isOf(RiftBlocks.RIFT_PORTAL)) h++;
        if (w == 0 || h == 0) return null;
        return new Spot(world.getRegistryKey(), p, axis, w, h);
    }
}
