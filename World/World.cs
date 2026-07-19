using System.Numerics;
using Raylib_cs;
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
    public readonly DebrisSystem Debris = new();

    /// <summary>The lone Crab-Core boss seeded into the stage, or null. Runs its
    /// own Stalker Protocol against the player independent of the tank combat.</summary>
    public CrabCore? Boss { get; private set; }

    private readonly Projectile[] _projectiles;

    private const int MaxProjectiles = 64;
    private const float PlayerShotDamage = 1f;   // vs shield "points"
    private const float GrenadeDamage = 4f;      // heavy round, dealt to all in the blast
    private const float EnemyShotDamage = 12f;   // vs player's 100-point shield

    // Shield fraction at which the low-health alarm sounds. Crossing *down*
    // through this line fires warning.wav once — not once per frame below it.
    private const float LowShieldWarning = 0.45f;

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

        // One Crab-Core boss, dead ahead so it can be walked up to and watched. It
        // sits just outside its own detection radius, idling, until the player
        // strays close and trips the Stalker Protocol. A capture override drops it
        // in point-blank (and already awake) so the rig can be screenshotted.
        Boss = Environment.GetEnvironmentVariable("VOIDTANKS_BOSS_NEAR") == "1"
            ? new CrabCore(new Vector2(0f, 44f))
            : new CrabCore(new Vector2(0f, 85f));
    }

    public IReadOnlyList<Projectile> Projectiles => _projectiles;

    public void Update(float dt)
    {
        // Read player actions from global input, then run the shared sim step. The
        // step itself is input-free so the headless self-test can reuse it.
        // Grenade takes precedence over the cannon on the frame both are held,
        // since they share the fire cooldown.
        if (InputMap.Grenade)
            FirePlayerGrenade();
        else if (InputMap.Fire)
            FirePlayerShot();

        if (InputMap.HyperspacePressed)
            Player.TryHyperspace();

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

        // The boss stalks on its own clock; a true return means the carapace just
        // slammed shut this tick, so sound the bit-crushed CLANG. Wherever a leg
        // planted this tick, kick up a puff of grid dust under the foot.
        if (Boss is { } boss)
        {
            if (boss.Update(dt, Player.Position))
                Audio.PlayClamp();
            foreach (var f in boss.Footfalls)
                Debris.FootPuff(new Vector3(f.X, 0f, f.Y));
        }

        UpdateProjectiles(dt);
        Debris.Update(dt);
        Enemies.RemoveAll(e => !e.Alive);
    }

    /// <summary>Requests a player shot; honoured only if off cooldown with ammo.</summary>
    public void FirePlayerShot()
    {
        if (Player.TryFire(out Vector2 origin, out Vector2 dir))
            SpawnProjectile(origin, dir, fromPlayer: true);
    }

    /// <summary>Requests a heavy grenade; honoured only if off cooldown with 10 ammo.</summary>
    public void FirePlayerGrenade()
    {
        if (Player.TryFireGrenade(out Vector2 origin, out Vector2 dir))
            SpawnProjectile(origin, dir, fromPlayer: true, grenade: true);
    }

    private void SpawnProjectile(Vector2 origin, Vector2 dir, bool fromPlayer, bool grenade = false)
    {
        foreach (var p in _projectiles)
        {
            if (p.Active) continue;
            if (grenade) p.FireGrenade(origin, dir, fromPlayer);
            else p.Fire(origin, dir, fromPlayer);
            // The report of a barrel firing — same clip for player and enemy
            // shots, since both spawn through here.
            Audio.PlayDetonation();
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
                        if (p.IsGrenade)
                            Detonate(p);           // area burst, damages the whole cluster
                        else
                            DamageEnemy(e, PlayerShotDamage);
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
                    DamagePlayer(EnemyShotDamage);
                    p.Active = false;
                }
            }
        }
    }

    /// <summary>
    /// Applies enemy damage to the player and sounds the right combat cue: the
    /// death explosion if this shot ends the run; otherwise the low-shield
    /// warning while the craft sits at/below the danger line (it *replaces* the
    /// normal hit clip there, so a wounded player hears the alarm on every hit);
    /// otherwise the plain hit. A respawn refills the shield above the line, so
    /// hits go back to the normal clip.
    /// </summary>
    private void DamagePlayer(float amount)
    {
        Player.TakeDamage(amount);

        // A lost life that ends the run is still a destroyed craft.
        if (!Player.Alive)
            Audio.PlayExplosion();
        else if (Player.ShieldFraction <= LowShieldWarning)
            Audio.PlayWarning();
        else
            Audio.PlayHit();
    }

    /// <summary>
    /// Deals damage to an enemy and sounds the explosion if this hit is what
    /// destroys it — the alive→dead transition, so a cluster killed by one blast
    /// each reports its own death.
    /// </summary>
    private void DamageEnemy(EnemyTank enemy, float amount)
    {
        bool wasAlive = enemy.Alive;
        enemy.TakeDamage(amount);
        if (wasAlive && !enemy.Alive)
        {
            Audio.PlayExplosion();
            // Break the hunter into flying polygon shards + sparks at roughly its
            // body's centre height (the mesh sits on the grid, scaled up in view).
            var origin = new Vector3(enemy.Position.X, 1.6f, enemy.Position.Y);
            Color body = enemy.IsElite ? Palette.EliteFill : Palette.EnemyFill;
            Debris.Burst(origin, body, enemy.IsElite);
        }
    }

    /// <summary>
    /// Grenade blast: deals damage to every live enemy whose centre falls inside
    /// the projectile's splash radius — the whole cluster feels one hit.
    /// </summary>
    private void Detonate(Projectile p)
    {
        float reach = p.SplashRadius + EnemyTank.Radius;
        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            if (WithinHit(p.Position, e.Position, reach))
                DamageEnemy(e, GrenadeDamage);
        }
    }

    private static bool WithinHit(Vector2 a, Vector2 b, float radius)
        => Vector2.DistanceSquared(a, b) <= radius * radius;
}
