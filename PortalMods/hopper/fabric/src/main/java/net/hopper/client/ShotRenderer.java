package net.hopper.client;

import net.hopper.entity.HopperShotEntity;
import net.minecraft.client.render.RenderLayer;
import net.minecraft.client.render.VertexConsumer;
import net.minecraft.client.render.VertexConsumerProvider;
import net.minecraft.client.render.entity.EntityRenderer;
import net.minecraft.client.render.entity.EntityRendererFactory;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.util.Identifier;
import org.joml.Matrix4f;

/** The shot: a small bright green ball inside a fainter glow. */
public final class ShotRenderer extends EntityRenderer<HopperShotEntity> {
    private static final Identifier NONE = Identifier.ofVanilla("textures/misc/white.png");

    public ShotRenderer(EntityRendererFactory.Context ctx) {
        super(ctx);
    }

    @Override
    public void render(HopperShotEntity e, float yaw, float tickDelta, MatrixStack matrices,
                       VertexConsumerProvider buffers, int light) {
        matrices.push();
        matrices.translate(0, e.getHeight() / 2, 0);
        Matrix4f m = matrices.peek().getPositionMatrix();
        VertexConsumer vc = buffers.getBuffer(RenderLayer.getDebugQuads());
        float pulse = 1f + 0.15f * (float) Math.sin((e.age + tickDelta) * 1.3f);
        sphere(vc, m, 0.16f, 0xE8FFE0FF);
        sphere(vc, m, 0.32f * pulse, 0x5CFF6E70);
        matrices.pop();
    }

    private static void sphere(VertexConsumer vc, Matrix4f m, float r, int rgba) {
        final int rings = 6, slices = 10;
        for (int i = 0; i < rings; i++) {
            double t0 = Math.PI * i / rings - Math.PI / 2, t1 = Math.PI * (i + 1) / rings - Math.PI / 2;
            for (int j = 0; j < slices; j++) {
                double p0 = Math.PI * 2 * j / slices, p1 = Math.PI * 2 * (j + 1) / slices;
                float[][] q = {at(r, t0, p0), at(r, t0, p1), at(r, t1, p1), at(r, t1, p0)};
                for (int k = 0; k < 4; k++) put(vc, m, q[k], rgba);
                for (int k = 3; k >= 0; k--) put(vc, m, q[k], rgba);
            }
        }
    }

    private static float[] at(float r, double lat, double lon) {
        return new float[]{(float) (Math.cos(lat) * Math.cos(lon)) * r, (float) Math.sin(lat) * r,
            (float) (Math.cos(lat) * Math.sin(lon)) * r};
    }

    private static void put(VertexConsumer vc, Matrix4f m, float[] p, int rgba) {
        vc.vertex(m, p[0], p[1], p[2]).color(rgba >>> 24, (rgba >>> 16) & 0xFF, (rgba >>> 8) & 0xFF, rgba & 0xFF);
    }

    @Override
    public Identifier getTexture(HopperShotEntity entity) {
        return NONE;
    }
}
