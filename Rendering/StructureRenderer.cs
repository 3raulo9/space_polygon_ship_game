using System.Numerics;
using VoidTanks.Core;
using VoidTanks.World;

namespace VoidTanks.Rendering;

/// <summary>
/// Draws the skyline: the field's dead towers and the arcs slung between them, each at
/// its nearest image across the torus so the wrap stays invisible.
///
/// The meshes are built once and shared — a hundred and thirty buildings are a dozen
/// shapes turned, scaled and stood in different places, which is the only way a city
/// this size fits inside the same budget the rest of the game runs on. Nothing here is
/// per-frame state: hand it the field and a camera and it draws.
///
/// Culling is <see cref="PolyMesh"/>'s own — a structure whose centre is past the fog
/// boundary isn't drawn at all. That means a tower snaps into being at
/// <see cref="Config.FogEnd"/> rather than easing in, which is the established look
/// everywhere else in this world (Doc 02) and reads, on something this big, as a
/// silhouette resolving out of the murk.
/// </summary>
public sealed class StructureRenderer
{
    private readonly PolyMesh[] _towers = new PolyMesh[StructureMeshes.TowerVariants];
    private readonly PolyMesh[] _arches = new PolyMesh[StructureMeshes.ArchVariants];

    public StructureRenderer()
    {
        for (int i = 0; i < _towers.Length; i++) _towers[i] = StructureMeshes.Tower(i);
        for (int i = 0; i < _arches.Length; i++) _arches[i] = StructureMeshes.Arch(i);
    }

    /// <summary>
    /// Draws every structure in <paramref name="field"/> around the given eye. Called
    /// inside the 3D pass, before the entities, so shots and monsters sort in front of
    /// the buildings they are standing near.
    /// </summary>
    public void Draw(IReadOnlyList<Structure> field, Vector3 cameraPos)
    {
        var eyeXZ = new Vector2(cameraPos.X, cameraPos.Z);

        foreach (var s in field)
        {
            // The variant is stored as an arbitrary int so the field never has to know
            // how many shapes exist; fold it into whatever the renderer actually has.
            PolyMesh mesh = s.Kind == StructureKind.Arch
                ? _arches[s.Variant % _arches.Length]
                : _towers[s.Variant % _towers.Length];

            // Roll and sink are zero on anything standing, so an intact city draws
            // exactly as it always did; a struck one topples about its own base and then
            // goes down through the grid, which is where the wreck quietly leaves.
            mesh.Draw(Torus.NearestImage(s.Position, eyeXZ), s.Heading, s.Sink, cameraPos,
                s.Scale, tint: null, pitch: 0f, roll: s.Roll);
        }
    }
}
