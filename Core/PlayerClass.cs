using Raylib_cs;

namespace VoidTanks.Core;

/// <summary>
/// What the player climbs into. The machine offers five chassis and admits, on the
/// same screen, that it can only still assemble two of them — the rest are listed
/// because the manifest says they exist, not because anything is left to build.
/// </summary>
public enum PlayerClass
{
    Tank,     // the original craft: the game as it has always played
    Spider,   // a player-sized Crab-Core, lasers and a charged lance
    Virus,    // offline
    Fish,     // offline
    Soldier,  // offline
}

/// <summary>
/// One chassis on the select screen: what it is called, what it does, whether the
/// machine can still build it, and which of its parts take their own colour. Kept as
/// plain data so the screen (state) and the renderer (drawing) both read the same
/// description and can never disagree about how many colourable parts a class has.
/// </summary>
public sealed record ClassArchetype(
    PlayerClass Kind,
    string Name,
    string Tagline,
    string[] Lines,
    bool Available,
    string[] PartNames,
    int[] DefaultSwatches);

/// <summary>
/// The roster the class-select screen walks. Order here is the order shown.
/// </summary>
public static class ClassCatalog
{
    public static readonly IReadOnlyList<ClassArchetype> All = new[]
    {
        new ClassArchetype(
            PlayerClass.Tank, "TANK",
            "STANDARD CHASSIS",
            new[]
            {
                "THE CRAFT YOU HAVE ALWAYS DRIVEN. HEAVY, SLOW TO",
                "TURN, AND HONEST ABOUT IT. CANNON ON THE LEFT",
                "TRIGGER, HEAVY GRENADE ON THE RIGHT.",
            },
            Available: true,
            PartNames: new[] { "HULL", "CAP", "BARREL" },
            // Slate body, slate cap, cold chrome gun.
            DefaultSwatches: new[] { 1, 1, 0 }),

        new ClassArchetype(
            PlayerClass.Spider, "SPIDER",
            "SALVAGED CRAB-CORE",
            new[]
            {
                "A CRAB-CORE CUT DOWN TO YOUR SIZE. THE RED CORE",
                "IN THE MIDDLE IS THE WEAK POINT - AND IT IS YOURS.",
                "LEFT THROWS LASERS. RIGHT WINDS THE LANCE: HOLD",
                "IT TO FILL THE METER. YOU CANNOT MOVE WHILE IT DOES.",
            },
            Available: true,
            PartNames: new[] { "CARAPACE", "BASE", "LEGS", "CORE" },
            // Gunmetal shell over a neon-red core.
            DefaultSwatches: new[] { 10, 10, 10, 8 }),

        new ClassArchetype(
            PlayerClass.Virus, "VIRUS",
            "NO BUILD DATA",
            new[] { "THE MACHINE HAS NO PATTERN FOR THIS CHASSIS." },
            Available: false,
            PartNames: Array.Empty<string>(),
            DefaultSwatches: Array.Empty<int>()),

        new ClassArchetype(
            PlayerClass.Fish, "FISH",
            "NO BUILD DATA",
            new[] { "THE MACHINE HAS NO PATTERN FOR THIS CHASSIS." },
            Available: false,
            PartNames: Array.Empty<string>(),
            DefaultSwatches: Array.Empty<int>()),

        new ClassArchetype(
            PlayerClass.Soldier, "SOLDIER",
            "TWIN CABLE RIG",
            new[]
            {
                "NO CHASSIS - A PERSON ON FOOT WITH TWO GAS-FIRED",
                "GRAPPLING HOOKS. SPACE JUMPS, MOUSE LOOKS, E AND Q",
                "THROW AND RELEASE THE HOOKS. ANCHORED, WASD IS",
                "CABLE TENSION. LEFT SHOOTS, RIGHT ROCKETS. PACK-F",
            },
            Available: true,
            PartNames: new[] { "FATIGUES", "HARNESS", "LAUNCHERS", "CABLE" },
            // Deep field-drab under a gunmetal harness, with the steel left bare: this
            // is the only chassis on the roster that is a person, and it should read as
            // equipment worn rather than as a machine painted.
            DefaultSwatches: new[] { 6, 10, 0, 9 }),
    };

    public static ClassArchetype Get(PlayerClass kind) => All[(int)kind];

    /// <summary>
    /// The colours a part can be painted. Deliberately drawn from the game's own grim
    /// register plus the two neons — nothing here is a colour the void wouldn't allow.
    /// Indices into this table are what a <see cref="Loadout"/> actually stores, so a
    /// saved paint job survives a palette tweak.
    /// </summary>
    public static readonly (string Name, Color Color)[] Swatches =
    {
        ("CHROME",  Palette.HudChrome),
        ("SLATE",   Palette.PlayerFill),
        ("RUST",    Palette.EnemyFill),
        ("BRUISE",  Palette.EliteFill),
        ("JAUNDICE",Palette.Flag),
        ("TEAL",    Palette.GridNear),
        ("DEEP",    Palette.BatteryFill),
        ("MAGENTA", Palette.NeonMagenta),
        ("RED",     Palette.NeonRed),
        ("BONE",    Palette.MawTooth),
        ("GUNMETAL",Palette.CrabChassis),
        ("ROT",     Palette.MawShell),
    };

    public static Color SwatchColor(int index)
        => Swatches[((index % Swatches.Length) + Swatches.Length) % Swatches.Length].Color;

    public static string SwatchName(int index)
        => Swatches[((index % Swatches.Length) + Swatches.Length) % Swatches.Length].Name;
}
