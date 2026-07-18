using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;

namespace VoidTanks.Rendering;

/// <summary>
/// A flat-shaded convex-face mesh (Doc 02): each face is a single flat colour
/// with no gradient across it, faces meet at hard visible creases, and shading
/// is one crude directional term quantized to a few discrete steps — banded,
/// not smooth. No shadows, no contact grounding: solids float on the grid.
///
/// We draw triangles directly rather than using Raylib's Model pipeline so we
/// keep total control over per-face colour and the fog fade. Geometry is stored
/// in local space and transformed (rotate about Y + translate) at draw time.
/// </summary>
public sealed class PolyMesh
{
    /// <summary>One flat-shaded triangle in the model's local space.</summary>
    private readonly record struct Face(Vector3 A, Vector3 B, Vector3 C, Color BaseColor);

    private readonly List<Face> _faces = new();

    /// <summary>
    /// Add a convex polygon (>=3 coplanar verts, wound counter-clockwise when
    /// viewed from outside) as a fan of triangles sharing one flat colour.
    /// </summary>
    public PolyMesh AddFace(Color color, params Vector3[] verts)
    {
        for (int i = 1; i < verts.Length - 1; i++)
            _faces.Add(new Face(verts[0], verts[i], verts[i + 1], color));
        return this;
    }

    // A fixed light direction (Doc 02): a single crude directional term. Points
    // down and to one side so facets read as distinct planes.
    private static readonly Vector3 LightDir = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.3f));

    /// <summary>
    /// Draws the mesh at a world position, rotated by <paramref name="heading"/>
    /// (radians about Y). Each face gets a single quantized-brightness colour,
    /// then fades toward the fog colour by the model's distance from the camera.
    /// </summary>
    public void Draw(Vector2 position, float heading, float height, Vector2 cameraXZ)
    {
        float cos = MathF.Cos(heading);
        float sin = MathF.Sin(heading);

        float dist = Vector2.Distance(position, cameraXZ);
        float fog = GridRenderer.FogFactor(dist);
        // Fully-fogged: don't draw at all. Just inside the boundary it snaps into
        // existence — the pop-in is authentic and wanted (Doc 02).
        if (fog >= 0.99f) return;

        foreach (var f in _faces)
        {
            Vector3 a = Transform(f.A, cos, sin, position, height);
            Vector3 b = Transform(f.B, cos, sin, position, height);
            Vector3 c = Transform(f.C, cos, sin, position, height);

            Color shaded = ShadeFace(a, b, c, f.BaseColor);
            Color final = GridRenderer.LerpColor(shaded, Palette.Fog, fog);

            Raylib.DrawTriangle3D(a, b, c, final);
        }
    }

    /// <summary>Rotate about Y (heading), then translate to world position.</summary>
    private static Vector3 Transform(Vector3 v, float cos, float sin, Vector2 pos, float height)
    {
        // Heading 0 faces +Z, matching PlayerTank: rotate X/Z about Y.
        float x = v.X * cos + v.Z * sin;
        float z = -v.X * sin + v.Z * cos;
        return new Vector3(pos.X + x, height + v.Y, pos.Y + z);
    }

    /// <summary>
    /// Flat directional shading quantized to 3 discrete brightness steps. The
    /// face normal comes from the transformed triangle, so lighting is per-face
    /// and hard-edged — never interpolated across the surface.
    /// </summary>
    private static Color ShadeFace(Vector3 a, Vector3 b, Vector3 c, Color baseColor)
    {
        Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        float d = Vector3.Dot(normal, -LightDir);      // -1..1
        float lit = Math.Clamp(d, 0f, 1f);

        // Quantize to 3 bands, floored so even lit faces stay a little grim.
        float band = MathF.Floor(lit * 3f) / 3f;       // 0, .33, .66
        float bright = 0.45f + 0.55f * band;           // 0.45 .. 0.82

        return new Color(
            (int)(baseColor.R * bright),
            (int)(baseColor.G * bright),
            (int)(baseColor.B * bright),
            baseColor.A);
    }
}
