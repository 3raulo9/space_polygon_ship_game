using System.Numerics;
using Unrendered.Entities;

namespace Unrendered.Core;

/// <summary>
/// Headless checks for DESCENT: the run director, the rolled bosses, and the seam that keeps
/// the mode from quietly becoming SANDBOX with a bar over it.
///
/// <para>None of this needs a window, a renderer or an audio device — which is the whole reason
/// <see cref="IDescentField"/> exists. A full five-wave run, all four heralds and the Colossus,
/// is driven here against a stub field in a few milliseconds. A mode that could only be
/// exercised by playing it for half an hour is a mode nobody would ever dare change.</para>
/// </summary>
public static partial class SelfTest
{
    /// <summary>Registered from <see cref="Run"/>. Kept as its own list so the DESCENT block
    /// reads as a block in the output.</summary>
    private static int RunDescentChecks()
    {
        int failures = 0;
        failures += Check("DESCENT turns the sandbox's spawn director off", DescentSilencesTheDirector);
        failures += Check("a wave is an exact roster, counted down to the last body", WaveIsAnExactRoster);
        failures += Check("a wave feeds in against a live cap rather than all at once", WaveFeedsAgainstItsCap);
        failures += Check("the crowd is followed by a herald, and the herald by a break", WavesRunIntoHeraldsAndBreaks);
        failures += Check("a whole descent reaches the Colossus and clears", AWholeDescentCanBeCleared);
        failures += Check("every boss fight hands out a fragment, and somebody wears it", EveryBossGivesAFragment);
        failures += Check("holding READY cuts the salvage window short", ReadyEndsTheBreakEarly);
        failures += Check("a run meets all five lineages, in a shuffled order", RunMeetsAllFiveLineages);
        failures += Check("the same seed rolls the same boss; a different one does not", GenomesAreSeededAndVaried);
        failures += Check("a boss is only ever painted from the closed palette", BossesStayInThePalette);
        failures += Check("layers break one at a time and overkill does not cascade", LayersBreakOneAtATime);
        failures += Check("a boss is worth double in the beat after it commits", ExposureDoublesDamage);
        failures += Check("plates refuse a frontal hit and never a flanking one", PlatesRefuseTheFront);
        failures += Check("a five-layer Colossus sheds its plates as it dies", ColossusShedsItsLayers);
        failures += Check("a boss telegraphs before every single thing it does", NothingHappensWithoutAWindUp);
        return failures;
    }

    // --- A stub field ----------------------------------------------------------------

    /// <summary>
    /// A world's worth of behaviour with no world behind it. Counts what the director asked
    /// for and hands back the numbers it asks about, so a whole run can be driven at any
    /// timescale without a single entity existing.
    /// </summary>
    private sealed class StubField : IDescentField
    {
        public int Hunters, Elites, Squads, BossesRaised, SalvageDropped, Sweeps;
        public readonly List<string> Lines = new();
        public readonly List<BossGenome> Raised = new();

        /// <summary>How many wave units are notionally standing. The tests drive this by hand,
        /// which is exactly the point — the director must react to the field, not assume it.</summary>
        public int Live;
        public bool BossUp;

        public void SpawnWaveHunter(bool elite) { Hunters++; if (elite) Elites++; Live++; }
        public void SpawnWaveSquad() { Squads++; Live += 4; }
        public void RaiseBoss(BossGenome gene) { BossesRaised++; Raised.Add(gene); BossUp = true; }
        public int LiveWaveUnits => Live;
        public bool BossAlive => BossUp;
        public void DropSalvage(int count) => SalvageDropped += count;
        public void SweepStragglers() { Sweeps++; Live = 0; }
        public void Announce(string line) => Lines.Add(line);
        public void Signal(Cue id, float param = 0f) { }

        /// <summary>Kills everything on the field and books it against the wave, the way the
        /// world's own damage path does.</summary>
        public void WipeTheField(Descent run)
        {
            while (Live > 0) { Live--; run.CountKill(); }
        }
    }

    /// <summary>Steps a run for a stretch of seconds at a fixed tick.</summary>
    private static void Spin(Descent run, StubField field, float seconds, bool ready = false)
    {
        const float dt = 1f / 30f;
        for (float t = 0f; t < seconds; t += dt) run.Update(dt, field, ready);
    }

    // --- The seam --------------------------------------------------------------------

    /// <summary>
    /// The one failure mode that would ruin the whole mode: SANDBOX's director topping the field
    /// up while a wave bar counts down. A DESCENT world must open with nothing hostile on it and
    /// the director switched off, and a SANDBOX world must be exactly what it always was.
    /// </summary>
    private static string? DescentSilencesTheDirector()
    {
        var descent = new World.World(null, new MatchSettings
        {
            MaxPlayers = 1, Mode = GameMode.Descent, Destination = PlanetId.Verene,
        });
        if (!descent.IsDescent) return "a DESCENT world did not stand a run up";
        if (descent.DynamicSpawning) return "a DESCENT world left the sandbox's spawn director running";
        if (descent.Enemies.Count != 0) return "a DESCENT world opened with a hunter already on it";
        if (descent.Run!.Phase != DescentPhase.Landing) return "a descent did not open on the landing";
        if (descent.Run.Destination != PlanetId.Verene) return "the run was not bound to the chosen world";

        // ...and nothing about SANDBOX moved.
        var sandbox = new World.World(null, new MatchSettings { MaxPlayers = 1 });
        if (sandbox.IsDescent) return "a SANDBOX world stood a run up";
        if (!sandbox.DynamicSpawning) return "a SANDBOX world lost its spawn director";
        if (sandbox.Enemies.Count == 0) return "a SANDBOX world stopped opening with a hunter";
        if (sandbox.Bosses.Count != 0) return "a SANDBOX world raised a rolled boss";
        return null;
    }

    // --- The waves --------------------------------------------------------------------

    private static string? WaveIsAnExactRoster()
    {
        var run = new Descent(PlanetId.Solune, seed: 4242);
        var field = new StubField();

        Spin(run, field, Descent.LandingLength + 0.2f);
        if (run.Phase != DescentPhase.Wave) return "the landing did not open onto a wave";
        if (run.WaveTotal != Descent.WaveSizes[0])
            return $"wave one was {run.WaveTotal} contacts rather than {Descent.WaveSizes[0]}";
        if (Math.Abs(run.WaveFraction - 1f) > 0.001f) return "the wave bar did not open full";

        // Feed it in and kill it off. The bar has to fall monotonically and finish empty.
        float last = run.WaveFraction;
        for (int i = 0; i < 400 && run.Phase == DescentPhase.Wave; i++)
        {
            Spin(run, field, 1.2f);
            if (field.Live > 0) { field.Live--; run.CountKill(); }
            if (run.WaveFraction > last + 0.001f) return "the wave bar went back up";
            last = run.WaveFraction;
        }

        if (run.Killed != Descent.WaveSizes[0])
            return $"the wave counted {run.Killed} kills for a roster of {Descent.WaveSizes[0]}";
        if (field.Hunters + field.Squads * 4 != Descent.WaveSizes[0])
            return "the roster sent did not add up to the wave's size";
        return null;
    }

    /// <summary>
    /// A wave of fifty is fifty hunters' worth of fighting, not fifty hunters in view. The cap is
    /// what makes the big waves playable at all, and it must never be exceeded however long the
    /// player takes to clear them.
    /// </summary>
    private static string? WaveFeedsAgainstItsCap()
    {
        var run = new Descent(PlanetId.Abysse, seed: 77);
        var field = new StubField();
        Spin(run, field, Descent.LandingLength + 0.2f);

        int cap = Descent.LiveCaps[0];
        int peak = 0;
        // Never kill anything: the field fills to the cap and then the director must stop.
        for (int i = 0; i < 300; i++)
        {
            Spin(run, field, 1f);
            peak = Math.Max(peak, field.Live);
        }
        if (peak == 0) return "nothing was ever fed onto the field";
        // A squad costs four at once, so the cap can be overshot by at most three.
        if (peak > cap + 3) return $"{peak} units were on the field against a cap of {cap}";
        if (run.Phase != DescentPhase.Wave) return "the wave ended without anything being killed";
        return null;
    }

    private static string? WavesRunIntoHeraldsAndBreaks()
    {
        var run = new Descent(PlanetId.Thalos, seed: 9);
        var field = new StubField();
        Spin(run, field, Descent.LandingLength + 0.2f);

        ClearOneWave(run, field);
        if (run.Phase != DescentPhase.Herald) return "a spent wave did not raise a herald";
        if (field.BossesRaised != 1) return "no boss was raised for the herald";
        if (run.BossGene is null) return "the herald has no genome";
        if (run.BossGene.Layers != BossGen.HeraldLayers)
            return $"a herald carried {run.BossGene.Layers} layers rather than {BossGen.HeraldLayers}";

        // Kill it, and the run has to hold still for a beat before the break opens.
        field.BossUp = false;
        run.AwardFragment(0);
        Spin(run, field, Descent.AfterBossPause + 0.2f);
        if (run.Phase != DescentPhase.Intermission) return "a dead herald did not open the break";
        if (run.Wave != 2) return "the run did not advance to wave two";
        if (field.SalvageDropped == 0) return "the break opened with no salvage in it";
        if (run.Clock > Descent.IntermissionLength + 0.01f)
            return "the break ran longer than it is allowed to";

        Spin(run, field, Descent.IntermissionLength + 0.5f);
        if (run.Phase != DescentPhase.Wave) return "the break did not open onto the next wave";
        if (run.WaveTotal != Descent.WaveSizes[1]) return "wave two was not the second roster";
        return null;
    }

    /// <summary>Drives a whole crowd to nothing: feed it in, kill it off, repeat until the
    /// director moves on.</summary>
    private static void ClearOneWave(Descent run, StubField field)
    {
        for (int i = 0; i < 4000 && run.Phase == DescentPhase.Wave; i++)
        {
            Spin(run, field, 1.2f);
            field.WipeTheField(run);
        }
    }

    // --- The whole run ------------------------------------------------------------------

    /// <summary>
    /// The mode, end to end: four crowds, four heralds, four breaks, and the thing at the bottom
    /// of the planet. If this passes, DESCENT is a game that can be finished.
    /// </summary>
    private static string? AWholeDescentCanBeCleared()
    {
        var (run, field, err) = DriveAFullRun(seed: 31337);
        if (err is not null) return err;
        if (run.Phase != DescentPhase.Cleared) return $"a full run ended on {run.Phase}, not CLEAR";
        if (field.BossesRaised != Descent.WaveCount)
            return $"{field.BossesRaised} bosses were raised across a run of {Descent.WaveCount} fights";

        // The last one is the Colossus and is a different order of object from the four before.
        BossGenome last = field.Raised[^1];
        if (last.Layers != BossGen.ColossusLayers)
            return $"the Colossus carried {last.Layers} layers rather than {BossGen.ColossusLayers}";
        if (last.Quirk == Quirk.None) return "the Colossus rolled no quirk at all";
        if (last.Quirk == Quirk.Split) return "the Colossus rolled TWINNED, which it is barred from";
        for (int i = 0; i < field.Raised.Count - 1; i++)
            if (field.Raised[i].Scale >= last.Scale)
                return "a herald rolled no smaller than the Colossus";
        return null;
    }

    /// <summary>Runs a descent from the drop to whatever ends it, killing everything the moment
    /// it appears. Shared by the tests that care about the whole arc.</summary>
    private static (Descent Run, StubField Field, string? Error) DriveAFullRun(int seed)
    {
        var run = new Descent(PlanetId.Kirene, seed);
        var field = new StubField();
        int seat = 0;

        for (int guard = 0; guard < 20000 && !run.Finished; guard++)
        {
            Spin(run, field, 1.2f);
            field.WipeTheField(run);
            if (field.BossUp)
            {
                // Killing a boss is two things in the world — the entity dies and the fragment
                // is awarded — so the stub does both, in that order.
                field.BossUp = false;
                run.AwardFragment(seat);
                seat = (seat + 1) % 2;
            }
        }
        if (!run.Finished) return (run, field, "a full run never finished");
        return (run, field, null);
    }

    private static string? EveryBossGivesAFragment()
    {
        var (run, _, err) = DriveAFullRun(seed: 5150);
        if (err is not null) return err;

        int total = 0;
        for (int seat = 0; seat < 4; seat++) total += run.SunsOf(seat) + run.MoonsOf(seat);
        if (total != Descent.WaveCount)
            return $"{total} fragments came out of a run with {Descent.WaveCount} boss fights";

        // Whoever took one is wearing it, and whoever did not is wearing nothing — a tag that
        // everybody has is not a tag.
        bool anyTagged = false, anyBare = false;
        for (int seat = 0; seat < 4; seat++)
        {
            bool has = run.Carrying(seat);
            if (has && run.TagFor(seat).Length == 0) return "a carrier has no tag to wear";
            if (!has && run.TagFor(seat).Length != 0) return "somebody carrying nothing has a tag";
            anyTagged |= has;
            anyBare |= !has;
        }
        if (!anyTagged) return "nobody ended the run carrying anything";
        if (!anyBare) return "every seat in the roster was handed a fragment";

        // Fifty-fifty, with no memory: over many runs both kinds have to actually turn up.
        int suns = 0, moons = 0;
        for (int s = 0; s < 60; s++)
        {
            var r = new Descent(PlanetId.Solune, s);
            for (int i = 0; i < 5; i++)
                if (r.AwardFragment(0) == Fragment.Sun) suns++; else moons++;
        }
        if (suns == 0 || moons == 0) return "only one of the two fragments is ever handed out";
        if (suns < 90 || moons < 90) return $"the 50/50 came out {suns}/{moons} over 300 draws";
        return null;
    }

    private static string? ReadyEndsTheBreakEarly()
    {
        var run = new Descent(PlanetId.Verene, seed: 12);
        var field = new StubField();
        Spin(run, field, Descent.LandingLength + 0.2f);
        ClearOneWave(run, field);
        field.BossUp = false;
        run.AwardFragment(0);
        Spin(run, field, Descent.AfterBossPause + 0.2f);
        if (run.Phase != DescentPhase.Intermission) return "the run did not reach a break";

        // A tap does nothing: the hold has to be held.
        Spin(run, field, 0.2f, ready: true);
        Spin(run, field, 0.6f, ready: false);
        if (run.Phase != DescentPhase.Intermission) return "a tap of READY ended the break";
        if (run.Ready > 0.01f) return "the READY hold did not drain when the key came up";

        float before = run.Clock;
        Spin(run, field, Descent.ReadyHold + 0.3f, ready: true);
        if (run.Phase != DescentPhase.Wave) return "holding READY did not end the break";
        if (before < Descent.IntermissionLength * 0.5f)
            return "the break had nearly run out anyway, so this proved nothing";
        return null;
    }

    // --- The bosses ---------------------------------------------------------------------

    private static string? RunMeetsAllFiveLineages()
    {
        // One run must meet each of the five exactly once — that is what makes "a boss per
        // class" a structure rather than a coincidence.
        var seen = new HashSet<BossLineage>();
        var run = new Descent(PlanetId.Solune, seed: 606);
        for (int w = 1; w <= Descent.WaveCount; w++) seen.Add(run.LineageOf(w));
        if (seen.Count != 5) return $"a single run met only {seen.Count} of the five lineages";

        // And the order has to actually move between runs, or the fifth fight is always the
        // same family and the shuffle is decorative.
        var first = new List<BossLineage>();
        bool moved = false;
        for (int s = 0; s < 40; s++)
        {
            var r = new Descent(PlanetId.Solune, s);
            var order = new List<BossLineage>();
            for (int w = 1; w <= Descent.WaveCount; w++) order.Add(r.LineageOf(w));
            if (first.Count == 0) first = order;
            else if (!order.SequenceEqual(first)) moved = true;
        }
        return moved ? null : "every run drew the five lineages in the same order";
    }

    /// <summary>
    /// The claim the whole mode rests on: a boss is rolled, not written. Same seed, same
    /// monster — which is what makes one debuggable — and different seeds have to actually
    /// diverge across the body, the loadout and the paint, not just in one field.
    /// </summary>
    private static string? GenomesAreSeededAndVaried()
    {
        var a = BossGen.Herald(90210, BossLineage.Stalker);
        var b = BossGen.Herald(90210, BossLineage.Stalker);
        if (a.FullName != b.FullName || a.Carriage != b.Carriage || a.Quirk != b.Quirk
            || !a.Attacks.SequenceEqual(b.Attacks) || Math.Abs(a.Scale - b.Scale) > 1e-6f)
            return "the same seed rolled two different bosses";

        // Now the variety, measured across 200 rolls of one lineage so nothing can pass by
        // varying only the lineage.
        var names = new HashSet<string>();
        var loadouts = new HashSet<string>();
        var quirks = new HashSet<Quirk>();
        var carriages = new HashSet<Carriage>();
        var paints = new HashSet<int>();
        var limbCounts = new HashSet<int>();
        for (int s = 0; s < 200; s++)
        {
            var g = BossGen.Herald(s * 7919 + 13, BossLineage.Bulwark);
            names.Add(g.FullName);
            loadouts.Add(string.Join(",", g.Attacks.OrderBy(x => x)));
            quirks.Add(g.Quirk);
            carriages.Add(g.Carriage);
            paints.Add(g.ShellSwatch * 1000 + g.DeepSwatch * 100 + g.LimbSwatch * 10 + g.CoreTone);
            limbCounts.Add(g.LimbCount);

            if (g.Attacks.Length != 3) return $"a boss rolled {g.Attacks.Length} attacks, not three";
            if (g.Attacks.Distinct().Count() != 3) return "a boss rolled the same attack twice";
            if (!g.Has(BossGen.HeraldLayers == g.Layers ? g.Attacks[0] : g.Attacks[0]))
                return "a boss does not carry its own first attack";
        }
        if (loadouts.Count < 40) return $"200 rolls produced only {loadouts.Count} distinct loadouts";
        if (quirks.Count < 5) return $"only {quirks.Count} quirks ever came up";
        if (carriages.Count < 2) return "one lineage never rolled its alternative carriage";
        if (paints.Count < 20) return $"200 rolls produced only {paints.Count} paint jobs";
        if (limbCounts.Count < 2) return "the limb count never varied";
        if (names.Count < 5) return $"only {names.Count} distinct names came out of 200 rolls";
        return null;
    }

    /// <summary>
    /// The palette is a closed set and a rolled boss is not allowed to be the thing that breaks
    /// it. Every body colour has to come out of the shared swatch table the player paints from,
    /// and every core out of the game's own three neons.
    /// </summary>
    private static string? BossesStayInThePalette()
    {
        var swatches = ClassCatalog.Swatches.Select(s => s.Color).ToHashSet();
        foreach (BossLineage lineage in Enum.GetValues<BossLineage>())
            for (int s = 0; s < 60; s++)
            {
                var g = BossGen.Colossus(s * 131 + 7, lineage);
                if (!swatches.Contains(g.Shell)) return "a boss rolled a shell outside the swatch table";
                if (!swatches.Contains(g.Deep)) return "a boss rolled a body tone outside the swatch table";
                if (!swatches.Contains(g.Limb)) return "a boss rolled a limb outside the swatch table";
                if (!BossGen.Cores.Contains(g.Core)) return "a boss rolled a core outside the neon register";
            }
        return null;
    }

    // --- The fight ------------------------------------------------------------------------

    private static string? LayersBreakOneAtATime()
    {
        var gene = BossGen.Colossus(1234, BossLineage.Bulwark) with { Quirk = Quirk.None };
        var boss = Standing(gene);

        if (boss.LayersLeft != 5) return "a Colossus did not open with five layers";
        if (boss.ShedLayers != 0) return "a Colossus opened having already shed something";

        // One enormous hit. It must take exactly one layer off, never four — the five bars are
        // five rounds of a fight and a single number must not be able to skip past them.
        Hit(boss, gene.LayerHealth * 12f);
        if (boss.LayersLeft != 4) return $"one huge hit left {boss.LayersLeft} layers instead of four";
        if (boss.ShedLayers != 1) return "the broken layer did not come off the model";
        if (boss.Phase != ModularBoss.State.Breaking) return "a broken layer did not stage a break";
        if (Math.Abs(boss.LayerFraction(4)) > 0.001f) return "the broken layer is not empty";
        if (boss.LayerFraction(3) < 0.999f) return "the layer behind it was not left full";
        return null;
    }

    private static string? ExposureDoublesDamage()
    {
        var gene = BossGen.Herald(4321, BossLineage.Drowner) with
        {
            Quirk = Quirk.None, LayerHealth = 500f, Attacks = new[]
            {
                AttackModule.Spit, AttackModule.Mortar, AttackModule.Scream,
            },
        };
        var boss = Standing(gene);

        // Walk it to the exposure window by simply running the fight until it commits and
        // recovers — the window is reached by the state machine, never handed to it.
        var target = new Vector2(0f, 12f);
        for (int i = 0; i < 4000 && !boss.Exposed; i++)
        {
            boss.Update(1f / 30f, target, 0f);
            boss.ClearActs();
            boss.ClearCues();
        }
        if (!boss.Exposed) return "a boss never reached the beat it is meant to be open in";

        float open = boss.Damage(10f, fromFront: false);
        if (Math.Abs(open - 10f * ModularBoss.ExposedMultiplier) > 0.01f)
            return $"a hit in the open window landed for {open} rather than {10f * ModularBoss.ExposedMultiplier}";

        // ...and out of it, it is worth exactly what it says on the tin.
        var fresh = Standing(gene);
        float closed = fresh.Damage(10f, fromFront: false);
        if (Math.Abs(closed - 10f) > 0.01f)
            return $"an ordinary hit landed for {closed} rather than 10";
        return null;
    }

    private static string? PlatesRefuseTheFront()
    {
        var gene = BossGen.Herald(2468, BossLineage.Bulwark) with { Quirk = Quirk.Armoured };
        var boss = Standing(gene);
        boss.Heading = 0f;   // facing +Z
        if (!boss.PlatesUp) return "a SEALED boss did not come up plated";

        float front = boss.Damage(3f, fromFront: true);
        if (front != 0f) return "the plates let a frontal hit through";

        float behind = boss.Damage(3f, fromFront: false);
        if (behind <= 0f) return "a flanking hit was refused by frontal plates";

        // And the geometry the world uses to decide which is which has to agree with the words.
        if (!boss.IsFrontal(boss.Position + new Vector2(0f, 20f)))
            return "a shot from dead ahead did not read as frontal";
        if (boss.IsFrontal(boss.Position + new Vector2(0f, -20f)))
            return "a shot from directly behind read as frontal";

        // Enough frontal fire eventually breaks the plates off, and then the front tells.
        for (int i = 0; i < 200 && boss.PlatesUp; i++) boss.Damage(1f, fromFront: true);
        if (boss.PlatesUp) return "the plates never broke however much was put into them";
        if (boss.Damage(2f, fromFront: true) <= 0f)
            return "a frontal hit was still refused after the plates came off";
        return null;
    }

    private static string? ColossusShedsItsLayers()
    {
        var gene = BossGen.Colossus(999, BossLineage.Column) with { Quirk = Quirk.None };
        var boss = Standing(gene);
        var target = new Vector2(0f, 20f);

        int seenSheds = 0;
        for (int i = 0; i < 40000 && boss.Alive; i++)
        {
            boss.Update(1f / 30f, target, 0f);
            boss.ClearActs();
            boss.ClearCues();
            // Chip at it whenever it can be touched, exactly as a player would.
            if (!boss.Untouchable) boss.Damage(1.5f, fromFront: false);
            seenSheds = Math.Max(seenSheds, boss.ShedLayers);
        }
        if (boss.Alive) return "a Colossus could not be killed at all";
        if (seenSheds < BossGen.ColossusLayers - 1)
            return $"only {seenSheds} layers were ever shed off a five-layer boss";
        if (boss.Ruin < 0.999f) return "a dead boss was not fully ruined";
        return null;
    }

    /// <summary>
    /// The fairness rule, checked rather than asserted in a comment: nothing a boss does may
    /// arrive without a wind-up in front of it. Every act it ever emits has to be preceded by a
    /// <see cref="ModularBoss.State.Winding"/> phase, and that phase has to last long enough to
    /// react to.
    /// </summary>
    private static string? NothingHappensWithoutAWindUp()
    {
        foreach (BossLineage lineage in Enum.GetValues<BossLineage>())
            for (int s = 0; s < 12; s++)
            {
                var boss = Standing(BossGen.Herald(s * 313 + 11, lineage) with { LayerHealth = 1e6f });
                var target = new Vector2(0f, 14f);
                bool wound = false;
                float windStarted = -1f, clock = 0f;

                for (int i = 0; i < 3000; i++)
                {
                    var before = boss.Phase;
                    boss.Update(1f / 30f, target, 0f);
                    clock += 1f / 30f;

                    if (boss.Phase == ModularBoss.State.Winding && before != ModularBoss.State.Winding)
                    {
                        wound = true;
                        windStarted = clock;
                    }
                    if (boss.Acts.Count > 0)
                    {
                        if (!wound) return $"a {lineage} acted without ever winding up";
                        if (boss.Phase is ModularBoss.State.Winding)
                            return $"a {lineage} acted while still winding";
                        if (clock - windStarted < MinTelegraph)
                            return $"a {lineage} gave only {clock - windStarted:0.00}s of warning";
                    }
                    boss.ClearActs();
                    boss.ClearCues();
                }
                if (!wound) return $"a {lineage} never did anything at all in a hundred seconds";
            }
        return null;
    }

    /// <summary>The floor on a telegraph. Below this a wind-up is a formality rather than a
    /// warning — and every scaling factor in <c>ModularBoss.WindLength</c> is clamped above it.</summary>
    private const float MinTelegraph = 0.3f;

    // --- Helpers -----------------------------------------------------------------------

    /// <summary>A boss past its arrival and ready to fight, which is what every test about the
    /// fight actually wants — the arrival is three seconds of it being untouchable.</summary>
    private static ModularBoss Standing(BossGenome gene)
    {
        var boss = new ModularBoss(gene, Vector2.Zero);
        var target = new Vector2(0f, 30f);
        for (float t = 0f; t <= ModularBoss.ArrivalDuration + 0.1f; t += 1f / 30f)
        {
            boss.Update(1f / 30f, target, 0f);
            boss.ClearActs();
            boss.ClearCues();
        }
        return boss;
    }

    /// <summary>Puts a hit in from behind, so a plated boss does not silently eat the test.</summary>
    private static void Hit(ModularBoss boss, float amount) => boss.Damage(amount, fromFront: false);
}
