package net.unrendered.rift.client;

import com.mojang.blaze3d.systems.RenderSystem;
import net.minecraft.client.render.BufferBuilder;
import net.minecraft.client.render.BufferRenderer;
import net.minecraft.client.render.BuiltBuffer;
import net.minecraft.client.render.GameRenderer;
import net.minecraft.client.render.Tessellator;
import net.minecraft.client.render.VertexFormat;
import net.minecraft.client.render.VertexFormats;
import net.unrendered.rift.bridge.Wire;
import org.joml.Matrix4f;

import java.util.ArrayList;
import java.util.List;

/**
 * UNRENDERED's own vertices: what its renderer emitted, already shaded and fogged, in
 * draws of lines, triangles or quads. Replayed here as they are.
 */
public final class DrawList {
    public static final int LINES = 1, TRIANGLES = 4, QUADS = 7;

    public record Draw(int mode, float[] xyz, byte[] rgba, int count) {}

    public final List<Draw> draws = new ArrayList<>();

    public static DrawList read(Wire.In in) {
        DrawList list = new DrawList();
        long n = in.u32();
        for (long d = 0; d < n && in.remaining() >= 5; d++) {
            int mode = in.u8();
            int verts = (int) Math.min(in.u32(), in.remaining() / 16);
            float[] xyz = new float[verts * 3];
            byte[] rgba = new byte[verts * 4];
            for (int v = 0; v < verts; v++) {
                xyz[v * 3] = in.f32();
                xyz[v * 3 + 1] = in.f32();
                xyz[v * 3 + 2] = in.f32();
                rgba[v * 4] = (byte) in.u8();
                rgba[v * 4 + 1] = (byte) in.u8();
                rgba[v * 4 + 2] = (byte) in.u8();
                rgba[v * 4 + 3] = (byte) in.u8();
            }
            list.draws.add(new Draw(mode, xyz, rgba, verts));
        }
        return list;
    }

    /**
     * Draws it with position-colour, culling off (UNRENDERED's meshes are two-sided) and
     * depth on. Each vertex is moved by (dx, dy, dz) and then through {@code m}.
     */
    public void render(Matrix4f m, double dx, double dy, double dz) {
        RenderSystem.setShader(GameRenderer::getPositionColorProgram);
        RenderSystem.disableCull();
        RenderSystem.enableDepthTest();
        RenderSystem.depthMask(true);
        RenderSystem.enableBlend();
        RenderSystem.defaultBlendFunc();
        for (Draw d : draws) {
            VertexFormat.DrawMode mode;
            int group;
            switch (d.mode()) {
                case LINES -> { mode = VertexFormat.DrawMode.DEBUG_LINES; group = 2; }
                case QUADS -> { mode = VertexFormat.DrawMode.QUADS; group = 4; }
                case TRIANGLES -> { mode = VertexFormat.DrawMode.TRIANGLES; group = 3; }
                default -> { continue; }
            }
            int usable = d.count() - d.count() % group;
            int chunk = 60000 - 60000 % (group * 4);
            for (int start = 0; start < usable; start += chunk) {
                int end = Math.min(usable, start + chunk);
                BufferBuilder bb = Tessellator.getInstance().begin(mode, VertexFormats.POSITION_COLOR);
                float[] p = d.xyz();
                byte[] c = d.rgba();
                for (int v = start; v < end; v++) {
                    bb.vertex(m, (float) (p[v * 3] + dx), (float) (p[v * 3 + 1] + dy), (float) (p[v * 3 + 2] + dz))
                        .color(c[v * 4] & 0xFF, c[v * 4 + 1] & 0xFF, c[v * 4 + 2] & 0xFF, c[v * 4 + 3] & 0xFF);
                }
                BuiltBuffer built = bb.endNullable();
                if (built != null) BufferRenderer.drawWithGlobalProgram(built);
            }
        }
        RenderSystem.enableCull();
        RenderSystem.disableBlend();
    }
}
