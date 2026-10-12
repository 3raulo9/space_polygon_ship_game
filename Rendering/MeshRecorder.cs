using System.Numerics;
using Raylib_cs;

namespace Unrendered.Rendering;

/// <summary>
/// Catches draws instead of making them. While a recorder is active every <see cref="PolyMesh"/>
/// draw — and the few raw primitives the models use, through <see cref="Cylinder"/> and
/// <see cref="Sphere"/> — lands here as a mesh and a world transform. This is how the creator
/// copies a character exactly as the game draws it, rather than as somebody remembered it.
/// </summary>
public sealed class MeshRecorder
{
    public readonly record struct Shot(PolyMesh Mesh, Matrix4x4 World, Color? Tint);

    [ThreadStatic] private static MeshRecorder? _active;
    public static MeshRecorder? Active => _active;

    public List<Shot> Shots { get; } = new();

    /// <summary>The recorder's stand-in for rlgl's matrix stack: what a model pushed there is
    /// applied here instead, so a model scaled on the GPU is recorded at the size it is seen.</summary>
    private readonly Stack<Matrix4x4> _stack = new();
    private Matrix4x4 _top = Matrix4x4.Identity;

    public void Add(PolyMesh mesh, Matrix4x4 world, Color? tint) => Shots.Add(new(mesh, world * _top, tint));

    /// <summary>Scales everything drawn until <see cref="Pop"/> about a point on the ground —
    /// through rlgl normally, through the recorder's own stack while recording.</summary>
    public static void PushScale(Vector2 about, float scale)
    {
        if (_active is { } rec)
        {
            rec._stack.Push(rec._top);
            var m = Matrix4x4.CreateTranslation(-about.X, 0, -about.Y)
                  * Matrix4x4.CreateScale(scale)
                  * Matrix4x4.CreateTranslation(about.X, 0, about.Y);
            rec._top = m * rec._top;
            return;
        }
        Rlgl.PushMatrix();
        Rlgl.Translatef(about.X, 0f, about.Y);
        Rlgl.Scalef(scale, scale, scale);
        Rlgl.Translatef(-about.X, 0f, -about.Y);
    }

    public static void Pop()
    {
        if (_active is { } rec)
        {
            rec._top = rec._stack.Count > 0 ? rec._stack.Pop() : Matrix4x4.Identity;
            return;
        }
        Rlgl.PopMatrix();
    }

    /// <summary>Runs <paramref name="draw"/> with every draw caught, and returns what it drew.</summary>
    public static List<Shot> Record(System.Action draw)
    {
        var rec = new MeshRecorder();
        var outer = _active;
        _active = rec;
        try { draw(); }
        finally { _active = outer; }
        return rec.Shots;
    }

    /// <summary>A cylinder, recorded as a five-sided tapered prism. Returns false when nothing
    /// is recording, and the caller should draw it the ordinary way.</summary>
    public static bool Cylinder(Vector3 from, Vector3 to, float r0, float r1, Color color)
    {
        if (_active == null) return false;
        Vector3 along = to - from;
        float len = along.Length();
        if (len < 1e-5f) return true;
        var m = new PolyMesh();
        const int sides = 5;
        var a = new Vector3[sides];
        var b = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float t = MathF.Tau * i / sides;
            a[i] = new Vector3(MathF.Cos(t) * r0, 0, MathF.Sin(t) * r0);
            b[i] = new Vector3(MathF.Cos(t) * r1, len, MathF.Sin(t) * r1);
        }
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            m.AddFace(color, a[i], a[j], b[j], b[i]);
        }
        m.AddFace(color, b);
        _active.Add(m, AlongY(from, along / len), null);
        return true;
    }

    /// <summary>A sphere, recorded as an octahedron.</summary>
    public static bool Sphere(Vector3 centre, float r, Color color)
    {
        if (_active == null) return false;
        _active.Add(Octahedron(r, color), Matrix4x4.CreateTranslation(centre), null);
        return true;
    }

    public static PolyMesh Octahedron(float r, Color c)
    {
        var m = new PolyMesh();
        Vector3 px = new(r, 0, 0), nx = new(-r, 0, 0), py = new(0, r, 0), ny = new(0, -r, 0), pz = new(0, 0, r), nz = new(0, 0, -r);
        m.AddFace(c, py, pz, px); m.AddFace(c, py, px, nz); m.AddFace(c, py, nz, nx); m.AddFace(c, py, nx, pz);
        m.AddFace(c, ny, px, pz); m.AddFace(c, ny, nz, px); m.AddFace(c, ny, nx, nz); m.AddFace(c, ny, pz, nx);
        return m;
    }

    /// <summary>A frame at <paramref name="origin"/> whose +Y points along <paramref name="dir"/>.</summary>
    private static Matrix4x4 AlongY(Vector3 origin, Vector3 dir)
    {
        Vector3 helper = MathF.Abs(dir.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 x = Vector3.Normalize(Vector3.Cross(helper, dir));
        Vector3 z = Vector3.Cross(x, dir);
        return new Matrix4x4(x.X, x.Y, x.Z, 0, dir.X, dir.Y, dir.Z, 0, z.X, z.Y, z.Z, 0, origin.X, origin.Y, origin.Z, 1);
    }
}
