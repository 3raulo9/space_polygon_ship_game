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

    /// <summary>
    /// How much harder a hit lands on a naked VIRUS mote than on a body. This is the price
    /// of the whole class stated as one number: unhosted, the payload is fragile enough that
    /// a single hunter's round is most of a life, which is what makes reaching the next body
    /// a matter of survival rather than convenience. Ignored while a host is worn — there the
    /// husk soaks the blow instead (see <see cref="Entities.VirusRig.AbsorbDamage"/>).
    /// </summary>
    public const float MoteVulnerability = 2.2f;

    /// <summary>Half-height of the band an enemy shot has to arrive in to bite the player,
    /// measured off the craft's body centre (<see cref="EnemyTank.AimHeight"/>). Wide
    /// enough to cover the hull and forgive a per-tick step of the bolt, tight enough that
    /// climbing or dropping a couple of metres through the shot's flight slips it.</summary>
    private const float EnemyHitVertical = 1.6f;

    // Shield fraction at which the low-health alarm sounds. Crossing *down*
    // through this line fires warning.wav once — not once per frame below it.
    private const float LowShieldWarning = 0.45f;

    // --- The TANK's siege kit (world side) --------------------------------------
    // The craft-side state lives on PlayerTank; these are the numbers only the world can own,
    // because they are about the tank meeting the rest of the field — what a ram costs a hunter,
    // how far it shoves them, what an AP slug does to a line, and the screening smoke it lays.

    /// <summary>Base ram damage, scaled up by how hard the hull was moving. A cruising bump
    /// chips a hunter; a lurch-speed slam erases one. See <see cref="UpdateRam"/>.</summary>
    private const float RamDamage = 2.2f;

    /// <summary>How far a rammed hunter is thrown clear along the contact line. Enough to break
    /// contact in one tick, which is what keeps a single slam from billing every frame.</summary>
    private const float RamShove = 4.5f;

    /// <summary>What the AP slug deals per body it punches through — one-shots a standard hunter
    /// and badly hurts an elite, the price of five rounds and a long reload for a whole line.</summary>
    private const float SlugDamage = 3f;

    /// <summary>
    /// What a mortar burst deals to a boss's weak point when it lands on or arcs over one.
    /// Deliberately well short of the rocket's one-shot: the mortar is ammo-cheap and comes
    /// back every few seconds, so a boss should cost two or three good lobs (the crab's core is
    /// 4, the maw's crystal 5), not a single lucky one. This is the tank's indirect answer to a
    /// monster it can no longer leap up to hit — the same bargain the thrown CRAB CORE strikes,
    /// where a heavy detonation close enough simply reaches the core through its inert armour.
    /// </summary>
    private const float MortarBossDamage = 2f;

    /// <summary>The lone screening smoke a TANK lays with its dischargers. A short list, capped,
    /// aged every tick and swept when spent. Public so the renderer can draw the murk.</summary>
    public readonly List<SmokeCloud> Smoke = new();
    private const int MaxSmoke = 8;
    private const float SmokeLife = 6f;
    private const float SmokeRadius = 7.5f;

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

    // Hunters: keep at most this many on the field. Rolls come at a brisk cadence, and
    // when the field is already full the farthest hunter (drifted off into the fog) is
    // let go so a fresh one can always resolve on the horizon — spawning never stalls.
    // The field runs denser than it used to across the board: partly for the fight, and
    // partly because one chassis now eats bodies to live — a VIRUS in a starved arena is
    // a corpse with a timer.
    private const int MaxEnemies = 6;
    private const float EnemySpawnInterval = 6f;
    private const float EnemySpawnChance = 0.7f;
    private const float EliteChance = 0.22f;
    private float _enemyTimer;

    // Floating salvage: a slow, endless drip. At the cap the farthest piece drifts out
    // of play and a new one fades in, so batteries and rounds never stop appearing.
    private const int MaxPickups = 7;
    private const float PickupSpawnInterval = 7f;
    private const float PickupSpawnChance = 0.6f;
    private const float BatteryShare = 0.6f;   // this fraction of new salvage is batteries
    private float _pickupTimer;

    // The Crab-Core is no longer a rare boss — it is a regular inhabitant of the field:
    // while none stalks it, roll often for one to rise out of the fog at a random
    // bearing. Still only ever one at a time, because everything that reads a crab (the
    // beam, the seizure, the renderer) reads *the* crab — the demotion is in how often
    // it shows up, not in what it is.
    private const float BossSpawnInterval = 10f;
    private const float BossSpawnChance = 0.4f;
    private float _bossTimer;

    // The Maw-Core rolls on its own clock, independent of the crab's — the two are
    // different threats occupying different space (one on the grid, one in the air
    // over it) and meeting both at once is now simply an ordinary evening. Like the
    // crab it has stopped being an event and started being a neighbour.
    private const float MawSpawnInterval = 11f;
    private const float MawSpawnChance = 0.35f;
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

        // A soldier does not start in the clearing. Every other chassis opens at the
        // origin, which the skyline is deliberately kept out of (see
        // StructureField.ClearRadius) so nothing stands in the player's nose on the first
        // frame — but this one has nothing to hang from out there, and a class whose
        // whole loop is anchors would open on a minute of walking. So it starts within
        // one cable's throw of the nearest tower, looking straight at it.
        if (Player.Soldier != null) StandTheSoldierInTheCity();
        // And a fish does not start on the seabed. Opening beached would put the player's
        // first ten seconds in the one state the entire chassis is designed around
        // escaping — so it opens already swimming, up level with the middle of the towers,
        // with speed on the clock and the city laid out beneath it.
        if (Player.Fish != null) SwimTheFishOffTheDeck();

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
        // The SOLDIER walks on its own keys rather than through the craft's throttle, so
        // its WASD is read here and handed to the rig every frame — including while the
        // crafting panel is up, exactly as a tank keeps coasting under the overlay. The
        // *look* is not: the panel has the mouse, and a player dragging a stack across a
        // slot must not also be spinning on the spot behind it.
        if (Player.Soldier is { } rig)
        {
            rig.MoveInput = ScriptedSoldierMove ?? InputMap.SoldierMove;
            if (acceptCombatInput && !Player.Captured) UpdateMouseLook();
        }

        // The FISH is the same arrangement with a different pair of keys: A/D roll the
        // body and S folds the fins. The beat is not read here because it is not a state
        // — it is an event, and it belongs with the triggers below.
        if (Player.Fish is { } body)
        {
            body.MoveInput = ScriptedFishMove
                ?? new Vector2(InputMap.RollInput, InputMap.BrakeDown ? 1f : 0f);
            if (acceptCombatInput && !Player.Captured) UpdateMouseLook();
        }

        // The VIRUS is the same arrangement again: WASD is its movement — the mote's flight
        // and the worn host's drive both read it — and the mouse is the look, the whole way
        // up and down since the mote flies where it points.
        if (Player.Virus is { } payload)
        {
            payload.MoveInput = ScriptedVirusMove ?? InputMap.VirusMove;
            if (acceptCombatInput && !Player.Captured) UpdateMouseLook();
        }

        // The machines — the TANK and the SPIDER — read the same mouse to turn the whole
        // craft, exactly as the two bodies above do. Their WASD is not touched here: it is
        // read straight from the keys in the drive step (W/S throttle, A/D strafe), so only
        // the look is the mouse's.
        if (Player.IsMachine && acceptCombatInput && !Player.Captured) UpdateMouseLook();

        if (acceptCombatInput)
        {
            if (Digestion is { Held: true })
            {
                if (InputMap.Fire || InputMap.Grenade) FirePlayerShot();
            }
            else if (Player.Spider is { } spider)
                UpdateSpiderTriggers(spider, dt);
            else if (Player.Soldier is { } soldier)
                UpdateSoldierTriggers(soldier, dt);
            else if (Player.Fish is { } swimmer)
                UpdateFishTriggers(swimmer);
            else if (Player.Virus is { } virusRig)
                UpdateVirusTriggers(virusRig);
            else
                UpdateTankTriggers();   // the TANK's whole expanded kit — and the plain machine default

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
            // A tank reading the crafting panel is not dug in — drop the stance rather than
            // leaving it locked behind an overlay the player is dragging items around in.
            Player.Unplant();
        }

        // A soldier reading the crafting panel is not reeling, whatever the keys say, and
        // a fish reading it is not tearing through the water. The beds fade themselves out
        // the moment nothing feeds them.
        if (!acceptCombatInput && (Player.Soldier != null || Player.Fish != null || Player.Virus != null))
        {
            Audio.SetReel(false, 0f);
            Audio.SetWind(false, 0f);
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
        // After the collision pass, so a swing that ended in a wall is one of the events
        // being drained rather than something that happens a tick later.
        if (Player.Soldier is { } rig) UpdateSoldierEvents(rig, dt);
        if (Player.Fish is { } body) UpdateFishEvents(body);
        if (Player.Virus is { } payload) UpdateVirusEvents(payload);

        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            // Hand the hunter the player's height so it can elevate onto a craft off the
            // grid; the pitch it hands back rides the enemy round up the same way the
            // tank's cannon climbs on its own aim. A firing solution that has to cross a
            // TANK's screening smoke is simply lost — the hunter still spends its cooldown,
            // so a laid screen reads as the field going half-blind rather than pausing.
            if (e.Update(dt, Player.Position, Player.Height,
                out Vector2 eOrigin, out Vector2 eDir, out float ePitch)
                && !SmokeBlocks(e.Position, Player.Position))
                SpawnProjectile(eOrigin, eDir, fromPlayer: false, pitch: ePitch);
        }

        // The tank's ram: with the enemies stepped to their spots this tick, a hull that is
        // genuinely driving into one crushes it. Runs before the projectile pass so a shoved
        // hunter is already clear when its own shot is resolved.
        UpdateRam();

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
        UpdateSmoke(dt);
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
        // The AP slug punches through the skyline as readily as through a line of hunters —
        // going through cover is the whole point of it, so it is never blocked here.
        if (p.IsPiercing) return false;

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
                else if (p.IsRocket)
                {
                    // Neither is a rocket. Its contact fuse is exactly what a wall is for.
                    DetonateRocket(p);
                }
                else if (p.IsGrenade)
                {
                    // A mortar that clipped a tower it couldn't clear bursts against it — the
                    // same splash it would have thrown on the grid, just up the wall instead.
                    DetonateMortar(p);
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
            Structure s = Structures[i];

            // Over the top of it is past it. This never mattered while the only thing on
            // the field was a craft whose whole jump peaks at eight units, but a soldier
            // spends most of the run above the height of an arch's legs, and being
            // shoved sideways by a wall thirty metres beneath them would be absurd.
            if (Player.Height > s.BlockHeight) continue;

            int n = s.Blockers(blockers);
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

                // For a soldier this is not a scrape but a possible crash: meeting a wall
                // at speed is the way this chassis dies, and the rig decides which of the
                // two just happened from the momentum it was carrying. A fish threading a
                // reef is the same bargain at a lower threshold — it has no armour and the
                // whole run is spent at speed between buildings.
                Player.Soldier?.RegisterWallHit();
                Player.Fish?.RegisterWallHit();
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

        // The vertical half of the head's aim. Flat on a hull that can't crane its gun —
        // GunElevation is zero there — and up to the head's own stop on the two chassis
        // that can, so the bolt climbs or dives to where the crosshair is resting.
        SpawnProjectile(origin, dir, fromPlayer: true, launchHeight: launchHeight,
            laser: laser, pitch: Player.GunElevation);
    }

    // --- The TANK chassis: the siege kit ----------------------------------------

    /// <summary>
    /// Reads the TANK's expanded kit — and, since the SPIDER, FISH, SOLDIER and VIRUS are all
    /// dispatched earlier, this is the only chassis that reaches here, so it also carries the
    /// plain machine default of cannon-and-heavy-round. The order is deliberate: the plant
    /// toggles first (so a plant and a shot on the same frame both land), then the two Hyper /
    /// cooldown moves, then the AP slug takes priority over the cannon so tapping its key never
    /// loses the shot under a held fire button.
    /// </summary>
    private void UpdateTankTriggers()
    {
        // The siege plant, on the freed jump key. Locks the tracks, cranes the gun the full way
        // up and turns the front plate to the field — the tank's answer to a game it can no
        // longer leave the ground to solve.
        if (InputMap.TankPlantPressed)
        {
            Player.TogglePlant();
            Audio.PlayClamp();   // the clank of the tracks locking, or letting go
        }

        // The lurch: a Hyper-fed track-boost dodge in the drive direction. Refused while dug in.
        if (InputMap.TankLurchPressed && Player.TryLurch())
            Audio.PlayGasJump(0f);   // borrow the soldier's kick — a hard gout of thrust

        // The dischargers: a screen of smoke to blind the field and break contact.
        if (InputMap.TankSmokePressed && Player.TryDeploySmoke())
            DeploySmoke();

        // The AP slug: a heavy piercing shot. Ahead of the cannon so holding fire and tapping
        // its key fires the slug rather than swallowing it under the ordinary round.
        if (InputMap.TankSlugPressed)
        {
            FirePlayerSlug();
            return;
        }

        // The ordinary triggers: the mortar on the heavy button, the cannon on fire.
        if (InputMap.Grenade) FirePlayerGrenade();
        else if (InputMap.Fire) FirePlayerShot();
    }

    /// <summary>
    /// Fires the AP slug: a heavy piercing round down the gun line that punches through a whole
    /// line of hunters and through cover both (see the projectile pass and BlockShotOnStructure).
    /// Costs a fistful of the magazine at once. Dead while seized, like the cannon.
    /// </summary>
    public void FirePlayerSlug()
    {
        if (Seizure is { Held: true }) return;
        if (!Player.TryFireSlug(out Vector2 origin, out Vector2 dir, out float launchHeight)) return;
        SpawnProjectile(origin, dir, fromPlayer: true, launchHeight: launchHeight,
            pitch: Player.GunElevation, piercing: true);
    }

    /// <summary>Lays a smoke screen off the dischargers: one bank on the hull and one just
    /// behind it, so the murk is a wall to hide the whole craft rather than a puff beside it.</summary>
    private void DeploySmoke()
    {
        LaySmoke(Player.Position);
        LaySmoke(Player.Position - Player.Forward * 6f);
        Audio.PlayThrowWhoosh();
        Debris.Burst(new Vector3(Player.Position.X, 1.2f, Player.Position.Y),
            Palette.StructureShell, elite: false);
    }

    private void LaySmoke(Vector2 at)
    {
        if (Smoke.Count >= MaxSmoke) Smoke.RemoveAt(0);
        Smoke.Add(new SmokeCloud(Torus.Wrap(at), SmokeLife, SmokeRadius));
    }

    /// <summary>Test hook: lay a screen exactly as the discharger trigger would, if it has
    /// cooled. Returns whether it fired — the headless self-test can't press E.</summary>
    public bool DeploySmokeForTest()
    {
        if (!Player.TryDeploySmoke()) return false;
        DeploySmoke();
        return true;
    }

    private void UpdateSmoke(float dt)
    {
        if (Smoke.Count == 0) return;
        for (int i = Smoke.Count - 1; i >= 0; i--)
        {
            Smoke[i].Update(dt);
            if (!Smoke[i].Active) Smoke.RemoveAt(i);
        }
    }

    /// <summary>
    /// The tank's ram: a hull genuinely driving into a hunter crushes it. Gated on the TANK,
    /// on the grid, and on the drive velocity actually clearing <see cref="PlayerTank.RamThreshold"/>
    /// — a cruise bump chips, a lurch-speed slam erases. The struck hunter is thrown clear along
    /// the contact line, and that separation (not a per-enemy timer) is what makes one slam one
    /// hit. Reads the velocity <see cref="PlayerTank"/> stashed on its own move this tick.
    /// </summary>
    private void UpdateRam()
    {
        if (Player.Class != PlayerClass.Tank || Player.IsAirborne || Player.Captured) return;

        float speed = Player.DriveVelocity.Length();
        if (speed < Player.RamThreshold) return;

        float reach = PlayerTank.Radius + EnemyTank.Radius;
        float over = Math.Clamp((speed - Player.RamThreshold) / (PlayerTank.MaxSpeed * 0.9f), 0f, 1.5f);

        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            if (!WithinHit(Player.Position, e.Position, reach)) continue;

            DamageEnemy(e, RamDamage * (0.6f + over));

            Vector2 push = Torus.Delta(Player.Position, e.Position);
            push = push.LengthSquared() > 1e-4f ? Vector2.Normalize(push) : Player.Forward;
            e.Position = Torus.Wrap(e.Position + push * RamShove);

            Player.Jolt(0.35f);
            Audio.PlayDetonation();   // the crunch of the hull meeting a hull
        }
    }

    /// <summary>
    /// True if a live, opaque smoke screen sits across the segment from a shooter
    /// (<paramref name="from"/>) to the player (<paramref name="to"/>). Both the shooter and
    /// each cloud are folded into the player's own image across the wrap, so a hunter and a
    /// screen just over the seam are measured against the same line the round would fly.
    /// </summary>
    public bool SmokeBlocks(Vector2 from, Vector2 to)
    {
        if (Smoke.Count == 0) return false;
        Vector2 a = Torus.NearestImage(from, to);
        foreach (var c in Smoke)
        {
            if (!c.Opaque) continue;
            Vector2 centre = Torus.NearestImage(c.Position, to);
            if (PointSegmentDistanceSq(centre, a, to) <= c.Radius * c.Radius) return true;
        }
        return false;
    }

    /// <summary>True if a point sits inside any live, opaque screen — what a round already in
    /// the air is tested against, so a shot dies in the murk it flies into.</summary>
    public bool SmokeAbsorbs(Vector2 at)
    {
        foreach (var c in Smoke)
        {
            if (!c.Opaque) continue;
            if (Torus.DistanceSquared(at, c.Position) <= c.Radius * c.Radius) return true;
        }
        return false;
    }

    /// <summary>Squared distance from point <paramref name="p"/> to segment a→b, all in one
    /// planar frame — the caller has already resolved the torus wrap into common images.</summary>
    private static float PointSegmentDistanceSq(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.LengthSquared();
        if (len2 < 1e-6f) return (p - a).LengthSquared();
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        Vector2 proj = a + ab * t;
        return (p - proj).LengthSquared();
    }

    /// <summary>
    /// The mortar's burst where its lob came down: a splash on the whole cluster there, on either
    /// crystal if it landed on one, and on any footprint it dropped against — the same reach and
    /// damage a grenade always dealt, now delivered from above. Mirrors the rocket's detonation
    /// but throws the shake onto the hull directly, since a tank has no soldier rig to feel it.
    /// </summary>
    private void DetonateMortar(Projectile p)
    {
        var at = new Vector3(p.Position.X, MathF.Max(0.4f, p.Height), p.Position.Y);
        float reach = p.SplashRadius + EnemyTank.Radius;

        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            if (WithinHit(p.Position, e.Position, reach)) DamageEnemy(e, GrenadeDamage);
        }

        // The two monsters are bitten by planar proximity, not at the burst's own height: the
        // shell comes down on the grid far below the core or crystal, so a height-gated check
        // (the way a flat bolt is scored) would never land. A heavy detonation within reach of
        // the column simply strikes the weak point — the exact bargain the thrown CRAB CORE
        // strikes — for the smaller MortarBossDamage, so it takes a few good lobs, not one.
        if (Boss is { Alive: true } boss
            && WithinHit(p.Position, boss.Position, p.SplashRadius + CrabCore.CoreHitRadius))
        {
            if (boss.DamageCore(MortarBossDamage)) DestroyBoss(boss);
            else Audio.PlayCoreHit(1f - boss.CoreFraction);
        }
        if (Maw is { Alive: true } maw
            && WithinHit(p.Position, maw.Position, p.SplashRadius + MawRig.HitRadius))
        {
            if (maw.DamageCrystal(MortarBossDamage)) DestroyMaw(maw);
            else Audio.PlayMawHurt(1f - maw.CrystalFraction);
        }

        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        for (int i = Structures.Count - 1; i >= 0; i--)
        {
            Structure s = Structures[i];
            int n = s.Blockers(blockers);
            for (int b = 0; b < n; b++)
            {
                var (bAt, r) = blockers[b];
                if (!WithinHit(p.Position, bAt, p.SplashRadius + r)) continue;
                if (p.Height > s.BlockHeight + p.SplashRadius) continue;
                FellStructure(s);
                break;
            }
        }

        Debris.Burst(at, Palette.EliteFill, elite: true);
        Audio.PlayRocketBlast(Torus.Distance(p.Position, Player.Position));

        // Dropped too close and caught in your own burst: it bites, and the hull feels it.
        float range = Torus.Distance(p.Position, Player.Position);
        if (range < p.SplashRadius + PlayerTank.Radius)
        {
            DamagePlayer(GrenadeDamage);
            Player.Jolt(0.4f);
        }
        else if (range < RocketShakeRange)
        {
            Player.Jolt(0.25f * (1f - range / RocketShakeRange));
        }
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

        // The emitter sits out past the carapace along the craft's heading, and the shaft
        // leaves down the full look line — the SPIDER's ring aims anywhere, up the face of
        // a tower or down at the grid, unlike the tank's stopped-short gun.
        Vector2 look = Player.Forward;
        Vector2 muzzleXZ = Player.Position + look * SpiderWeapon.MuzzleForward;
        var origin = new Vector3(muzzleXZ.X, SpiderWeapon.MuzzleHeight + Player.Height, muzzleXZ.Y);
        Vector3 dir = Player.Forward3;

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
        => BurnBeamAlong(spider.BeamOrigin, spider.BeamDirection,
            SpiderWeapon.BeamLength, SpiderWeapon.BeamRadius, damage);

    /// <summary>
    /// Applies one fired beam of any owner's making to the world — the SPIDER's charged
    /// lance and each shaft of the worn crab's broken one both come through here, so what a
    /// beam can do is decided in exactly one place.
    /// </summary>
    private void BurnBeamAlong(Vector3 origin, Vector3 direction, float length, float radius,
        float damage)
    {
        var originXZ = new Vector2(origin.X, origin.Z);

        // A charged beam is a lance, not a round: what it meets, it fells. Which is the
        // payoff for whatever the shot cost — two seconds rooted in the open, or a slice
        // of the body the emitter is running on.
        CutStructuresAlong(origin, direction, length, radius);

        // A hunter is treated as the standing block it is drawn as, not as a point:
        // the shaft has to pass within its planar radius *and* be somewhere between the
        // grid and the top of its hull where it does so. Which is what makes the two
        // ways of firing a lance genuinely different — a grounded beam rakes a whole
        // line of tanks, and a beam loosed from height sails clean over them on its way
        // to something's core.
        var dirXZ = new Vector2(direction.X, direction.Z);
        float planar = dirXZ.Length();
        if (planar > 1e-4f) dirXZ /= planar;
        float slope = planar > 1e-4f ? direction.Y / planar : 0f;

        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            // Nearest image across the torus, so a beam fired near the seam still burns
            // the hunter standing just over it.
            Vector2 near = Torus.NearestImage(e.Position, originXZ);
            float along = Math.Clamp(Vector2.Dot(near - originXZ, dirXZ), 0f, length);
            if (Vector2.Distance(near, originXZ + dirXZ * along)
                > radius + EnemyTank.Radius) continue;

            float beamY = origin.Y + slope * along;
            if (beamY < -radius
                || beamY > EnemyTank.BodyHeight + radius) continue;

            DamageEnemy(e, damage);
        }

        // Step down the shaft looking for the two crystals. A one-unit stride is well
        // finer than either weak point's own reach, so nothing can sit between samples.
        const float step = 1f;
        bool hitCore = false, hitCrystal = false;
        for (float d = 0f; d <= length && !(hitCore && hitCrystal); d += step)
        {
            Vector3 at = origin + direction * d;
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

    // --- The SOLDIER chassis ----------------------------------------------------

    /// <summary>
    /// Walks the soldier out of the empty clearing and stands them in front of the
    /// nearest tower, facing it, one comfortable cable's throw away with the crosshair
    /// already resting on its flank. The very first thing that chassis can do is
    /// therefore press E, and the very first thing that happens is a hook biting stone.
    ///
    /// Picked as the tower nearest the origin rather than rolled, so the opening is the
    /// same place every run — the skyline is a fixed feature of this world, and where a
    /// class starts in it should be too.
    /// </summary>
    private void StandTheSoldierInTheCity()
    {
        Structure? nearest = null;
        float best = float.MaxValue;
        foreach (var s in Structures)
        {
            if (s.Kind != StructureKind.Tower) continue;
            float d = s.Position.LengthSquared();
            if (d < best) { best = d; nearest = s; }
        }
        if (nearest == null) return;   // a razed city (VOIDTANKS_STRUCTURES=0) — stay put

        // Stand off it on the origin's side, so the walk back out to open grid is behind
        // the player rather than through the building they are looking at.
        Vector2 away = nearest.Position.LengthSquared() > 1e-4f
            ? Vector2.Normalize(-nearest.Position)
            : new Vector2(0f, -1f);

        Player.Position = Torus.Wrap(nearest.Position + away * SoldierStandoff);
        Player.Heading = MathF.Atan2(-away.X, -away.Y);
        // Aimed a touch above level: the tower is forty metres of wall and the useful
        // anchors are up it, not at its feet.
        Player.Pitch = 0.22f;
    }

    /// <summary>How far off the tower a soldier opens — comfortably inside a cable's
    /// reach, comfortably outside the footprint.</summary>
    private const float SoldierStandoff = 34f;

    /// <summary>
    /// How far the mouse turns the look, in radians per pixel. Tuned against the
    /// internal 320×240 rather than against the window: the whole picture is three
    /// hundred pixels wide, so a sweep that feels ordinary in a modern shooter throws
    /// the view most of the way round the world here.
    /// </summary>
    private const float LookSensitivity = 0.0032f;

    /// <summary>Movement past this in one frame is a pointer warp, not a hand, and the
    /// whole frame's look is discarded. In pixels.</summary>
    private const float MaxLookJump = 180f;

    /// <summary>
    /// Where the crosshair's ray last landed, if it landed on anything — the point a
    /// hook fired this frame would bite. Read by the HUD, which brackets the crosshair
    /// on a valid anchor and greys it out otherwise, and by nothing in the sim: firing
    /// re-casts rather than trusting a cached answer, so a hook can never bite something
    /// that stopped existing between the frame and the trigger.
    /// </summary>
    public Vector3? AnchorInSight { get; private set; }

    /// <summary>True when the anchor in sight is weak enough to tear out under load —
    /// the HUD warns rather than letting the player find out mid-arc.</summary>
    public bool AnchorIsWeak { get; private set; }

    /// <summary>
    /// Mouse look, for every chassis. The yaw turns the whole craft — it runs into the
    /// shared <see cref="PlayerTank.Heading"/> the same way on a body and a machine alike
    /// — and the pitch cranes the eye as far as that chassis allows (see
    /// <see cref="PlayerTank.Look"/>). Everything downstream reads the same
    /// <see cref="PlayerTank.Forward"/> and <see cref="PlayerTank.Forward3"/>.
    ///
    /// The sign is the one thing here worth stating: the camera renders +X on the
    /// <em>left</em> of the screen, so a heading increase swings the view left, and
    /// pushing the mouse right therefore has to <em>decrease</em> it — hence both deltas
    /// go in negated.
    /// </summary>
    private void UpdateMouseLook()
    {
        Vector2 delta = InputMap.LookDelta;
        if (delta == Vector2.Zero) return;

        // A capture being taken or handed back — opening the pack, tabbing away, the
        // frame the cursor is locked to the window — reports one enormous jump as the
        // pointer is warped to the centre. That is not input and must not be treated as
        // any amount of input: clamping it still throws the view a quarter turn, so a
        // frame that reports a movement no hand could make is dropped outright. The
        // threshold sits well above a genuine fast flick.
        if (MathF.Abs(delta.X) > MaxLookJump || MathF.Abs(delta.Y) > MaxLookJump) return;

        Player.Look(-delta.X * LookSensitivity, -delta.Y * LookSensitivity);
    }

    /// <summary>
    /// The SOLDIER's buttons: the two hooks, the gas jump, the rifle and the rockets.
    /// Also re-casts the crosshair every frame so the HUD can bracket a valid anchor —
    /// reading the city at a glance, mid-flight, is the skill this class is built on,
    /// and it needs the answer before the player commits, not after.
    /// </summary>
    private void UpdateSoldierTriggers(SoldierRig rig, float dt)
    {
        // A cinematic has hold of the player: both cables go, and nothing else is read.
        // Being swung around by a monster while still anchored to a tower is not a
        // situation the constraint solver has any sensible answer for.
        if (Player.Captured)
        {
            rig.ReleaseBoth();
            AnchorInSight = null;
            return;
        }

        Vector3 eye = Player.Eye;
        Vector3 look = Player.Forward3;

        AnchorInSight = TryFindAnchor(eye, look, out Vector3 at, out Structure? holding)
            ? at : null;
        AnchorIsWeak = AnchorInSight != null && holding is { } h && h.Scale < SoldierRig.WeakScale;

        // E and Q are the same button twice: throw if stowed, let go if out. Which is
        // what makes the alternating chain — fire E, swing, fire Q, release E, re-anchor
        // E further ahead — a rhythm on two keys rather than a chord on four.
        if (InputMap.RightHookPressed) ToggleSoldierHook(rig, right: true, eye, look, at, holding);
        if (InputMap.LeftHookPressed) ToggleSoldierHook(rig, right: false, eye, look, at, holding);

        if (InputMap.HighJumpPressed && rig.Jump(Player))
        {
            Audio.PlayGasJump(rig.Starvation);
            // The ring of dust blasted out from under the launch.
            Debris.FootPuff(new Vector3(Player.Position.X, 0f, Player.Position.Y));
        }

        // Rockets before the rifle: on a frame both are down, the deliberate shot wins.
        // They share the fire cooldown, and a player holding fire while clicking for a
        // rocket should get the rocket rather than have it eaten by the stream.
        if (InputMap.RocketPressed) FireSoldierRocket();
        else if (InputMap.RifleDown) FireSoldierRifle();
    }

    /// <summary>
    /// One hook's key press. Fires it at whatever the crosshair is on, or — if it is
    /// already flying or anchored — brings it home. A shot at nothing still goes: the
    /// cable pays out its full reach and zips back, which is the honest feedback that
    /// the player aimed at sky.
    /// </summary>
    private void ToggleSoldierHook(SoldierRig rig, bool right, Vector3 eye, Vector3 look,
        Vector3 at, Structure? holding)
    {
        GrappleHook hook = rig.Hook(right);

        if (hook.Out)
        {
            rig.ReleaseHook(right);
            Audio.PlayCableZip();
            return;
        }

        // Fired from the hip on the matching side rather than from the eye, so the two
        // cables visibly leave different points on the body and cross where they should.
        Vector3 from = SoldierMuzzle(right);
        rig.FireHook(right, new Vector2(from.X, from.Z), from.Y, look,
            AnchorInSight, AnchorInSight != null ? holding : null);
        Audio.PlayCableFire();
    }

    /// <summary>
    /// Where a cable leaves the rig: out from the hip on its own side, at belt height.
    /// Shared by the sim (which fires from here) and the renderer (which draws from
    /// here), so the line the player sees is the line the hook actually flew.
    /// </summary>
    public Vector3 SoldierMuzzle(bool right)
    {
        Vector2 fwd = Player.Forward;
        var side = new Vector2(-fwd.Y, fwd.X) * (right ? 0.55f : -0.55f);
        return new Vector3(
            Player.Position.X + side.X + fwd.X * 0.4f,
            Player.Height + SoldierRig.ShoulderHeight,
            Player.Position.Y + side.Y + fwd.Y * 0.4f);
    }

    /// <summary>
    /// Walks the crosshair's ray out through the city looking for something to bite.
    /// Returns the point on the surface it found and which building it belongs to.
    ///
    /// Only the skyline is an anchor. Not the grid — a hook that could always bite the
    /// floor would make every one of these decisions free — and not the monsters, which
    /// would be a different game. That the city is the <em>only</em> thing holding the
    /// player up is what makes a rocket fired at the wrong tower a genuine mistake.
    ///
    /// Marched rather than solved, for the same reason the spider's lance walks its
    /// shaft: a half-metre stride is far finer than any footprint out there, it handles
    /// a ray that climbs or dives without a second case, and it costs a few hundred
    /// distance tests against the handful of buildings actually in front of the player.
    /// </summary>
    public bool TryFindAnchor(Vector3 origin, Vector3 direction,
        out Vector3 point, out Structure? holding)
    {
        point = default;
        holding = null;

        Vector3 d = Vector3.Normalize(direction);
        var originXZ = new Vector2(origin.X, origin.Z);
        var dirXZ = new Vector2(d.X, d.Z);

        // Everything the ray could possibly reach, gathered once so the march itself
        // only ever tests a handful of circles.
        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        Span<(Vector2 At, float Radius, float Top, int Owner)> near =
            stackalloc (Vector2, float, float, int)[MaxAnchorCandidates];
        int count = 0;

        for (int i = 0; i < Structures.Count && count < MaxAnchorCandidates; i++)
        {
            Structure s = Structures[i];
            int n = s.Blockers(blockers);
            for (int b = 0; b < n && count < MaxAnchorCandidates; b++)
            {
                var (at, r) = blockers[b];
                float reach = SoldierRig.MaxRange + r;
                if (Torus.DistanceSquared(at, originXZ) > reach * reach) continue;
                // Held as the nearest image so the march never has to wrap: a building
                // just over the seam is tested where the player can actually see it.
                near[count++] = (Torus.NearestImage(at, originXZ), r, s.BlockHeight, i);
            }
        }
        if (count == 0) return false;

        for (float t = MinAnchorRange; t <= SoldierRig.MaxRange; t += AnchorStep)
        {
            float y = origin.Y + d.Y * t;
            if (y < 0f) return false;             // the ray has gone into the grid

            Vector2 xz = originXZ + dirXZ * t;

            for (int i = 0; i < count; i++)
            {
                var (at, r, top, owner) = near[i];
                if (y > top) continue;
                if (Vector2.DistanceSquared(xz, at) > r * r) continue;

                // Pull the bite back out onto the surface rather than leaving it at the
                // sample point inside the wall, so the cable ends on the building and
                // the swing radius is the one the player can see.
                Vector2 outward = xz - at;
                float len = outward.Length();
                Vector2 surface = len > 1e-4f ? at + outward / len * r : at + new Vector2(r, 0f);

                point = new Vector3(surface.X, MathF.Max(1f, y), surface.Y);
                holding = Structures[owner];
                return true;
            }
        }

        return false;
    }

    /// <summary>Stride the anchor march walks the ray in. Well under the tightest
    /// footprint out there, so nothing can sit between two samples.</summary>
    private const float AnchorStep = 0.5f;

    /// <summary>No anchoring closer than this. A hook that bit the wall you are already
    /// scraping along would be a hook that does nothing.</summary>
    private const float MinAnchorRange = 4f;

    /// <summary>How many footprints the march will consider at once. Comfortably more
    /// than the city ever puts inside one cable's reach.</summary>
    private const int MaxAnchorCandidates = 16;

    /// <summary>
    /// Capture harness only: a WASD to drive the rig with instead of the keyboard's.
    /// Screenshotting this chassis means holding a reel in for a second or two while a
    /// swing builds, and there is nobody at the keys during a capture run.
    /// </summary>
    public Vector2? ScriptedSoldierMove;

    /// <summary>Pulls the rifle's trigger without a mouse — the capture harness and the
    /// self-test's way in. Honours the cooldown and the magazine exactly as a click
    /// does, so a burst driven through here is a burst the player could fire.</summary>
    public void FireSoldierRifleForTest() => FireSoldierRifle();

    /// <summary>The same for a rocket.</summary>
    public void FireSoldierRocketForTest() => FireSoldierRocket();

    /// <summary>
    /// Throws one hook at whatever the eye is currently pointed at, without waiting on a
    /// key edge — the headless self-test's way in, since it has no mouse. Returns whether
    /// there was anything out there to bite; a false still throws the cable, exactly as
    /// pressing the key at open sky does.
    /// </summary>
    public bool FireSoldierHookForTest(bool right)
    {
        if (Player.Soldier is not { } rig) return false;

        Vector3 eye = Player.Eye;
        Vector3 look = Player.Forward3;
        bool found = TryFindAnchor(eye, look, out Vector3 at, out Structure? holding);

        Vector3 from = SoldierMuzzle(right);
        rig.FireHook(right, new Vector2(from.X, from.Z), from.Y, look,
            found ? at : null, found ? holding : null);
        return found;
    }

    /// <summary>
    /// Drains the rig's events after the physics have run: the cues, the dust, and the
    /// damage a landing or a crash costs. Split out from the rig itself so that the
    /// physics stay a pure function of their own state and the world keeps sole
    /// ownership of hurting the player.
    /// </summary>
    private void UpdateSoldierEvents(SoldierRig rig, float dt)
    {
        // Both hooks, named rather than iterated: this runs every tick of every frame,
        // and an array built to walk two fields is two fields' worth of garbage a tick.
        SoundHookEvents(rig.Left);
        SoundHookEvents(rig.Right);

        // The gas jet while it pulls, the wind past the ears, and the steel creaking
        // under whatever weight it is carrying. All three are beds, fed every tick with
        // whatever the rig is doing and left to fade on their own the moment it stops —
        // see Audio.Update, which clears them after servicing.
        Audio.SetReel(rig.Reeling, 1f - rig.Starvation);
        Audio.SetWind(rig.PlanarSpeed > WindSpeed,
            Math.Clamp((rig.PlanarSpeed - WindSpeed) / 18f, 0f, 1f));

        float strain = MathF.Max(rig.Left.Taut ? rig.Left.Tension : 0f,
                                 rig.Right.Taut ? rig.Right.Tension : 0f);
        Audio.SetCableStrain(strain > 0f, strain);

        // And the tick that says the bottle is nearly out. Rate-limited inside Audio, so
        // it is safe to ask for on every tick the gauge is in the red.
        if (Player.Hyper < GasWarnLevel && !rig.Grounded) Audio.PlayGasLow();

        if (rig.JustCrashed)
        {
            Audio.PlayCrashLanding();
            DamagePlayer(CrashDamage);
            Debris.Burst(new Vector3(Player.Position.X, Player.Height + 1f, Player.Position.Y),
                Palette.StructureShell, elite: false);
        }

        if (rig.JustLanded && rig.LandingSpeed > SoldierRig.HardLanding)
        {
            Audio.PlayCrashLanding();
            Debris.FootPuff(new Vector3(Player.Position.X, 0f, Player.Position.Y));

            // Anything past a twenty-metre drop is paid for in shield, scaled by how far
            // past it went — a bad landing hurts, a catastrophic one nearly ends you.
            if (rig.LandingSpeed > SoldierRig.FallDamageSpeed)
            {
                float over = (rig.LandingSpeed - SoldierRig.FallDamageSpeed)
                           / (SoldierRig.FallDamageSpeed * 0.6f);
                DamagePlayer(FallDamageBase + FallDamageBase * Math.Clamp(over, 0f, 2f));
            }
        }
    }

    /// <summary>
    /// Voices one hook's single-frame events: the clank of a bite, the zip of a miss,
    /// and the splintering crack of an anchor tearing out — with a spray of masonry off
    /// the point it tore from, so the failure is seen as well as heard.
    /// </summary>
    private void SoundHookEvents(GrappleHook h)
    {
        if (h.JustBit)
            Audio.PlayAnchorBite(Torus.Distance(h.Tip, Player.Position));
        if (h.JustMissed)
            Audio.PlayCableZip();
        if (h.JustTore)
        {
            Audio.PlayAnchorTear();
            Debris.Burst(new Vector3(h.Tip.X, h.TipY, h.Tip.Y), Palette.StructureShell, elite: false);
        }
    }

    /// <summary>Planar speed past which the wind is audible at all.</summary>
    private const float WindSpeed = 12f;

    /// <summary>Reserve below which the rig starts ticking a warning — about one jump's
    /// worth left, which is the only amount worth being told about.</summary>
    private const float GasWarnLevel = 26f;

    /// <summary>What swinging into a wall costs. Well short of lethal on a full shield:
    /// the real punishment for a crash is the momentum, and momentum is this chassis's
    /// only currency.</summary>
    private const float CrashDamage = 14f;

    /// <summary>What the mildest damaging landing costs, before the overshoot scaling.</summary>
    private const float FallDamageBase = 12f;

    /// <summary>
    /// A rifle round down the eye's line. Cheap, fast, and — the point of the whole
    /// weapon — entirely usable mid-swing: nothing here touches the rig, so firing never
    /// breaks an arc.
    /// </summary>
    private void FireSoldierRifle()
    {
        if (Seizure is { Held: true }) return;
        if (!Player.TryFireRifle(out Vector3 origin, out Vector3 dir)) return;

        if (Digestion is { } digestion && digestion.Held)
        {
            digestion.RegisterShot();
            Audio.PlayRifleShot();
            return;
        }

        SpawnDirected(origin, dir, Projectile.RifleSpeed, rocket: false);
        Audio.PlayRifleShot();

        if (Player.Soldier is not { } rig) return;

        // The punch: a small nudge up the view, and a shell case tumbling out past the
        // ear. Both exist for the same reason — a weapon that produced a tracer and
        // nothing else would read as a cursor emitting dots.
        rig.Kick(0.012f);

        Vector2 fwd = Player.Forward;
        var side = new Vector2(-fwd.Y, fwd.X) * (rig.FlashOnRight ? 1f : -1f);
        Debris.Fleck(
            Player.Eye + new Vector3(side.X, -0.15f, side.Y) * 0.5f,
            new Vector3(side.X * 3.4f, 1.6f, side.Y * 3.4f)
                + new Vector3(-fwd.X, 0f, -fwd.Y) * 1.8f
                + new Vector3(rig.Velocity.X, 0f, rig.Velocity.Z),
            Palette.Flag, life: 0.45f);
    }

    /// <summary>
    /// A rocket. Contact-fused, so the world's ordinary projectile pass detonates it the
    /// instant it meets anything — including the grid, which a round fired down the line
    /// of a dive will find quickly.
    /// </summary>
    private void FireSoldierRocket()
    {
        if (Seizure is { Held: true } || Digestion is { Held: true }) return;
        if (!Player.TryFireRocket(out Vector3 origin, out Vector3 dir)) return;

        SpawnDirected(origin, dir, Projectile.RocketSpeed, rocket: true);
        Audio.PlayRocketLaunch();
    }

    private void SpawnDirected(Vector3 origin, Vector3 dir, float speed, bool rocket,
        bool acid = false)
    {
        foreach (var p in _projectiles)
        {
            if (p.Active) continue;
            p.FireDirected(origin, dir, speed, rocket, acid);
            return;
        }
    }

    // --- The FISH ---------------------------------------------------------------

    /// <summary>
    /// Where a fish opens the run. Not at the origin on the deck — a chassis whose entire
    /// design is about never touching the grid must not begin by touching it — but up
    /// level with the middle of the skyline, already moving, looking out across the city
    /// it is about to thread. The first thing the player sees is the thing the class is
    /// for, and they see it before they have pressed anything.
    /// </summary>
    private void SwimTheFishOffTheDeck()
    {
        Player.Height = OpeningDepth;
        Player.Pitch = -0.12f;   // nosed a touch down, so the reef is in frame and the sky isn't
        if (Player.Fish is { } body)
            body.Velocity = new Vector3(0f, 0f, FishRig.BeatImpulse);
    }

    /// <summary>
    /// How high a fish opens: squarely in the middle of the band it actually plays in —
    /// well clear of the grid, well under the warned water, and level with the middle of
    /// the towers rather than above them. The opening frame should show the player the
    /// city they are about to thread, not a view down onto it.
    /// </summary>
    private const float OpeningDepth = 20f;

    /// <summary>
    /// The FISH's buttons: the tail, the strike and the spit. Short, because on this
    /// chassis almost everything that happens is physics rather than input — one beat is
    /// one press and the water does the rest.
    /// </summary>
    private void UpdateFishTriggers(FishRig body)
    {
        // A cinematic has hold of the player. Nothing is read: a monster swinging a body
        // around by its tail is not a situation the water has an opinion about.
        if (Player.Captured) return;

        if (InputMap.BeatPressed) BeatFish(body);

        // The strike before the spit: on a frame both are down, the committed attack
        // wins. They share the fire cooldown, and a player holding the spit while
        // clicking for a strike should get the strike rather than have it eaten.
        if (InputMap.StrikePressed) StrikeFish(body);
        else if (InputMap.SpitDown) FireFishSpit();
    }

    /// <summary>One beat of the tail, with its cue and — off the deck — the puff of grid
    /// dust a flop kicks up under it.</summary>
    private void BeatFish(FishRig body)
    {
        bool beached = body.Beached;
        if (!body.Beat(Player)) return;

        Audio.PlayTailBeat(body.Starvation, beached);
        if (beached)
            Debris.FootPuff(new Vector3(Player.Position.X, 0f, Player.Position.Y));
    }

    /// <summary>Winds a strike, if the reserve and the body will allow one.</summary>
    private void StrikeFish(FishRig body)
    {
        if (body.BeginStrike(Player)) Audio.PlayFishCoil();
    }

    /// <summary>
    /// A spit down the eye's line. Cheap, fast, and — the point of the whole weapon —
    /// entirely usable mid-carve: nothing here touches the body, so firing never breaks
    /// an arc.
    /// </summary>
    private void FireFishSpit()
    {
        if (Seizure is { Held: true }) return;
        if (!Player.TryFireSpit(out Vector3 origin, out Vector3 dir)) return;

        // Inside the Maw-Core's throat every trigger is the escape trigger, exactly as it
        // is on every other chassis: there is nothing to aim at in a mouth.
        if (Digestion is { } digestion && digestion.Held)
        {
            digestion.RegisterShot();
            Audio.PlayFishSpit();
            return;
        }

        SpawnDirected(origin, dir, Projectile.RifleSpeed, rocket: false);
        Audio.PlayFishSpit();
        Player.Fish?.Kick(0.010f);
    }

    /// <summary>
    /// Drains the body's events after the physics have run: the cues, the beds, the
    /// strike's one hit, and what a crash or a beaching costs. Split out from the rig for
    /// the same reason the soldier's is — the physics stay a pure function of their own
    /// state, and the world keeps sole ownership of hurting anything.
    /// </summary>
    private void UpdateFishEvents(FishRig body)
    {
        // The water going past. The single loudest carrier of speed on a chassis with no
        // engine note to ride, fed every tick and left to fade on its own.
        Audio.SetWind(body.PlanarSpeed > FishWashSpeed,
            Math.Clamp((body.PlanarSpeed - FishWashSpeed) / 20f, 0f, 1f));

        UpdateBloom(body);

        // And the tick that says the breath is nearly out — only while off the deck,
        // since a beached fish has larger problems and is already being told about them.
        if (Player.Hyper < BreathWarnLevel && !body.Beached) Audio.PlayGasLow();

        if (body.JustStruck) Audio.PlayFishStrike();
        if (body.StrikeActive) SweepStrike(body);

        if (body.JustCrashed)
        {
            Audio.PlayCrashLanding();
            DamagePlayer(CrashDamage);
            Debris.Burst(new Vector3(Player.Position.X, Player.Height + 1f, Player.Position.Y),
                Palette.StructureShell, elite: false);
        }

        if (body.JustBeached)
        {
            Audio.PlayFishBeach(Math.Clamp(body.BeachImpact / 20f, 0f, 1f));
            Debris.FootPuff(new Vector3(Player.Position.X, 0f, Player.Position.Y));

            // Meeting the seabed hard costs shield, scaled by how hard. A fish has no
            // armour and no legs to take it with — the grid is simply not somewhere this
            // body is built to arrive at.
            if (body.BeachImpact > FishRig.BeachImpactSpeed)
            {
                float over = (body.BeachImpact - FishRig.BeachImpactSpeed)
                           / (FishRig.BeachImpactSpeed * 0.8f);
                DamagePlayer(BeachDamage + BeachDamage * Math.Clamp(over, 0f, 2f));
            }
        }
    }

    /// <summary>
    /// One tick of a live strike, looking for the single thing it spears. Nearest-first
    /// is deliberately <em>not</em> what this does: the lunge is short and fast enough
    /// that at most one candidate is ever inside its reach on a given tick, and walking
    /// the roster in a fixed order costs nothing and never allocates.
    ///
    /// The snout reaches the two crystals as well as the hunters, which is the whole
    /// reward for the class living up where they do. A Maw-Core in particular has spent
    /// the entire game being hittable only from the top of a jump; to a fish it is simply
    /// something else swimming at the same depth.
    /// </summary>
    private void SweepStrike(FishRig body)
    {
        var at = new Vector3(Player.Position.X, Player.Height, Player.Position.Y);
        float reach = FishRig.StrikeReach;

        // Hunters sit on the grid, so a strike only reaches one if the dive has genuinely
        // come down to them — a fish cruising at thirty metres cannot spear something on
        // the floor by pointing at it, and the whole cost of the attack is committing to
        // the descent that makes it possible.
        if (Player.Height <= reach + EnemyTank.Radius)
        {
            foreach (var e in Enemies)
            {
                if (!e.Alive) continue;
                if (!WithinHit(Player.Position, e.Position, reach + EnemyTank.Radius)) continue;
                if (!body.ConsumeStrike()) return;
                DamageEnemy(e, FishRig.StrikeDamage);
                LandStrike(at, Palette.EnemyFill);
                return;
            }
        }

        if (Boss is { } boss && boss.HitsCore(Player.Position, Player.Height))
        {
            if (!body.ConsumeStrike()) return;
            if (boss.DamageCore(FishRig.StrikeDamage)) DestroyBoss(boss);
            else Audio.PlayCoreHit(1f - boss.CoreFraction);
            LandStrike(at, Palette.NeonRed);
            return;
        }

        if (Maw is { } maw && maw.HitsCrystal(Player.Position, Player.Height))
        {
            if (!body.ConsumeStrike()) return;
            if (maw.DamageCrystal(FishRig.StrikeDamage)) DestroyMaw(maw);
            else Audio.PlayMawHurt(1f - maw.CrystalFraction);
            LandStrike(at, Palette.NeonRed);
        }
    }

    /// <summary>A strike connecting: the impact cue and a spray off whatever it went
    /// into. Shared by all three targets so a spear always reads the same.</summary>
    private void LandStrike(Vector3 at, Color colour)
    {
        Audio.PlayFishImpact();
        Debris.Burst(at, colour, elite: true);
        Player.Fish?.Jolt(0.45f);
    }

    /// <summary>
    /// The ceiling biting. Two separate things happen here and the order they happen in is
    /// the whole point: a player who climbs too high is <em>told</em> first — an alarm, a
    /// stain across the top of the frame, and a tail that visibly stops gripping — across
    /// a ten-metre band that costs them nothing at all. Only past that does it start
    /// taking shield.
    ///
    /// The damage is ticked rather than applied per frame for the same two reasons the
    /// Crab-Core's beam is: it stops depending on the frame rate, and each bite fires its
    /// own cue, which at sixty a second would be a solid tone rather than the sound of
    /// being repeatedly hurt.
    /// </summary>
    private void UpdateBloom(FishRig body)
    {
        // The warning band. Free, loud, and rate-limited inside Audio so it is safe to ask
        // for every tick the body is up here.
        if (body.BloomNotice > 0f && !body.InBloom) Audio.PlayBloomWarning();

        if (!body.InBloom)
        {
            _bloomTick = 0f;
            return;
        }

        // In it. The alarm goes from a tick to a proper klaxon, and the clock starts.
        Audio.PlayBloomAlarm();

        _bloomTick += (float)Config.FixedDt;
        if (_bloomTick < BloomTickInterval) return;
        _bloomTick -= BloomTickInterval;

        // Scaled by how far past the floor of it the body has pushed. At the boundary this
        // is a slow leak that a player can climb out of and repair; deep in, it is roughly
        // a shot from a hunter every half second, which no build survives for long.
        float bite = BloomBiteDamage * (1f + 2.2f * body.Toxicity);
        DamagePlayer(bite);
        body.Jolt(0.12f + 0.2f * body.Toxicity);
    }

    private float _bloomTick;

    /// <summary>How often the bloom bites while the body is in it. Twice a second — often
    /// enough that the drain is unmistakable, rare enough that each bite is a discrete
    /// event the player can count.</summary>
    private const float BloomTickInterval = 0.5f;

    /// <summary>What one bite costs at the very floor of the bloom, before the depth
    /// scaling. Deliberately survivable at the boundary: the first second up there should
    /// be a mistake, not a death.</summary>
    private const float BloomBiteDamage = 4f;

    /// <summary>Planar speed past which the water is audible at all.</summary>
    private const float FishWashSpeed = 14f;

    /// <summary>Reserve below which the body starts ticking a warning — about two beats'
    /// worth left, which is the only amount worth being told about.</summary>
    private const float BreathWarnLevel = 20f;

    /// <summary>What the mildest damaging arrival on the seabed costs, before the
    /// overshoot scaling.</summary>
    private const float BeachDamage = 10f;

    /// <summary>
    /// Capture harness and self-test only: a roll/brake to drive the body with instead of
    /// the keyboard's. Screenshotting this chassis means holding a carve through most of a
    /// second while a turn develops, and there is nobody at the keys during a capture run.
    /// </summary>
    public Vector2? ScriptedFishMove;

    /// <summary>Beats the tail without a keyboard — the harness's and the self-test's way
    /// in. Honours the refractory period and the reserve exactly as a press does.</summary>
    public bool BeatFishForTest()
    {
        if (Player.Fish is not { } body) return false;
        bool before = body.Beached;
        if (!body.Beat(Player)) return false;
        if (before) Debris.FootPuff(new Vector3(Player.Position.X, 0f, Player.Position.Y));
        return true;
    }

    /// <summary>The same for a strike.</summary>
    public bool StrikeFishForTest()
        => Player.Fish is { } body && body.BeginStrike(Player);

    /// <summary>And for the spit.</summary>
    public void FireFishSpitForTest() => FireFishSpit();

    // --- The VIRUS --------------------------------------------------------------

    /// <summary>
    /// The VIRUS's two triggers. Left fires whatever the current body fires — the mote's
    /// and a worn hunter's corruption bolt, the worn maw's acid spit, or the worn crab's
    /// broken lance. Right overloads the host into a bomb, and takes precedence on a frame
    /// both are down: the committed spend wins, exactly as the grenade beats the cannon and
    /// the strike beats the spit.
    /// </summary>
    private void UpdateVirusTriggers(VirusRig mote)
    {
        if (Player.Captured) return;

        if (mote.Hosted && InputMap.VirusOverloadPressed)
        {
            OverloadVirus(mote);
            return;   // it ejected — nothing else fires from a body that no longer exists
        }

        if (!InputMap.VirusFireDown) return;

        if (mote.HostKind == VirusHost.Crab) FireVirusLance(mote);
        else FireVirusRound(mote);
    }

    /// <summary>
    /// A virus round down the eye's line. Cheap, fast, and usable in the middle of a dart or
    /// a drive — nothing here touches the rig, so firing never breaks the flight. Routed
    /// through the same directed-round path the soldier's rifle and the fish's spit use, so
    /// it is blocked by the skyline, bites hunters and finds the two crystals identically.
    /// Fired out of a worn maw it leaves as the mouth's own acid bolt instead — slower, and
    /// unmistakably the monster's ordnance rather than a re-tinted rifle.
    /// </summary>
    private void FireVirusRound(VirusRig mote)
    {
        if (Seizure is { Held: true }) return;
        if (!Player.TryFireVirus(out Vector3 origin, out Vector3 dir)) return;

        // Inside the Maw-Core's throat every trigger is the escape trigger — there is
        // nothing to aim at in a mouth.
        if (Digestion is { } digestion && digestion.Held)
        {
            digestion.RegisterShot();
            Audio.PlayLaser();
            return;
        }

        bool acid = mote.HostKind == VirusHost.Maw;
        SpawnDirected(origin, dir, acid ? AcidSpitSpeed : Projectile.RifleSpeed,
            rocket: false, acid: acid);
        Audio.PlayLaser();          // a dry corruption zap, the SPIDER's emitter clip
        mote.Jolt(0.05f);
    }

    /// <summary>How fast a worn maw's acid bolt travels. Well under the rifle's ninety —
    /// the monster's own spit has always been slow enough to walk away from, and its
    /// stolen version keeps the family resemblance while staying usable as a weapon.</summary>
    private const float AcidSpitSpeed = 48f;

    /// <summary>
    /// The stolen lance. The Crab-Core's own beam, fired by whatever is wearing the crab —
    /// and it did not survive the theft intact. A corrupted core cannot hold a line, so one
    /// trigger pull leaves as a main shaft thrown roughly (only roughly) where it was aimed
    /// plus a spray of breaks in directions nobody chose, every one of them a genuine beam:
    /// it cuts buildings down, rakes hunters, and finds the two crystals, exactly as the
    /// intact weapon would. The bill is paid in the host itself — each discharge burns a
    /// slice of the decay meter, and the rig lets a shot on the last of it finish the body.
    /// </summary>
    private void FireVirusLance(VirusRig mote)
    {
        if (Seizure is { Held: true } || Digestion is { Held: true }) return;
        if (!mote.TryLance()) return;

        Vector3 aim = Player.Forward3;

        // The aimed shaft plus 2..4 breaks. The main one is jittered a touch off true —
        // even the shot you meant is not quite the shot you get — and the breaks are
        // thrown well off it, each on its own random axis.
        //
        // Each shaft carries two origins on purpose. The damage runs from just off the
        // eye, so nothing standing at point-blank sits in a dead zone under the beam. The
        // *picture* starts several units further out along the shaft's own line: its near
        // end (cap, sheath and muzzle flare) is the widest thing in a first-person frame,
        // several of them leave on one pull, and drawn from the eye they union into a wall
        // of red the player fires blind through. Spreading the visual origins down each
        // shaft's own direction also breaks the single shared ball into a visible fan.
        int breaks = 2 + Random.Shared.Next(3);
        for (int i = 0; i <= breaks; i++)
        {
            Vector3 dir = JitterDirection(aim, i == 0
                ? 0.05f
                : 0.18f + 0.45f * Random.Shared.NextSingle());
            mote.AddShaft(Player.Eye + dir * 8f, dir);
            BurnBeamAlong(Player.Eye + dir * 0.8f, dir, VirusRig.LanceLength,
                VirusRig.LanceRadius, VirusRig.LanceDamage);
        }

        Audio.PlayUnstableLance();
        mote.Jolt(0.3f);

        // A shot fired on the last of the meter finished the host (see VirusRig.TryLance):
        // the body it just burned out goes up around the player.
        if (!mote.Hosted) StageVirusBurst(overload: false);
    }

    /// <summary>A unit direction thrown up to <paramref name="spread"/> radians off
    /// <paramref name="dir"/>, on a random axis — how the broken lance decides where a
    /// shaft actually goes.</summary>
    private static Vector3 JitterDirection(Vector3 dir, float spread)
    {
        // A random perpendicular: cross with the axis least aligned to the direction, then
        // roll it around the direction itself.
        Vector3 seed = MathF.Abs(dir.Y) < 0.8f ? new Vector3(0f, 1f, 0f) : new Vector3(1f, 0f, 0f);
        Vector3 side = Vector3.Normalize(Vector3.Cross(dir, seed));
        float roll = Random.Shared.NextSingle() * MathF.Tau;
        Vector3 axis = side * MathF.Cos(roll)
                     + Vector3.Normalize(Vector3.Cross(dir, side)) * MathF.Sin(roll);

        float off = spread * (0.35f + 0.65f * Random.Shared.NextSingle());
        return Vector3.Normalize(dir * MathF.Cos(off) + axis * MathF.Sin(off));
    }

    /// <summary>
    /// Spends the worn host as a detonation: the rig ejects the player back to the mote, and
    /// the husk goes off as a corrupted energy burst — the very same radial star a thrown
    /// CRAB CORE throws, which is exactly the right picture for a core forced to overload and
    /// comes with its area damage already wired. One place, so the test hatch and the trigger
    /// stage identical blasts.
    /// </summary>
    private void OverloadVirus(VirusRig mote)
    {
        // Guarded here rather than only at the trigger, so the test hatch can never stage
        // a free detonation off a mote with no body to spend.
        if (!mote.Hosted) return;
        mote.Overload();
        StageVirusBurst(overload: true);
    }

    /// <summary>
    /// Drains the mote's events after the physics have run: the infection contact test, the
    /// withering clock, the rush of flight, and the one ejection the world can only learn
    /// about from a flag — the decay clock running out. The other ejections (an overload, a
    /// host shot out from under the player, a lance fired on the last of the meter) are
    /// staged at their source, because they happen at points in the tick where a flag
    /// drained here would be a frame late or lost entirely.
    /// </summary>
    private void UpdateVirusEvents(VirusRig mote)
    {
        // While exposed, flying the mote into a body seizes it — checked every tick the
        // way salvage collection is, so contact is enough and there is no button to fumble.
        if (mote.Exposed) TryInfect(mote);

        UpdateWithering(mote);

        // The rush past the ears, fed off the rig's flight speed and left to fade on its
        // own — the same bed the soldier's wind and the fish's wash ride.
        Audio.SetWind(mote.PlanarSpeed > VirusRushSpeed,
            Math.Clamp((mote.PlanarSpeed - VirusRushSpeed) / 20f, 0f, 1f));

        if (mote.JustEjected) StageVirusBurst(overload: false);
    }

    /// <summary>
    /// The mote coming apart in the open. The grace itself is free and mostly silent — a
    /// low tick starts near its end, so the player is told before the first bite — and past
    /// it the withering bills shield on a slow clock until a body is reached.
    ///
    /// The damage goes through <see cref="PlayerTank.TakeDamage"/> directly rather than
    /// <see cref="DamagePlayer"/> on purpose: that path amplifies hits on an exposed mote,
    /// which is a rule about <em>weapons</em> finding an unarmoured target. The withering
    /// is not a weapon — it is the mote's own physiology, already tuned in this constant.
    /// </summary>
    private void UpdateWithering(VirusRig mote)
    {
        if (!mote.Exposed || Player.Captured) return;

        // The courtesy tick as the grace runs out — rate-limited inside Audio, so it is
        // safe to ask for on every tick of the last stretch.
        if (!mote.Withering)
        {
            _witherTick = 0f;
            if (mote.GraceRemaining < WitherWarnTime) Audio.PlayGasLow();
            return;
        }

        _witherTick += (float)Config.FixedDt;
        if (_witherTick < WitherTickInterval) return;
        _witherTick -= WitherTickInterval;

        Player.TakeDamage(WitherBite);
        mote.Jolt(0.15f);
        if (!Player.Alive) Audio.PlayExplosion();
        else Audio.PlayWarning();
    }

    private float _witherTick;

    /// <summary>How often the withering bites once the grace is spent. Twice a second, the
    /// bloom's own cadence — discrete events the player can count, not a smooth drain.</summary>
    private const float WitherTickInterval = 0.5f;

    /// <summary>What one bite costs. Slow enough to cross most of the arena on after the
    /// grace has already run out; fast enough that living unhosted is never a plan.</summary>
    private const float WitherBite = 3f;

    /// <summary>Seconds of grace left when the warning tick starts.</summary>
    private const float WitherWarnTime = 5f;

    /// <summary>
    /// Seizes whatever body the mote has flown into, if any. The world owns this rather
    /// than the rig because only the world knows what is out there — and every body is
    /// <em>consumed</em>, not destroyed: no death blast, no debris, no fragment. A hunter
    /// drops silently off the roster; a monster is simply no longer on the field, because
    /// the player is now wearing it.
    ///
    /// The two big machines are entered the way everything else in this game reaches them:
    /// through the bright core. The same geometry that scores a bullet on the crab's gem or
    /// the maw's crystal admits a mote flown into it — the weak point is, for this one
    /// class, a door.
    /// </summary>
    private void TryInfect(VirusRig mote)
    {
        if (Player.Captured) return;

        // The Crab-Core, entered through the gem. Alive only — a dying rig mid-glitch is
        // not a body anymore. The probe rides half a unit up the mote, roughly its middle.
        if (Boss is { Alive: true } boss
            && boss.HitsCore(Player.Position, Player.Height + 0.5f))
        {
            Player.Position = boss.Position;
            Boss = null;                        // worn, not wrecked
            mote.Possess(Player, VirusHost.Crab);
            Audio.PlayMawSwallow();
            Audio.PlayClamp();                  // the carapace closing around its new owner
            return;
        }

        // The Maw-Core, entered through the crystal. The player keeps their height: they
        // are the hovering mouth now, and it does not fall out of the sky on possession.
        if (Maw is { Alive: true } maw
            && maw.HitsCrystal(Player.Position, Player.Height + 0.5f))
        {
            Player.Position = maw.Position;
            Maw = null;
            mote.Possess(Player, VirusHost.Maw);
            Audio.PlayMawSwallow();
            return;
        }

        float reach = PlayerTank.Radius + EnemyTank.Radius + VirusRig.InfectMargin;
        float reachSq = reach * reach;

        EnemyTank? prey = null;
        float best = reachSq;
        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            // The mote has to have come down onto the body, not merely be sailing overhead:
            // a hunter is a thing on the grid, and taking one means diving to its level.
            if (Player.Height > EnemyTank.BodyHeight + InfectVertical) continue;
            float d = Torus.DistanceSquared(e.Position, Player.Position);
            if (d < best) { best = d; prey = e; }
        }

        if (prey is null) return;

        bool elite = prey.IsElite;
        prey.TakeDamage(float.MaxValue);   // quietly consumed; RemoveAll clears it this tick
        Player.Position = prey.Position;   // climb into where the body stood
        mote.Possess(Player, elite ? VirusHost.Elite : VirusHost.Hunter);
        Audio.PlayMawSwallow();            // a wet gulp — something climbing inside a body
    }

    /// <summary>How far off the grid the mote may be and still reach a hunter to infect —
    /// low enough that seizing a body genuinely means diving onto it.</summary>
    private const float InfectVertical = 2.5f;

    /// <summary>
    /// Dresses an ejection. An overload spends the whole host as the CRAB CORE's own radial
    /// blast; a body that simply gave out throws off far less — a spatter of the mote's
    /// colour and a dull report, and no area damage, because letting a host rot is never
    /// rewarded the way deliberately spending one is.
    /// </summary>
    private void StageVirusBurst(bool overload)
    {
        if (overload)
        {
            StageCrabBlast(Player.Position);
            return;
        }

        Debris.Burst(new Vector3(Player.Position.X, Player.Height + 1f, Player.Position.Y),
            Palette.NeonMagenta, elite: false);
        Audio.PlayDetonation();
    }

    /// <summary>Planar flight speed past which the mote's rush is audible at all.</summary>
    private const float VirusRushSpeed = 16f;

    /// <summary>
    /// Capture harness and self-test only: a WASD to fly the mote or drive the host with
    /// instead of the keyboard's. Screenshotting this chassis means holding a dart for a
    /// second while speed builds, and there is nobody at the keys during a capture run.
    /// </summary>
    public Vector2? ScriptedVirusMove;

    /// <summary>Fires the virus round without a mouse — the harness's and the self-test's
    /// way in.</summary>
    public void FireVirusRoundForTest()
    {
        if (Player.Virus is { } mote) FireVirusRound(mote);
    }

    /// <summary>Overloads the worn host without a mouse, through the same path the trigger
    /// uses — so a test that drives an overload stages exactly the blast play does.</summary>
    public void OverloadVirusForTest()
    {
        if (Player.Virus is { } mote) OverloadVirus(mote);
    }

    /// <summary>Fires the worn crab's broken lance without a mouse — the harness's and the
    /// self-test's way in. Honours the cooldown and the decay cost exactly as a click does.</summary>
    public void FireVirusLanceForTest()
    {
        if (Player.Virus is { } mote) FireVirusLance(mote);
    }

    /// <summary>
    /// A rocket going off: the splash on everything nearby, and — alone among the
    /// player's ordnance short of a charged lance — the buildings inside the blast cut
    /// down. That last clause is the whole reason rockets are rationed. Anything the
    /// blast destroys stops being an anchor, and the game does not check first whether
    /// the player is currently hanging from it.
    /// </summary>
    private void DetonateRocket(Projectile p)
    {
        var at = new Vector3(p.Position.X, MathF.Max(0.4f, p.Height), p.Position.Y);

        float reach = Projectile.RocketSplash + EnemyTank.Radius;
        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            if (WithinHit(p.Position, e.Position, reach)) DamageEnemy(e, GrenadeDamage);
        }

        // The blast reaches the two crystals too, if either happens to be in it.
        if (Boss is { } boss && boss.HitsCore(p.Position, p.Height))
        {
            if (boss.DamageCore(GrenadeDamage)) DestroyBoss(boss);
            else Audio.PlayCoreHit(1f - boss.CoreFraction);
        }
        if (Maw is { } maw && maw.HitsCrystal(p.Position, p.Height))
        {
            if (maw.DamageCrystal(GrenadeDamage)) DestroyMaw(maw);
            else Audio.PlayMawHurt(1f - maw.CrystalFraction);
        }

        // Every standing footprint inside the blast comes down.
        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        for (int i = Structures.Count - 1; i >= 0; i--)
        {
            Structure s = Structures[i];
            int n = s.Blockers(blockers);
            for (int b = 0; b < n; b++)
            {
                var (bAt, r) = blockers[b];
                if (!WithinHit(p.Position, bAt, Projectile.RocketSplash + r)) continue;
                // Only what the blast can actually reach up the building's side: a rocket
                // going off at the foot of a forty-metre tower still fells it, but one
                // detonating in mid-air well above an arch's legs does not.
                if (p.Height > s.BlockHeight + Projectile.RocketSplash) continue;
                FellStructure(s);
                break;
            }
        }

        Debris.Burst(at, Palette.EliteFill, elite: true);
        Audio.PlayRocketBlast(Torus.Distance(p.Position, Player.Position));

        // The pressure ripple: everything nearby is thrown about, the player included.
        // A rocket fired at a wall you are swinging toward should be felt through the
        // camera, not merely heard.
        float range = Torus.Distance(p.Position, Player.Position);
        if (Player.Soldier is { } rig && range < RocketShakeRange)
        {
            rig.Jolt(0.35f + 0.65f * (1f - range / RocketShakeRange));
            // And it stings if the player is genuinely inside their own blast, which a
            // contact fuse on a fast approach makes entirely possible.
            if (range < Projectile.RocketSplash + PlayerTank.Radius)
                DamagePlayer(GrenadeDamage * 3f);
        }
    }

    /// <summary>How far a rocket's concussion still throws the view about.</summary>
    private const float RocketShakeRange = 34f;

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
        float launchHeight = Projectile.BoltHeight, bool laser = false, float pitch = 0f,
        bool piercing = false)
    {
        foreach (var p in _projectiles)
        {
            if (p.Active) continue;
            if (crabBomb) p.FireCrabBomb(origin, dir);
            else if (grenade) p.FireGrenade(origin, dir, fromPlayer);
            else p.Fire(origin, dir, fromPlayer, launchHeight, laser, pitch, piercing);
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

            // A rocket has a contact fuse and nothing else: it goes off wherever it
            // stops, which includes the grid it flew into and the empty air at the end
            // of its run.
            if (p.JustExpired && p.IsRocket)
                DetonateRocket(p);

            // The mortar's lob ends on the grid (or, rarely, at the very end of its life):
            // it bursts where it came down, splashing whatever it was dropped onto. A thrown
            // CRAB CORE is also flagged IsGrenade but is handled by its own branch above.
            if (p.JustExpired && p.IsGrenade && !p.IsCrabBomb)
                DetonateMortar(p);

            if (!p.Active) continue;

            // The skyline stops rounds — every round, from anyone. Tested before any
            // target, so a hunter standing behind a wall is genuinely behind it and a
            // shot cannot reach a boss's core through a tower. This is what makes the
            // buildings cover rather than decoration, and it cuts both ways: the thing
            // the player is hiding behind is also the thing they cannot shoot past.
            if (BlockShotOnStructure(p)) continue;

            if (p.FromPlayer)
            {
                // A rocket meeting either crystal goes off against it rather than
                // plinking it: the fuse does not care what it touched, and the blast is
                // already wired to bill both weak points for whatever it reaches.
                if (p.IsRocket
                    && ((Boss is { } rBoss && rBoss.HitsCore(p.Position, p.Height))
                     || (Maw is { } rMaw && rMaw.HitsCrystal(p.Position, p.Height))))
                {
                    DetonateRocket(p);
                    p.Active = false;
                    continue;
                }

                // A mortar (and only it) bursts on either boss it passes over. The lob flies a
                // real arc, so unlike a flat bolt it can be dropped onto or lobbed across the
                // column of a monster whose weak point hangs high overhead — a planar check, the
                // same one the thrown CRAB CORE uses, since a heavy detonation this near strikes
                // the core through the inert armour rather than needing to arrive at its height.
                // This is the tank's indirect answer to a boss it can no longer leap up to hit.
                if (p.IsGrenade && !p.IsCrabBomb
                    && ((Boss is { Alive: true } gBoss
                            && WithinHit(p.Position, gBoss.Position, p.SplashRadius + CrabCore.CoreHitRadius))
                     || (Maw is { Alive: true } gMaw
                            && WithinHit(p.Position, gMaw.Position, p.SplashRadius + MawRig.HitRadius))))
                {
                    DetonateMortar(p);
                    p.Active = false;
                    continue;
                }

                // The Crab-Core's only weak spot: a level air shot threading the
                // raised neon core. Checked before the tanks, since the core sits
                // where nothing else does — high overhead.
                if (!p.IsGrenade && !p.IsRocket
                    && Boss is { } liveBoss && liveBoss.HitsCore(p.Position, p.Height))
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
                if (!p.IsGrenade && !p.IsRocket
                    && Maw is { } liveMaw && liveMaw.HitsCrystal(p.Position, p.Height))
                {
                    if (liveMaw.DamageCrystal(PlayerShotDamage))
                        DestroyMaw(liveMaw);
                    else
                        Audio.PlayMawHurt(1f - liveMaw.CrystalFraction);
                    p.Active = false;
                    continue;
                }

                // A round riding high over a hunter's hull passes over it. The mortar joins
                // the rocket here: while it is still up the arc it sails over the hunters
                // massed in front of the target, and only meets them once it has come back
                // down onto them. Everything else in the pool travels flat at barrel height,
                // where the check would be noise.
                bool overhead = (p.IsRocket || p.IsGrenade) && p.Height > EnemyTank.BodyHeight + 1f;

                foreach (var e in Enemies)
                {
                    if (!e.Alive || overhead) continue;
                    if (!WithinHit(p.Position, e.Position, EnemyTank.Radius)) continue;

                    if (p.IsPiercing)
                    {
                        // The AP slug bulls straight on through the line: bill each body once —
                        // PierceLast guards the one it is currently crossing — and keep flying.
                        if (!ReferenceEquals(e, p.PierceLast))
                        {
                            DamageEnemy(e, SlugDamage);
                            p.PierceLast = e;
                        }
                        continue;
                    }

                    if (p.IsCrabBomb)
                        StageCrabBlast(p.Position); // erupts into the lance ring here
                    else if (p.IsRocket)
                        DetonateRocket(p);
                    else if (p.IsGrenade)
                        DetonateMortar(p);     // the shell has landed on the cluster
                    else
                        DamageEnemy(e, PlayerShotDamage);
                    p.Active = false;
                    break;
                }
            }
            else
            {
                // Enemy shot vs player. The hunters elevate onto the craft's height now,
                // so a leaping player is no longer simply spared — the round has to arrive
                // at the right place on the plane *and* the right height up the column, the
                // craft's body centre give or take its own height. The jump is still a
                // dodge, but by out-moving the shot in flight (no lead) rather than by the
                // old blanket immunity the instant a wheel left the grid.
                //
                // A captured player is the one exception. Being airborne used to be what
                // spared them while a set piece had them helpless; now that height no
                // longer grants immunity, the cinematic hold has to say so outright — a
                // player who cannot act must not be shot at by anything else.
                // Enemy rounds die in a TANK's smoke the same way the shooters are blinded by
                // it — a screen the player drove behind actually stops what is already in the
                // air, not only what is about to be fired.
                if (SmokeAbsorbs(p.Position))
                {
                    p.Active = false;
                    continue;
                }

                float aimH = Player.Height + EnemyTank.AimHeight;
                if (!Player.Captured
                    && WithinHit(p.Position, Player.Position, PlayerTank.Radius)
                    && MathF.Abs(p.Height - aimH) < EnemyHitVertical)
                {
                    // Directional armour: on the TANK the blow is turned by the sloped front,
                    // taken square on the flanks and taken worse from behind (returns 1 for
                    // every other chassis, which has no plating). Facing is the tank's defence.
                    DamagePlayer(EnemyShotDamage * Player.ArmorMultiplierFromShot(p.Velocity));
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
        // The VIRUS stands its worn host between itself and every blow: while a body is on,
        // damage drains the host's integrity instead of the player's shield, and only what a
        // broken husk fails to soak spills through. Exposed, it is the opposite — the naked
        // mote takes it amplified, because a payload has no armour of its own.
        if (Player.Virus is { } virus)
        {
            if (virus.Hosted)
            {
                amount = virus.AbsorbDamage(amount);
                // A hit that emptied the meter burst the host: stage that here, at the
                // moment it happens, because this pass runs after the tick's virus events
                // and a flag drained there would be a frame late or lost.
                if (!virus.Hosted) StageVirusBurst(overload: false);

                if (amount <= 0f)
                {
                    // Wholly soaked: the body took it, the player didn't. A plain hit cue,
                    // so a corrupted host being worn down still sounds like being shot at.
                    Audio.PlayHit();
                    return;
                }
                // The host broke and the overflow is about to reach a naked mote — it eats
                // that spill amplified, the same as any exposed hit.
                amount *= MoteVulnerability;
            }
            else
            {
                amount *= MoteVulnerability;
            }
        }

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
