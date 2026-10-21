package net.unrendered.rift;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import net.fabricmc.fabric.api.networking.v1.ServerPlayNetworking;
import net.minecraft.block.BlockState;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.item.Item;
import net.minecraft.item.ItemStack;
import net.minecraft.registry.RegistryKey;
import net.minecraft.registry.RegistryKeys;
import net.minecraft.util.Identifier;
import net.minecraft.util.WorldSavePath;
import net.minecraft.world.TeleportTarget;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.sound.SoundCategory;
import net.minecraft.sound.SoundEvents;
import net.minecraft.text.Text;
import net.minecraft.util.Formatting;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Direction;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.World;
import net.unrendered.rift.bridge.Bridge;
import net.unrendered.rift.bridge.Session;
import net.unrendered.rift.entity.EchoEntity;
import net.unrendered.rift.net.ScenePayload;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.Iterator;
import java.util.List;
import java.util.Map;
import java.util.UUID;

/**
 * Minecraft players inside UNRENDERED. They stay Minecraft players: their inventory, their
 * hearts, their hunger, their mods. They stand in the rift dimension, which UNRENDERED
 * paints with its own frame, and they fight UNRENDERED's hostiles through invisible
 * hitboxes the size of the real thing.
 */
public final class RiftVisitors {
    /** Where someone goes back to: the side of the portal they stepped in from. */
    private record Home(RegistryKey<World> dim, Vec3d pos, float yaw) {}

    private static final class Pending {
        final UUID id;
        int ticks;

        Pending(UUID id) {
            this.id = id;
        }
    }

    private static final Map<UUID, Home> homes = new HashMap<>();
    private static final Map<UUID, Pending> pending = new HashMap<>();
    private static final Map<Integer, EchoEntity> echoes = new HashMap<>();
    private static final Map<UUID, Integer> swings = new HashMap<>();
    private static final Map<UUID, Boolean> wasSwinging = new HashMap<>();
    private static int tick;

    private RiftVisitors() {}

    public static int count() {
        MinecraftServer s = RiftMod.server;
        ServerWorld rift = s == null ? null : s.getWorld(RiftMod.RIFT);
        return rift == null ? 0 : rift.getPlayers().size();
    }

    public static void clear() {
        homes.clear();
        loadedFrom = null;
        pending.clear();
        echoes.clear();
        swings.clear();
        wasSwinging.clear();
    }

    // --- Where everyone came from, kept with the world --------------------------------
    // A visitor's way home must outlive a restart: the world can be closed with them still
    // standing in the rift, and the next time they log in that is where they will be.

    private static MinecraftServer loadedFrom;

    private static Path homesFile(MinecraftServer server) {
        return server.getSavePath(WorldSavePath.ROOT).resolve("unrendered_portal_homes.json");
    }

    public static void load(MinecraftServer server) {
        homes.clear();
        loadedFrom = server;
        Path f = homesFile(server);
        if (!Files.exists(f)) return;
        try {
            JsonObject o = JsonParser.parseString(Files.readString(f)).getAsJsonObject();
            for (Map.Entry<String, JsonElement> e : o.entrySet()) {
                JsonObject h = e.getValue().getAsJsonObject();
                RegistryKey<World> dim = RegistryKey.of(RegistryKeys.WORLD, Identifier.of(h.get("dim").getAsString()));
                homes.put(UUID.fromString(e.getKey()), new Home(dim,
                    new Vec3d(h.get("x").getAsDouble(), h.get("y").getAsDouble(), h.get("z").getAsDouble()), h.get("yaw").getAsFloat()));
            }
        } catch (IOException | RuntimeException e) {
            RiftMod.LOG.warn("Rift: could not read where visitors came from: {}", e.toString());
        }
    }

    private static void saveHomes() {
        MinecraftServer server = loadedFrom;
        if (server == null) return;
        JsonObject o = new JsonObject();
        homes.forEach((id, h) -> {
            JsonObject j = new JsonObject();
            j.addProperty("dim", h.dim().getValue().toString());
            j.addProperty("x", h.pos().x);
            j.addProperty("y", h.pos().y);
            j.addProperty("z", h.pos().z);
            j.addProperty("yaw", h.yaw());
            o.add(id.toString(), j);
        });
        try {
            Files.writeString(homesFile(server), o.toString());
        } catch (IOException e) {
            RiftMod.LOG.warn("Rift: could not save where visitors came from: {}", e.toString());
        }
    }

    // --- Arriving and leaving -----------------------------------------------------------

    /** A player stepped into the water. UNRENDERED is told, and they cross when it answers with `place`. */
    public static void requestArrival(ServerPlayerEntity player, RegistryKey<World> dim, Vec3d back, float yaw) {
        homes.put(player.getUuid(), new Home(dim, back, yaw));
        saveHomes();
        pending.put(player.getUuid(), new Pending(player.getUuid()));
        JsonObject a = Bridge.msg("arrive");
        a.addProperty("uuid", player.getUuidAsString());
        a.addProperty("name", player.getGameProfile().getName());
        Bridge.send(a);
        player.sendMessage(Text.literal("The water pulls...").formatted(Formatting.LIGHT_PURPLE, Formatting.ITALIC), true);
    }

    /** UNRENDERED says where a visitor stands, and where its portal stands. */
    public static void onPlace(MinecraftServer server, JsonObject m) {
        ServerWorld rift = server.getWorld(RiftMod.RIFT);
        if (rift == null) {
            RiftMod.LOG.error("Rift: the rift dimension is missing; is the mod's datapack loaded?");
            return;
        }
        if (m.has("portal") && m.get("portal").isJsonObject()) placeRiftPortal(rift, m.getAsJsonObject("portal"));

        UUID id;
        try {
            id = UUID.fromString(Session.str(m, "uuid"));
        } catch (IllegalArgumentException e) {
            return;
        }
        ServerPlayerEntity player = server.getPlayerManager().getPlayer(id);
        if (player == null) return;
        double x = m.get("x").getAsDouble(), z = m.get("z").getAsDouble();
        float heading = m.has("yaw") ? m.get("yaw").getAsFloat() : 0f;
        float yaw = (float) -Math.toDegrees(heading);
        boolean arriving = pending.remove(id) != null;
        if (!arriving && player.getServerWorld() == rift) {
            // Only the portal moved; leave the player where they are.
            return;
        }
        if (!arriving) return;
        cross(player, rift, x, RiftMod.FLOOR_Y, z, yaw);
    }

    private static void cross(ServerPlayerEntity player, ServerWorld rift, double x, double y, double z, float yaw) {
        rift.getChunk(BlockPos.ofFloored(x, y, z));
        player.teleport(rift, x, y, z, yaw, player.getPitch());
        player.setVelocity(Vec3d.ZERO);
        rift.playSound(null, x, y, z, SoundEvents.BLOCK_PORTAL_TRAVEL, SoundCategory.PLAYERS, 0.25f, 1.6f);
        player.sendMessage(Text.literal("UNRENDERED").formatted(Formatting.LIGHT_PURPLE, Formatting.BOLD), true);
        // What other Minecraft players built here before them, as UNRENDERED should show it.
        if (player.getServer() != null) RiftBuilt.sendAll(player.getServer());
    }

    /** Builds (or moves) the glowstone portal in the rift where UNRENDERED's portal stands. */
    private static void placeRiftPortal(ServerWorld rift, JsonObject p) {
        double x = p.get("x").getAsDouble(), z = p.get("z").getAsDouble();
        double facing = p.has("facing") ? p.get("facing").getAsDouble() : 0.0;
        // The water faces `facing`; its plane runs across it.
        double nx = Math.sin(facing), nz = Math.cos(facing);
        Direction right = Math.abs(nx) > Math.abs(nz) ? Direction.SOUTH : Direction.EAST;
        BlockPos centre = BlockPos.ofFloored(x, RiftMod.FLOOR_Y, z);
        BlockPos bottomLeft = centre.offset(right, -1);
        RiftPortals.Spot existing = RiftPortals.riftPortal;
        if (existing != null && existing.bottomLeft().equals(bottomLeft) && existing.axis() == right.getAxis()
            && rift.getBlockState(bottomLeft).isOf(RiftBlocks.RIFT_PORTAL)) {
            return;
        }
        if (existing != null) RiftPortals.unbuild(rift, existing, RiftBlocks.RIFT_FLOOR.getDefaultState());
        RiftPortals.riftPortal = RiftPortals.build(rift, bottomLeft, right);
        RiftPortals.save();
    }

    /** Back the way they came. */
    public static void sendHome(ServerPlayerEntity player) {
        MinecraftServer server = player.getServer();
        if (server == null) return;
        Home home = homes.remove(player.getUuid());
        saveHomes();
        ServerWorld world = home == null ? null : server.getWorld(home.dim());
        Vec3d pos;
        float yaw;
        if (home != null && world != null) {
            pos = home.pos();
            yaw = home.yaw();
        } else {
            // No way back on record (or its world is gone): wherever they would respawn — their
            // bed or anchor if they have one, the world's spawn if not.
            TeleportTarget respawn = player.getRespawnTarget(true, TeleportTarget.NO_OP);
            world = respawn.world();
            if (world == null || world.getRegistryKey() == RiftMod.RIFT) {
                world = server.getOverworld();
                pos = Vec3d.ofBottomCenter(world.getTopPosition(net.minecraft.world.Heightmap.Type.MOTION_BLOCKING, world.getSpawnPos()));
            } else {
                pos = respawn.pos();
            }
            yaw = player.getYaw();
        }
        JsonObject d = Bridge.msg("depart");
        d.addProperty("uuid", player.getUuidAsString());
        Bridge.send(d);
        player.teleport(world, pos.x, pos.y, pos.z, yaw, player.getPitch());
        player.setVelocity(Vec3d.ZERO);
        world.playSound(null, pos.x, pos.y, pos.z, SoundEvents.BLOCK_PORTAL_TRAVEL, SoundCategory.PLAYERS, 0.25f, 0.8f);
    }

    public static void onDisconnect(ServerPlayerEntity player) {
        pending.remove(player.getUuid());
        if (player.getServerWorld().getRegistryKey() == RiftMod.RIFT) {
            JsonObject d = Bridge.msg("depart");
            d.addProperty("uuid", player.getUuidAsString());
            Bridge.send(d);
        }
    }

    /** A visitor died. Minecraft respawns them at home; UNRENDERED must stop holding their seat. */
    public static void onDeath(ServerPlayerEntity player) {
        pending.remove(player.getUuid());
        if (player.getServerWorld().getRegistryKey() != RiftMod.RIFT) return;
        JsonObject d = Bridge.msg("depart");
        d.addProperty("uuid", player.getUuidAsString());
        Bridge.send(d);
        if (homes.remove(player.getUuid()) != null) saveHomes();
    }

    /**
     * Somebody logged in standing in the rift. With UNRENDERED linked they are a visitor again
     * (it is told, as if they had just stepped through). Without it there is nothing there to
     * stand in, so they go home.
     */
    public static void onJoin(ServerPlayerEntity player) {
        if (player.getServerWorld().getRegistryKey() != RiftMod.RIFT) return;
        if (Bridge.linked()) {
            pending.put(player.getUuid(), new Pending(player.getUuid()));
            JsonObject a = Bridge.msg("arrive");
            a.addProperty("uuid", player.getUuidAsString());
            a.addProperty("name", player.getGameProfile().getName());
            Bridge.send(a);
            return;
        }
        sendHome(player);
        player.sendMessage(Text.literal("The rift is closed \u2014 you were sent home.").formatted(Formatting.DARK_PURPLE), false);
    }

    /** Everybody in the rift goes home: the world is closing, or the link is about to go. */
    public static void sendAllHome(MinecraftServer server, String why) {
        pending.clear();
        ServerWorld rift = server.getWorld(RiftMod.RIFT);
        if (rift == null) return;
        for (ServerPlayerEntity p : new ArrayList<>(rift.getPlayers())) {
            p.sendMessage(Text.literal(why).formatted(Formatting.DARK_PURPLE), true);
            sendHome(p);
        }
    }

    /** UNRENDERED went away: nobody can stay in a world that is no longer being drawn. */
    public static void onLinkLost(MinecraftServer server) {
        sendAllHome(server, "UNRENDERED let go of you.");
        for (EchoEntity e : echoes.values()) e.discard();
        echoes.clear();
    }

    // --- Every tick ---------------------------------------------------------------------

    public static void tick(MinecraftServer server) {
        tick++;
        ServerWorld rift = server.getWorld(RiftMod.RIFT);

        // Somebody who stepped in while UNRENDERED never answered goes in anyway, by the
        // portal, after three seconds.
        for (Iterator<Pending> it = pending.values().iterator(); it.hasNext(); ) {
            Pending p = it.next();
            if (++p.ticks < 60) continue;
            it.remove();
            ServerPlayerEntity player = server.getPlayerManager().getPlayer(p.id);
            if (player != null && rift != null && Bridge.linked()) {
                RiftPortals.Spot portal = RiftPortals.riftPortal;
                Vec3d at = portal != null ? portal.floorCentre().add(portal.normal().multiply(2.0)) : new Vec3d(0.5, RiftMod.FLOOR_Y, 0.5);
                cross(player, rift, at.x, RiftMod.FLOOR_Y, at.z, player.getYaw());
            }
        }

        if (rift == null) return;
        List<ServerPlayerEntity> players = rift.getPlayers();

        for (ServerPlayerEntity p : players) {
            // The torus: walk off one edge and come back on the other.
            double half = RiftMod.TORUS / 2;
            double x = p.getX(), z = p.getZ();
            if (x >= half || x < -half || z >= half || z < -half) {
                double nx = x - RiftMod.TORUS * Math.floor(x / RiftMod.TORUS + 0.5);
                double nz = z - RiftMod.TORUS * Math.floor(z / RiftMod.TORUS + 0.5);
                Vec3d v = p.getVelocity();
                p.teleport(rift, nx, p.getY(), nz, p.getYaw(), p.getPitch());
                p.setVelocity(v);
                p.velocityModified = true;
            }
            // Fell out of the world below the grid (it shouldn't happen, but a pickaxe finds a way).
            if (p.getY() < RiftMod.FLOOR_Y - 40) p.teleport(rift, p.getX(), RiftMod.FLOOR_Y + 1, p.getZ(), p.getYaw(), p.getPitch());

            if (!Bridge.linked()) continue;
            boolean swinging = p.handSwinging;
            if (swinging && !wasSwinging.getOrDefault(p.getUuid(), false)) swings.merge(p.getUuid(), 1, Integer::sum);
            wasSwinging.put(p.getUuid(), swinging);

            JsonObject v = Bridge.msg("visitor");
            v.addProperty("uuid", p.getUuidAsString());
            v.addProperty("name", p.getGameProfile().getName());
            v.addProperty("x", p.getX());
            v.addProperty("y", p.getY() - RiftMod.FLOOR_Y);
            v.addProperty("z", p.getZ());
            v.addProperty("yaw", -Math.toRadians(p.getYaw()));
            v.addProperty("pitch", -Math.toRadians(p.getPitch()));
            v.addProperty("hp", p.getHealth());
            v.addProperty("max", p.getMaxHealth());
            v.addProperty("swing", swings.getOrDefault(p.getUuid(), 0));
            v.addProperty("held", net.minecraft.registry.Registries.ITEM.getId(p.getMainHandStack().getItem()).toString());
            Bridge.send(v);
        }

        // Echoes UNRENDERED has stopped mentioning are gone.
        for (Iterator<Map.Entry<Integer, EchoEntity>> it = echoes.entrySet().iterator(); it.hasNext(); ) {
            EchoEntity e = it.next().getValue();
            if (e.isRemoved() || ++e.unseen > 40) {
                e.discard();
                it.remove();
            }
        }
    }

    // --- From UNRENDERED ----------------------------------------------------------------

    /** UNRENDERED's frame for one visitor: forwarded, still compressed, to that player's client. */
    private static int scenesIn;

    public static void onScene(MinecraftServer server, JsonObject m, byte[] packed) {
        scenesIn++;
        if (packed == null || packed.length > 1_000_000) {
            if (scenesIn < 200 && scenesIn % 20 == 1)
                RiftMod.LOG.warn("Rift: scene {} from UNRENDERED dropped, {} bytes packed (a payload holds 1 MB)", scenesIn, packed == null ? -1 : packed.length);
            return;
        }
        if (scenesIn % 60 == 1) RiftMod.LOG.info("Rift: scene {} from UNRENDERED, {} bytes packed", scenesIn, packed.length);
        UUID id;
        try {
            id = UUID.fromString(Session.str(m, "uuid"));
        } catch (IllegalArgumentException e) {
            return;
        }
        ServerPlayerEntity p = server.getPlayerManager().getPlayer(id);
        if (p == null || p.getServerWorld().getRegistryKey() != RiftMod.RIFT) return;
        ServerPlayNetworking.send(p, new ScenePayload(packed));
    }

    /** UNRENDERED's hostiles near the visitors: keep a hitbox on each. */
    public static void onField(MinecraftServer server, JsonObject m) {
        ServerWorld rift = server.getWorld(RiftMod.RIFT);
        if (rift == null || !m.has("ents")) return;
        List<ServerPlayerEntity> players = rift.getPlayers();
        for (JsonElement el : m.getAsJsonArray("ents")) {
            JsonObject o = el.getAsJsonObject();
            int id = o.get("id").getAsInt();
            double x = o.get("x").getAsDouble(), y = o.has("y") ? o.get("y").getAsDouble() : 0.0, z = o.get("z").getAsDouble();
            float r = o.has("r") ? o.get("r").getAsFloat() : 1.0f;
            float h = o.has("h") ? o.get("h").getAsFloat() : 2.0f;
            // The torus: put it at the copy nearest a visitor.
            ServerPlayerEntity near = null;
            double best = Double.MAX_VALUE;
            for (ServerPlayerEntity p : players) {
                double d = wrapDelta(x, p.getX()) * wrapDelta(x, p.getX()) + wrapDelta(z, p.getZ()) * wrapDelta(z, p.getZ());
                if (d < best) {
                    best = d;
                    near = p;
                }
            }
            if (near != null) {
                x = near.getX() - wrapDelta(x, near.getX());
                z = near.getZ() - wrapDelta(z, near.getZ());
            }
            EchoEntity echo = echoes.get(id);
            if (echo == null || echo.isRemoved()) {
                echo = RiftEntities.ECHO.create(rift);
                if (echo == null) continue;
                echo.unrId = id;
                echo.refreshPositionAndAngles(x, RiftMod.FLOOR_Y + y, z, 0f, 0f);
                echo.setBox(2 * r, h);
                rift.spawnEntity(echo);
                echoes.put(id, echo);
            }
            echo.unseen = 0;
            echo.setBox(2 * r, h);
            echo.updatePosition(x, RiftMod.FLOOR_Y + y, z);
            if (o.has("hp") && o.has("max")) {
                float max = Math.max(0.01f, o.get("max").getAsFloat());
                echo.setHealth(Math.max(0.5f, 20f * o.get("hp").getAsFloat() / max));
            }
        }
    }

    /** Shortest signed distance from {@code a} to {@code b} across the torus seam. */
    private static double wrapDelta(double a, double b) {
        double d = b - a;
        return d - RiftMod.TORUS * Math.floor(d / RiftMod.TORUS + 0.5);
    }

    /** A Minecraft weapon (any of them) landed on an UNRENDERED hostile. */
    public static void onStrike(EchoEntity echo, ServerPlayerEntity player, float amount) {
        JsonObject s = Bridge.msg("strike");
        s.addProperty("id", echo.unrId);
        s.addProperty("dmg", amount);
        s.addProperty("uuid", player.getUuidAsString());
        Bridge.send(s);
    }

    /** UNRENDERED hit a visitor. */
    public static void onHarm(MinecraftServer server, JsonObject m) {
        ServerPlayerEntity p = player(server, m);
        // Only somebody actually standing in UNRENDERED's world can be hurt by it: not a visitor
        // who died and respawned at home while their seat there was still being shot at.
        if (p == null || !p.isAlive() || p.getServerWorld().getRegistryKey() != RiftMod.RIFT) return;
        float amount = m.has("amount") ? m.get("amount").getAsFloat() : 0f;
        if (amount > 0f) p.damage(RiftDamage.source(p.getServerWorld()), amount);
        // The world they were standing in ended (a run over, a match, no seat left): home.
        if (m.has("eject") && m.get("eject").getAsBoolean() && p.getServerWorld().getRegistryKey() == RiftMod.RIFT)
            sendHome(p);
    }

    /** A visitor collected UNRENDERED salvage. */
    public static void onGive(MinecraftServer server, JsonObject m) {
        ServerPlayerEntity p = player(server, m);
        if (p == null) return;
        Item item = RiftItems.fromUnrendered(Session.str(m, "item"));
        if (item == null) return;
        // As many stacks as it takes (a whole inventory's worth at most): a large give is not
        // one oversized stack and is not cut short.
        int count = Math.min(m.has("count") ? Math.max(1, m.get("count").getAsInt()) : 1, 64 * 36);
        int max = Math.max(1, item.getMaxCount());
        for (int left = count; left > 0; left -= max) p.getInventory().offerOrDrop(new ItemStack(item, Math.min(max, left)));
        p.getServerWorld().playSound(null, p.getX(), p.getY(), p.getZ(), SoundEvents.ENTITY_ITEM_PICKUP,
            SoundCategory.PLAYERS, 0.3f, 1.4f);
    }

    /** Whoever is breaking a block right now (set around a player's break), for `mine`. */
    public static PlayerEntity breaker;

    /**
     * A piece of the city was broken: by a visitor's pickaxe, an explosion, anything. It is
     * reported to UNRENDERED, which cuts the real building there, and it drops what
     * UNRENDERED's buildings are made of.
     */
    public static void onCityBroken(World world, BlockPos pos) {
        JsonObject mine = Bridge.msg("mine");
        PlayerEntity by = breaker;
        mine.addProperty("uuid", by != null ? by.getUuidAsString() : "");
        mine.addProperty("x", pos.getX() + 0.5);
        mine.addProperty("y", pos.getY() - RiftMod.FLOOR_Y + 0.5);
        mine.addProperty("z", pos.getZ() + 0.5);
        Bridge.send(mine);

        if (world.random.nextFloat() < (by != null ? 0.85f : 0.4f)) {
            float roll = world.random.nextFloat();
            Item loot = roll < 0.7f ? RiftItems.SCRAP_METAL : roll < 0.88f ? RiftItems.DENSE_ALLOY : RiftItems.COPPER_WIRE;
            net.minecraft.block.Block.dropStack(world, pos, new ItemStack(loot));
        }
    }

    private static ServerPlayerEntity player(MinecraftServer server, JsonObject m) {
        try {
            return server.getPlayerManager().getPlayer(UUID.fromString(Session.str(m, "uuid")));
        } catch (IllegalArgumentException e) {
            return null;
        }
    }
}
