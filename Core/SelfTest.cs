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
        failures += Check("player can destroy an enemy", PlayerKillsEnemy);
        failures += Check("enemy can damage the player", EnemyDamagesPlayer);
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
