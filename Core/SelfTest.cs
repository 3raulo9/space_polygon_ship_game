using System.Numerics;
using VoidTanks.World;

namespace VoidTanks.Core;

/// <summary>
/// Headless behaviour check for the combat sim — no window, no graphics. Drives
/// <see cref="World"/> directly and asserts the loop actually works end to end:
/// the player can destroy an enemy, and an enemy can damage the player. Run with
/// `dotnet run -- --selftest`. Exits non-zero on failure so it can gate a build.
/// </summary>
public static class SelfTest
{
    public static int Run()
    {
        int failures = 0;
        failures += Check("the world is a finite wrap-around torus", WorldIsAFiniteTorus);
        failures += Check("the skyline is a city, not nine buildings", SkylineIsPopulated);
        failures += Check("the skyline is solid to the craft", StructuresBlockThePlayer);
        failures += Check("rounds stop at a wall and leave it standing", RoundsStopAtTheSkyline);
        failures += Check("a beam cuts a tower down and clears the wreck", BeamsCutStructuresDown);
        failures += Check("player can destroy an enemy", PlayerKillsEnemy);
        failures += Check("enemy can damage the player", EnemyDamagesPlayer);
        failures += Check("enemies elevate onto a player in the air", EnemyReachesPlayerInTheAir);
        failures += Check("ammo is finite", AmmoDepletes);
        failures += Check("salvage stows into the inventory, doesn't auto-charge", BatteryStowsThenCharges);
        failures += Check("bullet salvage stows a random handful of rounds", AmmoStowsThenLoads);
        failures += Check("three fragments craft a throwable CRAB CORE", FragmentsCraftCrabCore);
        failures += Check("a thrown CRAB CORE blast destroys enemies", CrabCoreBlastKills);
        failures += Check("a CRAB CORE blast can destroy the Crab-Core", CrabCoreBlastKillsBoss);
        failures += Check("a CRAB CORE blast can destroy the Maw-Core", CrabCoreBlastKillsMaw);
        failures += Check("grounded shot misses the boss core", GroundedShotMissesCore);
        failures += Check("air shot at core height kills the boss", AirShotKillsCore);
        failures += Check("air shot detonates on the horizon", AirShotExpiresForBlast);
        failures += Check("debug key spawns a random enemy", DebugSpawnAddsEnemy);
        failures += Check("the boss seizes and throws a cornered player", BossSeizesPlayer);
        failures += Check("a held player is raised to face the core", SeizureFramesTheCore);
        failures += Check("the boss's hands frame the held player", SeizureHandsReachThePlayer);
        failures += Check("the boss's beam fires where the player was", BeamLocksItsDirection);
        failures += Check("the maw hangs at the top of the player's jump", MawHangsAtJumpApex);
        failures += Check("only a leaping shot reaches the maw's crystal", MawNeedsAnAirShot);
        failures += Check("the maw swallows a player who stands under it", MawSwallowsStillPlayer);
        failures += Check("three shots from inside break the maw's hold", MawReleasesOnThreeShots);
        failures += Check("digestion bites 15% a time until you escape", MawDigestionBites);
        failures += Check("a swallowed player can shoot their way out for real", MawEscapeThroughTheGun);
        failures += Check("the loadout budget refuses an eleventh point", BudgetRefusesOverspend);
        failures += Check("a 5/5/5 build is exactly the historical craft", DefaultBuildIsTheOldCraft);
        failures += Check("loadout points reach the player's live stats", LoadoutDrivesPlayerStats);
        failures += Check("the spider's lance costs rounds and burns a line", SpiderLanceKills);
        failures += Check("charging the spider's lance roots the craft", SpiderChargeRootsTheCraft);
        failures += Check("the mouse turns the whole craft, not a turret", MouseTurnsTheWholeCraft);
        failures += Check("the tank's gun is stopped short of the sky", TankGunElevationIsShallow);
        failures += Check("the spider's gun cranes the full way up", SpiderGunCranesAllTheWay);
        failures += Check("the cannon fires where the craft is aimed", CannonFollowsTheAim);
        failures += Check("a raised tank gun still can't reach the maw's crystal", TankGunStaysUnderTheMaw);
        failures += Check("a soldier opens within a cable's throw of the city", SoldierStartsAtAnAnchor);
        failures += Check("the high jump clears fifteen metres and costs gas", SoldierJumpClearsTheCity);
        failures += Check("a hook bites the building it was aimed at", SoldierHookBites);
        failures += Check("a taut cable swings the player instead of dropping them", SoldierCableSwings);
        failures += Check("releasing at speed keeps every bit of the momentum", SoldierReleaseKeepsMomentum);
        failures += Check("reeling in costs gas and gains speed", SoldierReelBurnsGas);
        failures += Check("rifle rounds fly exactly where the crosshair points", SoldierRifleFliesTrue);
        failures += Check("a felled building stops being an anchor", SoldierAnchorDiesWithItsTower);
        failures += Check("weak material tears out from under a swing", SoldierWeakAnchorTears);
        failures += Check("a fish opens already swimming, never on the deck", FishStartsSwimming);
        failures += Check("the tail is an impulse, not a throttle", FishBeatIsAnImpulse);
        failures += Check("beats cost breath and only coasting gives it back", FishBreathIsARhythm);
        failures += Check("a rolled body carves tighter than a level one", FishCarveTurnsTighter);
        failures += Check("speed is what holds a fish up", FishLiftHoldsAltitude);
        failures += Check("a strike coils, commits, then leaves you spent", FishStrikeCommits);
        failures += Check("a strike spears one thing and is done", FishStrikeSpearsOnce);
        failures += Check("touching the grid beaches a fish, and beats free it", FishBeachesAndRecovers);
        failures += Check("the bloom warns for ten metres before it bites", FishBloomWarnsFirst);
        failures += Check("the bloom costs shield and thins the water", FishBloomHurts);
        failures += Check("spit flies exactly where the crosshair points", FishSpitFliesTrue);
        failures += Check("a virus opens exposed and flies where it looks", VirusMoteFliesWhereItLooks);
        failures += Check("flying into a hunter wears it as a host", VirusInfectsOnContact);
        failures += Check("a worn host rots out and ejects the mote", VirusHostRots);
        failures += Check("a worn host soaks damage the shield never sees", VirusHostSoaksDamage);
        failures += Check("a naked mote takes hits amplified", VirusMoteIsFragile);
        failures += Check("an overload spends the whole host as a blast", VirusOverloadSpendsTheHost);
        failures += Check("the virus round flies exactly where the crosshair points", VirusRoundFliesTrue);
        failures += Check("an exposed mote withers only after its grace", VirusWithersAfterGrace);
        failures += Check("the crab can be worn, and its lance breaks", VirusWearsTheCrab);
        failures += Check("the maw can be worn, and it hovers", VirusWearsTheMaw);

        Console.WriteLine(failures == 0
            ? "SELFTEST: all checks passed"
            : $"SELFTEST: {failures} check(s) FAILED");
        return failures == 0 ? 0 : 1;
    }

    // --- The skyline -----------------------------------------------------------

    /// <summary>
    /// The field's rejection sampler is asked for hundreds of buildings and quietly
    /// settles for whatever fits, so a spacing change that over-subscribes the torus
    /// doesn't fail — it just returns a near-empty world that still runs and still
    /// renders, and nobody notices until they look at the horizon. This pins a floor
    /// under it. The numbers are a long way below what the field is asked for, so
    /// ordinary retuning doesn't trip it and a collapse does.
    /// </summary>
    private static string? SkylineIsPopulated()
    {
        var field = StructureField.Create();
        int towers = 0, arcs = 0;
        foreach (var s in field)
        {
            if (s.Kind == StructureKind.Tower) towers++; else arcs++;
        }

        if (towers < 40) return $"only {towers} towers were placed";
        if (arcs < 6) return $"only {arcs} arcs were placed";

        // And nothing standing on the ground every screen opens on — the craft's start,
        // the hangar's turntable and the title screen's idling camera all live in here.
        foreach (var s in field)
            if (s.Position.Length() < StructureField.ClearRadius)
                return $"a structure stands {s.Position.Length():0.0} from the origin";

        return null;
    }

    /// <summary>
    /// A tower is a wall, not scenery: driving into one has to stop the craft entering
    /// it, and — just as importantly — has to leave the craft somewhere legal rather
    /// than jammed on the surface it was pushed out of.
    /// </summary>
    private static string? StructuresBlockThePlayer()
    {
        var world = new World.World();
        var tower = FirstTower(world);
        if (tower == null) return "no tower to drive into";

        // Park the craft inside the footprint and let one tick resolve it.
        world.Player.Position = tower.Position;
        StepWithoutInput(world);

        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        tower.Blockers(blockers);
        float wanted = blockers[0].Radius + Entities.PlayerTank.Radius;
        float got = Torus.Distance(world.Player.Position, blockers[0].At);
        if (got < wanted - 0.01f)
            return $"craft sits {got:0.00} into a footprint that reaches {wanted:0.00}";

        // Steady state: another hundred ticks of the same resolution must not creep the
        // craft anywhere, which is what would happen if the push-out overshot and the
        // next tick pushed it back.
        Vector2 settled = world.Player.Position;
        for (int i = 0; i < 100; i++) StepWithoutInput(world);
        if (Torus.Distance(settled, world.Player.Position) > 0.01f)
            return "the craft drifts while resting against a wall";

        return null;
    }

    /// <summary>
    /// A round stops against a wall whoever fired it, and — just as load-bearing — the
    /// wall is still standing afterwards. Bullets are what buildings are <em>for</em>;
    /// only a beam takes one down, which the next check covers.
    /// </summary>
    private static string? RoundsStopAtTheSkyline()
    {
        var world = new World.World { DynamicSpawning = false };
        var tower = FirstTower(world);
        if (tower == null) return "no tower to shoot at";

        // Stand off the tower and aim square at it. Well outside its footprint, so the
        // round has grid to cross and is genuinely stopped rather than born inside a wall.
        Vector2 delta = Torus.Delta(tower.Position, world.Player.Position);
        Vector2 away = Vector2.Normalize(delta) * 14f;
        world.Player.Position = Torus.Wrap(tower.Position + away);
        world.Player.Heading = MathF.Atan2(-away.X, -away.Y);

        world.FirePlayerShot();
        for (int i = 0; i < 120; i++)
        {
            StepWithoutInput(world);
            if (!AnyProjectileActive(world)) break;
        }

        if (AnyProjectileActive(world)) return "the round never stopped";
        if (tower.Falling) return "a plain round brought a tower down";

        // And it stopped *at the wall*, not by flying past and expiring: something has to
        // still be standing between the craft and where it was aiming.
        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        if (tower.Blockers(blockers) == 0) return "the tower stopped being solid";

        return null;
    }

    /// <summary>
    /// A beam is not a round: the SPIDER's charged lance cuts a tower down and runs the
    /// collapse to completion, after which the field has genuinely let it go.
    /// </summary>
    private static string? BeamsCutStructuresDown()
    {
        var loadout = new Loadout { Class = PlayerClass.Spider };
        var world = new World.World(loadout) { DynamicSpawning = false };
        var tower = FirstTower(world);
        if (tower == null) return "no tower to cut";
        if (world.Player.Spider == null) return "the spider chassis has no emitter";

        Vector2 delta = Torus.Delta(tower.Position, world.Player.Position);
        Vector2 away = Vector2.Normalize(delta) * 20f;
        world.Player.Position = Torus.Wrap(tower.Position + away);
        world.Player.Heading = MathF.Atan2(-away.X, -away.Y);

        // Wind the lance to full and loose it down the line of the tower.
        for (int i = 0; i < 200; i++) world.Player.Spider.Hold((float)Config.FixedDt);
        world.FireSpiderLanceForTest();

        if (!tower.Falling) return "the lance left the tower standing";

        // The collapse has to actually finish and clear itself off the field, or a run
        // long enough accumulates wreckage that is neither solid nor ever removed. The
        // count is only checked for having dropped, not for having dropped by one: the
        // lance is ninety units long and deliberately fells everything standing in it,
        // so a shot that opens a road through three towers is correct behaviour.
        int before = world.Structures.Count;
        for (int i = 0; i < 60 * 6; i++) StepWithoutInput(world);
        if (world.Structures.Contains(tower)) return "the wreck never left the field";
        if (world.Structures.Count >= before) return "nothing was cleared off the field";

        return null;
    }

    private static bool AnyProjectileActive(World.World world)
    {
        foreach (var p in world.Projectiles) if (p.Active) return true;
        return false;
    }

    private static Structure? FirstTower(World.World world)
    {
        foreach (var s in world.Structures)
            if (s.Kind == StructureKind.Tower) return s;
        return null;
    }

    // --- The hangar: the loadout budget and the SPIDER chassis ------------------

    private static string? BudgetRefusesOverspend()
    {
        var lo = new Loadout();

        // A track can only climb into points that are actually free, so maxing one out
        // from the even opening spread means selling the other two down first. That the
        // climb stalls until you do is itself the rule under test.
        while (lo.Adjust(Loadout.Stat.Shield, +1)) { }
        if (lo.Shield != 6) return $"shield climbed to {lo.Shield} on one spare point";

        while (lo.Adjust(Loadout.Stat.Speed, -1)) { }
        while (lo.Adjust(Loadout.Stat.Ammo, -1)) { }
        while (lo.Adjust(Loadout.Stat.Shield, +1)) { }
        if (lo.Shield != Loadout.StatMax) return $"shield capped at {lo.Shield}, want 10";

        while (lo.Adjust(Loadout.Stat.Speed, +1)) { }
        if (lo.Speed != 5) return $"second track reached {lo.Speed}, want 5";

        if (lo.Adjust(Loadout.Stat.Ammo, +1))
            return $"third track climbed to {lo.Ammo} with the budget spent";
        if (lo.Ammo != Loadout.StatMin) return $"third track sits at {lo.Ammo}, want 1";
        if (lo.Spent != Loadout.Budget) return $"spent {lo.Spent}, want {Loadout.Budget}";

        // ...and an even spread is legal, with a point left over.
        var even = new Loadout();
        if (even.Spent != 15 || even.Remaining != 1)
            return $"5/5/5 spends {even.Spent} of {Loadout.Budget}";
        return null;
    }

    private static string? DefaultBuildIsTheOldCraft()
    {
        var lo = new Loadout();
        if (MathF.Abs(lo.MaxShield - 100f) > 0.01f) return $"shield {lo.MaxShield}, want 100";
        if (MathF.Abs(lo.SpeedScale - 1f) > 0.001f) return $"speed scale {lo.SpeedScale}, want 1";
        if (lo.MaxAmmo != 50) return $"magazine {lo.MaxAmmo}, want 50";
        return null;
    }

    private static string? LoadoutDrivesPlayerStats()
    {
        var lo = new Loadout();
        while (lo.Adjust(Loadout.Stat.Ammo, +1)) { }        // ammo to 10, the rest starve
        var world = new World.World(lo);

        if (world.Player.MaxAmmo != lo.MaxAmmo)
            return $"magazine {world.Player.MaxAmmo}, loadout says {lo.MaxAmmo}";
        if (MathF.Abs(world.Player.MaxShield - lo.MaxShield) > 0.01f)
            return $"shield {world.Player.MaxShield}, loadout says {lo.MaxShield}";
        if (MathF.Abs(world.Player.TopSpeed - PlayerTankMaxSpeed * lo.SpeedScale) > 0.01f)
            return $"top speed {world.Player.TopSpeed} doesn't match the speed track";
        if (world.Player.Ammo > world.Player.MaxAmmo)
            return "opened with more rounds than the magazine holds";
        return null;
    }

    private static float PlayerTankMaxSpeed => Entities.PlayerTank.MaxSpeed;

    private static string? SpiderLanceKills()
    {
        var lo = new Loadout { Class = PlayerClass.Spider };
        var world = new World.World(lo);
        if (world.Player.Spider is not { } spider) return "spider chassis has no emitter";

        world.Enemies.Clear();
        // Two hunters strung out along the craft's forward axis (+Z at heading 0), so a
        // single shaft has to rake through both — the lance pierces, it doesn't stop at
        // the first thing it touches.
        var near = new Entities.EnemyTank(new Vector2(0f, 20f), elite: false);
        var far = new Entities.EnemyTank(new Vector2(0f, 40f), elite: false);
        world.Enemies.Add(near);
        world.Enemies.Add(far);

        int ammo0 = world.Player.Ammo;

        // Wind the meter to full, then let go. Both calls go through the world's own
        // trigger handler, so this exercises the same path a held right-click does.
        for (int i = 0; i < 200 && spider.Charge < Entities.SpiderWeapon.MaxCharge; i++)
            spider.Hold((float)Config.FixedDt);
        world.FireSpiderLanceForTest();

        if (near.Alive || far.Alive)
            return $"lance left hunters standing (near {near.Alive}, far {far.Alive})";
        if (world.Player.Ammo >= ammo0)
            return "a full-charge lance cost no rounds";
        if (spider.Charge != 0f) return $"meter kept {spider.Charge} after firing";
        return null;
    }

    private static string? SpiderChargeRootsTheCraft()
    {
        var lo = new Loadout { Class = PlayerClass.Spider };
        var world = new World.World(lo);
        if (world.Player.Spider is not { } spider) return "spider chassis has no emitter";

        // Root the craft and shove it: a rooted craft ignores drive input, so with no
        // momentum carried in it must not travel. (StepWithoutInput doesn't press
        // anything anyway — what's under test is that Rooted survives a step and that
        // the charge climbs while it's set.)
        world.Player.Rooted = true;
        Vector2 start = world.Player.Position;
        for (int i = 0; i < 30; i++)
        {
            spider.Hold((float)Config.FixedDt);
            StepWithoutInput(world);
        }

        if (Vector2.Distance(world.Player.Position, start) > 0.01f)
            return "a rooted craft drifted while charging";
        if (spider.Charge <= 0f) return "the meter never filled";
        if (spider.Charge > Entities.SpiderWeapon.MaxCharge)
            return $"the meter overran its ceiling ({spider.Charge})";
        return null;
    }

    // --- The machines: TANK and SPIDER aim ---------------------------------------

    /// <summary>
    /// The mouse turns the whole craft — the thing the player asked for after a turret
    /// that swung the view one way while A/D swung the body another read as disorienting.
    /// A yaw of half a radian has to move the shared heading by exactly that, on both
    /// machines, with nothing left held off to the side.
    /// </summary>
    private static string? MouseTurnsTheWholeCraft()
    {
        foreach (var kind in new[] { PlayerClass.Tank, PlayerClass.Spider })
        {
            var p = new Entities.PlayerTank(Vector2.Zero, heading: 0.7f,
                loadout: new Loadout { Class = kind });
            if (!p.IsMachine) return $"{kind} isn't treated as a machine";

            float before = p.Heading;
            p.Look(0.5f, 0f);
            if (MathF.Abs(p.Heading - (before + 0.5f)) > 1e-4f)
                return $"{kind}: a 0.5 rad mouse yaw moved the heading {p.Heading - before:0.000}";
            // And the craft's forward — what it drives and fires along — comes round with it.
            var want = new Vector2(MathF.Sin(before + 0.5f), MathF.Cos(before + 0.5f));
            if (Vector2.Distance(p.Forward, want) > 1e-4f)
                return $"{kind}: forward didn't follow the turned heading";
        }
        return null;
    }

    /// <summary>
    /// The tank's gun is stopped short of the sky — fifteen degrees either way. This is
    /// the number that keeps the Maw-Core honest (see <see cref="TankGunStaysUnderTheMaw"/>),
    /// so the elevation clamp is checked here in isolation before the shot that depends on it.
    /// </summary>
    private static string? TankGunElevationIsShallow()
    {
        var p = new Entities.PlayerTank(Vector2.Zero);

        for (int i = 0; i < 200; i++) p.Look(0f, 0.1f);    // crane hard up
        if (p.Pitch > Entities.PlayerTank.TurretElevation + 1e-4f)
            return $"the gun elevated to {p.Pitch:0.00}, past its {Entities.PlayerTank.TurretElevation:0.00} stop";
        if (p.Pitch < Entities.PlayerTank.TurretElevation - 1e-4f)
            return $"the gun stalled at {p.Pitch:0.00} short of its stop";
        if (p.GunElevation > Entities.PlayerTank.TurretElevation + 1e-4f)
            return "the shot's elevation runs past the gun's stop";

        for (int i = 0; i < 200; i++) p.Look(0f, -0.1f);   // and hard down
        if (p.Pitch < -Entities.PlayerTank.TurretElevation - 1e-4f)
            return $"the gun depressed to {p.Pitch:0.00}, past its stop";
        return null;
    }

    /// <summary>
    /// The SPIDER's ring, unlike the tank's gun, cranes the full way up — nearly to
    /// vertical, the Crab-Core's own reach. It pays for that with an exposed core, not by
    /// being stopped short.
    /// </summary>
    private static string? SpiderGunCranesAllTheWay()
    {
        var p = new Entities.PlayerTank(Vector2.Zero,
            loadout: new Loadout { Class = PlayerClass.Spider });

        for (int i = 0; i < 400; i++) p.Look(0f, 0.1f);
        if (p.Pitch < Entities.PlayerTank.MaxPitch - 0.05f)
            return $"the spider's ring only craned to {p.Pitch:0.00}, not near vertical";
        if (p.GunElevation < Entities.PlayerTank.MaxPitch - 0.05f)
            return "the spider's shot elevation is capped below its look";
        return null;
    }

    /// <summary>
    /// The cannon leaves along the craft's heading, which the mouse aims: turn the craft
    /// and the bolt turns with it. If the shot went along a stale facing, the free look
    /// would be a lie the first round exposes.
    /// </summary>
    private static string? CannonFollowsTheAim()
    {
        var p = new Entities.PlayerTank(Vector2.Zero, heading: 0.3f);
        p.Look(0.8f, 0f);   // swing the craft round with the mouse

        if (!p.TryFire(out _, out Vector2 dir, out _))
            return "the cannon refused to fire";

        if (Vector2.Distance(Vector2.Normalize(dir), p.Forward) > 1e-3f)
            return "the bolt left along something other than the aimed heading";
        return null;
    }

    /// <summary>
    /// The load-bearing limit. The Maw-Core hangs its crystal at the top of a jump so the
    /// class has to leave the ground to hurt it; the tank's new elevation must not quietly
    /// hand it an anti-air gun that snipes the gem from the deck. So the gun's own stop is
    /// checked to be genuinely shallow, and — the real test — a bolt fired up it from the
    /// grid is walked its whole life and must never once pass through the crystal's strike
    /// band while it is anywhere near the maw's column.
    /// </summary>
    private static string? TankGunStaysUnderTheMaw()
    {
        var p = new Entities.PlayerTank(Vector2.Zero);
        // Crane the gun as far up as it goes, harder than any hand could.
        for (int i = 0; i < 200; i++) p.Look(0f, 0.1f);
        if (p.GunElevation > Entities.PlayerTank.TurretElevation + 1e-4f)
            return $"the gun elevated to {p.GunElevation:0.00}, past its {Entities.PlayerTank.TurretElevation:0.00} stop";

        // Stand the maw out along the shot and fire up the raised gun. Walk the round its
        // whole flight; if it ever satisfies HitsCrystal, a grounded tank could kill it.
        float band = Entities.MawRig.CrystalWorldY;
        for (float range = 8f; range <= 60f; range += 2f)
        {
            var maw = new Entities.MawCore(new Vector2(0f, range));
            var round = new Entities.Projectile();
            // Straight down +Z, up the gun's own elevation, from the barrel.
            round.Fire(Vector2.Zero, new Vector2(0f, 1f), fromPlayer: true,
                launchHeight: Entities.Projectile.BoltHeight, pitch: p.GunElevation);
            for (int i = 0; i < 240 && round.Active; i++)
            {
                round.Update((float)Config.FixedDt);
                if (maw.HitsCrystal(round.Position, round.Height))
                    return $"a grounded tank bolt reached the crystal at range {range:0} "
                         + $"(height {round.Height:0.0}, band {band:0.0})";
            }
        }
        return null;
    }

    private static int Check(string name, Func<string?> test)
    {
        string? err = test();
        if (err == null)
        {
            Console.WriteLine($"  PASS  {name}");
            return 0;
        }
        Console.WriteLine($"  FAIL  {name}: {err}");
        return 1;
    }

    private static string? WorldIsAFiniteTorus()
    {
        // Drive off one edge and you come back on the opposite one — the map is not
        // infinite. A point a full world-width along any axis is literally the same
        // point, so its wrapped distance is zero.
        if (Torus.Distance(new Vector2(12f, -30f), new Vector2(12f + Torus.Size, -30f)) > 0.001f)
            return "a full world-width apart isn't the same place";

        // Wrapping always folds a coordinate back into the [-Half, Half) play window,
        // so positions can never run off to infinity.
        float w = Torus.WrapCoord(Torus.Half + 5f);
        if (w < -Torus.Half || w >= Torus.Half)
            return $"a wrapped coordinate ({w:F1}) fell outside the world window";

        // The opposite edges are stitched together: just over the top edge is a couple
        // of units below the bottom edge, not a whole arena away.
        float seam = Torus.Distance(new Vector2(0f, Torus.Half - 1f),
                                    new Vector2(0f, -Torus.Half + 1f));
        if (seam > 3f) return $"the seam isn't stitched — edge to edge measured {seam:F1}";

        // And a craft driven straight past the world's edge reappears wrapped, still
        // in the window, rather than sailing off forever.
        var player = new Entities.PlayerTank(new Vector2(Torus.Half - 2f, 0f));
        player.Position = Torus.Wrap(player.Position + new Vector2(6f, 0f));
        if (player.Position.X > 0f)
            return "crossing the +X edge didn't wrap the craft to the far side";
        return null;
    }

    private static string? PlayerKillsEnemy()
    {
        var world = new World.World { DynamicSpawning = false };
        // Face the enemy and hold fire; step a few seconds of sim. Spawning is off so
        // the seeded hunter is the only one — killing it clears the field.
        AimPlayerAtFirstEnemy(world);

        for (int i = 0; i < 60 * 8 && world.Enemies.Count > 0; i++)
        {
            AimPlayerAtFirstEnemy(world);
            world.FirePlayerShot();
            StepWithoutInput(world);
        }
        return world.Enemies.Count == 0 ? null : "enemy still alive after 8s of fire";
    }

    private static string? EnemyDamagesPlayer()
    {
        var world = new World.World { DynamicSpawning = false };
        float startShield = world.Player.Shield;

        // Don't fire; just let the enemy close and shoot. Player stays grounded
        // (no jump) so hits land.
        for (int i = 0; i < 60 * 15; i++)
        {
            AimPlayerAtFirstEnemy(world);
            StepWithoutInput(world);
            if (world.Player.Shield < startShield) break;
        }
        return world.Player.Shield < startShield
            ? null
            : "player took no damage in 15s under fire";
    }

    /// <summary>
    /// A leaping craft used to be untouchable — enemy fire passed clean underneath the
    /// moment a wheel left the grid. Now the hunters elevate onto the craft's height, so a
    /// player held up in the air still takes fire. Same setup as
    /// <see cref="EnemyDamagesPlayer"/>, but the craft is pinned six metres up: if the old
    /// blanket immunity were still in force nothing here would ever land.
    /// </summary>
    private static string? EnemyReachesPlayerInTheAir()
    {
        var world = new World.World { DynamicSpawning = false };
        float startShield = world.Player.Shield;
        const float altitude = 6f;

        for (int i = 0; i < 60 * 15; i++)
        {
            // Hold it aloft before the step (so the hunters aim up at it) and again after
            // (so the jump physics inside the step don't quietly bring it back down).
            world.Player.Height = altitude;
            AimPlayerAtFirstEnemy(world);
            StepWithoutInput(world);
            world.Player.Height = altitude;
            if (world.Player.Shield < startShield) break;
        }
        return world.Player.Shield < startShield
            ? null
            : "an airborne player took no damage in 15s under fire";
    }

    private static string? AmmoDepletes()
    {
        var world = new World.World();
        int startAmmo = world.Player.Ammo;
        for (int i = 0; i < 60 * 30; i++)
        {
            world.FirePlayerShot();
            StepWithoutInput(world);
            if (world.Player.Ammo == 0) break;
        }
        return world.Player.Ammo == 0 ? null : $"ammo did not deplete (left {world.Player.Ammo})";
    }

    private static string? BatteryStowsThenCharges()
    {
        var world = new World.World();
        // Spend some shield and hyper so a later charge has room to land.
        world.Player.TakeDamage(50f);
        world.Player.TryHyperspace(); // drains most of the Hyper reserve
        float shield0 = world.Player.Shield;
        float hyper0 = world.Player.Hyper;

        // Drop a battery right on the craft and step once so it's collected.
        world.Pickups.Add(new Entities.Pickup(world.Player.Position, Entities.PickupKind.Battery));
        StepWithoutInput(world);

        // The new contract: salvage no longer charges on contact — it's stowed. Shield
        // has no passive regen, so it's the clean witness (Hyper trickles back on its
        // own every tick, which is not the battery charging).
        if (world.Player.Shield != shield0)
            return "battery charged shield on contact (should stow, not auto-charge)";
        if (CountItems(world.Inventory, ItemKind.Battery) < 1)
            return "battery was not stowed in the inventory";

        // Spending it (as the panel's right-click does) recharges shield + hyper.
        world.Player.RefillShield(World.World.BatteryChargeFraction);
        world.Player.RefillHyper(World.World.BatteryChargeFraction);
        if (world.Player.Shield <= shield0) return "spending a battery did not recharge shield";
        if (world.Player.Hyper <= hyper0) return "spending a battery did not recharge hyper";
        return null;
    }

    private static string? AmmoStowsThenLoads()
    {
        var world = new World.World();
        // Burn some ammo first so a later reload is observable.
        for (int i = 0; i < 20; i++) { world.FirePlayerShot(); StepWithoutInput(world); }
        int ammo0 = world.Player.Ammo;

        world.Pickups.Add(new Entities.Pickup(world.Player.Position, Entities.PickupKind.Ammo));
        StepWithoutInput(world);

        // Stowed, not auto-loaded, and carrying a random 5–20 rounds.
        if (world.Player.Ammo != ammo0) return "ammo loaded on contact (should stow, not auto-load)";
        int bullets = CountItems(world.Inventory, ItemKind.Bullet);
        if (bullets < 5 || bullets > 20) return $"bullet salvage stowed {bullets} rounds (expected 5-20)";

        // Spending the stack loads it into the magazine.
        world.Player.Ammo = Math.Min(world.Player.MaxAmmo, world.Player.Ammo + bullets);
        return world.Player.Ammo > ammo0 ? null : "spending bullets did not restock ammo";
    }

    private static string? FragmentsCraftCrabCore()
    {
        var inv = new Inventory();
        // Three fragments, one to a triangle corner, satisfy the recipe.
        for (int i = 0; i < Inventory.CraftCount; i++)
            inv.Craft[i] = new ItemStack(ItemKind.CrabFragment, 1);

        if (!inv.CanCraft()) return "three fragments did not satisfy the recipe";
        ItemStack core = inv.TakeCraftOutput();
        if (core.IsEmpty || core.Kind != ItemKind.CrabCore) return "crafting did not yield a CRAB CORE";
        // The fragments are spent — the corners are now empty.
        for (int i = 0; i < Inventory.CraftCount; i++)
            if (!inv.Craft[i].IsEmpty) return "crafting did not consume the fragments";
        return null;
    }

    private static string? CrabCoreBlastKills()
    {
        var world = new World.World();
        // Park an enemy just in front of the craft, well inside a lance's reach.
        world.Enemies.Clear();
        var enemy = new Entities.EnemyTank(world.Player.Position + world.Player.Forward * 8f, elite: false);
        world.Enemies.Add(enemy);

        // Equip a crafted core and throw it straight ahead; it lobs a short way, lands
        // near the enemy and erupts into the lance ring.
        world.Inventory.Weapons[0] = new ItemStack(ItemKind.CrabCore, 1);
        world.UseWeaponSlot(0);

        // Step long enough for the bomb to detonate and the star to burn through.
        for (int i = 0; i < 180 && enemy.Alive; i++) StepWithoutInput(world);

        return enemy.Alive ? "the blast did not destroy the enemy in its path" : null;
    }

    private static string? CrabCoreBlastKillsBoss()
    {
        var world = new World.World();
        world.DynamicSpawning = false;
        world.Enemies.Clear();
        world.SpawnCrabAhead();   // a dormant Crab-Core out along the player's heading

        // Throw a core; it lobs out toward the boss and detonates near it.
        world.Inventory.Weapons[0] = new ItemStack(ItemKind.CrabCore, 1);
        world.UseWeaponSlot(0);

        for (int i = 0; i < 240 && world.Boss is { Alive: true }; i++) StepWithoutInput(world);

        return world.Boss is null or { Alive: false }
            ? null : "the blast did not destroy the Crab-Core";
    }

    private static string? CrabCoreBlastKillsMaw()
    {
        var world = new World.World();
        world.DynamicSpawning = false;
        world.Enemies.Clear();
        world.SpawnMawAhead();    // a hanging Maw-Core out along the player's heading

        world.Inventory.Weapons[0] = new ItemStack(ItemKind.CrabCore, 1);
        world.UseWeaponSlot(0);

        for (int i = 0; i < 240 && world.Maw is { Alive: true }; i++) StepWithoutInput(world);

        return world.Maw is null or { Alive: false }
            ? null : "the blast did not destroy the Maw-Core";
    }

    /// <summary>Total count of a kind across the inventory grid.</summary>
    private static int CountItems(Inventory inv, ItemKind kind)
    {
        int n = 0;
        foreach (var s in inv.Slots)
            if (!s.IsEmpty && s.Kind == kind) n += s.Count;
        return n;
    }

    private static string? GroundedShotMissesCore()
    {
        // A bolt at barrel height, dead-centre on the core's planar spot, must sail
        // underneath: only a leaping shot rides high enough to reach the gem.
        var boss = new Entities.CrabCore(Vector2.Zero);
        return boss.HitsCore(Vector2.Zero, Entities.Projectile.BoltHeight)
            ? "a grounded-height shot connected with the core"
            : null;
    }

    private static string? AirShotKillsCore()
    {
        var boss = new Entities.CrabCore(Vector2.Zero);
        if (!boss.HitsCore(Vector2.Zero, Entities.CrabCore.CoreHitHeight))
            return "a shot at core height missed the core";

        bool killedReported = false;
        for (int i = 0; i < 100 && boss.Alive; i++)
            killedReported = boss.DamageCore(1f);

        if (boss.Alive) return "core never depleted under repeated air hits";
        if (!killedReported) return "the killing hit didn't report the kill";

        // The death glitch should ramp as the rig tears apart, then finish (Dead).
        for (int i = 0; i < 60 * 3 && !boss.Dead; i++)
            boss.Update((float)Config.FixedDt, Vector2.Zero);
        if (!boss.Dead) return "death glitch never finished";
        return null;
    }

    private static string? AirShotExpiresForBlast()
    {
        // A shot launched well above barrel height is an air shot: it must glide out
        // and expire on its own (the flag the world reads to stage the horizon blast).
        var p = new Entities.Projectile();
        p.Fire(Vector2.Zero, new Vector2(0f, 1f), fromPlayer: true, launchHeight: 6.5f);
        if (!p.IsAirShot) return "a high launch wasn't treated as an air shot";

        bool sawExpire = false;
        for (int i = 0; i < 60 * 6 && p.Active; i++)
        {
            p.Update((float)Config.FixedDt);
            if (p.JustExpired) sawExpire = true;
        }
        return sawExpire ? null : "air shot never expired to stage its blast";
    }

    private static string? DebugSpawnAddsEnemy()
    {
        // The in-game 'L' hatch: each call must put one more threat on the field —
        // a hunter or the Crab-Core. Spawning off so the director doesn't muddy the
        // count. Repeat enough to exercise every branch (both tanks and the boss).
        var world = new World.World { DynamicSpawning = false };
        for (int i = 0; i < 40; i++)
        {
            int tanks = world.Enemies.Count;
            bool hadBoss = world.Boss != null;
            world.SpawnRandomEnemy();

            bool grew = world.Enemies.Count > tanks || (!hadBoss && world.Boss != null);
            // At the hunter cap a spawn can swap rather than grow the count; only
            // count that as a miss if the boss slot didn't fill either.
            if (!grew && world.Enemies.Count < 1 && world.Boss == null)
                return "a debug spawn added no enemy";
        }
        return world.Enemies.Count > 0 ? null : "no hunters on the field after 40 spawns";
    }

    private static string? BossSeizesPlayer()
    {
        var (boss, player) = CorneredByBoss();
        if (boss == null || player == null) return "the boss never entered pursuit";

        if (!Entities.CrabSeizure.CanSeize(boss, player))
            return "a boss standing on the player wouldn't seize";

        // Run the whole cinematic and count the moments that cost the player.
        var seizure = new Entities.CrabSeizure(boss, player);
        int struck = 0, landed = 0;
        for (int i = 0; i < 60 * 20 && seizure.Active; i++)
        {
            boss.Update((float)Config.FixedDt, player.Position);
            switch (seizure.Update((float)Config.FixedDt))
            {
                case Entities.CrabSeizure.Event.Struck: struck++; break;
                case Entities.CrabSeizure.Event.Landed: landed++; break;
            }
        }

        if (seizure.Active) return "the seizure never finished";
        // Each damage moment has to fire exactly once: a strike that repeated every
        // tick of the swing would delete the player outright.
        if (struck != 1) return $"the claw's blow fired {struck} times, expected 1";
        if (landed != 1) return $"the landing fired {landed} times, expected 1";

        // The player must be handed back: on the grid, driving again, and thrown
        // clear of the boss rather than dropped back inside its reach.
        if (player.Captured) return "the player was never released from the grip";
        if (player.Height > 0.001f) return "the player never came back down";
        float thrown = Vector2.Distance(player.Position, boss.Position);
        if (thrown <= Entities.CrabSeizure.GrabRadius)
            return $"the throw only moved the player {thrown:F1} units — still in reach";
        if (boss.Seizing) return "the boss is still posed as holding someone";
        return null;
    }

    /// <summary>
    /// The two hands have opposite jobs, and both are easy to get silently wrong.
    ///
    /// The striking one has to converge on the craft or the blow lands in empty air.
    /// The holding one has to stay off the line of sight: it is drawn from a walking
    /// leg, and the pose that raises it into an arm lifts the whole limb — knee
    /// included — so on the centre line it becomes a column through the middle of the
    /// shot and the player spends the scream looking at a leg instead of the crystal.
    ///
    /// Neither is visible from the numbers alone, because a claw's world position and
    /// the point the cinematic parks the craft at are reached down entirely separate
    /// paths: a pose yaw through the renderer's rotation convention, versus a forward
    /// offset through the seizure's.
    /// </summary>
    private static string? SeizureHandsReachThePlayer()
    {
        var (boss, player) = CorneredByBoss();
        if (boss == null || player == null) return "the boss never entered pursuit";

        var legs = Entities.CrabRig.Legs;
        var grab = legs[Entities.CrabRig.GrabLeg];
        var strike = legs[Entities.CrabRig.StrikeLeg];

        var seizure = new Entities.CrabSeizure(boss, player);
        bool sawHold = false;
        for (int i = 0; i < 60 * 20 && seizure.Active; i++)
        {
            boss.Update((float)Config.FixedDt, player.Position);
            seizure.Update((float)Config.FixedDt);

            // Checked once the hold has settled: the drag in is an interpolation from
            // wherever the craft was standing, so only the stages after it are claimed
            // to have the player actually in the grip.
            if (seizure.Phase is not (Entities.CrabSeizure.Stage.Scream
                                   or Entities.CrabSeizure.Stage.Strike)) continue;
            sawHold = true;

            // The striking hand converges on the craft: it has to actually connect.
            // Generous, because the grip trembles and the blow knocks the craft off the
            // claw on purpose — this only catches a hand in the wrong place entirely.
            Vector2 hit = Entities.CrabRig.TipWorldXZ(
                strike, Entities.CrabRig.CentreGripYaw(strike), boss.Position, boss.Heading);
            float miss = Vector2.Distance(hit, player.Position);
            if (miss > 5f)
                return $"the striking claw is {miss:F1} from the player it should hit";

            // The holding hand must NOT. It is a limb the size of a building and the
            // pose that raises it carries its knee higher still, so anywhere near the
            // line of sight it becomes a column straight through the middle of the shot
            // with the core behind it. Held off to the side it frames the view instead.
            Vector2 held = Entities.CrabRig.TipWorldXZ(
                grab, Entities.CrabRig.HoldingGripYaw(grab), boss.Position, boss.Heading);
            Vector2 toClaw = held - player.Position;
            Vector2 view = boss.Position - player.Position;
            if (toClaw.LengthSquared() > 0.01f && view.LengthSquared() > 0.01f)
            {
                float off = MathF.Acos(Math.Clamp(Vector2.Dot(
                    Vector2.Normalize(toClaw), Vector2.Normalize(view)), -1f, 1f));
                if (off < 0.6f)
                    return $"the holding claw is only {off:F2} rad off the view axis "
                         + "— it will stand between the player and the core";
            }

            float lift = MathF.Abs(player.Height - Entities.CrabRig.HoldWorldY);
            if (lift > 3f)
                return $"the craft is carried {lift:F1} off the height it is held at";

            // The point of the whole arrangement: the holding claw grips from below the
            // eye. Level with it, the limb lies along the line of sight and the player
            // spends the scream looking at a leg instead of at the crystal.
            float eye = player.Height + Config.CameraHeight;
            if (Entities.CrabRig.GripWorldY >= eye - 1f)
                return $"the holding claw at {Entities.CrabRig.GripWorldY:F1} is not clear "
                     + $"below the eye at {eye:F1} — it will block the core";
        }
        return sawHold ? null : "the hold never played";
    }

    private static string? SeizureFramesTheCore()
    {
        // The whole point of being held is watching the core. The craft has to be
        // lifted so the eye sits inside the pyramid's vertical span — too low and the
        // player spends the scream staring at the chassis with the gem off-screen.
        var (boss, player) = CorneredByBoss();
        if (boss == null || player == null) return "the boss never entered pursuit";

        float gemBase = Entities.CrabRig.CoreWorldY;
        float gemApex = gemBase + Entities.CrabRig.CoreMeshHeight;

        var seizure = new Entities.CrabSeizure(boss, player);
        bool sawScream = false;
        for (int i = 0; i < 60 * 20 && seizure.Active; i++)
        {
            boss.Update((float)Config.FixedDt, player.Position);
            seizure.Update((float)Config.FixedDt);
            if (seizure.Phase != Entities.CrabSeizure.Stage.Scream) continue;

            sawScream = true;
            float eye = player.Height + Config.CameraHeight;
            if (eye < gemBase || eye > gemApex)
                return $"eye at {eye:F1} is outside the core's {gemBase:F1}..{gemApex:F1} band";

            // And the camera has to actually be pointed at the gem. Checking the sign
            // of the seizure's own pitch is not the same thing and quietly passes while
            // the crystal sits off the bottom of the screen: the eye carries a standing
            // upward tilt of its own, so what matters is where the two together land at
            // the boss's distance, not whether the cinematic's share of it is positive.
            float slope = Config.CameraLookLift + seizure.Pitch;
            float aim = eye + slope * Vector2.Distance(player.Position, boss.Position);
            if (aim < gemBase || aim > gemApex)
                return $"the view is aimed at {aim:F1}, outside the core's "
                     + $"{gemBase:F1}..{gemApex:F1} band";
        }
        return sawScream ? null : "the scream stage never played";
    }

    /// <summary>
    /// The lance's one promise: once it fires, it fires where the player <em>was</em>.
    ///
    /// This is the property the whole attack is balanced on — the charge is a window
    /// to leave the line, and that window is only real if walking out of it works. So
    /// this drives a boss all the way to the shot and then teleports the player a long
    /// way sideways mid-burn, and asserts the beam neither turns to follow nor lands a
    /// hit. If a future change ever makes the beam track, this fails rather than the
    /// attack quietly becoming unavoidable.
    /// </summary>
    private static string? BeamLocksItsDirection()
    {
        var (boss, player) = CorneredByBoss();
        if (boss == null || player == null) return "boss never reached pursuit";

        // Stand it off at lance range — inside the grab radius it goes for the claw.
        var aimedAt = new Vector2(0f, 34f);
        boss.Position = aimedAt;
        float dt = (float)Config.FixedDt;

        // The cooldown runs down over several seconds of pursuit, during which the
        // boss is walking in. Pin it at range each tick — in play that gap is held by
        // the player outrunning it, which is the situation the attack exists for; here
        // it just keeps the wait from ending with the crab in the player's lap.
        for (int i = 0; i < 60 * 30 && !boss.BeamActive; i++)
        {
            if (boss.Phase == Entities.CrabCore.State.Pursuit) boss.Position = aimedAt;
            boss.Update(dt, player.Position);
        }

        if (!boss.BeamActive) return "boss never fired its beam in 30s of pursuit";

        Vector3 firedAlong = boss.BeamDirection;

        // First: it is aimed at the player it locked. This is what pins the bearing
        // and elevation conventions together — get either of them mirrored and the
        // beam still fires, still holds its line, and still misses every time.
        if (!InBeam(boss, player.Position, firedAlong))
            return "the beam did not point at the player it locked onto";

        // Now break for cover: straight out to one side, well clear of the shaft.
        player.Position = new Vector2(60f, 0f);

        for (int i = 0; i < 60 * 4 && boss.BeamActive; i++)
        {
            boss.Update(dt, player.Position);
            if (!boss.BeamActive) break;

            if (Vector3.Distance(boss.BeamDirection, firedAlong) > 0.001f)
                return "the beam turned to follow the player after firing";

            // ...and the player who ran is genuinely out of it.
            if (InBeam(boss, player.Position, firedAlong))
                return "a player who ran clear was still inside the beam";
        }

        return null;
    }

    /// <summary>Whether a craft standing at <paramref name="at"/> is inside the
    /// boss's beam — the same point-to-ray test the world damages on.</summary>
    private static bool InBeam(Entities.CrabCore boss, Vector2 at, Vector3 dir)
    {
        var p = new Vector3(at.X, 1f, at.Y);
        Vector3 from = boss.BeamOrigin;
        float along = Math.Clamp(Vector3.Dot(p - from, dir), 0f, Entities.CrabCore.BeamLength);
        return Vector3.Distance(p, from + dir * along) <= Entities.CrabCore.BeamRadius;
    }

    /// <summary>
    /// Builds a boss and a player standing in each other's laps and runs the Stalker
    /// Protocol forward until it commits to the hunt — the state a seizure needs.
    /// Returns nulls if it never got there.
    /// </summary>
    private static (Entities.CrabCore?, Entities.PlayerTank?) CorneredByBoss()
    {
        var player = new Entities.PlayerTank(Vector2.Zero);
        var boss = new Entities.CrabCore(new Vector2(0f, 9f));

        // Idle -> threat display -> clamping -> pursuit takes a few fixed seconds.
        for (int i = 0; i < 60 * 10 && boss.Phase != Entities.CrabCore.State.Pursuit; i++)
            boss.Update((float)Config.FixedDt, player.Position);

        if (boss.Phase != Entities.CrabCore.State.Pursuit) return (null, null);

        // The display slides it sideways, so walk it back into arm's reach.
        boss.Position = player.Position + new Vector2(0f, 9f);
        boss.SnapToFace(player.Position);
        return (boss, player);
    }

    // --- The Maw-Core: the hanging mouth --------------------------------------

    private static string? MawHangsAtJumpApex()
    {
        // The load-bearing claim of the whole enemy: its crystal sits where a bolt
        // fired at the peak of a leap is travelling. Checked against the jump's own
        // physics rather than against a copy of the number, so retuning the jump
        // without moving the monster fails here rather than silently in play.
        float apexShot = Entities.PlayerTank.JumpApex + Entities.Projectile.BoltHeight;
        var maw = new Entities.MawCore(Vector2.Zero);

        if (!maw.HitsCrystal(Vector2.Zero, apexShot))
            return $"a shot at apex height {apexShot:F2} missed the crystal";

        // The band must also be tight enough that it is genuinely a jump check: a shot
        // from halfway up the arc has to miss, or "shoot it while airborne" collapses
        // into "shoot it while vaguely off the ground".
        float halfway = Entities.PlayerTank.JumpApex * 0.5f + Entities.Projectile.BoltHeight;
        return maw.HitsCrystal(Vector2.Zero, halfway)
            ? $"a shot from halfway up the jump ({halfway:F2}) still reached the crystal"
            : null;
    }

    private static string? MawNeedsAnAirShot()
    {
        var maw = new Entities.MawCore(Vector2.Zero);

        // Grounded: must sail underneath, however well aimed.
        if (maw.HitsCrystal(Vector2.Zero, Entities.Projectile.BoltHeight))
            return "a grounded-height shot connected with the crystal";

        // ...and the crystal must actually be destructible from the air, glitch and all.
        if (!maw.HitsCrystal(Vector2.Zero, Entities.MawRig.CrystalWorldY))
            return "a shot at the strike band missed the crystal";

        bool killedReported = false;
        for (int i = 0; i < 100 && maw.Alive; i++)
            killedReported = maw.DamageCrystal(1f);

        if (maw.Alive) return "crystal never depleted under repeated air hits";
        if (!killedReported) return "the killing hit didn't report the kill";

        for (int i = 0; i < 60 * 3 && !maw.Dead; i++)
            maw.Update((float)Config.FixedDt, Vector2.Zero, 0f);
        return maw.Dead ? null : "death glitch never finished";
    }

    private static string? MawSwallowsStillPlayer()
    {
        // Standing still under it has to end in being caught — that is the deal, and
        // UnderTheMaw only returns a pair once JustCaught has actually fired.
        var (maw, player) = UnderTheMaw();
        if (maw == null || player == null) return "the maw never dropped on a still player";
        if (maw.Phase != Entities.MawCore.State.Digest)
            return $"the maw caught the player but sat in {maw.Phase}";

        // ...and the other half of the deal: a player who keeps walking is never
        // caught, so the lunge has to miss someone who left the column.
        var mover = new Entities.PlayerTank(Vector2.Zero);
        var missing = new Entities.MawCore(Vector2.Zero);
        for (int i = 0; i < 60 * 10; i++)
        {
            // Walking flat out, straight line — the simplest possible evasion.
            mover.Position += new Vector2(0f, Entities.PlayerTank.MaxSpeed * (float)Config.FixedDt);
            missing.Update((float)Config.FixedDt, mover.Position, mover.Height);
            if (missing.JustCaught) return "the maw caught a player who never stopped moving";
        }
        return null;
    }

    private static string? MawReleasesOnThreeShots()
    {
        var (maw, player) = UnderTheMaw();
        if (maw == null || player == null) return "the maw never caught the player";

        var digestion = new Entities.MawDigestion(maw, player);

        // Step into the hold, then put the escape shots in. Fewer than three must not
        // free anybody — that is the whole tension of the beat.
        for (int i = 0; i < 60 * 2 && digestion.Phase != Entities.MawDigestion.Stage.Digest; i++)
            digestion.Update((float)Config.FixedDt);
        if (digestion.Phase != Entities.MawDigestion.Stage.Digest)
            return "the swallow never reached the digest stage";

        if (digestion.RegisterShot()) return "one shot freed the player";
        if (digestion.RegisterShot()) return "two shots freed the player";
        if (!digestion.RegisterShot()) return "three shots did not free the player";
        if (digestion.Hits != Entities.MawDigestion.EscapeHits)
            return $"escape counted {digestion.Hits} hits, expected {Entities.MawDigestion.EscapeHits}";

        // The whole cinematic must then play out and hand control back — a trap the
        // player can never drive out of is a hang, not a set piece.
        bool landed = false;
        for (int i = 0; i < 60 * 12 && digestion.Active; i++)
            if (digestion.Update((float)Config.FixedDt) == Entities.MawDigestion.Event.Landed)
                landed = true;

        if (digestion.Active) return "the digestion never finished";
        if (!landed) return "the player was never put back on the grid";
        if (player.Captured) return "control was never handed back";
        return null;
    }

    private static string? MawDigestionBites()
    {
        var (maw, player) = UnderTheMaw();
        if (maw == null || player == null) return "the maw never caught the player";

        var digestion = new Entities.MawDigestion(maw, player);

        // Held and never shooting back: the bites have to keep coming. Run long enough
        // to see several, so a single one firing on entry wouldn't pass this.
        int bites = 0;
        for (int i = 0; i < 60 * 6 && digestion.Held; i++)
            if (digestion.Update((float)Config.FixedDt) == Entities.MawDigestion.Event.Bitten)
                bites++;

        if (bites < 3) return $"only {bites} bite(s) in six seconds of being digested";

        // And each one must be worth 15% of a shield, which is what makes the escape
        // urgent rather than optional.
        if (MathF.Abs(Entities.MawDigestion.BiteFraction - 0.15f) > 0.001f)
            return $"a bite costs {Entities.MawDigestion.BiteFraction:P0}, expected 15%";
        return null;
    }

    private static string? MawEscapeThroughTheGun()
    {
        // The escape driven the way a player actually drives it: through the world's
        // own fire entry point, against a live World, with the cannon's real cooldown
        // and ammo in the way.
        //
        // This exists because testing the digestion in isolation is not enough and once
        // shipped a trap with no exit. MawReleasesOnThreeShots calls RegisterShot()
        // directly, which sails straight past PlayerTank.TryFire — and TryFire depends
        // on a cooldown that Update() used to skip entirely while Captured. The player
        // got one shot, then the gun stayed locked for the whole hold and the only way
        // out of the monster was sealed. Every layer between the button and the effect
        // has to be in the loop or the check proves nothing.
        var world = new World.World { DynamicSpawning = false };
        world.Enemies.Clear();

        var player = world.Player;
        var maw = new Entities.MawCore(player.Position);
        world.AttachMawForTest(maw);

        // Stand still and be caught.
        for (int i = 0; i < 60 * 10 && world.Digestion == null; i++)
            StepWithoutInput(world);
        if (world.Digestion == null) return "the maw never swallowed a stationary player";

        int ammo0 = player.Ammo;

        // Hold the trigger down, exactly as a panicking player would, and step the sim.
        // Three shots at a 0.35s cooldown is about a second of being chewed.
        for (int i = 0; i < 60 * 8 && world.Digestion is { Held: true }; i++)
        {
            world.FirePlayerShot();
            StepWithoutInput(world);
        }

        if (world.Digestion is { Held: true })
            return "holding fire for eight seconds never broke the maw's hold";
        if (player.Ammo >= ammo0)
            return "the escape shots never cost any ammo";
        if (ammo0 - player.Ammo > 8)
            return $"the escape burned {ammo0 - player.Ammo} rounds — the cooldown isn't holding";

        // ...and the player ends up back on the grid, driving.
        for (int i = 0; i < 60 * 12 && world.Digestion != null; i++)
            StepWithoutInput(world);
        if (world.Digestion != null) return "the digestion never finished";
        if (player.Captured) return "control was never handed back";
        return null;
    }

    /// <summary>
    /// Hangs a maw directly over a stationary player and runs it forward until its
    /// lunge closes over them — the state a digestion needs. Returns nulls if it never
    /// got there, which is itself the failure worth reporting.
    /// </summary>
    private static (Entities.MawCore?, Entities.PlayerTank?) UnderTheMaw()
    {
        var player = new Entities.PlayerTank(Vector2.Zero);
        var maw = new Entities.MawCore(Vector2.Zero);

        // Hover -> (windup) -> lunge -> caught. The player never moves, which is the
        // one behaviour this monster punishes.
        for (int i = 0; i < 60 * 10; i++)
        {
            maw.Update((float)Config.FixedDt, player.Position, player.Height);
            if (maw.JustCaught) return (maw, player);
        }
        return (null, null);
    }

    // --- The SOLDIER -----------------------------------------------------------

    /// <summary>
    /// Every other chassis opens at the origin, which the skyline is deliberately kept
    /// out of. That start is useless to this one: its whole loop is anchors, and there
    /// is nothing to anchor to inside the clearing. So the check is not "does it stand
    /// somewhere sensible" but the thing the player actually experiences — on frame one,
    /// with nothing touched, is there something the crosshair can bite?
    /// </summary>
    private static string? SoldierStartsAtAnAnchor()
    {
        var world = SoldierWorld();
        var p = world.Player;

        if (p.Soldier == null) return "the soldier loadout produced a craft with no rig";

        float toCity = float.MaxValue;
        foreach (var s in world.Structures)
            toCity = MathF.Min(toCity, Torus.Distance(s.Position, p.Position));
        if (toCity > Entities.SoldierRig.MaxRange)
            return $"opens {toCity:0} units from the nearest building — past a cable's {Entities.SoldierRig.MaxRange:0}";

        if (!world.TryFindAnchor(p.Eye, p.Forward3, out Vector3 at, out _))
            return "the opening view has no anchor in it at all";

        float range = Vector3.Distance(p.Eye, at);
        if (range > Entities.SoldierRig.MaxRange)
            return $"the anchor in sight is {range:0} out, past the rig's reach";
        return null;
    }

    /// <summary>
    /// The opener. It has to clear enough to matter (the spec asks for 12 to 18 metres),
    /// take about 1.2 seconds getting there, and cost a visible chunk of the bottle —
    /// a jump that were free would make the reserve meaningless.
    /// </summary>
    private static string? SoldierJumpClearsTheCity()
    {
        var world = SoldierWorld();
        var p = world.Player;
        var rig = p.Soldier!;

        float gas0 = p.Hyper;
        if (!rig.Jump(p)) return "a standing soldier refused to jump on a full reserve";
        if (p.Hyper >= gas0) return "the jump cost no gas";

        float peak = 0f;
        float apexAt = 0f;
        int nearPeak = 0;
        for (int i = 0; i < 60 * 6; i++)
        {
            StepWithoutInput(world);
            if (p.Height > peak) { peak = p.Height; apexAt = (i + 1) / 60f; }
            // Frames spent within a metre of the top: the hang, counted.
            if (peak > 0f && p.Height > peak - 1f) nearPeak++;
            if (peak > 0f && p.Height <= 0f) break;
        }

        if (peak < 12f || peak > 18f) return $"the jump peaked at {peak:0.0}m, wanted 12-18";
        if (apexAt < 1.05f || apexAt > 1.4f) return $"took {apexAt:0.00}s to the apex, wanted ~1.2";

        // And the floaty hang at the top has to actually be there. Without it the apex
        // is a corner rather than a beat, and the beat is where the player picks the
        // anchor they are about to fire at.
        float hang = nearPeak / 60f;
        if (hang < 0.35f) return $"only {hang:0.00}s spent at the top — there is no hang";

        if (p.Height > 0.01f) return "the soldier never came back down";
        return null;
    }

    /// <summary>
    /// The core of the class: aim at a building, press the key, and a second later be
    /// hanging off it. Drives it exactly the way the player does — through the world's
    /// own fire path, against a live raycast, stepping the sim between.
    /// </summary>
    private static string? SoldierHookBites()
    {
        var world = SoldierWorld();
        var rig = world.Player.Soldier!;

        if (!world.FireSoldierHookForTest(right: true))
            return "nothing in the opening view to fire at";
        if (rig.Right.State != Entities.HookState.Flying)
            return "the hook never left the launcher";

        for (int i = 0; i < 60 * 3 && !rig.Right.Anchored; i++) StepWithoutInput(world);

        if (!rig.Right.Anchored) return "the hook flew but never bit anything";
        if (rig.Right.Holding == null) return "the hook bit, but is holding nothing";

        // And what it bit has to be where it was drawn to bite: on the surface of the
        // thing, not floating in the air next to it or buried in its middle.
        Structure s = rig.Right.Holding!;
        float off = Torus.Distance(rig.Right.Tip, s.Position);
        if (off > 12f) return $"the bite landed {off:0.0} units from the building it claims";
        if (rig.Right.TipY < 0f) return "the bite landed underground";
        return null;
    }

    /// <summary>
    /// A cable is a hard constraint, not a rope that stretches: the player may never end
    /// up further from the anchor than its length, and gravity acting on a body held at
    /// a fixed radius has to produce a <em>swing</em> — lateral travel — rather than a
    /// fall. Both halves matter. A constraint that held but killed all momentum would
    /// pass the first and make the class unplayable.
    /// </summary>
    private static string? SoldierCableSwings()
    {
        var world = SoldierWorld();
        var p = world.Player;
        var rig = p.Soldier!;

        // Up into the air first, so there is somewhere to swing to.
        rig.Jump(p);
        for (int i = 0; i < 45; i++) StepWithoutInput(world);

        if (!world.FireSoldierHookForTest(right: true)) return "nothing to fire at";
        for (int i = 0; i < 60 * 3 && !rig.Right.Anchored; i++) StepWithoutInput(world);
        if (!rig.Right.Anchored) return "the hook never bit";

        float length = rig.Right.Length;
        float worstOver = 0f;
        float travelled = 0f;
        Vector2 was = p.Position;

        for (int i = 0; i < 60 * 4; i++)
        {
            StepWithoutInput(world);
            if (!rig.Right.Anchored) break;

            var to = new Vector3(
                Torus.Delta(p.Position, rig.Right.Tip).X,
                rig.Right.TipY - p.Height - Entities.SoldierRig.ShoulderHeight,
                Torus.Delta(p.Position, rig.Right.Tip).Y);
            worstOver = MathF.Max(worstOver, to.Length() - rig.Right.Length);

            travelled += Torus.Distance(was, p.Position);
            was = p.Position;
        }

        // A centimetre of overshoot inside one step is the solver settling; a metre is
        // a rope made of elastic.
        if (worstOver > 0.25f)
            return $"the cable stretched {worstOver:0.00} past its length";
        if (travelled < length * 0.5f)
            return $"hanging on a {length:0}m cable only moved the player {travelled:0.0}m — it is not swinging";
        return null;
    }

    /// <summary>
    /// The slingshot. Letting go has to do <em>nothing</em> — no impulse, no damping,
    /// no snap — because everything the player built in the arc is theirs and the whole
    /// expressive ceiling of the class is choosing the moment to stop being attached.
    /// </summary>
    private static string? SoldierReleaseKeepsMomentum()
    {
        var world = SoldierWorld();
        var p = world.Player;
        var rig = p.Soldier!;

        rig.Jump(p);
        for (int i = 0; i < 45; i++) StepWithoutInput(world);
        if (!world.FireSoldierHookForTest(right: true)) return "nothing to fire at";
        for (int i = 0; i < 60 * 3 && !rig.Right.Anchored; i++) StepWithoutInput(world);
        if (!rig.Right.Anchored) return "the hook never bit";

        // Swing until there is real speed on the clock.
        for (int i = 0; i < 60 * 3 && rig.PlanarSpeed < 8f; i++) StepWithoutInput(world);
        if (rig.PlanarSpeed < 8f) return "the swing never built any speed to keep";

        Vector3 before = rig.Velocity;
        rig.ReleaseHook(right: true);
        if (rig.Velocity != before)
            return "letting go of the cable changed the player's momentum";

        // And one step later it should differ only by gravity — nothing else may touch it.
        StepWithoutInput(world);
        var planarBefore = new Vector2(before.X, before.Z);
        var planarAfter = new Vector2(rig.Velocity.X, rig.Velocity.Z);
        if (Vector2.Distance(planarBefore, planarAfter) > 0.5f)
            return "the released player's planar momentum was damped";
        return null;
    }

    /// <summary>
    /// Reeling is the engine: it has to convert gas into speed. If it costs nothing the
    /// reserve is decoration, and if it produces nothing the cables are a tether rather
    /// than a way to travel.
    /// </summary>
    private static string? SoldierReelBurnsGas()
    {
        var world = SoldierWorld();
        var p = world.Player;
        var rig = p.Soldier!;

        rig.Jump(p);
        for (int i = 0; i < 45; i++) StepWithoutInput(world);
        if (!world.FireSoldierHookForTest(right: true)) return "nothing to fire at";
        for (int i = 0; i < 60 * 3 && !rig.Right.Anchored; i++) StepWithoutInput(world);
        if (!rig.Right.Anchored) return "the hook never bit";

        float gas0 = p.Hyper;
        float length0 = rig.Right.Length;
        float speed0 = rig.Speed;

        // Hold W for a second, which is what a player crossing a gap does.
        rig.MoveInput = new Vector2(0f, 1f);
        for (int i = 0; i < 60; i++) StepWithoutInput(world);
        rig.MoveInput = Vector2.Zero;

        if (p.Hyper >= gas0) return "a second of reeling cost no gas";
        if (rig.Right.Length >= length0) return "reeling didn't shorten the cable";
        if (rig.Speed <= speed0) return "reeling didn't accelerate the player";
        if (!rig.Right.Anchored) return "reeling shook the hook loose";
        return null;
    }

    /// <summary>
    /// The single most important property of a mouse-aimed weapon: the round goes where
    /// the crosshair is. On every other chassis the gun points where the chassis points
    /// and there is nothing to get wrong; here the aim, the eye, the muzzle offset and
    /// the round's own climb are four separate pieces of arithmetic, and any one of them
    /// being off by a few degrees is invisible standing still and infuriating in a fight.
    ///
    /// Checked at several pitches, including steep ones, because the failure this is
    /// really guarding against — treating a look <em>slope</em> as a look <em>angle</em>
    /// — is nearly exact at level and badly wrong the moment the player looks up.
    /// </summary>
    private static string? SoldierRifleFliesTrue()
    {
        foreach (float pitch in new[] { 0f, 0.22f, -0.4f, 0.9f })
        {
            var world = SoldierWorld();
            var p = world.Player;
            p.Pitch = pitch;
            // Fired from the air, which is where this chassis actually shoots from — and
            // is also the only way a steeply downward shot has any flight to measure
            // before it correctly buries itself in the grid a metre and a half below.
            p.Height = 40f;

            Vector3 eye = p.Eye;
            Vector3 aim = p.Forward3;

            world.FireSoldierRifleForTest();
            // Two steps, so the round has genuinely travelled and any per-step error has
            // had a chance to accumulate rather than hiding in the launch offset.
            StepWithoutInput(world);
            StepWithoutInput(world);

            Entities.Projectile? round = null;
            foreach (var q in world.Projectiles)
                if (q.Active && q.IsTracer) { round = q; break; }
            if (round == null) return $"no round in the air at pitch {pitch:0.00}";

            var at = new Vector3(round.Position.X, round.Height, round.Position.Y);
            Vector3 fromEye = at - eye;
            float along = Vector3.Dot(fromEye, aim);
            if (along < 1f) return $"the round went nowhere at pitch {pitch:0.00}";

            // How far off the line of sight it is, as an angle — which is the number a
            // player actually experiences, and the one that stays meaningful whatever
            // the range happens to be.
            float off = Vector3.Distance(fromEye, aim * along);
            float error = MathF.Atan2(off, along);
            if (error > 0.02f)
                return $"at pitch {pitch:0.00} the round flies {error:0.000} rad off the aim";
        }
        return null;
    }

    /// <summary>
    /// A rocket takes the building down, and anything hanging from it goes with it. This
    /// is the one rule that makes the rockets frightening rather than free: the player is
    /// entirely capable of shooting away the thing holding them up.
    /// </summary>
    private static string? SoldierAnchorDiesWithItsTower()
    {
        var world = SoldierWorld();
        var rig = world.Player.Soldier!;

        if (!world.FireSoldierHookForTest(right: true)) return "nothing to fire at";
        for (int i = 0; i < 60 * 3 && !rig.Right.Anchored; i++) StepWithoutInput(world);
        if (!rig.Right.Anchored) return "the hook never bit";

        Structure held = rig.Right.Holding!;
        held.Strike();   // whatever cut it down — a rocket, a lance, the sky falling

        for (int i = 0; i < 60 && rig.Right.Anchored; i++) StepWithoutInput(world);

        if (rig.Right.Anchored)
            return "the player is still hanging from a building that is on its way down";
        if (rig.Right.Holding != null)
            return "the torn hook is still holding a reference to the wreck";
        return null;
    }

    /// <summary>
    /// Weak material gives way. The point is not the failure itself but that it is
    /// <em>readable</em>: the same scale threshold the HUD warns on is the one that
    /// tears, so a player who learns to distrust thin spires is learning something true.
    /// </summary>
    private static string? SoldierWeakAnchorTears()
    {
        var world = SoldierWorld();
        var p = world.Player;
        var rig = p.Soldier!;

        rig.Jump(p);
        for (int i = 0; i < 45; i++) StepWithoutInput(world);
        if (!world.FireSoldierHookForTest(right: true)) return "nothing to fire at";
        for (int i = 0; i < 60 * 3 && !rig.Right.Anchored; i++) StepWithoutInput(world);
        if (!rig.Right.Anchored) return "the hook never bit";

        Structure held = rig.Right.Holding!;
        bool weak = held.Scale < Entities.SoldierRig.WeakScale;

        // Hang on it for comfortably longer than weak material is supposed to hold.
        for (int i = 0; i < (int)(60 * (Entities.SoldierRig.TearTime + 1.5f)); i++)
        {
            StepWithoutInput(world);
            if (!rig.Right.Anchored) break;
        }

        if (weak && rig.Right.Anchored)
            return $"a scale-{held.Scale:0.00} anchor held for good — weak material never tears";
        if (!weak && !rig.Right.Anchored)
            return $"a scale-{held.Scale:0.00} anchor let go — solid material is failing";
        return null;
    }

    /// <summary>A stage with a soldier in it and nothing else moving: no spawn director,
    /// no hunters, so the checks above are measuring the rig and not a firefight.</summary>
    private static World.World SoldierWorld()
    {
        var loadout = new Loadout { Class = PlayerClass.Soldier };
        var world = new World.World(loadout) { DynamicSpawning = false };
        world.Enemies.Clear();
        return world;
    }

    // --- The FISH ---------------------------------------------------------------

    /// <summary>
    /// The one fact about this chassis that has to be true before anything else can be
    /// tested: it is in the water. Every rule the class has — the lift, the beat, the
    /// bloom, the beaching — is a rule about a body that is off the ground, and a fish
    /// that opened lying on the grid would spend the player's first ten seconds in the one
    /// state the entire design is about escaping.
    /// </summary>
    private static string? FishStartsSwimming()
    {
        var world = FishWorld();
        var body = world.Player.Fish!;

        if (world.Player.Height < 10f)
            return $"a fish opened {world.Player.Height:0.0} off the grid";
        if (body.Beached) return "a fish opened beached";
        // And already moving: the class cannot hold altitude without speed, so opening
        // stationary would mean opening in a stall.
        if (body.PlanarSpeed < 1f) return "a fish opened dead in the water";

        // And the sink has to be gentle enough to answer. A player who takes their hands
        // off gets several seconds of drifting downward before anything bad happens — long
        // enough to read the depth ladder, understand what is going on and beat out of it.
        // Much less than this and the class would be a chore rather than a glide.
        for (int i = 0; i < 60 * 5; i++) StepWithoutInput(world);
        if (body.Beached) return "an idle fish hit the grid inside five seconds";
        return null;
    }

    /// <summary>
    /// The single decision the whole class rests on. A beat is an <em>impulse</em>: one
    /// press is one shove, mashing inside the refractory period buys nothing, and holding
    /// anything at all buys nothing either. If this ever degrades into a throttle the
    /// chassis becomes a slower aircraft and every other rule stops mattering.
    /// </summary>
    private static string? FishBeatIsAnImpulse()
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        p.Pitch = 0f;
        body.Velocity = Vector3.Zero;

        float before = body.Speed;
        if (!world.BeatFishForTest()) return "the first beat was refused";
        if (body.Speed < before + Entities.FishRig.BeatImpulse * 0.5f)
            return $"a beat only added {body.Speed - before:0.0} m/s";

        // Mashing: the next press inside the refractory period does nothing at all.
        if (world.BeatFishForTest()) return "a second beat landed inside the refractory period";

        // ...and one after it does. The gap is the rhythm the player is learning.
        for (int i = 0; i < (int)(Entities.FishRig.BeatInterval * 60f) + 2; i++)
            StepWithoutInput(world);
        if (!world.BeatFishForTest()) return "the tail never recovered between beats";

        // And with nothing pressed at all, the water takes it back. A body that coasted
        // forever would make the reserve decoration.
        float carried = body.Speed;
        for (int i = 0; i < 60 * 3; i++) StepWithoutInput(world);
        if (body.Speed >= carried)
            return "three seconds of coasting cost the body no speed at all";
        return null;
    }

    /// <summary>
    /// The economy. Beats cost breath, and breath comes back <em>only</em> while the tail
    /// is still — which is the rule that turns movement on this chassis into a rhythm
    /// rather than a key held down. Both halves have to hold: if beating were free the
    /// reserve would be decoration, and if the reserve refilled while beating there would
    /// be no reason ever to stop.
    /// </summary>
    private static string? FishBreathIsARhythm()
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        p.Hyper = p.MaxHyper;
        if (!world.BeatFishForTest()) return "the beat was refused";
        StepWithoutInput(world);

        float spent = p.Hyper;
        if (spent >= p.MaxHyper) return "a beat cost no breath";
        if (body.Recovering) return "breath started coming back the instant the tail moved";

        // Beating flat out for two seconds must genuinely run the reserve down rather
        // than being paid for out of the regen.
        for (int i = 0; i < 60 * 2; i++)
        {
            world.BeatFishForTest();
            StepWithoutInput(world);
        }
        if (p.Hyper >= spent)
            return "two seconds of continuous beating did not drain the reserve";

        // And coasting fills it. Nothing else does.
        float drained = p.Hyper;
        for (int i = 0; i < 60 * 2; i++) StepWithoutInput(world);
        if (!body.Recovering) return "a coasting fish never starts recovering";
        if (p.Hyper <= drained) return "two seconds of coasting gave no breath back";
        return null;
    }

    /// <summary>
    /// The handling model. Momentum lags the crosshair, and how fast it catches up is set
    /// by how far the body is rolled — that gap, and the player's control over closing it,
    /// is the entire difference between this chassis and a strafing camera. A carve that
    /// turned no faster than a drift would make A and D pure decoration.
    /// </summary>
    private static string? FishCarveTurnsTighter()
    {
        float level = TurnedTowardLookIn(roll: 0f);
        float carved = TurnedTowardLookIn(roll: 1f);

        if (carved <= level + 0.05f)
            return $"a full carve converged to {carved:0.00} against a level drift's {level:0.00}";
        // And the level drift has to genuinely be a drift — a body that snapped onto the
        // crosshair without rolling would pass the comparison above and still be wrong.
        if (level > 0.98f)
            return "a level body turned onto the crosshair almost instantly — there is no drift";
        return null;
    }

    /// <summary>
    /// One second of turning ninety degrees onto the crosshair at a given roll, reported as
    /// how far the momentum got: 1 is fully converged, 0 is still travelling at a right
    /// angle to where the player is looking.
    /// </summary>
    private static float TurnedTowardLookIn(float roll)
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        // Travelling along +Z and looking along +X: a dead ninety degrees to turn through.
        p.Pitch = 0f;
        p.Heading = MathF.PI / 2f;
        body.Velocity = new Vector3(0f, 0f, 24f);
        body.MoveInput = new Vector2(roll, 0f);

        for (int i = 0; i < 60; i++) StepWithoutInput(world);

        return Vector3.Dot(Vector3.Normalize(body.Velocity), p.Forward3);
    }

    /// <summary>
    /// The altitude economy, and the class's answer to the soldier's height-for-speed
    /// trade: a body generates lift by moving, so speed is what keeps it up and the price
    /// of stalling is height. This is what makes running out of breath frightening rather
    /// than merely slow — the reserve going means the altitude goes with it.
    /// </summary>
    private static string? FishLiftHoldsAltitude()
    {
        float stalled = SankOverASecond(speed: 0f);
        float moving = SankOverASecond(speed: 34f);

        if (stalled <= 0.5f) return "a stalled fish did not sink at all";
        if (moving >= stalled * 0.6f)
            return $"a body at speed sank {moving:0.0}m against a stalled one's {stalled:0.0}m — lift is doing nothing";
        return null;
    }

    /// <summary>
    /// Metres lost in one second from a given planar speed, with nothing pressed. Measured
    /// down in clean water, well below the warned band — up in the thin stuff the lift is
    /// deliberately failing, which is the bloom's job and not this test's.
    /// </summary>
    private static float SankOverASecond(float speed)
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        p.Pitch = 0f;
        p.Height = Entities.FishRig.BloomWarnHeight - 9f;
        body.Velocity = new Vector3(0f, 0f, speed);

        float was = p.Height;
        for (int i = 0; i < 60; i++) StepWithoutInput(world);
        return was - p.Height;
    }

    /// <summary>
    /// The strike is a commitment, and the shape of that commitment is the whole balance of
    /// it: a gather where the body is nearly stopped and helpless, a lunge that is
    /// genuinely faster than anything swimming can reach, and a recovery that refuses to
    /// beat. Lose any of the three and it stops being a decision.
    /// </summary>
    private static string? FishStrikeCommits()
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        p.Pitch = 0f;
        body.Velocity = new Vector3(0f, 0f, 30f);

        if (!world.StrikeFishForTest()) return "the strike was refused on a full reserve";
        if (body.Strike != Entities.StrikeState.Coil) return "the strike did not begin by coiling";

        // The gather: momentum is scrubbed, and this is what the player pays before they
        // know whether the shot lands.
        for (int i = 0; i < (int)(Entities.FishRig.CoilTime * 60f) - 2; i++) StepWithoutInput(world);
        if (body.Speed > 12f) return $"the coil left {body.Speed:0.0} m/s on the clock — it costs nothing";

        for (int i = 0; i < 6 && body.Strike == Entities.StrikeState.Coil; i++) StepWithoutInput(world);
        if (body.Strike != Entities.StrikeState.Lunge) return "the coil never loosed";
        if (body.Speed < Entities.FishRig.MaxSpeed)
            return $"the lunge travels at {body.Speed:0.0}, no faster than swimming does";

        // And the bill: spent, and unable to beat its way out of the recovery.
        for (int i = 0; i < (int)(Entities.FishRig.LungeTime * 60f) + 4; i++) StepWithoutInput(world);
        if (body.Strike != Entities.StrikeState.Recover) return "the lunge never ended";
        if (world.BeatFishForTest()) return "a spent fish could beat straight out of its recovery";

        for (int i = 0; i < (int)(Entities.FishRig.RecoverTime * 60f) + 4; i++) StepWithoutInput(world);
        if (body.Strike != Entities.StrikeState.Ready) return "the strike never came back";
        return null;
    }

    /// <summary>
    /// A strike spears one thing. It is not a plough that clears a street — if a single
    /// lunge could rake a whole group the correct play would be to fly through crowds, and
    /// the attack would stop being about picking a target.
    /// </summary>
    private static string? FishStrikeSpearsOnce()
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        // Down at hunter height, looking along +Z, with two of them in a line dead ahead.
        p.Pitch = 0f;
        p.Heading = 0f;
        p.Height = 1.5f;
        p.Position = Vector2.Zero;
        body.Velocity = Vector3.Zero;

        var near = new Entities.EnemyTank(new Vector2(0f, 9f), elite: false);
        var far = new Entities.EnemyTank(new Vector2(0f, 17f), elite: false);
        world.Enemies.Add(near);
        world.Enemies.Add(far);

        if (!world.StrikeFishForTest()) return "the strike was refused";

        // Long enough for the whole lunge to run through both of them.
        int steps = (int)((Entities.FishRig.CoilTime + Entities.FishRig.LungeTime) * 60f) + 6;
        for (int i = 0; i < steps; i++) StepWithoutInput(world);

        if (near.Alive) return "the strike passed through a hunter without hurting it";
        if (!far.Alive) return "one strike killed two hunters — the lunge is a plough";
        return null;
    }

    /// <summary>
    /// The floor. Meeting the grid is not a landing on this chassis, it is a fish out of
    /// water — and the important half of the rule is that it is <em>recoverable</em>: a
    /// beached player must be able to beat their way back up, or the state is a death
    /// sentence rather than a punishment.
    /// </summary>
    private static string? FishBeachesAndRecovers()
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        p.Pitch = 0f;
        p.Height = 3f;
        body.Velocity = new Vector3(0f, -6f, 0f);

        for (int i = 0; i < 60 * 2 && !body.Beached; i++) StepWithoutInput(world);
        if (!body.Beached) return "a fish driven into the grid never beached";
        if (p.Height > 0.01f) return "a beached fish is not actually on the grid";

        // On the deck it can barely move. That is the whole punishment, and a beached body
        // that went on skating at speed would read as landing badly rather than as being
        // out of its element. Measured over half a second, which is the window in which a
        // player decides whether they are in trouble.
        body.Velocity = new Vector3(20f, 0f, 0f);
        for (int i = 0; i < 30; i++) StepWithoutInput(world);
        if (body.PlanarSpeed > 1f)
            return $"a beached fish still carries {body.PlanarSpeed:0.0} m/s after half a second";

        // Now beat out of it. Nose up, wait out any stagger, and take a couple of flops.
        p.Pitch = 0.5f;
        for (int i = 0; i < 60 * 4; i++)
        {
            world.BeatFishForTest();
            StepWithoutInput(world);
            if (!body.Beached) break;
        }
        if (body.Beached) return "a beached fish could not beat its way back into the water";
        return null;
    }

    /// <summary>
    /// The ceiling, and the half of it that matters most: the player is told, for free, a
    /// long way before anything costs them. Ten metres of warned, thinning water is enough
    /// to turn round in at any speed the class can reach — and if that band ever started
    /// billing, the warning would just be a cheaper way of being hurt.
    /// </summary>
    private static string? FishBloomWarnsFirst()
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        // Parked in the middle of the warned band, held there against the sink.
        p.Pitch = 0f;
        float shield = p.Shield;

        for (int i = 0; i < 60 * 3; i++)
        {
            p.Height = (Entities.FishRig.BloomWarnHeight + Entities.FishRig.BloomHeight) * 0.5f;
            StepWithoutInput(world);
        }

        if (body.BloomNotice <= 0f) return "the warned band never raised a notice";
        if (body.InBloom) return "the middle of the warned band already counts as the bloom";
        if (body.Toxicity > 0f) return "the warned band reports toxicity";
        if (p.Shield < shield) return "three seconds in the warned band cost shield";

        // And nothing at all below it, so ordinary play at skyline height is never nagged.
        p.Height = Entities.FishRig.BloomWarnHeight - 6f;
        StepWithoutInput(world);
        if (body.BloomNotice > 0f) return "the notice fires below the warned band";
        return null;
    }

    /// <summary>
    /// And the other half: past the warning it genuinely bites, and the water up there is
    /// genuinely thinner. The thinning is the more important of the two — it is a fence the
    /// player feels through the controls, which means a fish that stops fighting sinks out
    /// of the bloom on its own rather than needing to be told to.
    /// </summary>
    private static string? FishBloomHurts()
    {
        var world = FishWorld();
        var p = world.Player;
        var body = p.Fish!;

        p.Pitch = 0f;
        p.Height = Entities.FishRig.BloomHeight + 8f;
        body.Velocity = new Vector3(0f, 0f, 30f);

        float shield = p.Shield;
        float was = p.Height;
        for (int i = 0; i < 60 * 2; i++) StepWithoutInput(world);

        if (!body.InBloom && p.Height > Entities.FishRig.BloomHeight)
            return "the body is above the bloom's floor but not in it";
        if (p.Shield >= shield) return "two seconds inside the bloom cost no shield";

        // Thin water: a body moving this fast holds its altitude easily down in the clean
        // water, and must not up here.
        if (p.Height >= was)
            return "the body held its altitude inside the bloom — the water is not thinning";
        return null;
    }

    /// <summary>
    /// The same property the soldier's rifle has to have, for the same reason: this is a
    /// mouse-aimed weapon, so the aim, the eye, the muzzle offset and the round's own climb
    /// are four separate pieces of arithmetic, and any one of them being off is invisible
    /// standing still and infuriating mid-carve. Checked at steep pitches especially, since
    /// the failure it guards against — treating a look slope as a look angle — is nearly
    /// exact at level and badly wrong the moment the player looks up.
    /// </summary>
    private static string? FishSpitFliesTrue()
    {
        foreach (float pitch in new[] { 0f, 0.22f, -0.4f, 0.9f })
        {
            var world = FishWorld();
            var p = world.Player;
            p.Pitch = pitch;
            // High enough that a steeply downward shot has some flight to measure before
            // it correctly buries itself in the grid, low enough to stay in clean water.
            p.Height = Entities.FishRig.BloomWarnHeight - 9f;

            Vector3 eye = p.Eye;
            Vector3 aim = p.Forward3;

            world.FireFishSpitForTest();
            StepWithoutInput(world);
            StepWithoutInput(world);

            Entities.Projectile? round = null;
            foreach (var q in world.Projectiles)
                if (q.Active && q.IsTracer) { round = q; break; }
            if (round == null) return $"no round in the water at pitch {pitch:0.00}";

            var at = new Vector3(round.Position.X, round.Height, round.Position.Y);
            Vector3 fromEye = at - eye;
            float along = Vector3.Dot(fromEye, aim);
            if (along < 1f) return $"the round went nowhere at pitch {pitch:0.00}";

            float off = Vector3.Distance(fromEye, aim * along);
            float error = MathF.Atan2(off, along);
            if (error > 0.02f)
                return $"at pitch {pitch:0.00} the spit flies {error:0.000} rad off the aim";
        }
        return null;
    }

    /// <summary>A stage with a fish in it and nothing else moving: no spawn director, no
    /// hunters, so the checks above are measuring the body and not a firefight.</summary>
    private static World.World FishWorld()
    {
        var loadout = new Loadout { Class = PlayerClass.Fish };
        var world = new World.World(loadout) { DynamicSpawning = false };
        world.Enemies.Clear();
        return world;
    }

    // --- The VIRUS: the mote and the bodies it wears -----------------------------

    /// <summary>
    /// The class's opening state and its one unique freedom. A virus starts exposed — the
    /// naked mote, no host — and the mote flies where the crosshair points, which no other
    /// chassis's W can claim: nose up and drive, and it gains genuine altitude with no
    /// impulse, no arc and no cable involved.
    /// </summary>
    private static string? VirusMoteFliesWhereItLooks()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        if (!v.Exposed) return "a virus did not open exposed";
        if (v.Hosted) return "a virus opened already wearing a body";

        // Nose up the look and hold thrust: the mote climbs and travels along the bearing.
        p.Pitch = 0.6f;
        p.Heading = 0f;
        v.MoveInput = new Vector2(0f, 1f);
        Vector2 start = p.Position;

        for (int i = 0; i < 60 * 2; i++) StepWithoutInput(world);

        if (p.Height < 4f)
            return $"two seconds of flying up the look only gained {p.Height:0.0}m";
        Vector2 gone = Torus.Delta(start, p.Position);
        if (gone.Y < 4f)
            return $"the mote only covered {gone.Y:0.0}m along the bearing it was flown on";

        // And letting go coasts it down, not on forever: drag is what keeps the mote a
        // creature rather than a projectile.
        v.MoveInput = Vector2.Zero;
        float carried = v.Speed;
        for (int i = 0; i < 60 * 2; i++) StepWithoutInput(world);
        if (v.Speed >= carried * 0.6f)
            return "two seconds of coasting cost the mote almost none of its speed";
        return null;
    }

    /// <summary>
    /// The infection. Contact is the whole mechanic — flying the mote into a hunter wears
    /// it, with no button to press — and the body is <em>consumed</em>, not killed: it
    /// leaves the roster without a death, and the player is standing where it stood with a
    /// full decay meter on the clock.
    /// </summary>
    private static string? VirusInfectsOnContact()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        var prey = new Entities.EnemyTank(p.Position + new Vector2(0f, 2f), elite: false);
        Vector2 stood = prey.Position;
        world.Enemies.Add(prey);
        StepWithoutInput(world);

        if (!v.Hosted) return "contact with a hunter did not possess it";
        if (world.Enemies.Count != 0) return "the worn hunter is still on the roster";
        if (v.Decay < 0.99f) return $"a fresh host opened at {v.Decay:0.00} of its meter";
        if (Torus.Distance(p.Position, stood) > 0.1f)
            return "the player did not climb into where the body stood";
        return null;
    }

    /// <summary>
    /// The clock. A worn host rots out on time alone and ejects the mote — the rule that
    /// makes a body a countdown rather than a home — and rotting out costs the player's own
    /// shield nothing at all: the meter is the only thing the clock spends.
    /// </summary>
    private static string? VirusHostRots()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        PossessFreshHost(world);
        if (!v.Hosted) return "the mote never took its host";
        float shield = p.Shield;

        int i = 0;
        for (; i < 60 * 70 && v.Hosted; i++) StepWithoutInput(world);

        if (v.Hosted) return "the host never rotted out";
        // The advertised lifetime is most of a minute — a body that gave out in a few
        // seconds untouched would make hosting pointless, and the clock is meant to be
        // the rare way a host ends, not the usual one.
        if (i < 60 * 30) return $"a clean host lasted only {i / 60f:0.0}s";
        if (p.Shield < shield) return "rotting out cost the player shield";
        if (!v.Exposed) return "the ejected mote is not exposed";
        return null;
    }

    /// <summary>
    /// The armour. While a host is worn, damage drains the host's meter and the player's
    /// shield never sees it — proved by racing two identical hosted worlds, one under fire:
    /// after the same time the shot-at host is visibly more rotten and both shields are
    /// untouched. Plus the rig-level contract for a blow bigger than the whole body: the
    /// husk breaks and only the overflow comes through.
    /// </summary>
    private static string? VirusHostSoaksDamage()
    {
        var under = VirusWorld();
        var calm = VirusWorld();
        PossessFreshHost(under);
        PossessFreshHost(calm);
        if (under.Player.Virus is not { Hosted: true } shot) return "the mote never took its host";
        if (calm.Player.Virus is not { Hosted: true } idle) return "the control mote never took its host";

        // The shooter, held at a stand-off range where its own AI keeps it — far outside
        // the infect reach, close enough to land several hits over the window.
        under.Enemies.Add(new Entities.EnemyTank(new Vector2(0f, 30f), elite: false));

        float max = under.Player.MaxShield;
        for (int i = 0; i < 60 * 8; i++)
        {
            StepWithoutInput(under);
            StepWithoutInput(calm);
        }

        if (!shot.Hosted) return "eight seconds of hunter fire broke a whole host";
        if (under.Player.Shield < max) return "a hosted player's shield was touched under fire";
        if (shot.Decay >= idle.Decay - 0.02f)
            return $"fire left the host at {shot.Decay:0.00} against the clock's own {idle.Decay:0.00} — nothing was soaked";

        // And the overflow contract, at the rig: a blow past the body's whole capacity
        // breaks it and passes only the remainder through.
        var rig = new Entities.VirusRig();
        var dummy = new Entities.PlayerTank(Vector2.Zero,
            loadout: new Loadout { Class = PlayerClass.Virus });
        rig.Possess(dummy, Entities.VirusHost.Hunter);
        float over = rig.AbsorbDamage(10_000f);
        if (rig.Hosted) return "a blow bigger than the whole body left the host standing";
        if (over <= 0f || over >= 10_000f)
            return $"a broken husk passed {over:0} of a 10000 blow through";
        return null;
    }

    /// <summary>
    /// The price. A naked mote takes hits amplified — the same hunter's round costs it well
    /// over what a standard craft pays, which is the number the whole hop-or-die tension
    /// hangs on. Measured against a tank control under identical fire rather than against a
    /// copied constant, so retuning either side moves this check with it.
    /// </summary>
    private static string? VirusMoteIsFragile()
    {
        float moteLoss = FirstHitCost(new Loadout { Class = PlayerClass.Virus });
        float tankLoss = FirstHitCost(new Loadout { Class = PlayerClass.Tank });

        if (tankLoss <= 0f) return "no shot ever landed on the control tank";
        if (moteLoss <= 0f) return "no shot ever landed on the exposed mote";
        if (moteLoss < tankLoss * 1.5f)
            return $"an exposed hit cost {moteLoss:0.0} against a tank's {tankLoss:0.0} — the mote is not fragile";
        return null;
    }

    /// <summary>Shield lost to the first landed hit, for a given build standing still under
    /// one hunter's fire at stand-off range.</summary>
    private static float FirstHitCost(Loadout loadout)
    {
        var world = new World.World(loadout) { DynamicSpawning = false };
        world.Enemies.Clear();
        world.Enemies.Add(new Entities.EnemyTank(new Vector2(0f, 30f), elite: false));

        float max = world.Player.MaxShield;
        for (int i = 0; i < 60 * 15 && world.Player.Shield >= max; i++)
            StepWithoutInput(world);
        return max - world.Player.Shield;
    }

    /// <summary>
    /// The heavy. An overload spends the worn host outright: the player is ejected on the
    /// spot and the body goes off as the radial blast, which kills what is standing near
    /// it. And the other half of the guard — with no host there is nothing to spend, so the
    /// trigger stages nothing at all.
    /// </summary>
    private static string? VirusOverloadSpendsTheHost()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        // Exposed, the overload must refuse: no host, no bomb.
        world.OverloadVirusForTest();
        if (world.Blasts.Count != 0) return "an overload with no host staged a blast";

        PossessFreshHost(world);
        if (!v.Hosted) return "the mote never took its host";

        var victim = new Entities.EnemyTank(p.Position + new Vector2(0f, 8f), elite: false);
        world.Enemies.Add(victim);

        world.OverloadVirusForTest();
        if (!v.Exposed) return "the overload did not eject the player";
        if (world.Blasts.Count != 1) return $"the overload staged {world.Blasts.Count} blasts";

        for (int i = 0; i < 240 && victim.Alive; i++) StepWithoutInput(world);
        if (victim.Alive) return "the spent host's blast left a point-blank hunter standing";
        return null;
    }

    /// <summary>
    /// The same property the rifle and the spit have to have, for the same reason: a
    /// mouse-aimed weapon whose aim, eye, muzzle offset and flight are four separate pieces
    /// of arithmetic. Checked at steep pitches especially — the mote fires down its whole
    /// flight line, so "up" is not a rare case on this chassis, it is the ordinary one.
    /// </summary>
    private static string? VirusRoundFliesTrue()
    {
        foreach (float pitch in new[] { 0f, 0.22f, -0.4f, 0.9f })
        {
            var world = VirusWorld();
            var p = world.Player;
            p.Pitch = pitch;
            // Up in the mote's own air, so a steeply downward round has flight to measure
            // before it correctly buries itself in the grid.
            p.Height = 20f;

            Vector3 eye = p.Eye;
            Vector3 aim = p.Forward3;

            world.FireVirusRoundForTest();
            StepWithoutInput(world);
            StepWithoutInput(world);

            Entities.Projectile? round = null;
            foreach (var q in world.Projectiles)
                if (q.Active && q.IsTracer) { round = q; break; }
            if (round == null) return $"no round in the air at pitch {pitch:0.00}";

            var at = new Vector3(round.Position.X, round.Height, round.Position.Y);
            Vector3 fromEye = at - eye;
            float along = Vector3.Dot(fromEye, aim);
            if (along < 1f) return $"the round went nowhere at pitch {pitch:0.00}";

            float off = Vector3.Distance(fromEye, aim * along);
            float error = MathF.Atan2(off, along);
            if (error > 0.02f)
                return $"at pitch {pitch:0.00} the round flies {error:0.000} rad off the aim";
        }
        return null;
    }

    /// <summary>
    /// The mote's own clock. The grace is genuinely free — a mote can sit exposed for
    /// nearly all of it and never be billed — and past it the withering bites until a body
    /// is reached, at which point the clock is handed back whole. Lose either half and the
    /// state stops meaning what it says: a grace that bills is just damage with a delay,
    /// and a withering that possession doesn't cure is a death sentence with extra steps.
    /// </summary>
    private static string? VirusWithersAfterGrace()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        // Most of the grace, sitting still in the open: free.
        float max = p.MaxShield;
        int safe = (int)((Entities.VirusRig.ExposureGrace - 1f) * 60f);
        for (int i = 0; i < safe; i++) StepWithoutInput(world);
        if (v.Withering) return "the withering started inside the grace";
        if (p.Shield < max) return "the mote was billed inside its grace";

        // Past it: the bites start and keep coming.
        for (int i = 0; i < 60 * 4; i++) StepWithoutInput(world);
        if (!v.Withering) return "the grace ran out and nothing started";
        if (p.Shield >= max) return "four seconds of withering cost no shield";

        // And a body ends it on the spot.
        PossessFreshHost(world);
        if (!v.Hosted) return "a withering mote could not take a host";
        if (v.Withering) return "possession did not stop the withering";
        float shield = p.Shield;
        for (int i = 0; i < 60 * 2; i++) StepWithoutInput(world);
        if (p.Shield < shield) return "a hosted mote went on withering";
        return null;
    }

    /// <summary>
    /// The big prize. The Crab-Core is entered through its gem — the same weak point a
    /// bullet needs, used as a door — it leaves the field worn rather than wrecked, and
    /// the weapon that comes with it is its own lance gone wrong: one pull costs a slice
    /// of the meter and leaves several shafts burning, and the aimed one stays honest
    /// enough to kill what the crosshair was actually on.
    /// </summary>
    private static string? VirusWearsTheCrab()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        world.SpawnCrabAhead();
        if (world.Boss is not { } crab) return "no crab was raised to wear";

        // Fly the mote into the gem.
        p.Position = crab.Position;
        p.Height = Entities.CrabCore.CoreHitHeight;
        StepWithoutInput(world);

        if (v.HostKind != Entities.VirusHost.Crab)
            return "standing in the core did not possess the crab";
        if (world.Boss != null) return "the worn crab is still on the field";

        // Let the body settle onto the grid, then stand a hunter out in front and fire
        // the lance down at it from the crab's high eye.
        for (int i = 0; i < 90; i++) StepWithoutInput(world);

        var victim = new Entities.EnemyTank(
            Torus.Wrap(p.Position + new Vector2(0f, 18f)), elite: false);
        world.Enemies.Add(victim);
        p.Heading = 0f;
        p.Pitch = MathF.Atan2(Entities.EnemyTank.AimHeight - p.EyeHeight, 18f);

        float decay = v.Decay;
        world.FireVirusLanceForTest();

        if (v.Decay >= decay) return "a lance discharge cost the host nothing";
        int shafts = 0;
        foreach (var s in v.Shafts) if (s.Life > 0f) shafts++;
        if (shafts < 3) return $"the broken lance left only {shafts} shafts burning";
        if (victim.Alive) return "the aimed shaft missed the hunter under the crosshair";
        return null;
    }

    /// <summary>
    /// The other one. The Maw-Core is entered through its crystal, and what you get is the
    /// monster's own life: a body that hovers — two seconds untouched and it has not sunk
    /// to the grid — and a spit that leaves as the mouth's acid bolt rather than a
    /// re-tinted rifle round.
    /// </summary>
    private static string? VirusWearsTheMaw()
    {
        var world = VirusWorld();
        var p = world.Player;
        var v = p.Virus!;

        world.SpawnMawAhead();
        if (world.Maw is not { } mouth) return "no maw was raised to wear";

        p.Position = mouth.Position;
        p.Height = Entities.MawRig.CrystalWorldY;
        StepWithoutInput(world);

        if (v.HostKind != Entities.VirusHost.Maw)
            return "standing in the crystal did not possess the maw";
        if (world.Maw != null) return "the worn maw is still on the field";

        for (int i = 0; i < 60 * 2; i++) StepWithoutInput(world);
        if (p.Height < 1.5f) return $"a worn maw sank to {p.Height:0.0} — it is not hovering";

        world.FireVirusRoundForTest();
        StepWithoutInput(world);
        foreach (var q in world.Projectiles)
            if (q.Active && q.IsAcid) return null;
        return "the worn maw's spit is not an acid bolt";
    }

    /// <summary>A stage with a virus in it and nothing else moving, so the checks above are
    /// measuring the mote and not a firefight.</summary>
    private static World.World VirusWorld()
    {
        var loadout = new Loadout { Class = PlayerClass.Virus };
        var world = new World.World(loadout) { DynamicSpawning = false };
        world.Enemies.Clear();
        return world;
    }

    /// <summary>Parks a hunter on the player and steps once, so the mote takes it — the
    /// standard way into the hosted state for the checks above.</summary>
    private static void PossessFreshHost(World.World world)
    {
        world.Enemies.Add(new Entities.EnemyTank(world.Player.Position, elite: false));
        StepWithoutInput(world);
    }

    // --- helpers: advance the sim without going through global input ---

    private static void StepWithoutInput(World.World world)
    {
        // Re-implements World.Update minus the InputMap read, so the test needs
        // no Raylib window. Player fire is injected explicitly by the caller.
        world.StepForTest((float)Config.FixedDt);
    }

    private static void AimPlayerAtFirstEnemy(World.World world)
    {
        if (world.Enemies.Count == 0) return;
        Vector2 to = world.Enemies[0].Position - world.Player.Position;
        world.Player.Heading = MathF.Atan2(to.X, to.Y);
    }
}
