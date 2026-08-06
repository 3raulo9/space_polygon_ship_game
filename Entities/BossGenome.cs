using Raylib_cs;
using Unrendered.Core;

namespace Unrendered.Entities;

/// <summary>
/// Which of the five the thing in front of you is descended from. Every DESCENT boss is a
/// distorted mirror of a chassis the player can climb into — the machine that builds the
/// player's craft is the same machine that builds these, and it has been left running.
///
/// The order matches <see cref="PlayerClass"/> deliberately, so the two can be cast across:
/// a lineage is nothing more than "what happened to this one".
/// </summary>
public enum BossLineage
{
    Bulwark,  // TANK — a siege chassis that never stopped being armoured
    Stalker,  // SPIDER — the Crab-Core grown past its own rig
    Wearer,   // VIRUS — no body of its own; it is wearing the last few
    Drowner,  // FISH — the Maw lineage, still swimming in air that is not water
    Column,   // SOLDIER — a person's shape at a size a person is not
}

/// <summary>
/// How a boss meets the ground, which is the single biggest thing about its silhouette. A
/// walker reads as an animal, a tread reads as a machine, and something that never touches
/// the grid at all reads as neither — and at 320×240, that read happens before any detail
/// of the body does.
/// </summary>
public enum Carriage
{
    Treads,   // two long plates, ground-bound, heavy
    Legs,     // splayed spider limbs, raised knees
    Hover,    // nothing underneath; it floats and bobs
    Biped,    // two long legs, upright, a person's proportions at the wrong scale
    Husks,    // a heap of dead hunters, walked on their own stolen legs
}

/// <summary>
/// One thing a boss can do to you. A genome takes three of these, and it is overwhelmingly
/// the loadout rather than the body that decides what a fight feels like — which is why the
/// pool is wide and the rolls are what get re-rolled every session.
///
/// Every entry here is required to have a <em>physical</em> telegraph: something that happens
/// in the world before the damage does. Nothing in this list is allowed to simply occur.
/// </summary>
public enum AttackModule
{
    Lance,    // winds a beam in the core, then a shaft that sweeps where you were
    Sweep,    // a low beam turned through a wide arc — you go over it or behind something
    Mortar,   // high lobbed shells; the shadow lands before the shell does
    Charge,   // squares up, holds, then crosses the arena in a straight line
    Summon,   // tears open and hunters walk out of it
    Slam,     // rears and drops; a ring of grid-shock travels outward
    Tether,   // throws a line, and reels in whatever it catches
    Spit,     // a fast flat volley, cheap and constant, the thing that keeps you moving
    Burrow,   // goes into the floor and comes up somewhere you are not looking
    Shroud,   // vents cover and stops being targetable inside it
    Bulwarks, // raises plates; frontal damage is refused until they are broken off
    Leap,     // an arc onto your head, with a long hang at the top of it
    Scream,   // no damage at all — staggers you and winds everything else up
    Snare,    // pins your feet to the grid for a beat, and then uses the beat
}

/// <summary>
/// The one modifier that colours the whole encounter, over and above the three attacks. Kept
/// to a single roll on purpose: two quirks stacked stops being a personality and starts being
/// a difficulty setting nobody chose.
/// </summary>
public enum Quirk
{
    None,
    Armoured,  // shed layers hold; each one has to be broken off before the next takes damage
    Rabid,     // every layer lost makes it faster and its cadence shorter
    Leech,     // heals off its own adds — kill what it summons or you are not making progress
    Phased,    // periodically stops being there at all, on a rhythm you can learn
    Split,     // its death is not the end of it
}

/// <summary>
/// The complete description of one boss, rolled from a seed and then never changed. Every
/// system that draws, fights or names this thing reads it from here, so the model on screen,
/// the hitbox, and the thing the HUD calls it can never disagree.
///
/// This is a <em>record</em>, and deliberately: a genome is the identity of one encounter. Two
/// bosses with the same seed are the same boss, which is what makes a fight reproducible in a
/// self-test and in a bug report, and what lets a capture harness photograph a specific one.
/// </summary>
public sealed record BossGenome(
    int Seed,
    BossLineage Lineage,
    string Name,
    string Epithet,

    // --- What it takes to kill ------------------------------------------------------
    // Layers are health bars AND shed armour: see ModularBoss. A herald has one, the
    // Colossus at the end of a descent has five, and each one that breaks physically comes
    // off the model.
    int Layers,
    float LayerHealth,

    // --- The body -------------------------------------------------------------------
    float Scale,
    Carriage Carriage,
    int LimbCount,
    float LimbLength,
    float BodyWidth,
    float BodyRise,
    float BodyDepth,
    int SpineCount,
    // One limb grown far past the others. Rare, and the single most memorable thing a
    // silhouette can have — you remember the one with the huge left arm.
    int OversizeLimb,

    // --- The paint ------------------------------------------------------------------
    // Indices into ClassCatalog.Swatches, not raw colours: the palette is a closed set and a
    // boss is not allowed to be the thing that breaks it. Stored as indices so a swatch
    // retune moves every boss ever rolled along with it.
    int ShellSwatch,
    int DeepSwatch,
    int LimbSwatch,
    // The living core. Its own tiny register — see BossGen.Cores — because "the one bright
    // wrong thing in the middle" is this game's whole convention for something alive, and it
    // is not up for grabs by an ordinary body swatch.
    int CoreTone,

    // --- What it does ---------------------------------------------------------------
    AttackModule[] Attacks,
    Quirk Quirk,

    // --- How it does it -------------------------------------------------------------
    // 0..1 across the board, so the entity can read them without knowing what rolled them.
    float Aggression,   // shorter gaps between attacks, faster wind-ups
    float Standoff,     // how far out it likes to sit, as a fraction of its band
    float GaitSpeed)
{
    /// <summary>
    /// How high the core sits in the body's own frame, before scale. The single number the
    /// entity and the renderer must never disagree about: the entity fires beams from it and
    /// the renderer draws the gem at it, and the first build of this had the two out of step —
    /// the core was placed at 0.9× the body's rise while the body itself ran to 1.55×, so on
    /// every lineage the one bright landmark on the whole silhouette was buried inside its own
    /// chest and simply never seen.
    ///
    /// <para>Sitting a little <em>above</em> the body's top rather than flush with it, because a
    /// core level with the shell is a core the shell occludes from half the bearings on the
    /// field, and the whole job of this thing is being visible from all of them.</para>
    /// </summary>
    public float CoreLocalY => BodyRise * 1.62f;

    /// <summary>
    /// How far the carriage lifts the body off the grid, before scale. A walker rides high on
    /// its legs; a tread sits in the dirt; a hovering body has no carriage at all and its own
    /// height instead.
    ///
    /// <para>Lives on the genome for the same reason <see cref="CoreLocalY"/> does: the entity
    /// places the body at it and the renderer has to build a carriage exactly that tall to fill
    /// the gap. The first build had this on the entity alone, and the biped's legs — which the
    /// renderer would have had to build to reach — simply were not there, so a COLUMN was a
    /// torso floating five metres over an empty patch of grid.</para>
    /// </summary>
    public float CarriageLift => Carriage switch
    {
        Carriage.Treads => 0.35f,
        Carriage.Legs => 2.4f * LimbLength,
        Carriage.Hover => 0f,
        Carriage.Biped => 3.6f * LimbLength,
        _ => 1.6f,
    };

    /// <summary>The shell colour, resolved. Everything drawing this boss goes through here
    /// rather than reaching into the swatch table itself.</summary>
    public Color Shell => ClassCatalog.SwatchColor(ShellSwatch);
    public Color Deep => ClassCatalog.SwatchColor(DeepSwatch);
    public Color Limb => ClassCatalog.SwatchColor(LimbSwatch);
    public Color Core => BossGen.Cores[((CoreTone % BossGen.Cores.Length) + BossGen.Cores.Length) % BossGen.Cores.Length];

    /// <summary>What the HUD prints over the health bars: "RABID BULWARK", "DROWNER, UNMADE".
    /// Built once here so the banner, the bar and the kill notice all say the same thing.</summary>
    public string FullName => Epithet.Length == 0 ? Name : $"{Epithet} {Name}";

    /// <summary>Total health across every layer — what the wave bar counts this thing as
    /// worth, and what a self-test measures a fight's length against.</summary>
    public float TotalHealth => Layers * LayerHealth;

    public bool Has(AttackModule m) => Array.IndexOf(Attacks, m) >= 0;
}

/// <summary>
/// The roller. Hands back a <see cref="BossGenome"/> for a seed, a lineage and a tier, and is
/// the only place in the game that decides what a boss <em>is</em>.
///
/// The whole point of this file is that a player should never be able to say "oh, this one
/// again". The arithmetic on that: five lineages, fourteen attack modules taken three at a
/// time (364 combinations, before the per-lineage weighting), six quirks, eight body swatches
/// in three slots, three core tones, and continuous rolls on scale, limb count, limb length
/// and the three behaviour traits. Whole numbers alone put it past a hundred thousand
/// distinguishable bosses; the continuous rolls mean two are essentially never identical.
///
/// Deterministic from the seed, always. A run seeds this from its own clock, but a capture or
/// a self-test can pin a seed and get the same monster back every time — which is the only way
/// to photograph or regression-test a thing that is defined by being different each time.
/// </summary>
public static class BossGen
{
    /// <summary>
    /// The core register. Not drawn from the ordinary swatch table: a core is the one bright
    /// wrong thing on a body in this game (the Crab's gem, the Maw's crystal, the Fish's lure,
    /// the Virus's payload), and the convention is worth more than the extra variety would be.
    /// Three tones, each already carrying meaning somewhere else in the game — so a green-cored
    /// boss reads as kin to the Maw before the briefing says so.
    /// </summary>
    public static readonly Color[] Cores =
    {
        Palette.NeonMagenta,  // the Crab-Core's register: a pilot light that should not be that bright
        Palette.NeonRed,      // the same thing having decided to kill you
        Palette.MawLaser,     // acid green — the Maw's line, and nothing else in the game's
    };

    /// <summary>
    /// The body swatches a boss may be painted from — the grim end of the shared table. The
    /// two neons are excluded on purpose: a whole carapace in neon magenta would drown the
    /// core, and the core outshining the body is the entire read.
    /// </summary>
    private static readonly int[] BodySwatches = { 0, 1, 2, 3, 6, 9, 10, 11 };

    /// <summary>And the ones a trim or a limb may take, which allows the two dirty accents the
    /// body proper does not — a jaundiced limb on an otherwise grey machine is exactly the kind
    /// of wrongness this game trades in.</summary>
    private static readonly int[] TrimSwatches = { 0, 1, 2, 3, 4, 5, 6, 9, 10, 11 };

    /// <summary>
    /// How many health bars a boss of each rank carries, and how much is behind each. A herald
    /// closes a wave and is meant to take a minute; the Colossus closes a planet and is meant
    /// to be the reason you remember which planet.
    /// </summary>
    public const int HeraldLayers = 1;
    public const int ColossusLayers = 5;

    /// <summary>
    /// What one layer of a herald is worth, on the game's shield-point scale — where a plain
    /// bolt is 1, a slug 3, a mortar 4 and a lance 9. Twenty-two puts a herald at roughly
    /// twenty landed bolts, or half that for a player working the exposure window, which comes
    /// out around a minute of fighting. Well above an elite hunter's five so it plainly is not
    /// one, and short of the Crab-Core's forty so a wave does not end in a siege.
    /// </summary>
    public const float HeraldLayerHealth = 22f;

    /// <summary>
    /// And one layer of the Colossus. Five of these is by a distance the biggest health pool in
    /// the game, which is the point of it — but the number is picked off the <em>bar</em>, not
    /// off the total: each individual layer has to fall inside a stretch a player can feel, or
    /// five bars reads as one wall rather than as five rounds of a fight. At thirty-eight, a bar
    /// is about the length of a whole herald, and the fight is five of those with the body
    /// changing between each.
    /// </summary>
    public const float ColossusLayerHealth = 38f;

    /// <summary>
    /// Rolls a herald: the stronger thing that closes out one wave. One layer, ordinary size,
    /// three attacks like everything else — a herald is not a small Colossus, it is a full
    /// boss that happens to die once instead of five times.
    /// </summary>
    public static BossGenome Herald(int seed, BossLineage lineage, float difficulty = 0f)
        => Roll(seed, lineage, HeraldLayers, HeraldLayerHealth * (1f + 0.35f * difficulty),
            scaleBand: (1.0f, 1.35f), difficulty);

    /// <summary>
    /// Rolls the Colossus: the thing at the bottom of a descent. Five layers, and half again
    /// the size of a herald — it has to be visibly a different order of object from across the
    /// arena, before any bar appears on the HUD.
    /// </summary>
    public static BossGenome Colossus(int seed, BossLineage lineage, float difficulty = 0f)
        => Roll(seed, lineage, ColossusLayers, ColossusLayerHealth * (1f + 0.25f * difficulty),
            scaleBand: (1.9f, 2.5f), difficulty);

    /// <summary>
    /// The roll proper. Everything about a boss lands here.
    ///
    /// <paramref name="difficulty"/> (0..1) is how deep into a descent this is. It never
    /// changes <em>what</em> can roll — a wave-one herald can be the nastiest thing in the pool
    /// and that is a good story — it only leans the continuous traits: a late boss is a little
    /// bigger, a little more aggressive, a little more likely to carry a quirk.
    /// </summary>
    public static BossGenome Roll(int seed, BossLineage lineage, int layers, float layerHealth,
        (float Lo, float Hi) scaleBand, float difficulty = 0f)
    {
        var rng = new Random(seed);
        difficulty = Math.Clamp(difficulty, 0f, 1f);

        Carriage carriage = CarriageFor(lineage, rng);
        int limbs = LimbsFor(carriage, rng);

        // Body proportions. Rolled around each lineage's own middle rather than around one
        // shared shape, so a BULWARK is always squat and a COLUMN is always tall no matter
        // what else the dice did — the lineage has to survive the randomisation or the five
        // of them collapse into one procedural blob with different attacks.
        (float w, float rise, float depth) = MassFor(lineage);
        float jitterW = 0.72f + rng.NextSingle() * 0.66f;
        float jitterH = 0.75f + rng.NextSingle() * 0.62f;
        float jitterD = 0.72f + rng.NextSingle() * 0.66f;

        float scale = scaleBand.Lo + rng.NextSingle() * (scaleBand.Hi - scaleBand.Lo);
        scale *= 1f + 0.12f * difficulty;

        // The one huge limb. About one boss in five, and never on something with fewer than
        // three limbs — an asymmetric biped is just a thing with a broken leg.
        int oversize = limbs >= 3 && rng.NextSingle() < 0.2f ? rng.Next(limbs) : -1;

        var attacks = RollAttacks(lineage, rng);
        Quirk quirk = RollQuirk(rng, difficulty, layers);

        int shell = Pick(BodySwatches, rng);
        int deep = rng.NextSingle() < 0.55f ? Pick(BodySwatches, rng) : shell;
        int limb = rng.NextSingle() < 0.35f ? Pick(TrimSwatches, rng) : shell;
        int core = CoreFor(lineage, rng);

        string name = NameFor(lineage, layers);
        string epithet = EpithetFor(quirk, attacks, rng);

        return new BossGenome(
            Seed: seed,
            Lineage: lineage,
            Name: name,
            Epithet: epithet,
            Layers: layers,
            LayerHealth: layerHealth,
            Scale: scale,
            Carriage: carriage,
            LimbCount: limbs,
            LimbLength: 0.75f + rng.NextSingle() * 0.85f,
            BodyWidth: w * jitterW,
            BodyRise: rise * jitterH,
            BodyDepth: depth * jitterD,
            SpineCount: rng.NextSingle() < 0.45f ? 2 + rng.Next(6) : 0,
            OversizeLimb: oversize,
            ShellSwatch: shell,
            DeepSwatch: deep,
            LimbSwatch: limb,
            CoreTone: core,
            Attacks: attacks,
            Quirk: quirk,
            Aggression: Math.Clamp(0.2f + rng.NextSingle() * 0.6f + 0.2f * difficulty, 0f, 1f),
            Standoff: 0.15f + rng.NextSingle() * 0.8f,
            GaitSpeed: 0.55f + rng.NextSingle() * 0.75f);
    }

    // --- The body ------------------------------------------------------------------

    /// <summary>
    /// How this lineage stands. Mostly settled by descent — a DROWNER floats because the FISH
    /// swims — but each has one alternative it is allowed to roll into, so the silhouette can
    /// still surprise. A STALKER that came up on treads is a genuinely unsettling object.
    /// </summary>
    private static Carriage CarriageFor(BossLineage lineage, Random rng)
    {
        bool odd = rng.NextSingle() < 0.18f;
        return lineage switch
        {
            BossLineage.Bulwark => odd ? Carriage.Legs : Carriage.Treads,
            BossLineage.Stalker => odd ? Carriage.Biped : Carriage.Legs,
            BossLineage.Wearer  => odd ? Carriage.Legs : Carriage.Husks,
            BossLineage.Drowner => odd ? Carriage.Legs : Carriage.Hover,
            _                   => odd ? Carriage.Hover : Carriage.Biped,
        };
    }

    private static int LimbsFor(Carriage carriage, Random rng) => carriage switch
    {
        Carriage.Treads => rng.Next(2, 5),   // gun mounts and arms, not walking limbs
        Carriage.Legs => 4 + 2 * rng.Next(0, 3),  // 4, 6 or 8 — always even, always splayed
        Carriage.Hover => rng.Next(0, 5),    // trailing tendrils; a bare hovering slab is allowed
        Carriage.Biped => 2,                 // two arms, on top of the two it stands on
        _ => rng.Next(3, 7),                 // a heap of husks, each still owning its own limbs
    };

    /// <summary>The middle proportions of each lineage, before the per-boss jitter. This table
    /// is the whole reason five procedural monsters still read as five species.</summary>
    private static (float Width, float Rise, float Depth) MassFor(BossLineage lineage) => lineage switch
    {
        BossLineage.Bulwark => (3.4f, 1.8f, 4.2f),  // wide, low, long — a thing that pushes
        BossLineage.Stalker => (2.6f, 2.0f, 2.6f),  // a carapace: roughly round, riding high
        BossLineage.Wearer  => (2.2f, 3.2f, 2.2f),  // a stack, taller than it is broad
        BossLineage.Drowner => (1.9f, 1.6f, 5.0f),  // long and thin — a body built to move through
        // A person's proportions, at several times a person. Pulled in from the 4.2 it was first
        // built at: a COLUMN Colossus came out twenty-six units tall — two thirds of a tower —
        // and at any range you could actually fight it at, its head and its core were off the top
        // of the screen. Three is still half again the tallest walker on the roster.
        _                   => (1.7f, 3.0f, 1.5f),
    };

    // --- The paint -----------------------------------------------------------------

    /// <summary>Which core tone this lineage leans toward. Never fixed — every lineage can roll
    /// every tone — but a DROWNER is mostly acid green because it is the Maw's descendant, and
    /// that inheritance should be legible at a glance.</summary>
    private static int CoreFor(BossLineage lineage, Random rng)
    {
        float r = rng.NextSingle();
        return lineage switch
        {
            BossLineage.Drowner => r < 0.6f ? 2 : (r < 0.8f ? 0 : 1),
            BossLineage.Stalker => r < 0.55f ? 0 : (r < 0.85f ? 1 : 2),
            BossLineage.Wearer  => r < 0.45f ? 0 : (r < 0.75f ? 2 : 1),
            _                   => r < 0.5f ? 1 : (r < 0.8f ? 0 : 2),
        };
    }

    // --- What it does --------------------------------------------------------------

    /// <summary>
    /// Three attacks, no duplicates. The first is the lineage's signature and is always
    /// present — it is what makes a STALKER recognisably a STALKER even when the other two
    /// rolls are wild — and the remaining two come from a weighted pool that leans toward
    /// what suits the body without ever excluding anything.
    ///
    /// Not excluding anything is deliberate. A hovering DROWNER that rolled CHARGE is a fish
    /// that rams you, which is ridiculous and excellent, and the fights people remember are
    /// the ones where the dice did something the designer would not have.
    /// </summary>
    private static AttackModule[] RollAttacks(BossLineage lineage, Random rng)
    {
        var picked = new List<AttackModule> { SignatureOf(lineage) };
        var pool = WeightedPool(lineage);

        while (picked.Count < 3)
        {
            AttackModule m = pool[rng.Next(pool.Length)];
            if (!picked.Contains(m)) picked.Add(m);
        }

        // Shuffle so the signature is not always the one it opens with — the order here is
        // the order the entity cycles them, and a boss that always leads with the same move
        // is a boss you have already learned by the second wave.
        for (int i = picked.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (picked[i], picked[j]) = (picked[j], picked[i]);
        }
        return picked.ToArray();
    }

    private static AttackModule SignatureOf(BossLineage lineage) => lineage switch
    {
        BossLineage.Bulwark => AttackModule.Mortar,   // the TANK's own right mouse button
        BossLineage.Stalker => AttackModule.Lance,    // the Crab-Core's beam, inherited whole
        BossLineage.Wearer  => AttackModule.Summon,   // it is made of bodies; it can spare some
        BossLineage.Drowner => AttackModule.Leap,     // the Maw's dive, at this size
        _                   => AttackModule.Tether,   // the SOLDIER's cable, thrown at you
    };

    /// <summary>
    /// The draw bag. Entries repeat to weight them — a plain array with duplicates rather than
    /// a weight table, because at this size the array is easier to read and impossible to get
    /// subtly wrong.
    /// </summary>
    private static AttackModule[] WeightedPool(BossLineage lineage)
    {
        // Everything, once. This is the floor: any boss can roll any move.
        var all = (AttackModule[])Enum.GetValues<AttackModule>().Clone();
        var bag = new List<AttackModule>(all);

        // And then the ones that suit this body again, so they come up more often.
        bag.AddRange(lineage switch
        {
            BossLineage.Bulwark => new[]
            {
                AttackModule.Charge, AttackModule.Bulwarks, AttackModule.Slam,
                AttackModule.Spit, AttackModule.Charge, AttackModule.Bulwarks,
            },
            BossLineage.Stalker => new[]
            {
                AttackModule.Sweep, AttackModule.Snare, AttackModule.Slam,
                AttackModule.Burrow, AttackModule.Sweep, AttackModule.Snare,
            },
            BossLineage.Wearer => new[]
            {
                AttackModule.Summon, AttackModule.Shroud, AttackModule.Spit,
                AttackModule.Scream, AttackModule.Summon, AttackModule.Shroud,
            },
            BossLineage.Drowner => new[]
            {
                AttackModule.Spit, AttackModule.Burrow, AttackModule.Leap,
                AttackModule.Shroud, AttackModule.Spit, AttackModule.Leap,
            },
            _ => new[]
            {
                AttackModule.Tether, AttackModule.Leap, AttackModule.Scream,
                AttackModule.Snare, AttackModule.Tether, AttackModule.Mortar,
            },
        });
        return bag.ToArray();
    }

    /// <summary>
    /// The one modifier. About half of heralds carry none at all, which matters: a quirk is
    /// only special if some bosses are plain, and a run where every single fight has a gimmick
    /// is a run where none of them do. The Colossus always carries one.
    /// </summary>
    private static Quirk RollQuirk(Random rng, float difficulty, int layers)
    {
        if (layers < BossGen.ColossusLayers && rng.NextSingle() > 0.35f + 0.35f * difficulty)
            return Quirk.None;

        // SPLIT is withheld from the Colossus: a five-bar boss that then becomes two more
        // bosses is not a climax, it is a chore, and the ending of a descent has to land.
        int span = layers >= ColossusLayers ? 4 : 5;
        return (Quirk)(1 + rng.Next(span));
    }

    // --- The name ------------------------------------------------------------------
    // A rolled boss needs a rolled name or the HUD says "BOSS" five times a run and every one
    // of them blurs into the last. The naming is deliberately terse and a bit liturgical —
    // this game's register — and it is built from what actually rolled, so a name is
    // information: RABID tells you it speeds up, SEALED tells you the plates hold.

    private static string NameFor(BossLineage lineage, int layers)
    {
        string stem = lineage switch
        {
            BossLineage.Bulwark => "BULWARK",
            BossLineage.Stalker => "STALKER",
            BossLineage.Wearer => "WEARER",
            BossLineage.Drowner => "DROWNER",
            _ => "COLUMN",
        };
        // The last thing on a planet is not a bulwark, it is THE bulwark.
        return layers >= ColossusLayers ? $"THE {stem}" : stem;
    }

    private static string EpithetFor(Quirk quirk, AttackModule[] attacks, Random rng)
    {
        // The quirk names itself where it has one — that is the whole point of putting it in
        // the name, so a player can read "LEECHING" and know to kill the adds.
        string fromQuirk = quirk switch
        {
            Quirk.Armoured => "SEALED",
            Quirk.Rabid => "RABID",
            Quirk.Leech => "LEECHING",
            Quirk.Phased => "UNTRUE",
            Quirk.Split => "TWINNED",
            _ => "",
        };
        if (fromQuirk.Length > 0) return fromQuirk;

        // Nothing to warn about, so the name takes its flavour from what it carries instead.
        string[] plain =
        {
            "DIM", "SPENT", "HOLLOW", "PATIENT", "COLD", "WRETCHED", "LATE", "MUTE",
        };
        if (Array.IndexOf(attacks, AttackModule.Burrow) >= 0) return "BURIED";
        if (Array.IndexOf(attacks, AttackModule.Summon) >= 0) return "TEEMING";
        if (Array.IndexOf(attacks, AttackModule.Scream) >= 0) return "SHRIEKING";
        return plain[rng.Next(plain.Length)];
    }

    private static int Pick(int[] from, Random rng) => from[rng.Next(from.Length)];

    /// <summary>
    /// The five lineages in a shuffled order for one descent, so a run meets each of them
    /// exactly once across its five fights and never in the same sequence twice. This is what
    /// makes "based off the five classes" a structure rather than a coincidence — you will see
    /// all five in a session, and you will not know which is next.
    /// </summary>
    public static BossLineage[] ShuffledLineages(int seed)
    {
        var rng = new Random(seed);
        var order = (BossLineage[])Enum.GetValues<BossLineage>().Clone();
        for (int i = order.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        return order;
    }
}
