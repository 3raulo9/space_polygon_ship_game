package net.unrendered.rift;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.mojang.serialization.JsonOps;
import net.fabricmc.fabric.api.networking.v1.PlayerLookup;
import net.fabricmc.fabric.api.networking.v1.ServerPlayNetworking;
import net.minecraft.block.BlockRenderType;
import net.minecraft.block.BlockState;
import net.minecraft.command.argument.BlockArgumentParser;
import net.minecraft.component.DataComponentTypes;
import net.minecraft.component.type.AttributeModifiersComponent;
import net.minecraft.component.type.FoodComponent;
import net.minecraft.entity.Entity;
import net.minecraft.entity.ItemEntity;
import net.minecraft.entity.attribute.EntityAttributes;
import net.minecraft.entity.damage.DamageSource;
import net.minecraft.fluid.FluidState;
import net.minecraft.item.ItemStack;
import net.minecraft.registry.Registries;
import net.minecraft.registry.RegistryOps;
import net.minecraft.registry.tag.FluidTags;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.text.Text;
import net.minecraft.util.Formatting;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Box;
import net.minecraft.util.math.Direction;
import net.minecraft.util.math.Vec3d;
import net.minecraft.util.shape.VoxelShape;
import net.minecraft.world.LightType;
import net.minecraft.world.World;
import net.unrendered.rift.bridge.Bridge;
import net.unrendered.rift.bridge.Session;
import net.unrendered.rift.bridge.Wire;
import net.unrendered.rift.entity.PilotEntity;
import net.unrendered.rift.net.CraftPayload;

import java.util.ArrayList;
import java.util.Collections;
import java.util.IdentityHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * An UNRENDERED pilot inside Minecraft. UNRENDERED keeps simulating their craft and keeps
 * its own HUD, so all Minecraft has to do is show it the world as it is: every block's
 * baked model and texture, every mob and item as Minecraft rendered it this tick. It also
 * gives the pilot a body here that mobs can see, chase and hit.
 */
public final class RiftPilot {
    private static boolean active;
    private static PilotEntity pilot;
    private static WorldRef world;
    private static int ox, oy, oz, sx, sy, sz;
    private static final List<BlockState> palette = new ArrayList<>();
    private static final Map<BlockState, Integer> paletteIndex = new IdentityHashMap<>();
    private static final Set<BlockPos> changed = Collections.synchronizedSet(new LinkedHashSet<>());
    private static Vec3d pilotPos = Vec3d.ZERO;
    private static long captureSerial;
    private static int tick;
    private static final ExecutorService textures = Executors.newSingleThreadExecutor(r -> {
        Thread t = new Thread(r, "rift-textures");
        t.setDaemon(true);
        return t;
    });

    /** The world the pilot is in. */
    private record WorldRef(net.minecraft.registry.RegistryKey<World> key) {}

    private RiftPilot() {}

    public static boolean active() {
        return active;
    }

    public static void clear() {
        active = false;
        pilot = null;
        world = null;
        palette.clear();
        paletteIndex.clear();
        changed.clear();
        RiftCapture.nbtSent.clear();
    }

    private static ServerWorld world(MinecraftServer server) {
        return world == null ? null : server.getWorld(world.key());
    }

    // --- Arriving -----------------------------------------------------------------------

    public static void onEnter(MinecraftServer server, JsonObject m) {
        if (active) onLeave(server);
        RiftPortals.Spot spot = RiftPortals.lastPortalStands(server) ? RiftPortals.lastPortal : null;
        if (spot == null) {
            // No portal standing on this side: tear one open next to the first player.
            ServerPlayerEntity host = null;
            for (ServerPlayerEntity p : server.getPlayerManager().getPlayerList()) {
                if (p.getServerWorld().getRegistryKey() != RiftMod.RIFT) {
                    host = p;
                    break;
                }
            }
            if (host == null) {
                JsonObject c = Bridge.msg("chat");
                c.addProperty("text", "NOBODY IS STANDING IN THE MINECRAFT WORLD TO OPEN A PORTAL BESIDE");
                Bridge.send(c);
                return;
            }
            spot = RiftPortals.buildInFront(host);
            RiftPortals.broadcast(server, Text.literal("A rift tore open beside " + host.getGameProfile().getName() + ".")
                .formatted(Formatting.GRAY));
        }
        ServerWorld w = server.getWorld(spot.dim());
        if (w == null) return;
        world = new WorldRef(spot.dim());
        active = true;
        Session s = Bridge.session();
        String name = s != null ? s.pilotName : Session.str(m, "name");
        String chassis = Session.str(m, "chassis");

        Vec3d centre = spot.floorCentre();
        int r = RiftConfig.regionRadius;
        ox = (int) Math.floor(centre.x) - r;
        oz = (int) Math.floor(centre.z) - r;
        sx = sz = 2 * r + 1;
        int bottom = Math.max(w.getBottomY(), spot.bottomLeft().getY() - RiftConfig.regionBelow);
        int top = Math.min(w.getTopY(), spot.bottomLeft().getY() + RiftConfig.regionAbove);
        oy = bottom;
        sy = Math.max(1, top - bottom);

        sendRegion(w);

        // Out of the water on whichever side is clear, facing away from it.
        Vec3d n = spot.normal();
        Vec3d out = centre.add(n.multiply(1.8));
        if (!clear(w, BlockPos.ofFloored(out))) out = centre.add(n.multiply(-1.8));
        Vec3d away = out.subtract(centre);
        float yaw = (float) Math.toDegrees(Math.atan2(-away.x, away.z));
        pilotPos = out;

        PilotEntity body = RiftEntities.PILOT.create(w);
        if (body != null) {
            body.refreshPositionAndAngles(out.x, out.y, out.z, yaw, 0f);
            body.setHeadYaw(yaw);
            body.setBodyYaw(yaw);
            body.setCustomName(Text.literal(name).formatted(Formatting.LIGHT_PURPLE));
            w.spawnEntity(body);
            pilot = body;
        }

        JsonObject sp = Bridge.msg("spawn");
        sp.addProperty("x", out.x);
        sp.addProperty("y", out.y);
        sp.addProperty("z", out.z);
        sp.addProperty("yaw", yaw);
        Bridge.send(sp);

        RiftPortals.broadcast(server, Text.literal(name + " stepped out of UNRENDERED"
            + (chassis.isEmpty() ? "" : " in a " + chassis.toLowerCase()) + ".").formatted(Formatting.LIGHT_PURPLE));
    }

    private static boolean clear(World w, BlockPos p) {
        return w.getBlockState(p).getCollisionShape(w, p).isEmpty() && w.getBlockState(p.up()).getCollisionShape(w, p.up()).isEmpty();
    }

    private static int indexOf(ServerWorld w, BlockState state, BlockPos pos, JsonArray newEntries) {
        Integer i = paletteIndex.get(state);
        if (i != null) return i;
        int idx = palette.size();
        palette.add(state);
        paletteIndex.put(state, idx);
        newEntries.add(entry(w, state, pos.toImmutable(), idx));
        return idx;
    }

    private static void sendRegion(ServerWorld w) {
        palette.clear();
        paletteIndex.clear();
        JsonArray entries = new JsonArray();
        int n = sx * sy * sz;
        Wire.Out out = new Wire.Out(n * 3 + 16);
        byte[] light = new byte[n];
        BlockPos.Mutable pos = new BlockPos.Mutable();
        int k = 0;
        for (int y = 0; y < sy; y++) {
            for (int z = 0; z < sz; z++) {
                for (int x = 0; x < sx; x++) {
                    pos.set(ox + x, oy + y, oz + z);
                    BlockState state = w.getBlockState(pos);
                    out.u16(indexOf(w, state, pos, entries));
                    int sky = w.getLightLevel(LightType.SKY, pos);
                    int block = w.getLightLevel(LightType.BLOCK, pos);
                    light[k++] = (byte) ((sky << 4) | block);
                }
            }
        }
        out.bytes(light);
        JsonObject pal = Bridge.msg("palette");
        pal.add("entries", entries);
        Bridge.send(pal);
        JsonObject h = new JsonObject();
        h.addProperty("ox", ox);
        h.addProperty("oy", oy);
        h.addProperty("oz", oz);
        h.addProperty("sx", sx);
        h.addProperty("sy", sy);
        h.addProperty("sz", sz);
        Bridge.sendBin("region", h, out.toArray());
        RiftMod.LOG.info("Rift: sent the pilot {}x{}x{} blocks, {} block states", sx, sy, sz, palette.size());
    }

    /** One palette entry: what a block state looks like and how it behaves. */
    static JsonObject entry(ServerWorld w, BlockState state, BlockPos pos, int idx) {
        JsonObject e = new JsonObject();
        e.addProperty("i", idx);
        e.addProperty("id", BlockArgumentParser.stringifyBlockState(state));
        e.addProperty("solid", state.isOpaqueFullCube(w, pos));
        FluidState fluid = state.getFluidState();
        String liquid = fluid.isEmpty() ? null : fluid.isIn(FluidTags.LAVA) ? "lava" : "water";
        if (liquid == null) e.add("liquid", null);
        else e.addProperty("liquid", liquid);
        e.addProperty("emit", state.getLuminance());
        VoxelShape shape = state.getCollisionShape(w, pos);
        e.addProperty("collide", !shape.isEmpty());
        e.addProperty("top", shape.isEmpty() ? 0.0 : shape.getMax(Direction.Axis.Y));
        e.addProperty("portal", state.isOf(RiftBlocks.RIFT_PORTAL));

        JsonArray quads = null;
        RiftHooks.Client c = RiftHooks.client;
        if (c != null) {
            try {
                quads = c.blockQuads(state, w, pos);
            } catch (RuntimeException ex) {
                RiftMod.LOG.debug("Rift: no model for {}: {}", state, ex.toString());
            }
        }
        if (quads == null) quads = fallbackCube(w, state, pos);
        if (liquid != null) addFluid(quads, w, state, pos, liquid);
        e.add("quads", quads);
        return e;
    }

    /** Without a client to read models from: a full cube in the block's map colour. */
    private static JsonArray fallbackCube(ServerWorld w, BlockState state, BlockPos pos) {
        JsonArray quads = new JsonArray();
        if (state.isAir() || state.getRenderType() == BlockRenderType.INVISIBLE) return quads;
        if (!state.getFluidState().isEmpty() && state.getCollisionShape(w, pos).isEmpty()) return quads;
        int rgb = state.getMapColor(w, pos).color;
        for (Direction d : Direction.values()) quads.add(face(d, "", rgb, 1.0f, d.getId()));
        return quads;
    }

    /** Fluids aren't baked models; this is their surface and sides, in vanilla's still texture. */
    private static void addFluid(JsonArray quads, ServerWorld w, BlockState state, BlockPos pos, String liquid) {
        FluidState fluid = state.getFluidState();
        float h = Math.max(0.1f, Math.min(1.0f, fluid.getHeight(w, pos)));
        if (w.getFluidState(pos.up()).getFluid().matchesType(fluid.getFluid())) h = 1.0f;
        String tex = "water".equals(liquid) ? "minecraft:textures/block/water_still.png" : "minecraft:textures/block/lava_still.png";
        int tint = "water".equals(liquid) ? w.getBiome(pos).value().getWaterColor() : -1;
        for (Direction d : Direction.values()) quads.add(face(d, tex, tint, h, d.getId()));
    }

    /** One axis-aligned face of a box from y = 0 to {@code h}, as a palette quad. */
    private static JsonObject face(Direction d, String tex, int tintRgb, float h, int cull) {
        float[][] v = switch (d) {
            case DOWN -> new float[][]{{0, 0, 1}, {0, 0, 0}, {1, 0, 0}, {1, 0, 1}};
            case UP -> new float[][]{{0, h, 0}, {0, h, 1}, {1, h, 1}, {1, h, 0}};
            case NORTH -> new float[][]{{1, h, 0}, {1, 0, 0}, {0, 0, 0}, {0, h, 0}};
            case SOUTH -> new float[][]{{0, h, 1}, {0, 0, 1}, {1, 0, 1}, {1, h, 1}};
            case WEST -> new float[][]{{0, h, 0}, {0, 0, 0}, {0, 0, 1}, {0, h, 1}};
            case EAST -> new float[][]{{1, h, 1}, {1, 0, 1}, {1, 0, 0}, {1, h, 0}};
        };
        float[][] uv = {{0, 0}, {0, 1}, {1, 1}, {1, 0}};
        JsonObject q = new JsonObject();
        q.addProperty("tex", tex);
        q.addProperty("cull", cull);
        if (tintRgb >= 0) {
            JsonArray t = new JsonArray();
            t.add((tintRgb >> 16) & 0xFF);
            t.add((tintRgb >> 8) & 0xFF);
            t.add(tintRgb & 0xFF);
            q.add("tint", t);
        } else {
            q.add("tint", null);
        }
        q.addProperty("shade", true);
        JsonArray vs = new JsonArray();
        for (int i = 0; i < 4; i++) {
            vs.add(v[i][0]);
            vs.add(v[i][1]);
            vs.add(v[i][2]);
            vs.add(uv[i][0]);
            vs.add(d.getAxis() == Direction.Axis.Y ? uv[i][1] : uv[i][1] * h);
        }
        q.add("v", vs);
        return q;
    }

    // --- While they're here -------------------------------------------------------------

    public static void onMe(MinecraftServer server, JsonObject m) {
        if (!active) return;
        double x = m.get("x").getAsDouble(), y = m.get("y").getAsDouble(), z = m.get("z").getAsDouble();
        float yaw = m.has("yaw") ? m.get("yaw").getAsFloat() : 0f;
        float pitch = m.has("pitch") ? m.get("pitch").getAsFloat() : 0f;
        pilotPos = new Vec3d(x, y, z);
        PilotEntity p = pilot;
        if (p != null && !p.isRemoved()) {
            p.updatePositionAndAngles(x, y, z, yaw, pitch);
            p.setHeadYaw(yaw);
            p.setBodyYaw(yaw);
            if (m.has("hp") && m.has("max")) {
                float max = Math.max(0.01f, m.get("max").getAsFloat());
                p.setHealth(Math.max(0.5f, Math.min(20f, 20f * m.get("hp").getAsFloat() / max)));
            }
        }
    }

    /** The pilot's craft as UNRENDERED drew it, for every client watching the pilot. */
    public static void onCraft(MinecraftServer server, byte[] packed) {
        PilotEntity p = pilot;
        if (!active || p == null || packed == null || packed.length > 1_000_000) return;
        CraftPayload payload = new CraftPayload(p.getId(), packed);
        for (ServerPlayerEntity viewer : PlayerLookup.tracking(p)) ServerPlayNetworking.send(viewer, payload);
    }

    public static void onTex(MinecraftServer server, JsonObject m) {
        if (!m.has("ids")) return;
        List<String> ids = new ArrayList<>();
        for (JsonElement e : m.getAsJsonArray("ids")) ids.add(e.getAsString());
        textures.submit(() -> {
            for (String id : ids) {
                JsonObject t = Bridge.msg("texdata");
                t.addProperty("id", id);
                RiftHooks.Client c = RiftHooks.client;
                RiftHooks.TextureData data = null;
                try {
                    if (c != null) data = c.texture(id);
                } catch (RuntimeException e) {
                    RiftMod.LOG.debug("Rift: texture {} failed: {}", id, e.toString());
                }
                if (data == null) {
                    t.add("png", null);
                    t.addProperty("frametime", 1);
                } else {
                    t.addProperty("png", java.util.Base64.getEncoder().encodeToString(data.png()));
                    t.addProperty("frametime", data.frametime());
                }
                Bridge.send(t);
            }
        });
    }

    public static void onHit(MinecraftServer server, JsonObject m) {
        ServerWorld w = world(server);
        PilotEntity p = pilot;
        if (w == null || p == null) return;
        Entity target = w.getEntityById(m.get("id").getAsInt());
        if (target == null || target == p) return;
        float dmg = m.has("dmg") ? m.get("dmg").getAsFloat() : 1f;
        target.damage(w.getDamageSources().mobAttack(p), dmg);
    }

    public static void onBreak(MinecraftServer server, JsonObject m) {
        ServerWorld w = world(server);
        if (w == null || !RiftConfig.allowBlockBreak) return;
        BlockPos pos = BlockPos.ofFloored(m.get("x").getAsDouble(), m.get("y").getAsDouble(), m.get("z").getAsDouble());
        BlockState state = w.getBlockState(pos);
        float hardness = state.getHardness(w, pos);
        if (state.isAir() || hardness < 0f || hardness >= 50f || state.isOf(RiftBlocks.RIFT_PORTAL)) return;
        if (!state.getFluidState().isEmpty() && state.getCollisionShape(w, pos).isEmpty()) return;
        w.breakBlock(pos, true, pilot);
    }

    public static void onPickup(MinecraftServer server, JsonObject m) {
        ServerWorld w = world(server);
        if (w == null) return;
        Entity e = w.getEntityById(m.get("id").getAsInt());
        if (!(e instanceof ItemEntity item) || item.isRemoved()) return;
        if (item.getPos().squaredDistanceTo(pilotPos) > 6.0 * 6.0) return;
        ItemStack stack = item.getStack();
        if (stack.isEmpty()) return;
        RegistryOps<JsonElement> ops = w.getRegistryManager().getOps(JsonOps.INSTANCE);
        JsonElement encoded = ItemStack.CODEC.encodeStart(ops, stack).result().orElse(null);
        if (encoded == null) return;

        JsonObject got = Bridge.msg("got");
        got.add("stack", encoded);
        got.addProperty("item", Registries.ITEM.getId(stack.getItem()).toString());
        got.addProperty("name", stack.getName().getString());
        got.addProperty("count", stack.getCount());
        String tex = null;
        RiftHooks.Client c = RiftHooks.client;
        if (c != null) {
            try {
                tex = c.itemTexture(stack);
            } catch (RuntimeException ignored) {
            }
        }
        if (tex == null) {
            var id = Registries.ITEM.getId(stack.getItem());
            tex = id.getNamespace() + ":textures/item/" + id.getPath() + ".png";
        }
        got.addProperty("tex", tex);
        got.addProperty("attack", attackOf(stack));
        FoodComponent food = stack.get(DataComponentTypes.FOOD);
        got.addProperty("food", food == null ? 0 : food.nutrition());
        Bridge.send(got);
        w.playSound(null, item.getX(), item.getY(), item.getZ(), net.minecraft.sound.SoundEvents.ENTITY_ITEM_PICKUP,
            net.minecraft.sound.SoundCategory.PLAYERS, 0.3f, 1.2f);
        item.discard();
    }

    /** A weapon's hit, fist included: 1 plus whatever its attack modifiers add. */
    private static double attackOf(ItemStack stack) {
        double dmg = 1.0;
        AttributeModifiersComponent mods = stack.getOrDefault(DataComponentTypes.ATTRIBUTE_MODIFIERS, AttributeModifiersComponent.DEFAULT);
        for (AttributeModifiersComponent.Entry entry : mods.modifiers()) {
            if (entry.attribute().value() == EntityAttributes.GENERIC_ATTACK_DAMAGE.value()) dmg += entry.modifier().value();
        }
        return dmg;
    }

    public static void onDrop(MinecraftServer server, JsonObject m) {
        ServerWorld w = world(server);
        if (w == null && server.getOverworld() != null) w = server.getOverworld();
        if (w == null || !m.has("stack")) return;
        RegistryOps<JsonElement> ops = w.getRegistryManager().getOps(JsonOps.INSTANCE);
        ItemStack stack = ItemStack.CODEC.parse(ops, m.get("stack")).result().orElse(ItemStack.EMPTY);
        if (stack.isEmpty()) return;
        double x = m.has("x") ? m.get("x").getAsDouble() : pilotPos.x;
        double y = m.has("y") ? m.get("y").getAsDouble() : pilotPos.y;
        double z = m.has("z") ? m.get("z").getAsDouble() : pilotPos.z;
        ItemEntity item = new ItemEntity(w, x, y + 0.5, z, stack);
        item.setPickupDelay(30);
        w.spawnEntity(item);
    }

    public static void onLeave(MinecraftServer server) {
        PilotEntity p = pilot;
        if (p != null && !p.isRemoved()) p.discard();
        if (active) {
            Session s = Bridge.session();
            RiftPortals.broadcast(server, Text.literal((s != null ? s.pilotName : "The pilot") + " went back to UNRENDERED.")
                .formatted(Formatting.GRAY));
        }
        clear();
    }

    public static void onLinkLost(MinecraftServer server) {
        if (active) onLeave(server);
    }

    /** Minecraft hurt the pilot. UNRENDERED does the bookkeeping. */
    public static void onPilotHurt(PilotEntity body, DamageSource source, float amount) {
        JsonObject h = Bridge.msg("hurt");
        h.addProperty("amount", amount);
        Entity by = source.getAttacker();
        h.addProperty("by", by != null ? by.getName().getString() : source.getName());
        Bridge.send(h);
    }

    /** Called by the world mixin for every block change on the server. */
    public static void onBlockChanged(World w, BlockPos pos) {
        if (!active || world == null || w.getRegistryKey() != world.key()) return;
        int x = pos.getX() - ox, y = pos.getY() - oy, z = pos.getZ() - oz;
        if (x < 0 || y < 0 || z < 0 || x >= sx || y >= sy || z >= sz) return;
        changed.add(pos.toImmutable());
    }

    public static void tick(MinecraftServer server) {
        tick++;
        if (active && !Bridge.linked()) clear();
        if (active && pilot != null && pilot.isRemoved()) {
            // Killed by something Minecraft does that no `hurt` could describe (/kill, the void).
            pilot = null;
        }

        if (active && !changed.isEmpty() && tick % 2 == 0) flushChanges(server);

        // The pilot is a second player here: the world around them stays loaded and alive.
        if (active && pilot != null && !pilot.isRemoved() && tick % 10 == 0 && pilot.getWorld() instanceof ServerWorld pw)
            RiftMod.hold(pw, pilot.getPos());

        ServerWorld rift = server.getWorld(RiftMod.RIFT);
        List<ServerPlayerEntity> visitors = rift != null && Bridge.linked() && tick % 2 == 0 ? rift.getPlayers() : List.of();
        boolean riftWatched = rift != null && Bridge.linked() && (!rift.getPlayers().isEmpty() || RiftSeats.any());
        if (Bridge.linked() && (active || !visitors.isEmpty() || riftWatched)) requestCapture(server, visitors);
    }

    /**
     * The rift dimension's own creatures: mobs that walked through the portal, items on the
     * floor, arrows in the air. Everything near a visitor or one of UNRENDERED's seats, except
     * the rift's stand-ins and the visitors themselves (who travel as avatars). Null when
     * nobody is watching.
     */
    private static List<RiftHooks.EntitySnap> riftEntities(MinecraftServer server) {
        ServerWorld rift = server.getWorld(RiftMod.RIFT);
        if (rift == null) return null;
        List<Vec3d> eyes = new ArrayList<>();
        for (ServerPlayerEntity p : rift.getPlayers()) eyes.add(p.getPos());
        eyes.addAll(RiftSeats.positions());
        if (eyes.isEmpty()) return null;
        Map<Integer, RiftHooks.EntitySnap> found = new java.util.LinkedHashMap<>();
        for (Vec3d at : eyes) {
            Box box = Box.of(at, 192.0, 128.0, 192.0);
            for (Entity e : rift.getEntitiesByClass(Entity.class, box, RiftCapture::riftVisible)) {
                if (found.size() >= 256) break;
                found.computeIfAbsent(e.getId(), id -> RiftCapture.snap(e));
            }
        }
        return new ArrayList<>(found.values());
    }

    private static void flushChanges(MinecraftServer server) {
        ServerWorld w = world(server);
        if (w == null) return;
        List<BlockPos> batch = new ArrayList<>();
        synchronized (changed) {
            var it = changed.iterator();
            while (it.hasNext() && batch.size() < 2048) {
                batch.add(it.next());
                it.remove();
            }
        }
        JsonArray fresh = new JsonArray();
        JsonArray set = new JsonArray();
        for (BlockPos pos : batch) {
            BlockState state = w.getBlockState(pos);
            int idx = indexOf(w, state, pos, fresh);
            JsonArray row = new JsonArray();
            row.add(pos.getX());
            row.add(pos.getY());
            row.add(pos.getZ());
            row.add(idx);
            row.add((w.getLightLevel(LightType.SKY, pos) << 4) | w.getLightLevel(LightType.BLOCK, pos));
            set.add(row);
        }
        if (!fresh.isEmpty()) {
            JsonObject pal = Bridge.msg("palette");
            pal.add("entries", fresh);
            Bridge.send(pal);
        }
        JsonObject b = Bridge.msg("blocks");
        b.add("set", set);
        Bridge.send(b);
    }

    // --- What everyone looks like -------------------------------------------------------

    /**
     * Snapshots what has to be drawn this tick (the mobs around the pilot, and each
     * visitor) and hands it to the client's render thread. With no client, plain boxes are
     * sent straight away.
     */
    public static void requestCapture(MinecraftServer server, List<ServerPlayerEntity> visitors) {
        Session s = Bridge.session();
        if (s == null || s.backlog() > 64) return;   // the link is behind; skip a frame rather than queue it
        List<RiftHooks.EntitySnap> ents = new ArrayList<>();
        ServerWorld w = world(server);
        if (active && w != null) {
            Box box = Box.of(pilotPos, RiftConfig.regionRadius * 2.0, 96.0, RiftConfig.regionRadius * 2.0);
            for (Entity e : w.getOtherEntities(pilot, box, RiftCapture::visible)) ents.add(RiftCapture.snap(e));
        }
        List<RiftHooks.EntitySnap> avatars = new ArrayList<>();
        List<UUID> owners = new ArrayList<>();
        for (ServerPlayerEntity v : visitors) {
            avatars.add(RiftCapture.snap(v));
            owners.add(v.getUuid());
        }
        int time = w != null ? (int) (w.getTimeOfDay() % 24000L) : (int) (server.getOverworld().getTimeOfDay() % 24000L);
        List<RiftHooks.EntitySnap> rents = riftEntities(server);

        RiftHooks.Client c = RiftHooks.client;
        if (c != null) {
            c.request(new RiftHooks.CaptureRequest(++captureSerial, time, active ? ents : null, avatars, owners, rents));
            return;
        }
        if (rents != null) {
            Wire.Out out = new Wire.Out(rents.size() * 600 + 8);
            out.u16(rents.size());
            for (RiftHooks.EntitySnap e : rents) {
                e.y -= RiftMod.FLOOR_Y;
                RiftCapture.writeBox(out, e);
            }
            Bridge.sendBin("rents", new JsonObject(), out.toArray());
        }
        // Dedicated server: boxes.
        if (active) {
            Wire.Out out = new Wire.Out(ents.size() * 600 + 8);
            out.u16(ents.size());
            for (RiftHooks.EntitySnap e : ents) RiftCapture.writeBox(out, e);
            JsonObject h = new JsonObject();
            h.addProperty("time", time);
            Bridge.sendBin("ents", h, out.toArray());
        }
        for (int i = 0; i < avatars.size(); i++) {
            RiftHooks.EntitySnap a = avatars.get(i);
            a.y -= RiftMod.FLOOR_Y;
            Wire.Out out = new Wire.Out(700);
            RiftCapture.writeBox(out, a);
            JsonObject h = new JsonObject();
            h.addProperty("uuid", owners.get(i).toString());
            Bridge.sendBin("avatar", h, out.toArray());
        }
    }
}
