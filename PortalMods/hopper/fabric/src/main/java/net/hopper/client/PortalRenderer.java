package net.hopper.client;

import net.hopper.entity.PortalEntity;
import net.minecraft.client.render.Frustum;
import net.minecraft.client.render.RenderLayer;
import net.minecraft.client.render.VertexConsumer;
import net.minecraft.client.render.VertexConsumerProvider;
import net.minecraft.client.render.entity.EntityRenderer;
import net.minecraft.client.render.entity.EntityRendererFactory;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.util.Identifier;
import net.minecraft.util.math.MathHelper;
import net.minecraft.util.math.RotationAxis;
import org.joml.Matrix4f;

/**
 * The portal: an upright oval of churning green, drawn rather than textured. Rings of the
 * oval are banded by a spiral that winds inward as it turns, bright at the rim and deep in
 * the middle, so it reads as a hole spinning down into somewhere else. It opens from a point
 * over its first half second and shrinks away in its last. Unlit (it is its own light), and
 * the same from both sides.
 */
public final class PortalRenderer extends EntityRenderer<PortalEntity> {
    private static final Identifier NONE = Identifier.ofVanilla("textures/misc/white.png");
    private static final int RINGS = 14, SEGMENTS = 56;

    public PortalRenderer(EntityRendererFactory.Context ctx) {
        super(ctx);
    }

    @Override
    public boolean shouldRender(PortalEntity e, Frustum frustum, double x, double y, double z) {
        return true;
    }

    @Override
    public void render(PortalEntity e, float yaw, float tickDelta, MatrixStack matrices,
                       VertexConsumerProvider buffers, int light) {
        float t = (e.openFor() + tickDelta) / 20f;
        float grow = MathHelper.clamp(t / 0.5f, 0f, 1f);
        float shrink = MathHelper.clamp((PortalEntity.LIFE / 20f - t) / 0.6f, 0f, 1f);
        float open = (float) Math.sin(Math.min(grow, shrink) * Math.PI / 2);
        if (open <= 0.01f) return;
        matrices.push();
        matrices.translate(0, PortalEntity.HEIGHT / 2, 0);
        matrices.multiply(RotationAxis.POSITIVE_Y.rotationDegrees(-e.getYaw()));
        Matrix4f m = matrices.peek().getPositionMatrix();
        VertexConsumer vc = buffers.getBuffer(RenderLayer.getDebugQuads());
        float a = PortalEntity.WIDTH / 2 * open, b = PortalEntity.HEIGHT / 2 * open;
        for (int ri = 0; ri < RINGS; ri++) {
            float r0 = ri / (float) RINGS, r1 = (ri + 1) / (float) RINGS;
            for (int si = 0; si < SEGMENTS; si++) {
                float th0 = (float) (si * Math.PI * 2 / SEGMENTS), th1 = (float) ((si + 1) * Math.PI * 2 / SEGMENTS);
                int c00 = colour(r0, th0, t), c01 = colour(r0, th1, t), c11 = colour(r1, th1, t), c10 = colour(r1, th0, t);
                quad(vc, m,
                    a * r0 * (float) Math.cos(th0), b * r0 * (float) Math.sin(th0), c00,
                    a * r0 * (float) Math.cos(th1), b * r0 * (float) Math.sin(th1), c01,
                    a * r1 * (float) Math.cos(th1), b * r1 * (float) Math.sin(th1), c11,
                    a * r1 * (float) Math.cos(th0), b * r1 * (float) Math.sin(th0), c10);
            }
        }
        // The rim: a bright lip just outside the swirl, shimmering.
        for (int si = 0; si < SEGMENTS; si++) {
            float th0 = (float) (si * Math.PI * 2 / SEGMENTS), th1 = (float) ((si + 1) * Math.PI * 2 / SEGMENTS);
            float w0 = 1.0f + 0.07f * (float) Math.sin(th0 * 7 + t * 9), w1 = 1.0f + 0.07f * (float) Math.sin(th1 * 7 + t * 9);
            int rim = 0xE6C8FFB4, out = 0x00A0FF80;
            quad(vc, m,
                a * (float) Math.cos(th0), b * (float) Math.sin(th0), rim,
                a * (float) Math.cos(th1), b * (float) Math.sin(th1), rim,
                a * w1 * 1.06f * (float) Math.cos(th1), b * w1 * 1.06f * (float) Math.sin(th1), out,
                a * w0 * 1.06f * (float) Math.cos(th0), b * w0 * 1.06f * (float) Math.sin(th0), out);
        }
        matrices.pop();
        super.render(e, yaw, tickDelta, matrices, buffers, light);
    }

    /** The swirl at radius r (0 centre, 1 rim) and angle th: spiral bands winding inward. */
    private static int colour(float r, float th, float t) {
        float band = 0.5f + 0.5f * (float) Math.sin(th * 3 + r * 11f - t * 5f);
        float glow = 0.35f + 0.65f * r;                     // brighter toward the rim
        float v = MathHelper.clamp(glow * (0.45f + 0.55f * band), 0f, 1f);
        int red = (int) (40 + 150 * v * v), green = (int) (120 + 135 * v), blue = (int) (30 + 90 * v * v);
        int alpha = (int) (215 + 35 * v);
        return red << 24 | green << 16 | blue << 8 | Math.min(255, alpha);
    }

    private static void quad(VertexConsumer vc, Matrix4f m, float x0, float y0, int c0, float x1, float y1, int c1,
                             float x2, float y2, int c2, float x3, float y3, int c3) {
        float[] xs = {x0, x1, x2, x3}, ys = {y0, y1, y2, y3};
        int[] cs = {c0, c1, c2, c3};
        for (int k = 0; k < 4; k++) put(vc, m, xs[k], ys[k], cs[k]);
        for (int k = 3; k >= 0; k--) put(vc, m, xs[k], ys[k], cs[k]);
    }

    private static void put(VertexConsumer vc, Matrix4f m, float x, float y, int rgba) {
        vc.vertex(m, x, y, 0f).color(rgba >>> 24, (rgba >>> 16) & 0xFF, (rgba >>> 8) & 0xFF, rgba & 0xFF);
    }

    @Override
    public Identifier getTexture(PortalEntity entity) {
        return NONE;
    }
}
