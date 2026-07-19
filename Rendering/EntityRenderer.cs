using System.Numerics;
using Raylib_cs;
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

    // Death debris: a jagged chunk and a bright spark. Both are drawn white and
    // tinted per-instance so each piece can carry its own (fading) colour.
    private readonly PolyMesh _shard = Meshes.Shard(Color.White);
    private readonly PolyMesh _spark = Meshes.Bolt(Color.White);

    // Placeholder polygon ships — built only for the test screen's turntable.
    private readonly PolyMesh _shipInterceptor = Meshes.ShipInterceptor(Palette.EnemyFill);
    private readonly PolyMesh _shipGunship = Meshes.ShipGunship(Palette.EliteFill);
    private readonly PolyMesh _shipScout = Meshes.ShipScout(Palette.PlayerFill);

    public void Draw(World.World world, Vector3 cameraPos)
    {
        foreach (var e in world.Enemies)
        {
            if (!e.Alive) continue;
            var mesh = e.IsElite ? _eliteCone : _standardTank;
            // Scale the mesh by the same factor the hitbox uses, so the visible
            // body and the collision radius are one and the same.
            mesh.Draw(e.Position, e.Heading, 0f, cameraPos, EnemyTank.Scale);
        }

        foreach (var p in world.Projectiles)
        {
            if (!p.Active) continue;
            // Bolts hover at barrel height and don't need a heading. The heavy
            // grenade round is a fatter slug in the elite/orange colour, riding a
            // touch higher so it reads as the dangerous one.
            if (p.IsGrenade) _grenade.Draw(p.Position, 0f, 0.7f, cameraPos);
            else _bolt.Draw(p.Position, 0f, 0.55f, cameraPos);
        }

        // Death debris last: chunks and sparks flung from destroyed enemies, each
        // shrinking and fading toward the void over its short life.
        foreach (var s in world.Debris.Shards)
        {
            if (!s.Active) continue;
            var posXZ = new Vector2(s.Position.X, s.Position.Z);
            float f = s.LifeFrac;
            Color tint = GridRenderer.LerpColor(s.Color, Palette.Void, 1f - f);
            var mesh = s.IsSpark ? _spark : _shard;
            // Chunks shrink as they die; sparks stay small and just wink out.
            float size = s.IsSpark ? s.Size : s.Size * (0.4f + 0.6f * f);
            mesh.Draw(posXZ, s.Angle, s.Position.Y, cameraPos, size, tint);
        }
    }

    /// <summary>
    /// Draws a single roster entry as a rotating turntable specimen for the test
    /// screen. Tanks sit on the grid; the ships float, matching the rootless drift
    /// the enemies already have. Returns nothing — purely a display pass.
    /// </summary>
    public void DrawShowcase(EnemyKind kind, Vector2 pos, float heading, Vector3 cameraPos)
    {
        (PolyMesh mesh, float height) = kind switch
        {
            EnemyKind.StandardTank    => (_standardTank, 0f),
            EnemyKind.EliteTank       => (_eliteCone, 0f),
            EnemyKind.ShipInterceptor => (_shipInterceptor, 1.5f),
            EnemyKind.ShipGunship     => (_shipGunship, 1.4f),
            EnemyKind.ShipScout       => (_shipScout, 1.6f),
            _                         => (_standardTank, 0f),
        };
        mesh.Draw(pos, heading, height, cameraPos);
    }
}
