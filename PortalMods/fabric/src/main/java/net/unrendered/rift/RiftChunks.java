package net.unrendered.rift;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import net.minecraft.block.BlockState;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.world.ChunkTicketType;
import net.minecraft.server.world.ServerWorld;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.ChunkPos;
import net.minecraft.world.LightType;
import net.minecraft.world.chunk.ChunkSection;
import net.minecraft.world.chunk.WorldChunk;
import net.minecraft.world.chunk.light.ChunkLightingView;
import net.unrendered.rift.bridge.Bridge;
import net.unrendered.rift.bridge.Wire;

import java.util.Comparator;
import java.util.HashSet;
import java.util.Set;

/**
 * Minecraft streamed to the pilot the way Minecraft streams itself to a player: chunk by
 * chunk, nearest first, around wherever they are standing, generating what was never
 * generated. UNRENDERED says where it is ({@code view}); this side holds a ticket around that
 * spot so the chunks load and tick, and sends each one once it is ready.
 */
public final class RiftChunks {
    /** A chunk ticket that holds the pilot's view loaded, like a player's own. */
    public static final ChunkTicketType<ChunkPos> VIEW =
        ChunkTicketType.create("unrendered_view", Comparator.comparingLong(ChunkPos::toLong));

    private static int cx, cz, r = 8;
    private static boolean viewing;
    private static ChunkPos ticket;
    private static int ticketRadius;
    private static final Set<Long> sent = new HashSet<>();
    private static int sentTotal;

    private RiftChunks() {}

    public static int radius() {
        return viewing ? r : RiftConfig.regionRadius / 16;
    }

    public static boolean isSent(BlockPos pos) {
        return sent.contains(ChunkPos.toLong(pos.getX() >> 4, pos.getZ() >> 4));
    }

    public static void clear(MinecraftServer server) {
        ServerWorld w = RiftPilot.world(server);
        if (w != null && ticket != null) w.getChunkManager().removeTicket(VIEW, ticket, ticketRadius, ticket);
        ticket = null;
        viewing = false;
        sent.clear();
    }

    /** UNRENDERED says which chunk the pilot is in and how far they see. */
    public static void onView(MinecraftServer server, JsonObject m) {
        if (!RiftPilot.active()) return;
        cx = m.get("cx").getAsInt();
        cz = m.get("cz").getAsInt();
        r = Math.max(2, Math.min(16, m.has("r") ? m.get("r").getAsInt() : 8));
        viewing = true;
        ServerWorld w = RiftPilot.world(server);
        if (w == null) return;
        ChunkPos want = new ChunkPos(cx, cz);
        if (ticket == null || !ticket.equals(want) || ticketRadius != r) {
            if (ticket != null) w.getChunkManager().removeTicket(VIEW, ticket, ticketRadius, ticket);
            w.getChunkManager().addTicket(VIEW, want, r, want);
            ticket = want;
            ticketRadius = r;
        }
        // Forget what is now well out of sight: if the pilot comes back, it is sent again.
        sent.removeIf(k -> Math.max(Math.abs(ChunkPos.getPackedX(k) - cx), Math.abs(ChunkPos.getPackedZ(k) - cz)) > r + 2);
    }

    /** A few chunks a tick, nearest first, each once it has finished loading. */
    public static void tick(MinecraftServer server) {
        if (!RiftPilot.active() || !viewing || !Bridge.linked()) return;
        var s = Bridge.session();
        if (s == null || s.backlog() > 32) return;
        ServerWorld w = RiftPilot.world(server);
        if (w == null) return;
        int budget = RiftConfig.chunksPerTick;
        for (int ring = 0; ring <= r && budget > 0; ring++) {
            for (int dx = -ring; dx <= ring && budget > 0; dx++) {
                for (int dz = -ring; dz <= ring && budget > 0; dz++) {
                    if (Math.max(Math.abs(dx), Math.abs(dz)) != ring) continue;
                    int x = cx + dx, z = cz + dz;
                    long key = ChunkPos.toLong(x, z);
                    if (sent.contains(key)) continue;
                    WorldChunk chunk = w.getChunkManager().getWorldChunk(x, z, false);
                    if (chunk == null) continue;   // still loading or generating; next tick
                    send(w, chunk);
                    sent.add(key);
                    budget--;
                }
            }
        }
    }

    /** One column: the run of sections that hold anything, as palette indices and light. */
    public static void send(ServerWorld w, WorldChunk chunk) {
        ChunkSection[] sections = chunk.getSectionArray();
        int lo = -1, hi = -1;
        for (int i = 0; i < sections.length; i++) {
            if (sections[i] != null && !sections[i].isEmpty()) {
                if (lo < 0) lo = i;
                hi = i;
            }
        }
        ChunkPos cp = chunk.getPos();
        int bottom = w.getBottomY();
        int y0 = lo < 0 ? bottom : bottom + lo * 16;
        int h = lo < 0 ? 0 : (hi - lo + 1) * 16;
        // One section of air on top, so the light the surface faces into is sent too.
        if (h > 0 && y0 + h < w.getTopY()) h += 16;

        JsonArray fresh = new JsonArray();
        int n = 16 * 16 * h;
        Wire.Out out = new Wire.Out(n * 3 + 8);
        byte[] light = new byte[n];
        ChunkLightingView sky = w.getLightingProvider().get(LightType.SKY);
        ChunkLightingView blk = w.getLightingProvider().get(LightType.BLOCK);
        BlockPos.Mutable pos = new BlockPos.Mutable();
        int k = 0;
        int bx = cp.getStartX(), bz = cp.getStartZ();
        for (int y = 0; y < h; y++) {
            for (int z = 0; z < 16; z++) {
                for (int x = 0; x < 16; x++) {
                    pos.set(bx + x, y0 + y, bz + z);
                    BlockState state = chunk.getBlockState(pos);
                    out.u16(RiftPilot.indexOf(w, state, pos, fresh));
                    int sl = sky.getLightLevel(pos), bl = blk.getLightLevel(pos);
                    light[k++] = (byte) ((sl << 4) | bl);
                }
            }
        }
        out.bytes(light);
        if (!fresh.isEmpty()) {
            JsonObject pal = Bridge.msg("palette");
            pal.add("entries", fresh);
            Bridge.send(pal);
        }
        JsonObject head = new JsonObject();
        head.addProperty("cx", cp.x);
        head.addProperty("cz", cp.z);
        head.addProperty("y0", y0);
        head.addProperty("h", h);
        Bridge.sendBin("chunk", head, out.toArray());
        sentTotal++;
        if (sentTotal % 50 == 1) RiftMod.LOG.info("Rift: {} chunks streamed to the pilot (last {},{} y0={} h={})", sentTotal, cp.x, cp.z, y0, h);
    }
}
