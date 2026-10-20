package net.unrendered.rift.client;

import net.minecraft.client.render.VertexConsumer;
import net.unrendered.rift.UnrModels;
import org.joml.Matrix4f;

/**
 * Draws one of UNRENDERED's meshes the way UNRENDERED's PolyMesh draws it: scaled about its
 * origin, turned by a heading about Y (heading 0 faces +Z), and every triangle one flat
 * colour, lit by one directional term quantised to three bands and floored so even a lit
 * face stays a little grim. The shading is worked out in UNRENDERED's own frame, against
 * UNRENDERED's own light and the eye it would be seen from, so the colours are the ones
 * UNRENDERED puts on screen.
 */
public final class UnrMeshes {
    private UnrMeshes() {}

    /**
     * @param eye    where the viewer stands, in the mesh's frame after the turn and lift
     * @param lift   subtracted from Y after scaling (the icon turns about its centre)
     * @param tint   if non-zero, the RGBA every face takes instead of its own (before shading)
     */
    public static void emit(VertexConsumer vc, Matrix4f m, UnrModels.Mesh mesh, float scale, float heading,
                            float lift, float ex, float ey, float ez, int tint, float alpha) {
        if (mesh == null) return;
        float[] v = mesh.v();
        int[] c = mesh.c();
        float cos = (float) Math.cos(heading), sin = (float) Math.sin(heading);
        float[] l = UnrModels.light();
        float[] p = new float[9];
        for (int t = 0; t < c.length && t * 9 + 8 < v.length; t++) {
            for (int k = 0; k < 3; k++) {
                float x = v[t * 9 + k * 3] * scale, y = v[t * 9 + k * 3 + 1] * scale, z = v[t * 9 + k * 3 + 2] * scale;
                p[k * 3] = x * cos + z * sin;
                p[k * 3 + 1] = y - lift;
                p[k * 3 + 2] = -x * sin + z * cos;
            }
            int rgba = tint != 0 ? tint : c[t];
            float bright = bright(p, ex, ey, ez, l);
            int r = (int) (((rgba >>> 24) & 0xFF) * bright);
            int g = (int) (((rgba >>> 16) & 0xFF) * bright);
            int b = (int) (((rgba >>> 8) & 0xFF) * bright);
            int a = (int) ((rgba & 0xFF) * alpha);
            // As a quad with its last corner doubled, in both windings: UNRENDERED's meshes
            // are two-sided.
            int[] order = {0, 1, 2, 2, 2, 2, 1, 0};
            for (int k : order) vc.vertex(m, p[k * 3], p[k * 3 + 1], p[k * 3 + 2]).color(r, g, b, a);
        }
    }

    /** PolyMesh.ShadeFace. */
    private static float bright(float[] p, float ex, float ey, float ez, float[] l) {
        float ux = p[3] - p[0], uy = p[4] - p[1], uz = p[5] - p[2];
        float wx = p[6] - p[0], wy = p[7] - p[1], wz = p[8] - p[2];
        float nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
        float len = (float) Math.sqrt(nx * nx + ny * ny + nz * nz);
        if (len < 1e-9f) return 0.45f;
        nx /= len; ny /= len; nz /= len;
        float cx = (p[0] + p[3] + p[6]) / 3f, cy = (p[1] + p[4] + p[7]) / 3f, cz = (p[2] + p[5] + p[8]) / 3f;
        if (nx * (ex - cx) + ny * (ey - cy) + nz * (ez - cz) < 0f) { nx = -nx; ny = -ny; nz = -nz; }
        float d = -(nx * l[0] + ny * l[1] + nz * l[2]);
        float lit = Math.max(0f, Math.min(1f, d));
        float band = (float) Math.floor(lit * 3f) / 3f;
        return 0.45f + 0.55f * band;
    }
}
