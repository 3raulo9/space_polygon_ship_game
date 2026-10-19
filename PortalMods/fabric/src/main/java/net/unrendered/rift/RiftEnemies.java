package net.unrendered.rift;

import com.google.gson.JsonObject;
import net.fabricmc.fabric.api.networking.v1.ServerPlayNetworking;
import net.minecraft.entity.SpawnReason;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Vec3d;
import net.unrendered.rift.bridge.Bridge;
import net.unrendered.rift.bridge.Session;
import net.unrendered.rift.entity.UnrEnemyEntity;
import net.unrendered.rift.net.ModelPayload;

import java.util.HashMap;
import java.util.Map;

/**
 * UNRENDERED's enemies on this side of the portal. They arrive with `cross`, are drawn with
 * the `model` UNRENDERED sent for their kind, and go home with `return`.
 */
public final class RiftEnemies {
    /** Each kind's body, still deflated, ready to forward to clients. */
    private static final Map<String, byte[]> models = new HashMap<>();

    private RiftEnemies() {}

    public static void onModel(MinecraftServer server, JsonObject m, byte[] packed) {
        String kind = Session.str(m, "kind");
        if (kind.isEmpty() || packed == null || packed.length > 1_000_000) return;
        models.put(kind, packed);
        ModelPayload payload = new ModelPayload(kind, packed);
        for (ServerPlayerEntity p : server.getPlayerManager().getPlayerList()) ServerPlayNetworking.send(p, payload);
    }

    /** A player joined: they need every body already known. */
    public static void onJoin(ServerPlayerEntity player) {
        for (Map.Entry<String, byte[]> e : models.entrySet())
            ServerPlayNetworking.send(player, new ModelPayload(e.getKey(), e.getValue()));
    }

    /** An enemy walked into UNRENDERED's portal. It comes out of this one. */
    public static void onCross(MinecraftServer server, JsonObject m) {
        String kind = Session.str(m, "kind");
        float hp = m.has("hp") ? m.get("hp").getAsFloat() : 3f;
        float max = m.has("max") ? m.get("max").getAsFloat() : 3f;
        if (!kind.equals("hunter") && !kind.equals("elite") && !kind.equals("soldier")) kind = "hunter";
        RiftPortals.Spot spot = RiftPortals.lastPortalStands(server) ? RiftPortals.lastPortal : null;
        ServerWorld w = spot == null ? null : server.getWorld(spot.dim());
        if (w == null) {
            // Nowhere to come out: it stays where it is.
            sendBack(kind, hp, max);
            return;
        }
        Vec3d at = RiftPortals.exitBeside(w, spot, 2.4);
        UnrEnemyEntity e = RiftEntities.UNR_ENEMY.create(w);
        if (e == null) {
            sendBack(kind, hp, max);
            return;
        }
        e.refreshPositionAndAngles(at.x, at.y, at.z, w.random.nextFloat() * 360f, 0f);
        e.initialize(w, w.getLocalDifficulty(BlockPos.ofFloored(at)), SpawnReason.DIMENSION_TRAVEL, null);
        e.setup(kind, hp, max);
        e.setPersistent();
        e.setPortalCooldown(80);
        w.spawnEntity(e);
        RiftMod.LOG.info("Rift: an UNRENDERED {} came through", kind);
    }

    /** Back through a portal: UNRENDERED takes it, with what it has left. */
    public static void sendBack(UnrEnemyEntity e) {
        sendBack(e.kind(), e.unrHp(), e.unrMax);
        e.discard();
    }

    private static void sendBack(String kind, float hp, float max) {
        JsonObject r = Bridge.msg("return");
        r.addProperty("kind", kind);
        r.addProperty("hp", hp);
        r.addProperty("max", max);
        Bridge.send(r);
    }

    public static void clear() {
        models.clear();
    }
}
