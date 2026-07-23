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

    /// <summary>The hangar build this stage was spun up for — the chassis, its point
    /// spend and its paint job. Read by the renderer to colour the craft's own parts,
    /// and by nothing in the sim: the stats were baked into the player at construction.</summary>
    public readonly Loadout Loadout;

    public readonly List<EnemyTank> Enemies = new();
    public readonly DebrisSystem Debris = new();

    /// <summary>Floating salvage scattered on the grid — batteries and stray rounds.</summary>
    public readonly List<Pickup> Pickups = new();

    /// <summary>The player's carried goods — filled by driving over salvage, spent from
    /// the inventory panel (E). Salvage no longer charges the craft on contact; it is
    /// stowed here and applied by hand.</summary>
    public readonly Inventory Inventory = new();

    /// <summary>Live CRAB CORE detonations — each a brief ring of lances raking the
    /// grid. Rare and short-lived, so a plain list rather than a pool.</summary>
    public readonly List<CrabCoreBlast> Blasts = new();

    /// <summary>The lone Crab-Core boss seeded into the stage, or null. Runs its
    /// own Stalker Protocol against the player independent of the tank combat.</summary>
    public CrabCore? Boss { get; private set; }

    /// <summary>
    /// The boss's execution cinematic while it has hold of the player, or null the
    /// rest of the time. While one exists it owns the player's transform outright and
    /// the renderer reads its shake, roll and glow — see <see cref="CrabSeizure"/>.
    /// </summary>
    public CrabSeizure? Seizure { get; private set; }

    /// <summary>The lone Maw-Core hanging over the stage, or null. Hovers at the top
    /// of the player's jump and runs its own hunt — see <see cref="MawCore"/>.</summary>
    public MawCore? Maw { get; private set; }

    /// <summary>
    /// The Maw-Core's digestion, while it has the player in its throat. Unlike the
    /// crab's seizure this one has no timer: it runs until the player shoots their way
    /// out of it or is eaten.
    /// </summary>
    public MawDigestion? Digestion { get; private set; }

    /// <summary>
    /// Whichever set piece currently owns the camera, or null. Only ever one at a
    /// time: both monsters refuse to start one on a player who is already
    /// <see cref="PlayerTank.Captured"/>, so the two can never overlap.
    /// </summary>
    public ICinematicView? Cinematic => (ICinematicView?)Seizure ?? Digestion;

    private readonly Projectile[] _projectiles;

    private const int MaxProjectiles = 64;
    private const float PlayerShotDamage = 1f;   // vs shield "points"
    private const float GrenadeDamage = 4f;      // heavy round, dealt to all in the blast
    private const float EnemyShotDamage = 12f;   // vs player's 100-point shield

    // Shield fraction at which the low-health alarm sounds. Crossing *down*
    // through this line fires warning.wav once — not once per frame below it.
    private const float LowShieldWarning = 0.45f;

    // What one battery is worth when spent from the pack: 30% of the shield *and* 30%
    // of the Hyper reserve. Public so the inventory panel's right-click charge reads
    // the same figure the salvage used to apply on contact.
    public const float BatteryChargeFraction = 0.30f;

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

    // The Maw-Core rolls on its own clock, independent of the crab's — the two are
    // different threats occupying different space (one on the grid, one in the air
    // over it) and meeting both at once is a legitimate, if unkind, situation. Rarer
    // than a crab: a thing that eats you should not be routine.
    private const float MawSpawnInterval = 22f;
    private const float MawSpawnChance = 0.11f;
    private float _mawTimer;

    /// <summary>
    /// Builds a stage for the given hangar build. A null loadout is the standard TANK
    /// on a straight 5/5/5 — the craft this game had before the hangar existed — which
    /// is what the headless self-test and the capture harness both want.
    /// </summary>
    public World(Loadout? loadout = null)
    {
        Loadout = loadout ?? new Loadout();
        Player = new PlayerTank(Vector2.Zero, 0f, Loadout);

        _projectiles = new Projectile[MaxProjectiles];
        for (int i = 0; i < _projectiles.Length; i++)
            _projectiles[i] = new Projectile();

        // Capture overrides freeze a controlled scene for the verification harness:
        // exact point-blank placements and no drifting-in spawns.
        string? nearPickup = Environment.GetEnvironmentVariable("VOIDTANKS_PICKUP_NEAR");
        string? nearEnemy = Environment.GetEnvironmentVariable("VOIDTANKS_ENEMY_NEAR");
        string? nearBoss = Environment.GetEnvironmentVariable("VOIDTANKS_BOSS_NEAR");
        string? nearMaw = Environment.GetEnvironmentVariable("VOIDTANKS_MAW_NEAR");
        bool capture = nearPickup == "1" || nearEnemy is "1" or "elite"
                    || nearBoss is "1" or "seize" || nearMaw is "1" or "swallow";
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
        // "seize" stands the boss right on top of the player instead, inside
        // CrabSeizure.GrabRadius, so the protocol runs itself up to pursuit and the
        // grab fires on its own a second or two in — the only way to get a screenshot
        // of the cinematic, which otherwise needs a human to let the thing corner them.
        Boss = nearBoss switch
        {
            "1"     => new CrabCore(new Vector2(0f, 44f)),
            "seize" => new CrabCore(new Vector2(0f, 9f)),
            _       => null,
        };

        // Same arrangement for the Maw-Core. "swallow" hangs it directly over the
        // player's head, inside its own strike column, so it winds up and drops on a
        // stationary craft within a second — the only way to screenshot the digestion,
        // which otherwise needs a human willing to stand still and be eaten.
        Maw = nearMaw switch
        {
            "1"       => new MawCore(new Vector2(0f, 20f)),
            "swallow" => new MawCore(Vector2.Zero),
            _         => null,
        };
    }

    public IReadOnlyList<Projectile> Projectiles => _projectiles;

    public void Update(float dt, bool acceptCombatInput = true)
    {
        // Read player actions from global input, then run the shared sim step. The
        // step itself is input-free so the headless self-test can reuse it.
        // Grenade takes precedence over the cannon on the frame both are held,
        // since they share the fire cooldown.
        // Inside the Maw-Core's throat every trigger is the same trigger. Normally the
        // grenade takes precedence on a frame both are held (they share a cooldown),
        // but honouring that here would mean a player mashing the heavy button while
        // being eaten never lands an escape shot and dies wondering why. There is
        // nothing to lob a splash round at inside a mouth, so both buttons route to
        // the one action that can save them.
        //
        // Combat input is muted while the inventory panel is open (acceptCombatInput
        // false): the mouse is busy managing items there, so a click on a stack must
        // never also loose a round. Movement still reads its own keys in StepForTest,
        // so the craft keeps drifting under the overlay.
        if (acceptCombatInput)
        {
            if (Digestion is { Held: true })
            {
                if (InputMap.Fire || InputMap.Grenade) FirePlayerShot();
            }
            else if (Player.Spider is { } spider)
                UpdateSpiderTriggers(spider, dt);
            else if (InputMap.Grenade)
                FirePlayerGrenade();
            else if (InputMap.Fire)
                FirePlayerShot();

            if (InputMap.HyperspacePressed)
                Player.TryHyperspace();
        }
        else
        {
            // The panel is up and the mouse belongs to it. Drop any part-wound lance
            // rather than letting it sit charged (and the craft rooted) behind an
            // overlay the player is busy dragging items around in.
            Player.Spider?.Cancel();
            if (Player.Spider != null) Audio.SetLanceCharge(false, 0f);
            Player.Rooted = false;
        }

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
        ResolveStructureCollisions();

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
                Audio.PlayFootstep(f.Leg, Torus.Distance(f.Pos, Player.Position));
            }

            // Its machinery hums the whole time it exists, spooling up the moment it
            // notices the player. Fed every tick; Audio eases the rate and level.
            Audio.SetBossHum(true, Torus.Distance(boss.Position, Player.Position), boss.Agitation);

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

        // The hanging mouth runs on the same pattern: its set piece steps first,
        // because while one is live it owns the player's transform and the monster is
        // frozen around them.
        UpdateDigestion(dt);
        UpdateMaw(dt);

        UpdateProjectiles(dt);
        UpdateCrabBlasts(dt);
        UpdateStructures(dt);
        UpdatePickups(dt);
        if (DynamicSpawning) UpdateSpawning(dt);
        Debris.Update(dt);
        Enemies.RemoveAll(e => !e.Alive);
    }

    // --- The skyline ------------------------------------------------------------

    /// <summary>
    /// The dead alien city standing on this torus — towers and the arcs slung between
    /// them. The layout is a fixed feature of the world: the same buildings stand in the
    /// same places in every run (see <see cref="StructureField"/>), which is what makes
    /// them landmarks instead of scenery. The <em>instances</em> belong to this stage,
    /// because they can be cut down.
    /// </summary>
    public readonly List<Structure> Structures = StructureField.Create();

    /// <summary>
    /// Ages any collapse in progress and lets the finished ones go. The tick a mass
    /// lands, it throws dust off the grid where it came down and thuds at whatever
    /// volume the distance earns.
    /// </summary>
    private void UpdateStructures(float dt)
    {
        for (int i = Structures.Count - 1; i >= 0; i--)
        {
            var s = Structures[i];
            if (!s.Falling) continue;

            if (s.Update(dt))
            {
                // The impact: a long spray of dust down the line the mass fell along,
                // rather than one puff at the middle, since the thing that just hit the
                // grid is forty metres of it.
                var along = new Vector2(MathF.Cos(s.Heading), -MathF.Sin(s.Heading));
                for (int k = 1; k <= 3; k++)
                    Debris.Burst(new Vector3(
                        s.Position.X + along.X * s.BlockHeight * 0.25f * k, 0.5f,
                        s.Position.Y + along.Y * s.BlockHeight * 0.25f * k),
                        Palette.StructureShell, elite: false);
                Audio.PlayExplosionAt(Torus.Distance(s.Position, Player.Position));
            }

            if (s.Gone) Structures.RemoveAt(i);
        }
    }

    /// <summary>
    /// Cuts down whatever standing structure is nearest along a beam, and stages the
    /// collapse: dust off it at several heights and a blast pitched by the distance.
    ///
    /// Only beams do this. A round of any kind — the cannon, the heavy grenade, an
    /// enemy's shot, the SPIDER's laser bolt — stops dead against a wall and leaves it
    /// standing (see <see cref="BlockShotOnStructure"/>). The distinction is the whole
    /// point of the buildings: they are cover, and cover you can shoot through is not
    /// cover. What defeats them is the thing that was never a bullet in the first place —
    /// the Crab-Core's lance, the SPIDER's charged beam, the ring thrown off a detonating
    /// CRAB CORE — and a wall coming down under one of those should be worth watching.
    ///
    /// The whole shaft is swept rather than stopping at the first hit, because a beam
    /// that has already cut through one tower has visibly not stopped, and a second wall
    /// standing untouched in the same light would read as a bug.
    /// </summary>
    /// <returns>How many structures this cut down.</returns>
    private int CutStructuresAlong(Vector3 origin, Vector3 direction, float length, float radius)
    {
        var originXZ = new Vector2(origin.X, origin.Z);
        var dirXZ = new Vector2(direction.X, direction.Z);
        float planar = dirXZ.Length();
        if (planar > 1e-4f) dirXZ /= planar;
        // How fast the shaft climbs per unit of ground covered — a beam loosed from the
        // top of a jump sails over the low legs of an arc, and should.
        float slope = planar > 1e-4f ? direction.Y / planar : 0f;

        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        int cut = 0;

        foreach (var s in Structures)
        {
            int n = s.Blockers(blockers);
            for (int b = 0; b < n; b++)
            {
                var (at, r) = blockers[b];
                Vector2 near = Torus.NearestImage(at, originXZ);
                float along = Math.Clamp(Vector2.Dot(near - originXZ, dirXZ), 0f, length);
                if (Vector2.Distance(near, originXZ + dirXZ * along) > r + radius) continue;

                // Passing overhead is passing overhead, however wide the shaft is.
                float beamY = origin.Y + slope * along;
                if (beamY > s.BlockHeight + radius) continue;

                if (FellStructure(s)) cut++;
                break;   // one strike per structure, whichever leg it was
            }
        }

        return cut;
    }

    /// <summary>
    /// Starts a structure's collapse and dresses it: chunks blown off the mass at three
    /// heights up its body, so a tall thing comes apart down its length rather than
    /// popping at the base, and the report of it carrying to wherever the player is.
    /// Silently does nothing to something already on its way down.
    /// </summary>
    private bool FellStructure(Structure s)
    {
        if (!s.Strike()) return false;

        float top = s.BlockHeight;
        for (int i = 1; i <= 3; i++)
            Debris.Burst(new Vector3(s.Position.X, top * (i / 4f), s.Position.Y),
                i == 1 ? Palette.StructureGlow : Palette.StructureShell, elite: i == 3);

        Audio.PlayExplosionAt(Torus.Distance(s.Position, Player.Position));
        return true;
    }

    /// <summary>
    /// Stops a round against the skyline. Returns true if the shot struck a wall, having
    /// already spent it: the projectile is killed, a scatter of masonry is thrown off the
    /// point of impact and the hit is heard at whatever the range earns.
    ///
    /// Height is honoured, which is what makes an arc worth driving through: its legs are
    /// short, so a shot passes under the span the same way the craft does. Towers reach
    /// far higher than anything can shoot, so they simply stop everything.
    /// </summary>
    private bool BlockShotOnStructure(Projectile p)
    {
        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];

        foreach (var s in Structures)
        {
            if (p.Height > s.BlockHeight) continue;

            int n = s.Blockers(blockers);
            for (int b = 0; b < n; b++)
            {
                var (at, r) = blockers[b];
                if (Torus.DistanceSquared(p.Position, at) > r * r) continue;

                // A thrown CRAB CORE is not a round — it is a bomb that happened to hit a
                // wall, so it does what it would have done anywhere else, and the ring of
                // lances it throws is perfectly capable of bringing the wall down.
                if (p.IsCrabBomb)
                {
                    StageCrabBlast(p.Position);
                }
                else
                {
                    Debris.Burst(new Vector3(p.Position.X, MathF.Max(0.4f, p.Height), p.Position.Y),
                        Palette.StructureShell, elite: false);
                    Audio.PlayExplosionAt(Torus.Distance(p.Position, Player.Position));
                }

                p.Active = false;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Keeps the craft out of the solid parts of the skyline: a tower's footprint, an
    /// arc's two legs. Anything overlapped this tick is resolved by sliding the craft
    /// straight back out along the shortest way — which, because it never touches the
    /// player's speed or heading, reads as scraping along a wall rather than as being
    /// stopped by one, and can't leave the craft stuck inside geometry with nowhere to go.
    ///
    /// Run right after the player's own move, so nothing else this tick — aiming,
    /// shooting, a monster measuring the range — ever sees the craft inside a building.
    /// A captured player is skipped outright: a set piece owns the transform and is
    /// entitled to drag them through a wall if that is where the claw goes.
    ///
    /// Nothing else on the field collides with the city. Hunters drive through it, and
    /// so do rounds and beams — buildings are terrain to navigate, not cover, and giving
    /// the AI walls to path around is a much larger piece of work than this.
    /// </summary>
    private void ResolveStructureCollisions()
    {
        if (Player.Captured) return;

        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];

        for (int i = 0; i < Structures.Count; i++)
        {
            int n = Structures[i].Blockers(blockers);
            for (int b = 0; b < n; b++)
            {
                var (at, radius) = blockers[b];
                float reach = radius + PlayerTank.Radius;

                // Measured the short way round the torus, so a building sitting over the
                // seam blocks the craft that has just wrapped past it.
                Vector2 out_ = Torus.Delta(at, Player.Position);
                float distSq = out_.LengthSquared();
                if (distSq >= reach * reach) continue;

                // Dead centre (only reachable if something teleported the craft into a
                // pylon): shove it out along a fixed axis rather than dividing by zero.
                Vector2 push = distSq > 1e-6f
                    ? out_ / MathF.Sqrt(distSq) * reach
                    : new Vector2(reach, 0f);

                Player.Position = Torus.Wrap(at + push);
            }
        }
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

        // Whatever the lance is standing in, it cuts down. Swept every tick it burns
        // rather than once when it lights: the shaft is fixed but the buildings are only
        // struck once each (Structure.Strike refuses a second), so this costs one pass
        // over a short list and gains the case where the first tower falls out of the way
        // and reveals the next one in line, which then goes too.
        CutStructuresAlong(boss.BeamOrigin, boss.BeamDirection, CrabCore.BeamLength, CrabCore.BeamRadius);

        // Measure the player against the boss's nearest image across the torus, so a
        // beam fired near the world's edge still burns the craft standing just over it.
        Vector2 nearPlayer = Torus.NearestImage(Player.Position, boss.Position);
        var target = new Vector3(nearPlayer.X, Player.Height + 1f, nearPlayer.Y);
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

    // --- The Maw-Core ---------------------------------------------------------

    /// <summary>What one of the mouth's little lasers costs. Less than a hunter's
    /// round: they are slow enough to walk away from, so a player who eats one has
    /// usually chosen to stand their ground and shoot back, and that shouldn't be
    /// punished as hard as being caught out by a tank.</summary>
    private const float MawLaserDamage = 7f;

    // Cadences for the mouth's two continuous layers. Held here rather than in the
    // entity so the simulation stays free of anything that has to know about ranges
    // and mixing — exactly where the crab's hunting call lives.
    private const float MawTeethInterval = 1.1f;
    private const float MawCrystalInterval = 2.4f;
    private float _mawTeethTimer;
    private float _mawCrystalTimer;

    /// <summary>
    /// Steps the Maw-Core: its own hunt, the lasers it has in the air, and the two
    /// sound layers that run the whole time it exists. Its hover bed is fed every tick
    /// the way the crab's rotor is, and faded out on the ticks where there is nothing
    /// up there.
    /// </summary>
    private void UpdateMaw(float dt)
    {
        if (Maw is not { } maw)
        {
            Audio.SetMawHover(false, 0f, 0f);
            return;
        }

        if (maw.Update(dt, Player.Position, Player.Height))
            Audio.PlayMawSpit(Torus.Distance(maw.Position, Player.Position));

        float dist = Torus.Distance(maw.Position, Player.Position);
        Audio.SetMawHover(true, dist, maw.Agitation);

        // The teeth and the crystal, each on their own unrelated clock so the two
        // never fall into a rhythm together. Suppressed while it is digesting — the
        // cinematic voices its own, much closer, grinding.
        if (maw.Alive && !maw.Digesting)
        {
            _mawTeethTimer -= dt;
            if (_mawTeethTimer <= 0f)
            {
                _mawTeethTimer = MawTeethInterval;
                Audio.PlayMawTeeth(dist);
            }

            _mawCrystalTimer -= dt;
            if (_mawCrystalTimer <= 0f)
            {
                _mawCrystalTimer = MawCrystalInterval;
                Audio.PlayMawCrystal(dist, maw.Agitation);
            }
        }

        UpdateMawLasers(maw);

        // Once the death glitch has fully torn it apart, drop it so the director is
        // free to hang a new one later.
        if (maw.Dead) Maw = null;
    }

    /// <summary>
    /// Applies the mouth's little lasers to the player. Unlike a hunter's round these
    /// bite an airborne craft too: the jump is the counter to everything on the grid,
    /// and this monster's whole point is that it forces you into the air — sparing a
    /// leaping player would mean the safe answer is to simply never come down.
    /// </summary>
    private void UpdateMawLasers(MawCore maw)
    {
        // Nothing can touch a player inside a set piece; they cannot act, so they
        // must not be shot at by anything else either.
        if (Player.Captured) return;

        // The lasers live in absolute coordinates around the maw; measure the craft
        // against the maw's nearest image so a bolt still bites a player just over the
        // seam from it.
        Vector2 nearPlayer = Torus.NearestImage(Player.Position, maw.Position);
        var craft = new Vector3(nearPlayer.X, Player.Height + 1f, nearPlayer.Y);
        float reach = MawCore.LaserRadius + PlayerTank.Radius;

        var lasers = maw.Lasers;
        for (int i = 0; i < lasers.Length; i++)
        {
            if (!lasers[i].Active) continue;
            if (Vector3.DistanceSquared(lasers[i].Position, craft) > reach * reach) continue;

            maw.ConsumeLaser(i);      // one bolt, one bite
            DamagePlayer(MawLaserDamage);
        }
    }

    /// <summary>
    /// Runs the digestion: starts one on the tick a lunging mouth closes over the
    /// player, steps the live one, and applies the two things that cost anything — the
    /// bites while they are held, and the landing when they are spat out.
    /// </summary>
    private void UpdateDigestion(float dt)
    {
        if (Digestion is { } active)
        {
            switch (active.Update(dt))
            {
                case MawDigestion.Event.Bitten:
                    // A fraction of the maximum, so being eaten costs the same share
                    // of a life whatever state the player was caught in.
                    DamagePlayer(Player.MaxShield * MawDigestion.BiteFraction);
                    break;
                case MawDigestion.Event.Landed:
                    DamagePlayer(Player.MaxShield * MawDigestion.LandingFraction);
                    break;
            }

            if (!active.Active) Digestion = null;
            return;
        }

        if (Maw is { } maw && MawDigestion.CanSwallow(maw, Player))
            Digestion = new MawDigestion(maw, Player);
    }

    /// <summary>
    /// The Maw-Core's death: the shell blows apart, the teeth go everywhere and the
    /// whole thing stops holding itself up. Bursts are staged at the three heights the
    /// rig actually occupies rather than at one centre, so a tall floating thing comes
    /// apart down its length instead of popping like a tank.
    /// </summary>
    private void DestroyMaw(MawCore maw)
    {
        Audio.PlayMawDeath();

        var c = maw.Position;
        float body = maw.BodyY;
        // The crystal, up in the well — a hot neon burst where the weak spot was.
        Debris.Burst(new Vector3(c.X, body + MawRig.CrystalLocalY * MawRig.Scale, c.Y),
            Palette.NeonRed, elite: true);
        // The shell shattering at the seam where its middle used to be.
        Debris.Burst(new Vector3(c.X, body, c.Y), Palette.MawShell, elite: true);
        // And the teeth, thrown off the ring they were still turning on.
        Debris.Burst(new Vector3(c.X, body + MawRig.ToothLocalY * MawRig.Scale, c.Y),
            Palette.MawTooth, elite: false);
    }

    /// <summary>
    /// Debug hatch: hangs a Maw-Core well ahead of the player, outside its own detect
    /// radius so it drifts nowhere until they walk up on it. Replaces any already on
    /// the field rather than stacking a second.
    /// </summary>
    public void SpawnMawAhead()
    {
        const float Ahead = MawCore.DetectRadius + 15f;
        Maw = new MawCore(Player.Position + Player.Forward * Ahead);
    }

    /// <summary>Test hatch: hangs a prepared Maw-Core on the field, so the headless
    /// self-test can drive a swallow end to end against a real world rather than
    /// against the entity in isolation.</summary>
    public void AttachMawForTest(MawCore maw) => Maw = maw;

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

        _mawTimer += dt;
        if (_mawTimer >= MawSpawnInterval)
        {
            _mawTimer = 0f;
            // Only ever one mouth, and never while one is already up there.
            if (Maw is null && Random.Shared.NextSingle() < MawSpawnChance)
                Maw = new MawCore(RandomPointAroundPlayer(SpawnMinRange, SpawnMaxRange));
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
        _fragmentToRemove = null;
        foreach (var pk in Pickups)
        {
            pk.Update(dt);
            if (Torus.DistanceSquared(pk.Position, Player.Position) <= reachSq)
                Collect(pk);
        }
        // A spent fragment is pulled off the field after the walk so the list isn't
        // mutated mid-iteration.
        if (_fragmentToRemove != null) Pickups.Remove(_fragmentToRemove);
    }

    /// <summary>
    /// Stows a driven-over pickup into the inventory, sounds the collect, and relocates
    /// it. Salvage no longer charges the craft on contact — a battery becomes one
    /// battery item, a stray round a random handful of bullets, a shard one fragment;
    /// the player spends them from the panel (E). Overflow that won't fit the pack is
    /// simply lost as the pickup drifts back out to the fog.
    /// </summary>
    private void Collect(Pickup pk)
    {
        ItemKind kind = pk.Kind switch
        {
            PickupKind.Battery      => ItemKind.Battery,
            PickupKind.CrabFragment => ItemKind.CrabFragment,
            _                       => ItemKind.Bullet,
        };
        Inventory.Add(kind, pk.Amount);

        Audio.PlayPickup();

        // A small sparkle in the pickup's colour marks the grab, reusing the debris
        // system (cosmetic only — it never touches damage or collision).
        Color spark = pk.Kind switch
        {
            PickupKind.Battery      => Palette.BatteryCore,
            PickupKind.CrabFragment => Palette.NeonRed,
            _                       => Palette.Flag,
        };
        Debris.Burst(new Vector3(pk.Position.X, pk.BobHeight, pk.Position.Y), spark, elite: false);

        // A collected fragment is spent, not endless salvage: drop it from the field
        // rather than respawning it in the fog. Batteries and rounds keep drifting back.
        if (pk.Kind == PickupKind.CrabFragment)
        {
            _fragmentToRemove = pk;
            return;
        }

        // Respawn out in the fog around the craft so it drifts back into view later.
        pk.Position = RandomPointAroundPlayer(45f, 100f);
        pk.Age = 0f;
    }

    // A fragment collected this tick, queued for removal after the pickup loop so the
    // list isn't mutated while it's being walked. Cleared each pass.
    private Pickup? _fragmentToRemove;

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

    /// <summary>
    /// Test hatch: hands the player a ready-made CRAB CORE weapon — equipped straight
    /// into the first empty R/T/Y/U slot so it can be thrown at once, or dropped in the
    /// pack if all four are taken. Wired to the same 'K' key that plants a Crab-Core, so
    /// one press both raises the enemy and arms you against it.
    /// </summary>
    public void GiveCrabCore()
    {
        for (int i = 0; i < Inventory.Weapons.Length; i++)
        {
            if (!Inventory.Weapons[i].IsEmpty) continue;
            Inventory.Weapons[i] = new ItemStack(ItemKind.CrabCore, 1);
            return;
        }
        Inventory.Add(ItemKind.CrabCore, 1);
    }

    /// <summary>Test hatch: stages a CRAB CORE blast a fixed distance ahead of the
    /// player, so the capture harness can photograph the cinematic without timing a throw.</summary>
    public void StageCrabBlastAheadForTest() => StageCrabBlast(Player.Position + Player.Forward * 20f);

    private Vector2 RandomPointAroundPlayer(float minDist, float maxDist)
        => PointAround(Player.Position, minDist, maxDist);

    /// <summary>Drops the item farthest from the player from a list — used to make room
    /// for a fresh spawn so a full field never blocks new arrivals.</summary>
    private void RemoveFarthest<T>(List<T> list, Func<T, Vector2> posOf)
    {
        if (list.Count == 0) return;
        int farthest = 0;
        float best = Torus.DistanceSquared(posOf(list[0]), Player.Position);
        for (int i = 1; i < list.Count; i++)
        {
            float d = Torus.DistanceSquared(posOf(list[i]), Player.Position);
            if (d > best) { best = d; farthest = i; }
        }
        list.RemoveAt(farthest);
    }

    private static Vector2 PointAround(Vector2 origin, float minDist, float maxDist)
    {
        float angle = Random.Shared.NextSingle() * MathF.Tau;
        float dist = minDist + Random.Shared.NextSingle() * (maxDist - minDist);
        // Fold back into the wrap window: a bearing past the world's edge lands on the
        // opposite side of the torus, which the renderer re-images near the player.
        return Torus.Wrap(origin + new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * dist);
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

        // Weight toward tanks; only offer each big form when the field is clear of
        // one, so the roll never wastes on an impossible pick.
        int forms = 2 + (Boss is null ? 1 : 0) + (Maw is null ? 1 : 0);
        int pick = Random.Shared.Next(forms);
        // With the crab already up, the boss slot is skipped and the mouth slides
        // down into index 2 — so the roll always lands on something that can spawn.
        if (pick == 2 && Boss is not null) pick = 3;
        switch (pick)
        {
            case 0: // standard hunter
                if (Enemies.Count >= MaxEnemies) RemoveFarthest(Enemies, e => e.Position);
                Enemies.Add(new EnemyTank(at, elite: false));
                break;
            case 1: // elite hunter
                if (Enemies.Count >= MaxEnemies) RemoveFarthest(Enemies, e => e.Position);
                Enemies.Add(new EnemyTank(at, elite: true));
                break;
            case 2: // Crab-Core (only reachable when Boss is null)
                Boss = new CrabCore(at);
                break;
            default: // Maw-Core (only reachable when Maw is null)
                Maw = new MawCore(at);
                break;
        }
    }

    /// <summary>
    /// Requests a player shot; honoured only if off cooldown with ammo.
    ///
    /// Fired from inside the Maw-Core's throat the round never becomes a projectile:
    /// there is nothing to aim at and nothing to miss at point-blank inside a mouth,
    /// so the shot is handed straight to the digestion as one of the three that break
    /// its hold. Note that the craft still pays for it in ammo and cooldown — being
    /// swallowed does not come with free bullets, and the cooldown is what makes the
    /// escape take a few seconds of being chewed rather than one panicked trigger pull.
    /// </summary>
    public void FirePlayerShot() => FirePlayerShot(laser: false);

    /// <summary>
    /// The player's ordinary trigger pull. <paramref name="laser"/> only changes what
    /// the round is drawn as (the SPIDER's emitter throws neon streaks where the tank
    /// throws bolts) — the ammo cost, the cooldown and the damage are the same round.
    /// </summary>
    public void FirePlayerShot(bool laser)
    {
        // The Crab-Core's seizure is a cutscene, not a trap: there is no shooting your
        // way out of it, so the trigger is dead for its duration and the round isn't
        // even spent. Checked before TryFire so a held player doesn't quietly burn
        // ammo on shots that go nowhere. This matters more than it used to — the gun
        // now cools while captured (so the Maw's escape can work), which without this
        // would let a seized player plink bolts out of the crab's claw all through the
        // scream.
        if (Seizure is { Held: true }) return;

        if (!Player.TryFire(out Vector2 origin, out Vector2 dir, out float launchHeight))
            return;

        if (Digestion is { } digestion && digestion.Held)
        {
            digestion.RegisterShot();
            Audio.PlayDetonation();
            return;
        }

        SpawnProjectile(origin, dir, fromPlayer: true, launchHeight: launchHeight, laser: laser);
    }

    // --- The SPIDER chassis -----------------------------------------------------

    /// <summary>
    /// Reads the spider's two triggers. Left is the emitter's ordinary fire, routed
    /// straight through the craft's cannon so it costs and cools identically. Right
    /// winds the lance: while it is held the craft is rooted, and the frame it is let go
    /// the meter is spent as a beam.
    ///
    /// The order matters. The release is handled before the laser so that letting go of
    /// the right button on the same frame the left one is down fires the lance rather
    /// than swallowing it — a player mashing both should get the expensive shot, not
    /// silently lose the charge they spent two seconds standing still for.
    /// </summary>
    private void UpdateSpiderTriggers(SpiderWeapon spider, float dt)
    {
        // A cinematic has hold of the craft: nothing is charging and nothing is rooted,
        // and any wound-up meter is dropped rather than fired into a claw.
        if (Seizure is { Held: true })
        {
            spider.Cancel();
            Audio.SetLanceCharge(false, 0f);
            Player.Rooted = false;
            return;
        }

        if (InputMap.Grenade)
        {
            spider.Hold(dt);
            Player.Rooted = true;
            // The whine climbs with the meter, so the player can hear how loaded the
            // shot is while they are busy watching the thing that is walking at them.
            Audio.SetLanceCharge(true, spider.ChargeFraction);
            return;
        }

        Audio.SetLanceCharge(false, 0f);
        Player.Rooted = false;

        if (spider.Charging)
        {
            FireSpiderLance(spider);
            return;
        }

        if (InputMap.Fire) FirePlayerShot(laser: true);
    }

    /// <summary>
    /// Looses the charged lance: spends rounds in proportion to the meter, raises the
    /// beam, and bites everything standing on the shaft. The damage is applied once,
    /// here — the beam that burns for the next half second is the picture of a shot that
    /// has already happened, not a lingering hazard, so a target can't be billed twice
    /// for one trigger pull.
    ///
    /// Refused outright when the magazine can't cover the bill, and the charge is
    /// dropped either way: an empty craft can't fire a lance any more than it can fire
    /// a cannon.
    /// </summary>
    private void FireSpiderLance(SpiderWeapon spider)
    {
        int cost = spider.AmmoCost;
        float damage = spider.Damage;

        Vector2 fwd = Player.Forward;
        Vector2 muzzleXZ = Player.Position + fwd * SpiderWeapon.MuzzleForward;
        var origin = new Vector3(muzzleXZ.X, SpiderWeapon.MuzzleHeight + Player.Height, muzzleXZ.Y);
        var dir = new Vector3(fwd.X, 0f, fwd.Y);

        if (Player.Ammo < cost)
        {
            spider.Cancel();   // the meter is spent whether or not a shot comes out
            return;
        }

        if (!spider.Release(origin, dir, out _)) return;

        Player.Ammo -= cost;
        Audio.PlayLanceFire();
        BurnSpiderLance(spider, damage);
    }

    /// <summary>
    /// Looses whatever is wound into the spider's meter, without waiting on a trigger
    /// edge — the headless self-test's way in, since it has no mouse. Silently does
    /// nothing on a chassis with no emitter.
    /// </summary>
    public void FireSpiderLanceForTest()
    {
        if (Player.Spider is { } spider) FireSpiderLance(spider);
    }

    /// <summary>
    /// Applies a fired lance to everything on its axis. Enemies are tested against the
    /// shaft directly (it pierces — one beam rakes a whole line of hunters); the two
    /// monsters' weak points are found by walking the ray and asking each of them the
    /// same question an ordinary bolt asks, so what the lance can hit is exactly what a
    /// round flying down the same line could hit, and no new geometry has to agree with
    /// the old geometry about where a core is.
    /// </summary>
    private void BurnSpiderLance(SpiderWeapon spider, float damage)
    {
        var originXZ = new Vector2(spider.BeamOrigin.X, spider.BeamOrigin.Z);

        // The emitter's charged beam is a lance, not a round: what it meets, it fells.
        // Which is the payoff for standing rooted for two seconds while something walks
        // at you — the SPIDER is the one chassis that can open a road through the city.
        CutStructuresAlong(spider.BeamOrigin, spider.BeamDirection,
            SpiderWeapon.BeamLength, SpiderWeapon.BeamRadius);

        // A hunter is treated as the standing block it is drawn as, not as a point:
        // the shaft has to pass within its planar radius *and* be somewhere between the
        // grid and the top of its hull where it does so. Which is what makes the two
        // ways of firing the lance genuinely different — a grounded beam rakes a whole
        // line of tanks, and a beam loosed at the top of a jump sails clean over them
        // on its way to something's core.
        var dirXZ = new Vector2(spider.BeamDirection.X, spider.BeamDirection.Z);
        float planar = dirXZ.Length();
        if (planar > 1e-4f) dirXZ /= planar;
        float slope = planar > 1e-4f ? spider.BeamDirection.Y / planar : 0f;

        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            // Nearest image across the torus, so a beam fired near the seam still burns
            // the hunter standing just over it.
            Vector2 near = Torus.NearestImage(e.Position, originXZ);
            float along = Math.Clamp(Vector2.Dot(near - originXZ, dirXZ), 0f, SpiderWeapon.BeamLength);
            if (Vector2.Distance(near, originXZ + dirXZ * along)
                > SpiderWeapon.BeamRadius + EnemyTank.Radius) continue;

            float beamY = spider.BeamOrigin.Y + slope * along;
            if (beamY < -SpiderWeapon.BeamRadius
                || beamY > EnemyTank.BodyHeight + SpiderWeapon.BeamRadius) continue;

            DamageEnemy(e, damage);
        }

        // Step down the shaft looking for the two crystals. A one-unit stride is well
        // finer than either weak point's own reach, so nothing can sit between samples.
        const float step = 1f;
        bool hitCore = false, hitCrystal = false;
        for (float d = 0f; d <= SpiderWeapon.BeamLength && !(hitCore && hitCrystal); d += step)
        {
            Vector3 at = spider.BeamOrigin + spider.BeamDirection * d;
            var xz = new Vector2(at.X, at.Z);

            if (!hitCore && Boss is { } boss && boss.HitsCore(xz, at.Y))
            {
                hitCore = true;
                if (boss.DamageCore(damage)) DestroyBoss(boss);
                else Audio.PlayCoreHit(1f - boss.CoreFraction);
            }

            if (!hitCrystal && Maw is { } maw && maw.HitsCrystal(xz, at.Y))
            {
                hitCrystal = true;
                if (maw.DamageCrystal(damage)) DestroyMaw(maw);
                else Audio.PlayMawHurt(1f - maw.CrystalFraction);
            }
        }
    }

    /// <summary>Requests a heavy grenade; honoured only if off cooldown with 10 ammo.</summary>
    public void FirePlayerGrenade()
    {
        if (Player.TryFireGrenade(out Vector2 origin, out Vector2 dir))
            SpawnProjectile(origin, dir, fromPlayer: true, grenade: true);
    }

    /// <summary>
    /// Throws whatever the given equip slot (R/T/Y/U → 0..3) holds. Only the crafted
    /// CRAB CORE is throwable: it lobs out in front like a grenade and detonates into a
    /// ring of lances. Spends the item — the slot empties. A no-op for an empty slot or
    /// a non-throwable item, and never while a set piece owns the craft.
    /// </summary>
    public void UseWeaponSlot(int slot)
    {
        if (Player.Captured) return;
        if (slot < 0 || slot >= Inventory.Weapons.Length) return;
        ref ItemStack w = ref Inventory.Weapons[slot];
        if (w.IsEmpty || w.Kind != ItemKind.CrabCore) return;

        // Consume one core and lob it from the muzzle along the craft's heading.
        w.Count--;
        if (w.Count <= 0) w = ItemStack.Empty;

        Vector2 dir = Player.Forward;
        Vector2 origin = Player.Position + dir * (PlayerTank.Radius + 0.6f);
        SpawnProjectile(origin, dir, fromPlayer: true, crabBomb: true);
    }

    private void SpawnProjectile(Vector2 origin, Vector2 dir, bool fromPlayer,
        bool grenade = false, bool crabBomb = false,
        float launchHeight = Projectile.BoltHeight, bool laser = false)
    {
        foreach (var p in _projectiles)
        {
            if (p.Active) continue;
            if (crabBomb) p.FireCrabBomb(origin, dir);
            else if (grenade) p.FireGrenade(origin, dir, fromPlayer);
            else p.Fire(origin, dir, fromPlayer, launchHeight, laser);
            // The report of a barrel firing — same clip for player and enemy
            // shots, since both spawn through here. The thrown core gets a heavier
            // launch thud instead of the light bolt report, and the SPIDER's laser its
            // own dry zap: the cannon clip has enough body that a stream of them at the
            // emitter's cadence stacks into a continuous roar.
            if (crabBomb) Audio.PlayThrowWhoosh();
            else if (laser) Audio.PlayLaser();
            else Audio.PlayDetonation();
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

            // A thrown CRAB CORE that reached the end of its short lob without striking
            // anything goes off where it landed — the ring of lances erupts there.
            if (p.JustExpired && p.IsCrabBomb)
                StageCrabBlast(p.Position);

            if (!p.Active) continue;

            // The skyline stops rounds — every round, from anyone. Tested before any
            // target, so a hunter standing behind a wall is genuinely behind it and a
            // shot cannot reach a boss's core through a tower. This is what makes the
            // buildings cover rather than decoration, and it cuts both ways: the thing
            // the player is hiding behind is also the thing they cannot shoot past.
            if (BlockShotOnStructure(p)) continue;

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

                // The Maw-Core's crystal, hanging at the top of a jump. Same bargain
                // as the crab's core and a stricter one: this band sits so far above
                // barrel height that a grounded shot cannot reach it at all, so
                // landing this is proof the player was in the air when they fired.
                if (!p.IsGrenade && Maw is { } liveMaw && liveMaw.HitsCrystal(p.Position, p.Height))
                {
                    if (liveMaw.DamageCrystal(PlayerShotDamage))
                        DestroyMaw(liveMaw);
                    else
                        Audio.PlayMawHurt(1f - liveMaw.CrystalFraction);
                    p.Active = false;
                    continue;
                }

                foreach (var e in Enemies)
                {
                    if (!e.Alive) continue;
                    if (WithinHit(p.Position, e.Position, EnemyTank.Radius))
                    {
                        if (p.IsCrabBomb)
                            StageCrabBlast(p.Position); // erupts into the lance ring here
                        else if (p.IsGrenade)
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
        Audio.PlayExplosionAt(Torus.Distance(p.Position, Player.Position));
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

        // The kill leaves a shard of the core behind — a CRAB CORE fragment to collect.
        // Three of them craft a thrown CRAB CORE of the player's own.
        Pickups.Add(new Pickup(boss.Position, PickupKind.CrabFragment));
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
        => Torus.DistanceSquared(a, b) <= radius * radius;

    // --- The thrown CRAB CORE's radial blast -----------------------------------

    /// <summary>Crab-beam-tier damage each lance of the blast deals per tick — enough
    /// to erase a hunter over the burn and badly hurt an elite.</summary>
    private const float CrabBlastDamage = BeamDamage;

    /// <summary>How often each lance bites while the star burns — the same cadence the
    /// boss's own beam uses, so a caught enemy is chewed a few times over the short life
    /// rather than instantly or once.</summary>
    private const float CrabBlastTickInterval = BeamTickInterval;

    private float _crabBlastTick;

    /// <summary>
    /// Stages a CRAB CORE detonation at <paramref name="at"/>: raises the lance ring,
    /// sounds its creepier, layered echo of the boss's beam, and throws a hot neon
    /// burst of debris at the centre.
    /// </summary>
    private void StageCrabBlast(Vector2 at)
    {
        Blasts.Add(new CrabCoreBlast(at));
        Audio.PlayCrabCoreBlast();
        Debris.Burst(new Vector3(at.X, CrabCoreBlast.CoreHeight, at.Y), Palette.NeonRed, elite: true);
        Debris.Burst(new Vector3(at.X, 0.4f, at.Y), Palette.CrabChassis, elite: false);
    }

    /// <summary>
    /// Ages the live blasts and bites anything inside their energy field. Now that the
    /// burst fires in every direction and churns, the damage is a swelling-then-shrinking
    /// sphere around the core rather than a per-lance ray test: any enemy within the
    /// blast's current planar reach (which grows with the swell and retracts as it dies)
    /// takes a bite. Ticked so the damage doesn't scale with the frame rate and a caught
    /// enemy is worn down across the three seconds.
    /// </summary>
    private void UpdateCrabBlasts(float dt)
    {
        if (Blasts.Count == 0) return;

        _crabBlastTick -= dt;
        bool bite = _crabBlastTick <= 0f;
        if (bite) _crabBlastTick = CrabBlastTickInterval;

        foreach (var blast in Blasts)
        {
            blast.Update(dt);
            if (!bite) continue;

            float field = blast.CurrentDamageRadius;
            if (field <= 0f) continue;

            // The ring of lances is beam-grade, so anything of the city caught inside the
            // swell comes down with everything else. Structures are struck once and then
            // stop being blockers, so a core thrown into a plaza takes the whole block on
            // the tick it reaches them rather than gnawing at them for three seconds.
            foreach (var s in Structures)
            {
                if (s.Falling) continue;
                if (Torus.DistanceSquared(s.Position, blast.Position)
                    <= (field + BlastBossMargin) * (field + BlastBossMargin))
                    FellStructure(s);
            }

            float reach = field + EnemyTank.Radius;
            float reachSq = reach * reach;
            foreach (var e in Enemies)
            {
                if (!e.Alive) continue;
                // Measured across the torus so a burst near the seam still catches bodies
                // just over it.
                if (Torus.DistanceSquared(e.Position, blast.Position) <= reachSq)
                    DamageEnemy(e, CrabBlastDamage);
            }

            // The big monsters are fair game too — a thrown core is powerful enough to
            // bite the Crab-Core's own gem and the Maw-Core's crystal. Both are only
            // damageable through those weak points normally, but a detonation this close
            // simply strikes them directly; a killing bite stages the usual death (which,
            // for the crab, also drops the fragment its own kill would).
            float bossReach = field + BlastBossMargin;
            float bossReachSq = bossReach * bossReach;

            if (Boss is { Alive: true } boss
                && Torus.DistanceSquared(boss.Position, blast.Position) <= bossReachSq)
            {
                if (boss.DamageCore(CrabBlastDamage)) DestroyBoss(boss);
                else Audio.PlayCoreHit(1f - boss.CoreFraction);
            }

            if (Maw is { Alive: true } maw
                && Torus.DistanceSquared(maw.Position, blast.Position) <= bossReachSq)
            {
                if (maw.DamageCrystal(CrabBlastDamage)) DestroyMaw(maw);
                else Audio.PlayMawHurt(1f - maw.CrystalFraction);
            }
        }

        Blasts.RemoveAll(b => !b.Active);
    }

    /// <summary>Extra planar slack added to a blast's field when checking the two big
    /// monsters, whose bodies are far larger than a tank's — so a burst that clearly
    /// engulfs one connects even when its centre is a few units off the monster's.</summary>
    private const float BlastBossMargin = 6f;
}
