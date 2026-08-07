using System.Numerics;
using Unrendered.Core;
using Unrendered.Entities;

namespace Unrendered.World;

/// <summary>
/// Everything the world does on behalf of the FLOWER: the triggers, the petals in flight, the
/// harvest, and the replant's one moment of actually moving something.
///
/// <para>Its own file for the same reason the rig is its own class — the chassis asks the world
/// for a set of things nothing else asks for. Only two of them are interesting:</para>
///
/// <para><b>The petals are steered here, not in the rig.</b> A petal chases; chasing means
/// knowing what is worth chasing; and the rig, like every other rig in this game, deliberately
/// knows nothing about enemies. So the rig owns what a petal <em>is</em> (where, how fast, how
/// hard it can turn) and this owns what a petal <em>wants</em>, which is the only half that
/// needs the field.</para>
///
/// <para><b>The replant is the world's to land.</b> The rig runs the four states and raises a
/// flag on the tick the seed should surface; where it actually surfaces is this file's call,
/// because the rig has no idea whether the ground the player picked has a tower standing on
/// it. Same division the fish's beaching and the soldier's anchors are built on.</para>
/// </summary>
public sealed partial class World
{
    // --- Triggers ---------------------------------------------------------------------

    /// <summary>
    /// The FLOWER's turn at the buttons. Nothing is read while a replant owns the body — a
    /// seed under the plate has no gun and no petals — and nothing is read while a cinematic
    /// has the craft, exactly as on every other chassis.
    /// </summary>
    private void UpdateFlowerTriggers(FlowerRig stalk, in InputFrame input, PlayerTank who)
    {
        if (who.Captured) return;

        // Held to choose ground, released to grow there. The marker the player is aiming with
        // is recomputed every tick from the live look (see FlowerAimPoint), so the ground moves
        // under the reticle as they turn rather than being fixed at the moment of the press.
        bool wants = input.ReplantDown;
        bool wasAiming = _replantAiming.Contains(who);
        if (wants && !stalk.Replanting)
        {
            _replantAiming.Add(who);
        }
        else if (wasAiming)
        {
            _replantAiming.Remove(who);
            // Released — and it always goes. There is no longer anything to be short of: the
            // replant is this chassis's movement rather than one of its tricks, and it is free
            // (see FlowerRig.ReplantCost). What stops a player hopping forever is the transit,
            // which they spend underground with no gun.
            if (!wants && !stalk.Replanting)
                stalk.BeginReplant(FlowerAimPoint(who), who.Position);
        }

        if (stalk.Replanting) return;

        if (input.HarvestPressed) HarvestFlower(who, stalk);

        // The petal before the seed, on a frame both are down: the committed attack wins, the
        // same rule the fish's strike beats its spit by.
        if (input.PetalPressed) ThrowPetal(who, stalk);
        else if (input.SeedDown) FireSeed(who);
    }

    /// <summary>
    /// Which players are currently holding the replant key, so the renderer can draw their
    /// marker and the release can be told from a key that was never down. A set rather than a
    /// flag on the rig because it is a fact about a <em>keyboard</em>, not about a plant: it
    /// never crosses the wire, and a remote flower's aim is nobody else's business until the
    /// moment it commits.
    /// </summary>
    private readonly HashSet<PlayerTank> _replantAiming = new();

    /// <summary>The headless test's hand on the stalk, exactly as <c>ScriptedFishMove</c> is its
    /// hand on the fish. Only ever drives the local craft, which is the only one it has.</summary>
    public Vector2? ScriptedFlowerLean;

    /// <summary>True while this craft is picking ground — what the HUD and the world renderer
    /// read to draw the reticle on the grid.</summary>
    public bool IsChoosingGround(PlayerTank who) => _replantAiming.Contains(who);

    /// <summary>
    /// Where a flower's look meets the grid, clamped to the class's reach and pulled out of any
    /// wall it landed in.
    ///
    /// <para>Looking <em>up</em> is the interesting case. A ray aimed above the horizon never
    /// meets the floor, and the obvious answers — refuse it, or clamp the pitch — both make the
    /// ability feel broken at exactly the moment a player is trying to leave in a hurry. So a
    /// level or raised look throws the marker to maximum range along the heading, which is what
    /// the player meant: "over there, as far as this goes".</para>
    /// </summary>
    public Vector2 FlowerAimPoint(PlayerTank who)
    {
        Vector3 eye = who.Eye;
        Vector3 look = who.Forward3;

        float reach = FlowerRig.ReplantRange;
        // Down-looking: solve where the line crosses the plate, and take the nearer of that and
        // the class's reach. Anything else: the full reach along the heading.
        if (look.Y < -0.05f)
        {
            float t = eye.Y / -look.Y;
            reach = MathF.Min(reach, t);
        }

        var flat = new Vector2(look.X, look.Z);
        if (flat.LengthSquared() < 1e-6f) flat = who.Forward;
        flat = Vector2.Normalize(flat);

        return ReachablePoint(Torus.Wrap(who.Muzzle + flat * reach));
    }

    /// <summary>The FLOWER's ordinary round: a seed down the eye's line, off the head rather
    /// than off the root, so a leaning plant genuinely fires from where its head is.</summary>
    private void FireSeed(PlayerTank who)
    {
        if (HeldInClaw(who)) return;
        if (!who.TryFireSeed(out Vector3 origin, out Vector3 dir)) return;

        // Inside the Maw-Core's throat every trigger is the escape trigger, on every chassis.
        if (SwallowedIn(who) is { Held: true } digestion)
        {
            digestion.RegisterShot();
            Emit(Cue.FishSpit, who.Position, owner: Seat(who));
            return;
        }

        SpawnDirected(origin, dir, Projectile.RifleSpeed, rocket: false, owner: Seat(who));
        Emit(Cue.Detonation, who.Muzzle, owner: Seat(who));
        who.Flower?.Kick(0.02f);
    }

    /// <summary>
    /// Throws one petal down the look. The rig decides whether there is one to throw and
    /// spends it; this only places it out past the plant's own body so it does not start
    /// inside the head it left.
    /// </summary>
    private void ThrowPetal(PlayerTank who, FlowerRig stalk)
    {
        if (HeldInClaw(who) || SwallowedIn(who) is { Held: true }) return;

        Vector3 look = who.Forward3;
        var muzzle = new Vector3(who.Muzzle.X, who.Eye.Y, who.Muzzle.Y);
        stalk.Throw(muzzle + look * (PlayerTank.Radius + 0.8f), look);
    }

    /// <summary>
    /// Spends a ripe head: three things on the grid around the plant. The rig decides whether
    /// the head is ripe and empties it; this rolls what actually falls and puts it down.
    ///
    /// <para>Client-side this runs to the point of emptying the bar and no further, exactly like
    /// every other inventory action in this game — a client that invented three pieces of
    /// salvage would watch them vanish on the next snapshot. The bar emptying instantly is what
    /// makes the button feel answered; the host decides what was actually in the head.</para>
    /// </summary>
    private void HarvestFlower(PlayerTank who, FlowerRig stalk)
    {
        if (!stalk.Harvest(who.Muzzle)) return;
        if (!Authoritative) return;

        for (int i = 0; i < FlowerRig.HarvestYield; i++)
        {
            // Scattered around the root on a ring rather than dropped in a pile, so three
            // things read as three things rather than as one flickering pickup.
            //
            // The radius is the whole design of the drop and it is tightly bounded: it has to
            // be big enough that the crop visibly <em>falls out</em> of the plant onto the
            // grid, and small enough to stay inside the craft's own collection reach — because
            // this player cannot walk over and get it. Anything past
            // Pickup.Radius + PlayerTank.Radius is a harvest the plant can see and not have,
            // which on a rooted chassis is not a cost, it is a bug.
            float bearing = Random.Shared.NextSingle() * MathF.Tau;
            float range = 1.2f + Random.Shared.NextSingle()
                * (Pickup.Radius + PlayerTank.Radius - 1.6f);
            Vector2 at = Torus.Wrap(who.Position
                + new Vector2(MathF.Sin(bearing), MathF.Cos(bearing)) * range);
            TossSalvage(at, RollHarvest());
        }
    }

    /// <summary>
    /// What comes out of a ripe head, as a weighted roll.
    ///
    /// <para>The weights are the whole economy of the class in one table, and they are set to
    /// make the harvest a <em>supply line</em> rather than a jackpot: mostly the two things
    /// every craft burns continuously — rounds and the metals a cell is made of — and rarely
    /// something that would otherwise mean crossing the map. There is deliberately no crab
    /// fragment on this table. A fragment comes off a dead boss, and a class that could grow
    /// them would quietly delete the reason anybody fights one.</para>
    /// </summary>
    private static PickupKind RollHarvest()
    {
        int roll = Random.Shared.Next(100);
        return roll switch
        {
            < 26 => PickupKind.Ammo,           // the commonest need, and the commonest yield
            < 44 => PickupKind.ScrapMetal,
            < 56 => PickupKind.SpaceGunpowder,
            < 66 => PickupKind.CopperWire,
            < 74 => PickupKind.DenseAlloy,
            < 80 => PickupKind.Lead,
            < 86 => PickupKind.Zinc,
            < 91 => PickupKind.Lithium,
            < 96 => PickupKind.Battery,        // scarce: a whole charge, grown
            < 99 => PickupKind.RepairKit,      // rare, and the only hull in the game you can farm
            // And once in a hundred heads, a piece of the moon. This is the only way to get one
            // without being out under a night sky when it falls, and it belongs here rather than
            // anywhere else on the roster for a reason worth stating: the FLOWER is the chassis
            // that grows on <em>light</em> (see FlowerRig.RipenRateAt), and on the one world
            // where the crop ripens slowest, the thing overhead that is stopping it is the thing
            // this occasionally turns out to be a piece of. It is a joke the game never explains.
            _ => PickupKind.MoonFragment,
        };
    }

    // --- Petals in flight ---------------------------------------------------------------

    /// <summary>
    /// How far out a petal will look for something to chase. Generous, because the alternative
    /// is a weapon that only works if the player was already aimed — and if a player wanted to
    /// aim, they had a gun.
    /// </summary>
    private const float PetalLockRange = 58f;

    /// <summary>
    /// What one bites. Against a hunter's three-to-five points of shield it is a clean
    /// one-shot, so a full ring emptied into a crowd is a dozen dead hunters if every throw is
    /// caught — and a petal carves <em>through</em> a line rather than stopping at the first
    /// body, so in practice it is more than that.
    ///
    /// <para>Left where it was when the ring doubled from six to twelve, which is worth a
    /// sentence: that doubled this class's burst output outright. If it turns out to need
    /// pulling back, pull it back <em>here</em> or at
    /// <see cref="FlowerRig.ThrowInterval"/> rather than at the petal count — the count is what
    /// the head and the panel are drawn around, and it is the thing the player reads.</para>
    /// </summary>
    private const float PetalDamage = 6f;

    /// <summary>What a petal does to a player it crosses. Heavy — this is the single hardest
    /// hit anything in the game lands on a craft short of a boss beam — and it has to be: a
    /// weapon this slow, this visible and this expensive to lose must be worth being hit by.</summary>
    private const float PetalPlayerDamage = 24f;

    /// <summary>The rosette's reach, and what it deals to everything else standing in it.</summary>
    private const float BloomRadius = 5.5f;
    private const float BloomDamage = 2f;

    /// <summary>And what the same rosette costs a craft caught at the edge of one. Small on
    /// purpose: the petal itself carries the kill (see <see cref="PetalPlayerDamage"/>), and
    /// this is only what standing next to whoever caught it is worth.</summary>
    private const float BloomPlayerDamage = 6f;

    /// <summary>How long a petal will not re-bite the same thing after biting it. Long enough
    /// that crossing a hull over several ticks scores once; short enough that the return leg
    /// through the same hunter scores again, which is the whole reason it is a boomerang.</summary>
    private const float PetalRefractory = 0.5f;

    /// <summary>How wide a petal's bite is.</summary>
    private const float PetalHitRadius = 2.0f;

    /// <summary>
    /// Flies every petal a seat has in the air: pick something worth chasing, turn toward it as
    /// hard as the blade allows, move, and see what it crossed.
    ///
    /// <para>Host-only, like every other pass that decides what got hurt. A client's own petals
    /// are still <em>drawn</em> flying — the rig steps them locally so the weapon feels instant
    /// in the player's hands — but nothing a client's petal touches is hurt until the host has
    /// said so.</para>
    /// </summary>
    private void UpdateFlowerPetals(PlayerTank who, FlowerRig stalk, float dt)
    {
        int seat = Seat(who);
        Vector2 home = who.Muzzle;

        foreach (var petal in stalk.Petals)
        {
            if (!petal.InAir) continue;

            // The outbound leg has run: turn for home whatever it was chasing. Everything on
            // the way back is incidental — the petal's job now is to be caught.
            if (petal.State == FlowerRig.PetalState.Outbound && petal.Life >= FlowerRig.OutTime)
                FlowerRig.SendHome(petal);

            // What it wants. Homing, that is always the plant; outbound, it is whatever is
            // nearest and worth killing, and straight ahead if there is nothing.
            Vector3 want = petal.State == FlowerRig.PetalState.Homing
                ? new Vector3(Torus.Delta(petal.Position, home).X,
                              (who.Eye.Y - petal.Height) * 2f,
                              Torus.Delta(petal.Position, home).Y)
                : SeekTarget(petal, seat);

            petal.Steer(want, dt, FlowerRig.TurnRate);
            petal.Advance(dt);

            // A tower stops it dead. A blade is not a bullet — it does not go off, it simply
            // buries itself in the wall and is not coming back, which is most of what makes
            // throwing petals down a street a different decision from throwing them across
            // open grid.
            if (PetalStruckStructure(petal)) { StageBloom(petal.Position, petal.Height, seat, hitSomething: false); stalk.Lose(petal); continue; }

            if (BitePetalTargets(petal, stalk, seat, who)) continue;

            // Home. Caught only on the way back — a petal cannot be caught on the tick it is
            // thrown, which it otherwise would be, since it starts a metre from the head.
            if (petal.State == FlowerRig.PetalState.Homing
                && Torus.DistanceSquared(petal.Position, home) < FlowerRig.CatchRadius * FlowerRig.CatchRadius)
            {
                stalk.Catch(petal, home);
                continue;
            }

            // Out of flight. It drops wherever it got to and grows back on the slow clock.
            if (petal.Life >= FlowerRig.MaxFlight) stalk.Lose(petal);
        }
    }

    /// <summary>
    /// The aimbot's one decision: what this petal is going for. Nearest wins, measured from the
    /// petal rather than from the plant, so a blade already halfway across the field re-targets
    /// onto whatever it has ended up near instead of doggedly returning to the thing it was
    /// thrown at.
    ///
    /// <para>Returns a direction, not a position, because the caller wants a heading and the
    /// torus means a "position" is ambiguous by a map width.</para>
    /// </summary>
    private Vector3 SeekTarget(FlowerRig.Petal petal, int seat)
    {
        float best = PetalLockRange * PetalLockRange;
        Vector3 found = default;
        bool any = false;

        void Consider(Vector2 at, float height)
        {
            Vector2 d = Torus.Delta(petal.Position, at);
            float sq = d.LengthSquared();
            if (sq >= best) return;
            best = sq;
            found = new Vector3(d.X, (height - petal.Height) * 1.5f, d.Y);
            any = true;
        }

        foreach (var e in Enemies)
            if (e.Alive) Consider(e.Position, 1.0f);

        foreach (var squad in Squads)
            foreach (var s in squad.Members)
                if (s.Alive) Consider(s.Position, s.Height);

        if (Boss is { Alive: true } boss) Consider(boss.Position, CrabRig.CoreWorldY);
        if (Maw is { Alive: true } maw) Consider(maw.Position, MawRig.CrystalWorldY);

        // Other players, but only when friendly fire is on and only the ones that are not us.
        // A petal that chased team-mates around a room would be the single most hated object
        // in this game.
        if (Match.FriendlyFire)
            for (int i = 0; i < Players.Count; i++)
            {
                if (i == seat) continue;
                PlayerTank p = Players[i];
                if (!p.Alive || p.Away) continue;
                Consider(p.Muzzle, p.EyeHeight + p.Height);
            }

        // Nothing in range: fly the line it was thrown on.
        return any ? found : new Vector3(petal.Direction.X, 0f, petal.Direction.Y);
    }

    /// <summary>Whether a petal has flown into the skyline. Reuses the same blocker geometry
    /// every round is tested against, so a wall that stops a bullet stops a blade.</summary>
    private bool PetalStruckStructure(FlowerRig.Petal petal)
    {
        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        foreach (var s in Structures)
        {
            if (petal.Height > s.BlockHeight) continue;
            int n = s.Blockers(blockers);
            for (int b = 0; b < n; b++)
                if (Torus.DistanceSquared(petal.Position, blockers[b].At)
                    <= blockers[b].Radius * blockers[b].Radius) return true;
        }
        return false;
    }

    /// <summary>
    /// What this petal crossed this tick, and what that cost. Returns true when the petal is no
    /// longer in the air — which currently only happens when it buries itself in a player, since
    /// everything else it merely passes through.
    ///
    /// <para>It passes through, deliberately. A boomerang that stopped at the first hunter would
    /// be a slow bullet; one that carves a line through four of them on the way out and four
    /// more on the way back is the reason this class trades away the ability to walk.</para>
    /// </summary>
    private bool BitePetalTargets(FlowerRig.Petal petal, FlowerRig stalk, int seat, PlayerTank owner)
    {
        if (petal.Refractory > 0f) return false;

        bool bit = false;

        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            if (!WithinHit(petal.Position, e.Position, PetalHitRadius + EnemyTank.Radius)) continue;
            DamageEnemy(e, PetalDamage, seat);
            bit = true;
            break;
        }

        if (!bit)
            foreach (var squad in Squads)
            {
                foreach (var s in squad.Members)
                {
                    if (!s.Alive) continue;
                    if (!WithinHit(petal.Position, s.Position, PetalHitRadius + 1.4f)) continue;
                    if (MathF.Abs(s.Height - petal.Height) > 3.5f) continue;
                    DamageSoldier(s, PetalDamage, petal.Position);
                    bit = true;
                    break;
                }
                if (bit) break;
            }

        // The two crystals. A petal is a physical object arriving at a weak point, which is
        // exactly the case both bosses are built to admit — and unlike the tank's shell it can
        // climb, so this chassis reaches a hanging crystal without leaving the ground it is
        // rooted to. That is not an oversight; it is the only answer a rooted class has to a
        // monster that hovers.
        if (!bit && Boss is { Alive: true } liveBoss && liveBoss.HitsCore(petal.Position, petal.Height))
        {
            if (liveBoss.DamageCore(PetalDamage)) DestroyBoss(liveBoss);
            else Emit(Cue.CoreHit, liveBoss.Position, 1f - liveBoss.CoreFraction);
            bit = true;
        }
        if (!bit && Maw is { Alive: true } liveMaw && liveMaw.HitsCrystal(petal.Position, petal.Height))
        {
            if (liveMaw.DamageCrystal(PetalDamage)) DestroyMaw(liveMaw);
            else Emit(Cue.MawHurt, liveMaw.Position, 1f - liveMaw.CrystalFraction);
            bit = true;
        }

        // The DESCENT bosses, which answer with their whole body rather than one crystal.
        if (!bit && Bosses.Count > 0
            && StrikeBosses(petal.Position, petal.Height, PetalDamage, seat,
                            petal.Position - petal.Direction))
            bit = true;

        // A craft. This is the one thing a petal does not pass through: it goes off in them.
        if (!bit && Match.FriendlyFire)
            for (int i = 0; i < Players.Count; i++)
            {
                if (i == seat) continue;
                PlayerTank p = Players[i];
                if (!p.Alive || p.Away || p.Captured) continue;
                if (!WithinHit(petal.Position, p.Muzzle, PetalHitRadius + PlayerTank.Radius)) continue;
                DamagePlayer(PetalPlayerDamage, p, petal.Position);
                StageBloom(petal.Position, petal.Height, seat, hitSomething: true);
                stalk.Lose(petal);
                return true;
            }

        if (!bit) return false;

        // It bit something and lived. The rosette goes off around it, it will not re-bite for
        // half a second, and — if it was still outbound — it turns for home, because a petal
        // that has done its job should be coming back rather than wandering off looking for a
        // second one it will probably not survive.
        StageBloom(petal.Position, petal.Height, seat, hitSomething: true);
        petal.Refractory = PetalRefractory;
        FlowerRig.SendHome(petal);
        owner.Flower?.Jolt(0.18f);
        return false;
    }

    // --- The rosette --------------------------------------------------------------------

    /// <summary>
    /// A bloom on the field: where a petal went off, how old it is, and how big it opened.
    /// Purely a sim record — the renderer turns it into the six-lobed rosette that opens out
    /// of the grid and fades. Ages and is swept exactly like the tank's smoke.
    /// </summary>
    public sealed class Bloom
    {
        public Vector2 Position;
        public float Height;
        public float Age;
        public readonly float Life;
        public readonly float Radius;

        public Bloom(Vector2 at, float height, float life, float radius)
        {
            Position = at;
            Height = height;
            Life = life;
            Radius = radius;
        }

        public bool Active => Age < Life;
        public void Update(float dt) => Age += dt;

        /// <summary>0..1 through its life. The rosette opens on the first quarter and thins
        /// out over the rest, which is the shape of a flower doing anything.</summary>
        public float Phase => Math.Clamp(Age / Life, 0f, 1f);
    }

    /// <summary>Every rosette currently open. Public so the renderer can walk it; capped, aged
    /// and swept in <see cref="UpdateBlooms"/>.</summary>
    public readonly List<Bloom> Blooms = new();
    private const int MaxBlooms = 12;
    private const float BloomLife = 0.9f;

    /// <summary>
    /// Opens a rosette: the splash, the petal-coloured debris, the cue, and the record the
    /// renderer draws. <paramref name="hitSomething"/> is false for a petal that merely buried
    /// itself in a wall — that gets the picture and the noise but not the damage, because
    /// nothing was hit.
    /// </summary>
    private void StageBloom(Vector2 at, float height, int seat, bool hitSomething)
    {
        while (Blooms.Count >= MaxBlooms) Blooms.RemoveAt(0);
        Blooms.Add(new Bloom(Torus.Wrap(at), MathF.Max(0.4f, height), BloomLife, BloomRadius));

        Debris.Burst(new Vector3(at.X, MathF.Max(0.5f, height), at.Y), Palette.Flag, elite: false);
        Emit(Cue.FlowerBloom, at, owner: seat);

        if (!hitSomething || !Authoritative) return;

        // The splash. Small — the petal itself carries the kill, and this is what everything
        // standing next to the thing it killed gets — but it is what turns a well-thrown blade
        // into an answer to a group rather than to one hunter at a time.
        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            if (!WithinHit(at, e.Position, BloomRadius + EnemyTank.Radius)) continue;
            DamageEnemy(e, BloomDamage, seat);
        }
        DamageSoldiersInBlast(at, height, BloomRadius, BloomDamage);
        HarmPlayersInBlast(at, height, BloomRadius, BloomPlayerDamage, seat);
    }

    /// <summary>Ages the rosettes and sweeps the spent ones. Runs on every machine — a bloom is
    /// a picture, and a client that only opened its own would watch its team-mates' petals go
    /// off silently and invisibly.</summary>
    private void UpdateBlooms(float dt)
    {
        for (int i = Blooms.Count - 1; i >= 0; i--)
        {
            Blooms[i].Update(dt);
            if (!Blooms[i].Active) Blooms.RemoveAt(i);
        }
    }

    // --- The body's own events ------------------------------------------------------------

    /// <summary>
    /// Drains a flower's tick after the physics have run: its cues, its petals, and the one
    /// moment a replant actually moves it. Same shape and the same place in the step as
    /// <c>UpdateFishEvents</c>, for the same reason — the rig stays a pure function of its own
    /// state and the world keeps sole ownership of moving and hurting things.
    /// </summary>
    private void UpdateFlowerEvents(PlayerTank who, FlowerRig stalk, float dt)
    {
        // How dark it is where this plant is standing. Written every tick rather than read by
        // the rig, because a rig that could ask the world what planet it was on would be a rig
        // that needed a world to be tested.
        stalk.Night = Sky.Night;

        DrainCues(stalk.Cues, stalk.Cues.Clear);

        // The seed surfacing. The ground it picked was already pulled out of any wall when the
        // marker was placed, but a tower can have come down since — or gone up, in DESCENT —
        // so it is checked again here, on the tick it matters.
        if (stalk.TakeReplant(out Vector2 to))
        {
            who.Position = Torus.Wrap(ReachablePoint(to));
            who.Height = 0f;
            who.GroundHeight = 0f;
            who.ResetMomentum();
            // Everything that was tracking the plant has just lost it, and everything that was
            // shooting at where it used to be is now shooting at a hole.
            Debris.Burst(new Vector3(who.Position.X, 0.6f, who.Position.Y), Palette.GridNear, elite: false);
        }

        // The petals are the host's to fly, like every other thing on the field that decides
        // what it hits — except our own, which we fly locally so the weapon answers instantly
        // in the hands of whoever threw it. A client's local petals hurt nothing; they are a
        // prediction, and the host's account of the field is what settles the matter.
        if (Authoritative || ReferenceEquals(who, Players[LocalIndex]))
            UpdateFlowerPetals(who, stalk, dt);
    }
}
