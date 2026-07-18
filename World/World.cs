using System.Numerics;
using VoidTanks.Core;
using VoidTanks.Entities;
using VoidTanks.Input;

namespace VoidTanks.World;

/// <summary>
/// Holds the live entities and runs the combat simulation: player + enemies +
/// a pooled projectile list, with collision and damage. Kept separate from the
/// render/loop plumbing so the rules read in one place. Milestone 2 seeds a
/// single enemy at range so it materializes out of the fog and hunts.
/// </summary>
public sealed class World
{
    public readonly PlayerTank Player;
    public readonly List<EnemyTank> Enemies = new();
    private readonly Projectile[] _projectiles;

    private const int MaxProjectiles = 64;
    private const float PlayerShotDamage = 1f;   // vs shield "points"
    private const float EnemyShotDamage = 12f;   // vs player's 100-point shield

    public World()
    {
        Player = new PlayerTank(Vector2.Zero);

        _projectiles = new Projectile[MaxProjectiles];
        for (int i = 0; i < _projectiles.Length; i++)
            _projectiles[i] = new Projectile();

        // One standard tank, spawned at range and off-axis so it enters from
        // the fog rather than dead ahead — the blip-before-shape beat (Doc 03).
        // A capture override lets the verification harness spawn it close enough
        // to inspect the polygon silhouette.
        string? near = Environment.GetEnvironmentVariable("VOIDTANKS_ENEMY_NEAR");
        if (near == "1")
            Enemies.Add(new EnemyTank(new Vector2(4f, 16f), elite: false));
        else if (near == "elite")
            Enemies.Add(new EnemyTank(new Vector2(4f, 16f), elite: true));
        else
            Enemies.Add(new EnemyTank(new Vector2(30f, 70f), elite: false));
    }

    public IReadOnlyList<Projectile> Projectiles => _projectiles;

    public void Update(float dt)
    {
        // Read player fire from global input, then run the shared sim step. The
        // step itself is input-free so the headless self-test can reuse it.
        if (InputMap.Fire)
            FirePlayerShot();

        StepForTest(dt);
    }

    /// <summary>
    /// The input-free simulation step: player physics, enemy AI, projectiles,
    /// collisions, cleanup. Shared by the live loop and the headless self-test
    /// (which injects player fire via <see cref="FirePlayerShot"/>).
    /// </summary>
    public void StepForTest(float dt)
    {
        Player.Update(dt);

        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            if (e.Update(dt, Player.Position, out Vector2 eOrigin, out Vector2 eDir))
                SpawnProjectile(eOrigin, eDir, fromPlayer: false);
        }

        UpdateProjectiles(dt);
        Enemies.RemoveAll(e => !e.Alive);
    }

    /// <summary>Requests a player shot; honoured only if off cooldown with ammo.</summary>
    public void FirePlayerShot()
    {
        if (Player.TryFire(out Vector2 origin, out Vector2 dir))
            SpawnProjectile(origin, dir, fromPlayer: true);
    }

    private void SpawnProjectile(Vector2 origin, Vector2 dir, bool fromPlayer)
    {
        foreach (var p in _projectiles)
        {
            if (p.Active) continue;
            p.Fire(origin, dir, fromPlayer);
            return;
        }
        // Pool full: silently drop the shot rather than allocate. Rare.
    }

    private void UpdateProjectiles(float dt)
    {
        foreach (var p in _projectiles)
        {
            if (!p.Active) continue;
            p.Update(dt);
            if (!p.Active) continue;

            if (p.FromPlayer)
            {
                foreach (var e in Enemies)
                {
                    if (!e.Alive) continue;
                    if (WithinHit(p.Position, e.Position, EnemyTank.Radius))
                    {
                        e.TakeDamage(PlayerShotDamage);
                        p.Active = false;
                        break;
                    }
                }
            }
            else
            {
                // Enemy shot vs player — only bites when the player is grounded;
                // the jump is the dodge (Doc 03), so an airborne craft is spared.
                if (!Player.IsAirborne &&
                    WithinHit(p.Position, Player.Position, PlayerTank.Radius))
                {
                    Player.TakeDamage(EnemyShotDamage);
                    p.Active = false;
                }
            }
        }
    }

    private static bool WithinHit(Vector2 a, Vector2 b, float radius)
        => Vector2.DistanceSquared(a, b) <= radius * radius;
}
