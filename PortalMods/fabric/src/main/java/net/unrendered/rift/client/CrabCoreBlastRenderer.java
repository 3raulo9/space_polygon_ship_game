package net.unrendered.rift.client;

import net.minecraft.client.render.Frustum;
import net.minecraft.client.render.RenderLayer;
import net.minecraft.client.render.VertexConsumer;
import net.minecraft.client.render.VertexConsumerProvider;
import net.minecraft.client.render.entity.EntityRenderer;
import net.minecraft.client.render.entity.EntityRendererFactory;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.util.Identifier;
import net.minecraft.util.Util;
import net.unrendered.rift.UnrModels;
import net.unrendered.rift.entity.CrabCoreBlastEntity;
import org.joml.Matrix4f;
import org.joml.Vector3f;

/**
 * UNRENDERED's crab core blast, drawn the way UNRENDERED's EntityRenderer draws it: every
 * lance a tapering six-sided shaft, white core under a red sheath, fat at the core and a
 * point at the tip, boiling at 33 Hz; a magenta bubble breathing round the core with a
 * paler one inside it; and the blown-out core itself pulsing white at the centre.
 */
public final class CrabCoreBlastRenderer extends EntityRenderer<CrabCoreBlastEntity> {
    private static final Identifier NONE = Identifier.ofVanilla("textures/misc/white.png");

    public CrabCoreBlastRenderer(EntityRendererFactory.Context ctx) {
        super(ctx);
    }

    @Override
    public boolean shouldRender(CrabCoreBlastEntity e, Frustum frustum, double x, double y, double z) {
        return true;
    }

    @Override
    public void render(CrabCoreBlastEntity e, float yaw, float tickDelta, MatrixStack matrices,
                       VertexConsumerProvider buffers, int light) {
        float age = e.age(tickDelta);
        float env = CrabCoreBlastEntity.envelope(age);
        if (env <= 0.001f) return;
        float t = (Util.getMeasuringTimeMs() % 3_600_000L) / 1000f;
        Matrix4f m = matrices.peek().getPositionMatrix();
        VertexConsumer vc = buffers.getBuffer(RenderLayer.getDebugQuads());

        float len = UnrModels.blastLength * env;
        float flutter = 0.9f + 0.1f * (float) Math.sin(t * 33f);
        float sheath = 1.15f * env * flutter;
        float core = sheath * 0.45f;
        int red = UnrModels.blastRed;
        int redSheath = (red & 0xFFFFFF00) | (int) (150 * env);
        Vector3f o = new Vector3f();
        for (int b = 0; b < UnrModels.blastBeams; b++) {
            Vector3f to = e.direction(b, age).mul(len);
            cylinder(vc, m, o, to, core * 1.2f, core * 0.15f, 0xFFFFFFFF);
            cylinder(vc, m, o, to, sheath * 1.3f, sheath * 0.15f, redSheath);
        }

        float bubble = CrabCoreBlastEntity.bubble(age);
        if (bubble > 0.05f) {
            int mag = UnrModels.blastMagenta;
            sphere(vc, m, bubble, 12, 12, (mag & 0xFFFFFF00) | (int) (70 * env));
            sphere(vc, m, bubble * 0.6f, 10, 10, 0xFF82AA00 | (int) (95 * env));
        }
        sphere(vc, m, (1.5f + 0.6f * (float) Math.sin(t * 20f)) * env, 8, 8, 0xFFFFFFFF);
    }

    /** raylib's DrawCylinderEx: a tapering shaft of {@code sides} faces, ends left open. */
    private static void cylinder(VertexConsumer vc, Matrix4f m, Vector3f a, Vector3f b, float r0, float r1, int rgba) {
        Vector3f axis = new Vector3f(b).sub(a);
        if (axis.lengthSquared() < 1e-6f) return;
        axis.normalize();
        Vector3f u = Math.abs(axis.y) < 0.9f ? new Vector3f(0, 1, 0).cross(axis).normalize() : new Vector3f(1, 0, 0).cross(axis).normalize();
        Vector3f w = new Vector3f(axis).cross(u);
        final int sides = 6;
        for (int i = 0; i < sides; i++) {
            double a0 = Math.PI * 2 * i / sides, a1 = Math.PI * 2 * (i + 1) / sides;
            Vector3f d0 = new Vector3f(u).mul((float) Math.cos(a0)).add(new Vector3f(w).mul((float) Math.sin(a0)));
            Vector3f d1 = new Vector3f(u).mul((float) Math.cos(a1)).add(new Vector3f(w).mul((float) Math.sin(a1)));
            quad(vc, m, rgba,
                new Vector3f(d0).mul(r0).add(a), new Vector3f(d1).mul(r0).add(a),
                new Vector3f(d1).mul(r1).add(b), new Vector3f(d0).mul(r1).add(b));
        }
    }

    /** raylib's DrawSphereEx, as rings and slices. */
    private static void sphere(VertexConsumer vc, Matrix4f m, float r, int rings, int slices, int rgba) {
        for (int i = 0; i < rings; i++) {
            double t0 = Math.PI * i / rings - Math.PI / 2, t1 = Math.PI * (i + 1) / rings - Math.PI / 2;
            for (int j = 0; j < slices; j++) {
                double p0 = Math.PI * 2 * j / slices, p1 = Math.PI * 2 * (j + 1) / slices;
                quad(vc, m, rgba, at(r, t0, p0), at(r, t0, p1), at(r, t1, p1), at(r, t1, p0));
            }
        }
    }

    private static Vector3f at(float r, double lat, double lon) {
        return new Vector3f((float) (Math.cos(lat) * Math.cos(lon)) * r, (float) Math.sin(lat) * r,
            (float) (Math.cos(lat) * Math.sin(lon)) * r);
    }

    private static void quad(VertexConsumer vc, Matrix4f m, int rgba, Vector3f... p) {
        int r = rgba >>> 24, g = (rgba >>> 16) & 0xFF, b = (rgba >>> 8) & 0xFF, a = rgba & 0xFF;
        for (int k = 0; k < 4; k++) vc.vertex(m, p[k].x, p[k].y, p[k].z).color(r, g, b, a);
        for (int k = 3; k >= 0; k--) vc.vertex(m, p[k].x, p[k].y, p[k].z).color(r, g, b, a);
    }

    @Override
    public Identifier getTexture(CrabCoreBlastEntity entity) {
        return NONE;
    }
}
