package net.unrendered.rift;

import com.google.gson.Gson;
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

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * UNRENDERED's skyline, as something a Minecraft player bumps into. Every leg of every
 * building is a column of invisible solid blocks as tall as the real thing. You can't
 * walk through a tower, you can stand on its roof, and you can dig into it. You don't
 * see any of this; you see UNRENDERED's own towers, streamed in.
 */
public final class RiftCity {
    private static final Gson GSON = new Gson();

    /** A structure's collision: its columns and height. */
    private record Building(int index, int height, List<long[]> columns) {}

    private static String builtHash = "";
    private static final Map<Integer, Building> buildings = new HashMap<>();
    /** Pending block writes: {x, y, z, solid?1:0}. */
    private static final ArrayDeque<int[]> queue = new ArrayDeque<>();
    private static boolean dirty;

    private RiftCity() {}

    private static Path file(MinecraftServer server) {
        return server.getSavePath(WorldSavePath.ROOT).resolve("unrendered_portal_city.json");
    }

    public static void load(MinecraftServer server) {
        buildings.clear();
        queue.clear();
        builtHash = "";
        Path f = file(server);
        if (!Files.exists(f)) return;
        try {
            JsonObject o = JsonParser.parseString(Files.readString(f)).getAsJsonObject();
            builtHash = o.get("hash").getAsString();
            for (JsonElement e : o.getAsJsonArray("structures")) readBuilding(e.getAsJsonObject());
        } catch (IOException | RuntimeException e) {
            RiftMod.LOG.warn("Rift: could not read the saved city: {}", e.toString());
        }
    }

    private static void save(MinecraftServer server) {
        JsonObject o = new JsonObject();
        o.addProperty("hash", builtHash);
        JsonArray arr = new JsonArray();
        for (Building b : buildings.values()) {
            JsonObject s = new JsonObject();
            s.addProperty("i", b.index());
            s.addProperty("h", b.height());
            JsonArray cols = new JsonArray();
            for (long[] c : b.columns()) {
                JsonArray xz = new JsonArray();
                xz.add(c[0]);
                xz.add(c[1]);
                cols.add(xz);
            }
            s.add("cols", cols);
            arr.add(s);
        }
        o.add("structures", arr);
        try {
            Files.writeString(file(server), GSON.toJson(o));
        } catch (IOException e) {
            RiftMod.LOG.warn("Rift: could not save the city: {}", e.toString());
        }
    }

    private static void readBuilding(JsonObject s) {
        int i = s.get("i").getAsInt();
        int h = s.get("h").getAsInt();
        List<long[]> cols = new ArrayList<>();
        for (JsonElement c : s.getAsJsonArray("cols")) {
            JsonArray xz = c.getAsJsonArray();
            cols.add(new long[]{xz.get(0).getAsLong(), xz.get(1).getAsLong()});
        }
        buildings.put(i, new Building(i, h, cols));
    }

    private static int wrap(double v) {
        double w = v - RiftMod.TORUS * Math.floor(v / RiftMod.TORUS + 0.5);
        return (int) Math.floor(w);
    }

    /** UNRENDERED's skyline arrived. Rebuild the collision if it isn't the one already standing. */
    public static void onCity(MinecraftServer server, JsonObject m) {
        String hash = m.has("hash") ? m.get("hash").getAsString() : "";
        if (!hash.isEmpty() && hash.equals(builtHash) && !buildings.isEmpty()) return;

        // Take the old skyline down first.
        for (Building b : buildings.values()) enqueue(b, false);
        buildings.clear();

        for (JsonElement e : m.getAsJsonArray("structures")) {
            JsonObject s = e.getAsJsonObject();
            int index = s.get("i").getAsInt();
            int height = (int) Math.ceil(s.get("h").getAsDouble());
            height = Math.max(1, Math.min(height, 180));
            List<long[]> cols = new ArrayList<>();
            java.util.Set<Long> seen = new java.util.HashSet<>();
            for (JsonElement l : s.getAsJsonArray("legs")) {
                JsonArray leg = l.getAsJsonArray();
                double cx = leg.get(0).getAsDouble(), cz = leg.get(1).getAsDouble(), r = leg.get(2).getAsDouble();
                int ri = (int) Math.ceil(r);
                for (int dx = -ri; dx <= ri; dx++) {
                    for (int dz = -ri; dz <= ri; dz++) {
                        double px = Math.floor(cx) + dx + 0.5, pz = Math.floor(cz) + dz + 0.5;
                        if ((px - cx) * (px - cx) + (pz - cz) * (pz - cz) > r * r) continue;
                        int x = wrap(px), z = wrap(pz);
                        long key = ((long) x << 32) ^ (z & 0xFFFFFFFFL);
                        if (seen.add(key)) cols.add(new long[]{x, z});
                    }
                }
            }
            Building b = new Building(index, height, cols);
            buildings.put(index, b);
            enqueue(b, true);
        }
        builtHash = hash;
        dirty = true;
        RiftMod.LOG.info("Rift: raising UNRENDERED's skyline ({} structures, {} blocks queued)", buildings.size(), queue.size());
    }

    /** Structure {@code i} came down in UNRENDERED, so its collision goes. */
    public static void onFell(MinecraftServer server, JsonObject m) {
        int i = m.get("i").getAsInt();
        Building b = buildings.remove(i);
        if (b != null) {
            enqueue(b, false);
            dirty = true;
        }
    }

    private static void enqueue(Building b, boolean solid) {
        for (long[] c : b.columns()) {
            for (int y = 0; y < b.height(); y++) {
                queue.add(new int[]{(int) c[0], RiftMod.FLOOR_Y + y, (int) c[1], solid ? 1 : 0});
            }
        }
    }

    public static void tick(MinecraftServer server) {
        if (queue.isEmpty()) {
            if (dirty) {
                dirty = false;
                save(server);
            }
            return;
        }
        ServerWorld rift = server.getWorld(RiftMod.RIFT);
        if (rift == null) {
            queue.clear();
            return;
        }
        BlockState solid = RiftBlocks.RIFT_SOLID.getDefaultState();
        BlockState air = Blocks.AIR.getDefaultState();
        BlockPos.Mutable pos = new BlockPos.Mutable();
        for (int budget = 6000; budget > 0 && !queue.isEmpty(); budget--) {
            int[] op = queue.poll();
            pos.set(op[0], op[1], op[2]);
            BlockState now = rift.getBlockState(pos);
            if (op[3] == 1) {
                // Never bury what a player built there.
                if (now.isAir()) rift.setBlockState(pos, solid, 2 | 16);
            } else if (now.isOf(RiftBlocks.RIFT_SOLID)) {
                rift.setBlockState(pos, air, 2 | 16);
            }
        }
    }

    /** Which structure a block of the city belongs to (for `mine`), or -1. */
    public static int structureAt(int x, int z) {
        for (Building b : buildings.values()) {
            for (long[] c : b.columns()) if (c[0] == x && c[1] == z) return b.index();
        }
        return -1;
    }
}
