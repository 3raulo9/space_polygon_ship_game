package net.unrendered.rift;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.io.InputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Map;

/**
 * UNRENDERED's items as UNRENDERED models them (assets/unrendered_portal/unrendered/items.json,
 * written out of the game itself by PortalMods/tools/export-items.sh): each kind's own
 * polygons and colours, how its pack icon frames it, how many stack, and the thrown CRAB
 * CORE — its slug in flight and the numbers of its blast. Nothing here is made up on this
 * side; it is all read off UNRENDERED.
 */
public final class UnrModels {
    /** One mesh: triangles as nine floats each, one RGBA colour per triangle. */
    public record Mesh(float[] v, int[] c, float scale, float centerY) {}

    private static final Map<String, Mesh> ITEMS = new HashMap<>();
    private static final Map<String, Integer> STACKS = new HashMap<>();
    private static Mesh slug;
    private static float[] light = {-0.358f, -0.894f, -0.268f};

    // The throw and the blast, as UNRENDERED has them (units are metres there, blocks here).
    public static float throwSpeed = 60f, throwLife = 0.9f, throwHeight = 0.7f;
    public static float blastLife = 3f, blastLength = 46f, blastCoreHeight = 5f, blastRadius = 24f;
    public static float blastTick = 0.35f, blastDamage = 9f;
    public static int blastBeams = 32, blastRed = 0xFF2828FF, blastMagenta = 0xE63CC8FF;

    private static boolean loaded;

    private UnrModels() {}

    public static synchronized void load() {
        if (loaded) return;
        loaded = true;
        try (InputStream in = UnrModels.class.getResourceAsStream("/assets/unrendered_portal/unrendered/items.json")) {
            if (in == null) {
                RiftMod.LOG.warn("Rift: UNRENDERED's item models are missing from the jar");
                return;
            }
            JsonObject root = JsonParser.parseReader(new InputStreamReader(in, StandardCharsets.UTF_8)).getAsJsonObject();
            JsonArray l = root.getAsJsonArray("light");
            light = new float[]{l.get(0).getAsFloat(), l.get(1).getAsFloat(), l.get(2).getAsFloat()};
            for (Map.Entry<String, com.google.gson.JsonElement> e : root.getAsJsonObject("items").entrySet()) {
                JsonObject o = e.getValue().getAsJsonObject();
                ITEMS.put(e.getKey(), mesh(o, o.get("scale").getAsFloat(), o.get("centerY").getAsFloat()));
                STACKS.put(e.getKey(), o.get("stack").getAsInt());
            }
            slug = mesh(root.getAsJsonObject("slug"), 1f, 0f);
            JsonObject t = root.getAsJsonObject("throw");
            throwSpeed = t.get("speed").getAsFloat();
            throwLife = t.get("life").getAsFloat();
            throwHeight = t.get("height").getAsFloat();
            JsonObject b = root.getAsJsonObject("blast");
            blastLife = b.get("life").getAsFloat();
            blastBeams = b.get("beams").getAsInt();
            blastLength = b.get("length").getAsFloat();
            blastCoreHeight = b.get("coreHeight").getAsFloat();
            blastRadius = b.get("radius").getAsFloat();
            blastTick = b.get("tick").getAsFloat();
            blastDamage = b.get("damage").getAsFloat();
            blastRed = b.get("red").getAsInt();
            blastMagenta = b.get("magenta").getAsInt();
        } catch (Exception e) {
            RiftMod.LOG.warn("Rift: could not read UNRENDERED's item models: {}", e.toString());
        }
    }

    private static Mesh mesh(JsonObject o, float scale, float centerY) {
        JsonArray v = o.getAsJsonArray("v"), c = o.getAsJsonArray("c");
        float[] vs = new float[v.size()];
        for (int i = 0; i < vs.length; i++) vs[i] = v.get(i).getAsFloat();
        int[] cs = new int[c.size()];
        for (int i = 0; i < cs.length; i++) cs[i] = c.get(i).getAsInt();
        return new Mesh(vs, cs, scale, centerY);
    }

    public static Mesh item(String id) {
        load();
        return ITEMS.get(id);
    }

    public static Mesh slug() {
        load();
        return slug;
    }

    public static float[] light() {
        load();
        return light;
    }

    /** How many of it UNRENDERED's pack stacks, or {@code fallback}. */
    public static int stack(String id, int fallback) {
        load();
        return STACKS.getOrDefault(id, fallback);
    }
}
