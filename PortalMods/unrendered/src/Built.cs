using System.Numerics;
using System.Text.Json.Nodes;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Entities;
using Unrendered.Rendering;
using GameWorld = Unrendered.World.World;

namespace UnrenderedPortal;

/// <summary>
/// What Minecraft players have built in UNRENDERED. A block placed in the rift dimension is a
/// block standing in UNRENDERED's world: drawn exactly as Minecraft draws it, and solid —
/// a craft walks into its wall, stands on its top, and a soldier can climb a staircase of it.
/// </summary>
public static class Built
{
    private static readonly Dictionary<(int X, int Y, int Z), int> _blocks = new();
    private static readonly List<Terrain.Entry?> _palette = new();
    private static readonly List<Mesh> _opaque = new(), _clear = new();
    private static bool _dirty;

    public static bool Any => _blocks.Count > 0;

    /// <summary>Where the first built block stands, for the test hatch to look at.</summary>
    public static System.Numerics.Vector3? First
        => _blocks.Count == 0 ? null : new System.Numerics.Vector3(_blocks.Keys.First().X + 0.5f, _blocks.Keys.First().Y, _blocks.Keys.First().Z + 0.5f);

    public static void Clear()
    {
        _blocks.Clear();
        _palette.Clear();
        Terrain.Free(_opaque);
        Terrain.Free(_clear);
    }

    internal static void Handle(Msg m)
    {
        if (m.J["palette"] is JsonArray pal) Terrain.AddEntries(_palette, pal);
        if (m.J["set"] is JsonArray set)
            foreach (var n in set)
            {
                if (n is not JsonArray a || a.Count < 4) continue;
                var key = (a[0]!.GetValue<int>(), a[1]!.GetValue<int>(), a[2]!.GetValue<int>());
                int p = a[3]!.GetValue<int>();
                if (p == 0) _blocks.Remove(key);
                else _blocks[key] = p;
            }
        _dirty = true;
    }

    private static Terrain.Entry? At(int x, int y, int z)
        => _blocks.TryGetValue((x, y, z), out int p) && p < _palette.Count ? _palette[p] : null;

    /// <summary>Built blocks stand in UNRENDERED's open air: full sky light.</summary>
    private static int Light(int x, int y, int z) => 0xF0;

    internal static void Draw(Vector3 eye, bool keyed)
    {
        if (_blocks.Count == 0) return;
        if (_dirty)
        {
            _dirty = false;
            Terrain.Free(_opaque);
            Terrain.Free(_clear);
            var mesher = new Terrain.Mesher(_opaque, _clear);
            foreach (var ((x, y, z), p) in _blocks)
                if (p < _palette.Count && _palette[p] is { Quads.Length: > 0 } e) mesher.Block(e, x, y, z, At, Light);
            mesher.Finish();
        }
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
        Terrain.DrawMeshes(_opaque, _clear, Vector3.Zero, eye, 1f, Atmosphere.FogColor,
            Atmosphere.FogStart, Atmosphere.FogEnd, keyed);
        Rlgl.DisableBackfaceCulling();
    }

    /// <summary>
    /// After each step: this machine's crafts against the built blocks. Walls push out, tops
    /// hold a craft up (machines stand on them through the ground height the game already has
    /// for roofs; the bodies are lifted onto them).
    /// </summary>
    internal static void Collide(GameWorld w)
    {
        if (_blocks.Count == 0) return;
        foreach (var craft in w.Players)
        {
            if (craft.Puppet || craft.Away || !craft.Alive || craft.Captured) continue;
            if (craft.Virus is { Exposed: true }) continue;   // a mote passes through matter
            float r = craft.IsMachine ? PlayerTank.Radius : 0.4f;
            float step = craft.IsMachine ? 1.05f : 0.6f;
            float body = MathF.Max(1.6f, craft.EyeHeight + 0.2f);
            float feet = craft.Height;

            Vector2 push = Vector2.Zero;
            var p = craft.Position;
            int x0 = (int)MathF.Floor(p.X - r), x1 = (int)MathF.Floor(p.X + r);
            int z0 = (int)MathF.Floor(p.Y - r), z1 = (int)MathF.Floor(p.Y + r);
            float ground = 0f;
            for (int bx = x0; bx <= x1; bx++)
                for (int bz = z0; bz <= z1; bz++)
                    for (int by = (int)MathF.Floor(feet) - 1; by <= (int)MathF.Ceiling(feet + body); by++)
                    {
                        if (At(bx, by, bz) is not { Collide: true } e) continue;
                        float top = by + e.Top;
                        float cx = Math.Clamp(p.X, bx, bx + 1), cz = Math.Clamp(p.Y, bz, bz + 1);
                        var d = new Vector2(p.X - cx, p.Y - cz);
                        float len = d.Length();
                        if (top <= feet + step)
                        {
                            // Under the feet (or a step up): stood on, if the middle is over it.
                            if (len < r * 0.6f) ground = MathF.Max(ground, top);
                            continue;
                        }
                        if (by >= feet + body || len >= r) continue;
                        push += len < 1e-4f ? new Vector2(r, 0) : d / len * (r - len);
                    }
            if (push != Vector2.Zero)
            {
                craft.Position = Torus.Wrap(craft.Position + push);
                craft.Rig?.RegisterWallHit();
            }
            if (craft.IsMachine) craft.GroundHeight = MathF.Max(craft.GroundHeight, ground);
            if (ground > 0f && craft.Height < ground) craft.Height = ground;
        }
    }
}
