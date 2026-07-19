namespace VoidTanks.Entities;

/// <summary>
/// Every enemy form the game can put on screen, as a flat identifier. The live
/// sim only spawns the two tanks today; the ship kinds exist so the hidden test
/// screen can show off placeholder polygon craft next to the real hunters.
/// </summary>
public enum EnemyKind
{
    StandardTank, // the wedge hunter
    EliteTank,    // the cone elite
    ShipInterceptor,
    ShipGunship,
    ShipScout,
    CrabCoreBoss, // the Stalker — animated state-machine boss
}

/// <summary>
/// One row in the bestiary: an enemy form with a display name and the numbers a
/// tester would want to eyeball — how much it can soak, how hard it hits. Health
/// mirrors the live shield points; damage mirrors the shot damage where the form
/// actually fights. The ships carry placeholder numbers so their silhouettes can
/// be judged before they ever get a brain.
/// </summary>
public sealed record EnemyArchetype(
    EnemyKind Kind,
    string Name,
    string Class,
    float Health,
    float Damage);

/// <summary>
/// The full roster shown on the test screen: the two enemies that really exist
/// today, followed by three unbuilt polygon spaceships kept around purely to see
/// how they read. Placeholder names so each is distinguishable at a glance.
/// </summary>
public static class EnemyCatalog
{
    public static readonly IReadOnlyList<EnemyArchetype> All = new[]
    {
        // Live enemies — numbers match the real sim (World.EnemyShotDamage = 12,
        // EnemyTank shield 3 / elite 5).
        new EnemyArchetype(EnemyKind.StandardTank, "GRINDER", "HUNTER", 3f, 12f),
        new EnemyArchetype(EnemyKind.EliteTank,    "REVENANT", "ELITE",  5f, 12f),

        // Placeholder polygon spaceships — silhouettes to test, stats to tune.
        new EnemyArchetype(EnemyKind.ShipInterceptor, "WRAITH-07",   "INTERCEPTOR", 2f,   8f),
        new EnemyArchetype(EnemyKind.ShipGunship,     "PALLBEARER",  "GUNSHIP",     6f,  16f),
        new EnemyArchetype(EnemyKind.ShipScout,       "NULL-9",      "SCOUT",       1.5f, 5f),

        // The Stalker boss — animated rig, shown so its phases can be scrubbed.
        new EnemyArchetype(EnemyKind.CrabCoreBoss,    "CRAB-CORE",   "STALKER",     40f, 20f),
    };
}
