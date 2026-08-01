using System.Numerics;
using Raylib_cs;
using Unrendered.Core;

namespace Unrendered.Rendering;

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

    /// <summary>
    /// Add a six-sided box (optionally a frustum: the top rectangle can be a
    /// different half-size than the bottom, for a tapering prism). Spans X in
    /// [-hwB, hwB] at the bottom and [-hwT, hwT] at the top, similarly for Z, and
    /// Y in [y0, y1]. Every face is wound for an outward normal so the flat
    /// shading reads correctly. This is the primitive the stacked tank is built
    /// from — track plates, hull, turret, tapering barrel are all boxes.
    /// </summary>
    public PolyMesh AddBox(Color color,
        float hwB, float hdB, float hwT, float hdT, float y0, float y1)
    {
        // Bottom rectangle (larger for a taper), top rectangle (smaller).
        Vector3 b0 = new(-hwB, y0, -hdB), b1 = new(hwB, y0, -hdB);
        Vector3 b2 = new(hwB, y0, hdB), b3 = new(-hwB, y0, hdB);
        Vector3 t0 = new(-hwT, y1, -hdT), t1 = new(hwT, y1, -hdT);
        Vector3 t2 = new(hwT, y1, hdT), t3 = new(-hwT, y1, hdT);

        AddFace(color, t0, t1, t2, t3);   // top
        AddFace(color, b3, b2, b1, b0);   // bottom (wound downward)
        AddFace(color, b0, b1, t1, t0);   // back  (-Z)
        AddFace(color, b2, b3, t3, t2);   // front (+Z)
        AddFace(color, b3, b0, t0, t3);   // left  (-X)
        AddFace(color, b1, b2, t2, t1);   // right (+X)
        return this;
    }

    /// <summary>Convenience: a straight (non-tapering) box with equal top/bottom.</summary>
    public PolyMesh AddBox(Color color, float hw, float hd, float y0, float y1)
        => AddBox(color, hw, hd, hw, hd, y0, y1);

    /// <summary>
    /// A straight box spanning explicit X/Z ranges (not centred on the axis) —
    /// used for the two side track plates, which sit left and right of centre.
    /// </summary>
    public PolyMesh AddBoxSpan(Color color, float x0, float x1, float z0, float z1, float y0, float y1)
    {
        Vector3 b0 = new(x0, y0, z0), b1 = new(x1, y0, z0);
        Vector3 b2 = new(x1, y0, z1), b3 = new(x0, y0, z1);
        Vector3 t0 = new(x0, y1, z0), t1 = new(x1, y1, z0);
        Vector3 t2 = new(x1, y1, z1), t3 = new(x0, y1, z1);

        AddFace(color, t0, t1, t2, t3);   // top
        AddFace(color, b3, b2, b1, b0);   // bottom
        AddFace(color, b0, b1, t1, t0);   // back  (-Z)
        AddFace(color, b2, b3, t3, t2);   // front (+Z)
        AddFace(color, b3, b0, t0, t3);   // left  (-X)
        AddFace(color, b1, b2, t2, t1);   // right (+X)
        return this;
    }

    // A single crude directional term (Doc 02), pointing down and to one side so facets read
    // as distinct planes. Fixed on four of the five worlds — and on SOLUNE it swings with the
    // sun, so a facet genuinely lights from the east at dawn and from overhead at noon.
    private static Vector3 LightDir => Atmosphere.LightDir;

    /// <summary>
    /// Draws the mesh at a world position, rotated by <paramref name="heading"/>
    /// (radians about Y). Each face gets a single quantized-brightness colour,
    /// then fades toward the fog colour by the model's distance from the camera.
    /// Backface culling is left disabled by the Renderer so every facet draws;
    /// shading is two-sided (see <see cref="ShadeFace"/>) so a solid reads solid
    /// no matter which way each triangle happens to be wound.
    /// </summary>
    public void Draw(Vector2 position, float heading, float height, Vector3 cameraPos,
        float scale = 1f, Color? tint = null, float pitch = 0f, float roll = 0f)
    {
        float cos = MathF.Cos(heading);
        float sin = MathF.Sin(heading);

        var cameraXZ = new Vector2(cameraPos.X, cameraPos.Z);
        float dist = Vector2.Distance(position, cameraXZ);
        float fog = GridRenderer.FogFactor(dist);
        // Fully-fogged: don't draw at all. Just inside the boundary it snaps into
        // existence — the pop-in is authentic and wanted (Doc 02).
        if (fog >= 0.99f) return;

        foreach (var f in _faces)
        {
            Vector3 a = Transform(f.A, cos, sin, position, height, scale, pitch, roll);
            Vector3 b = Transform(f.B, cos, sin, position, height, scale, pitch, roll);
            Vector3 c = Transform(f.C, cos, sin, position, height, scale, pitch, roll);

            // A per-instance tint (used by short-lived debris that fade as they
            // die) overrides the face's built-in colour before shading.
            Color shaded = ShadeFace(a, b, c, tint ?? f.BaseColor, cameraPos);
            Color final = GridRenderer.LerpColor(shaded, Atmosphere.FogColor, fog);

            Raylib.DrawTriangle3D(a, b, c, final);
        }
    }

    /// <summary>
    /// Draws the mesh as an <em>outline</em>: the solid filled in the void's own near-black
    /// so it occludes whatever is behind it, and every edge stroked in
    /// <paramref name="edge"/> over the top.
    ///
    /// This is how the world looks to a thing with no eyes. The exposed VIRUS mote does not
    /// see matter — it sees movement, as a shape with a bright wire around it and nothing
    /// inside — and at 320×240 a black body with white creases reads as a solid object
    /// perceived by something that isn't using light far better than any shading trick
    /// would. The fill is what makes it read as a <em>body</em> rather than as a cloud of
    /// lines: without it you see every edge of the far side through the near side and the
    /// shape collapses into noise.
    ///
    /// No fog fade, and deliberately: what perceives this is not looking through air.
    /// </summary>
    public void DrawWire(Vector2 position, float heading, float height, Vector3 cameraPos,
        float scale, Color edge, float pitch = 0f, float roll = 0f)
    {
        var cameraXZ = new Vector2(cameraPos.X, cameraPos.Z);
        if (Vector2.Distance(position, cameraXZ) > UnseenRange) return;

        float cos = MathF.Cos(heading);
        float sin = MathF.Sin(heading);

        foreach (var f in _faces)
        {
            Vector3 a = Transform(f.A, cos, sin, position, height, scale, pitch, roll);
            Vector3 b = Transform(f.B, cos, sin, position, height, scale, pitch, roll);
            Vector3 c = Transform(f.C, cos, sin, position, height, scale, pitch, roll);

            Raylib.DrawTriangle3D(a, b, c, Hollow);
            Raylib.DrawLine3D(a, b, edge);
            Raylib.DrawLine3D(b, c, edge);
            Raylib.DrawLine3D(c, a, edge);
        }
    }

    /// <summary>How far a moving thing can be perceived without eyes. Shorter than the fog,
    /// which is the point: unhosted, the world shrinks to whatever is close enough to feel.</summary>
    public const float UnseenRange = 95f;

    /// <summary>What fills an outlined solid. Not quite the void, so one shape overlapping
    /// another still separates from it.</summary>
    private static readonly Color Hollow = new(3, 4, 7, 255);

    /// <summary>
    /// Scale about the model origin, tip it on its own axes, rotate about Y
    /// (heading), then translate to world position.
    ///
    /// The tilt runs <em>before</em> the heading so both angles are read in the
    /// model's own frame: <paramref name="roll"/> tips it onto one side (about its
    /// local Z, the nose/tail axis) and <paramref name="pitch"/> noses it down
    /// (about its local X). Applied in that order, a part that is both rolled and
    /// pitched leans the way a body braced on one side and aiming downward does,
    /// rather than swinging through a second axis that has already moved. Both are
    /// zero for everything but the Crab-Core lining up its lance, so every other
    /// caller transforms exactly as it always has.
    /// </summary>
    private static Vector3 Transform(Vector3 v, float cos, float sin, Vector2 pos,
        float height, float scale, float pitch = 0f, float roll = 0f)
    {
        v *= scale;

        if (roll != 0f)
        {
            float c = MathF.Cos(roll), s = MathF.Sin(roll);
            v = new Vector3(v.X * c - v.Y * s, v.X * s + v.Y * c, v.Z);
        }
        if (pitch != 0f)
        {
            float c = MathF.Cos(pitch), s = MathF.Sin(pitch);
            v = new Vector3(v.X, v.Y * c - v.Z * s, v.Y * s + v.Z * c);
        }

        // Heading 0 faces +Z, matching PlayerTank: rotate X/Z about Y.
        float x = v.X * cos + v.Z * sin;
        float z = -v.X * sin + v.Z * cos;
        return new Vector3(pos.X + x, height + v.Y, pos.Y + z);
    }

    /// <summary>
    /// Flat directional shading quantized to 3 discrete brightness steps. The
    /// face normal comes from the transformed triangle, so lighting is per-face
    /// and hard-edged — never interpolated across the surface.
    ///
    /// Shading is two-sided: the normal is flipped to face the camera before
    /// lighting, so a facet lights as the solid surface it is regardless of its
    /// triangle winding. This is what keeps a closed mesh reading as a solid
    /// block instead of an inside-out, folded-paper shell.
    /// </summary>
    private static Color ShadeFace(Vector3 a, Vector3 b, Vector3 c, Color baseColor, Vector3 cameraPos)
    {
        Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));

        // Orient the normal toward the viewer so back-wound facets don't light as
        // if inverted (the "unwrapped paper" look).
        Vector3 centroid = (a + b + c) / 3f;
        if (Vector3.Dot(normal, cameraPos - centroid) < 0f) normal = -normal;

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
