using System.Numerics;
using Unrendered.Entities;
using Unrendered.World;

namespace Unrendered.Core;

/// <summary>
/// Headless checks for the way off a planet: the fragments, the gates, the panel's chart, the
/// portal, and the crossing that strings five planets into one session.
///
/// <para>None of it needs a window. <see cref="Arch"/> is pure state by design, so a whole gate
/// can be driven from dark to open in a few hundred ticks with no renderer, no audio and no
/// mouse — which matters more here than almost anywhere else in the game, because the alternative
/// way to exercise this code is to play three hours of DESCENT.</para>
/// </summary>
public static partial class SelfTest
{
    private static int RunArchChecks()
    {
        int failures = 0;
        failures += Check("the sim and the renderer agree where an arch's curve is", ArchGeometryAgrees);
        failures += Check("gates stand dark all run and light when the Colossus falls", GatesLightOnlyAtTheEnd);
        failures += Check("the first hand on a panel claims it and the rest go dark", FirstClaimWins);
        failures += Check("a gate takes nothing until the room has chosen where to go", NoFragmentsBeforeACourse);
        failures += Check("the span fills from the feet inward and the fifth is the keystone", SocketsFillOutsideIn);
        failures += Check("five seated fragments charge the span and open it", FiveOpensThePortal);
        failures += Check("the portal's colour is the mix that bought it", PortalReadsItsMix);
        failures += Check("a fragment leaves the pack only if the arch actually took it", FeedingSpendsExactlyOnce);
        failures += Check("you have to be standing at the arch to feed it", FeedingNeedsToBeThere);
        failures += Check("a full pack refuses a fragment and leaves it on the ground", AFullPackRefusesAFragment);
        failures += Check("a wreck gives back every fragment it was carrying", DeathDropsTheFragments);
        failures += Check("nothing on the field is ever evicted to make room for salvage", FragmentsAreNeverEvicted);
        failures += Check("a gate cannot be cut down", GatesCannotBeFelled);
        failures += Check("everybody through, or the clock takes them", TheRoomLeavesTogether);
        failures += Check("the chart will not offer a world the run has already crossed", ChartLocksWhereYouHaveBeen);
        failures += Check("a crossing climbs, so late worlds are harder than early ones", CampaignDifficultyClimbs);
        failures += Check("a crossing carries the craft and the pack, and spends the fragments", CrossingCarriesTheRoom);
        failures += Check("a gate crosses the wire whole", GateSurvivesTheWire);
        return failures;
    }

    // --- Geometry -------------------------------------------------------------------------

    /// <summary>
    /// The one duplicated constant in this feature, guarded. <see cref="Arch"/> places sockets
    /// on a half-ellipse and <c>StructureMeshes</c> builds the arc's beams on the same one, and
    /// they are two named constants in two namespaces rather than one shared number because the
    /// simulation reaching into a rendering class for geometry is the worse of the two evils.
    ///
    /// <para>This is the check that makes that trade safe. The last time an entity and its
    /// renderer each derived a position from their own idea of the geometry, a boss's core spent
    /// a whole build buried inside its own chest.</para>
    /// </summary>
    private static string? ArchGeometryAgrees()
    {
        // Read into locals first. Both sides are compile-time constants, so comparing them
        // directly is folded away and the check becomes unreachable code the compiler warns
        // about — a test that cannot fail, which is worse than no test at all because it looks
        // like one.
        float springSim = Arch.SpringHeight, springMesh = Rendering.StructureMeshes.SpringHeight;
        float riseSim = Arch.ArchRise, riseMesh = Rendering.StructureMeshes.ArchRise;

        if (springSim != springMesh)
            return "the arch's springing height differs between the sim and the mesh";
        if (riseSim != riseMesh)
            return "the arch's rise differs between the sim and the mesh";

        // And the sockets land on the span rather than beside it: the feet at the pylons, the
        // keystone at the crown.
        var gate = new Arch(0, Vector2.Zero, 0f, 1f);
        Vector3 left = gate.SocketPoint(0), crown = gate.SocketPoint(2), right = gate.SocketPoint(4);

        if (crown.Y <= left.Y || crown.Y <= right.Y)
            return "the keystone does not sit above the feet";
        if (crown.Y > (Arch.SpringHeight + Arch.ArchRise) * 1.01f)
            return "the keystone stands above the arc it is meant to be part of";
        if (MathF.Abs(MathF.Abs(left.X) - MathF.Abs(right.X)) > 2f)
            return "the two feet are not symmetric about the centre";

        // The haul rides the curve rather than cutting through the stonework: every point on
        // the way out to the far foot has to be at least as high as the springing.
        gate.Light();
        gate.Claim();
        gate.SetDestination(PlanetId.Kirene);
        gate.Feed(Fragment.Sun, 0);
        for (int i = 0; i <= 20; i++)
        {
            float along = 1f + (Arch.SocketAlong[gate.CarryingTo] - 1f) * (i / 20f);
            if (gate.CurvePoint(along).Y < Arch.SpringHeight * 0.99f)
                return "the rail dips below the springing point on its way out";
        }
        return null;
    }

    // --- Lighting and claiming ----------------------------------------------------------

    private static string? GatesLightOnlyAtTheEnd()
    {
        World.World w = DescentWorld();
        if (w.Gates.Count == 0) return "a descent opened with no gates on the planet";
        if (w.GatesLit) return "the gates had power in them at the drop";
        foreach (var g in w.Gates)
            if (g.State != Arch.Phase.Dark) return "a gate was lit before the Colossus fell";

        // Every arc a run picks has to be a real building standing on the map, or the light
        // column would be burning over an empty patch of grid.
        foreach (var g in w.Gates)
        {
            bool found = false;
            foreach (var s in w.Structures)
                if (s.Index == g.StructureIndex && s.Kind == StructureKind.Arch) found = true;
            if (!found) return "a gate was placed on something that is not an arc";
        }

        // And no arc is used twice — a seeded shuffle rather than three independent rolls.
        for (int i = 0; i < w.Gates.Count; i++)
            for (int j = i + 1; j < w.Gates.Count; j++)
                if (w.Gates[i].StructureIndex == w.Gates[j].StructureIndex)
                    return "the same arc was chosen as two different gates";

        ClearTheColossus(w);
        if (!w.GatesLit) return "the Colossus fell and nothing took power";
        foreach (var g in w.Gates)
            if (g.State != Arch.Phase.Lit) return "a gate stayed dark after the Colossus fell";
        return null;
    }

    private static string? FirstClaimWins()
    {
        World.World w = DescentWorld();
        if (w.Gates.Count < 2) return "the planet did not offer a choice of gates";
        ClearTheColossus(w);

        Arch mine = w.Gates[0], theirs = w.Gates[1];
        if (!w.ClaimGate(mine, 0)) return "the first hand on a panel was refused";
        if (!ReferenceEquals(w.ClaimedGate, mine)) return "the claim did not stick";
        if (mine.State != Arch.Phase.Claimed) return "a claimed gate is not marked as one";

        // Everything else stops being anything. Two columns burning after the choice is made
        // would leave the room with two answers to "where are we going".
        foreach (var g in w.Gates)
            if (!ReferenceEquals(g, mine) && g.State != Arch.Phase.Dark)
                return "a gate stayed lit after another one was claimed";

        // And the race is over: a second hand is refused, not queued.
        if (w.ClaimGate(theirs, 1)) return "a second player claimed a second gate";
        if (!ReferenceEquals(w.ClaimedGate, mine)) return "a refused claim moved the room's gate";
        return null;
    }

    // --- Feeding ---------------------------------------------------------------------------

    private static string? NoFragmentsBeforeACourse()
    {
        World.World w = DescentWorld();
        ClearTheColossus(w);
        Arch gate = w.Gates[0];

        if (gate.CanAccept) return "an unclaimed gate was willing to take a fragment";
        w.ClaimGate(gate, 0);
        if (gate.CanAccept) return "a gate with no course set was willing to take a fragment";

        gate.SetDestination(PlanetId.Abysse);
        if (!gate.CanAccept) return "a claimed and aimed gate refused to take anything";
        return null;
    }

    private static string? SocketsFillOutsideIn()
    {
        Arch gate = OpenGate();
        var order = new List<int>();
        for (int i = 0; i < Arch.SocketCount; i++)
        {
            order.Add(gate.NextSocket());
            gate.Feed(Fragment.Sun, 0);
            RunHaul(gate);
        }

        // Both feet, then both haunches, then the crown. The span stays balanced the whole way
        // and the fifth fragment is visibly the one that closes it — which is the entire reason
        // it is not simply left to right.
        int[] want = Arch.FillOrder;
        for (int i = 0; i < want.Length; i++)
            if (order[i] != want[i])
                return $"fragment {i + 1} went to socket {order[i]} rather than {want[i]}";
        if (want[^1] != 2) return "the last fragment is not the keystone";

        if (gate.NextSocket() != -1) return "a full span still offered a socket";
        if (gate.CanAccept) return "a full span was willing to take a sixth fragment";
        return null;
    }

    private static string? FiveOpensThePortal()
    {
        Arch gate = OpenGate();
        for (int i = 0; i < Arch.SocketCount - 1; i++)
        {
            gate.Feed(Fragment.Moon, 0);
            RunHaul(gate);
            if (gate.State != Arch.Phase.Claimed)
                return $"the span started charging with only {gate.Filled} seated";
        }

        gate.Feed(Fragment.Moon, 0);
        RunHaul(gate);
        if (gate.State != Arch.Phase.Charging) return "the fifth fragment did not start the charge";
        if (gate.Charge > 0.01f) return "the charge did not start from nothing";

        // Eleven seconds of something happening, and not a frame less.
        gate.Step(Arch.ChargeTime * 0.5f, 1);
        if (gate.State != Arch.Phase.Charging) return "the span opened halfway through its charge";
        gate.Step(Arch.ChargeTime, 1);
        if (gate.State != Arch.Phase.Open) return "the charge ran out and nothing opened";
        return null;
    }

    private static string? PortalReadsItsMix()
    {
        // An empty span sits at the midpoint rather than reading as a moon gate, so five unfed
        // recesses do not glow as though a decision had already been made.
        Arch neutral = OpenGate();
        if (MathF.Abs(neutral.Warmth - 0.5f) > 0.001f)
            return "an unfed span is not neutral";

        Arch sun = OpenGate();
        for (int i = 0; i < Arch.SocketCount; i++) { sun.Feed(Fragment.Sun, 0); RunHaul(sun); }
        if (sun.Warmth < 0.999f) return "five suns did not open a sun gate";
        if (sun.Mix().Suns != 5) return "five suns were not counted as five";

        Arch moon = OpenGate();
        for (int i = 0; i < Arch.SocketCount; i++) { moon.Feed(Fragment.Moon, 0); RunHaul(moon); }
        if (moon.Warmth > 0.001f) return "five moons did not open a moon gate";

        // And a mix lands honestly between, which is the case that makes the other two mean
        // anything: three and two is 0.6, not "warm-ish".
        Arch mixed = OpenGate();
        for (int i = 0; i < 3; i++) { mixed.Feed(Fragment.Sun, 0); RunHaul(mixed); }
        for (int i = 0; i < 2; i++) { mixed.Feed(Fragment.Moon, 0); RunHaul(mixed); }
        if (MathF.Abs(mixed.Warmth - 0.6f) > 0.001f)
            return $"three suns and two moons read as {mixed.Warmth:0.00} rather than 0.60";
        return null;
    }

    private static string? FeedingSpendsExactlyOnce()
    {
        World.World w = ArmedWorld(out Arch gate, suns: 2, moons: 1);
        w.Players[0].Position = PanelSpot(gate);

        int before = CountFragments(w, 0);
        if (before != 3) return "the test failed to arm the craft";

        int slot = FirstFragmentSlot(w, 0);
        if (!w.FeedGate(0, slot)) return "a fragment in reach of an open panel was refused";
        if (CountFragments(w, 0) != before - 1)
            return "feeding the arch did not spend exactly one fragment";

        // The rail is busy. A second feed is refused — and, crucially, refused without taking
        // anything: a fragment that vanished out of a pack into a refusal would be a fifth of a
        // planet gone to a double-click.
        int held = CountFragments(w, 0);
        if (w.FeedGate(0, FirstFragmentSlot(w, 0)))
            return "the arch took a second fragment while the rail was still moving";
        if (CountFragments(w, 0) != held)
            return "a refused feed still emptied a slot";
        return null;
    }

    private static string? FeedingNeedsToBeThere()
    {
        World.World w = ArmedWorld(out Arch gate, suns: 1, moons: 0);

        // Standing across the city with a fragment in the pack. The panel is not a remote
        // control — if it were, carrying the things anywhere would stop meaning anything.
        w.Players[0].Position = Torus.Wrap(gate.Position + new Vector2(120f, 90f));
        if (w.FeedGate(0, FirstFragmentSlot(w, 0)))
            return "the arch took a fragment from the other side of the map";
        if (CountFragments(w, 0) != 1) return "a refused remote feed still emptied a slot";

        w.Players[0].Position = PanelSpot(gate);
        if (!w.FeedGate(0, FirstFragmentSlot(w, 0)))
            return "the arch refused somebody standing right at it";
        return null;
    }

    // --- The rocks themselves ---------------------------------------------------------------

    private static string? AFullPackRefusesAFragment()
    {
        var w = new World.World(null, MatchSettings.SinglePlayer) { DynamicSpawning = false };
        w.Enemies.Clear();
        w.Pickups.Clear();

        // Every slot full of something that is not a fragment.
        Inventory inv = w.InventoryOf(0);
        for (int i = 0; i < Inventory.SlotCount; i++)
            inv.Slots[i] = new ItemStack(ItemKind.ScrapMetal, Inventory.MaxStack(ItemKind.ScrapMetal));

        w.DropFragmentForTest(w.Players[0].Position, Fragment.Sun);
        if (w.Pickups.Count != 1) return "the test failed to put a fragment on the grid";

        // Driven over repeatedly with nowhere to put it. It has to still be lying there.
        for (int i = 0; i < 120; i++) StepWithoutInput(w);
        if (w.Pickups.Count != 1)
            return "a fragment a full pack could not take was taken anyway, or vanished";
        if (CountFragments(w, 0) != 0) return "a full pack somehow absorbed a fragment";

        // Make room, drive over it again, and it goes in — the refusal is about space, not
        // about the fragment being unreachable for ever.
        inv.Slots[0] = ItemStack.Empty;
        for (int i = 0; i < 120; i++) StepWithoutInput(w);
        if (CountFragments(w, 0) != 1) return "a fragment was refused by a pack with room in it";
        return null;
    }

    private static string? DeathDropsTheFragments()
    {
        var w = new World.World(null, MatchSettings.SinglePlayer) { DynamicSpawning = false };
        w.Enemies.Clear();
        w.Pickups.Clear();

        Inventory inv = w.InventoryOf(0);
        inv.Slots[0] = new ItemStack(ItemKind.SunFragment, 1);
        inv.Slots[1] = new ItemStack(ItemKind.MoonFragment, 1);
        inv.Slots[2] = new ItemStack(ItemKind.RepairKit, 2);

        // One step first, so the world has seen this seat alive and the next one reads as a
        // death rather than as an opening frame.
        StepWithoutInput(w);
        SpendALife(w.Players[0]);
        StepWithoutInput(w);
        if (w.Players[0].Lives >= 3) return "the test failed to kill the craft";

        // Both fragments are back on the grid, and neither was destroyed. A death is a setback
        // and a trip back out — never a lost run.
        int onGround = 0;
        foreach (var pk in w.Pickups) if (World.World.IsFragment(pk)) onGround++;
        if (onGround != 2) return $"a wreck gave back {onGround} of the 2 fragments it carried";
        if (CountFragments(w, 0) != 0) return "a wreck kept hold of a fragment";

        // And nothing else was spilled. This is not a corpse-run mechanic: the rest of a pack
        // survives a death exactly as it always has, and turning every death into an errand
        // would be a different game.
        if (inv.Slots[2].IsEmpty || inv.Slots[2].Kind != ItemKind.RepairKit)
            return "a death spilled something that was not a fragment";
        return null;
    }

    private static string? FragmentsAreNeverEvicted()
    {
        var w = new World.World(null, MatchSettings.SinglePlayer) { DynamicSpawning = false };
        w.Enemies.Clear();
        w.Pickups.Clear();

        // Five fragments on a field, then far more ordinary salvage than the ceiling allows.
        // The eviction rule releases the piece furthest from everybody, and "furthest" is very
        // often a fragment lying where a boss died across the city.
        for (int i = 0; i < Arch.SocketCount; i++)
            w.DropFragmentForTest(Torus.Wrap(new Vector2(60f + i * 20f, 80f)), Fragment.Moon);

        for (int i = 0; i < 200; i++)
            w.DropSalvageForTest(Torus.Wrap(new Vector2(i % 40 * 3f, i % 17 * 5f)),
                PickupKind.ScrapMetal);

        int left = 0;
        foreach (var pk in w.Pickups) if (World.World.IsFragment(pk)) left++;
        if (left != Arch.SocketCount)
            return $"{Arch.SocketCount - left} fragments were evicted to make room for scrap";
        return null;
    }

    private static string? GatesCannotBeFelled()
    {
        World.World w = DescentWorld();
        if (w.Gates.Count == 0) return "a descent opened with no gates on the planet";
        Arch gate = w.Gates[0];

        Structure? building = null;
        foreach (var s in w.Structures) if (s.Index == gate.StructureIndex) building = s;
        if (building is null) return "the gate's building is not on the field";

        // Hit hard enough to flatten anything else on the map, repeatedly. A room that cut its
        // own exit out of the skyline with a stray lance would have lost a three-hour crossing
        // to a ricochet.
        for (int i = 0; i < 20; i++) w.FellStructureForTest(building);
        if (building.Falling || building.Gone) return "a gate was cut down";

        // And an ordinary arc still goes over, so the guard is about gates rather than about
        // arches having quietly become indestructible.
        Structure? plain = null;
        foreach (var s in w.Structures)
            if (s.Kind == StructureKind.Arch && !w.IsGate(s)) { plain = s; break; }
        if (plain is null) return "the map has no un-chosen arc to compare against";
        w.FellStructureForTest(plain);
        if (!plain.Falling && !plain.Gone) return "an ordinary arc refused to come down";
        return null;
    }

    // --- Leaving ----------------------------------------------------------------------------

    private static string? TheRoomLeavesTogether()
    {
        Arch gate = OpenGate();
        for (int i = 0; i < Arch.SocketCount; i++) { gate.Feed(Fragment.Sun, 0); RunHaul(gate); }
        gate.Step(Arch.ChargeTime + 1f, 3);
        if (gate.State != Arch.Phase.Open) return "the span did not open";

        // Three people in the room. Two go through and the gate waits.
        if (!gate.Enter(0)) return "the first craft could not go through";
        if (gate.Grace <= 0f) return "the first one through did not start the clock on the rest";
        if (gate.Departed) return "the room left with one of three still on the planet";

        if (gate.Enter(0)) return "the same seat went through twice";
        if (gate.EnteredCount != 1) return "a repeated entry was counted twice";

        gate.Enter(1);
        gate.Step(0.1f, 3);
        if (gate.Departed) return "the room left with one of three still on the planet";

        // The third arrives and the gate closes behind them.
        gate.Enter(2);
        if (gate.Step(0.1f, 3) != ArchEvent.Departed) return "everybody was through and nobody left";

        // The other half of the rule: somebody who never comes cannot hold the room for ever.
        // Nineteen people are not waiting on one person making a cup of tea.
        Arch stalled = OpenGate();
        for (int i = 0; i < Arch.SocketCount; i++) { stalled.Feed(Fragment.Sun, 0); RunHaul(stalled); }
        stalled.Step(Arch.ChargeTime + 1f, 4);
        stalled.Enter(0);
        for (int i = 0; i < 10 && !stalled.Departed; i++) stalled.Step(1f, 4);
        if (stalled.Departed) return "the grace clock expired early";
        for (int i = 0; i < 60 && !stalled.Departed; i++) stalled.Step(1f, 4);
        if (!stalled.Departed) return "one player who never moved held the whole room for ever";
        return null;
    }

    // --- The crossing -------------------------------------------------------------------

    private static string? ChartLocksWhereYouHaveBeen()
    {
        var panel = new UI.ArchPanel();
        var gate = new Arch(0, Vector2.Zero, 0f, 1f);
        gate.Light();
        gate.Claim();

        int visited = (1 << (int)PlanetId.Thalos) | (1 << (int)PlanetId.Verene);
        panel.OpenOn(gate, here: PlanetId.Solune, visited);

        if (!panel.Chart.IsLocked(PlanetId.Solune))
            return "the chart offered the planet the room is standing on";
        if (!panel.Chart.IsLocked(PlanetId.Thalos) || !panel.Chart.IsLocked(PlanetId.Verene))
            return "the chart offered a world the crossing has already used";
        if (panel.Chart.IsLocked(PlanetId.Kirene) || panel.Chart.IsLocked(PlanetId.Abysse))
            return "the chart locked a world the crossing has not been to";

        // The cursor never rests somewhere the player cannot commit to.
        if (panel.Chart.IsLocked(panel.Chart.Cursor))
            return "the chart opened pointing at a locked world";
        for (int i = 0; i < 10; i++) panel.Chart.Move(-1);
        if (panel.Chart.IsLocked(panel.Chart.Cursor))
            return "walking the chart left the cursor on a locked world";
        for (int i = 0; i < 10; i++) panel.Chart.Move(+1);
        if (panel.Chart.IsLocked(panel.Chart.Cursor))
            return "walking the chart the other way left the cursor on a locked world";

        // And a ballot for a locked world is dropped rather than counted, so a stale packet or
        // an old build cannot send the room somewhere it has already been.
        panel.Chart.OpenVote();
        panel.Chart.Cast(3, PlanetId.Thalos);
        if (panel.Chart.Tally(PlanetId.Thalos) != 0)
            return "a ballot for a crossed world was counted";
        if (panel.Chart.Resolve(PlanetId.Kirene) == PlanetId.Thalos)
            return "a vote resolved to a world the crossing had already used";

        // With everywhere used up, the panel says so rather than offering an empty chart.
        var done = new UI.ArchPanel();
        int all = 0;
        foreach (var p in Planet.All) all |= 1 << (int)p.Id;
        done.OpenOn(gate, PlanetId.Solune, all);
        if (done.Where != UI.ArchPanel.Page.Exhausted)
            return "a crossing with nowhere left did not report itself";
        return null;
    }

    private static string? CampaignDifficultyClimbs()
    {
        var c = new Campaign(seed: 4242);

        // Within a planet it still climbs, or the five-wave shape would stop meaning anything.
        float w1 = c.DifficultyAt(0, 1, Descent.WaveCount);
        float w5 = c.DifficultyAt(0, Descent.WaveCount, Descent.WaveCount);
        if (w5 <= w1) return "difficulty does not climb across a planet's own waves";

        // And it never steps backwards over a portal: the last wave of one world is no harder
        // than the first of the next. This is the whole difference between twenty-five fights
        // that escalate and five loops of five.
        for (int hop = 0; hop + 1 < Campaign.Length; hop++)
        {
            float last = c.DifficultyAt(hop, Descent.WaveCount, Descent.WaveCount);
            float next = c.DifficultyAt(hop + 1, 1, Descent.WaveCount);
            if (next < last - 0.001f)
                return $"crossing from world {hop + 1} to {hop + 2} made the game easier";
        }

        float first = c.DifficultyAt(0, 1, Descent.WaveCount);
        float final = c.DifficultyAt(Campaign.Length - 1, Descent.WaveCount, Descent.WaveCount);
        if (first > 0.01f) return "the very first wave of a crossing did not start at the bottom";
        if (final < 0.99f) return "the very last wave of a crossing did not reach the top";

        // A herald late in a crossing has to outclass the Colossus at the start of it, which is
        // the claim the whole curve exists to make.
        var early = BossGen.Colossus(1, BossLineage.Bulwark, c.DifficultyAt(0, 5, 5));
        var late = BossGen.Herald(1, BossLineage.Bulwark, c.DifficultyAt(3, 2, 5));
        if (late.LayerHealth <= early.LayerHealth / BossGen.ColossusLayers)
            return "a late herald is no tougher per layer than an early Colossus";

        // Seeds are mixed rather than incremented, so consecutive worlds are not near-identical.
        if (c.SeedFor(0) == c.SeedFor(1)) return "two legs of a crossing share a seed";
        return null;
    }

    private static string? CrossingCarriesTheRoom()
    {
        World.World old = DescentWorld();
        old.Enemies.Clear();

        // A craft that has been through something, and a pack with a fragment still in it.
        old.Players[0].Health = old.Players[0].MaxHealth * 0.4f;
        old.Players[0].Lives = 1;
        old.Players[0].Ammo = 7;
        Inventory pack = old.InventoryOf(0);
        pack.Slots[0] = new ItemStack(ItemKind.RepairKit, 2);
        pack.Slots[1] = new ItemStack(ItemKind.SunFragment, 1);

        MatchSettings rules = old.Match.Clamped();
        rules.Destination = PlanetId.Abysse;
        rules.Mode = GameMode.Descent;
        var next = new World.World(null, rules, old.Session) { DynamicSpawning = false };
        next.CarryOverFrom(old);

        // Everything comes through: wounds, spent lives, a half-empty magazine, the pack.
        if (MathF.Abs(next.Players[0].Health - old.Players[0].MaxHealth * 0.4f) > 0.01f)
            return "a crossing healed the craft";
        if (next.Players[0].Lives != 1) return "a crossing refilled the revives";
        if (next.Players[0].Ammo != 7) return "a crossing reloaded the magazine";
        if (next.InventoryOf(0).Slots[0].Kind != ItemKind.RepairKit
            || next.InventoryOf(0).Slots[0].Count != 2)
            return "a crossing lost the pack";

        // Anything still in hand comes too — it can only be a sixth nobody fed in, and that is
        // fair to keep.
        if (CountFragments(next, 0) != 1) return "a crossing took a fragment nobody spent";

        // And the new planet is a fresh five with a fresh set of gates on it.
        if (next.Run is null) return "the far side of a portal had no run on it";
        if (next.Run.Dropped != 0) return "the next planet opened with fragments already dropped";
        if (next.Run.Destination != PlanetId.Abysse) return "the crossing landed on the wrong world";
        if (!next.HasVisited(PlanetId.Abysse)) return "arriving somewhere did not mark it crossed";
        if (next.GatesLit) return "the next planet's gates had power in them at the drop";
        return null;
    }

    // --- The wire ---------------------------------------------------------------------------

    private static string? GateSurvivesTheWire()
    {
        World.World host = DescentWorld();
        ClearTheColossus(host);
        Arch sent = host.Gates[1];
        host.ClaimGate(sent, 0);
        sent.SetDestination(PlanetId.Verene);
        sent.Feed(Fragment.Sun, 0);
        RunHaul(sent);
        sent.Feed(Fragment.Moon, 0);
        sent.Step(0.3f, 1);   // caught mid-haul on purpose: the rail has to travel too

        // A client's world knows none of this. It rebuilds the run and the gates off the seed
        // alone — the whole point of the design — and then takes the gate's state.
        var client = new World.World(null, host.Match.Clamped())
        { DynamicSpawning = false, Authoritative = false };
        client.NetOpenRun(host.Run!.Seed, 0, host.Run.Destination, host.VisitedMask);

        if (client.Gates.Count != host.Gates.Count)
            return "a client derived a different number of gates from the same seed";
        for (int i = 0; i < host.Gates.Count; i++)
            if (client.Gates[i].StructureIndex != host.Gates[i].StructureIndex)
                return "a client derived different arcs from the same seed";

        Span<Fragment> sockets = stackalloc Fragment[Arch.SocketCount];
        for (int i = 0; i < Arch.SocketCount; i++) sockets[i] = sent.SocketAt(i);
        client.NetAdoptGate(sent.StructureIndex, sent.State, sent.Destination, sockets,
            sent.Carrying, sent.CarryingTo, sent.CarryT, sent.Charge, sent.Entered, sent.Grace);

        Arch got = client.ClaimedGate ?? throw new InvalidOperationException();
        if (got.StructureIndex != sent.StructureIndex)
            return "the client claimed a different arch from the host";
        if (got.Destination != sent.Destination) return "the course did not cross the wire";
        if (got.Filled != sent.Filled) return "the seated count did not cross the wire";
        if (got.SocketAt(sent.SocketAt(0) != Fragment.None ? 0 : 4) == Fragment.None
            && sent.Filled > 0)
            return "a seated fragment did not cross the wire";
        if (got.Carrying != sent.Carrying) return "the fragment on the rail did not cross the wire";
        if (got.CarryingTo != sent.CarryingTo) return "the rail's destination did not cross";
        if (MathF.Abs(got.CarryT - sent.CarryT) > 0.02f)
            return "the rail's progress did not cross the wire";

        // And the other gates went dark on the client too, so nobody is looking at two answers.
        foreach (var g in client.Gates)
            if (!ReferenceEquals(g, got) && g.State != Arch.Phase.Dark)
                return "a client left a second gate lit after a claim";
        return null;
    }

    // --- Helpers -----------------------------------------------------------------------------

    /// <summary>A DESCENT world with its director open and its gates chosen, and nothing
    /// hostile on it.</summary>
    private static World.World DescentWorld()
    {
        var w = new World.World(null,
            new MatchSettings { MaxPlayers = 4, Mode = GameMode.Descent, Destination = PlanetId.Solune },
            new Campaign(seed: 1234))
        { DynamicSpawning = false };
        w.Enemies.Clear();
        w.Pickups.Clear();
        return w;
    }

    /// <summary>Drops the run straight to CLEAR and lets one tick light the gates.</summary>
    private static void ClearTheColossus(World.World w)
    {
        w.Run!.SkipTo(DescentPhase.Cleared, Descent.WaveCount, w);
        StepWithoutInput(w);
    }

    /// <summary>A bare gate, claimed and aimed, with no world behind it — which is all most of
    /// these checks need.</summary>
    private static Arch OpenGate()
    {
        var g = new Arch(0, Vector2.Zero, 0f, 1f);
        g.Light();
        g.Claim();
        g.SetDestination(PlanetId.Kirene);
        return g;
    }

    /// <summary>Runs whatever is on the rail all the way into its socket.</summary>
    private static void RunHaul(Arch gate)
    {
        for (int i = 0; i < 400 && gate.Carrying != Fragment.None; i++) gate.Step(0.05f, 1);
    }

    /// <summary>A cleared DESCENT world with one gate claimed and aimed, and a pack armed.</summary>
    private static World.World ArmedWorld(out Arch gate, int suns, int moons)
    {
        World.World w = DescentWorld();
        ClearTheColossus(w);
        gate = w.Gates[0];
        w.ClaimGate(gate, 0);
        gate.SetDestination(PlanetId.Verene);

        Inventory inv = w.InventoryOf(0);
        int slot = 0;
        for (int i = 0; i < suns; i++) inv.Slots[slot++] = new ItemStack(ItemKind.SunFragment, 1);
        for (int i = 0; i < moons; i++) inv.Slots[slot++] = new ItemStack(ItemKind.MoonFragment, 1);
        return w;
    }

    /// <summary>Somewhere a craft can stand and work a gate's panel.</summary>
    private static Vector2 PanelSpot(Arch gate)
    {
        Vector3 p = gate.PanelPoint();
        return Torus.Wrap(new Vector2(p.X, p.Z));
    }

    private static int CountFragments(World.World w, int seat)
        => w.FragmentsOf(seat, Fragment.Sun) + w.FragmentsOf(seat, Fragment.Moon);

    private static int FirstFragmentSlot(World.World w, int seat)
    {
        Inventory inv = w.InventoryOf(seat);
        for (int i = 0; i < Inventory.SlotCount; i++)
            if (!inv.Slots[i].IsEmpty && World.World.IsFragment(inv.Slots[i].Kind)) return i;
        return -1;
    }
}
