using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Raylib_cs;

namespace UnrenderedPortal;

/// <summary>
/// The Minecraft world around the pilot, streamed in by chunk column as they move, held block
/// by block and drawn the way
/// Minecraft draws it: each block state's own baked model quads (so stairs are stairs and
/// a torch is a torch), Minecraft's own textures out of an atlas, its sky and block light,
/// its face shading and its biome tints.
/// </summary>
public sealed unsafe class Terrain : IDisposable
{
    public sealed class Entry
    {
        public int I;
        public string Id = "";
        public bool Solid, Collide, Portal;
        public string? Liquid;
        public float Top;
        public int Emit;
        public Quad[] Quads = Array.Empty<Quad>();
    }

    public readonly struct Quad
    {
        public readonly string Tex;
        public readonly int Cull;          // -1 or direction 0..5
        public readonly Color Tint;
        public readonly bool Shade;
        public readonly float[] V;         // 4 × (x, y, z, u, v)
        public readonly Vector3 Normal;

        public Quad(string tex, int cull, Color tint, bool shade, float[] v)
        {
            Tex = tex; Cull = cull; Tint = tint; Shade = shade; V = v;
            var a = new Vector3(v[0], v[1], v[2]);
            var b = new Vector3(v[5], v[6], v[7]);
            var c = new Vector3(v[10], v[11], v[12]);
            var n = Vector3.Cross(b - a, c - a);
            Normal = n.LengthSquared() > 1e-9f ? Vector3.Normalize(n) : Vector3.UnitY;
        }
    }

    public readonly List<Entry?> Palette = new();

    /// <summary>One chunk column, as Minecraft sent it: blocks from <see cref="Y0"/> up
    /// through every section that holds anything.</summary>
    private sealed class Column
    {
        public int Cx, Cz, Y0, H;
        public ushort[] Idx = Array.Empty<ushort>();
        public byte[] Light = Array.Empty<byte>();
        public Section?[] Secs = Array.Empty<Section?>();
        public bool[] Dirty = Array.Empty<bool>();
    }

    private readonly Dictionary<(int, int), Column> _cols = new();
    public bool Loaded => _cols.Count > 0;
    public int Columns => _cols.Count;

    // --- Palette and region -----------------------------------------------------------

    public void AddPalette(JsonArray entries)
    {
        AddEntries(Palette, entries);
        MarkAllDirty();
    }

    /// <summary>Reads palette entries (PROTOCOL.md) into <paramref name="palette"/>, by index.</summary>
    public static void AddEntries(List<Entry?> palette, JsonArray entries)
    {
        foreach (var node in entries)
        {
            if (node is not JsonObject o) continue;
            var e = new Entry
            {
                I = o["i"]?.GetValue<int>() ?? palette.Count,
                Id = o["id"]?.GetValue<string>() ?? "",
                Solid = o["solid"]?.GetValue<bool>() ?? false,
                Collide = o["collide"]?.GetValue<bool>() ?? (o["solid"]?.GetValue<bool>() ?? false),
                Portal = o["portal"]?.GetValue<bool>() ?? false,
                Liquid = o["liquid"]?.GetValue<string>(),
                Top = o["top"]?.GetValue<float>() ?? 1f,
                Emit = o["emit"]?.GetValue<int>() ?? 0,
            };
            if (e.Id.StartsWith("minecraft:air") || e.Id.StartsWith("minecraft:cave_air") || e.Id.StartsWith("minecraft:void_air"))
                e.Collide = false;
            if (e.Liquid != null) e.Collide = false;
            var quads = new List<Quad>();
            if (o["quads"] is JsonArray qs)
                foreach (var qn in qs)
                {
                    if (qn is not JsonObject q || q["v"] is not JsonArray va || va.Count < 20) continue;
                    var v = new float[20];
                    for (int i = 0; i < 20; i++) v[i] = va[i]!.GetValue<float>();
                    Color tint = Color.White;
                    if (q["tint"] is JsonArray t && t.Count >= 3)
                        tint = new Color(t[0]!.GetValue<int>(), t[1]!.GetValue<int>(), t[2]!.GetValue<int>(), 255);
                    string tex = q["tex"]?.GetValue<string>() ?? "minecraft:missing";
                    quads.Add(new Quad(tex, q["cull"]?.GetValue<int>() ?? -1, tint,
                        q["shade"]?.GetValue<bool>() ?? true, v));
                    Atlas.Slot(tex);
                }
            e.Quads = quads.ToArray();
            while (palette.Count <= e.I) palette.Add(null);
            palette[e.I] = e;
        }
    }

    /// <summary>One chunk column off the wire (PROTOCOL.md, <c>chunk</c>).</summary>
    public void LoadChunk(Msg m)
    {
        int cx = m.I("cx"), cz = m.I("cz"), y0 = m.I("y0"), h = m.I("h");
        Put(cx, cz, y0, h, m.Bin!, 0);
    }

    /// <summary>The old fixed slice, still understood: cut into columns.</summary>
    public void LoadRegion(Msg m)
    {
        int ox = m.I("ox"), oy = m.I("oy"), oz = m.I("oz");
        int sx = m.I("sx"), sy = m.I("sy"), sz = m.I("sz");
        var bin = m.Bin!;
        int n = sx * sy * sz;
        int h = (sy + 15) / 16 * 16;
        for (int cx = ox >> 4; cx <= (ox + sx - 1) >> 4; cx++)
            for (int cz = oz >> 4; cz <= (oz + sz - 1) >> 4; cz++)
            {
                var col = new Column { Cx = cx, Cz = cz, Y0 = oy, H = h, Idx = new ushort[256 * h], Light = new byte[256 * h] };
                for (int y = 0; y < sy; y++)
                    for (int z = 0; z < 16; z++)
                        for (int x = 0; x < 16; x++)
                        {
                            int wx = cx * 16 + x - ox, wz = cz * 16 + z - oz;
                            int i = (y * 16 + z) * 16 + x;
                            if ((uint)wx >= (uint)sx || (uint)wz >= (uint)sz) { col.Light[i] = 0xF0; continue; }
                            int r = (y * sz + wz) * sx + wx;
                            col.Idx[i] = (ushort)(bin[r * 2] | bin[r * 2 + 1] << 8);
                            col.Light[i] = bin.Length >= n * 3 ? bin[n * 2 + r] : (byte)0xF0;
                        }
                Install(col);
            }
        Rift.Log($"terrain: region {sx}x{sy}x{sz} at {ox},{oy},{oz} as {_cols.Count} columns");
    }

    private void Put(int cx, int cz, int y0, int h, byte[] bin, int at)
    {
        int n = 256 * h;
        var col = new Column { Cx = cx, Cz = cz, Y0 = y0, H = h, Idx = new ushort[n], Light = new byte[n] };
        if (n > 0)
        {
            MemoryMarshal.Cast<byte, ushort>(bin.AsSpan(at, n * 2)).CopyTo(col.Idx);
            if (bin.Length >= at + n * 3) bin.AsSpan(at + n * 2, n).CopyTo(col.Light);
            else Array.Fill(col.Light, (byte)0xF0);
        }
        Install(col);
    }

    private void Install(Column col)
    {
        if (_cols.Remove((col.Cx, col.Cz), out var old)) Free(old);
        col.Secs = new Section?[col.H / 16];
        col.Dirty = new bool[col.H / 16];
        Array.Fill(col.Dirty, true);
        _cols[(col.Cx, col.Cz)] = col;
        // The neighbours' faces against this column were drawn against nothing; now they
        // have something to be culled against.
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            if (_cols.TryGetValue((col.Cx + dx, col.Cz + dz), out var n)) Array.Fill(n.Dirty, true);
    }

    /// <summary>Lets go of every column farther than <paramref name="keep"/> chunks from (cx, cz).</summary>
    public void Forget(int cx, int cz, int keep)
    {
        foreach (var key in _cols.Keys.Where(k => Math.Max(Math.Abs(k.Item1 - cx), Math.Abs(k.Item2 - cz)) > keep).ToList())
        {
            Free(_cols[key]);
            _cols.Remove(key);
        }
    }

    public void SetBlock(int x, int y, int z, int p, int light)
    {
        if (!_cols.TryGetValue((x >> 4, z >> 4), out var c)) return;
        int ly = y - c.Y0;
        if (ly < 0) return;
        if (ly >= c.H)
        {
            if (p == 0) return;
            Grow(c, (ly / 16 + 1) * 16);
        }
        int i = (ly * 16 + (z & 15)) * 16 + (x & 15);
        c.Idx[i] = (ushort)p;
        if (light >= 0) c.Light[i] = (byte)light;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                    MarkDirty(x + dx, y + dy, z + dz);
    }

    /// <summary>A block placed above the top of what was sent: the column grows to hold it.</summary>
    private static void Grow(Column c, int h)
    {
        var idx = new ushort[256 * h];
        var light = new byte[256 * h];
        Array.Fill(light, (byte)0xF0);
        c.Idx.CopyTo(idx, 0);
        c.Light.CopyTo(light, 0);
        var secs = new Section?[h / 16];
        c.Secs.CopyTo(secs, 0);
        var dirty = new bool[h / 16];
        Array.Fill(dirty, true);
        c.Idx = idx; c.Light = light; c.Secs = secs; c.Dirty = dirty; c.H = h;
    }

    /// <summary>True when Minecraft has sent the column this point stands in.</summary>
    public bool Has(int x, int z) => _cols.ContainsKey((x >> 4, z >> 4));

    /// <summary>The block state at an absolute Minecraft position (null for air, or for
    /// anywhere Minecraft has not sent).</summary>
    public Entry? At(int x, int y, int z)
    {
        if (!_cols.TryGetValue((x >> 4, z >> 4), out var c)) return null;
        int ly = y - c.Y0;
        if ((uint)ly >= (uint)c.H) return null;
        int p = c.Idx[(ly * 16 + (z & 15)) * 16 + (x & 15)];
        return p < Palette.Count ? Palette[p] : null;
    }

    public int LightAt(int x, int y, int z)
    {
        if (!_cols.TryGetValue((x >> 4, z >> 4), out var c)) return 0xF0;
        int ly = y - c.Y0;
        if (ly < 0) return 0;
        if (ly >= c.H) return 0xF0;
        return c.Light[(ly * 16 + (z & 15)) * 16 + (x & 15)];
    }

    /// <summary>The lowest Y any column holds, for a fall with nothing under it.</summary>
    private int Floor(int x, int z) => _cols.TryGetValue((x >> 4, z >> 4), out var c) ? c.Y0 : -64;

    /// <summary>The bottom of the world under this column (Minecraft's minimum Y there).</summary>
    public int Bottom(int x, int z) => Floor(x, z);

    /// <summary>How far under the bottom of the world nothing at all is: past here the pilot has
    /// fallen out of it.</summary>
    public const float Void = 64f;

    // --- Collision, for UNRENDERED's crafts ------------------------------------------

    /// <summary>The top of whatever can be stood on in this column at or below
    /// <paramref name="fromY"/>, in absolute Minecraft Y.</summary>
    public float GroundBelow(int x, int z, float fromY)
    {
        int bottom = Floor(x, z);
        for (int y = (int)MathF.Floor(fromY); y >= bottom; y--)
        {
            var e = At(x, y, z);
            if (e is { Collide: true } && y + e.Top <= fromY + 1e-3f) return y + e.Top;
        }
        // Nothing to stand on all the way down (the End's islands, a hole to the void): no
        // floor is made up for it. The craft falls.
        return bottom - Void;
    }

    /// <summary>True if anything collidable occupies [y0, y1) in this column — and a column
    /// Minecraft has not sent yet is a wall, not a hole.</summary>
    public bool Blocked(int x, int z, float y0, float y1)
    {
        if (!Has(x, z)) return true;
        for (int y = (int)MathF.Floor(y0) - 1; y < (int)MathF.Ceiling(y1); y++)
        {
            var e = At(x, y, z);
            if (e is not { Collide: true }) continue;
            float top = y + e.Top, bottom = y;
            if (top > y0 + 1e-3f && bottom < y1) return true;
        }
        return false;
    }

    /// <summary>True if a point is inside a block a round would stop against.</summary>
    public bool Solid(Vector3 p)
    {
        var e = At((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
        return e is { Collide: true } && p.Y - MathF.Floor(p.Y) <= e.Top;
    }

    public bool PortalNear(Vector3 p, float r)
    {
        int x0 = (int)MathF.Floor(p.X - r), x1 = (int)MathF.Floor(p.X + r);
        int y0 = (int)MathF.Floor(p.Y), y1 = (int)MathF.Floor(p.Y + 1.5f);
        int z0 = (int)MathF.Floor(p.Z - r), z1 = (int)MathF.Floor(p.Z + r);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    if (At(x, y, z) is { Portal: true }) return true;
        return false;
    }

    // --- Meshes ----------------------------------------------------------------------

    private sealed class Section
    {
        public readonly List<Mesh> Opaque = new();
        public readonly List<Mesh> Clear = new();
    }

    private void MarkDirty(int x, int y, int z)
    {
        if (!_cols.TryGetValue((x >> 4, z >> 4), out var c)) return;
        int s = (y - c.Y0) >> 4;
        if ((uint)s < (uint)c.Dirty.Length) c.Dirty[s] = true;
    }

    private void MarkAllDirty()
    {
        foreach (var c in _cols.Values) Array.Fill(c.Dirty, true);
    }

    /// <summary>Rebuilds a few dirty sections a frame, nearest the eye first.</summary>
    public void Rebuild(Vector3 eyeMc, int budget)
    {
        while (budget-- > 0)
        {
            Column? best = null;
            int bestS = -1;
            float bestD = float.MaxValue;
            foreach (var c in _cols.Values)
                for (int s = 0; s < c.Dirty.Length; s++)
                {
                    if (!c.Dirty[s]) continue;
                    var mid = new Vector3(c.Cx * 16 + 8, c.Y0 + s * 16 + 8, c.Cz * 16 + 8);
                    float d = Vector3.DistanceSquared(mid, eyeMc);
                    if (d < bestD) { bestD = d; best = c; bestS = s; }
                }
            if (best == null) return;
            best.Dirty[bestS] = false;
            BuildSection(best, bestS);
        }
    }

    private static readonly int[][] Dir =
    {
        new[] { 0, -1, 0 }, new[] { 0, 1, 0 }, new[] { 0, 0, -1 },
        new[] { 0, 0, 1 }, new[] { -1, 0, 0 }, new[] { 1, 0, 0 },
    };

    private sealed class Builder
    {
        public readonly List<float> Pos = new(), Uv = new(), Light = new();
        public readonly List<byte> Col = new();
        public int Verts => Pos.Count / 3;
    }

    private void BuildSection(Column c, int s)
    {
        if (c.Secs[s] is { } old) Free(old);
        var sec = new Section();
        var b = new Mesher(sec.Opaque, sec.Clear);
        int y0 = s * 16;
        for (int ly = y0; ly < Math.Min(c.H, y0 + 16); ly++)
            for (int lz = 0; lz < 16; lz++)
                for (int lx = 0; lx < 16; lx++)
                {
                    int p = c.Idx[(ly * 16 + lz) * 16 + lx];
                    var e = p < Palette.Count ? Palette[p] : null;
                    if (e == null || e.Quads.Length == 0) continue;
                    b.Block(e, c.Cx * 16 + lx, c.Y0 + ly, c.Cz * 16 + lz, At, LightAt);
                }
        b.Finish();
        c.Secs[s] = sec;
    }

    /// <summary>
    /// Turns blocks into meshes: each block state's own quads, culled against the solid
    /// neighbours Minecraft would cull them against, lit by the light in the block each face
    /// looks into, with Minecraft's face shading. Shared by the slice of Minecraft a pilot
    /// walks in and by the blocks Minecraft players build in UNRENDERED.
    /// </summary>
    public sealed class Mesher
    {
        private readonly List<Mesh> _opaque, _clear;
        private readonly Builder _o = new(), _c = new();

        public Mesher(List<Mesh> opaque, List<Mesh> clear) { _opaque = opaque; _clear = clear; }

        public void Block(Entry e, int x, int y, int z, Func<int, int, int, Entry?> at, Func<int, int, int, int> light)
        {
            var into = e.Liquid != null || e.Portal ? _c : _o;
            foreach (var q in e.Quads)
            {
                if (q.Cull >= 0)
                {
                    var d = Dir[q.Cull];
                    var n = at(x + d[0], y + d[1], z + d[2]);
                    if (n != null && (n.Solid || (e.Liquid != null && n.Liquid == e.Liquid))) continue;
                }
                // Light from the block the face looks into, as Minecraft lights it.
                int lightAt = q.Cull >= 0
                    ? light(x + Dir[q.Cull][0], y + Dir[q.Cull][1], z + Dir[q.Cull][2])
                    : Math.Max(light(x, y, z), light(x, y + 1, z));
                float sky = (lightAt >> 4) / 15f;
                float blk = Math.Max(lightAt & 15, e.Emit) / 15f;
                float shade = q.Shade ? FaceShade(q.Normal) : 1f;
                Emit(into, q, x, y, z, shade, sky, blk);
                if (into.Verts > 60000) Flush(into, into == _c ? _clear : _opaque);
            }
        }

        public void Finish()
        {
            Flush(_o, _opaque);
            Flush(_c, _clear);
        }
    }

    /// <summary>Minecraft's directional face shading: full on top, half underneath, the
    /// sides in between.</summary>
    private static float FaceShade(Vector3 n)
    {
        float ax = MathF.Abs(n.X), ay = MathF.Abs(n.Y), az = MathF.Abs(n.Z);
        if (ay >= ax && ay >= az) return n.Y > 0 ? 1f : 0.5f;
        return az >= ax ? 0.8f : 0.6f;
    }

    private static void Emit(Builder b, in Quad q, int x, int y, int z, float shade, float sky, float blk)
    {
        var uv = Atlas.Uv(q.Tex);
        Color tint = q.Tint;
        byte r = (byte)(tint.R * shade), g = (byte)(tint.G * shade), bl = (byte)(tint.B * shade);
        int[] order = { 0, 1, 2, 0, 2, 3 };
        foreach (int k in order)
        {
            b.Pos.Add(x + q.V[k * 5]); b.Pos.Add(y + q.V[k * 5 + 1]); b.Pos.Add(z + q.V[k * 5 + 2]);
            b.Uv.Add(uv.X + q.V[k * 5 + 3] * uv.Z); b.Uv.Add(uv.Y + q.V[k * 5 + 4] * uv.W);
            b.Light.Add(sky); b.Light.Add(blk);
            b.Col.Add(r); b.Col.Add(g); b.Col.Add(bl); b.Col.Add(255);
        }
    }

    private static void Flush(Builder b, List<Mesh> into)
    {
        int n = b.Verts;
        if (n == 0) return;
        var mesh = new Mesh { VertexCount = n, TriangleCount = n / 3 };
        mesh.Vertices = (float*)Raylib.MemAlloc((uint)(n * 3 * sizeof(float)));
        mesh.TexCoords = (float*)Raylib.MemAlloc((uint)(n * 2 * sizeof(float)));
        mesh.TexCoords2 = (float*)Raylib.MemAlloc((uint)(n * 2 * sizeof(float)));
        mesh.Colors = (byte*)Raylib.MemAlloc((uint)(n * 4));
        CollectionsMarshal.AsSpan(b.Pos).CopyTo(new Span<float>(mesh.Vertices, n * 3));
        CollectionsMarshal.AsSpan(b.Uv).CopyTo(new Span<float>(mesh.TexCoords, n * 2));
        CollectionsMarshal.AsSpan(b.Light).CopyTo(new Span<float>(mesh.TexCoords2, n * 2));
        CollectionsMarshal.AsSpan(b.Col).CopyTo(new Span<byte>(mesh.Colors, n * 4));
        Raylib.UploadMesh(ref mesh, false);
        into.Add(mesh);
        b.Pos.Clear(); b.Uv.Clear(); b.Light.Clear(); b.Col.Clear();
    }

    private static void Free(Section s)
    {
        foreach (var m in s.Opaque) Raylib.UnloadMesh(m);
        foreach (var m in s.Clear) Raylib.UnloadMesh(m);
    }

    public static void Free(List<Mesh> meshes)
    {
        foreach (var m in meshes) Raylib.UnloadMesh(m);
        meshes.Clear();
    }

    private static void Free(Column c)
    {
        foreach (var s in c.Secs) if (s != null) Free(s);
    }

    /// <summary>Everything let go — the pilot went home.</summary>
    public void Dispose()
    {
        foreach (var c in _cols.Values) Free(c);
        _cols.Clear();
        Palette.Clear();
    }

    // --- Drawing -------------------------------------------------------------------

    private static Shader _shader;
    private static Material _material;
    private static bool _ready;
    private static int _locDay, _locCam, _locFog, _locFogStart, _locFogEnd, _locKeyMode;

    /// <summary>Draws every built section. The meshes are in Minecraft's own coordinates;
    /// <paramref name="origin"/> is the Minecraft point that sits at the drawing space's zero.</summary>
    public void Draw(Vector3 origin, Vector3 camMc, float daylight, Color fog, float fogStart, float fogEnd)
    {
        if (!Loaded) return;
        var opaque = new List<Mesh>();
        var clear = new List<Mesh>();
        float reach = fogEnd + 24f;
        foreach (var c in _cols.Values)
        {
            float dx = c.Cx * 16 + 8 - camMc.X, dz = c.Cz * 16 + 8 - camMc.Z;
            if (dx * dx + dz * dz > reach * reach) continue;
            foreach (var s in c.Secs)
                if (s != null) { opaque.AddRange(s.Opaque); clear.AddRange(s.Clear); }
        }
        DrawMeshes(opaque, clear, origin, camMc, daylight, fog, fogStart, fogEnd, keyed: false);
    }

    /// <summary>
    /// Minecraft block meshes, through Minecraft's light and fog. <paramref name="keyed"/> draws
    /// them as the composite's mask instead (water and glass included — whatever shows of a
    /// block is Minecraft's to fill in).
    /// </summary>
    public static void DrawMeshes(List<Mesh> opaque, List<Mesh> clear, Vector3 origin, Vector3 camMc,
        float daylight, Color fog, float fogStart, float fogEnd, bool keyed)
    {
        Ready();
        Atlas.Upload();
        Raylib.SetShaderValue(_shader, _locDay, daylight, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locCam, camMc, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_shader, _locFog, new Vector4(fog.R / 255f, fog.G / 255f, fog.B / 255f, 1f), ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(_shader, _locFogStart, fogStart, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locFogEnd, fogEnd, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_shader, _locKeyMode, keyed ? 1 : 0, ShaderUniformDataType.Int);
        _material.Maps[(int)MaterialMapIndex.Albedo].Texture = Atlas.Texture;

        Matrix4x4 at = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(-origin));
        foreach (var m in opaque) Raylib.DrawMesh(m, _material, at);

        // Water, lava and the portal: after everything solid, without writing depth, so
        // what is under the surface still shows through it.
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        foreach (var m in clear) Raylib.DrawMesh(m, _material, at);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
    }

    private static void Ready()
    {
        if (_ready) return;
        _ready = true;
        _shader = Raylib.LoadShaderFromMemory(Vs, Fs);
        _locDay = Raylib.GetShaderLocation(_shader, "daylight");
        _locCam = Raylib.GetShaderLocation(_shader, "camPos");
        _locFog = Raylib.GetShaderLocation(_shader, "fogColor");
        _locFogStart = Raylib.GetShaderLocation(_shader, "fogStart");
        _locFogEnd = Raylib.GetShaderLocation(_shader, "fogEnd");
        _locKeyMode = Raylib.GetShaderLocation(_shader, "keyMode");
        _material = Raylib.LoadMaterialDefault();
        _material.Shader = _shader;
    }

    private const string Vs = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec2 vertexTexCoord2;
        in vec4 vertexColor;
        uniform mat4 mvp;
        uniform mat4 matModel;
        out vec2 uv;
        out vec4 tint;
        out vec2 light;
        out vec3 world;
        void main() {
            uv = vertexTexCoord;
            tint = vertexColor;
            light = vertexTexCoord2;
            world = vertexPosition;
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    // Minecraft's light curve (the lightmap at the default "moody/bright" midpoint), sky light
    // dimmed by the hour, block light not; then fog by horizontal distance, as Minecraft does.
    private const string Fs = """
        #version 330
        in vec2 uv;
        in vec4 tint;
        in vec2 light;
        in vec3 world;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        uniform float daylight;
        uniform vec3 camPos;
        uniform vec4 fogColor;
        uniform float fogStart;
        uniform float fogEnd;
        uniform int keyMode;
        out vec4 finalColor;
        float curve(float f) {
            float v = f / (4.0 - 3.0 * f);
            float g = 1.0 - pow(1.0 - v, 4.0);
            return mix(v, g, 0.5);
        }
        void main() {
            vec4 t = texture(texture0, uv);
            if (t.a < 0.1) discard;
            if (keyMode == 1) { finalColor = vec4(0.0); return; }
            float l = max(curve(light.x) * daylight, curve(light.y) * vec3(1.0, 0.9, 0.75).r);
            l = max(l, 0.08);
            vec3 blockTint = vec3(1.0, 0.92, 0.8);
            vec3 lit = mix(vec3(l), vec3(l) * blockTint, step(curve(light.x) * daylight, curve(light.y)));
            vec3 c = t.rgb * tint.rgb * lit;
            float d = length(world.xz - camPos.xz);
            float f = clamp((d - fogStart) / (fogEnd - fogStart), 0.0, 1.0);
            finalColor = vec4(mix(c, fogColor.rgb, f), t.a * tint.a);
        }
        """;
}

/// <summary>
/// Every block texture the slice uses, packed 16×16 into one 1024² atlas so the terrain can
/// be drawn in a handful of calls. Slots are handed out as soon as a texture id is seen, so
/// a mesh can be built before its pixels have crossed the wire; the pixels land in their
/// slot when they arrive and the meshes never need rebuilding for it. Animated strips
/// (water, lava, fire, the portal) are stepped in place every frame.
/// </summary>
public static unsafe class Atlas
{
    private const int Size = 1024, Tile = 16, PerRow = Size / Tile;
    private static readonly Dictionary<string, int> _slots = new();
    private static readonly List<(int Slot, Textures.Tex Tex, Image Strip)> _animated = new();
    private static Image _img;
    private static Texture2D _tex;
    private static bool _made, _dirty;
    private static readonly int[] _frame = new int[PerRow * PerRow];

    public static Texture2D Texture => _tex;

    private static void Make()
    {
        if (_made) return;
        _made = true;
        _img = Raylib.GenImageChecked(Size, Size, 8, 8, new Color(248, 0, 248, 255), Color.Black);
        Raylib.ImageFormat(ref _img, PixelFormat.UncompressedR8G8B8A8);
        _tex = Raylib.LoadTextureFromImage(_img);
        Raylib.SetTextureFilter(_tex, TextureFilter.Point);
        Textures.Loaded += Paint;

        // Slot 0 is plain white, for faces with no texture at all — a Minecraft with no client
        // sends blocks as their map colour, which is all tint.
        _slots[""] = 0;
        Raylib.ImageDrawRectangle(ref _img, 0, 0, Tile, Tile, Color.White);
    }

    public static int Slot(string tex)
    {
        Make();
        if (_slots.TryGetValue(tex, out int s)) return s;
        s = _slots.Count;
        if (tex.Length == 0) return 0;
        if (s >= PerRow * PerRow) return 0;
        _slots[tex] = s;
        if (Textures.Get(tex) is { } t) Paint(t);
        return s;
    }

    /// <summary>(u0, v0, du, dv) of a texture's slot, inset a hair so neighbours never bleed.</summary>
    public static Vector4 Uv(string tex)
    {
        int s = Slot(tex);
        float px = 1f / Size;
        float u = (s % PerRow) * Tile * px, v = (s / PerRow) * Tile * px;
        const float inset = 0.01f / Size;
        return new Vector4(u + inset, v + inset, Tile * px - 2 * inset, Tile * px - 2 * inset);
    }

    private static void Paint(Textures.Tex t)
    {
        if (!_slots.TryGetValue(t.Id, out int s) || s == 0) return;
        Raylib.ImageDrawRectangle(ref _img, (s % PerRow) * Tile, (s / PerRow) * Tile, Tile, Tile, Color.Blank);
        // The whole strip, scaled to 16 wide, so every frame is ready to step through.
        Image strip = Raylib.ImageCopy(t.Cpu);
        Raylib.ImageResizeNN(ref strip, Tile, Tile * t.Frames);
        var dst = new Rectangle((s % PerRow) * Tile, (s / PerRow) * Tile, Tile, Tile);
        Raylib.ImageDraw(ref _img, strip, new Rectangle(0, 0, Tile, Tile), dst, Color.White);
        if (t.Frames > 1) _animated.Add((s, t, strip));
        else Raylib.UnloadImage(strip);
        _dirty = true;
    }

    /// <summary>Pushes new pixels and steps the animated slots. Once a frame.</summary>
    public static void Upload()
    {
        if (!_made) return;
        double time = Raylib.GetTime();
        foreach (var (s, t, strip) in _animated)
        {
            int f = Textures.FrameOf(t, time);
            if (_frame[s] == f) continue;
            _frame[s] = f;
            var src = new Rectangle(0, f * Tile, Tile, Tile);
            var dst = new Rectangle((s % PerRow) * Tile, (s / PerRow) * Tile, Tile, Tile);
            Raylib.ImageDraw(ref _img, strip, src, dst, Color.White);
            _dirty = true;
        }
        if (!_dirty) return;
        _dirty = false;
        Raylib.UpdateTexture(_tex, _img.Data);
    }
}
