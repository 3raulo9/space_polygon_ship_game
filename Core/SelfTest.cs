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
        var world = new World.World();
        // Face the enemy and hold fire; step a few seconds of sim.
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
        var world = new World.World();
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
