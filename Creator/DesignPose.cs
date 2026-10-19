using System.Numerics;
using Raylib_cs;

namespace Unrendered.Creator;

/// <summary>
/// Turns a design plus a moment in one of its clips into a world transform for every part,
/// and answers what the mouse is over. One implementation shared by drawing, picking and
/// dropping, so what you click is exactly what you see and a part lands exactly where the
/// cursor says it will.
/// </summary>
public static class DesignPose
{
    /// <summary>
    /// World matrix per part id. <paramref name="clip"/> may be null for the rest pose;
    /// <paramref name="t"/> is normalised clip time, 0..1. <paramref name="root"/> places
    /// the whole creation in the world.
    /// </summary>
    public static Dictionary<int, Matrix4x4> Evaluate(Design d, Clip? clip, float t, Matrix4x4 root)
    {
        // Sum every track per (part, channel) first: two tracks on the same channel add,
        // which is how a hand-made wobble layers over a built-in gait.
        var add = new Dictionary<int, (Vector3 Rot, Vector3 Move, Vector3 Stretch)>();
        if (clip != null)
        {
            foreach (var tr in clip.Tracks)
            {
                float v = tr.Sample(t, clip.Loop);
                add.TryGetValue(tr.Part, out var cur);
                switch (tr.Channel)
                {
                    case Channel.Pitch: cur.Rot.X += v; break;
                    case Channel.Yaw: cur.Rot.Y += v; break;
                    case Channel.Roll: cur.Rot.Z += v; break;
                    case Channel.MoveX: cur.Move.X += v; break;
                    case Channel.MoveY: cur.Move.Y += v; break;
                    case Channel.MoveZ: cur.Move.Z += v; break;
                    case Channel.StretchX: cur.Stretch.X += v; break;
                    case Channel.StretchY: cur.Stretch.Y += v; break;
                    case Channel.StretchZ: cur.Stretch.Z += v; break;
                }
                add[tr.Part] = cur;
            }
        }

        // Parts hang off each other's frames; an animated stretch sizes only the part's own
        // blocks, so it is folded into what is returned but not into what children inherit.
        var frames = new Dictionary<int, Matrix4x4>(d.Parts.Count);
        var world = new Dictionary<int, Matrix4x4>(d.Parts.Count);
        foreach (var p in d.TreeOrder())
        {
            add.TryGetValue(p.Id, out var a);
            var parent = p.Parent >= 0 ? d.Find(p.Parent) : null;
            var parentM = parent != null && frames.TryGetValue(parent.Id, out var pm) ? pm : root;
            var frame = Local(p, parent, a.Rot, a.Move) * parentM;
            frames[p.Id] = frame;
            world[p.Id] = a.Stretch == Vector3.Zero
                ? frame
                : Matrix4x4.CreateScale(Vector3.Max(Vector3.One + a.Stretch, new Vector3(0.01f))) * frame;
        }
        return world;
    }

    /// <summary>A part in its parent's frame: sized, turned about its mounting, stood on its
    /// mounting, and carried to its joint — which rides the parent's stretch, so a part on a
    /// stretched body stays on the body's surface.</summary>
    public static Matrix4x4 Local(Part p, Part? parent, Vector3 addRot = default, Vector3 addMove = default)
    {
        Vector3 rot = p.Rest + addRot;
        Vector3 at = p.Pivot * (parent?.Stretch ?? Vector3.One) + addMove;
        return Matrix4x4.CreateScale(p.Scale)
             * Matrix4x4.CreateFromYawPitchRoll(rot.Y, rot.X, rot.Z)
             * Align(p.Mount)
             * Matrix4x4.CreateTranslation(at);
    }

    /// <summary>The shortest turn that takes +Y onto <paramref name="up"/>.</summary>
    public static Matrix4x4 Align(Vector3 up)
    {
        if (up.LengthSquared() < 1e-8f) return Matrix4x4.Identity;
        up = Vector3.Normalize(up);
        float d = Vector3.Dot(Vector3.UnitY, up);
        if (d > 0.99999f) return Matrix4x4.Identity;
        if (d < -0.99999f) return Matrix4x4.CreateRotationX(MathF.PI);
        Vector3 axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, up));
        return Matrix4x4.CreateFromAxisAngle(axis, MathF.Acos(d));
    }

    /// <summary>A block's own transform: its mesh stretched to its size, set at its centre,
    /// with the part's stretch over the top.</summary>
    public static Matrix4x4 BlockMatrix(Part p, Block b, Matrix4x4 part)
    {
        var m = Matrix4x4.CreateTranslation(b.Center) * Matrix4x4.CreateScale(p.Stretch) * part;
        if (b.Shape == BlockShape.Mesh) m = Matrix4x4.CreateScale(b.MeshScale) * m;
        return m;
    }

    /// <summary>
    /// Turns a part about its own joint by <paramref name="turn"/>, a rotation given in the
    /// world — how a limb is bent by dragging it. The joint itself does not move; the part and
    /// everything hung from it swing round it. <paramref name="parentWorld"/> is the parent's
    /// world transform in the pose being edited. Returns false if the frames will not invert.
    /// </summary>
    public static bool TurnAtJoint(Part p, Matrix4x4 parentWorld, Matrix4x4 turn)
    {
        // The rest angle lives inside the mounting and the parent's frame, so the world turn
        // is carried across into it: rest' = rest · A · L · turn · L⁻¹ · A⁻¹.
        var parent = parentWorld;
        parent.Translation = Vector3.Zero;
        var align = Align(p.Mount);
        if (!Matrix4x4.Invert(parent, out var parentInv) || !Matrix4x4.Invert(align, out var alignInv)) return false;
        var rest = Matrix4x4.CreateFromYawPitchRoll(p.Rest.Y, p.Rest.X, p.Rest.Z);
        p.Rest = ToEuler(Orthonormal(rest * align * parent * turn * parentInv * alignInv));
        return true;
    }

    /// <summary>Squeezes the scale out of a matrix that should be a pure turn but has picked
    /// up a parent's size on the way through.</summary>
    private static Matrix4x4 Orthonormal(Matrix4x4 m)
    {
        var x = Vector3.Normalize(new Vector3(m.M11, m.M12, m.M13));
        var y = new Vector3(m.M21, m.M22, m.M23);
        y = Vector3.Normalize(y - x * Vector3.Dot(x, y));
        var z = Vector3.Cross(x, y);
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
    }

    /// <summary>Breaks a rigid rotation back into the yaw, pitch and roll
    /// <see cref="Matrix4x4.CreateFromYawPitchRoll"/> would build it from.</summary>
    public static Vector3 ToEuler(Matrix4x4 m)
    {
        float pitch = MathF.Asin(Math.Clamp(-m.M32, -1f, 1f));
        float yaw, roll;
        if (MathF.Abs(m.M32) < 0.9999f)
        {
            yaw = MathF.Atan2(m.M31, m.M33);
            roll = MathF.Atan2(m.M12, m.M22);
        }
        else
        {
            // Pitched straight up or down: yaw and roll turn about the same axis, so put it
            // all in yaw.
            yaw = MathF.Atan2(-m.M13, m.M11);
            roll = 0f;
        }
        return new Vector3(pitch, yaw, roll);
    }

    // =========================================================================
    //  Picking
    // =========================================================================

    public readonly record struct Hit(int Part, Vector3 Point, Vector3 Normal, float Distance);

    /// <summary>
    /// The nearest surface along a ray: which part, where, and which way the surface faces
    /// (toward the ray). Parts in <paramref name="ignore"/> are see-through — a part being
    /// dragged is never under its own cursor.
    /// </summary>
    public static Hit? Raycast(Design d, Dictionary<int, Matrix4x4> world, Vector3 origin, Vector3 dir,
        ISet<int>? ignore = null)
    {
        Hit? best = null;
        foreach (var p in d.Parts)
        {
            if (ignore != null && ignore.Contains(p.Id)) continue;
            if (!world.TryGetValue(p.Id, out var pm)) continue;
            foreach (var b in p.Blocks)
            {
                var bm = BlockMatrix(p, b, pm);
                foreach (var (a0, b0, c0, _) in BlockGeometry.Solid(b))
                {
                    Vector3 a = Vector3.Transform(a0, bm), bb = Vector3.Transform(b0, bm), c = Vector3.Transform(c0, bm);
                    if (!RayTriangle(origin, dir, a, bb, c, out float dist)) continue;
                    if (best is { } cur && cur.Distance <= dist) continue;
                    Vector3 n = Vector3.Cross(bb - a, c - a);
                    if (n.LengthSquared() < 1e-12f) continue;
                    n = Vector3.Normalize(n);
                    if (Vector3.Dot(n, dir) > 0f) n = -n;
                    best = new Hit(p.Id, origin + dir * dist, n, dist);
                }
            }
        }
        return best;
    }

    private static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        // Möller–Trumbore, two-sided.
        t = 0f;
        Vector3 e1 = b - a, e2 = c - a;
        Vector3 p = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-9f) return false;
        float inv = 1f / det;
        Vector3 s = o - a;
        float u = Vector3.Dot(s, p) * inv;
        if (u < 0f || u > 1f) return false;
        Vector3 q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(d, q) * inv;
        if (v < 0f || u + v > 1f) return false;
        t = Vector3.Dot(e2, q) * inv;
        return t > 1e-4f;
    }

    /// <summary>The bounds of a part's blocks in the part's own frame, stretch included.</summary>
    public static (Vector3 Min, Vector3 Max) PartBounds(Part p)
    {
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var b in p.Blocks)
        {
            var m = BlockMatrix(p, b, Matrix4x4.Identity);
            foreach (var (a, bb, c, _) in BlockGeometry.Solid(b))
                foreach (var v in new[] { a, bb, c })
                {
                    var w = Vector3.Transform(v, m);
                    lo = Vector3.Min(lo, w);
                    hi = Vector3.Max(hi, w);
                }
        }
        if (lo.X > hi.X) return (Vector3.Zero, Vector3.Zero);
        return (lo, hi);
    }
}

/// <summary>
/// The triangles of one block, in the block's own frame, with their colours. The single
/// source of a block's shape: the renderer draws these, picking hits these.
///
/// <para>Two views of the same block. <see cref="Triangles"/> is what is drawn, cut finer
/// where a paint pattern needs smaller flats to fall on; <see cref="Solid"/> is the bare
/// shape, which is all picking and measuring need and is many times cheaper to ray-test.
/// An unpatterned block is the same list for both.</para>
/// </summary>
public static class BlockGeometry
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Block, Cache> _cache = new();

    private sealed class Cache
    {
        public int Key;
        public List<(Vector3, Vector3, Vector3, Color)> Solid = new();
        public List<(Vector3, Vector3, Vector3, Color)> Painted = new();
    }

    public static List<(Vector3 A, Vector3 B, Vector3 C, Color Color)> Triangles(Block b) => Get(b).Painted;

    public static List<(Vector3 A, Vector3 B, Vector3 C, Color Color)> Solid(Block b) => Get(b).Solid;

    private static Cache Get(Block b)
    {
        int key = HashCode.Combine(b.Shape, b.Size, b.Taper, b.Swatch, b.Tris, b.Swatch2, b.Pattern);
        var cache = _cache.GetOrCreateValue(b);
        if (cache.Key == key && cache.Solid.Count > 0) return cache;
        cache.Key = key;
        cache.Solid = Build(b);
        cache.Painted = b.Pattern == PaintPattern.Solid ? cache.Solid : Paint(b, cache.Solid);
        return cache;
    }

    private static List<(Vector3, Vector3, Vector3, Color)> Build(Block b)
    {
        var list = new List<(Vector3, Vector3, Vector3, Color)>();
        Color c = b.Swatch >= 0 ? Paints.Color(b.Swatch) : Core.Palette.HudChrome;
        float hx = b.Size.X * 0.5f, hy = b.Size.Y * 0.5f, hz = b.Size.Z * 0.5f;

        void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            list.Add((p0, p1, p2, c));
            list.Add((p0, p2, p3, c));
        }

        switch (b.Shape)
        {
            case BlockShape.Mesh when b.Tris != null && b.Colors != null:
                for (int i = 0, f = 0; i + 8 < b.Tris.Length; i += 9, f++)
                {
                    var t = b.Tris;
                    Color col = b.Swatch >= 0 ? c : Block.Unpack(b.Colors[f]);
                    list.Add((new(t[i], t[i + 1], t[i + 2]), new(t[i + 3], t[i + 4], t[i + 5]), new(t[i + 6], t[i + 7], t[i + 8]), col));
                }
                break;
            case BlockShape.Wedge:
            {
                Vector3 b0 = new(-hx, -hy, -hz), b1 = new(hx, -hy, -hz), b2 = new(hx, -hy, hz), b3 = new(-hx, -hy, hz);
                Vector3 t0 = new(-hx, hy, -hz), t1 = new(hx, hy, -hz);
                Quad(b3, b2, b1, b0);
                Quad(b0, b1, t1, t0);
                Quad(t0, t1, b2, b3);
                list.Add((b3, b0, t0, c));
                list.Add((b1, b2, t1, c));
                break;
            }
            case BlockShape.Octa:
            {
                Vector3 px = new(hx, 0, 0), nx = new(-hx, 0, 0), py = new(0, hy, 0), ny = new(0, -hy, 0), pz = new(0, 0, hz), nz = new(0, 0, -hz);
                list.Add((py, pz, px, c)); list.Add((py, px, nz, c)); list.Add((py, nz, nx, c)); list.Add((py, nx, pz, c));
                list.Add((ny, px, pz, c)); list.Add((ny, nz, px, c)); list.Add((ny, nx, nz, c)); list.Add((ny, pz, nx, c));
                break;
            }
            default:
            {
                float tx = hx * b.Taper, tz = hz * b.Taper;
                Vector3 b0 = new(-hx, -hy, -hz), b1 = new(hx, -hy, -hz), b2 = new(hx, -hy, hz), b3 = new(-hx, -hy, hz);
                Vector3 t0 = new(-tx, hy, -tz), t1 = new(tx, hy, -tz), t2 = new(tx, hy, tz), t3 = new(-tx, hy, tz);
                Quad(t0, t1, t2, t3);
                Quad(b3, b2, b1, b0);
                Quad(b0, b1, t1, t0);
                Quad(b2, b3, t3, t2);
                Quad(b3, b0, t0, t3);
                Quad(b1, b2, t2, t1);
                return list;
            }
        }

        // Every other shape tapers too — a copied game mesh as much as a gem — by pinching
        // its top toward its middle, the way a box's top face shrinks.
        if (MathF.Abs(b.Taper - 1f) > 1e-4f && list.Count > 0) Taper(list, b.Taper);
        return list;
    }

    private static void Taper(List<(Vector3, Vector3, Vector3, Color)> list, float taper)
    {
        var (lo, hi) = Bounds(list);
        Vector3 mid = (lo + hi) * 0.5f;
        float span = MathF.Max(1e-5f, hi.Y - lo.Y);
        Vector3 Pinch(Vector3 v)
        {
            float k = 1f + (taper - 1f) * Math.Clamp((v.Y - lo.Y) / span, 0f, 1f);
            return new Vector3(mid.X + (v.X - mid.X) * k, v.Y, mid.Z + (v.Z - mid.Z) * k);
        }
        for (int i = 0; i < list.Count; i++)
        {
            var (a, bb, c, col) = list[i];
            list[i] = (Pinch(a), Pinch(bb), Pinch(c), col);
        }
    }

    private static (Vector3 Lo, Vector3 Hi) Bounds(List<(Vector3, Vector3, Vector3, Color)> list)
    {
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var (a, bb, c, _) in list)
        {
            lo = Vector3.Min(lo, Vector3.Min(a, Vector3.Min(bb, c)));
            hi = Vector3.Max(hi, Vector3.Max(a, Vector3.Max(bb, c)));
        }
        return (lo, hi);
    }

    /// <summary>The finest a pattern cuts a block: a facet is quartered until it is under a
    /// fifth of the block across, three times at most.</summary>
    private const int MaxCuts = 3;

    /// <summary>
    /// Lays the block's pattern over it: each triangle is split into four until it is small
    /// enough for a band or a spot to land on, and each piece takes the mark or keeps its
    /// own colour depending on where it sits. The pieces are coplanar with the face they came
    /// from, so the shape is untouched — only the flats of colour change.
    /// </summary>
    private static List<(Vector3, Vector3, Vector3, Color)> Paint(Block b, List<(Vector3, Vector3, Vector3, Color)> solid)
    {
        var (lo, hi) = Bounds(solid);
        Vector3 size = hi - lo;
        float cell = MathF.Max(size.X, MathF.Max(size.Y, size.Z)) / 5f;
        Color mark = Paints.Color(b.Swatch2);
        var result = new List<(Vector3, Vector3, Vector3, Color)>(solid.Count * 16);

        static float U(float v, float l, float s) => s > 1e-4f ? Math.Clamp((v - l) / s, 0f, 1f) : 0.5f;

        void Emit(Vector3 a, Vector3 bb, Vector3 c, Color own, Vector3 n, int depth)
        {
            float longest = MathF.Max(Vector3.Distance(a, bb), MathF.Max(Vector3.Distance(bb, c), Vector3.Distance(c, a)));
            if (depth < MaxCuts && longest > cell)
            {
                Vector3 ab = (a + bb) * 0.5f, bc = (bb + c) * 0.5f, ca = (c + a) * 0.5f;
                Emit(a, ab, ca, own, n, depth + 1);
                Emit(ab, bb, bc, own, n, depth + 1);
                Emit(ca, bc, c, own, n, depth + 1);
                Emit(ab, bc, ca, own, n, depth + 1);
                return;
            }
            Vector3 mid = (a + bb + c) / 3f;
            var u = new Vector3(U(mid.X, lo.X, size.X), U(mid.Y, lo.Y, size.Y), U(mid.Z, lo.Z, size.Z));
            result.Add((a, bb, c, Paints.Marked(b.Pattern, u, n, size) ? mark : own));
        }

        foreach (var (a, bb, c, col) in solid)
        {
            var n = Vector3.Cross(bb - a, c - a);
            n = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
            Emit(a, bb, c, col, n, 0);
        }
        return result;
    }
}
