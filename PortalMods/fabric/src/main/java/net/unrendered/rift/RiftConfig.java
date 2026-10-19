package net.unrendered.rift;

import net.fabricmc.loader.api.FabricLoader;

import java.io.IOException;
import java.io.Reader;
import java.io.Writer;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Properties;

/** config/unrendered_portal.properties. Written with its defaults the first time. */
public final class RiftConfig {
    /** TCP port the bridge listens on. 0 means "25570, or the next free one up to 25579". */
    public static int bridgePort = 0;
    /** Whether an UNRENDERED pilot's rounds break the blocks they hit. */
    public static boolean allowBlockBreak = true;
    /** How far from the portal, in blocks, the terrain sent to a pilot reaches. */
    public static int regionRadius = 48;
    /** How far below and above the portal's floor the terrain reaches. */
    public static int regionBelow = 24;
    public static int regionAbove = 40;
    /** How many chunks a tick are streamed to a pilot (each is a full column). */
    public static int chunksPerTick = 3;
    /** Whether to announce the world on the LAN for UNRENDERED to find. */
    public static boolean beacon = true;

    private RiftConfig() {}

    public static void load() {
        Path file = FabricLoader.getInstance().getConfigDir().resolve("unrendered_portal.properties");
        Properties p = new Properties();
        if (Files.exists(file)) {
            try (Reader r = Files.newBufferedReader(file)) {
                p.load(r);
            } catch (IOException e) {
                RiftMod.LOG.warn("Could not read {}: {}", file, e.toString());
            }
        }
        bridgePort = num(p, "bridgePort", bridgePort, 0, 65535);
        allowBlockBreak = Boolean.parseBoolean(p.getProperty("allowBlockBreak", Boolean.toString(allowBlockBreak)));
        regionRadius = num(p, "regionRadius", regionRadius, 8, 96);
        chunksPerTick = num(p, "chunksPerTick", chunksPerTick, 1, 16);
        regionBelow = num(p, "regionBelow", regionBelow, 4, 64);
        regionAbove = num(p, "regionAbove", regionAbove, 8, 96);
        beacon = Boolean.parseBoolean(p.getProperty("beacon", Boolean.toString(beacon)));

        if (!Files.exists(file)) {
            p.setProperty("bridgePort", Integer.toString(bridgePort));
            p.setProperty("allowBlockBreak", Boolean.toString(allowBlockBreak));
            p.setProperty("regionRadius", Integer.toString(regionRadius));
            p.setProperty("chunksPerTick", Integer.toString(chunksPerTick));
            p.setProperty("regionBelow", Integer.toString(regionBelow));
            p.setProperty("regionAbove", Integer.toString(regionAbove));
            p.setProperty("beacon", Boolean.toString(beacon));
            try (Writer w = Files.newBufferedWriter(file)) {
                p.store(w, "THE RIFT - the bridge between Minecraft and UNRENDERED");
            } catch (IOException e) {
                RiftMod.LOG.warn("Could not write {}: {}", file, e.toString());
            }
        }
    }

    private static int num(Properties p, String key, int def, int min, int max) {
        try {
            int v = Integer.parseInt(p.getProperty(key, Integer.toString(def)).trim());
            return Math.max(min, Math.min(max, v));
        } catch (NumberFormatException e) {
            return def;
        }
    }
}
