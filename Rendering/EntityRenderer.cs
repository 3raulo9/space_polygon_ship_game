using System.Numerics;
using VoidTanks.Core;
using VoidTanks.Entities;

namespace VoidTanks.Rendering;

/// <summary>
/// Draws the flat-shaded solids (enemies, projectiles) inside the 3D pass.
/// Meshes are built once and shared. Everything fades into fog and pops in at
/// the boundary via <see cref="PolyMesh"/>; nothing casts a shadow or is grounded
/// to the grid — the rootless float is part of the wrongness (Doc 02).
/// </summary>
public sealed class EntityRenderer
{
    private readonly PolyMesh _standardTank = Meshes.Tank(Palette.EnemyFill);
    private readonly PolyMesh _eliteCone = Meshes.EliteCone(Palette.EliteFill);
    private readonly PolyMesh _bolt = Meshes.Bolt(Palette.Flag);
    private readonly PolyMesh _grenade = Meshes.Grenade(Palette.EliteFill);

    public void Draw(World.World world, Vector2 cameraXZ)
    {
        foreach (var e in world.Enemies)
        {
            if (!e.Alive) continue;
            var mesh = e.IsElite ? _eliteCone : _standardTank;
            mesh.Draw(e.Position, e.Heading, 0f, cameraXZ);
        }

        foreach (var p in world.Projectiles)
        {
            if (!p.Active) continue;
            // Bolts hover at barrel height and don't need a heading. The heavy
            // grenade round is a fatter slug in the elite/orange colour, riding a
            // touch higher so it reads as the dangerous one.
            if (p.IsGrenade) _grenade.Draw(p.Position, 0f, 0.7f, cameraXZ);
            else _bolt.Draw(p.Position, 0f, 0.55f, cameraXZ);
        }
    }
}
