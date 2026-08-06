using System.Numerics;
using Unrendered.Core;

namespace Unrendered.Entities;

/// <summary>
/// What a boss is asking the world to do this tick. The boss itself owns no projectiles, no
/// debris and no spawns — it decides, and the world acts, exactly the way the Crab-Core's beam
/// is decided in <see cref="CrabCore"/> and dealt in <c>World.UpdateBeam</c>.
///
/// That split is the whole reason this entity can be exercised headlessly: a self-test can run
/// a full five-layer fight with no renderer, no audio and no projectile pool, and simply read
/// the acts coming out of it.
/// </summary>
public enum BossActKind
{
    Mortar,   // a lobbed shell toward Target; the world arcs it and detonates it
    Bolt,     // a flat direct round along Dir
    Beam,     // a sustained shaft from Origin along Dir (Power = radius)
    Shock,    // an expanding ground ring centred on Origin (Power = radius)
    Summon,   // a hunter walks out of it at Origin (Power >= 1 means an elite)
    Tether,   // a line thrown at Target; whatever it catches is reeled in
    Snare,    // pins the nearest craft where it stands for Power seconds
    Scream,   // no damage: staggers the view and winds the field up
    Shroud,   // vents cover at Origin
    Impact,   // it landed on the grid — a leap, a slam, a charge ending in something
}

/// <summary>One decision, drained by the world each tick and then forgotten.</summary>
public readonly record struct BossAct(
    BossActKind Kind,
    Vector3 Origin,
    Vector3 Dir,
    Vector2 Target,
    float Power);

/// <summary>
/// The DESCENT boss. One class covers all five lineages and every rolled variant, because the
/// thing that differs between two bosses is entirely <see cref="BossGenome"/> data — the body
/// it is drawn with, the three moves it cycles, the one quirk that colours the fight. There is
/// no BulwarkBoss and no DrownerBoss and there should never be one: the moment a lineage needs
/// its own class, the modularity that makes these interesting has quietly stopped existing.
///
/// <para><b>Layers are the design.</b> A boss's health is not one pool with a bar drawn over
/// it — it is <see cref="BossGenome.Layers"/> separate shells, broken from the outside in, and
/// each one that breaks physically comes off the model (see <see cref="ShedLayers"/>). The
/// five-bar Colossus at the bottom of a descent is therefore five visibly different monsters
/// in a row: fully plated at the top of the fight, and a bare core on legs by the end of it.
/// Nothing about that is a HUD effect.</para>
///
/// <para>Every attack runs wind → strike → recover, and <em>recover is the point</em>. A boss
/// that has just committed to something is wide open for a beat (<see cref="Exposed"/>, worth
/// double), so the fight is a conversation about openings rather than a stand-up damage race.
/// The wind-up is always physical — light gathering, a body rearing, plates opening — because
/// a telegraph that only exists on the HUD is a telegraph a player learns to ignore.</para>
/// </summary>
public sealed class ModularBoss
{
    public readonly BossGenome Gene;

    public Vector2 Position;
    public float Heading;

    /// <summary>How far off the grid the body is riding. A hovering lineage sits up here for
    /// its whole life; everything else visits — a leap, a burrow, the moment a slam is at the
    /// top of its rear.</summary>
    public float Height { get; private set; }

    public enum State
    {
        Arriving,   // fading up out of the fog; cannot be hurt, does nothing
        Stalking,   // holding its band, closing or backing off, waiting on its clock
        Winding,    // the telegraph — the thing you are meant to see coming
        Striking,   // committed
        Recover,    // open. This is where a fight is actually won
        Breaking,   // a layer just went; locked, screaming, shedding
        Dying,
        Dead,
    }

    public State Phase { get; private set; } = State.Arriving;
    public bool Alive => Phase is not (State.Dying or State.Dead);
    public bool Dead => Phase == State.Dead;

    // --- The layers ----------------------------------------------------------------

    private readonly float[] _layers;

    /// <summary>How many shells are still on it, outermost first. The HUD draws one bar per
    /// entry and the renderer sheds one plate ring per entry lost.</summary>
    public int LayersLeft { get; private set; }

    /// <summary>How full the layer currently taking damage is, 0..1 — the bar that is actually
    /// moving. The ones behind it are full and the ones in front of it are gone.</summary>
    public float TopFraction => LayersLeft <= 0 ? 0f
        : Math.Clamp(_layers[LayersLeft - 1] / Gene.LayerHealth, 0f, 1f);

    /// <summary>How many shells have come off, which is what the renderer strips from the
    /// model. Rises as the fight goes; never falls.</summary>
    public int ShedLayers => Gene.Layers - LayersLeft;

    /// <summary>
    /// How full one particular shell is, 0..1. Index 0 is the <em>innermost</em> — the last one
    /// standing — and the highest index is the outer plate that breaks first, which is the order
    /// the HUD stacks them in: the bars empty from the top down as the thing is peeled.
    /// </summary>
    public float LayerFraction(int index)
    {
        if ((uint)index >= (uint)_layers.Length) return 0f;
        return Math.Clamp(_layers[index] / MathF.Max(1f, Gene.LayerHealth), 0f, 1f);
    }

    /// <summary>0 at full health across every layer, 1 at the last sliver of the last one. What
    /// everything cosmetic keys off — the gait speeding up, the core burning hotter, the smoke
    /// coming out of it.</summary>
    public float Ruin
    {
        get
        {
            float left = 0f;
            for (int i = 0; i < LayersLeft; i++) left += _layers[i];
            return 1f - Math.Clamp(left / MathF.Max(1f, Gene.TotalHealth), 0f, 1f);
        }
    }

    // --- Timing --------------------------------------------------------------------

    private float _phaseT;          // seconds inside the current phase
    private float _phaseLen;        // how long it lasts
    private int _attackIndex = -1;  // where in the genome's three-move cycle we are
    private float _rest;            // countdown to the next attack while stalking

    /// <summary>0..1 through whatever the current phase is. The renderer reads it for the
    /// wind-up pose and the HUD reads it for the telegraph flash.</summary>
    public float PhaseProgress => _phaseLen <= 0f ? 0f : Math.Clamp(_phaseT / _phaseLen, 0f, 1f);

    /// <summary>The move being wound, struck or recovered from. Meaningless while stalking,
    /// which is why <see cref="Committed"/> exists to ask first.</summary>
    public AttackModule Current => _attackIndex < 0
        ? Gene.Attacks[0] : Gene.Attacks[_attackIndex % Gene.Attacks.Length];

    public bool Committed => Phase is State.Winding or State.Striking or State.Recover;

    /// <summary>The window a player is fishing for: the beat after it commits, when it is worth
    /// <see cref="ExposedMultiplier"/> times as much. Deliberately generous in length and
    /// obvious on the model — a hidden crit window is a secret, not a mechanic.</summary>
    public bool Exposed => Phase == State.Recover;

    /// <summary>What a hit in that window is worth. Two is enough that a player who reads the
    /// fight finishes it in half the time of one who does not, which is exactly the gap that
    /// should exist between the two.</summary>
    public const float ExposedMultiplier = 2f;

    /// <summary>True while nothing can touch it: the arrival, the break between layers, and a
    /// PHASED boss's periodic absence. The HUD says so rather than letting shots quietly do
    /// nothing, which reads as the game being broken.</summary>
    public bool Untouchable => Phase is State.Arriving or State.Breaking || _phaseOut > 0f;

    private float _phaseOut;     // PHASED: seconds of not being here
    private float _phaseClock;   // PHASED: the rhythm it goes on

    /// <summary>SEALED only: the frontal plates are up and damage from in front is refused
    /// until they are shot off. Drops for good once broken, and comes back on the next layer.</summary>
    public bool PlatesUp { get; private set; }
    private float _plateHealth;

    // --- Output --------------------------------------------------------------------

    private readonly List<BossAct> _acts = new();
    public IReadOnlyList<BossAct> Acts => _acts;
    public void ClearActs() => _acts.Clear();

    private readonly List<EntityCue> _cues = new();
    public IReadOnlyList<EntityCue> Cues => _cues;
    public void ClearCues() => _cues.Clear();

    /// <summary>Raised on the tick a layer breaks, so the world can stage the moment — the
    /// screen wash, the shockwave, the towers coming down — without polling for it.</summary>
    public int LayersBrokenThisTick { get; private set; }

    /// <summary>Set on the tick it finally dies, so the fragment drops exactly once.</summary>
    public bool JustDied { get; private set; }

    // --- Geometry ------------------------------------------------------------------

    /// <summary>Planar hit radius. Grown off the rolled body rather than a constant, so a wide
    /// BULWARK really is harder to miss than a thin DROWNER — what you see is what you hit,
    /// which is the rule the hunters have always followed (see <c>EnemyTank.Scale</c>).</summary>
    public float Radius => Gene.Scale * (Gene.BodyWidth + Gene.BodyDepth) * 0.30f;

    /// <summary>How tall it stands from its own base. The vertical half of the hit test, and
    /// what the camera uses to keep the whole thing in frame during an arrival.</summary>
    public float BodyHeight => Gene.Scale * (Gene.BodyRise * 1.6f + LimbRise);

    /// <summary>How far the carriage lifts the body off the grid before the body itself starts.
    /// The genome's own figure — the renderer builds a carriage exactly this tall, so the two
    /// cannot drift apart and leave a body floating over nothing.</summary>
    public float CarriageLift => Gene.CarriageLift;

    private float LimbRise => Gene.Carriage is Carriage.Legs or Carriage.Biped
        ? 2.0f * Gene.LimbLength : 0.6f;

    /// <summary>Where the core sits in the world — the emitter for a beam, the thing a camera
    /// looks at, and the bright spot the whole silhouette is arranged around.</summary>
    public Vector3 CorePoint => new(
        Position.X,
        Height + (CarriageLift + Gene.CoreLocalY) * Gene.Scale,
        Position.Y);

    /// <summary>Which way it is facing, on the plane. Matches the tanks' convention: heading 0
    /// faces +Z.</summary>
    public Vector2 Forward => new(MathF.Sin(Heading), MathF.Cos(Heading));

    // --- Movement ------------------------------------------------------------------

    /// <summary>How far out it wants to sit. Rolled per boss inside a band wide enough that one
    /// genuinely crowds you and another genuinely snipes, which changes how a fight is played
    /// more than any single attack does.</summary>
    public float PreferredRange => 16f + Gene.Standoff * 40f;

    private float MoveSpeed => (6f + 9f * Gene.GaitSpeed) * RabidFactor;
    private float TurnSpeed => (0.7f + 0.9f * Gene.GaitSpeed) * RabidFactor;

    /// <summary>RABID's whole effect: everything it does gets faster as it loses layers, so the
    /// last bar of a five-bar fight is a different animal from the first. Ends at half again,
    /// which is a lot when it applies to the wind-ups too.</summary>
    private float RabidFactor => Gene.Quirk == Quirk.Rabid
        ? 1f + 0.5f * (ShedLayers / MathF.Max(1f, Gene.Layers - 1f)) : 1f;

    // --- Animation channels --------------------------------------------------------
    // Read by the renderer only. Kept here rather than solved in the renderer so a headless
    // run advances the same numbers a drawn one does, and a screenshot of frame N is the same
    // pose the sim was in at frame N.

    /// <summary>The gait. Limbs bob and sweep off this; a tread rolls off it.</summary>
    public float GaitPhase { get; private set; }

    /// <summary>How wound the body is — 0 at rest, 1 fully reared or fully charged. The single
    /// channel every telegraph pose is built on.</summary>
    public float Wind { get; private set; }

    /// <summary>The core's own spin, which runs faster the closer it is to acting. This is the
    /// tell a player learns first, because it is visible from anywhere in the arena.</summary>
    public float CoreSpin { get; private set; }

    /// <summary>How hot the core is burning, 0..1. Rides the wind-up and spikes when a layer
    /// breaks.</summary>
    public float CoreHeat { get; private set; }

    /// <summary>0→1 across the death glitch, driving the same tear-apart the Crab-Core has.</summary>
    public float DeathProgress => Phase == State.Dying
        ? Math.Clamp(_phaseT / DeathDuration, 0f, 1f) : (Phase == State.Dead ? 1f : 0f);

    /// <summary>0→1 across the arrival, so the renderer can bring it up out of the floor
    /// rather than having it simply be there one frame.</summary>
    public float ArrivalProgress => Phase == State.Arriving
        ? Math.Clamp(_phaseT / ArrivalDuration, 0f, 1f) : 1f;

    // --- Constants -----------------------------------------------------------------

    /// <summary>How long it takes to actually arrive. Long, and deliberately: this is the only
    /// look a player gets at the whole silhouette before it starts trying to kill them, and a
    /// procedurally-built monster that nobody ever sees whole is a waste of the generator.</summary>
    public const float ArrivalDuration = 3.2f;

    private const float DeathDuration = 1.9f;

    /// <summary>How long everything stops while a layer comes off. A full beat — the boss is
    /// locked, the plates are flying, and the player gets an unambiguous "that worked".</summary>
    public const float BreakDuration = 1.9f;

    /// <summary>How close it has to be before it will commit to anything, as a multiple of its
    /// preferred range. Stops a boss winding a lance at a player three streets away.</summary>
    private const float EngageFactor = 2.2f;

    public ModularBoss(BossGenome gene, Vector2 at, float heading = 0f)
    {
        Gene = gene;
        Position = Torus.Wrap(at);
        Heading = heading;

        _layers = new float[gene.Layers];
        for (int i = 0; i < _layers.Length; i++) _layers[i] = gene.LayerHealth;
        LayersLeft = gene.Layers;

        Enter(State.Arriving, ArrivalDuration);
        ArmPlates();

        // A hovering body starts at its cruising height rather than climbing to it, so an
        // arrival reads as something already up there resolving out of the murk.
        if (gene.Carriage == Carriage.Hover) Height = HoverHeight;

        // Desync the phase rhythm so two PHASED bosses on one field never blink together.
        _phaseClock = Random.Shared.NextSingle() * PhaseCycle;
    }

    /// <summary>
    /// How high a hovering body cruises. Held down to roughly one body's worth off the grid
    /// rather than the two it was first built at: a DROWNER Colossus at twelve units is a thing
    /// the grounded chassis — the TANK above all, which cannot leave the floor at all — can only
    /// shoot at the underside of, and a boss that one of the five classes simply cannot reach is
    /// not a difficulty, it is a wall.
    /// </summary>
    private float HoverHeight => (2.6f + 1.6f * Gene.LimbLength) * Gene.Scale * 0.55f;

    // --- The tick ------------------------------------------------------------------

    /// <summary>
    /// Steps the boss one frame against whichever craft is nearest. Everything it decides comes
    /// out through <see cref="Acts"/> and <see cref="Cues"/>; nothing is applied here.
    /// </summary>
    public void Update(float dt, Vector2 targetPos, float targetHeight)
    {
        LayersBrokenThisTick = 0;
        JustDied = false;

        // A posed rig is held where PoseAs put it: the clock does not advance, so the phase
        // never times out and the capture gets the exact frame it asked for. Everything
        // cosmetic still runs, so the core keeps turning and the gait keeps breathing.
        if (Posed && Phase is not (State.Arriving or State.Stalking))
        {
            Animate(dt);
            FaceToward(Torus.NearestImage(targetPos, Position), TurnSpeed * dt);
            return;
        }

        _phaseT += dt;
        Animate(dt);

        if (Phase == State.Dead) return;
        if (Phase == State.Dying)
        {
            if (_phaseT >= DeathDuration) Phase = State.Dead;
            return;
        }

        UpdatePhasedQuirk(dt);

        // Work against the target's nearest image across the seam, the same way a hunter does
        // — a boss by the world's edge must hunt the player just over it, not drive the long
        // way round the torus to reach them.
        Vector2 near = Torus.NearestImage(targetPos, Position);

        switch (Phase)
        {
            case State.Arriving:
                UpdateArriving(dt, near);
                break;
            case State.Stalking:
                UpdateStalking(dt, near);
                break;
            case State.Winding:
                UpdateWinding(dt, near, targetHeight);
                break;
            case State.Striking:
                UpdateStriking(dt, near, targetHeight);
                break;
            case State.Recover:
                UpdateRecover(dt, near);
                break;
            case State.Breaking:
                UpdateBreaking(dt);
                break;
        }

        Position = Torus.Wrap(Position);
    }

    private void UpdateArriving(float dt, Vector2 target)
    {
        // Turns to face the player across the arrival, so by the time it is whole it is
        // already looking at them. Nothing else moves.
        FaceToward(target, dt * 0.8f);
        if (Gene.Carriage != Carriage.Hover)
            Height = -BodyHeight * (1f - ArrivalProgress) * 0.55f;

        if (_phaseT >= ArrivalDuration)
        {
            Height = Gene.Carriage == Carriage.Hover ? HoverHeight : 0f;
            _cues.Add(new EntityCue(Cue.Alarm, Position));
            Enter(State.Stalking, 0f);
            _rest = RestLength * 0.5f;   // half a beat's grace, then it starts
        }
    }

    /// <summary>
    /// Capture/debug hold: the boss stands where it is put, keeps breathing, and never commits
    /// to anything. Nothing in play ever sets it.
    ///
    /// <para>It exists because the screenshot harness could not otherwise photograph one of
    /// these: a DROWNER's signature move is a leap onto the player, so every attempt to frame one
    /// ended with the animal parked on the lens. A posed boss can be looked at, which is the only
    /// way to check a body nobody designed by hand.</para>
    /// </summary>
    public bool Posed;

    /// <summary>
    /// Capture/debug: freezes the rig in the middle of a named phase, so a wind-up, a
    /// follow-through or a recovery slump can be photographed rather than caught by luck on the
    /// one frame in sixty it happens to be on.
    /// </summary>
    public void PoseAs(State phase, AttackModule move, float progress = 0.85f)
    {
        Posed = true;
        _attackIndex = Math.Max(0, Array.IndexOf(Gene.Attacks, move));
        _phaseLen = 1f;
        _phaseT = Math.Clamp(progress, 0f, 1f);
        Phase = phase;
        Wind = phase switch
        {
            State.Winding => _phaseT,
            State.Striking => 1f - _phaseT * 0.4f,
            State.Recover => MathF.Max(0f, 1f - _phaseT * 1.4f),
            State.Breaking => MathF.Sin(_phaseT * MathF.PI),
            _ => 0f,
        };
        CoreHeat = phase is State.Winding or State.Breaking ? _phaseT : 0.2f;
    }

    private void UpdateStalking(float dt, Vector2 target)
    {
        FaceToward(target, TurnSpeed * dt);
        if (Posed) return;   // stands where it was put, still breathing (see Animate)

        HoldBand(dt, target);
        BobHover(dt);

        _rest -= dt;
        float dist = Vector2.Distance(Position, target);
        if (_rest <= 0f && dist < PreferredRange * EngageFactor)
            BeginAttack(target);
    }

    /// <summary>
    /// The gap between attacks. An aggressive boss is not one that hits harder — it is one that
    /// gives you less room between the things it does, which is the difference a player
    /// actually feels.
    /// </summary>
    private float RestLength => (2.6f - 1.5f * Gene.Aggression) / RabidFactor;

    private void BeginAttack(Vector2 target)
    {
        _attackIndex++;
        // Two-in-three chance of taking the next move in the cycle, one-in-three of picking
        // freely. A pure cycle is learnable in one fight; pure randomness has no rhythm to
        // read at all. This sits between: there is a pattern, and it lies to you sometimes.
        if (Random.Shared.NextSingle() < 0.33f)
            _attackIndex = Random.Shared.Next(Gene.Attacks.Length);

        Enter(State.Winding, WindLength(Current));
        _cues.Add(new EntityCue(CueForWind(Current), Position, Gene.Aggression));

        // Face the shot up front for anything aimed, so the wind-up is a thing pointing at you
        // rather than a thing thinking about it.
        if (Current is AttackModule.Lance or AttackModule.Charge or AttackModule.Tether)
            FaceToward(target, MathF.PI);
    }

    /// <summary>
    /// How long each telegraph runs. These are the most important numbers in the file: the
    /// wind-up is the entire fairness of an attack, and the ones that hurt most are the ones
    /// that take longest to arrive. Scaled down by aggression and by RABID, never below a floor
    /// that a player can still react inside.
    /// </summary>
    private float WindLength(AttackModule m)
    {
        float raw = m switch
        {
            AttackModule.Lance => 2.4f,     // the biggest single hit, so the longest look at it
            AttackModule.Charge => 1.6f,    // it squares up and holds — you can see the line
            AttackModule.Slam => 1.3f,
            AttackModule.Leap => 1.2f,
            AttackModule.Mortar => 1.1f,
            AttackModule.Sweep => 1.8f,
            AttackModule.Summon => 1.4f,
            AttackModule.Tether => 0.9f,
            AttackModule.Burrow => 0.8f,
            AttackModule.Shroud => 0.6f,
            AttackModule.Bulwarks => 0.8f,
            AttackModule.Scream => 1.0f,
            AttackModule.Snare => 0.7f,
            _ => 0.5f,                      // Spit — cheap, fast, constant pressure
        };
        return MathF.Max(0.35f, raw * (1.25f - 0.5f * Gene.Aggression) / RabidFactor);
    }

    private float StrikeLength(AttackModule m) => m switch
    {
        AttackModule.Lance => 1.6f,
        AttackModule.Sweep => 2.4f,
        AttackModule.Charge => 1.4f,
        AttackModule.Leap => 1.0f,
        AttackModule.Burrow => 1.8f,
        AttackModule.Shroud => 0.4f,
        AttackModule.Spit => 0.9f,
        AttackModule.Mortar => 0.8f,
        _ => 0.5f,
    };

    /// <summary>
    /// How long it stands there open afterward. Scaled by how big a swing it just took: a lance
    /// leaves it helpless, a spit barely costs it anything. This is the reward schedule of the
    /// whole fight — bait the big move, punish the recovery.
    /// </summary>
    private float RecoverLength(AttackModule m) => m switch
    {
        AttackModule.Lance => 2.2f,
        AttackModule.Charge => 2.0f,
        AttackModule.Slam => 1.8f,
        AttackModule.Leap => 1.6f,
        AttackModule.Sweep => 1.9f,
        AttackModule.Scream => 1.4f,
        AttackModule.Summon => 1.2f,
        AttackModule.Mortar => 0.9f,
        _ => 0.6f,
    };

    private void UpdateWinding(float dt, Vector2 target, float targetHeight)
    {
        Wind = PhaseProgress;
        CoreHeat = MathF.Min(1f, CoreHeat + dt * 1.6f);
        BobHover(dt);

        // Most wind-ups keep tracking, so a telegraph is not a free dodge — you have to move
        // out of it, not merely wait it out. BURROW and SHROUD are the exceptions: they are
        // not aimed at anything.
        if (Current is not (AttackModule.Burrow or AttackModule.Shroud))
            FaceToward(target, TurnSpeed * dt * 0.7f);

        // CHARGE and SLAM haul themselves back before they come through, which is the pose
        // that sells the wind-up from any angle.
        if (Current == AttackModule.Charge)
            Position -= Forward * (2.5f * dt * Wind);

        if (_phaseT >= _phaseLen)
        {
            Strike(target, targetHeight);
            Enter(State.Striking, StrikeLength(Current));
        }
    }

    /// <summary>
    /// The moment of commitment: turns the current module into acts the world will carry out.
    /// Everything before this was a pose and everything after it is a follow-through.
    /// </summary>
    private void Strike(Vector2 target, float targetHeight)
    {
        Vector3 core = CorePoint;
        Vector2 flat = target - Position;
        float dist = flat.Length();
        Vector2 dir = dist > 0.001f ? flat / dist : Forward;
        var aim = new Vector3(dir.X, 0f, dir.Y);

        switch (Current)
        {
            case AttackModule.Lance:
            {
                // Elevated onto the craft's body rather than fired dead flat, so a boss can
                // actually reach a fish or a soldier up in the towers.
                float rise = targetHeight + 1.4f - core.Y;
                var d = Vector3.Normalize(new Vector3(dir.X, rise / MathF.Max(6f, dist), dir.Y));
                _acts.Add(new BossAct(BossActKind.Beam, core, d, target, 2.2f + 1.4f * Gene.Scale * 0.4f));
                _cues.Add(new EntityCue(Cue.BeamFire, Position));
                break;
            }

            case AttackModule.Sweep:
                // A low shaft that will be turned through an arc across the strike — the world
                // re-reads the direction every tick while Striking, below.
                _acts.Add(new BossAct(BossActKind.Beam, core, aim, target, 1.6f));
                _cues.Add(new EntityCue(Cue.BeamFire, Position, 0.5f));
                break;

            case AttackModule.Mortar:
            {
                // Three to five shells fanned around where they are now. Each is a separate
                // act so the world can stagger them and paint a landing mark under each.
                int n = 3 + Random.Shared.Next(3);
                for (int i = 0; i < n; i++)
                {
                    float spread = (i - (n - 1) * 0.5f) * 4.5f;
                    Vector2 side = new(-dir.Y, dir.X);
                    Vector2 mark = target + side * spread
                        + dir * ((Random.Shared.NextSingle() - 0.5f) * 6f);
                    _acts.Add(new BossAct(BossActKind.Mortar, core, aim, Torus.Wrap(mark), i * 0.22f));
                }
                _cues.Add(new EntityCue(Cue.Detonation, Position));
                break;
            }

            case AttackModule.Spit:
            {
                int n = 4 + (int)(4 * Gene.Aggression);
                for (int i = 0; i < n; i++)
                {
                    float yaw = (i - (n - 1) * 0.5f) * 0.09f;
                    float c = MathF.Cos(yaw), s = MathF.Sin(yaw);
                    var d = new Vector3(dir.X * c - dir.Y * s, 0.02f, dir.X * s + dir.Y * c);
                    _acts.Add(new BossAct(BossActKind.Bolt, core, Vector3.Normalize(d), target, i * 0.07f));
                }
                _cues.Add(new EntityCue(Cue.MawSpit, Position));
                break;
            }

            case AttackModule.Charge:
                _cues.Add(new EntityCue(Cue.HuntCall, Position));
                break;   // the movement itself is the attack; see UpdateStriking

            case AttackModule.Slam:
                _acts.Add(new BossAct(BossActKind.Shock, new Vector3(Position.X, 0f, Position.Y),
                    Vector3.UnitY, Position, Radius * 3.2f));
                _cues.Add(new EntityCue(Cue.ClawSlam, Position));
                break;

            case AttackModule.Leap:
                _cues.Add(new EntityCue(Cue.MawDive, Position));
                _leapFrom = Position;
                _leapTo = target;
                break;

            case AttackModule.Summon:
            {
                // It tears open and hunters walk out. Count rises with how hurt it is, which is
                // what stops a LEECHing boss from being a stalemate — the more you break it, the
                // more it feeds itself, and the more it feeds you.
                int n = 2 + (int)(2f * Ruin) + (Gene.Quirk == Quirk.Leech ? 1 : 0);
                for (int i = 0; i < n; i++)
                {
                    float a = MathF.Tau * i / n + Heading;
                    var at = Position + new Vector2(MathF.Sin(a), MathF.Cos(a)) * (Radius + 6f);
                    _acts.Add(new BossAct(BossActKind.Summon,
                        new Vector3(at.X, 0f, at.Y), Vector3.Zero, Torus.Wrap(at),
                        Random.Shared.NextSingle() < 0.3f + 0.3f * Ruin ? 1f : 0f));
                }
                _cues.Add(new EntityCue(Cue.CrabScream, Position, 0.4f));
                break;
            }

            case AttackModule.Tether:
                _acts.Add(new BossAct(BossActKind.Tether, core, aim, target, 1f));
                _cues.Add(new EntityCue(Cue.CableFire, Position));
                break;

            case AttackModule.Snare:
                _acts.Add(new BossAct(BossActKind.Snare, core, aim, target, 1.3f));
                _cues.Add(new EntityCue(Cue.AnchorBite, Position));
                break;

            case AttackModule.Scream:
                _acts.Add(new BossAct(BossActKind.Scream, core, Vector3.UnitY, Position, 1f));
                _cues.Add(new EntityCue(Cue.CrabScream, Position, 1f));
                break;

            case AttackModule.Shroud:
                _acts.Add(new BossAct(BossActKind.Shroud, new Vector3(Position.X, 0f, Position.Y),
                    Vector3.UnitY, Position, Radius * 2f));
                _phaseOut = MathF.Max(_phaseOut, 2.2f);
                break;

            case AttackModule.Bulwarks:
                ArmPlates();
                _cues.Add(new EntityCue(Cue.Clamp, Position));
                break;

            case AttackModule.Burrow:
                _burrowTo = Torus.Wrap(target + RandomOffset(PreferredRange * 0.6f));
                _cues.Add(new EntityCue(Cue.StructureGroan, Position));
                break;
        }
    }

    private Vector2 _leapFrom, _leapTo, _burrowTo;

    private void UpdateStriking(float dt, Vector2 target, float targetHeight)
    {
        Wind = 1f - PhaseProgress * 0.4f;
        BobHover(dt);

        switch (Current)
        {
            case AttackModule.Charge:
            {
                // Straight ahead, fast, no steering. The commitment is the counterplay: it goes
                // exactly where it was pointing when it let go, and stepping out of that line is
                // the whole answer.
                Position += Forward * (MoveSpeed * 3.4f * dt);
                break;
            }

            case AttackModule.Sweep:
            {
                // The shaft turns through a wide arc across the strike. Re-emitted every tick
                // with the current bearing, so the world always has a live direction to burn
                // along rather than a stale one from the moment it fired.
                float t = PhaseProgress;
                float arc = (t - 0.5f) * 2.4f;
                float yaw = Heading + arc;
                var d = new Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw));
                _acts.Add(new BossAct(BossActKind.Beam, CorePoint, d, target, 1.6f));
                break;
            }

            case AttackModule.Leap:
            {
                // A real arc with a real hang at the top of it — this is the one move where the
                // boss is genuinely off the grid and genuinely above you, and the shadow it
                // throws is the only warning worth having.
                float t = PhaseProgress;
                Position = Vector2.Lerp(_leapFrom, Torus.NearestImage(_leapTo, _leapFrom), t);
                Height = MathF.Sin(t * MathF.PI) * (10f + 6f * Gene.Scale)
                    + (Gene.Carriage == Carriage.Hover ? HoverHeight : 0f);
                if (t >= 1f) Land();
                break;
            }

            case AttackModule.Burrow:
            {
                // Down, gone, and up again somewhere else. Untouchable for the middle of it, so
                // it cannot be chewed to death while it is in the floor.
                float t = PhaseProgress;
                if (t < 0.4f) Height = -BodyHeight * (t / 0.4f);
                else if (t < 0.6f) { Height = -BodyHeight; Position = _burrowTo; _phaseOut = 0.35f; }
                else Height = -BodyHeight * (1f - (t - 0.6f) / 0.4f);
                break;
            }
        }

        if (_phaseT >= _phaseLen)
        {
            if (Current is AttackModule.Charge or AttackModule.Slam) Land();
            Height = Gene.Carriage == Carriage.Hover ? HoverHeight : 0f;
            Enter(State.Recover, RecoverLength(Current));
        }
    }

    private void Land()
    {
        _acts.Add(new BossAct(BossActKind.Impact, new Vector3(Position.X, 0f, Position.Y),
            Vector3.UnitY, Position, Radius * 2.4f));
        _cues.Add(new EntityCue(Cue.CrashLanding, Position, 0.8f));
    }

    private void UpdateRecover(float dt, Vector2 target)
    {
        // Everything sags. The pose is the tell: a boss in recovery is visibly slumped, its
        // core dimmed, and it is not tracking you.
        Wind = MathF.Max(0f, 1f - PhaseProgress * 1.4f);
        CoreHeat = MathF.Max(0f, CoreHeat - dt * 0.9f);
        BobHover(dt);

        if (_phaseT >= _phaseLen)
        {
            Enter(State.Stalking, 0f);
            _rest = RestLength;
        }
    }

    private void UpdateBreaking(float dt)
    {
        CoreHeat = 1f;
        Wind = MathF.Sin(PhaseProgress * MathF.PI);
        if (_phaseT >= _phaseLen)
        {
            ArmPlates();
            Enter(State.Stalking, 0f);
            _rest = RestLength * 0.4f;   // it comes back angry, not politely
        }
    }

    // --- Movement helpers ----------------------------------------------------------

    private void HoldBand(float dt, Vector2 target)
    {
        float dist = Vector2.Distance(Position, target);
        float advance = dist > PreferredRange + 5f ? 1f
            : dist < PreferredRange - 9f ? -0.55f : 0f;
        if (advance != 0f) Position += Forward * (MoveSpeed * advance * dt);
        GaitPhase += dt * (2.2f + 4f * Gene.GaitSpeed) * (advance != 0f ? 1f : 0.25f);
    }

    private void BobHover(float dt)
    {
        if (Gene.Carriage != Carriage.Hover) return;
        if (Phase == State.Striking && Current is AttackModule.Leap or AttackModule.Burrow) return;
        Height = HoverHeight + MathF.Sin(GaitPhase * 0.7f) * 0.8f * Gene.Scale;
    }

    private void FaceToward(Vector2 target, float maxStep)
    {
        Vector2 to = target - Position;
        if (to.LengthSquared() < 1e-6f) return;
        float want = MathF.Atan2(to.X, to.Y);
        float delta = MathF.IEEERemainder(want - Heading, MathF.Tau);
        Heading += MathF.Abs(delta) <= maxStep ? delta : MathF.Sign(delta) * maxStep;
    }

    private static Vector2 RandomOffset(float reach)
    {
        float a = Random.Shared.NextSingle() * MathF.Tau;
        float d = reach * (0.4f + 0.6f * Random.Shared.NextSingle());
        return new Vector2(MathF.Sin(a), MathF.Cos(a)) * d;
    }

    // --- The PHASED quirk ----------------------------------------------------------

    /// <summary>How long one blink-out cycle takes. Long enough to be a rhythm a player can
    /// count rather than a random unfairness.</summary>
    private const float PhaseCycle = 9f;
    private const float PhaseOutTime = 1.6f;

    private void UpdatePhasedQuirk(float dt)
    {
        if (_phaseOut > 0f) _phaseOut -= dt;
        if (Gene.Quirk != Quirk.Phased) return;

        _phaseClock += dt;
        if (_phaseClock >= PhaseCycle)
        {
            _phaseClock -= PhaseCycle;
            _phaseOut = PhaseOutTime;
            _cues.Add(new EntityCue(Cue.MawCrystal, Position, 0.8f));
        }
    }

    // --- Damage --------------------------------------------------------------------

    /// <summary>How much armour the SEALED plates soak before they come off. A fifth of a
    /// layer, so breaking them is a real but short job rather than a second health bar.</summary>
    private float PlateHealth => Gene.LayerHealth * 0.2f;

    private void ArmPlates()
    {
        PlatesUp = Gene.Quirk == Quirk.Armoured || Gene.Has(AttackModule.Bulwarks);
        _plateHealth = PlateHealth;
    }

    /// <summary>
    /// Puts damage into it, outermost layer first. Returns what actually landed, which is not
    /// what was asked for: the exposure window doubles it, the plates may refuse it outright,
    /// and a boss mid-break or mid-phase takes nothing at all.
    ///
    /// <paramref name="fromFront"/> decides whether the SEALED plates are in the way. Shots to
    /// the back of a plated boss always tell — which is the whole answer to that quirk, and the
    /// reason it is worth carrying rather than simply being more health.
    /// </summary>
    public float Damage(float amount, bool fromFront = true)
    {
        if (!Alive || Untouchable || amount <= 0f) return 0f;

        if (PlatesUp && fromFront)
        {
            _plateHealth -= amount;
            if (_plateHealth <= 0f)
            {
                PlatesUp = false;
                _cues.Add(new EntityCue(Cue.Clamp, Position, 1f));
            }
            return 0f;   // the plates ate it. The HUD says so; nothing is silently lost.
        }

        if (Exposed) amount *= ExposedMultiplier;

        int top = LayersLeft - 1;
        _layers[top] -= amount;
        CoreHeat = MathF.Min(1f, CoreHeat + 0.15f);
        _cues.Add(new EntityCue(Cue.CoreHit, Position, Math.Clamp(amount / Gene.LayerHealth, 0f, 1f)));

        if (_layers[top] <= 0f)
        {
            // Overkill does NOT roll into the layer behind. Each shell is its own fight and a
            // single enormous hit must not cascade through three of them — the five bars of a
            // Colossus are meant to be five acts, not one damage number.
            _layers[top] = 0f;
            LayersLeft--;
            LayersBrokenThisTick++;

            if (LayersLeft <= 0)
            {
                Enter(State.Dying, DeathDuration);
                JustDied = true;
                _cues.Add(new EntityCue(Cue.BossDeath, Position));
            }
            else
            {
                Enter(State.Breaking, BreakDuration / RabidFactor);
                _cues.Add(new EntityCue(Cue.CrabScream, Position, 1f));
                // The break itself throws everyone off it — a shed layer is not a cosmetic
                // event, it clears the ground around the boss.
                _acts.Add(new BossAct(BossActKind.Shock, new Vector3(Position.X, 0f, Position.Y),
                    Vector3.UnitY, Position, Radius * 4f));
            }
        }
        return amount;
    }

    /// <summary>
    /// LEECH only: what the boss takes back when one of its own adds dies near it. Deliberately
    /// small per body and capped at the current layer — a leeching boss can undo your progress
    /// on this bar and never on a bar you have already broken, so the fight can stall but can
    /// never actually run backwards.
    /// </summary>
    public void Leech(float amount)
    {
        if (Gene.Quirk != Quirk.Leech || !Alive || LayersLeft <= 0) return;
        int top = LayersLeft - 1;
        _layers[top] = MathF.Min(Gene.LayerHealth, _layers[top] + amount);
    }

    /// <summary>Whether a shot at the given point and height is on the body. Planar radius plus
    /// a vertical band, the same shape every other big thing in this game is hit through.</summary>
    public bool Hits(Vector2 shotXZ, float shotHeight)
    {
        if (!Alive || Untouchable) return false;
        if (Torus.DistanceSquared(shotXZ, Position) > Radius * Radius) return false;
        float low = Height - 1.5f;
        float high = Height + CarriageLift * Gene.Scale + BodyHeight;
        return shotHeight >= low && shotHeight <= high;
    }

    /// <summary>Whether a hit came in from the front, for the plate test. Half-angle of 70°, so
    /// "behind it" is a genuine flank rather than a pixel-perfect rear.</summary>
    public bool IsFrontal(Vector2 from)
    {
        Vector2 to = Torus.NearestImage(from, Position) - Position;
        if (to.LengthSquared() < 1e-6f) return true;
        return Vector2.Dot(Vector2.Normalize(to), Forward) > 0.34f;
    }

    // --- Housekeeping --------------------------------------------------------------

    private void Enter(State next, float length)
    {
        Phase = next;
        _phaseT = 0f;
        _phaseLen = length;
        if (next == State.Stalking) Wind = 0f;
    }

    private void Animate(float dt)
    {
        // The core spins faster the closer it is to acting, and faster again the more broken it
        // is. Visible from anywhere in the arena, which is what makes it the tell.
        float rate = 1.5f + 9f * Wind + 4f * Ruin;
        CoreSpin += dt * rate;
        if (Phase is State.Stalking or State.Recover)
            CoreHeat = MathF.Max(Ruin * 0.4f, CoreHeat - dt * 0.6f);
        GaitPhase += dt * 0.6f;
    }

    /// <summary>Which noise the wind-up of each module makes. Reuses the game's existing bank
    /// rather than inventing a sound per module — a boss should sound like this world.</summary>
    private static Cue CueForWind(AttackModule m) => m switch
    {
        AttackModule.Lance or AttackModule.Sweep => Cue.BeamCharge,
        AttackModule.Mortar => Cue.ThrowWhoosh,
        AttackModule.Charge => Cue.Alarm,
        AttackModule.Slam or AttackModule.Leap => Cue.FishCoil,
        AttackModule.Summon => Cue.HuntCall,
        AttackModule.Tether or AttackModule.Snare => Cue.CableFire,
        AttackModule.Shroud => Cue.MawHurt,
        AttackModule.Bulwarks => Cue.Clamp,
        AttackModule.Scream => Cue.CrabScream,
        AttackModule.Burrow => Cue.StructureCrack,
        _ => Cue.MawTeeth,
    };
}
