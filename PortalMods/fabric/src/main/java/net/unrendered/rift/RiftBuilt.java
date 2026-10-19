package net.unrendered.rift;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import net.minecraft.block.BlockState;
import net.minecraft.block.Blocks;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.util.WorldSavePath;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Direction;
import net.minecraft.world.World;
import net.unrendered.rift.bridge.Bridge;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Collections;
import java.util.IdentityHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * What Minecraft players build inside UNRENDERED. A block placed in the rift dimension is a
 * real Minecraft block, and UNRENDERED is told about it so it stands in UNRENDERED's world
 * too, looking the way Minecraft draws it and solid to UNRENDERED's crafts. The rift's own
 * collision (the grid, the city) and its portal are not building and are never sent.
 */
public final class RiftBuilt {
    /** Every position a player has built at, kept across restarts. */
    private static final Set<BlockPos> built = Collections.synchronizedSet(new LinkedHashSet<>());
    private static final Set<BlockPos> changed = Collections.synchronizedSet(new LinkedHashSet<>());
    private static final List<BlockState> palette = new ArrayList<>();
    private static final Map<BlockState, Integer> index = new IdentityHashMap<>();
    /** Palette entries UNRENDERED already has. */
    private static int paletteSent;
    private static boolean dirty;
    private static int tick;

    private static final int MAX = 50_000;

    private RiftBuilt() {}

    private static Path file(MinecraftServer server) {
        return server.getSavePath(WorldSavePath.ROOT).resolve("unrendered_portal_built.json");
    }

    public static void load(MinecraftServer server) {
        built.clear();
        changed.clear();
        resetPalette();
        Path f = file(server);
        if (!Files.exists(f)) return;
        try {
            for (JsonElement e : JsonParser.parseString(Files.readString(f)).getAsJsonArray()) {
                JsonArray a = e.getAsJsonArray();
                built.add(new BlockPos(a.get(0).getAsInt(), a.get(1).getAsInt(), a.get(2).getAsInt()));
            }
        } catch (IOException | RuntimeException e) {
            RiftMod.LOG.warn("Rift: could not read what was built in the rift: {}", e.toString());
        }
    }

    private static void save(MinecraftServer server) {
        JsonArray arr = new JsonArray();
        synchronized (built) {
            for (BlockPos p : built) {
                JsonArray a = new JsonArray();
                a.add(p.getX());
                a.add(p.getY());
                a.add(p.getZ());
                arr.add(a);
            }
        }
        try {
            Files.writeString(file(server), arr.toString());
        } catch (IOException e) {
            RiftMod.LOG.warn("Rift: could not save what was built in the rift: {}", e.toString());
        }
    }

    /** Index 0 is always air: "nothing here any more". */
    private static void resetPalette() {
        palette.clear();
        index.clear();
        palette.add(Blocks.AIR.getDefaultState());
        index.put(Blocks.AIR.getDefaultState(), 0);
        paletteSent = 0;
    }

    /** Called by the world mixin for every block change on the server. */
    public static void onBlockChanged(World w, BlockPos pos) {
        if (w.isClient || w.getRegistryKey() != RiftMod.RIFT) return;
        changed.add(pos.toImmutable());
    }

    /** The rift's own blocks are not building: the grid, the city, the portal and its frame. */
    private static boolean isBuilding(BlockState state, BlockPos pos) {
        if (state.isAir()) return false;
        if (state.isOf(RiftBlocks.RIFT_FLOOR) || state.isOf(RiftBlocks.RIFT_SOLID) || state.isOf(RiftBlocks.RIFT_PORTAL)) return false;
        RiftPortals.Spot portal = RiftPortals.riftPortal;
        if (portal != null && state.isOf(Blocks.GLOWSTONE) && inFrame(portal, pos)) return false;
        return true;
    }

    private static boolean inFrame(RiftPortals.Spot s, BlockPos p) {
        Direction right = s.axis() == Direction.Axis.X ? Direction.EAST : Direction.SOUTH;
        BlockPos bl = s.bottomLeft();
        int along = right == Direction.EAST ? p.getX() - bl.getX() : p.getZ() - bl.getZ();
        int across = right == Direction.EAST ? p.getZ() - bl.getZ() : p.getX() - bl.getX();
        int up = p.getY() - bl.getY();
        return across == 0 && along >= -1 && along <= s.width() && up >= -1 && up <= s.height();
    }

    private static int indexOf(ServerWorld w, BlockState state, BlockPos pos) {
        Integer i = index.get(state);
        if (i != null) return i;
        int idx = palette.size();
        palette.add(state);
        index.put(state, idx);
        return idx;
    }

    /** New palette entries since the last message, in the pilot palette's format. */
    private static JsonArray freshPalette(ServerWorld w, BlockPos near) {
        JsonArray out = new JsonArray();
        for (int i = paletteSent; i < palette.size(); i++) out.add(RiftPilot.entry(w, palette.get(i), near, i));
        paletteSent = palette.size();
        return out;
    }

    private static JsonArray row(BlockPos p, int idx) {
        JsonArray a = new JsonArray();
        a.add(p.getX());
        a.add(p.getY() - RiftMod.FLOOR_Y);
        a.add(p.getZ());
        a.add(idx);
        return a;
    }

    /** Everything built so far, with the whole palette, for UNRENDERED or a new visitor. */
    public static void sendAll(MinecraftServer server) {
        ServerWorld rift = server.getWorld(RiftMod.RIFT);
        if (rift == null || !Bridge.linked()) return;
        resetPalette();
        JsonArray set = new JsonArray();
        List<BlockPos> gone = new ArrayList<>();
        BlockPos first = null;
        synchronized (built) {
            for (BlockPos p : built) {
                BlockState state = rift.getBlockState(p);
                if (!isBuilding(state, p)) {
                    gone.add(p);
                    continue;
                }
                if (first == null) first = p;
                set.add(row(p, indexOf(rift, state, p)));
            }
        }
        if (!gone.isEmpty()) {
            built.removeAll(gone);
            dirty = true;
        }
        JsonObject b = Bridge.msg("built");
        b.add("palette", freshPalette(rift, first != null ? first : new BlockPos(0, RiftMod.FLOOR_Y, 0)));
        b.add("set", set);
        b.addProperty("full", true);
        Bridge.send(b);
        RiftMod.LOG.info("Rift: sent UNRENDERED {} built blocks", set.size());
    }

    public static void tick(MinecraftServer server) {
        tick++;
        if (dirty && tick % 200 == 0) {
            dirty = false;
            save(server);
        }
        if (changed.isEmpty() || tick % 2 != 0) return;
        ServerWorld rift = server.getWorld(RiftMod.RIFT);
        if (rift == null) {
            changed.clear();
            return;
        }
        List<BlockPos> batch = new ArrayList<>();
        synchronized (changed) {
            var it = changed.iterator();
            while (it.hasNext() && batch.size() < 2048) {
                batch.add(it.next());
                it.remove();
            }
        }
        JsonArray set = new JsonArray();
        BlockPos near = null;
        for (BlockPos p : batch) {
            BlockState state = rift.getBlockState(p);
            if (isBuilding(state, p)) {
                if (built.size() >= MAX && !built.contains(p)) continue;
                built.add(p);
                set.add(row(p, indexOf(rift, state, p)));
                if (near == null) near = p;
            } else if (built.remove(p)) {
                set.add(row(p, 0));
            }
        }
        if (set.isEmpty()) return;
        dirty = true;
        if (!Bridge.linked()) return;
        JsonObject b = Bridge.msg("built");
        b.add("palette", freshPalette(rift, near != null ? near : batch.get(0)));
        b.add("set", set);
        Bridge.send(b);
    }
}
