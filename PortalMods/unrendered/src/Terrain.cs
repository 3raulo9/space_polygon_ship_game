using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Raylib_cs;

namespace UnrenderedPortal;

/// <summary>
/// The slice of a Minecraft world around the portal, held block by block and drawn the way
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
    public int Ox, Oy, Oz, Sx, Sy, Sz;
    private ushort[] _idx = Array.Empty<ushort>();
    private byte[] _light = Array.Empty<byte>();
    public bool Loaded { get; private set; }

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

    public void LoadRegion(Msg m)
    {
        Ox = m.I("ox"); Oy = m.I("oy"); Oz = m.I("oz");
        Sx = m.I("sx"); Sy = m.I("sy"); Sz = m.I("sz");
        int n = Sx * Sy * Sz;
        var bin = m.Bin!;
        _idx = new ushort[n];
        _light = new byte[n];
        MemoryMarshal.Cast<byte, ushort>(bin.AsSpan(0, n * 2)).CopyTo(_idx);
        if (bin.Length >= n * 3) bin.AsSpan(n * 2, n).CopyTo(_light);
        else Array.Fill(_light, (byte)0xF0);
        Loaded = true;
        Unload();
        _secX = (Sx + 15) / 16; _secY = (Sy + 15) / 16; _secZ = (Sz + 15) / 16;
        _sections = new Section?[_secX * _secY * _secZ];
        _dirty = new bool[_sections.Length];
        MarkAllDirty();
        Rift.Log($"terrain: region {Sx}x{Sy}x{Sz} at {Ox},{Oy},{Oz}, palette {Palette.Count}");
    }

    public void SetBlock(int x, int y, int z, int p, int light)
    {
        int lx = x - Ox, ly = y - Oy, lz = z - Oz;
        if (!In(lx, ly, lz)) return;
        int i = (ly * Sz + lz) * Sx + lx;
        _idx[i] = (ushort)p;
        if (light >= 0) _light[i] = (byte)light;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                    MarkDirty(lx + dx, ly + dy, lz + dz);
    }

    private bool In(int lx, int ly, int lz) => (uint)lx < (uint)Sx && (uint)ly < (uint)Sy && (uint)lz < (uint)Sz;

    /// <summary>The block state at an absolute Minecraft position, or null outside the slice.</summary>
    public Entry? At(int x, int y, int z)
    {
        int lx = x - Ox, ly = y - Oy, lz = z - Oz;
        if (!In(lx, ly, lz)) return null;
        int p = _idx[(ly * Sz + lz) * Sx + lx];
        return p < Palette.Count ? Palette[p] : null;
    }

    public int LightAt(int x, int y, int z)
    {
        int lx = x - Ox, ly = y - Oy, lz = z - Oz;
        if (!In(lx, ly, lz)) return ly >= Sy ? 0xF0 : 0;
        return _light[(ly * Sz + lz) * Sx + lx];
    }

    public bool InsideXZ(float x, float z, float margin)
        => x >= Ox + margin && x < Ox + Sx - margin && z >= Oz + margin && z < Oz + Sz - margin;

    // --- Collision, for UNRENDERED's crafts ------------------------------------------

    /// <summary>The top of whatever can be stood on in this column at or below
    /// <paramref name="fromY"/>, in absolute Minecraft Y. Falls to the slice floor.</summary>
    public float GroundBelow(int x, int z, float fromY)
    {
        int y = (int)MathF.Floor(fromY);
        for (; y >= Oy; y--)
        {
            var e = At(x, y, z);
            if (e is { Collide: true } && y + e.Top <= fromY + 1e-3f) return y + e.Top;
            // A fence's 1.5 sticks up into the block above: look one below as well.
        }
        return Oy;
    }

    /// <summary>True if anything collidable occupies [y0, y1) in this column.</summary>
    public bool Blocked(int x, int z, float y0, float y1)
    {
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

    private Section?[] _sections = Array.Empty<Section?>();
    private bool[] _dirty = Array.Empty<bool>();
    private int _secX, _secY, _secZ;

    private void MarkDirty(int lx, int ly, int lz)
    {
        if (!In(lx, ly, lz)) return;
        _dirty[((ly / 16) * _secZ + lz / 16) * _secX + lx / 16] = true;
    }

    private void MarkAllDirty()
    {
        for (int i = 0; i < _dirty.Length; i++) _dirty[i] = true;
    }

    /// <summary>Rebuilds a few dirty sections a frame, nearest the eye first.</summary>
    public void Rebuild(Vector3 eyeMc, int budget)
    {
        if (!Loaded) return;
        while (budget-- > 0)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < _dirty.Length; i++)
            {
                if (!_dirty[i]) continue;
                int sx = i % _secX, sz = (i / _secX) % _secZ, sy = i / (_secX * _secZ);
                var c = new Vector3(Ox + sx * 16 + 8, Oy + sy * 16 + 8, Oz + sz * 16 + 8);
                float d = Vector3.DistanceSquared(c, eyeMc);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return;
            _dirty[best] = false;
            BuildSection(best);
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

    private void BuildSection(int s)
    {
        if (_sections[s] is { } old) Free(old);
        int sx = s % _secX, sz = (s / _secX) % _secZ, sy = s / (_secX * _secZ);
        var sec = new Section();
        var b = new Mesher(sec.Opaque, sec.Clear);

        for (int ly = sy * 16; ly < Math.Min(Sy, sy * 16 + 16); ly++)
            for (int lz = sz * 16; lz < Math.Min(Sz, sz * 16 + 16); lz++)
                for (int lx = sx * 16; lx < Math.Min(Sx, sx * 16 + 16); lx++)
                {
                    var e = Palette.Count > _idx[(ly * Sz + lz) * Sx + lx] ? Palette[_idx[(ly * Sz + lz) * Sx + lx]] : null;
                    if (e == null || e.Quads.Length == 0) continue;
                    b.Block(e, Ox + lx, Oy + ly, Oz + lz, At, LightAt);
                }
        b.Finish();
        _sections[s] = sec;
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

    private void Unload()
    {
        foreach (var s in _sections) if (s != null) Free(s);
        Array.Clear(_sections);
    }

    public void Dispose() => Unload();

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
        foreach (var s in _sections)
            if (s != null) { opaque.AddRange(s.Opaque); clear.AddRange(s.Clear); }
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
            l = max(l, 0.05);
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
