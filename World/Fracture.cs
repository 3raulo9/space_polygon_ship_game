using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;

namespace VoidTanks.World;

/// <summary>One block of a fractured tower, in the structure's own local (unscaled) space —
/// the same space the renderer's tower meshes are built in, so a chunk drawn at the
/// structure's scale sits exactly where the intact mesh's material was.</summary>
public struct FractureChunk
{
    public Vector3 Center;   // local centre
    public Vector3 Half;     // local half-extents (x, y, z)
    public Color Color;
    public int Layer;        // 0 = ground
    public float Damage;     // accumulated cutting damage toward CellHP
    public bool Alive;
}

/// <summary>A chunk knocked loose this hit, handed back in <em>world</em> space so the world
/// can turn it into crushing debris and dust. <see cref="Size"/> is the chunk's world
/// half-height, which the crush billing reads as its mass.</summary>
public readonly record struct DebrisSpawn(Vector3 Pos, float Size, Color Color);

/// <summary>
/// The destructible chunk model a tower fractures into the first time a beam bites it —
/// the thing that lets a tower come apart <em>locally</em> where it is struck instead of
/// toppling as one rigid piece.
///
/// The tower is a stack of <see cref="Layers"/> layers, each a small grid of cells. A cut
/// destroys the cells within its impact radius (radial falloff from the strike point) and
/// accumulates toward each cell's threshold, so repeated hits on one spot compound. When a
/// layer loses enough cells to stop carrying load, that layer and <em>everything above it</em>
/// loses its anchor and collapses onto what remains below — cut the crown and the top rains
/// down while the base still stands; cut the base and the whole thing comes down.
///
/// It owns no physics of its own: a collapsed cell is handed back to the world as a
/// <see cref="DebrisSpawn"/>, which becomes a mass-bearing chunk in the shared debris pool
/// (so the crush billing, the renderer and the pool cap are all the ones already in place).
/// What it keeps is the <em>standing</em> shape — the surviving cells — which the renderer
/// draws in place of the intact mesh, holes and stump and all.
/// </summary>
public sealed class Fracture
{
    // How finely a tower breaks. Twelve layers of a 2×2 grid (tapering to a single cell up
    // top) lands ~36 chunks — the "finer grid" the destruction is tuned for: granular enough
    // that a hit reads as a local hole and a collapse as a shower of blocks, cheap enough that
    // several towers coming down at once don't drown the 320-piece debris pool.
    private const int Layers = 12;
    private const int SingleFromLayer = 8;   // this layer up is a single central cell

    /// <summary>Cutting damage one cell soaks before it breaks off. Small enough that a
    /// one-shot siege beam obliterates every cell in its radius at once (an instant local
    /// collapse), large enough that a dwelling enemy beam has to chew — the visible chipping
    /// before a section finally lets go.</summary>
    private const float CellHP = 20f;

    private readonly FractureChunk[] _chunks;
    private readonly int[] _layerAlive;
    private readonly int[] _layerCap;

    // The structure's transform, snapshotted so the chunk model can map its own local cells
    // to and from world space without a reference back to the structure.
    private readonly Vector2 _pos;
    private readonly float _cos, _sin, _scale;

    /// <summary>Bumped whenever a cell breaks, so the renderer knows to rebuild its cached
    /// mesh of the surviving shape only when the shape has actually changed.</summary>
    public int Version { get; private set; }

    /// <summary>True while any cell still stands. Once false the structure is a cleared lot
    /// and the field lets it go.</summary>
    public bool AnyStanding { get; private set; } = true;

    /// <summary>The cells, for the renderer to walk — draw the ones where <c>Alive</c>.</summary>
    public FractureChunk[] Chunks => _chunks;

    public Fracture(Vector2 pos, float heading, float scale, float localHeight, float footprint, int variant)
    {
        _pos = pos;
        _cos = MathF.Cos(heading);
        _sin = MathF.Sin(heading);
        _scale = scale;

        var rng = new Random(variant * 9176 + 4703);
        var list = new List<FractureChunk>(Layers * 4);
        _layerAlive = new int[Layers];
        _layerCap = new int[Layers];

        float layerH = localHeight / Layers;
        for (int L = 0; L < Layers; L++)
        {
            float yc = (L + 0.5f) * layerH;
            float hy = layerH * 0.5f * 0.94f;                 // a hair of gap so storeys read
            // Taper the stack as it climbs so the swap from the intact mesh isn't a jump in
            // width — 1.0 at the base easing to ~0.4 at the crown.
            float t = 1f - 0.6f * (L / (float)(Layers - 1));
            float fp = footprint * t;

            if (L >= SingleFromLayer)
            {
                AddCell(list, rng, L, new Vector3(0f, yc, 0f),
                    new Vector3(fp * 0.62f, hy, fp * 0.62f));
            }
            else
            {
                float c = fp * 0.5f;            // cell centres at ±half-footprint/…
                float hx = fp * 0.5f * 0.9f;    // …with a small inset so the four read apart
                foreach (float sx in Sides)
                    foreach (float sz in Sides)
                        AddCell(list, rng, L, new Vector3(sx * c, yc, sz * c),
                            new Vector3(hx, hy, hx));
            }
        }

        _chunks = list.ToArray();
        for (int i = 0; i < _chunks.Length; i++)
        {
            _layerAlive[_chunks[i].Layer]++;
            _layerCap[_chunks[i].Layer]++;
        }
    }

    private static readonly float[] Sides = { -1f, 1f };

    private void AddCell(List<FractureChunk> list, Random rng, int layer, Vector3 centre, Vector3 half)
    {
        // Same palette the intact towers pull from: shells and deep tone banded by storey,
        // with the odd lit band, so a fractured tower still reads as the same architecture.
        Color c = (layer & 1) == 0 ? Palette.StructureShell : Palette.StructureDeep;
        if (rng.NextSingle() < 0.12f) c = Palette.StructureGlow;
        list.Add(new FractureChunk { Center = centre, Half = half, Color = c, Layer = layer, Alive = true });
    }

    /// <summary>
    /// Cuts the tower at <paramref name="worldImpact"/> with the given radius and amount:
    /// destroys cells within reach (radial falloff), accumulates toward the ones it doesn't
    /// break outright, then collapses any layer that has lost its footing and everything
    /// riding on it. Fills <paramref name="detached"/> with every cell knocked loose this
    /// call, in world space. Returns true if the base gave way — the whole tower is now down.
    /// </summary>
    public bool ApplyDamage(Vector3 worldImpact, float worldRadius, float amount, List<DebrisSpawn> detached)
    {
        Vector3 li = ToLocal(worldImpact);
        float lr = worldRadius / _scale;
        if (lr <= 0f) return false;

        bool changed = false;
        for (int i = 0; i < _chunks.Length; i++)
        {
            ref FractureChunk ch = ref _chunks[i];
            if (!ch.Alive) continue;

            float dist = Vector3.Distance(ch.Center, li);
            if (dist > lr) continue;

            float falloff = 1f - dist / lr;          // 1 at the strike point, 0 at the edge
            ch.Damage += amount * falloff;
            if (ch.Damage >= CellHP)
            {
                Break(ref ch, detached);
                changed = true;
            }
        }

        bool baseFell = false;
        if (changed)
        {
            baseFell = Cascade(detached);
            Version++;
            RecountStanding();
        }
        return baseFell;
    }

    /// <summary>
    /// Brings the whole tower down at once — a detonation, not a cut. Every standing cell
    /// lets go. Fills <paramref name="detached"/> if given (so a bomb throws real falling
    /// chunks); a null list simply erases them (the caller's own blast covers the picture).
    /// </summary>
    public void CollapseAll(List<DebrisSpawn>? detached)
    {
        for (int i = 0; i < _chunks.Length; i++)
        {
            ref FractureChunk ch = ref _chunks[i];
            if (!ch.Alive) continue;
            if (detached != null) Break(ref ch, detached);
            else { ch.Alive = false; _layerAlive[ch.Layer]--; }
        }
        Version++;
        RecountStanding();
    }

    private void Break(ref FractureChunk ch, List<DebrisSpawn> detached)
    {
        ch.Alive = false;
        _layerAlive[ch.Layer]--;
        detached.Add(new DebrisSpawn(ToWorld(ch.Center), ch.Half.Y * _scale, ch.Color));
    }

    /// <summary>
    /// Finds the lowest layer that can no longer carry a load and drops it and everything
    /// above onto what is left. A layer has failed once it is down to under half its cells —
    /// the supports beneath the impact have given way. Returns true if that layer was the
    /// base, i.e. the whole structure is coming down.
    /// </summary>
    private bool Cascade(List<DebrisSpawn> detached)
    {
        int fail = -1;
        for (int L = 0; L < Layers; L++)
        {
            if (_layerAlive[L] < (_layerCap[L] + 1) / 2) { fail = L; break; }
        }
        if (fail < 0) return false;

        for (int i = 0; i < _chunks.Length; i++)
        {
            ref FractureChunk ch = ref _chunks[i];
            if (ch.Alive && ch.Layer >= fail) Break(ref ch, detached);
        }
        return fail == 0;
    }

    private void RecountStanding()
    {
        for (int i = 0; i < _chunks.Length; i++)
            if (_chunks[i].Alive) { AnyStanding = true; return; }
        AnyStanding = false;
    }

    // local (unscaled) -> world: scale, rotate by heading (0 faces +Z), translate. Matches
    // Rendering.PolyMesh.Transform, so a chunk lands exactly where its material was drawn.
    private Vector3 ToWorld(Vector3 local)
    {
        Vector3 s = local * _scale;
        float x = s.X * _cos + s.Z * _sin;
        float z = -s.X * _sin + s.Z * _cos;
        return new Vector3(_pos.X + x, s.Y, _pos.Y + z);
    }

    // world -> local: the exact inverse, so a beam's world strike point is tested against the
    // cells in the frame they were laid out in.
    private Vector3 ToLocal(Vector3 world)
    {
        float dx = world.X - _pos.X;
        float dz = world.Z - _pos.Y;
        float lx = dx * _cos - dz * _sin;
        float lz = dx * _sin + dz * _cos;
        return new Vector3(lx / _scale, world.Y / _scale, lz / _scale);
    }
}
