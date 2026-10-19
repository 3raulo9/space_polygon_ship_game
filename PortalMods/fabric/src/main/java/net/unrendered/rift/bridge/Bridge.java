package net.unrendered.rift.bridge;

import com.google.gson.JsonObject;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.text.Text;
import net.minecraft.util.Formatting;
import net.unrendered.rift.RiftBuilt;
import net.unrendered.rift.RiftCity;
import net.unrendered.rift.RiftEnemies;
import net.unrendered.rift.RiftSeats;
import net.unrendered.rift.RiftConfig;
import net.unrendered.rift.RiftMod;
import net.unrendered.rift.RiftPilot;
import net.unrendered.rift.RiftChunks;
import net.unrendered.rift.RiftPortals;
import net.unrendered.rift.RiftVisitors;

import java.io.IOException;
import java.net.DatagramPacket;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.MulticastSocket;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ConcurrentLinkedQueue;

/**
 * The Minecraft end of the wire: a TCP listener for UNRENDERED, a LAN beacon so
 * UNRENDERED can find it, and the dispatch of everything that arrives, always on the
 * server thread.
 */
public final class Bridge {
    public static final int PROTO = 1;
    public static final int BEACON_PORT = 4446;
    public static final String BEACON_GROUP = "224.0.2.61";

    static final ConcurrentLinkedQueue<Session.Inbound> inbox = new ConcurrentLinkedQueue<>();

    private static volatile ServerSocket listener;
    private static volatile Session session;
    private static volatile int port;
    private static Thread beaconThread;
    private static volatile String beaconText = "";

    private Bridge() {}

    public static boolean running() { return listener != null; }

    public static int port() { return port; }

    /** The live, welcomed session, or null. */
    public static Session session() {
        Session s = session;
        return s != null && s.isOpen() && s.welcomed ? s : null;
    }

    public static boolean linked() { return session() != null; }

    public static void send(JsonObject msg) {
        Session s = session();
        if (s != null) s.send(msg);
    }

    public static void sendBin(String kind, JsonObject header, byte[] raw) {
        Session s = session();
        if (s != null) s.sendBin(kind, header, raw);
    }

    public static JsonObject msg(String type) {
        JsonObject o = new JsonObject();
        o.addProperty("t", type);
        return o;
    }

    public static void start(MinecraftServer server) {
        ServerSocket ss = null;
        int first = RiftConfig.bridgePort > 0 ? RiftConfig.bridgePort : 25570;
        int last = RiftConfig.bridgePort > 0 ? RiftConfig.bridgePort : 25579;
        for (int p = first; p <= last && ss == null; p++) {
            try {
                ServerSocket candidate = new ServerSocket();
                candidate.setReuseAddress(true);
                candidate.bind(new InetSocketAddress(p));
                ss = candidate;
                port = p;
            } catch (IOException ignored) {
            }
        }
        if (ss == null) {
            RiftMod.LOG.error("Rift: no free port between {} and {}; UNRENDERED can't connect", first, last);
            // Pretend to be running so the tick does not retry every frame.
            port = -1;
            try {
                listener = new ServerSocket();
            } catch (IOException e) {
                return;
            }
            return;
        }
        listener = ss;
        ServerSocket accepting = ss;
        Thread t = new Thread(() -> acceptLoop(accepting), "rift-listener");
        t.setDaemon(true);
        t.start();

        String world = server.getSaveProperties().getLevelName();
        String host = hostName(server);
        JsonObject b = new JsonObject();
        b.addProperty("port", port);
        b.addProperty("world", world);
        b.addProperty("host", host);
        b.addProperty("motd", server.getServerMotd());
        b.addProperty("mc", "1.21.1");
        beaconText = "UNRENDERED-RIFT/1 " + b;
        if (RiftConfig.beacon) startBeacon();

        RiftMod.LOG.info("Rift listening for UNRENDERED on port {}", port);
        for (ServerPlayerEntity p : server.getPlayerManager().getPlayerList()) {
            p.sendMessage(Text.literal("[RIFT] ").formatted(Formatting.LIGHT_PURPLE)
                .append(Text.literal("This world is visible to UNRENDERED on your network.").formatted(Formatting.GRAY)), false);
        }
    }

    public static void stop(String reason) {
        Session s = session;
        if (s != null) s.close(reason);
        session = null;
        ServerSocket l = listener;
        listener = null;
        if (l != null) {
            try {
                l.close();
            } catch (IOException ignored) {
            }
        }
        if (beaconThread != null) beaconThread.interrupt();
        beaconThread = null;
        inbox.clear();
    }

    static String hostName(MinecraftServer server) {
        var list = server.getPlayerManager().getPlayerList();
        if (!list.isEmpty()) return list.get(0).getGameProfile().getName();
        return "MINECRAFT";
    }

    private static void acceptLoop(ServerSocket ss) {
        while (!ss.isClosed()) {
            try {
                Socket sock = ss.accept();
                sock.setTcpNoDelay(true);
                Session current = session;
                if (current != null && current.isOpen()) {
                    // One UNRENDERED per world. A second one is told why, then let go.
                    Session extra = new Session(sock);
                    JsonObject r = msg("refuse");
                    r.addProperty("reason", "another UNRENDERED is already linked to this world");
                    extra.send(r);
                    Thread.sleep(200);
                    extra.close("busy");
                    continue;
                }
                session = new Session(sock);
                RiftMod.LOG.info("Rift: UNRENDERED connected from {}", sock.getRemoteSocketAddress());
            } catch (IOException | InterruptedException e) {
                if (!ss.isClosed()) RiftMod.LOG.warn("Rift accept failed: {}", e.toString());
            }
        }
    }

    private static void startBeacon() {
        beaconThread = new Thread(() -> {
            try (MulticastSocket sock = new MulticastSocket()) {
                sock.setBroadcast(true);
                sock.setTimeToLive(4);
                InetAddress group = InetAddress.getByName(BEACON_GROUP);
                InetAddress broadcast = InetAddress.getByName("255.255.255.255");
                while (!Thread.currentThread().isInterrupted() && listener != null) {
                    byte[] b = beaconText.getBytes(StandardCharsets.UTF_8);
                    try {
                        sock.send(new DatagramPacket(b, b.length, group, BEACON_PORT));
                    } catch (IOException ignored) {
                    }
                    try {
                        sock.send(new DatagramPacket(b, b.length, broadcast, BEACON_PORT));
                    } catch (IOException ignored) {
                    }
                    Thread.sleep(1500);
                }
            } catch (IOException e) {
                RiftMod.LOG.warn("Rift beacon failed: {}", e.toString());
            } catch (InterruptedException ignored) {
            }
        }, "rift-beacon");
        beaconThread.setDaemon(true);
        beaconThread.start();
    }

    /** Called every server tick: keep-alive, drop detection, and dispatch. */
    public static void pump(MinecraftServer server) {
        Session s = session;
        if (s != null) {
            if (s.isOpen()) s.tick();
            if (!s.isOpen()) {
                session = null;
                boolean wasLinked = s.welcomed;
                inbox.clear();
                if (wasLinked) onLinkLost(server);
                return;
            }
        }
        Session.Inbound in;
        int budget = 512;
        while (budget-- > 0 && (in = inbox.poll()) != null) {
            try {
                dispatch(server, in);
            } catch (RuntimeException e) {
                RiftMod.LOG.warn("Rift: failed handling {}: {}", Session.str(in.msg(), "t"), e.toString(), e);
            }
        }
    }

    private static void onLinkLost(MinecraftServer server) {
        RiftPilot.onLinkLost(server);
        RiftVisitors.onLinkLost(server);
        RiftSeats.clear();
        for (ServerPlayerEntity p : server.getPlayerManager().getPlayerList()) {
            p.sendMessage(Text.literal("[RIFT] ").formatted(Formatting.LIGHT_PURPLE)
                .append(Text.literal("UNRENDERED let go of this world.").formatted(Formatting.GRAY)), false);
        }
    }

    private static void dispatch(MinecraftServer server, Session.Inbound in) {
        JsonObject m = in.msg();
        String t = Session.str(m, "t");
        Session s = session;
        if (s == null) return;

        if (!s.welcomed) {
            if (!"hello".equals(t)) return;
            int proto = m.has("proto") ? m.get("proto").getAsInt() : 0;
            if (proto != PROTO) {
                JsonObject r = msg("refuse");
                r.addProperty("reason", "protocol " + proto + " does not match the rift's " + PROTO);
                s.send(r);
                s.close("protocol mismatch");
                return;
            }
            s.pilotName = Session.str(m, "name").isEmpty() ? "PILOT" : Session.str(m, "name");
            s.chassis = Session.str(m, "chassis");
            s.welcomed = true;
            JsonObject w = msg("welcome");
            w.addProperty("proto", PROTO);
            w.addProperty("world", server.getSaveProperties().getLevelName());
            w.addProperty("host", hostName(server));
            w.addProperty("mc", "1.21.1");
            JsonObject portal = RiftPortals.portalJson();
            if (portal != null) w.add("portal", portal);
            s.send(w);
            RiftPortals.broadcast(server, Text.literal(s.pilotName + " reached this world from UNRENDERED.")
                .formatted(Formatting.GRAY));
            // Whatever Minecraft players already built in UNRENDERED's world.
            RiftBuilt.sendAll(server);
            return;
        }

        switch (t) {
            case "join" -> RiftPortals.onJoinRequest(server, s.pilotName);
            case "bye" -> s.close("UNRENDERED said goodbye");
            // A pilot in Minecraft.
            case "enter" -> RiftPilot.onEnter(server, m);
            case "me" -> RiftPilot.onMe(server, m);
            case "view" -> RiftChunks.onView(server, m);
            case "tex" -> RiftPilot.onTex(server, m);
            case "hit" -> RiftPilot.onHit(server, m);
            case "break" -> RiftPilot.onBreak(server, m);
            case "pickup" -> RiftPilot.onPickup(server, m);
            case "drop" -> RiftPilot.onDrop(server, m);
            case "leave" -> RiftPilot.onLeave(server);
            // Minecraft players in UNRENDERED.
            case "city" -> RiftCity.onCity(server, m);
            case "fell" -> RiftCity.onFell(server, m);
            case "place" -> RiftVisitors.onPlace(server, m);
            case "field" -> RiftVisitors.onField(server, m);
            case "harm" -> RiftVisitors.onHarm(server, m);
            case "give" -> RiftVisitors.onGive(server, m);
            // One world each, shared.
            case "seats" -> RiftSeats.onSeats(server, m);
            case "cut" -> RiftCity.onCut(server, m);
            case "cross" -> RiftEnemies.onCross(server, m);
            case "bin" -> {
                String k = Session.str(m, "k");
                byte[] p = in.payload();
                if (p == null) return;
                switch (k) {
                    case "scene" -> RiftVisitors.onScene(server, m, in.packed());
                    case "craft" -> RiftPilot.onCraft(server, in.packed());
                    case "model" -> RiftEnemies.onModel(server, m, in.packed());
                    default -> { }
                }
            }
            default -> { }
        }
    }
}
