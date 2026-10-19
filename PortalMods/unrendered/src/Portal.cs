using System.Numerics;
using System.Text.Json.Nodes;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Rendering;
using GameWorld = Unrendered.World.World;

namespace UnrenderedPortal;

/// <summary>
/// UNRENDERED's half of the door: a frame of Minecraft glowstone with Minecraft water
/// standing in it, opened on the grid in front of the craft. The same shape a Minecraft
/// player builds by hand on the other side — two wide and three tall inside — only scaled
/// so that a tank fits through.
/// </summary>
public static class Portal
{
    /// <summary>Metres per Minecraft block, for the frame only. At 1.0 the opening would be
    /// narrower than a tank.</summary>
    public const float Block = 1.6f;
    public const int InnerW = 2, InnerH = 3;

    public static bool Open => _world != null;
    private static GameWorld? _world;
    private static Vector2 _at;        // centre of the opening, on the plane
    private static float _facing;      // radians: the side the craft stood on looks along this
    private static float _age;
    private static float _lastSide;
    private static bool _haveSide;
    private static float _stillWarned = -10f;

    /// <summary>Minecraft's half, once a world is linked.</summary>
    public static JsonObject? Linked { get; private set; }

    public static Vector2 Position => _at;
    public static float Facing => _facing;
    public static GameWorld? World => _world;

    public const string Glowstone = "minecraft:textures/block/glowstone.png";
    public const string Water = "minecraft:textures/block/water_still.png";
    public static readonly Color WaterTint = new(63, 118, 228, 255);   // Minecraft's default water

    /// <summary>Opens (or re-opens) the portal a few strides ahead of the craft, facing it.</summary>
    public static void OpenAhead()
    {
        if (Rift.World is not { } w) return;
        var p = w.Player;
        _world = w;
        _facing = p.Heading + MathF.PI;   // the water faces back at whoever opened it
        _at = Torus.Wrap(p.Position + p.Forward * 7f);
        _age = 0f;
        _haveSide = false;
        Rift.Log($"portal opened at {Rift.F(_at.X)},{Rift.F(_at.Y)}");
        // Ask for the two textures now so the frame is dressed by the time it has grown.
        Textures.Get(Glowstone);
        Textures.Get(Water);
        CueBank.Play(Cue.PortalOpen, _at, 0f);
    }

    public static void LinkTo(JsonObject mc)
    {
        Linked = mc;
        Rift.Log("portal linked to minecraft at " + mc.ToJsonString());
    }

    public static void Unlink() => Linked = null;

    public static void Forget()
    {
        _world = null;
        _haveSide = false;
    }

    /// <summary>How far through the opening the frame has grown, 0..1 — it does not just
    /// appear, it builds itself block by block like a frame being placed.</summary>
    private static float Grown => Math.Clamp(_age / 1.2f, 0f, 1f);

    internal static void Tick(GameWorld world, float dt)
    {
        if (!ReferenceEquals(world, _world)) return;
        _age += dt;
        if (Grown < 1f) return;

        Crossings.Tick(world);

        var p = world.Player;
        if (!p.Alive) return;

        // Which side of the water the craft is on, measured along the portal's normal.
        Vector2 n = new(MathF.Sin(_facing), MathF.Cos(_facing));
        Vector2 rel = Torus.Delta(_at, p.Position);
        float side = Vector2.Dot(rel, n);
        float across = Vector2.Dot(rel, new Vector2(n.Y, -n.X));
        bool inside = MathF.Abs(across) < InnerW * Block * 0.5f + 0.4f
                   && p.Height < InnerH * Block && MathF.Abs(side) < 4f;

        if (_haveSide && inside && MathF.Sign(side) != MathF.Sign(_lastSide) && side != 0f)
            Cross(world);
        _lastSide = side;
        _haveSide = true;
    }

    private static void Cross(GameWorld world)
    {
        if (Rift.Live == null || Linked == null)
        {
            if (Raylib.GetTime() - _stillWarned > 4)
            {
                _stillWarned = (float)Raylib.GetTime();
                Rift.Notice(Rift.Live == null
                    ? "THE WATER IS STILL — NO MINECRAFT WORLD ON THE OTHER SIDE"
                    : "THE WATER STIRS — WAITING FOR MINECRAFT TO OPEN ITS SIDE");
            }
            return;
        }
        Realm.Enter(world);
    }

    // --- Drawing ----------------------------------------------------------------------

    /// <summary>Inside the 3D pass, after the game's own entities.</summary>
    internal static void Draw(GameWorld world, Vector3 cam, bool keyed = false)
    {
        if (!ReferenceEquals(world, _world)) return;
        McShader.Begin(keyed);
        try { DrawFrame(cam); }
        finally { McShader.End(); }
    }

    private static void DrawFrame(Vector3 cam)
    {
        var eye = new Vector2(cam.X, cam.Z);
        Vector2 at = Torus.NearestImage(_at, eye);
        float fog = GridRenderer.FogFactor(Vector2.Distance(at, eye));
        if (fog >= 0.99f) return;

        Vector2 n = new(MathF.Sin(_facing), MathF.Cos(_facing));
        Vector3 right = new(n.Y, 0f, -n.X);
        Vector3 normal = new(n.X, 0f, n.Y);
        Vector3 origin = new(at.X, 0f, at.Y);

        var glow = Textures.Or(Glowstone);
        var water = Textures.Or(Water);

        // The frame: 4 x 5 blocks with the inside 2 x 3 left open. Grows bottom-up, one
        // block at a time, the way a frame gets placed.
        int total = (InnerW + 2) * 2 + InnerH * 2;
        int placed = (int)(Grown * total + 0.999f);
        int k = 0;
        Color lit = GridRenderer.LerpColor(Color.White, Atmosphere.FogColor, fog);
        for (int row = 0; row < InnerH + 2; row++)
        {
            for (int col = 0; col < InnerW + 2; col++)
            {
                bool edge = row == 0 || row == InnerH + 1 || col == 0 || col == InnerW + 1;
                if (!edge) continue;
                if (k++ >= placed) continue;
                float x = (col - (InnerW + 2) * 0.5f) * Block;
                Vector3 c = origin + right * (x + Block * 0.5f) + new Vector3(0, row * Block + Block * 0.5f, 0);
                Cube(c, right, normal, Block, glow, lit);
            }
        }

        if (Grown < 1f) return;

        // The water. Minecraft's own still-water strip, animated at its own frame time and
        // tinted the way Minecraft tints it. Drawn both ways round, since you can see it
        // from either side.
        AnimateSlot(water);
        byte alpha = (byte)(Linked != null ? 205 : 150);
        Color wc = GridRenderer.LerpColor(WaterTint, Atmosphere.FogColor, fog);
        wc.A = alpha;
        Vector3 bl = origin + right * (-InnerW * 0.5f * Block) + new Vector3(0, Block, 0);
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        Rlgl.SetTexture(water.Gpu.Id);
        Rlgl.Begin(UnrenderedPortal.Draw.Quads);
        for (int cx = 0; cx < InnerW; cx++)
            for (int cy = 0; cy < InnerH; cy++)
            {
                Vector3 a = bl + right * (cx * Block) + new Vector3(0, cy * Block, 0);
                Vector3 b = a + right * Block;
                Vector3 c = b + new Vector3(0, Block, 0);
                Vector3 d = a + new Vector3(0, Block, 0);
                Quad(a, b, c, d, wc);
                Quad(b, a, d, c, wc);
            }
        Rlgl.End();
        Rlgl.SetTexture(0);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
    }

    private static int _waterFrame = -1;

    /// <summary>Steps an animated strip's GPU texture to the frame it should be showing.</summary>
    private static void AnimateSlot(Textures.Tex t)
    {
        if (t.Frames <= 1) return;
        int f = Textures.FrameOf(t, Raylib.GetTime());
        if (f == _waterFrame) return;
        _waterFrame = f;
        Raylib.UpdateTexture(t.Gpu, Textures.FramePixels(t, f).ToArray());
    }

    /// <summary>A textured cube centred on <paramref name="c"/>, one Minecraft face per side.
    /// Glowstone gives off its own light, so it is drawn at full brightness on every face;
    /// only the fog touches it.</summary>
    internal static void Cube(Vector3 c, Vector3 right, Vector3 normal, float size, Textures.Tex tex, Color tint)
    {
        float h = size * 0.5f;
        Vector3 r = right * h, u = new(0, h, 0), f = normal * h;
        Rlgl.SetTexture(tex.Gpu.Id);
        Rlgl.Begin(UnrenderedPortal.Draw.Quads);
        Quad(c - r - u + f, c + r - u + f, c + r + u + f, c - r + u + f, tint);   // front
        Quad(c + r - u - f, c - r - u - f, c - r + u - f, c + r + u - f, tint);   // back
        Quad(c - r - u - f, c - r - u + f, c - r + u + f, c - r + u - f, tint);   // left
        Quad(c + r - u + f, c + r - u - f, c + r + u - f, c + r + u + f, tint);   // right
        Quad(c - r + u + f, c + r + u + f, c + r + u - f, c - r + u - f, tint);   // top
        Quad(c - r - u - f, c + r - u - f, c + r - u + f, c - r - u + f, tint);   // bottom
        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    /// <summary>a b c d counter-clockwise, a at the texture's bottom-left.</summary>
    internal static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
    {
        Rlgl.Color4ub(col.R, col.G, col.B, col.A);
        Rlgl.TexCoord2f(0, 1); Rlgl.Vertex3f(a.X, a.Y, a.Z);
        Rlgl.TexCoord2f(1, 1); Rlgl.Vertex3f(b.X, b.Y, b.Z);
        Rlgl.TexCoord2f(1, 0); Rlgl.Vertex3f(c.X, c.Y, c.Z);
        Rlgl.TexCoord2f(0, 0); Rlgl.Vertex3f(d.X, d.Y, d.Z);
    }
}
