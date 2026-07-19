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

    /// <summary>Floating salvage scattered on the grid — batteries and stray rounds.</summary>
    public readonly List<Pickup> Pickups = new();

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

    // Floating pickups keep the field stocked: a handful of battery cells and stray
    // rounds drift on the grid at all times. Each restores 30% of its resource, and
    // a collected one respawns out in the fog so the supply never runs dry.
    private const int BatteryCount = 4;
    private const int AmmoCount = 3;
    private const float PickupRefill = 0.30f;

    public World()
    {
        Player = new PlayerTank(Vector2.Zero);

        // Scatter the initial field around the player's start point, out past the
        // near fog so they read as objects drifting in the void, not underfoot. A
        // capture override instead seeds one battery and one round dead ahead so the
        // verification harness can inspect the pickup silhouettes point-blank.
        if (Environment.GetEnvironmentVariable("VOIDTANKS_PICKUP_NEAR") == "1")
        {
            Pickups.Add(new Pickup(new Vector2(-2.5f, 14f), PickupKind.Battery));
            Pickups.Add(new Pickup(new Vector2(2.5f, 14f), PickupKind.Ammo));
        }
        else
        {
            for (int i = 0; i < BatteryCount; i++)
                Pickups.Add(new Pickup(RandomFieldPoint(28f, 110f), PickupKind.Battery));
            for (int i = 0; i < AmmoCount; i++)
                Pickups.Add(new Pickup(RandomFieldPoint(28f, 110f), PickupKind.Ammo));
        }

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
        UpdatePickups(dt);
        Debris.Update(dt);
        Enemies.RemoveAll(e => !e.Alive);
    }

    /// <summary>
    /// Bobs and spins each pickup and collects any the craft has driven over. A
    /// collected pickup applies its charge, sparks in its own colour, then teleports
    /// back out into the fog so the field stays stocked — the salvage is endless.
    /// </summary>
    private void UpdatePickups(float dt)
    {
        float reach = PlayerTank.Radius + Pickup.Radius;
        float reachSq = reach * reach;
        foreach (var pk in Pickups)
        {
            pk.Update(dt);
            if (Vector2.DistanceSquared(pk.Position, Player.Position) <= reachSq)
                Collect(pk);
        }
    }

    /// <summary>Applies a pickup's charge, sounds the collect, and relocates it.</summary>
    private void Collect(Pickup pk)
    {
        switch (pk.Kind)
        {
            case PickupKind.Battery:
                // A battery repairs the shield *and* recharges the Hyper reserve.
                Player.RefillShield(PickupRefill);
                Player.RefillHyper(PickupRefill);
                break;
            case PickupKind.Ammo:
                Player.RefillAmmo(PickupRefill);
                break;
        }

        Audio.PlayPickup();

        // A small sparkle in the pickup's colour marks the grab, reusing the debris
        // system (cosmetic only — it never touches damage or collision).
        Color spark = pk.Kind == PickupKind.Battery ? Palette.BatteryCore : Palette.Flag;
        Debris.Burst(new Vector3(pk.Position.X, pk.BobHeight, pk.Position.Y), spark, elite: false);

        // Respawn out in the fog around the craft so it drifts back into view later.
        pk.Position = RandomPointAroundPlayer(45f, 100f);
        pk.Age = 0f;
    }

    /// <summary>A random point on the plane at [min,max] from the world origin.</summary>
    private static Vector2 RandomFieldPoint(float minDist, float maxDist)
        => PointAround(Vector2.Zero, minDist, maxDist);

    /// <summary>A random point on the plane at [min,max] from the player.</summary>
    private Vector2 RandomPointAroundPlayer(float minDist, float maxDist)
        => PointAround(Player.Position, minDist, maxDist);

    private static Vector2 PointAround(Vector2 origin, float minDist, float maxDist)
    {
        float angle = Random.Shared.NextSingle() * MathF.Tau;
        float dist = minDist + Random.Shared.NextSingle() * (maxDist - minDist);
        return origin + new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * dist;
    }

    /// <summary>Requests a player shot; honoured only if off cooldown with ammo.</summary>
    public void FirePlayerShot()
    {
        if (Player.TryFire(out Vector2 origin, out Vector2 dir, out float launchHeight))
            SpawnProjectile(origin, dir, fromPlayer: true, launchHeight: launchHeight);
    }

    /// <summary>Requests a heavy grenade; honoured only if off cooldown with 10 ammo.</summary>
    public void FirePlayerGrenade()
    {
        if (Player.TryFireGrenade(out Vector2 origin, out Vector2 dir))
            SpawnProjectile(origin, dir, fromPlayer: true, grenade: true);
    }

    private void SpawnProjectile(Vector2 origin, Vector2 dir, bool fromPlayer,
        bool grenade = false, float launchHeight = Projectile.BoltHeight)
    {
        foreach (var p in _projectiles)
        {
            if (p.Active) continue;
            if (grenade) p.FireGrenade(origin, dir, fromPlayer);
            else p.Fire(origin, dir, fromPlayer, launchHeight);
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

            // A player's air shot that ran its course without hitting anything comes
            // down on the horizon: a far-off blast and a puff of debris out where it
            // fell, the explosion fading with distance.
            if (p.JustExpired && p.IsAirShot && p.FromPlayer)
                ExplodeAirShot(p);

            if (!p.Active) continue;

            if (p.FromPlayer)
            {
                // The Crab-Core's only weak spot: a level air shot threading the
                // raised neon core. Checked before the tanks, since the core sits
                // where nothing else does — high overhead.
                if (!p.IsGrenade && Boss is { } liveBoss && liveBoss.HitsCore(p.Position, p.Height))
                {
                    if (liveBoss.DamageCore(PlayerShotDamage))
                        DestroyBoss(liveBoss);
                    else
                        Audio.PlayHit();           // a sharp core-ping on a non-lethal hit
                    p.Active = false;
                    continue;
                }

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
    /// Stages the horizon detonation of a spent air shot: a spark-and-chunk burst
    /// where it came down and the explosion clip played back at a distance-faded
    /// volume, so a shot fizzling far downrange reads as a faint thud on the skyline.
    /// </summary>
    private void ExplodeAirShot(Projectile p)
    {
        var origin = new Vector3(p.Position.X, MathF.Max(0.4f, p.Height), p.Position.Y);
        Debris.Burst(origin, Palette.Flag, elite: false);
        Audio.PlayExplosionAt(Vector2.Distance(p.Position, Player.Position));
    }

    /// <summary>
    /// The Crab-Core's death: its parts blow apart in a rain of debris and the whole
    /// rig glitches out (the renderer drives the tearing from the boss's death
    /// progress). A full-volume blast at the core, a chassis burst at the body, and a
    /// shower from each leg where it planted, so the giant comes apart everywhere at
    /// once rather than popping like a tank.
    /// </summary>
    private void DestroyBoss(CrabCore boss)
    {
        Audio.PlayExplosion();

        var c = boss.Position;
        // The core itself, up high where the gem sat — a hot neon-red burst.
        Debris.Burst(new Vector3(c.X, CrabCore.CoreHitHeight, c.Y), Palette.NeonRed, elite: true);
        // The carapace shattering at mid-body height.
        Debris.Burst(new Vector3(c.X, CrabRig.BodyHeight * CrabRig.Scale, c.Y),
            Palette.CrabChassis, elite: true);
        // Each leg flings its own chunks from where it met the floor.
        foreach (var leg in CrabRig.Legs)
        {
            Vector2 foot = CrabRig.FootWorldXZ(leg, c, boss.Heading);
            Debris.Burst(new Vector3(foot.X, 1.0f, foot.Y), Palette.CrabChassis, elite: false);
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
