using Raylib_cs;

namespace VoidTanks.Core;

/// <summary>
/// What the player climbs into. The machine offers five chassis, and with the VIRUS
/// finally loaded the whole manifest can now be assembled — though calling that one
/// "assembled" is generous: it is the entry with no pattern, because it has no body.
/// It takes one.
/// </summary>
public enum PlayerClass
{
    Tank,     // the original craft: the game as it has always played
    Spider,   // a player-sized Crab-Core, lasers and a charged lance
    Virus,    // a naked mote that wears hunters as rotting armour
    Fish,     // the drowned swimmer: the void as an ocean
    Soldier,  // a person on foot with two gas-fired grappling hooks
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
                "HEAVY, HONEST, AND TOO HEAVY TO JUMP - THE",
                "TREADS OWN THE GRID. SPACE DIGS IN TO CRANE",
                "AND SHRUG SHOTS; Q LURCHES, E SMOKES, R SLUGS",
                "THROUGH COVER. RAM. LEFT CANNON, RIGHT MORTAR.",
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
            "NO CHASSIS OF ITS OWN",
            new[]
            {
                "THE MACHINE HAS NO PATTERN FOR THIS ONE - IT IS NOT",
                "A THING TO BUILD. YOU SPAWN AS A NAKED MOTE, ONE HIT",
                "FROM DEAD. FLY INTO A HUNTER TO WEAR IT. LEFT FIRES.",
                "RIGHT SPENDS THE ROTTING HOST AS A BOMB. KEEP HOPPING.",
            },
            Available: true,
            // No hull to paint, so the parts are the infection itself: the mote's own
            // core, the veins that crawl over every stolen body, the husk of whatever it
            // is wearing, and the payload glow. The tint travels with you onto every host,
            // which is the whole convention — you always know your own rot.
            PartNames: new[] { "MOTE", "VEINS", "HUSK", "PAYLOAD" },
            // Diseased register, kin to the Maw-Core's rot: a magenta mote over a bruised
            // vein, a dead-green husk, and the one bright neon-red payload for the living
            // core — this game's whole convention for a thing that is code, not matter.
            DefaultSwatches: new[] { 7, 3, 11, 8 }),

        new ClassArchetype(
            PlayerClass.Fish, "FISH",
            "DROWNED SWIMMER",
            new[]
            {
                "THE VOID IS AN OCEAN AND THIS IS THE ONLY THING",
                "LEFT THAT KNOWS IT. W BEATS THE TAIL - A RHYTHM,",
                "NOT A THROTTLE. A AND D ROLL, AND A ROLLED BODY",
                "CARVES. LEFT SPITS. RIGHT STRIKES. NEVER LAND.",
            },
            Available: true,
            PartNames: new[] { "HIDE", "FINS", "BELLY", "LURE" },
            // Kin to the Maw-Core, and the palette says so before the briefing does: the
            // same diseased shell, the same old bone. The lure is the one bright thing on
            // it, which is this game's whole convention for a living core.
            //
            // The fins are the dark one and the belly is the pale one, which is the
            // opposite of the obvious assignment and matters at 320 pixels across: fins are
            // thin membranes seen edge-on, so in a light colour they read as beige lumps
            // stuck to the animal, while the underside is a broad flat surface and pale is
            // exactly what countershading looks like on one. Bone on the belly also gets
            // the teeth for free.
            DefaultSwatches: new[] { 11, 6, 9, 7 }),

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
