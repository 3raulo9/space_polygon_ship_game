using System.Numerics;
using System.Runtime.CompilerServices;
using VoidTanks.Core;
using VoidTanks.World;

namespace VoidTanks.Rendering;

/// <summary>
/// Draws the skyline: the field's dead towers and the arcs slung between them, each at
/// its nearest image across the torus so the wrap stays invisible.
///
/// An intact structure is a dozen shapes turned, scaled and stood in different places —
/// one shared prebuilt mesh per variant, which is the only way a city this size fits the
/// budget. A <em>fractured</em> tower is different: it draws as its own surviving chunks,
/// so the holes a beam cut and the stump left behind are the real geometry, not a trick.
/// That per-tower chunk mesh is cached and rebuilt only when a cell actually breaks (the
/// fracture's version changes), never per frame, so a standing ruin costs one draw like
/// anything else.
///
/// Culling is <see cref="PolyMesh"/>'s own — a structure whose centre is past the fog
/// boundary isn't drawn at all, which reads on something this big as a silhouette
/// resolving out of the murk (Doc 02).
/// </summary>
public sealed class StructureRenderer
{
    private readonly PolyMesh[] _towers = new PolyMesh[StructureMeshes.TowerVariants];
    private readonly PolyMesh[] _arches = new PolyMesh[StructureMeshes.ArchVariants];

    // Per-fractured-tower cache of the surviving-chunk mesh. Keyed weakly, so a structure the
    // field has dropped takes its cached mesh with it and nothing has to prune.
    private sealed class ChunkMesh { public PolyMesh Mesh = new(); public int Version = -1; }
    private readonly ConditionalWeakTable<Structure, ChunkMesh> _fractureMeshes = new();

    public StructureRenderer()
    {
        for (int i = 0; i < _towers.Length; i++) _towers[i] = StructureMeshes.Tower(i);
        for (int i = 0; i < _arches.Length; i++) _arches[i] = StructureMeshes.Arch(i);
    }

    /// <summary>
    /// Draws every structure in <paramref name="field"/> around the given eye. Called inside
    /// the 3D pass, before the entities, so shots and monsters sort in front of the buildings
    /// they are standing near.
    /// </summary>
    public void Draw(IReadOnlyList<Structure> field, Vector3 cameraPos)
    {
        var eyeXZ = new Vector2(cameraPos.X, cameraPos.Z);

        foreach (var s in field)
        {
            Vector2 at = Torus.NearestImage(s.Position, eyeXZ);

            // A cut tower is drawn as its surviving cells — the local holes and the stump —
            // in place of the intact shape it no longer is.
            if (s.Fracture is { } frac)
            {
                ChunkMeshFor(s, frac).Draw(at, s.Heading, 0f, cameraPos, s.Scale);
                continue;
            }

            // The variant is stored as an arbitrary int so the field never has to know how
            // many shapes exist; fold it into whatever the renderer actually has. Roll and
            // sink are zero on anything standing and non-zero only while an arch topples.
            PolyMesh mesh = s.Kind == StructureKind.Arch
                ? _arches[s.Variant % _arches.Length]
                : _towers[s.Variant % _towers.Length];
            mesh.Draw(at, s.Heading, s.Sink, cameraPos, s.Scale, tint: null, pitch: 0f, roll: s.Roll);
        }
    }

    private PolyMesh ChunkMeshFor(Structure s, Fracture frac)
    {
        ChunkMesh cached = _fractureMeshes.GetValue(s, static _ => new ChunkMesh());
        if (cached.Version != frac.Version)
        {
            cached.Mesh = BuildChunkMesh(frac);
            cached.Version = frac.Version;
        }
        return cached.Mesh;
    }

    /// <summary>Builds the mesh of a fracture's surviving cells, each a flat-shaded box in the
    /// tower's own local space, so drawing it at the structure's transform stands the ruin
    /// exactly where the intact tower was.</summary>
    private static PolyMesh BuildChunkMesh(Fracture frac)
    {
        var m = new PolyMesh();
        foreach (var ch in frac.Chunks)
        {
            if (!ch.Alive) continue;
            m.AddBoxSpan(ch.Color,
                ch.Center.X - ch.Half.X, ch.Center.X + ch.Half.X,
                ch.Center.Z - ch.Half.Z, ch.Center.Z + ch.Half.Z,
                ch.Center.Y - ch.Half.Y, ch.Center.Y + ch.Half.Y);
        }
        return m;
    }
}
