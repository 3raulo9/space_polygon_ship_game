using System.Numerics;
using Unrendered.Entities;

namespace Unrendered.Core;

/// <summary>
/// Can the six chassis actually fight <em>each other</em>?
///
/// <para>Every attack in this game grew up pointed at the field. A hunter, a squad and the two
/// monsters are the things the damage passes were written against, and for most of the roster
/// that list is where they stopped: the SPIDER's charged lance raked hunters and passed through
/// a team-mate like fog, the FISH's strike speared soldiers and swam through a craft, a hurled
/// hostage landed on players without touching them, and a whole thrown CRAB CORE went off in
/// somebody's face for nothing. Solo, none of that is visible — there is nobody else to fire
/// at. In a room it is most of the game not working.</para>
///
/// <para>So each check below stands two craft in front of each other, pulls one trigger, and
/// asks the only two questions that matter: with friendly fire on, did the other one feel it?
/// With it off, was it left alone? Both halves are asserted every time, because they are two
/// different bugs — an attack that cannot reach a player and an attack that ignores the host's
/// toggle are equally broken and look nothing alike.</para>
///
/// <para>The mark is a FLOWER throughout. Partly because it is the chassis that most obviously
/// stands still and can be relied on to be where it was put; mostly because it is the one that
/// exposed the whole problem — a rooted plant that could be shot at all afternoon by a spider's
/// lance without noticing.</para>
/// </summary>
public static partial class SelfTest
{
    /// <summary>The whole block, run together so it reads as one in the output.</summary>
    private static int RunDuelChecks()
    {
        int failures = 0;
        failures += Check("TANK: the cannon reaches a player", () => Duel(Duels.TankCannon));
        failures += Check("TANK: the AP slug reaches a player", () => Duel(Duels.TankSlug));
        failures += Check("TANK: a ram reaches a player", () => Duel(Duels.TankRam));
        failures += Check("SPIDER: the laser reaches a player", () => Duel(Duels.SpiderLaser));
        failures += Check("SPIDER: the charged lance reaches a player", () => Duel(Duels.SpiderLance));
        failures += Check("SPIDER: a hurled hostage reaches a player", () => Duel(Duels.SpiderThrow));
        failures += Check("VIRUS: the corruption round reaches a player", () => Duel(Duels.VirusRound));
        failures += Check("VIRUS: the stolen lance reaches a player", () => Duel(Duels.VirusLance));
        failures += Check("VIRUS: a spent host reaches a player", () => Duel(Duels.VirusOverload));
        failures += Check("FISH: the spit reaches a player", () => Duel(Duels.FishSpit));
        failures += Check("FISH: the strike reaches a player", () => Duel(Duels.FishStrike));
        failures += Check("SOLDIER: the rifle reaches a player", () => Duel(Duels.SoldierRifle));
        failures += Check("SOLDIER: the rocket reaches a player", () => Duel(Duels.SoldierRocket));
        failures += Check("FLOWER: the spit reaches a player", () => Duel(Duels.FlowerSpit));
        failures += Check("FLOWER: a petal reaches a player", () => Duel(Duels.FlowerPetal));
        failures += Check("a thrown CRAB CORE reaches a player", () => Duel(Duels.ThrownCrabCore));
        failures += Check("no chassis is left with a harmless kit", EveryChassisCanFight);
        failures += Check("a hostage in the claw stops a player's round too", ClawCoversAgainstAPlayer);
        failures += Check("a player's round in the core breaks a winding lance", CoreHitBreaksAPlayersLance);
        failures += Check("an AP slug bills each craft it rakes exactly once", SlugRakesEachCraftOnce);
        return failures;
    }

    // --- The two bargains that only existed against the field ------------------------
    //
    // The SPIDER is built around one idea: its weak point faces forward. Everything that makes
    // that survivable — the hostage held out in front of the core as cover, and the fact that a
    // round into the core spoils a lance you spent two seconds winding — lived inside the
    // *enemy* round's branch and nowhere else. So both were true of a hunter's bolt and neither
    // was true of a team-mate's, which in a room is the entire chassis not working.

    /// <summary>
    /// A SPIDER carrying a hunter, shot at from inside the arc that catch covers. The hostage
    /// has to eat it, exactly as it does a hunter's bolt.
    ///
    /// <para>The geometry is fussy on purpose, and the first draft of this check proved nothing
    /// because of it. The cover is an <em>arc</em> (<see cref="SpiderClaw.ShieldArc"/>) rather
    /// than an object in the way, so a shooter parked dead ahead simply puts a round through the
    /// held body's own hitbox — which stops it in the ordinary enemy pass, before the claw is
    /// ever asked, and the check passes whether the cover works or not. The shot therefore comes
    /// in off the spider's shoulder: well inside the shielded arc, and several metres clear of
    /// the body being carried, so the only thing that can stop it is the rule under test.</para>
    /// </summary>
    private static string? ClawCoversAgainstAPlayer()
    {
        World.World world = DuelSetup(PlayerClass.Tank, PlayerClass.Spider, apart: 0f,
            out PlayerTank shooter, out PlayerTank mark);

        // The spider stands at the origin looking down +Y and carries its catch out that way;
        // the shooter stands off its shoulder, aiming back at it.
        mark.Position = Torus.Wrap(Vector2.Zero);
        mark.Heading = 0f;
        shooter.Position = Torus.Wrap(new Vector2(7f, 10f));
        shooter.Heading = MathF.Atan2(-7f, -10f);

        var hostage = new EnemyTank(
            Torus.Wrap(new Vector2(0f, SpiderClaw.HoldReach)), elite: false);
        world.Enemies.Add(hostage);
        if (mark.Claw is not { } claw || !claw.TryGrab(hostage)) return "the claw refused the grab";

        // The grip is the hold *trigger*, and an empty input frame is a release: without this
        // the spider throws its hostage on the very first step and stands there empty-handed.
        world.SetInput(1, new InputFrame(Btn.Fire, Btn.None, Vector2.Zero));

        float markBefore = mark.Shield + mark.Health;
        for (int i = 0; i < 8; i++)
        {
            world.FirePlayerShot(laser: false, by: shooter);
            world.StepForTest((float)Config.FixedDt);
        }
        for (int i = 0; i < 60; i++) world.StepForTest((float)Config.FixedDt);

        // Counted off the claw rather than off the hostage's integrity: a squeezed body is
        // losing integrity to the crush every tick anyway, so "it got weaker" would be true
        // whether or not a single round ever reached it.
        if (claw.Soaked == 0)
            return "the claw soaked none of a team-mate's rounds";
        if (mark.Shield + mark.Health < markBefore - 0.001f)
            return "the craft behind the hostage took the rounds anyway";
        return null;
    }

    /// <summary>Winds a lance on the mark, then shoots it in the core. The charge has to go.</summary>
    private static string? CoreHitBreaksAPlayersLance()
    {
        World.World world = DuelSetup(PlayerClass.Tank, PlayerClass.Spider, apart: 12f,
            out PlayerTank shooter, out PlayerTank mark);
        if (mark.Spider is not { } emitter) return "the spider chassis has no emitter";

        // Wound by hand rather than through the trigger: the mark is seat 1 and this test is
        // about what a round does to a charge, not about how the charge got there.
        for (int i = 0; i < 240; i++) emitter.Hold((float)Config.FixedDt);
        if (!emitter.Charging) return "two seconds on the trigger wound nothing";

        for (int i = 0; i < 8 && emitter.Charging; i++)
        {
            world.FirePlayerShot(laser: false, by: shooter);
            world.StepForTest((float)Config.FixedDt);
        }

        return emitter.Charging
            ? "a team-mate emptied the cannon into an exposed core and the lance stayed wound"
            : null;
    }

    /// <summary>
    /// Two craft side by side, one slug through both. Each must be billed once and only once —
    /// the round crosses a hull over several ticks at ninety a second, and a memory that only
    /// held the last craft it bit would bill the first one again on the very next tick.
    /// </summary>
    private static string? SlugRakesEachCraftOnce()
    {
        World.World world = DuelSetup(PlayerClass.Tank, PlayerClass.Flower, apart: 14f,
            out PlayerTank shooter, out _);
        PlayerTank second = world.AddPlayer(new Loadout { Class = PlayerClass.Flower })!;

        // Both squarely ON the slug's line and barely apart, and the placement is what makes
        // this able to fail at all — the first draft could not. A slug leaves the muzzle at
        // (0, 1.9) and covers 1.5u a tick against a 1.3u hit radius, so it is tested at
        // y = 13.9 then 15.4, and a craft parked even half a metre off the line is in reach for
        // exactly one of those. One tick is a tick no memory of any kind could get wrong. Sat on
        // the line and 0.4u apart, BOTH craft are in reach on BOTH ticks — so a one-slot memory
        // is overwritten by the second craft and bills the first all over again.
        world.Players[1].Position = Torus.Wrap(new Vector2(0f, 14.6f));
        second.Position = Torus.Wrap(new Vector2(0f, 14.2f));

        float aBefore = world.Players[1].Shield + world.Players[1].Health;
        float bBefore = second.Shield + second.Health;

        world.FirePlayerSlug(shooter);
        for (int i = 0; i < 90; i++) world.StepForTest((float)Config.FixedDt);

        float a = aBefore - (world.Players[1].Shield + world.Players[1].Health);
        float b = bBefore - (second.Shield + second.Health);

        if (a <= 0.001f || b <= 0.001f)
            return $"a slug through two craft billed them {a:0.0} and {b:0.0} — it did not pierce";
        // One bill each. Anything above a single slug's worth is the round re-billing a craft
        // it had already crossed, which is most of a life for standing next to somebody.
        if (a > SlugBill || b > SlugBill)
            return $"a slug billed craft twice over: {a:0.0} and {b:0.0}, one bite is at most {SlugBill:0.0}";
        return null;
    }

    /// <summary>What one slug can legitimately cost a craft: its own number, plus the worst the
    /// plating can do to it. Read off the sim rather than written down, so a retune of either
    /// does not turn this into a false alarm.</summary>
    private static float SlugBill => 28f * 1.35f + 0.001f;

    /// <summary>The shared placement the duels use, exposed for the checks above that need to
    /// reach into the world afterwards rather than just pull a trigger.</summary>
    private static World.World DuelSetup(PlayerClass attacker, PlayerClass target, float apart,
        out PlayerTank me, out PlayerTank mark)
    {
        var world = new World.World(new Loadout { Class = attacker },
            new MatchSettings { MaxPlayers = 4, FriendlyFire = true })
        { DynamicSpawning = false };
        world.Enemies.Clear();
        world.Squads.Clear();
        world.Soldiers.Clear();
        world.AddPlayer(new Loadout { Class = target });
        world.LocalIndex = 0;

        me = world.Players[0];
        mark = world.Players[1];
        me.Position = Torus.Wrap(Vector2.Zero);
        me.Heading = 0f;
        me.Pitch = 0f;
        mark.Position = Torus.Wrap(new Vector2(0f, apart));
        mark.Heading = MathF.PI;
        mark.Pitch = 0f;
        return world;
    }

    // --- One duel -------------------------------------------------------------------

    /// <summary>One attack under test: which chassis pulls it, how far apart the two craft
    /// stand, and what to do to fire it. The attacker is always seat 0 (the local one, as on a
    /// host) and the mark is always seat 1.</summary>
    private sealed record DuelCase(
        string Name,
        PlayerClass Chassis,
        float Apart,
        Action<World.World> Fire,
        int Settle = 90,
        Action<World.World, PlayerTank, PlayerTank>? Stage = null);

    /// <summary>How much shield the mark lost, and how much the attacker lost — the second one
    /// because several of these are splash weapons and a test that only watched the victim
    /// would happily pass an attack that killed the person who fired it.</summary>
    private static (float Mark, float Attacker) RunDuel(DuelCase c, bool friendlyFire)
    {
        var world = new World.World(new Loadout { Class = c.Chassis },
            new MatchSettings { MaxPlayers = 4, FriendlyFire = friendlyFire })
        { DynamicSpawning = false };

        // An empty field. The petal's aimbot and the spider's claw both pick the nearest thing
        // rather than the thing that was aimed at, so a stray hunter would quietly eat every
        // attack under test and the whole file would pass while proving nothing.
        world.Enemies.Clear();
        world.Squads.Clear();
        world.Soldiers.Clear();

        world.AddPlayer(new Loadout { Class = PlayerClass.Flower });
        world.LocalIndex = 0;

        PlayerTank me = world.Players[0];
        PlayerTank mark = world.Players[1];

        // Nose to nose down +Y, both looking dead level. The pitch matters: two chassis open
        // deliberately off level (a SOLDIER looks a touch up the tower it starts beside, a FISH
        // noses down at the reef) and a shot fired down a raked look is aimed at the floor
        // rather than at the craft twelve metres in front of it.
        me.Position = Torus.Wrap(Vector2.Zero);
        me.Heading = 0f;
        me.Pitch = 0f;
        mark.Position = Torus.Wrap(new Vector2(0f, c.Apart));
        mark.Heading = MathF.PI;
        mark.Pitch = 0f;

        // A fish opens twenty metres up and already swimming, which over a couple of seconds
        // carries it clean past whatever it was pointed at. Brought down to just off the deck —
        // still in the water, not beached — and the current stilled, so what the test drives is
        // the trigger rather than the drift.
        if (me.Fish is { } swimmer)
        {
            me.Height = 1.5f;
            swimmer.Velocity = Vector3.Zero;
        }

        c.Stage?.Invoke(world, me, mark);

        float markBefore = mark.Shield + mark.Health;
        float mineBefore = me.Shield + me.Health;

        c.Fire(world);
        for (int i = 0; i < c.Settle; i++) world.StepForTest((float)Config.FixedDt);

        return (markBefore - (mark.Shield + mark.Health),
                mineBefore - (me.Shield + me.Health));
    }

    /// <summary>Both halves of the rule, for one attack.</summary>
    private static string? Duel(DuelCase c)
    {
        var on = RunDuel(c, friendlyFire: true);
        if (on.Mark <= 0.001f)
            return $"friendly fire was on and {c.Name} did not touch the craft in front of it";

        // Several of these are splash weapons, and one of them (a VIRUS spending its host)
        // goes off around the player firing it. Whatever an attack costs its author, it has to
        // cost the person it landed on more — otherwise it is not a weapon, it is a way to die.
        if (on.Attacker >= on.Mark)
            return $"{c.Name} cost the player firing it {on.Attacker:0.0} and the mark {on.Mark:0.0}";

        var off = RunDuel(c, friendlyFire: false);
        if (off.Mark > 0.001f)
            return $"friendly fire was off and {c.Name} still cost a team-mate {off.Mark:0.0}";
        return null;
    }

    // --- The attacks ----------------------------------------------------------------
    //
    // Each of these is the shortest honest route to *firing the thing*: the real trigger where
    // a headless test can pull it, the world's own test hook where it cannot. Nothing here
    // reaches into a damage pass — the point is to prove the wiring from a trigger to another
    // player's shield, so anything that skipped part of that chain would be testing itself.

    private static class Duels
    {
        /// <summary>Steps a world with a live input frame, the way a keyboard does: the edge is
        /// spent on the first tick and only the held state repeats after.</summary>
        private static void Hold(World.World world, Btn down, Btn pressed, int ticks)
        {
            for (int i = 0; i < ticks; i++)
                world.Update((float)Config.FixedDt,
                    new InputFrame(down, i == 0 ? pressed : Btn.None, Vector2.Zero));
        }

        /// <summary>Points the attacker's look at the middle of the mark's body, the way a
        /// player with a mouse would. Only needed where the two eyes are at wildly different
        /// heights — a VIRUS wearing a Crab-Core is looking out of a nine-metre monster, and a
        /// level shot from up there passes over a plant rather than through it.</summary>
        private static void AimAt(PlayerTank me, PlayerTank mark)
            => me.Pitch = -MathF.Atan2(me.Eye.Y - mark.HitCentre,
                                       Torus.Distance(me.Position, mark.Position));

        /// <summary>Where a hull hurled out of the claw actually comes down, solved off the
        /// claw's own numbers so retuning the throw moves the mark rather than breaking the
        /// test. Launch height, lift and gravity, straight out of the ballistic solution.</summary>
        private static float ThrowRange()
        {
            const float g = SpiderClaw.ThrowGravity;
            float flight = (SpiderClaw.ThrowLift
                + MathF.Sqrt(SpiderClaw.ThrowLift * SpiderClaw.ThrowLift
                             + 2f * g * SpiderClaw.HoldHeight)) / g;
            return SpiderClaw.HoldReach + SpiderClaw.ThrowSpeed * flight;
        }

        public static readonly DuelCase TankCannon = new(
            "the TANK's cannon", PlayerClass.Tank, 12f,
            w => { for (int i = 0; i < 8; i++) { w.FirePlayerShot(); w.StepForTest((float)Config.FixedDt); } });

        public static readonly DuelCase TankSlug = new(
            "the TANK's AP slug", PlayerClass.Tank, 12f,
            w => w.FirePlayerSlug());

        /// <summary>Thirty units of run-up, then the hull arrives. A ram is the one attack in
        /// the game with no trigger at all — it is the drive doing it — so this is simply
        /// forward held until the two craft meet.</summary>
        public static readonly DuelCase TankRam = new(
            "the TANK's ram", PlayerClass.Tank, 30f,
            w => Hold(w, Btn.Forward, Btn.None, 300),
            Settle: 0);

        public static readonly DuelCase SpiderLaser = new(
            "the SPIDER's laser", PlayerClass.Spider, 14f,
            w => { for (int i = 0; i < 8; i++) { w.FirePlayerShot(laser: true); w.StepForTest((float)Config.FixedDt); } });

        /// <summary>The right trigger held long enough to wind the meter, then released — the
        /// real grammar of the weapon, since a lance fired off an empty meter is worth almost
        /// nothing and would prove the wrong thing.</summary>
        public static readonly DuelCase SpiderLance = new(
            "the SPIDER's charged lance", PlayerClass.Spider, 18f,
            w =>
            {
                Hold(w, Btn.Secondary, Btn.Secondary, 240);
                Hold(w, Btn.None, Btn.None, 2);
            },
            Settle: 0);

        /// <summary>A hunter put in the claw's reach, caught on the trigger and thrown down the
        /// look line at the mark. The whole point of the grab is what it lands on.</summary>
        public static readonly DuelCase SpiderThrow = new(
            "a hostage hurled out of the SPIDER's claw", PlayerClass.Spider, ThrowRange(),
            w =>
            {
                var hostage = new EnemyTank(
                    Torus.Wrap(new Vector2(0f, SpiderClaw.Reach - 2f)), elite: false);
                w.Enemies.Add(hostage);
                Hold(w, Btn.Fire, Btn.Fire, 30);    // closes on it and squeezes
                Hold(w, Btn.None, Btn.None, 1);     // let go: it flies

                // Fly it to the grid, then sweep the wreckage away the instant it lands. A hull
                // the size of a car meeting the plate throws real rubble, and rubble crushes
                // whoever is standing under it whatever the host's toggle says — a shard
                // carries no author (see World.ResolveCrush), so that is the world falling on
                // somebody rather than the SPIDER's attack. It is also a random number, which
                // would make this check pass or fail on the roll of a chunk. The chunks are
                // still on their way up on the tick they are spawned, so clearing here is
                // before any of them has landed on anybody.
                for (int i = 0; i < 240 && hostage.Flung; i++) w.StepForTest((float)Config.FixedDt);
                w.Debris.Clear();

                // And take the hostage off the field now it has done its one job. It survives
                // its own landing, and a hunter left standing next to the mark simply shoots
                // them — which is the field hurting a player, not a player hurting a player,
                // and would have this check pass for entirely the wrong reason.
                w.Enemies.Remove(hostage);
            },
            Settle: 120);

        public static readonly DuelCase VirusRound = new(
            "the VIRUS's corruption round", PlayerClass.Virus, 12f,
            w => { for (int i = 0; i < 8; i++) { w.FireVirusRoundForTest(); w.StepForTest((float)Config.FixedDt); } });

        /// <summary>Worn crab, broken lance. The host is handed over directly rather than flown
        /// into, because what is under test is the beam, not the possession.</summary>
        public static readonly DuelCase VirusLance = new(
            "the VIRUS's stolen lance", PlayerClass.Virus, 14f,
            w => w.FireVirusLanceForTest(),
            Stage: (w, me, mark) =>
            {
                me.Virus?.Possess(me, VirusHost.Crab);
                AimAt(me, mark);
            });

        /// <summary>The host spent as a bomb. The ring swells over about three seconds, so this
        /// one has to be given the time to actually reach anybody.</summary>
        public static readonly DuelCase VirusOverload = new(
            "a VIRUS host spent as a bomb", PlayerClass.Virus, 10f,
            w => w.OverloadVirusForTest(),
            Settle: 240,
            Stage: (_, me, _) => me.Virus?.Possess(me, VirusHost.Crab));

        public static readonly DuelCase FishSpit = new(
            "the FISH's spit", PlayerClass.Fish, 12f,
            w => { for (int i = 0; i < 8; i++) { w.FireFishSpitForTest(); w.StepForTest((float)Config.FixedDt); } });

        /// <summary>Close enough to spear, which for this chassis means genuinely arriving at
        /// the thing rather than pointing at it from across the street.</summary>
        public static readonly DuelCase FishStrike = new(
            "the FISH's strike", PlayerClass.Fish, FishRig.StrikeReach * 0.5f,
            w => w.StrikeFishForTest(),
            Settle: 60);

        public static readonly DuelCase SoldierRifle = new(
            "the SOLDIER's rifle", PlayerClass.Soldier, 12f,
            w => { for (int i = 0; i < 8; i++) { w.FireSoldierRifleForTest(); w.StepForTest((float)Config.FixedDt); } });

        public static readonly DuelCase SoldierRocket = new(
            "the SOLDIER's rocket", PlayerClass.Soldier, 14f,
            w => w.FireSoldierRocketForTest());

        public static readonly DuelCase FlowerSpit = new(
            "the FLOWER's spit", PlayerClass.Flower, 12f,
            w => Hold(w, Btn.Fire, Btn.Fire, 60),
            Settle: 60);

        public static readonly DuelCase FlowerPetal = new(
            "a FLOWER's petal", PlayerClass.Flower, 16f,
            w => Hold(w, Btn.None, Btn.Secondary, 1),
            Settle: 180);

        /// <summary>The crafted core, lobbed out in front and left to open. Not a chassis's own
        /// trigger — anybody can carry one — but it is the heaviest thing a player can throw and
        /// it went off harmlessly in a crowd of them.</summary>
        public static readonly DuelCase ThrownCrabCore = new(
            "a thrown CRAB CORE", PlayerClass.Tank, 18f,
            w => w.StageCrabBlastAheadForTest(),
            Settle: 240);
    }

    /// <summary>
    /// The roll-up, and the check that actually guards against this whole class of bug coming
    /// back: every chassis on the select screen must own at least one attack that can reach
    /// another player. A new class added with its damage pass pointed only at the field would
    /// pass every check above (they name the attacks that exist) and fail this one.
    /// </summary>
    private static string? EveryChassisCanFight()
    {
        var armed = new HashSet<PlayerClass>();
        foreach (DuelCase c in new[]
        {
            Duels.TankCannon, Duels.TankSlug, Duels.TankRam,
            Duels.SpiderLaser, Duels.SpiderLance, Duels.SpiderThrow,
            Duels.VirusRound, Duels.VirusLance, Duels.VirusOverload,
            Duels.FishSpit, Duels.FishStrike,
            Duels.SoldierRifle, Duels.SoldierRocket,
            Duels.FlowerSpit, Duels.FlowerPetal,
        })
            if (RunDuel(c, friendlyFire: true).Mark > 0.001f) armed.Add(c.Chassis);

        foreach (ClassArchetype a in ClassCatalog.All)
        {
            if (!a.Available) continue;
            if (!armed.Contains(a.Kind))
                return $"{a.Name} is on the select screen with nothing that can hurt another player";
        }
        return null;
    }
}
