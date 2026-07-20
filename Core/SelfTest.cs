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
        failures += Check("player can destroy an enemy", PlayerKillsEnemy);
        failures += Check("enemy can damage the player", EnemyDamagesPlayer);
        failures += Check("ammo is finite", AmmoDepletes);
        failures += Check("battery refuels shield + hyper", BatteryRefuels);
        failures += Check("bullet pickup restocks ammo", AmmoPickupRestocks);
        failures += Check("grounded shot misses the boss core", GroundedShotMissesCore);
        failures += Check("air shot at core height kills the boss", AirShotKillsCore);
        failures += Check("air shot detonates on the horizon", AirShotExpiresForBlast);
        failures += Check("debug key spawns a random enemy", DebugSpawnAddsEnemy);
        failures += Check("the boss seizes and throws a cornered player", BossSeizesPlayer);
        failures += Check("a held player is raised to face the core", SeizureFramesTheCore);
        failures += Check("the boss's hands frame the held player", SeizureHandsReachThePlayer);
        failures += Check("the boss's beam fires where the player was", BeamLocksItsDirection);

        Console.WriteLine(failures == 0
            ? "SELFTEST: all checks passed"
            : $"SELFTEST: {failures} check(s) FAILED");
        return failures == 0 ? 0 : 1;
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

    private static string? BatteryRefuels()
    {
        var world = new World.World();
        // Spend some shield and hyper so a refill has room to land.
        world.Player.TakeDamage(50f);
        world.Player.TryHyperspace(); // drains most of the Hyper reserve
        float shield0 = world.Player.Shield;
        float hyper0 = world.Player.Hyper;

        // Drop a battery right on the craft and step once so it's collected.
        world.Pickups.Add(new Entities.Pickup(world.Player.Position, Entities.PickupKind.Battery));
        StepWithoutInput(world);

        if (world.Player.Shield <= shield0) return "shield did not recharge";
        if (world.Player.Hyper <= hyper0) return "hyper did not recharge";
        return null;
    }

    private static string? AmmoPickupRestocks()
    {
        var world = new World.World();
        // Burn some ammo first so a restock is observable.
        for (int i = 0; i < 20; i++) { world.FirePlayerShot(); StepWithoutInput(world); }
        int ammo0 = world.Player.Ammo;

        world.Pickups.Add(new Entities.Pickup(world.Player.Position, Entities.PickupKind.Ammo));
        StepWithoutInput(world);

        return world.Player.Ammo > ammo0 ? null : "ammo did not restock";
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
