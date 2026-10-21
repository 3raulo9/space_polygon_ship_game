package net.unrendered.rift;

import net.minecraft.server.MinecraftServer;
import net.minecraft.server.integrated.IntegratedServer;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.GameMode;
import net.unrendered.rift.bridge.Bridge;
import net.unrendered.rift.bridge.Session;

import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.function.BiConsumer;
import java.util.function.BooleanSupplier;
import java.util.function.Supplier;

/**
 * UNRENDERED as a realm on the Dimension Hopper's dial, when that mod is installed too (it
 * reads the {@code dimension_hopper:realms} entrypoint; nothing here needs it to compile).
 * The connection is the rift's own: clear while UNRENDERED is linked, established by opening
 * the world to LAN so UNRENDERED can hear it, and walking into a hopper portal aimed at it
 * is the same arrival as stepping through the glowstone.
 */
public final class HopperRealms implements Supplier<List<Map<String, Object>>> {
    @Override
    public List<Map<String, Object>> get() {
        Map<String, Object> r = new HashMap<>();
        Session s = Bridge.session();
        r.put("id", "unrendered");
        r.put("name", s != null ? "UNRENDERED - " + s.pilotName : "UNRENDERED");
        r.put("game", "UNRENDERED");
        r.put("dimension", RiftMod.RIFT);
        r.put("clear", (BooleanSupplier) Bridge::linked);
        r.put("status", (Supplier<String>) HopperRealms::status);
        r.put("connect", (Runnable) HopperRealms::connect);
        r.put("enter", (BiConsumer<ServerPlayerEntity, Vec3d>) (player, at) ->
            RiftVisitors.requestArrival(player, player.getServerWorld().getRegistryKey(), at, player.getYaw()));
        return List.of(r);
    }

    private static String status() {
        Session s = Bridge.session();
        if (s != null) return "Connection clear: " + s.pilotName;
        if (Bridge.running()) return "Listening across the rift... answer it from UNRENDERED (press B)";
        return "No signal - open the world to LAN";
    }

    /** The bridge runs while the world is open to LAN; a single-player world is opened. */
    private static void connect() {
        MinecraftServer server = RiftMod.server;
        // A dedicated server always runs the bridge; only a single-player world needs opening.
        if (server == null || Bridge.running() || server.isDedicated()) return;
        if (server instanceof IntegratedServer integrated && !integrated.isRemote()) {
            GameMode mode = server.getSaveProperties().getGameMode();
            boolean cheats = server.getPlayerManager().areCheatsAllowed();
            for (int port = 25565; port < 25575; port++) {
                if (integrated.openToLan(mode, cheats, port)) {
                    RiftMod.LOG.info("Rift: opened to LAN on {} so UNRENDERED can find this world", port);
                    return;
                }
            }
        }
    }
}
