using System.Numerics;
using System.Text;
using Raylib_cs;
using Unrendered.Rendering;

namespace UnrenderedPortal;

/// <summary>
/// A Minecraft entity exactly as Minecraft's own renderer drew it on one tick: textured quads
/// in layers, one layer per texture (the body, the armour, the held sword, the glint…). The
/// Fabric side captures the vertices out of Minecraft's render pipeline; this side replays
/// them. A zombie in UNRENDERED is Minecraft's zombie, not a lookalike.
/// </summary>
public sealed class McEntity
{
    public int Id;
    public Vector3 Pos;        // MC coords of the entity's feet
    public float W, H, Hp, Max;
    public byte Flags;
    public string Name = "";
    public readonly List<(string Tex, float[] V, byte[] C)> Layers = new();

    public bool Hostile => (Flags & 1) != 0;
    public bool IsPlayer => (Flags & 2) != 0;
    public bool IsItem => (Flags & 4) != 0;
    public bool Pickable => (Flags & 8) != 0;

    public static McEntity Read(BinaryReader r)
    {
        var e = new McEntity
        {
            Id = r.ReadInt32(),
            Pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
            W = r.ReadSingle(), H = r.ReadSingle(),
            Hp = r.ReadSingle(), Max = r.ReadSingle(),
            Flags = r.ReadByte(),
        };
        e.Name = Str(r);
        int layers = r.ReadUInt16();
        for (int l = 0; l < layers; l++)
        {
            string tex = Str(r);
            int quads = (int)r.ReadUInt32();
            var v = new float[quads * 4 * 5];
            var c = new byte[quads * 4 * 4];
            for (int i = 0; i < quads * 4; i++)
            {
                v[i * 5] = r.ReadSingle(); v[i * 5 + 1] = r.ReadSingle(); v[i * 5 + 2] = r.ReadSingle();
                v[i * 5 + 3] = r.ReadSingle(); v[i * 5 + 4] = r.ReadSingle();
                c[i * 4] = r.ReadByte(); c[i * 4 + 1] = r.ReadByte(); c[i * 4 + 2] = r.ReadByte(); c[i * 4 + 3] = r.ReadByte();
            }
            e.Layers.Add((tex, v, c));
        }
        return e;
    }

    public static string Str(BinaryReader r)
    {
        int n = r.ReadUInt16();
        return n == 0 ? "" : Encoding.UTF8.GetString(r.ReadBytes(n));
    }

    /// <summary>
    /// Replays the captured quads with their feet at <paramref name="at"/> (in whatever space
    /// the caller is drawing). <paramref name="fog"/> 0..1 sinks the colours toward
    /// <paramref name="fogColor"/> — UNRENDERED's fog when a Minecraft player is standing in
    /// UNRENDERED, none when the pilot is looking at Minecraft. <paramref name="scale"/> lets
    /// a far-away dropped item stay the size Minecraft draws it.
    /// </summary>
    public void Draw(Vector3 at, float fog, Color fogColor)
    {
        foreach (var (texId, v, c) in Layers)
        {
            var tex = Textures.Or(texId);
            Rlgl.SetTexture(tex.Gpu.Id);
            Rlgl.Begin(Draw_.Quads);
            int n = v.Length / 5;
            for (int i = 0; i < n; i++)
            {
                Color col = new(c[i * 4], c[i * 4 + 1], c[i * 4 + 2], c[i * 4 + 3]);
                if (fog > 0f)
                {
                    byte a = col.A;
                    col = GridRenderer.LerpColor(col, fogColor, fog);
                    col.A = a;
                }
                Rlgl.Color4ub(col.R, col.G, col.B, col.A);
                Rlgl.TexCoord2f(v[i * 5 + 3], v[i * 5 + 4]);
                Rlgl.Vertex3f(at.X + v[i * 5], at.Y + v[i * 5 + 1], at.Z + v[i * 5 + 2]);
            }
            Rlgl.End();
            Rlgl.SetTexture(0);
        }
    }

    /// <summary>Asks for every texture this entity wears, so the first sight of a new mob
    /// costs one round trip rather than one per frame.</summary>
    public void Warm()
    {
        foreach (var (tex, _, _) in Layers) Textures.Get(tex);
    }

    private static class Draw_
    {
        public const int Quads = UnrenderedPortal.Draw.Quads;
    }
}
