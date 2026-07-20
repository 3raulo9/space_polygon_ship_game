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

    /// <summary>
    /// The boss's execution cinematic while it has hold of the player, or null the
    /// rest of the time. While one exists it owns the player's transform outright and
    /// the renderer reads its shake, roll and glow — see <see cref="CrabSeizure"/>.
    /// </summary>
    public CrabSeizure? Seizure { get; private set; }

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
    private const float PickupRefill = 0.30f;

    // --- Dynamic horizon spawning -------------------------------------------------
    // Nothing is pinned to a fixed spot. Hunters, salvage and the rare Crab-Core all
    // fade in out of the fog ring around the craft as it roams: each is rolled on its
    // own timer and dropped at a random bearing on the far horizon, so the field is
    // built by where the player goes rather than pre-placed. Turned off for the
    // capture harness and the headless self-test, which want a fixed, known scene.
    public bool DynamicSpawning = true;

    // The fog band new arrivals drop into — past the near fog so they resolve as
    // blips on the skyline, yet close enough to eventually drift into play.
    private const float SpawnMinRange = 72f;
    private const float SpawnMaxRange = 120f;

    // Hunters: keep at most this many on the field. Rolls come at a slow cadence, and
    // when the field is already full the farthest hunter (drifted off into the fog) is
    // let go so a fresh one can always resolve on the horizon — spawning never stalls.
    private const int MaxEnemies = 4;
    private const float EnemySpawnInterval = 9f;
    private const float EnemySpawnChance = 0.5f;
    private const float EliteChance = 0.22f;
    private float _enemyTimer;

    // Floating salvage: a slow, endless drip. At the cap the farthest piece drifts out
    // of play and a new one fades in, so batteries and rounds never stop appearing.
    private const int MaxPickups = 7;
    private const float PickupSpawnInterval = 7f;
    private const float PickupSpawnChance = 0.6f;
    private const float BatteryShare = 0.6f;   // this fraction of new salvage is batteries
    private float _pickupTimer;

    // The Crab-Core is rare: while none stalks the field, roll infrequently for one
    // to rise out of the fog at a random bearing — never at a fixed spot.
    private const float BossSpawnInterval = 18f;
    private const float BossSpawnChance = 0.14f;
    private float _bossTimer;

    public World()
    {
        Player = new PlayerTank(Vector2.Zero);

        _projectiles = new Projectile[MaxProjectiles];
        for (int i = 0; i < _projectiles.Length; i++)
            _projectiles[i] = new Projectile();

        // Capture overrides freeze a controlled scene for the verification harness:
        // exact point-blank placements and no drifting-in spawns.
        string? nearPickup = Environment.GetEnvironmentVariable("VOIDTANKS_PICKUP_NEAR");
        string? nearEnemy = Environment.GetEnvironmentVariable("VOIDTANKS_ENEMY_NEAR");
        string? nearBoss = Environment.GetEnvironmentVariable("VOIDTANKS_BOSS_NEAR");
        bool capture = nearPickup == "1" || nearEnemy is "1" or "elite" || nearBoss == "1";
        if (capture) DynamicSpawning = false;

        // Salvage. Capture seeds one battery and one round dead ahead; play seeds a
        // small starter field at random fog bearings — no fixed spots — so there's
        // salvage on the horizon from the first frame, then the director tops it up.
        if (nearPickup == "1")
        {
            Pickups.Add(new Pickup(new Vector2(-2.5f, 14f), PickupKind.Battery));
            Pickups.Add(new Pickup(new Vector2(2.5f, 14f), PickupKind.Ammo));
        }
        else
        {
            for (int i = 0; i < 3; i++)
                Pickups.Add(new Pickup(RandomPointAroundPlayer(SpawnMinRange, SpawnMaxRange), PickupKind.Battery));
            for (int i = 0; i < 2; i++)
                Pickups.Add(new Pickup(RandomPointAroundPlayer(SpawnMinRange, SpawnMaxRange), PickupKind.Ammo));
        }

        // One hunter to open on. Capture drops it in close on-axis so its polygon
        // silhouette can be inspected; play fades it in from a random bearing out in
        // the fog, and the director adds more over time.
        if (nearEnemy == "1")
            Enemies.Add(new EnemyTank(new Vector2(4f, 16f), elite: false));
        else if (nearEnemy == "elite")
            Enemies.Add(new EnemyTank(new Vector2(4f, 16f), elite: true));
        else
            Enemies.Add(new EnemyTank(RandomPointAroundPlayer(60f, 80f), elite: false));

        // The Crab-Core is no longer pre-placed on the field. A capture override drops
        // it in point-blank for screenshots; in play it starts absent and rises rarely
        // out of the fog via the spawn director.
        Boss = nearBoss == "1" ? new CrabCore(new Vector2(0f, 44f)) : null;
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
        // planted this tick, kick up a puff of grid dust under the foot — and thud
        // out a stomp, mixed by how close the nearest planting foot is to the
        // player so the gait swells as the crab closes in and is a faint tremor
        // while it's still stalking across the arena.
        // The seizure runs before the boss does, because while one is live it owns
        // the player's transform and the boss is frozen in the hold — stepping them
        // the other way round would let the crab chase a position the cinematic is
        // about to overwrite.
        UpdateSeizure(dt);

        if (Boss is { } boss)
        {
            if (boss.Update(dt, Player.Position))
                Audio.PlayClamp();
            // Every planting foot is voiced on its own — a tripod lands together, so
            // this is three overlapping impacts, each with its own limb's pitch and
            // its own distance to the player.
            foreach (var f in boss.Footfalls)
            {
                Debris.FootPuff(new Vector3(f.Pos.X, 0f, f.Pos.Y));
                Audio.PlayFootstep(f.Leg, Vector2.Distance(f.Pos, Player.Position));
            }

            // Its machinery hums the whole time it exists, spooling up the moment it
            // notices the player. Fed every tick; Audio eases the rate and level.
            Audio.SetBossHum(true, Vector2.Distance(boss.Position, Player.Position), boss.Agitation);

            UpdateBeam(boss, dt);

            // Once the death glitch has fully torn the rig apart, drop the boss so it
            // stops updating and the director is free to raise a new one later.
            if (boss.Dead) Boss = null;
        }
        else
        {
            // No boss on the field — let the rotor fade out and stop.
            Audio.SetBossHum(false, 0f, 0f);
        }

        UpdateProjectiles(dt);
        UpdatePickups(dt);
        if (DynamicSpawning) UpdateSpawning(dt);
        Debris.Update(dt);
        Enemies.RemoveAll(e => !e.Alive);
    }

    /// <summary>What standing in the Crab-Core's beam costs, per damage tick.</summary>
    private const float BeamDamage = 9f;

    /// <summary>
    /// How often the beam bites while the player is inside it. Ticked rather than
    /// applied per-frame for two reasons: the damage stops depending on the frame
    /// rate, and each bite fires the hit cue, which at 60Hz would be a solid tone
    /// rather than the sound of being hurt repeatedly.
    /// </summary>
    private const float BeamTickInterval = 0.35f;

    private float _beamTick;

    /// <summary>
    /// Applies the Crab-Core's beam to the player: while it is burning, anything
    /// inside the shaft takes a bite every <see cref="BeamTickInterval"/>.
    ///
    /// The test is a plain point-to-ray distance in 3D, which is exactly what the
    /// renderer draws — so what looks like standing in the light is standing in the
    /// light. Because the boss locked its direction before firing, the player's own
    /// movement is the entire defence: walk out of the line and the beam keeps
    /// burning empty grid for the rest of its five seconds.
    /// </summary>
    private void UpdateBeam(CrabCore boss, float dt)
    {
        if (!boss.BeamActive)
        {
            // Reset between shots so stepping into a fresh beam bites immediately
            // rather than on whatever was left of the last one's clock.
            _beamTick = 0f;
            return;
        }

        var target = new Vector3(Player.Position.X, Player.Height + 1f, Player.Position.Y);
        Vector3 from = boss.BeamOrigin;
        Vector3 dir = boss.BeamDirection;

        // Distance from the craft to the beam's axis, clamped to the shaft's own
        // length so the ray doesn't reach backwards out of the emitter.
        float along = Math.Clamp(Vector3.Dot(target - from, dir), 0f, CrabCore.BeamLength);
        float miss = Vector3.Distance(target, from + dir * along);

        _beamTick -= dt;
        if (miss > CrabCore.BeamRadius + PlayerTank.Radius) return;
        if (_beamTick > 0f) return;

        _beamTick = BeamTickInterval;
        DamagePlayer(BeamDamage);
    }

    /// <summary>
    /// Runs the boss's execution cinematic: starts one when a hunting Crab-Core has
    /// closed to arm's length, steps the live one, and applies the two moments that
    /// actually cost the player anything — the claw's blow and the landing.
    ///
    /// A seizure is held for its whole length, recovery included, and no new one can
    /// begin until it has finished. That window is what stops a crab standing over a
    /// grounded player and grabbing them again the instant they land, which would be
    /// an inescapable loop rather than a set piece.
    /// </summary>
    private void UpdateSeizure(float dt)
    {
        if (Seizure is { } active)
        {
            switch (active.Update(dt))
            {
                case CrabSeizure.Event.Struck:
                    DamagePlayer(CrabSeizure.StrikeDamage);
                    break;
                case CrabSeizure.Event.Landed:
                    // A fraction of the shield's maximum, so the landing costs the
                    // same whatever state the player was in when they were caught.
                    DamagePlayer(Player.MaxShield * CrabSeizure.LandingDamageFraction);
                    break;
            }

            if (!active.Active) Seizure = null;
            return;
        }

        if (Boss is { } boss && CrabSeizure.CanSeize(boss, Player))
            Seizure = new CrabSeizure(boss, Player);
    }

    /// <summary>
    /// The horizon spawn director: on independent timers, rolls to raise a new hunter,
    /// drift in fresh salvage, or — rarely — bring up a Crab-Core, always at a random
    /// bearing out in the fog around the roaming craft and only while under each cap.
    /// </summary>
    private void UpdateSpawning(float dt)
    {
        _enemyTimer += dt;
        if (_enemyTimer >= EnemySpawnInterval)
        {
            _enemyTimer = 0f;
            if (Random.Shared.NextSingle() < EnemySpawnChance)
            {
                // At the cap, release the farthest hunter so a fresh one always has room
                // to fade in — the population is bounded but the arrivals never stop.
                if (Enemies.Count >= MaxEnemies) RemoveFarthest(Enemies, e => e.Position);
                bool elite = Random.Shared.NextSingle() < EliteChance;
                Enemies.Add(new EnemyTank(RandomPointAroundPlayer(SpawnMinRange, SpawnMaxRange), elite));
            }
        }

        _pickupTimer += dt;
        if (_pickupTimer >= PickupSpawnInterval)
        {
            _pickupTimer = 0f;
            if (Random.Shared.NextSingle() < PickupSpawnChance)
            {
                // Same rule for salvage: when full, the farthest piece drifts out and a
                // new one drifts in, so batteries and rounds keep coming forever.
                if (Pickups.Count >= MaxPickups) RemoveFarthest(Pickups, pk => pk.Position);
                var kind = Random.Shared.NextSingle() < BatteryShare ? PickupKind.Battery : PickupKind.Ammo;
                Pickups.Add(new Pickup(RandomPointAroundPlayer(SpawnMinRange, SpawnMaxRange), kind));
            }
        }

        _bossTimer += dt;
        if (_bossTimer >= BossSpawnInterval)
        {
            _bossTimer = 0f;
            // Only ever one crab, and only when the field is clear of a live one.
            if (Boss is null && Random.Shared.NextSingle() < BossSpawnChance)
                Boss = new CrabCore(RandomPointAroundPlayer(SpawnMinRange, SpawnMaxRange));
        }
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

    /// <summary>A random point on the plane at [min,max] from the player.</summary>
    /// <summary>
    /// Debug hatch: plants a Crab-Core directly ahead of the player's current heading,
    /// far enough out that it sits outside <see cref="CrabCore.DetectRadius"/> and stays
    /// dormant — so a tester can walk up on a sleeping boss and choose the moment it
    /// wakes. Replaces any Crab-Core already on the field rather than stacking a second.
    /// </summary>
    public void SpawnCrabAhead()
    {
        // A margin past the wake radius: close enough to see and approach, but the
        // boss is unmistakably still asleep when it lands.
        const float Ahead = CrabCore.DetectRadius + 15f;
        Boss = new CrabCore(Player.Position + Player.Forward * Ahead);
    }

    private Vector2 RandomPointAroundPlayer(float minDist, float maxDist)
        => PointAround(Player.Position, minDist, maxDist);

    /// <summary>Drops the item farthest from the player from a list — used to make room
    /// for a fresh spawn so a full field never blocks new arrivals.</summary>
    private void RemoveFarthest<T>(List<T> list, Func<T, Vector2> posOf)
    {
        if (list.Count == 0) return;
        int farthest = 0;
        float best = Vector2.DistanceSquared(posOf(list[0]), Player.Position);
        for (int i = 1; i < list.Count; i++)
        {
            float d = Vector2.DistanceSquared(posOf(list[i]), Player.Position);
            if (d > best) { best = d; farthest = i; }
        }
        list.RemoveAt(farthest);
    }

    private static Vector2 PointAround(Vector2 origin, float minDist, float maxDist)
    {
        float angle = Random.Shared.NextSingle() * MathF.Tau;
        float dist = minDist + Random.Shared.NextSingle() * (maxDist - minDist);
        return origin + new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * dist;
    }

    /// <summary>
    /// Debug hatch: drops one random enemy from the live roster onto the horizon at
    /// a random bearing — a standard hunter, an elite hunter, or (only if the field
    /// is clear of one) a Crab-Core. Wired to the in-game 'L' key so a tester can
    /// stack up threats on demand without waiting on the spawn director. Respects the
    /// same caps as the director: at the hunter cap the farthest is released first.
    /// </summary>
    public void SpawnRandomEnemy()
    {
        Vector2 at = RandomPointAroundPlayer(SpawnMinRange, SpawnMaxRange);

        // Weight toward tanks; only offer the boss when none stalks the field, so
        // the roll never wastes on an impossible pick.
        int forms = Boss is null ? 3 : 2;
        switch (Random.Shared.Next(forms))
        {
            case 0: // standard hunter
                if (Enemies.Count >= MaxEnemies) RemoveFarthest(Enemies, e => e.Position);
                Enemies.Add(new EnemyTank(at, elite: false));
                break;
            case 1: // elite hunter
                if (Enemies.Count >= MaxEnemies) RemoveFarthest(Enemies, e => e.Position);
                Enemies.Add(new EnemyTank(at, elite: true));
                break;
            default: // Crab-Core (only reachable when Boss is null)
                Boss = new CrabCore(at);
                break;
        }
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
                        // A shriek over the impact, pitched by how far gone the core
                        // is — the boss loses composure as it's worn down.
                        Audio.PlayCoreHit(1f - liveBoss.CoreFraction);
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
        // Not the stock one-shot blast the tanks get — the boss gets its own death:
        // a falling scream over a cascade of pitched detonations, spread across the
        // glitch-apart animation rather than fired all at once.
        Audio.PlayBossDeath();

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
