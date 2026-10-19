package net.unrendered.rift.client;

import net.fabricmc.fabric.api.client.rendering.v1.WorldRenderContext;
import net.minecraft.client.MinecraftClient;
import net.minecraft.util.math.Vec3d;
import net.unrendered.rift.RiftMod;
import net.unrendered.rift.bridge.Wire;
import org.joml.Matrix4f;

/**
 * UNRENDERED's world around a Minecraft player in the rift: the latest frame UNRENDERED
 * rendered from their eye, replayed where it stands. MC (x, 64 + y, z) is UNR (x, y, z).
 */
public final class SceneRenderer {
    /** The sky UNRENDERED is under, from the latest frame. */
    public record Sky(int[] top, int[] mid, int[] horizon, int[] fog, float murk, float lx, float ly, float lz,
                      float sunAlt, float sunAz, float moonAlt, float moonAz, float night) {}

    private static volatile DrawList frame;
    public static volatile Sky sky;
    private static volatile long lastFrame;
    private static int scenes;

    private SceneRenderer() {}

    public static boolean live() {
        return frame != null && System.currentTimeMillis() - lastFrame < 5000;
    }

    /** A `scene` arrived (still deflated). */
    public static void accept(byte[] packed) {
        try {
            Wire.In in = new Wire.In(Wire.inflate(packed));
            int[] top = rgba(in), mid = rgba(in), horizon = rgba(in), fog = rgba(in);
            Sky s = new Sky(top, mid, horizon, fog, in.f32(), in.f32(), in.f32(), in.f32(),
                in.f32(), in.f32(), in.f32(), in.f32(), in.f32());
            in.f32();
            in.f32();
            in.f32();   // the eye UNRENDERED drew from
            DrawList list = DrawList.read(in);
            sky = s;
            frame = list;
            if (++scenes % 30 == 1) {
                int verts = 0;
                for (DrawList.Draw d : list.draws) verts += d.count();
                RiftMod.LOG.info("RIFT-E2E: scene #{} arrived: {} draws, {} vertices", scenes, list.draws.size(), verts);
            }
            lastFrame = System.currentTimeMillis();
        } catch (Exception e) {
            RiftMod.LOG.debug("Rift: bad scene: {}", e.toString());
        }
    }

    private static int[] rgba(Wire.In in) {
        return new int[]{in.u8(), in.u8(), in.u8(), in.u8()};
    }

    public static void clear() {
        frame = null;
        sky = null;
    }

    public static void render(WorldRenderContext ctx) {
        MinecraftClient mc = MinecraftClient.getInstance();
        if (mc.world == null || mc.world.getRegistryKey() != RiftMod.RIFT) {
            if (frame != null && mc.world != null) clear();
            return;
        }
        DrawList f = frame;
        if (f == null) return;
        Vec3d cam = ctx.camera().getPos();
        Matrix4f m = ctx.matrixStack() != null ? ctx.matrixStack().peek().getPositionMatrix() : new Matrix4f();
        f.render(m, -cam.x, RiftMod.FLOOR_Y - cam.y, -cam.z);
    }
}
