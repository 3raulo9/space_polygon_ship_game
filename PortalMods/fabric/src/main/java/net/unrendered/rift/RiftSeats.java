package net.unrendered.rift;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import net.minecraft.entity.Entity;
import net.minecraft.entity.damage.DamageSource;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.text.Text;
import net.minecraft.util.Formatting;
import net.minecraft.util.math.Vec3d;
import net.unrendered.rift.bridge.Bridge;
import net.unrendered.rift.bridge.Session;
import net.unrendered.rift.entity.SeatEchoEntity;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * UNRENDERED's own players, standing in their world, which is the rift dimension here. Each
 * one has an invisible stand-in so the rift's Minecraft side can reach them: monsters that
 * walked through the portal go for them, and a visitor can hit them.
 */
public final class RiftSeats {
    private static final Map<Integer, SeatEchoEntity> seats = new HashMap<>();

    private RiftSeats() {}

    public static boolean any() {
        return !seats.isEmpty();
    }

    /** Where every seat stands, in the rift's Minecraft coordinates. */
    public static List<Vec3d> positions() {
        List<Vec3d> out = new ArrayList<>();
        for (SeatEchoEntity e : seats.values()) if (!e.isRemoved()) out.add(e.getPos());
        return out;
    }

    /** `seats`: the full list, every 20th of a second. */
    public static void onSeats(MinecraftServer server, JsonObject m) {
        ServerWorld rift = server.getWorld(RiftMod.RIFT);
        if (rift == null || !m.has("list")) return;
        Set<Integer> listed = new HashSet<>();
        for (JsonElement el : m.getAsJsonArray("list")) {
            JsonObject o = el.getAsJsonObject();
            int seat = o.get("seat").getAsInt();
            listed.add(seat);
            double x = o.get("x").getAsDouble(), y = o.has("y") ? o.get("y").getAsDouble() : 0.0, z = o.get("z").getAsDouble();
            float r = o.has("r") ? o.get("r").getAsFloat() : 0.4f;
            float h = o.has("h") ? o.get("h").getAsFloat() : 1.9f;
            float yaw = o.has("yaw") ? (float) -Math.toDegrees(o.get("yaw").getAsDouble()) : 0f;
            SeatEchoEntity e = seats.get(seat);
            if (e == null || e.isRemoved()) {
                RiftMod.hold(rift, new Vec3d(x, RiftMod.FLOOR_Y + y, z));
                rift.getChunk(net.minecraft.util.math.BlockPos.ofFloored(x, RiftMod.FLOOR_Y + y, z));
                e = RiftEntities.SEAT.create(rift);
                if (e == null) continue;
                e.seat = seat;
                e.refreshPositionAndAngles(x, RiftMod.FLOOR_Y + y, z, yaw, 0f);
                String name = Session.str(o, "name");
                if (!name.isEmpty()) e.setCustomName(Text.literal(name).formatted(Formatting.LIGHT_PURPLE));
                rift.spawnEntity(e);
                seats.put(seat, e);
                RiftMod.LOG.info("Rift: a stand-in for UNRENDERED seat {} in the rift", seat);
            }
            e.unseen = 0;
            e.setBox(2 * r, h);
            e.updatePositionAndAngles(x, RiftMod.FLOOR_Y + y, z, yaw, 0f);
            e.setHeadYaw(yaw);
            e.setBodyYaw(yaw);
            if (o.has("hp") && o.has("max")) {
                float max = Math.max(0.01f, o.get("max").getAsFloat());
                e.setHealth(Math.max(0.5f, Math.min(20f, 20f * o.get("hp").getAsFloat() / max)));
            }
        }
        for (Iterator<Map.Entry<Integer, SeatEchoEntity>> it = seats.entrySet().iterator(); it.hasNext(); ) {
            Map.Entry<Integer, SeatEchoEntity> en = it.next();
            if (listed.contains(en.getKey())) continue;
            en.getValue().discard();
            it.remove();
        }
    }

    /** Something in the rift hit an UNRENDERED player. */
    public static void onHurt(SeatEchoEntity e, DamageSource source, float amount) {
        JsonObject h = Bridge.msg("hurt");
        h.addProperty("amount", amount);
        Entity by = source.getAttacker();
        h.addProperty("by", by != null ? by.getName().getString() : source.getName());
        h.addProperty("seat", e.seat);
        Bridge.send(h);
    }

    private static int tick;

    public static void tick(MinecraftServer server) {
        tick++;
        // UNRENDERED stopped talking about them: a second without word and they're gone.
        for (Iterator<SeatEchoEntity> it = seats.values().iterator(); it.hasNext(); ) {
            SeatEchoEntity e = it.next();
            // Each of UNRENDERED's players keeps the rift around them loaded, as a player would.
            if (tick % 10 == 0 && !e.isRemoved() && e.getWorld() instanceof ServerWorld sw) RiftMod.hold(sw, e.getPos());
            if (e.isRemoved() || ++e.unseen > 20 || !Bridge.linked()) {
                e.discard();
                it.remove();
            }
        }
    }

    public static void clear() {
        for (SeatEchoEntity e : seats.values()) e.discard();
        seats.clear();
    }
}
