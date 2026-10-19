package net.unrendered.rift.client;

import com.mojang.blaze3d.systems.RenderSystem;
import net.fabricmc.fabric.api.client.rendering.v1.WorldRenderContext;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.render.BufferBuilder;
import net.minecraft.client.render.BufferRenderer;
import net.minecraft.client.render.Camera;
import net.minecraft.client.render.GameRenderer;
import net.minecraft.client.render.Tessellator;
import net.minecraft.client.render.VertexFormat;
import net.minecraft.client.render.VertexFormats;
import com.mojang.blaze3d.systems.VertexSorter;
import net.minecraft.client.texture.NativeImage;
import net.minecraft.client.texture.NativeImageBackedTexture;
import net.minecraft.util.Identifier;
import net.unrendered.rift.RiftMod;
import org.joml.Matrix4f;
import org.joml.Matrix4fStack;
import org.joml.Vector3f;

import java.util.Random;

/**
 * UNRENDERED's sky, painted the way UNRENDERED paints it: a 240-pixel-tall band dithered
 * through six levels from its top colour to the horizon, a sun and a moon as hard little
 * polygons, and its drifting stars at night. A port of SkyRenderer.cs, fed the colours
 * UNRENDERED is under right now.
 */
public final class SkyPainter {
    private static final int H = 240;
    private static final int LEVELS = 6;
    private static final int[] BAYER = {0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5};
    private static final float[][] STARS = new float[70][3];
    private static final Identifier TEX = RiftMod.id("dynamic/sky");

    static {
        Random rng = new Random(1337);
        for (float[] s : STARS) {
            float y = (float) (rng.nextDouble() * rng.nextDouble());
            s[0] = (float) rng.nextDouble();
            s[1] = y;
            s[2] = 0.35f + (float) rng.nextDouble() * 0.65f;
        }
    }

    private static NativeImageBackedTexture texture;
    private static int width;

    private SkyPainter() {}

    private static int lerp(int[] a, int[] b, float t, int i) {
        return (int) (a[i] + (b[i] - a[i]) * t);
    }

    public static void render(WorldRenderContext ctx) {
        MinecraftClient mc = MinecraftClient.getInstance();
        SceneRenderer.Sky sky = SceneRenderer.sky;
        if (sky == null) {
            // Before the first frame: UNRENDERED's void.
            sky = new SceneRenderer.Sky(new int[]{5, 7, 10, 255}, new int[]{30, 12, 40, 255}, new int[]{60, 20, 70, 255},
                new int[]{5, 7, 10, 255}, 0.5f, 0, -1, 0, -1, 0, -1, 0, 1);
        }
        int w = Math.max(64, Math.round(H * (float) mc.getWindow().getFramebufferWidth() / Math.max(1, mc.getWindow().getFramebufferHeight())));
        if (texture == null || w != width) {
            if (texture != null) texture.close();
            texture = new NativeImageBackedTexture(new NativeImage(w, H, false));
            mc.getTextureManager().registerTexture(TEX, texture);
            width = w;
        }
        NativeImage img = texture.getImage();
        if (img == null) return;

        Camera camera = ctx.camera();
        float yawR = (float) Math.toRadians(camera.getYaw());
        float pitchR = (float) Math.toRadians(camera.getPitch());
        double fov = Math.toRadians(mc.options.getFov().getValue());
        float focal = (float) ((H / 2.0) / Math.tan(fov / 2.0));
        int horizonY = Math.round(H / 2f - focal * (float) Math.tan(pitchR));
        horizonY = Math.max(0, Math.min(H, horizonY));

        for (int y = 0; y < H; y++) {
            for (int x = 0; x < w; x++) {
                int r, g, b;
                if (y < horizonY) {
                    float f = (float) y / horizonY;
                    float t = (float) Math.pow(f, 6.5);
                    float level = t * LEVELS;
                    int lo = (int) level;
                    float frac = level - lo;
                    float thr = (BAYER[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;
                    float tq = (frac > thr ? lo + 1 : lo) / (float) LEVELS;
                    if (tq < 0.5f) {
                        r = lerp(sky.top(), sky.mid(), tq * 2, 0);
                        g = lerp(sky.top(), sky.mid(), tq * 2, 1);
                        b = lerp(sky.top(), sky.mid(), tq * 2, 2);
                    } else {
                        r = lerp(sky.mid(), sky.horizon(), (tq - 0.5f) * 2, 0);
                        g = lerp(sky.mid(), sky.horizon(), (tq - 0.5f) * 2, 1);
                        b = lerp(sky.mid(), sky.horizon(), (tq - 0.5f) * 2, 2);
                    }
                } else {
                    r = sky.horizon()[0];
                    g = sky.horizon()[1];
                    b = sky.horizon()[2];
                }
                img.setColor(x, y, abgr(r, g, b, 255));
            }
        }

        if (horizonY > 0) {
            Vector3f fwd = new Vector3f((float) (-Math.sin(yawR) * Math.cos(pitchR)), (float) -Math.sin(pitchR),
                (float) (Math.cos(yawR) * Math.cos(pitchR)));
            Vector3f right = new Vector3f(fwd).cross(0, 1, 0);
            if (right.lengthSquared() < 1e-6f) right.set(1, 0, 0);
            right.normalize();
            Vector3f up = new Vector3f(right).cross(fwd).normalize();
            body(img, w, horizonY, focal, fwd, right, up, sky.sunAlt(), sky.sunAz(), 9, 226, 196, 150);
            body(img, w, horizonY, focal, fwd, right, up, sky.moonAlt(), sky.moonAz(), 7, 176, 190, 198);

            float starlight = sky.sunAlt() <= -1f ? 1f : sky.night();
            if (starlight > 0.02f) {
                float drift = (System.currentTimeMillis() % 1_000_000L) / 1000f * 1.6f;
                for (float[] s : STARS) {
                    float px = s[0] * w + drift * (0.5f + s[2]);
                    px = ((px % w) + w) % w;
                    int y = (int) (s[1] * horizonY);
                    float glow = (float) y / horizonY;
                    float a = s[2] * glow * starlight;
                    int val = 150 + (int) (105 * s[2]);
                    blend(img, (int) px, y, val, val, Math.min(255, val + 20), a);
                }
            }
        }
        texture.upload();

        Matrix4f projection = new Matrix4f(RenderSystem.getProjectionMatrix());
        VertexSorter sorting = RenderSystem.getVertexSorting();
        RenderSystem.setProjectionMatrix(new Matrix4f(), VertexSorter.BY_Z);
        Matrix4fStack mv = RenderSystem.getModelViewStack();
        mv.pushMatrix();
        mv.identity();
        RenderSystem.applyModelViewMatrix();
        RenderSystem.depthMask(false);
        RenderSystem.disableDepthTest();
        RenderSystem.disableBlend();
        RenderSystem.setShader(GameRenderer::getPositionTexProgram);
        RenderSystem.setShaderTexture(0, TEX);
        RenderSystem.setShaderColor(1f, 1f, 1f, 1f);
        BufferBuilder bb = Tessellator.getInstance().begin(VertexFormat.DrawMode.QUADS, VertexFormats.POSITION_TEXTURE);
        bb.vertex(-1f, -1f, 0f).texture(0f, 1f);
        bb.vertex(1f, -1f, 0f).texture(1f, 1f);
        bb.vertex(1f, 1f, 0f).texture(1f, 0f);
        bb.vertex(-1f, 1f, 0f).texture(0f, 0f);
        BufferRenderer.drawWithGlobalProgram(bb.end());
        mv.popMatrix();
        RenderSystem.applyModelViewMatrix();
        RenderSystem.setProjectionMatrix(projection, sorting);
        RenderSystem.enableDepthTest();
        RenderSystem.depthMask(true);
    }

    /** The sun or moon: a filled polygon at its bearing, sunk into nothing below the horizon. */
    private static void body(NativeImage img, int w, int horizonY, float focal, Vector3f fwd, Vector3f right, Vector3f up,
                             float altitude, float azimuth, int radius, int r, int g, int b) {
        if (altitude <= 0.02f) return;
        float flat = (float) Math.sqrt(Math.max(0f, 1f - altitude * altitude));
        Vector3f d = new Vector3f((float) Math.sin(azimuth) * flat, altitude, (float) Math.cos(azimuth) * flat);
        float along = d.dot(fwd);
        if (along <= 0.05f) return;
        float sx = w / 2f + focal * d.dot(right) / along;
        float sy = H / 2f - focal * d.dot(up) / along;
        if (sx < -radius || sx > w + radius || sy > horizonY + radius) return;
        // Eight sides, turned 22.5 degrees, as UNRENDERED's DrawPoly.
        float apothem = (float) (radius * Math.cos(Math.PI / 8));
        for (int y = (int) (sy - radius); y <= sy + radius; y++) {
            for (int x = (int) (sx - radius); x <= sx + radius; x++) {
                if (x < 0 || y < 0 || x >= w || y >= H) continue;
                float dx = x + 0.5f - sx, dy = y + 0.5f - sy;
                boolean inside = true;
                for (int k = 0; k < 8 && inside; k++) {
                    double a = Math.PI / 4 * k;
                    inside = dx * Math.cos(a) + dy * Math.sin(a) <= apothem;
                }
                if (inside) img.setColor(x, y, abgr(r, g, b, 255));
            }
        }
    }

    private static void blend(NativeImage img, int x, int y, int r, int g, int b, float a) {
        if (x < 0 || y < 0 || x >= img.getWidth() || y >= img.getHeight() || a <= 0f) return;
        int under = img.getColor(x, y);
        int ur = under & 0xFF, ug = (under >> 8) & 0xFF, ub = (under >> 16) & 0xFF;
        a = Math.min(1f, a);
        img.setColor(x, y, abgr((int) (ur + (r - ur) * a), (int) (ug + (g - ug) * a), (int) (ub + (b - ub) * a), 255));
    }

    private static int abgr(int r, int g, int b, int a) {
        return (a << 24) | (b << 16) | (g << 8) | r;
    }
}
