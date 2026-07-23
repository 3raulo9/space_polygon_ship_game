using System.Numerics;
using VoidTanks.Core;

namespace VoidTanks.Entities;

/// <summary>Where a strike is in its cycle.</summary>
public enum StrikeState
{
    /// <summary>Nothing wound. The trigger is available.</summary>
    Ready,

    /// <summary>The body gathering. Momentum is being scrubbed and the fish is a sitting
    /// target for a fifth of a second — the price of the shot.</summary>
    Coil,

    /// <summary>Loosed. Travelling in a straight line at a speed nothing else on the
    /// chassis reaches, with the snout lethal for the duration.</summary>
    Lunge,

    /// <summary>Spent. Dragging hard, and refusing to beat until it has settled.</summary>
    Recover,
}

/// <summary>
/// The FISH chassis, and with it the whole of how that chassis moves.
///
/// Every other class in this game crawls on the bottom of the world. The tank drives it,
/// the spider walks it, and even the soldier — who spends most of a run off the ground —
/// is only ever falling between anchors, borrowing height from a city that has to be
/// there to lend it. This one doesn't borrow anything. The premise of the class is that
/// the void is an <em>ocean</em>, the grid is the seabed, the dead alien skyline is a
/// reef, and this is the only thing out here that ever understood that.
///
/// So the physics are fluid physics rather than ballistic ones, and the difference is the
/// whole class:
///
/// <list type="bullet">
/// <item><b>Drag dominates, not gravity.</b> The pull downward is a slow sink you can
/// out-swim, and what actually decides how fast you are going is how recently you beat
/// your tail against the water. Stop beating and you don't fall — you <em>slow</em>, and
/// then you sink.</item>
/// <item><b>Thrust is an impulse, not a throttle.</b> <see cref="Beat"/> is one flick of
/// the tail. Holding a key does nothing at all; chaining beats is what builds speed, and
/// the rhythm of doing it is the moment-to-moment of playing the class.</item>
/// <item><b>Momentum lags the crosshair.</b> Where the fish is looking and where the fish
/// is going are two different vectors that converge slowly, and how fast they converge is
/// set by how far the body is rolled — a banked fish carves, a level one drifts. That gap
/// between aim and travel is the feel, and closing it instantly (as a strafe would) would
/// throw the entire class away.</item>
/// <item><b>There is no ground, only beaching.</b> Touching the grid is not a landing, it
/// is a fish out of water: it flops, it can barely move, and it has to work its way back
/// up. Every other chassis treats the grid as home. This one treats it as drowning.</item>
/// </list>
///
/// Pure state and physics — no Raylib, no world queries. The world decides what a strike
/// ran into and what a wall costs, so the rig itself stays testable without a screen.
/// </summary>
public sealed class FishRig
{
    // --- The tail ---------------------------------------------------------------
    // One beat is one impulse. This is the single decision the whole class rests on:
    // a throttle would make the fish a slower aircraft, whereas an impulse makes speed
    // something the player produces rather than something they hold down, and it gives
    // the body a natural rhythm to animate and the audio a natural event to voice.

    /// <summary>Metres per second one beat adds along the look. Roughly a third of the
    /// top speed, so three good beats is most of a sprint and a single one out of a
    /// drift is a noticeable shove.</summary>
    public const float BeatImpulse = 11f;

    /// <summary>The refractory period between beats. Short enough to be a rhythm rather
    /// than a wait, long enough that mashing gains nothing over timing.</summary>
    public const float BeatInterval = 0.28f;

    /// <summary>What one beat costs the reserve. At the interval above, beating flat out
    /// burns about twenty-five a second — a full reserve buys four seconds of it, which
    /// is a long sprint and nowhere near a flight plan.</summary>
    public const float BeatCost = 7f;

    /// <summary>
    /// How much of a beat is spent lifting rather than driving forward. A fish angles its
    /// whole body to climb, so a beat with the nose up should genuinely gain height — but
    /// a raw look-direction impulse makes climbing strictly better than not climbing,
    /// since altitude is free speed later. This biases a little of every beat upward,
    /// which makes level flight cheap to hold and steep climbs no cheaper than they are.
    /// </summary>
    private const float BeatLift = 1.6f;

    // --- The water --------------------------------------------------------------

    /// <summary>
    /// Quadratic drag. Chosen against the beat above so that continuous beating settles
    /// at about thirty-six metres a second: thrust averages
    /// <see cref="BeatImpulse"/>/<see cref="BeatInterval"/>, and equilibrium is where that
    /// equals <c>Drag · v²</c>. Retune either and the top speed moves with them, which is
    /// the point of writing it down here rather than clamping a number somewhere.
    /// </summary>
    private const float Drag = 0.030f;

    /// <summary>Hard ceiling on the whole velocity vector, strike included. A long dive
    /// down the line of a sink can otherwise stack beats on gravity indefinitely.</summary>
    public const float MaxSpeed = 44f;

    /// <summary>
    /// How fast a dead-stopped fish sinks. Deliberately gentle — this is water, not air,
    /// and a class that plummeted the moment it stopped beating would be a class that
    /// never got to look at anything.
    /// </summary>
    private const float Sink = 3.4f;

    /// <summary>
    /// Planar speed at which the body generates enough lift to nearly cancel the sink.
    /// This is the class's version of the soldier's height-for-speed trade, and it runs
    /// the same way round: momentum is what keeps you up, so the punishment for stalling
    /// is not damage but <em>altitude</em>, paid slowly enough to see coming.
    /// </summary>
    private const float LiftSpeed = 20f;

    /// <summary>How much of the sink a full-speed body cancels. Never all of it: a fish
    /// that could hold altitude forever without beating would make the reserve
    /// decoration.</summary>
    private const float MaxLift = 0.85f;

    /// <summary>What folding the fins (S) multiplies drag by. A brake, not a reverse —
    /// nothing on this chassis goes backwards, and needing to turn round to leave is the
    /// same discipline the tank has always imposed.</summary>
    private const float BrakeDrag = 5.5f;

    // --- The carve --------------------------------------------------------------
    // How momentum catches up with the crosshair. The gap between the two is the entire
    // handling model, and the roll is the throttle on closing it.

    /// <summary>Radians a second the velocity swings toward the look with the body level.
    /// A wide, lazy drift — at cruise this is a turning circle most of a block across,
    /// which is what makes the roll worth reaching for.</summary>
    private const float DriftTurn = 1.15f;

    /// <summary>And what a fully-rolled body adds on top. Banked hard, the fish comes
    /// round about three times as fast — tight enough to thread a reef, and only
    /// available to a player willing to put the horizon on its side.</summary>
    private const float CarveTurn = 2.3f;

    /// <summary>How far the horizon tips at a full carve — forty degrees, well past the
    /// soldier's twenty-five. This is a body rolling onto its side, not an aircraft
    /// banking, and the picture should say so.</summary>
    public const float MaxBank = 0.70f;

    /// <summary>Radians a second the roll itself eases at. Slower than the turn it
    /// enables, so committing to a carve is a decision with a wind-up rather than a
    /// direction the player simply holds.</summary>
    private const float BankRate = 2.6f;

    // --- The strike -------------------------------------------------------------
    // The signature. It turns the movement system into the weapon: the damage is the
    // speed, so the skill is entirely positioning and timing rather than aim.

    /// <summary>The gather before the lunge. Short, but the body is nearly stopped for
    /// all of it, which is what makes a strike thrown at the wrong moment a real
    /// mistake.</summary>
    public const float CoilTime = 0.22f;

    /// <summary>How long the snout is lethal. Long enough at the speed below to cross
    /// twenty metres, which is the range the attack is actually usable at.</summary>
    public const float LungeTime = 0.42f;

    /// <summary>And the settle afterward: dragging hard, unable to beat. The window a
    /// missed strike leaves you hanging in.</summary>
    public const float RecoverTime = 0.5f;

    /// <summary>How fast the lunge travels. Comfortably past anything beating can reach,
    /// so a strike is visibly a different gear.</summary>
    public const float StrikeSpeed = 52f;

    /// <summary>What a strike costs the reserve. A third of it — two in quick succession
    /// is possible and leaves nothing to swim home on.</summary>
    public const float StrikeCost = 34f;

    /// <summary>How far off the snout a lunge reaches. The world sweeps this each tick
    /// the strike is live.</summary>
    public const float StrikeReach = 3.4f;

    /// <summary>What a connecting strike deals. Eight cannon rounds — the same as a
    /// fully-wound spider lance, because it is bought with the same currency: a window
    /// where the player cannot do anything else.</summary>
    public const float StrikeDamage = 8f;

    // --- Beaching ---------------------------------------------------------------

    /// <summary>Arriving at the grid faster than this hurts. A fish is not built to meet
    /// anything solid, so this is well under the soldier's twenty metres.</summary>
    public const float BeachImpactSpeed = 12f;

    /// <summary>Planar speed above which meeting a wall is a crash rather than a scrape,
    /// with everything the swing had built taken away.</summary>
    public const float CrashSpeed = 14f;

    /// <summary>What a beat costs while beached, as a multiple of the usual. Flopping is
    /// expensive: it is the state the whole class is about not being in.</summary>
    private const float BeachCostScale = 2f;

    /// <summary>The upward kick a flop is guaranteed, whatever the look is doing. Two of
    /// them clear a body length, which is enough to be swimming again — a player who
    /// beached should be punished with time and breath, not stranded.</summary>
    private const float FlopLift = 9f;

    /// <summary>And how much of the ordinary forward drive a flop keeps. Very little:
    /// out of the water there is nothing to push against.</summary>
    private const float FlopDrive = 0.4f;

    /// <summary>
    /// How hard the grid scrubs a beached body. Enormous, and it has to be: at anything
    /// gentler a fish driven into the deck at speed keeps sliding for the better part of a
    /// second, which reads as skating rather than as landing badly. This stops a full
    /// sprint inside a third of a second, so arriving on the grid is a full stop and the
    /// only way out of it is the tail.
    /// </summary>
    private const float BeachFriction = 60f;

    /// <summary>Height at which the fish is clear of the deck and swimming properly
    /// again. A hair above zero, so the beach state has hysteresis and doesn't flicker
    /// on a body skimming the grid.</summary>
    private const float SwimHeight = 0.9f;

    // --- The bloom --------------------------------------------------------------
    // The ceiling, and the other half of the class's altitude economy. The seabed is the
    // floor and it beaches you; this is the roof and it poisons you, and between them is
    // the band the whole run is played in.
    //
    // The fiction is the same one everything else on this chassis runs on: if the void is
    // an ocean then it has a surface, and whatever killed this world is still floating on
    // it. The magenta the sky has always glowed is not a nice light — it is the bloom, and
    // a swimmer that climbs into it is doing the vertical version of beaching.
    //
    // The important design property is that it is a *soft* fence with a hard punishment,
    // not a wall. Nothing stops the player going up. The water simply thins as they climb
    // — lift fails, then the tail stops biting — so a fish that pushes into the bloom
    // starts sinking out of it on its own, and the damage is what happens to a player who
    // fights that. An invisible wall would have been a third of the code and would have
    // taught the player nothing.

    /// <summary>
    /// Where the water starts to thin and the warning begins. Set deliberately <em>low</em>
    /// — inside the skyline rather than above it — because a ceiling the player never
    /// reaches is not a ceiling, it is a number in a file. At this height the taller
    /// spires in the city genuinely stand up through the bloom, which turns the reef from
    /// scenery you fly over into a maze you have to fly <em>through</em>: the roof and the
    /// buildings are the same problem, and threading them is the game.
    /// </summary>
    public const float BloomWarnHeight = 34f;

    /// <summary>Where the bloom actually begins to bite. Ten metres of warned, thinning,
    /// visibly-wrong water between this and the notice above — enough to turn round in at
    /// any speed the class can reach.</summary>
    public const float BloomHeight = 44f;

    /// <summary>And where it is at its worst. Past here the damage stops climbing, because
    /// a rate that kept scaling would make one careless climb unsurvivable rather than
    /// expensive.</summary>
    public const float BloomDeepHeight = 70f;

    /// <summary>The body's height above the grid, cached each step so the water and the
    /// bloom can be reasoned about without the rig having to reach into the player.</summary>
    public float Depth { get; private set; }

    /// <summary>
    /// How far into the warned band the body is, 0 (clean water) .. 1 (the bloom proper).
    /// Drives the thinning, the tint and the HUD's notice — everything that happens
    /// <em>before</em> anything starts costing shield.
    /// </summary>
    public float BloomNotice => Math.Clamp(
        (Depth - BloomWarnHeight) / (BloomHeight - BloomWarnHeight), 0f, 1f);

    /// <summary>
    /// How deep into the bloom itself, 0 (just under it) .. 1 (as bad as it gets). Zero
    /// everywhere below <see cref="BloomHeight"/>, which is what makes the warning band a
    /// genuinely free warning rather than a cheaper way of being hurt.
    /// </summary>
    public float Toxicity => Depth <= BloomHeight ? 0f : Math.Clamp(
        (Depth - BloomHeight) / (BloomDeepHeight - BloomHeight), 0f, 1f);

    /// <summary>True while the body is actually in the bloom and taking it.</summary>
    public bool InBloom => Depth > BloomHeight;

    /// <summary>Where the eye sits above the body's own origin. Small: unlike every other
    /// chassis the position <em>is</em> the creature, and a fish's eye is in its
    /// head.</summary>
    public const float EyeHeight = 0.5f;

    // --- Live state -------------------------------------------------------------

    /// <summary>Full 3D momentum: X and Z on the grid plane, Y up. This, and how slowly
    /// it agrees with where the player is looking, is what the class actually is.</summary>
    public Vector3 Velocity;

    /// <summary>This step's roll and brake, as (roll, brake) — A/D in -1..1 and S as 0..1
    /// — already resolved from the keys by the world.</summary>
    public Vector2 MoveInput;

    /// <summary>The camera roll, in radians. Eased rather than snapped, because the roll
    /// is not a camera effect here — it is the steering, and it has to be something the
    /// player feels themselves winding in.</summary>
    public float Bank { get; private set; }

    /// <summary>True while the body is down on the grid — the drowning state.</summary>
    public bool Beached { get; private set; }

    /// <summary>Seconds of stagger left after a crash or a hard beaching. The camera reads
    /// it, and so does the rig, which refuses to beat while it runs.</summary>
    public float Stagger { get; private set; }

    public StrikeState Strike { get; private set; } = StrikeState.Ready;

    /// <summary>How far through the current strike phase, 0..1. Drives the coil's gather
    /// and the lunge's stretch in the viewmodel.</summary>
    public float StrikePhase { get; private set; }

    /// <summary>True on every tick the snout is actually lethal. The world sweeps for a
    /// target while it is set.</summary>
    public bool StrikeActive => Strike == StrikeState.Lunge && !_strikeSpent;

    /// <summary>How starved the reserve is, 0 (full) .. 1 (fumes). The beat weakens and
    /// the audio thins as it rises.</summary>
    public float Starvation { get; private set; }

    /// <summary>
    /// The tail's own clock, 0..1, advancing once per beat and freewheeling between them.
    /// The renderer runs the body's whole S-curve off this, so the thing the player sees
    /// flexing is genuinely the thing that pushed them forward rather than a loop playing
    /// alongside it.
    /// </summary>
    public float BeatPhase { get; private set; }

    /// <summary>How much of the last beat is left in the view, 0..1 — the surge that
    /// shoves the camera forward as the tail bites. Decays on the sim's own clock.</summary>
    public float Surge { get; private set; }

    // --- Single-frame events the world drains each step --------------------------

    public bool JustBeat { get; private set; }
    public bool JustStruck { get; private set; }
    public bool JustBeached { get; private set; }
    public bool JustSurfaced { get; private set; }
    public bool JustCrashed { get; private set; }

    /// <summary>How hard the body met the grid, in m/s. Zero unless
    /// <see cref="JustBeached"/> is set this step.</summary>
    public float BeachImpact { get; private set; }

    // --- What the camera does about all this -------------------------------------

    /// <summary>A small upward nudge on the view from the spit's recoil, in radians.
    /// Applied to the camera and not to the aim, for the same reason the soldier's rifle
    /// is: the round has gone, and moving the crosshair mid-carve would make the weapon
    /// unusable.</summary>
    public float Recoil { get; private set; }

    /// <summary>Broadband shake, 0..1 — a crash, a hard beaching, a blast nearby.</summary>
    public float Shake { get; private set; }

    /// <summary>How much muzzle flash is left, 0..1.</summary>
    public float Flash { get; private set; }

    private float _beatTimer;
    private float _regenBlock;
    private float _strikeTime;
    private bool _strikeSpent;

    /// <summary>Planar speed — what the wind, the lift and the FOV stretch all key off.
    /// A dead-vertical sink should not roar.</summary>
    public float PlanarSpeed => new Vector2(Velocity.X, Velocity.Z).Length();

    public float Speed => Velocity.Length();

    /// <summary>How long the reserve has to be left alone before it refills. Only just
    /// longer than the beat interval, so a player beating at full rate gets nothing back
    /// and one holding any rhythm at all gets most of it.</summary>
    private const float RegenBlockTime = 0.34f;

    /// <summary>Reserve points a second while gliding. Set against the beat's cost so a
    /// sprint is paid back over about five seconds of coasting — long enough that the
    /// burst-and-glide rhythm is a real constraint, short enough that it never becomes
    /// waiting.</summary>
    public const float GlideRegen = 22f;

    /// <summary>True while the reserve is actually refilling — what the HUD's own
    /// readout lights, and the one piece of information a player mid-carve needs about
    /// their breath that the bar alone doesn't give.</summary>
    public bool Recovering => _regenBlock <= 0f;

    /// <summary>
    /// One flick of the tail: an impulse along the look, biased a little upward. Refused
    /// inside the refractory period, while staggered, mid-strike, and on an empty
    /// reserve — the last of which is how a player learns they are out of breath at
    /// exactly the moment it matters.
    ///
    /// Returns whether it actually went, so the world knows whether to sound it.
    /// </summary>
    public bool Beat(PlayerTank p)
    {
        float cost = BeatCost * (Beached ? BeachCostScale : 1f);
        if (_beatTimer > 0f || Stagger > 0f || p.Hyper < cost) return false;
        if (Strike is StrikeState.Coil or StrikeState.Lunge or StrikeState.Recover) return false;

        Vector3 look = p.Forward3;

        if (Beached)
        {
            // Out of the water there is nothing to push against, so a flop is mostly a
            // guaranteed upward kick and barely any drive. Two of them get the body clear.
            Velocity += look * (BeatImpulse * FlopDrive);
            Velocity.Y = MathF.Max(Velocity.Y, 0f) + FlopLift;
        }
        else
        {
            // A starved beat is a weak beat. The reserve going is felt as the body
            // getting heavier long before the gauge is worth a glance.
            float power = 0.55f + 0.45f * Math.Clamp(p.Hyper / 45f, 0f, 1f);
            // And a beat in thin water is a weak beat for a second reason: there is less
            // and less there to push against the higher the body climbs. This is the fence
            // under the bloom, and it is a fence the player feels through the controls
            // rather than one they are told about.
            power *= 1f - BloomThinning * BloomNotice;
            Velocity += look * (BeatImpulse * power);
            Velocity.Y += BeatLift * power;
        }

        p.Hyper = MathF.Max(0f, p.Hyper - cost);
        _beatTimer = BeatInterval;
        _regenBlock = RegenBlockTime;
        BeatPhase = 0f;
        Surge = 1f;
        JustBeat = true;
        return true;
    }

    /// <summary>
    /// Winds and looses a strike. One press does the whole cycle — the coil, the lunge and
    /// the recovery all run themselves — because the commitment is the point: once this
    /// returns true the player has bought the next second and a bit outright, and the only
    /// thing left to be good at is having pointed it somewhere useful.
    /// </summary>
    public bool BeginStrike(PlayerTank p)
    {
        if (Strike != StrikeState.Ready || Beached || Stagger > 0f) return false;
        if (p.Hyper < StrikeCost) return false;

        p.Hyper -= StrikeCost;
        _regenBlock = RegenBlockTime;
        Strike = StrikeState.Coil;
        _strikeTime = 0f;
        _strikeSpent = false;
        StrikePhase = 0f;
        return true;
    }

    /// <summary>
    /// Claims the strike's one hit. A lunge spears a single thing and is spent — it is not
    /// a plough that clears a street — so the world calls this the moment it finds a
    /// target and gets false ever after.
    /// </summary>
    public bool ConsumeStrike()
    {
        if (!StrikeActive) return false;
        _strikeSpent = true;
        return true;
    }

    /// <summary>Kicks the view up by one spit's worth and lights the muzzle.</summary>
    public void Kick(float radians)
    {
        Recoil = MathF.Min(0.07f, Recoil + radians);
        Flash = 1f;
    }

    /// <summary>Throws the whole view about by <paramref name="amount"/> (0..1). Takes the
    /// larger rather than summing, so a cluster of hits reads as one hard event.</summary>
    public void Jolt(float amount) => Shake = MathF.Min(1f, MathF.Max(Shake, amount));

    /// <summary>
    /// Registers a crash into the skyline. Called by the world's collision pass, which is
    /// the only thing that knows a wall was there. Momentum is the price and it is the
    /// only price this class cares about — a fish that hits a tower at speed loses
    /// everything it spent breath building.
    /// </summary>
    public bool RegisterWallHit()
    {
        float speed = PlanarSpeed;
        if (speed < CrashSpeed)
        {
            Velocity.X *= 0.45f;
            Velocity.Z *= 0.45f;
            return false;
        }

        // A crash also kills the strike outright: the lunge ended in masonry, and the
        // snout does not go on being lethal afterward.
        Velocity = new Vector3(0f, MathF.Min(Velocity.Y, 0f), 0f);
        Strike = StrikeState.Recover;
        _strikeTime = 0f;
        _strikeSpent = true;
        Stagger = 0.8f;
        Jolt(MathF.Min(1f, speed / 26f));
        JustCrashed = true;
        return true;
    }

    /// <summary>
    /// One fixed step: the strike's own clock, then the carve, then the water, then
    /// integration. Writes the player's position, height and momentum directly — a fish
    /// has no heading of its own to drive, since the mouse owns where it looks and the
    /// water owns where it goes.
    /// </summary>
    public void Step(float dt, PlayerTank p)
    {
        JustBeat = false;
        JustStruck = false;
        JustBeached = false;
        JustSurfaced = false;
        JustCrashed = false;
        BeachImpact = 0f;

        // Read the body's altitude in before anything else uses it: the water's thickness,
        // the bloom's bite and the whole HUD all key off this one number.
        Depth = p.Height;

        if (_beatTimer > 0f) _beatTimer = MathF.Max(0f, _beatTimer - dt);
        if (_regenBlock > 0f) _regenBlock = MathF.Max(0f, _regenBlock - dt);
        if (Stagger > 0f) Stagger = MathF.Max(0f, Stagger - dt);

        StepStrike(dt, p);
        ApplyCarve(dt, p);
        ApplyWater(dt);
        Integrate(dt, p);
        UpdateBank(dt);
        DecayViewFeedback(dt);

        Starvation = 1f - Math.Clamp(p.Hyper / 32f, 0f, 1f);
    }

    /// <summary>
    /// The strike's three beats. The lunge writes the velocity outright rather than adding
    /// to it, which is deliberate: a strike thrown from a drift and a strike thrown from a
    /// sprint have to arrive the same, or the attack becomes a speed multiplier and the
    /// correct play is to always be going as fast as possible before using it.
    /// </summary>
    private void StepStrike(float dt, PlayerTank p)
    {
        if (Strike == StrikeState.Ready) return;

        _strikeTime += dt;

        switch (Strike)
        {
            case StrikeState.Coil:
                StrikePhase = Math.Clamp(_strikeTime / CoilTime, 0f, 1f);
                // Gathering: the body scrubs almost everything it had. This is what the
                // player pays, and it is paid before they know whether the shot lands.
                Velocity *= MathF.Exp(-7f * dt);
                if (_strikeTime < CoilTime) return;

                Strike = StrikeState.Lunge;
                _strikeTime = 0f;
                Velocity = p.Forward3 * StrikeSpeed;
                JustStruck = true;
                return;

            case StrikeState.Lunge:
                StrikePhase = Math.Clamp(_strikeTime / LungeTime, 0f, 1f);
                if (_strikeTime < LungeTime) return;
                Strike = StrikeState.Recover;
                _strikeTime = 0f;
                return;

            default:
                StrikePhase = Math.Clamp(_strikeTime / RecoverTime, 0f, 1f);
                if (_strikeTime < RecoverTime) return;
                Strike = StrikeState.Ready;
                _strikeTime = 0f;
                StrikePhase = 0f;
                return;
        }
    }

    /// <summary>
    /// Momentum catching up with the crosshair. The one function that decides how this
    /// class handles.
    ///
    /// The velocity's <em>direction</em> is rotated toward the look while its magnitude is
    /// left alone, so turning is genuinely free of speed — a fish loses nothing by coming
    /// round, it simply takes distance to do it. How much distance is what the roll buys:
    /// level, the turn is wide and lazy; rolled hard onto one side, it is three times
    /// tighter. Nothing else in the game steers this way, and it is the reason A and D are
    /// worth having on a chassis whose aim is already on the mouse.
    /// </summary>
    private void ApplyCarve(float dt, PlayerTank p)
    {
        // A lunge is a straight line by definition, and a beached body has nothing to
        // bank against.
        if (Strike == StrikeState.Lunge || Beached) return;

        float speed = Velocity.Length();
        if (speed < 0.05f) return;

        Vector3 dir = Velocity / speed;
        Vector3 look = p.Forward3;

        float dot = Math.Clamp(Vector3.Dot(dir, look), -1f, 1f);
        float angle = MathF.Acos(dot);
        if (angle < 1e-4f) return;

        // Fully rolled either way carves at the same rate — the sign of the bank picks
        // which way the body is leaning, not how hard it is working.
        float commitment = MathF.Abs(Bank) / MaxBank;
        float rate = DriftTurn + CarveTurn * commitment;
        float step = MathF.Min(angle, rate * dt);

        // Slerp toward the look by exactly that much. Built from the component of the
        // look perpendicular to the current heading rather than from an axis-angle, which
        // costs one normalise and behaves correctly at every angle including a dead
        // reversal.
        Vector3 perp = look - dir * dot;
        if (perp.LengthSquared() < 1e-8f) return;
        perp = Vector3.Normalize(perp);

        Vector3 turned = dir * MathF.Cos(step) + perp * MathF.Sin(step);
        Velocity = turned * speed;
    }

    /// <summary>
    /// What the water does: drag against the body, and the slow sink a body that has
    /// stopped generating lift gives in to. A lunge ignores both — it is over in less than
    /// half a second and the whole point of it is that nothing slows it down.
    /// </summary>
    private void ApplyWater(float dt)
    {
        if (Strike == StrikeState.Lunge) return;

        if (Beached)
        {
            // On the deck: heavy friction on the plane, and nothing holding the body up.
            var planar = new Vector2(Velocity.X, Velocity.Z);
            planar = MoveToward(planar, Vector2.Zero, BeachFriction * dt);
            Velocity.X = planar.X;
            Velocity.Z = planar.Y;
            if (Velocity.Y > 0f) return;
            Velocity.Y -= Sink * dt;
            return;
        }

        float speed = Velocity.Length();
        if (speed > 1e-4f)
        {
            // Quadratic, which is what makes the top end feel like a wall of water rather
            // than a number: the first few beats are worth a lot and the last one is
            // barely worth taking.
            float brake = 1f + BrakeDrag * Math.Clamp(MoveInput.Y, 0f, 1f);
            // Recovering from a strike drags hard — the settle the attack is billed for.
            if (Strike == StrikeState.Recover) brake *= 2.2f;

            float decel = Drag * brake * speed * speed * dt;
            Velocity *= MathF.Max(0f, 1f - decel / speed);
        }

        // And the sink, most of which a moving body cancels. This is the class's whole
        // altitude economy: speed is what keeps you up, so the price of stalling is height.
        float lift = MaxLift * Math.Clamp(PlanarSpeed / LiftSpeed, 0f, 1f);
        // Except up in the thin water, where there is progressively less to generate lift
        // against. By the floor of the bloom a body has none at all and sinks however fast
        // it is going — which is what makes the ceiling self-correcting.
        lift *= 1f - BloomNotice;
        Velocity.Y -= Sink * (1f - lift) * dt;
    }

    /// <summary>How much of a beat the thin water takes away at the bloom's floor. Not
    /// all of it: a player who has decided to push up through it should be able to, and
    /// then find out why that was a bad idea.</summary>
    private const float BloomThinning = 0.55f;

    private void Integrate(float dt, PlayerTank p)
    {
        // The lunge is allowed past the swimming cap, and has to be: the whole point of
        // the attack is that it is a gear nothing else on the chassis reaches, and clamping
        // it back to the cruise ceiling would quietly make it a slightly faster swim.
        float cap = Strike == StrikeState.Lunge ? StrikeSpeed : MaxSpeed;
        float speed = Velocity.Length();
        if (speed > cap) Velocity *= cap / speed;

        p.Position = Torus.Wrap(p.Position + new Vector2(Velocity.X, Velocity.Z) * dt);
        p.Height += Velocity.Y * dt;

        if (p.Height > 0f)
        {
            // Clear of the deck by a body length: swimming properly again. The gap between
            // this and zero is hysteresis — without it a body skimming the grid flickers
            // in and out of the beached state and the audio machine-guns.
            if (Beached && p.Height >= SwimHeight)
            {
                Beached = false;
                JustSurfaced = true;
            }
            return;
        }

        p.Height = 0f;
        if (!Beached)
        {
            Beached = true;
            JustBeached = true;
            BeachImpact = -Velocity.Y;

            // A hard arrival throws the view and staggers the body. There is no version of
            // meeting the seabed that this chassis enjoys, but there is a difference
            // between settling onto it and being driven into it.
            if (BeachImpact > BeachImpactSpeed)
            {
                Stagger = MathF.Max(Stagger, 0.55f);
                Jolt(MathF.Min(1f, BeachImpact / 22f));
            }
        }

        if (Velocity.Y < 0f) Velocity.Y = 0f;
    }

    /// <summary>
    /// Eases the roll toward whatever the player is holding. Taken straight off the input
    /// rather than derived from the travel — unlike the soldier, whose bank is a readout
    /// of an arc it has no say in, this roll <em>is</em> the steering input, and a control
    /// that lagged behind what caused it would be unusable.
    /// </summary>
    private void UpdateBank(float dt)
    {
        float target = Beached ? 0f : Math.Clamp(MoveInput.X, -1f, 1f) * MaxBank;
        Bank = MoveToward(Bank, target, BankRate * dt);
    }

    /// <summary>
    /// Rings the view channels down, and advances the tail's freewheel. The beat phase
    /// keeps turning between beats rather than stopping — a fish's tail does not freeze
    /// mid-stroke — but slows as the body does, so a drifting fish sculls and a sprinting
    /// one thrashes.
    /// </summary>
    private void DecayViewFeedback(float dt)
    {
        Recoil = MathF.Max(0f, Recoil - RecoilRecovery * dt);
        Flash = MathF.Max(0f, Flash - dt / FlashTime);
        Surge = MathF.Max(0f, Surge - dt / SurgeTime);
        Shake *= MathF.Exp(-6.5f * dt);
        if (Shake < 0.002f) Shake = 0f;

        float scull = 0.9f + 2.4f * Math.Clamp(PlanarSpeed / MaxSpeed, 0f, 1f);
        BeatPhase = (BeatPhase + scull * dt) % 1f;
    }

    private const float RecoilRecovery = 0.5f;
    private const float FlashTime = 0.05f;
    private const float SurgeTime = 0.32f;

    private static float MoveToward(float value, float target, float maxDelta)
    {
        if (MathF.Abs(target - value) <= maxDelta) return target;
        return value + MathF.Sign(target - value) * maxDelta;
    }

    private static Vector2 MoveToward(Vector2 value, Vector2 target, float maxDelta)
    {
        Vector2 d = target - value;
        float len = d.Length();
        if (len <= maxDelta || len < 1e-5f) return target;
        return value + d / len * maxDelta;
    }
}
