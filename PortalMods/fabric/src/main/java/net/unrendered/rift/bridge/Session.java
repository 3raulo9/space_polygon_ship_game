package net.unrendered.rift.bridge;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import net.unrendered.rift.RiftMod;

import java.io.BufferedInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;

/**
 * One UNRENDERED on the other end of the wire. A reader thread turns the stream into
 * messages for the server thread, and a writer thread drains what the server thread
 * queued, so a slow link never stalls a tick.
 */
public final class Session {
    /** A message as it came off the wire, plus the payload if it was a binary frame. */
    public record Inbound(JsonObject msg, byte[] payload, byte[] packed) {}

    private final Socket socket;
    private final LinkedBlockingQueue<byte[]> outbox = new LinkedBlockingQueue<>();
    private volatile boolean open = true;
    private volatile long lastHeard = System.currentTimeMillis();
    private long lastPing;
    private final String remote;

    /** True once `hello` has been answered with `welcome`. */
    public volatile boolean welcomed;
    public volatile String pilotName = "PILOT";
    public volatile String chassis = "TANK";

    public Session(Socket socket) {
        this.socket = socket;
        this.remote = String.valueOf(socket.getRemoteSocketAddress());
        Thread reader = new Thread(this::readLoop, "rift-reader");
        reader.setDaemon(true);
        reader.start();
        Thread writer = new Thread(this::writeLoop, "rift-writer");
        writer.setDaemon(true);
        writer.start();
    }

    public String remote() { return remote; }

    public boolean isOpen() { return open; }

    public void send(JsonObject msg) {
        if (!open) return;
        outbox.offer((msg.toString() + "\n").getBytes(StandardCharsets.UTF_8));
    }

    /** Sends a binary frame: the header line, then the raw payload deflated. */
    public void sendBin(String kind, JsonObject header, byte[] raw) {
        if (!open) return;
        byte[] packed = Wire.deflate(raw);
        JsonObject h = header == null ? new JsonObject() : header.deepCopy();
        h.addProperty("t", "bin");
        h.addProperty("k", kind);
        h.addProperty("n", packed.length);
        byte[] line = (h.toString() + "\n").getBytes(StandardCharsets.UTF_8);
        byte[] frame = new byte[line.length + packed.length];
        System.arraycopy(line, 0, frame, 0, line.length);
        System.arraycopy(packed, 0, frame, line.length, packed.length);
        outbox.offer(frame);
    }

    /** How many bytes are waiting to go out. Streams back off while the link is behind. */
    public int backlog() { return outbox.size(); }

    /** Keep-alive and timeout, called every server tick. */
    public void tick() {
        long now = System.currentTimeMillis();
        if (now - lastPing > 2000) {
            lastPing = now;
            JsonObject p = new JsonObject();
            p.addProperty("t", "ping");
            send(p);
        }
        if (now - lastHeard > 15000) close("UNRENDERED stopped answering");
    }

    public void close(String reason) {
        if (!open) return;
        JsonObject bye = new JsonObject();
        bye.addProperty("t", "bye");
        bye.addProperty("reason", reason);
        try {
            OutputStream out = socket.getOutputStream();
            synchronized (this) {
                out.write((bye.toString() + "\n").getBytes(StandardCharsets.UTF_8));
                out.flush();
            }
        } catch (IOException ignored) {
        }
        open = false;
        outbox.offer(new byte[0]);
        try {
            socket.close();
        } catch (IOException ignored) {
        }
        RiftMod.LOG.info("Rift link to {} closed: {}", remote, reason);
    }

    private void writeLoop() {
        try {
            OutputStream out = socket.getOutputStream();
            while (open) {
                byte[] b = outbox.poll(1, TimeUnit.SECONDS);
                if (b == null || b.length == 0) continue;
                synchronized (this) {
                    out.write(b);
                    // Batch whatever else is already waiting into the same flush.
                    byte[] more;
                    while ((more = outbox.poll()) != null && more.length > 0) out.write(more);
                    out.flush();
                }
            }
        } catch (IOException | InterruptedException e) {
            if (open) {
                open = false;
                RiftMod.LOG.info("Rift link to {} lost while writing: {}", remote, e.toString());
            }
        }
    }

    private void readLoop() {
        try (InputStream raw = new BufferedInputStream(socket.getInputStream(), 1 << 16)) {
            ByteArrayOutputStream line = new ByteArrayOutputStream(512);
            while (open) {
                int c = raw.read();
                if (c < 0) break;
                if (c != '\n') {
                    line.write(c);
                    if (line.size() > (16 << 20)) throw new IOException("line too long");
                    continue;
                }
                String text = line.toString(StandardCharsets.UTF_8).trim();
                line.reset();
                if (text.isEmpty()) continue;
                lastHeard = System.currentTimeMillis();
                JsonObject msg;
                try {
                    msg = JsonParser.parseString(text).getAsJsonObject();
                } catch (RuntimeException e) {
                    RiftMod.LOG.warn("Rift: unreadable line from UNRENDERED: {}", e.toString());
                    continue;
                }
                byte[] payload = null;
                byte[] packedCopy = null;
                if ("bin".equals(str(msg, "t"))) {
                    int n = msg.has("n") ? msg.get("n").getAsInt() : 0;
                    if (n < 0 || n > (64 << 20)) throw new IOException("bad binary length " + n);
                    byte[] packed = raw.readNBytes(n);
                    if (packed.length != n) break;
                    packedCopy = packed;
                    try {
                        payload = Wire.inflate(packed);
                    } catch (Exception e) {
                        RiftMod.LOG.warn("Rift: bad binary frame {}: {}", str(msg, "k"), e.toString());
                        continue;
                    }
                }
                if ("ping".equals(str(msg, "t"))) continue;
                Bridge.inbox.offer(new Inbound(msg, payload, packedCopy));
            }
        } catch (IOException e) {
            if (open) RiftMod.LOG.info("Rift link to {} lost: {}", remote, e.toString());
        }
        open = false;
    }

    public static String str(JsonObject o, String key) {
        return o.has(key) && !o.get(key).isJsonNull() ? o.get(key).getAsString() : "";
    }
}
