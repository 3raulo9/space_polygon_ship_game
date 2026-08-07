using System.Numerics;
using Unrendered.Entities;

namespace Unrendered.Core;

/// <summary>
/// The FLOWER: a chassis defined almost entirely by things it <em>refuses</em> to do, which
/// makes it unusually well suited to headless checking and unusually badly suited to being
/// checked by looking at it.
///
/// <para>A screenshot of this class is a picture of a plant standing still, and a plant
/// standing still is also what a completely broken one looks like. Everything that matters
/// here is either a negative (the root did not move; the seventh throw did not happen) or is
/// only true over several seconds (the petal came back; the crop ripened; the reserve paid for
/// the ground). So the checks below step a real world with real input frames and assert about
/// what came out the other end.</para>
///
/// <para>What is checked is the mechanism, not the taste — that a petal returns, not whether it
/// returns at a pleasant speed; that a harvest yields three things, not whether those three are
/// the right three. The numbers are judgement and belong in the rig where they can be retuned.
/// The wiring is not.</para>
/// </summary>
public static partial class SelfTest
{
    /// <summary>The chassis's whole block, run together so it reads as one in the output.</summary>
    private static int RunFlowerChecks()
    {
        int failures = 0;
        failures += Check("a flower is rooted, and W only bends it", FlowerIsRootedAndOnlyLeans);
        failures += Check("a ripe head sets seed for exactly three things", FlowerHarvestYieldsThreeWhenRipe);
        failures += Check("the crop grows on light, not on a timer", FlowerRipensFasterInDaylight);
        failures += Check("a petal finds what it was thrown at and comes back", FlowerPetalKillsAndComesBack);
        failures += Check("a petal left behind is lost, not free", FlowerLosesPetalsItLeavesBehind);
        failures += Check("the ring is conserved and held fire draws it down", FlowerRingIsConserved);
        failures += Check("a replant is free, buries the plant and moves it", FlowerReplantsToChosenGround);
        failures += Check("sap grows petals back, and a dry plant grows them slower", FlowerSapGrowsPetalsBack);
        failures += Check("death sheds the ring and a comeback rebuilds it", FlowerShedsAndRebuildsItsRing);
        return failures;
    }

    private static World.World FlowerWorld()
        => new(new Loadout { Class = PlayerClass.Flower }) { DynamicSpawning = false };

    /// <summary>Steps a world with a live input frame — the path a real keyboard takes, so the
    /// triggers under test are the ones the player actually pulls. Edges are spent on the first
    /// step and repeated held-only after, exactly as the sampler does it.</summary>
    private static void StepFlower(World.World world, Btn down, Btn pressed, int steps = 1)
    {
        for (int i = 0; i < steps; i++)
        {
            var frame = new InputFrame(down, i == 0 ? pressed : Btn.None, Vector2.Zero);
            world.Update((float)Config.FixedDt, frame);
        }
    }

    /// <summary>
    /// The class's founding negative. Every other chassis answers W by travelling; this one
    /// answers it with about two metres of stalk and then stops, and the <em>root</em> never
    /// moves at all.
    ///
    /// Both halves are asserted, because they are two different bugs. A root that drifted would
    /// mean the drive integrator was still running under the rig; a head that did not move would
    /// mean the lean spring was not.
    /// </summary>
    private static string? FlowerIsRootedAndOnlyLeans()
    {
        var world = FlowerWorld();
        if (world.Player.Flower is not { } stalk) return "flower chassis has no rig";

        Vector2 root = world.Player.Position;

        // Lean forward and left, hard, for a good while — long enough for the spring to have
        // arrived and settled rather than merely to be on its way.
        StepFlower(world, Btn.Forward | Btn.TurnLeft, Btn.None, 120);

        if (Vector2.Distance(world.Player.Position, root) > 0.001f)
            return $"the root travelled {Vector2.Distance(world.Player.Position, root):0.00}u";
        if (stalk.Lean.Length() < 1f)
            return $"the stalk barely bent ({stalk.Lean.Length():0.00}u)";
        if (stalk.Lean.Length() > FlowerRig.MaxLean + 0.001f)
            return $"the stalk bent {stalk.Lean.Length():0.00}u, past its own {FlowerRig.MaxLean}u stop";

        // The head is where the eye and the gun are — the whole payoff of the lean.
        if (Vector2.Distance(world.Player.Muzzle, root) < 1f)
            return "the head did not move with the stalk";

        // Let go: it springs back through centre rather than sliding to a stop, which is the
        // difference between a stalk and a slider. Checked as "it returns", not as "it
        // overshoots" — the overshoot is tuning and this is wiring.
        StepFlower(world, Btn.None, Btn.None, 180);
        if (stalk.Lean.Length() > 0.15f)
            return $"the stalk never straightened ({stalk.Lean.Length():0.00}u left)";

        // And the one refusal that is not about movement: a plant cannot leave the ground.
        if (world.Player.TryJump()) return "a flower jumped";
        if (world.Player.TryHyperspace()) return "a flower hyperspaced";
        return null;
    }

    /// <summary>
    /// The crop clock: it fills on its own, Q spends it for exactly three things on the grid,
    /// and Q before it is ripe does nothing at all.
    ///
    /// The "does nothing" half is the one worth having. A harvest that fired early would not
    /// look like a bug — it would look like a generous cooldown — and it would quietly delete
    /// the only economy the class has.
    /// </summary>
    private static string? FlowerHarvestYieldsThreeWhenRipe()
    {
        var world = FlowerWorld();
        if (world.Player.Flower is not { } stalk) return "flower chassis has no rig";

        // Counted in the pack, not on the field, and that is the whole subtlety of this
        // chassis's harvest: the crop falls onto the grid in a ring tight around the root —
        // which is inside the plant's own collection radius, so it is picked straight back up,
        // usually on the very next tick.
        //
        // That is deliberate and it is the only arrangement that works. Dropped further out it
        // would be visible and unreachable, on a craft that cannot walk over to get it; dropped
        // straight into the pack it would be invisible, and "three things fell out of you" is
        // most of what makes the ability feel like a harvest rather than a cooldown. So it
        // falls, is seen to fall, and is taken in. What is under test is therefore what the
        // player ends up holding.
        int Held()
        {
            int n = 0;
            foreach (var s in world.InventoryOf(0).Slots) n += s.Count;
            return n;
        }

        int before = Held();

        // Unripe: the button is pressed and nothing whatsoever happens.
        StepFlower(world, Btn.Harvest, Btn.Harvest, 4);
        if (Held() != before) return "an unripe head still set seed";

        // Ripen it the honest way — by waiting — so the rate itself is under test rather than
        // a field being poked. Two ripening periods of headroom, since the world's own daylight
        // is whatever the default planet happens to be.
        int steps = (int)(FlowerRig.RipenTime * 2f / Config.FixedDt);
        for (int i = 0; i < steps && !stalk.Ripe; i++) StepFlower(world, Btn.None, Btn.None);
        if (!stalk.Ripe) return "the head never ripened";

        before = Held();
        StepFlower(world, Btn.Harvest, Btn.Harvest);
        // A few ticks for the three to be swept up off the grid they landed on.
        StepFlower(world, Btn.None, Btn.None, 10);

        int dropped = Held() - before;
        // A stray round carries a random handful rather than one, so three items is at least
        // three units and rarely exactly three. Bounded on both sides: fewer than three means a
        // seed went missing, and more than the yield's worth of ammo means it fired twice.
        if (dropped < FlowerRig.HarvestYield)
            return $"a harvest yielded {dropped} units, short of {FlowerRig.HarvestYield} items";
        if (dropped > FlowerRig.HarvestYield * 20)
            return $"a harvest yielded {dropped} units, far past one head's worth";
        // Emptied — not merely reduced. Compared against a threshold rather than against zero
        // because the same tick that spends the head also grows it, which is correct: the crop
        // starts over immediately, it does not sit idle waiting to be started.
        if (stalk.Ripeness > 0.02f)
            return $"a spent head kept {stalk.Ripeness:0.000} of its crop";
        return null;
    }

    /// <summary>
    /// Sunlight. This is the one number on the chassis that reads the sky, and it is worth
    /// pinning because nothing else in the game does: a bug here would present as "the class
    /// feels slow on one planet", which is indistinguishable from a balance opinion.
    /// </summary>
    private static string? FlowerRipensFasterInDaylight()
    {
        float noon = FlowerRig.RipenRateAt(0f);
        float night = FlowerRig.RipenRateAt(1f);

        if (!(noon > night)) return $"daylight ({noon:0.00}) did not beat night ({night:0.00})";
        if (night <= 0f) return "the crop stopped dead after dark, which is a stall not a cost";
        if (noon <= 1f) return "full daylight was not actually a bonus";
        return null;
    }

    /// <summary>
    /// The weapon, end to end: a petal leaves the ring, goes out, kills what it was pointed at,
    /// turns for home and re-seats in the gap it left.
    ///
    /// The return is the whole class. A petal that killed and then vanished would be a slow
    /// rocket with a five-second reload, and every decision the chassis asks the player to make
    /// — throw now or hold, chase or wait, a full ring or a bare one — exists only because they
    /// come back.
    /// </summary>
    private static string? FlowerPetalKillsAndComesBack()
    {
        var world = FlowerWorld();
        if (world.Player.Flower is not { } stalk) return "flower chassis has no rig";

        // One hunter, out in front, comfortably inside the outbound leg's reach — and it has to
        // be the ONLY one. The world seeds its own at random bearings, and the aimbot picks the
        // nearest thing to the petal rather than the thing it was thrown at, so a stray hunter
        // that happened to drop in closer would pull the blade off course and this check would
        // read a working weapon as a miss.
        world.Enemies.Clear();
        var mark = new EnemyTank(new Vector2(0f, 26f), elite: false);
        world.Enemies.Add(mark);
        AimPlayerAtFirstEnemy(world);

        int seated0 = stalk.Seated;
        StepFlower(world, Btn.Secondary, Btn.Secondary);

        if (stalk.Seated != seated0 - 1)
            return $"the throw did not leave a gap in the ring ({stalk.Seated}/{seated0})";

        bool wasOut = false, cameHome = false;
        int limit = (int)(FlowerRig.MaxFlight * 2f / Config.FixedDt);
        for (int i = 0; i < limit; i++)
        {
            StepFlower(world, Btn.None, Btn.None);
            foreach (var p in stalk.Petals)
                if (p.State == FlowerRig.PetalState.Homing) wasOut = true;
            if (stalk.Seated == seated0) { cameHome = true; break; }
        }

        if (mark.Alive) return "an aimbot petal missed a stationary hunter 26u away";
        if (!wasOut) return "the petal never turned for home";
        if (!cameHome) return "the petal never made it back into the ring";
        return null;
    }

    /// <summary>
    /// A petal that cannot get home is lost, and a lost petal is the only thing on this chassis
    /// that costs real time. Checked by throwing one and then <em>leaving</em> — a replant cuts
    /// every petal in the air loose, which is exactly the situation this rule exists for.
    /// </summary>
    private static string? FlowerLosesPetalsItLeavesBehind()
    {
        var world = FlowerWorld();
        if (world.Player.Flower is not { } stalk) return "flower chassis has no rig";

        StepFlower(world, Btn.Secondary, Btn.Secondary);
        StepFlower(world, Btn.None, Btn.None, 6);

        bool airborne = false;
        foreach (var p in stalk.Petals) if (p.InAir) airborne = true;
        if (!airborne) return "the petal was not in the air to begin with";

        // Pick ground and go. Hold for a few ticks, then release.
        StepFlower(world, Btn.Replant, Btn.Replant, 5);
        StepFlower(world, Btn.None, Btn.None);

        if (!stalk.Replanting) return "the replant never started";

        // Through the wilt, at which point everything in the air is cut loose.
        StepFlower(world, Btn.None, Btn.None, 60);

        foreach (var p in stalk.Petals)
            if (p.InAir) return "a petal was still chasing a plant that had left";

        bool regrowing = false;
        foreach (var p in stalk.Petals)
            if (p.State == FlowerRig.PetalState.Regrowing) regrowing = true;
        if (!regrowing) return "an abandoned petal came back for free";
        return null;
    }

    /// <summary>
    /// The ring is conserved: at no point during a sustained hold does it produce a petal it
    /// does not have, and at no point does one go missing.
    ///
    /// <para>Asserted as a conservation law rather than as "the ring ends up empty", because the
    /// ring does not end up empty — petals come <em>back</em>, and a trigger held down for six
    /// seconds is throwing some of them twice. That was already true at six petals; at twelve it
    /// is emphatically true, since the steady state under continuous fire is only four or five
    /// in the air. What would not be the class working is a slot spent while it was already
    /// airborne, and a running count is the only thing that can tell the two apart.</para>
    ///
    /// <para>What is still asserted about the count is that the ring is genuinely <em>drawn
    /// down</em> — that holding the trigger costs something visible. A ring that never dipped
    /// below full would mean the throw was not spending a slot at all.</para>
    /// </summary>
    private static string? FlowerRingIsConserved()
    {
        var world = FlowerWorld();
        if (world.Player.Flower is not { } stalk) return "flower chassis has no rig";
        if (stalk.Petals.Count != FlowerRig.PetalCount)
            return $"the ring holds {stalk.Petals.Count}, not {FlowerRig.PetalCount}";

        // Hold the trigger down for a good while. Nothing is in front of the plant, so the
        // petals fly straight, turn at their own reach and come home.
        int lowest = FlowerRig.PetalCount;
        int steps = (int)(8f / Config.FixedDt);
        for (int i = 0; i < steps; i++)
        {
            StepFlower(world, Btn.Secondary, i % 3 == 0 ? Btn.Secondary : Btn.None);

            int seated = stalk.Seated, air = 0, growing = 0;
            foreach (var p in stalk.Petals)
            {
                if (p.InAir) air++;
                else if (p.State == FlowerRig.PetalState.Regrowing) growing++;
            }

            if (seated + air + growing != FlowerRig.PetalCount)
                return $"the ring accounted for {seated + air + growing} petals, not {FlowerRig.PetalCount}";
            if (seated > FlowerRig.PetalCount) return $"the ring grew to {seated}";
            if (air > FlowerRig.PetalCount) return $"{air} petals were in the air at once";
            if (seated < lowest) lowest = seated;
        }

        // And the hold genuinely cost something: several slots were empty at once at some point
        // during it. A ring that stayed full through eight seconds of continuous fire would mean
        // the throw was not spending a slot, and every check above would have proved nothing.
        if (lowest > FlowerRig.PetalCount - 3)
            return $"the ring never dropped below {lowest} of {FlowerRig.PetalCount} under held fire";
        return null;
    }

    /// <summary>
    /// The replant: the only movement this class has. It costs the reserve, it takes the plant
    /// through the ground and out the other side, and it is refused outright when the reserve
    /// cannot pay — which on a chassis that cannot walk is the difference between a cost and
    /// being stranded, so it is asserted rather than assumed.
    /// </summary>
    private static string? FlowerReplantsToChosenGround()
    {
        var world = FlowerWorld();
        if (world.Player.Flower is not { } stalk) return "flower chassis has no rig";

        Vector2 from = world.Player.Position;

        // Look flat ahead and pick ground: hold, then release.
        world.Player.Pitch = 0f;
        StepFlower(world, Btn.Replant, Btn.Replant, 8);
        StepFlower(world, Btn.None, Btn.None);

        if (!stalk.Replanting) return "the replant never started";

        // Under the plate it is nearly untouchable, but not untouchable.
        float shielded = world.Player.Shield;
        int guard = (int)(2f / Config.FixedDt);
        for (int i = 0; i < guard && stalk.State != FlowerRig.Stance.Seeded; i++)
            StepFlower(world, Btn.None, Btn.None);
        if (stalk.State != FlowerRig.Stance.Seeded) return "the plant never went under";

        world.Player.TakeDamage(20f);
        float taken = shielded - world.Player.Shield;
        if (taken >= 20f) return $"a buried seed took the full {taken:0.0} of a 20-point hit";
        if (taken <= 0f) return "a buried seed was untouchable, which nothing in this game is";

        // Out the other side, somewhere else, standing.
        for (int i = 0; i < guard && stalk.Replanting; i++) StepFlower(world, Btn.None, Btn.None);
        if (stalk.Replanting) return "the replant never finished";
        if (Torus.Distance(world.Player.Position, from) < 5f)
            return "the plant came up where it went down";
        if (world.Player.Height != 0f) return "the plant came up off the ground";

        // Fresh soil is worth something, which is the line that ties the class's escape to its
        // economy. Without it a player who has to leave is also a player who has lost their crop.
        if (stalk.Ripeness < FlowerRig.FreshSoilRipeness - 0.001f)
            return "new ground gave the crop nothing";

        // And the reserve does not gate it. This is the whole point of the ability being free:
        // the replant is this chassis's *movement*, and a craft that cannot move because a bar
        // is empty is a craft that has been parked. Asserted with the reserve at zero, which is
        // exactly the state the old rule refused in.
        world.Player.Hyper = 0f;
        Vector2 drained = world.Player.Position;
        StepFlower(world, Btn.Replant, Btn.Replant, 8);
        StepFlower(world, Btn.None, Btn.None);
        if (!stalk.Replanting) return "an empty reserve refused the replant";

        for (int i = 0; i < guard && stalk.Replanting; i++) StepFlower(world, Btn.None, Btn.None);
        if (Torus.Distance(world.Player.Position, drained) < 5f)
            return "a replant on an empty reserve went nowhere";
        return null;
    }

    /// <summary>
    /// What the reserve does now that the replant is free: it grows petals back, and a plant
    /// that has run itself dry gets them back slower.
    ///
    /// <para>Worth pinning because it is the only job SAP has left on this chassis. If this
    /// silently stopped working, nothing on screen would look wrong — the bar would simply sit
    /// full forever and the class would have a resource that does nothing, which is exactly the
    /// state it was in for the ten minutes between making the replant free and this.</para>
    /// </summary>
    private static string? FlowerSapGrowsPetalsBack()
    {
        // Two identical plants, one fed and one dry, both missing a petal. The only difference
        // between the runs is the reserve.
        float Left(bool fed)
        {
            var world = FlowerWorld();
            var stalk = world.Player.Flower!;

            StepFlower(world, Btn.Secondary, Btn.Secondary);
            foreach (var p in stalk.Petals)
                if (p.InAir) stalk.Lose(p);

            // Held at the extreme each tick, since the craft's own trickle would otherwise
            // refill a starved plant within a second and the two runs would converge.
            int steps = (int)(1.5f / Config.FixedDt);
            for (int i = 0; i < steps; i++)
            {
                world.Player.Hyper = fed ? world.Player.MaxHyper : 0f;
                StepFlower(world, Btn.None, Btn.None);
            }

            foreach (var p in stalk.Petals)
                if (p.State == FlowerRig.PetalState.Regrowing) return p.Regrow;
            return 0f;
        }

        float fedLeft = Left(fed: true), dryLeft = Left(fed: false);
        if (!(dryLeft > fedLeft))
            return $"a dry plant regrew as fast as a fed one ({dryLeft:0.00}s vs {fedLeft:0.00}s left)";

        // Starved is slower, never stopped: a plant that could never get its ring back is a
        // player with nothing to do but wait, which is a punishment rather than a cost. Checked
        // against the clock the petal started on rather than against the constant, since the
        // constant is one the compiler can see through and asserting it proves nothing.
        if (dryLeft >= FlowerRig.RegrowTime - 0.01f)
            return "a dry plant made no progress at all in a second and a half";

        // And growing petals genuinely draws the reserve down rather than merely reading it.
        //
        // Checked with the whole ring lost at once, and that is not laziness — a single petal
        // draws about four a second against a trickle of fifteen, so one regrowing petal is
        // invisible on the bar and *should* be. The rule only bites when a lot of them are gone,
        // which is the curve the number was chosen for, and testing it anywhere else would be
        // testing a case the design deliberately does not have.
        var drain = FlowerWorld();
        var ring = drain.Player.Flower!;
        ring.Shed();
        drain.Player.Hyper = drain.Player.MaxHyper * 0.5f;
        float before = drain.Player.Hyper;
        StepFlower(drain, Btn.None, Btn.None, 60);

        if (drain.Player.Hyper >= before)
            return $"a whole ring regrowing cost the reserve nothing ({before:0.0} → {drain.Player.Hyper:0.0})";
        return null;
    }

    /// <summary>
    /// A dead plant sheds its ring and a revived one gets it back whole. The second half is the
    /// one that would actually be missed: a comeback that handed the player five petals on a
    /// seven-second clock each would spend the first half-minute of its new life unable to do
    /// the one thing the class is for.
    /// </summary>
    private static string? FlowerShedsAndRebuildsItsRing()
    {
        var world = FlowerWorld();
        if (world.Player.Flower is not { } stalk) return "flower chassis has no rig";

        world.Player.Lives = 2;
        world.Player.TakeDamage(world.Player.MaxShield + world.Player.MaxHealth + 50f);

        if (world.Player.Lives != 1) return "a lethal hit did not spend a life";
        if (stalk.Seated != FlowerRig.PetalCount)
            return $"a revived plant came back with {stalk.Seated} petals";

        // And a final death leaves the ring off, since there is nothing left to rebuild.
        world.Player.Lives = 0;
        world.Player.TakeDamage(world.Player.MaxShield + world.Player.MaxHealth + 50f);
        if (stalk.Seated != 0) return "a dead plant kept its petals";
        return null;
    }
}
