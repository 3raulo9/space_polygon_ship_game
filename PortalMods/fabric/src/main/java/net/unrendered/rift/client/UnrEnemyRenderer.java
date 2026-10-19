package net.unrendered.rift.client;

import net.minecraft.client.render.RenderLayer;
import net.minecraft.client.render.VertexConsumer;
import net.minecraft.client.render.VertexConsumerProvider;
import net.minecraft.client.render.entity.EntityRenderer;
import net.minecraft.client.render.entity.EntityRendererFactory;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.util.Identifier;
import net.minecraft.util.math.MathHelper;
import net.minecraft.util.math.RotationAxis;
import net.unrendered.rift.RiftMod;
import net.unrendered.rift.bridge.Wire;
import net.unrendered.rift.entity.UnrEnemyEntity;
import org.joml.Matrix4f;

import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Draws an UNRENDERED enemy with UNRENDERED's own polygons for its kind, turned to where it
 * faces. Through the ordinary entity pipeline, so a pilot watching from UNRENDERED gets the
 * same picture in its capture.
 */
public final class UnrEnemyRenderer extends EntityRenderer<UnrEnemyEntity> {
    private static final Map<String, DrawList> models = new ConcurrentHashMap<>();
    private static final Identifier NONE = Identifier.ofVanilla("textures/misc/white.png");

    public UnrEnemyRenderer(EntityRendererFactory.Context ctx) {
        super(ctx);
        shadowRadius = 0.9f;
    }

    /** A `model` arrived (still deflated). */
    public static void accept(String kind, byte[] packed) {
        try {
            models.put(kind, DrawList.read(new Wire.In(Wire.inflate(packed))));
        } catch (Exception e) {
            RiftMod.LOG.debug("Rift: bad model for {}: {}", kind, e.toString());
        }
    }

    @Override
    public void render(UnrEnemyEntity e, float yaw, float tickDelta, MatrixStack matrices,
                       VertexConsumerProvider buffers, int light) {
        float body = MathHelper.lerpAngleDegrees(tickDelta, e.prevBodyYaw, e.bodyYaw);
        matrices.push();
        // UNRENDERED heading 0 faces +Z, as does Minecraft's yaw 0; heading = -yaw.
        matrices.multiply(RotationAxis.POSITIVE_Y.rotationDegrees(-body));
        Matrix4f m = matrices.peek().getPositionMatrix();
        VertexConsumer vc = buffers.getBuffer(RenderLayer.getDebugQuads());
        boolean hurt = e.hurtTime > 0;
        DrawList model = models.get(e.kind());
        if (model != null) emit(vc, m, model, hurt);
        else box(vc, m, e.getWidth() / 2f, e.getHeight(), hurt);
        matrices.pop();
        super.render(e, yaw, tickDelta, matrices, buffers, light);
    }

    private static void emit(VertexConsumer vc, Matrix4f m, DrawList list, boolean hurt) {
        for (DrawList.Draw d : list.draws) {
            float[] p = d.xyz();
            byte[] c = d.rgba();
            int group = d.mode() == DrawList.QUADS ? 4 : d.mode() == DrawList.TRIANGLES ? 3 : 0;
            if (group == 0) continue;
            for (int s = 0; s + group <= d.count(); s += group) {
                int[] idx = group == 4 ? new int[]{s, s + 1, s + 2, s + 3} : new int[]{s, s + 1, s + 2, s + 2};
                // Both windings: UNRENDERED's meshes are two-sided.
                for (int pass = 0; pass < 2; pass++) {
                    for (int k = 0; k < 4; k++) {
                        int v = idx[pass == 0 ? k : 3 - k];
                        int r = c[v * 4] & 0xFF, g = c[v * 4 + 1] & 0xFF, b = c[v * 4 + 2] & 0xFF, a = c[v * 4 + 3] & 0xFF;
                        if (hurt) {
                            g = g / 2;
                            b = b / 2;
                            r = Math.min(255, r + 60);
                        }
                        vc.vertex(m, p[v * 3], p[v * 3 + 1], p[v * 3 + 2]).color(r, g, b, a);
                    }
                }
            }
        }
    }

    /** No body has arrived yet: a dark block the size of the hitbox. */
    private static void box(VertexConsumer vc, Matrix4f m, float hw, float h, boolean hurt) {
        int r = hurt ? 160 : 40, g = 20, b = hurt ? 30 : 50;
        float[][] f = {
            {-hw, 0, -hw, hw, 0, -hw, hw, 0, hw, -hw, 0, hw},
            {-hw, h, -hw, -hw, h, hw, hw, h, hw, hw, h, -hw},
            {-hw, 0, -hw, -hw, h, -hw, hw, h, -hw, hw, 0, -hw},
            {-hw, 0, hw, hw, 0, hw, hw, h, hw, -hw, h, hw},
            {-hw, 0, -hw, -hw, 0, hw, -hw, h, hw, -hw, h, -hw},
            {hw, 0, -hw, hw, h, -hw, hw, h, hw, hw, 0, hw},
        };
        for (float[] q : f) {
            for (int k = 0; k < 4; k++) vc.vertex(m, q[k * 3], q[k * 3 + 1], q[k * 3 + 2]).color(r, g, b, 255);
            for (int k = 3; k >= 0; k--) vc.vertex(m, q[k * 3], q[k * 3 + 1], q[k * 3 + 2]).color(r, g, b, 255);
        }
    }

    @Override
    public Identifier getTexture(UnrEnemyEntity entity) {
        return NONE;
    }

    public static void clear() {
        models.clear();
    }
}
