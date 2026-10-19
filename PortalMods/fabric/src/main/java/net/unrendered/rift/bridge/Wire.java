package net.unrendered.rift.bridge;

import java.io.ByteArrayOutputStream;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.zip.DataFormatException;
import java.util.zip.Deflater;
import java.util.zip.Inflater;

/** Little-endian byte building, and the raw DEFLATE every binary frame travels in. */
public final class Wire {
    private Wire() {}

    /** A growable little-endian buffer. */
    public static final class Out {
        private ByteBuffer buf;

        public Out(int initial) {
            buf = ByteBuffer.allocate(Math.max(64, initial)).order(ByteOrder.LITTLE_ENDIAN);
        }

        private void need(int n) {
            if (buf.remaining() >= n) return;
            int cap = Math.max(buf.capacity() * 2, buf.position() + n + 64);
            ByteBuffer grown = ByteBuffer.allocate(cap).order(ByteOrder.LITTLE_ENDIAN);
            buf.flip();
            grown.put(buf);
            buf = grown;
        }

        public Out u8(int v) { need(1); buf.put((byte) v); return this; }
        public Out u16(int v) { need(2); buf.putShort((short) v); return this; }
        public Out i32(int v) { need(4); buf.putInt(v); return this; }
        public Out u32(long v) { need(4); buf.putInt((int) v); return this; }
        public Out f32(float v) { need(4); buf.putFloat(v); return this; }

        public Out str(String s) {
            byte[] b = (s == null ? "" : s).getBytes(StandardCharsets.UTF_8);
            int n = Math.min(b.length, 65535);
            u16(n);
            need(n);
            buf.put(b, 0, n);
            return this;
        }

        public Out bytes(byte[] b) { need(b.length); buf.put(b); return this; }

        /** Patches a u32 written earlier (a count only known at the end). */
        public void setU32(int at, long v) { buf.putInt(at, (int) v); }
        public void setU16(int at, int v) { buf.putShort(at, (short) v); }

        public int position() { return buf.position(); }

        public byte[] toArray() { return Arrays.copyOf(buf.array(), buf.position()); }
    }

    /** A little-endian reader over a byte array. */
    public static final class In {
        private final ByteBuffer buf;

        public In(byte[] data) { buf = ByteBuffer.wrap(data).order(ByteOrder.LITTLE_ENDIAN); }

        public int u8() { return buf.get() & 0xFF; }
        public int u16() { return buf.getShort() & 0xFFFF; }
        public int i32() { return buf.getInt(); }
        public long u32() { return buf.getInt() & 0xFFFFFFFFL; }
        public float f32() { return buf.getFloat(); }
        public int remaining() { return buf.remaining(); }

        public String str() {
            int n = u16();
            byte[] b = new byte[n];
            buf.get(b);
            return new String(b, StandardCharsets.UTF_8);
        }
    }

    public static byte[] deflate(byte[] raw) {
        Deflater d = new Deflater(Deflater.BEST_SPEED, true);
        d.setInput(raw);
        d.finish();
        ByteArrayOutputStream out = new ByteArrayOutputStream(Math.max(64, raw.length / 3));
        byte[] chunk = new byte[65536];
        while (!d.finished()) {
            int n = d.deflate(chunk);
            out.write(chunk, 0, n);
        }
        d.end();
        return out.toByteArray();
    }

    public static byte[] inflate(byte[] packed) throws DataFormatException {
        Inflater inf = new Inflater(true);
        inf.setInput(packed);
        ByteArrayOutputStream out = new ByteArrayOutputStream(Math.max(64, packed.length * 4));
        byte[] chunk = new byte[65536];
        while (!inf.finished()) {
            int n = inf.inflate(chunk);
            if (n == 0) {
                if (inf.needsInput() || inf.needsDictionary()) break;
            }
            out.write(chunk, 0, n);
        }
        inf.end();
        return out.toByteArray();
    }
}
