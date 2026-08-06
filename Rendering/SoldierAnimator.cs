using System.Numerics;
using Unrendered.Core;

namespace Unrendered.Rendering;

/// <summary>
/// What the body is doing, as the animator understands it. Not a list of clips to play —
/// nothing here is played back — but a set of readings the pose is blended between, several
/// of them at once whenever the body is between two.
/// </summary>
public enum SoldierMotion
{
    /// <summary>Both boots down, going nowhere.</summary>
    Standing,

    /// <summary>Both boots down, striding.</summary>
    Running,

    /// <summary>Just left something — the ground, or a cable let go of at pace. The body
    /// extends into the direction it was thrown.</summary>
    Launching,

    /// <summary>Hanging off a taut cable and carving. The signature reading.</summary>
    Swinging,

    /// <summary>Being hauled in. Tighter than a swing and pointed harder at the anchor.</summary>
    Reeling,

    /// <summary>In the air with nothing holding. Reads two completely different ways
    /// depending on how fast the body is already travelling.</summary>
    Falling,

    /// <summary>Clung to a wall on a short cable, weight on the feet.</summary>
    Perched,

    /// <summary>Coming down, with ground close enough to reach for.</summary>
    LandingPrep,

    /// <summary>Just arrived. Folded into the impact, on the way back up.</summary>
    Landed,
}

/// <summary>
/// A critically-damped-ish spring toward a target. The single building block underneath
/// everything in this file that has weight.
///
/// Nothing here lerps toward where it is meant to be, and the difference is the whole
/// point: a lerp arrives and stops, which is what a puppet on a string does. A spring
/// carries velocity, so it lags a target that moves, overshoots one that stops, and rings
/// down afterward — which is what a limb attached to a body that changed its mind does.
/// </summary>
internal struct Spring
{
    public float Value;
    public float Vel;

    /// <summary>
    /// Advances toward <paramref name="target"/>. Substepped, because the stiffnesses in
    /// this file are high enough that a single Euler step at a bad frame rate goes
    /// unstable — and an animation system that explodes when the frame drops is worse
    /// than one with no springs in it at all.
    /// </summary>
    public void Step(float target, float stiffness, float damping, float dt)
    {
        int steps = Math.Clamp((int)MathF.Ceiling(dt * 120f), 1, 8);
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            Vel += ((target - Value) * stiffness - Vel * damping) * h;
            Value += Vel * h;
        }
    }

    /// <summary>Hits the spring with an impulse — an impact, a shot, a cable catching.</summary>
    public void Kick(float amount) => Vel += amount;

    public void Set(float value) { Value = value; Vel = 0f; }
}

/// <summary>
/// The whole of how a person in this game moves, as a layered, stateful performance rather
/// than as a lookup from a state to a pose.
///
/// The figure it drives has had elbows and knees since it was built, and up to now it was
/// still posed by a pure function of a few flags and the clock — which means it had no
/// memory. A body with no memory cannot lag, cannot overshoot, cannot settle, and cannot
/// carry a landing three frames past the moment it happened, so every pose it strikes is
/// struck instantly and correctly and reads as a diagram of the pose rather than as a
/// person in it.
///
/// So this class is instanced <em>per soldier</em> and stepped with a real dt, and it holds:
///
/// <list type="bullet">
/// <item>a <b>base locomotion layer</b> — the nine readings of <see cref="SoldierMotion"/>,
/// crossfaded rather than switched, each of them a continuous function of speed, turn rate
/// and how hard the cables are pulling rather than a fixed shape;</item>
/// <item><b>momentum</b>, applied by running every joint of that layer through a spring, so
/// the limbs trail a body that changes direction and swing past it when it stops;</item>
/// <item><b>additive layers</b> stacked on top and never replacing it — the head and chest
/// tracking where the eyes are pointed, the flinch of being hit, the arc of a shot or a
/// swung blade, the breath;</item>
/// <item><b>secondary motion</b> on the kit, which on this figure is the loose layer: the
/// hip launchers swing on their mounts and the bottle settles a beat behind the chest;</item>
/// <item>and <b>inverse kinematics</b> last, because a solved foot has to win. Feet plant
/// on the surface under them and stay put while the body travels over them, and hands go
/// where the cables actually leave the body.</item>
/// </list>
///
/// It is deliberately cosmetic. Nothing in here is simulated, replicated or read back by
/// anything that decides an outcome: it is stepped on the render clock, off signals that
/// already exist, so a machine drawing a soldier at two hundred frames a second and one
/// drawing the same soldier at forty agree about where that soldier is and disagree only
/// about how gracefully it got there. That is the right trade — and it is what keeps a
/// twenty-player match from carrying a joint hierarchy on the wire.
///
/// No Raylib, no world queries, no entity references. Signals in, a
/// <see cref="SoldierPose"/> out.
/// </summary>
public sealed class SoldierAnimator
{
    // ================================================================================
    //  Input
    // ================================================================================

    /// <summary>
    /// Everything about a soldier this frame that changes how they are drawn. A plain
    /// struct filled in by whoever is drawing, so the same animator serves the player's
    /// own body, a team-mate's on the wire and an enemy squad's without knowing which is
    /// which — they are the same kit moving the same way, and the only honest difference
    /// between them is how good the signals are.
    /// </summary>
    public readonly record struct Signals(
        /// <summary>Canonical world position, for the foot planting to measure against.</summary>
        Vector2 Position,
        /// <summary>Metres off the grid.</summary>
        float Height,
        /// <summary>Body yaw in radians — 0 faces +Z.</summary>
        float Heading,
        /// <summary>Full world momentum. The single most informative signal here: the air
        /// layer is very nearly a function of this vector alone.</summary>
        Vector3 Velocity,
        /// <summary>Both boots on a surface.</summary>
        bool Grounded,
        /// <summary>Clung to a wall on a short line.</summary>
        bool Perched,
        /// <summary>At least one hook is holding something.</summary>
        bool Anchored,
        /// <summary>The reel is actually pulling.</summary>
        bool Reeling,
        /// <summary>How hard each cable is pulling, 0..1, and whether it is out at all.
        /// Drives which arm is being hauled and how far.</summary>
        float LeftTension, float RightTension, bool LeftOut, bool RightOut,
        /// <summary>Where each hook is, in world space, when it is out. The hands are
        /// solved toward the launchers these leave through, so an arm points at the line
        /// it is actually carrying the body on.</summary>
        Vector3 LeftHook, Vector3 RightHook,
        /// <summary>The lean into the arc, radians, as the simulation computed it. Used as
        /// a floor: the animator banks the body at least this hard and usually harder,
        /// since the sim's number is tuned for a camera and a body should commit further
        /// than a camera does.</summary>
        float Bank,
        /// <summary>Seconds of stagger left after something went badly.</summary>
        float Stagger,
        /// <summary>Blades drawn.</summary>
        bool Blades,
        /// <summary>Where the eyes are pointed, relative to the body's own heading: yaw
        /// off the centre line and pitch off the horizon, both in radians, positive pitch
        /// up. For the player's own body this is zero and their look pitch; for anyone
        /// else's it is whatever arrived on the wire.</summary>
        float LookYaw, float LookPitch,
        /// <summary>The surface height directly under the body. Zero on the grid, the roof
        /// height on top of a building.</summary>
        float GroundY,
        /// <summary>How big this figure is drawn. The solver works in the model's own
        /// metres, so it needs this to read a world position as a body-local one.</summary>
        float Scale,
        /// <summary>Wall-clock seconds, for the cycles nothing else drives.</summary>
        float Time);

    // ================================================================================
    //  Output
    // ================================================================================

    /// <summary>The figure, this frame. Rebuilt every <see cref="Step"/>.</summary>
    public SoldierPose Pose => _pose;

    /// <summary>What the body is mostly doing. Informational — the pose is a blend of
    /// several, and this is only the loudest of them.</summary>
    public SoldierMotion Motion { get; private set; } = SoldierMotion.Standing;

    /// <summary>How much of the figure's weight is on each foot, 0..1. Read by the
    /// viewmodel so a footfall the body takes is a footfall the view takes.</summary>
    public float LeftFootLoad { get; private set; }
    public float RightFootLoad { get; private set; }

    /// <summary>Where the stride is, in radians. Exposed so anything hanging off the body
    /// — a first-person pair of arms, a footstep cue — paces itself off the same cycle the
    /// legs are actually walking rather than off a second clock that drifts against it.</summary>
    public float Stride => _stride;

    /// <summary>How deep into a landing the body is, in metres of compression. Negative is
    /// crushed; it springs back up through zero and settles.</summary>
    public float Squash => _squash.Value;

    // ================================================================================
    //  Events
    // ================================================================================
    //
    // Most of what the animator reacts to it works out for itself by watching the signals
    // — a landing is the frame Grounded goes true, a launch is the frame it goes false, a
    // whip is a spike in acceleration while a cable is holding. Only the things that leave
    // no trace in the body's momentum have to be told.

    /// <summary>One round out of the launcher on the given hip. Winds the arc up and lets
    /// the shoulder absorb it.</summary>
    public void Fire(bool right)
    {
        _fireSide = right ? 1f : -1f;
        _fire = 1f;
        (right ? ref _rightArmPitch : ref _leftArmPitch).Kick(9f);
        _chestTwist.Kick(right ? 5.5f : -5.5f);
    }

    /// <summary>A blade swung. One committed arc: a wind-up against the direction of
    /// travel, a strike, and a follow-through that carries past the contact before the
    /// arms come back.</summary>
    public void Slash()
    {
        _slash = 1f;
        _slashSide = -_slashSide;   // alternating, so a run of them isn't one gesture twice
    }

    /// <summary>
    /// Took a hit from <paramref name="worldAngle"/> — the direction the damage came
    /// <em>from</em>, in world radians, matching the heading convention. Strength 0..1.
    ///
    /// The direction is the whole reason this is a call rather than a flag. A symmetric
    /// recoil reads as the animation of being hit; a head that snaps away from the thing
    /// that hit it reads as having been hit by that thing, and it costs one angle.
    /// </summary>
    public void Flinch(float worldAngle, float strength)
    {
        _flinchAngle = worldAngle;
        _flinch = MathF.Max(_flinch, Math.Clamp(strength, 0f, 1f));
    }

    /// <summary>The whole body thrown — a wall met at speed, a blast alongside. Rings down
    /// through the same springs everything else does.</summary>
    public void Jolt(float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        _squash.Kick(-2.4f * amount);
        _chestTwist.Kick(6f * amount * (_slashSide > 0f ? 1f : -1f));
        _leftArmPitch.Kick(7f * amount);
        _rightArmPitch.Kick(-7f * amount);
    }

    /// <summary>
    /// Where the ground is under an arbitrary point, for the feet to be planted on. Left
    /// null the world is a flat grid at zero, which is what it is nearly everywhere; hand
    /// it a probe and the feet find rooftops and parapets without anything else changing.
    /// </summary>
    public Func<Vector2, float>? GroundProbe;

    // ================================================================================
    //  Feel constants
    // ================================================================================
    // Every number below is load-bearing for how the figure reads, and each is named and
    // sited here rather than buried at its use so the whole performance can be retuned
    // from one screenful. They follow the same convention as the rig's own tunables: the
    // comment says what the number buys, not what the number is.

    // --- Crossfading ---

    /// <summary>How fast one reading gives way to another, in weight per second. Slow
    /// enough that nothing snaps, fast enough that a landing is a landing rather than a
    /// suggestion.</summary>
    private const float BlendRate = 7.5f;

    /// <summary>And how fast the readings that are impacts take over. An arrival has to be
    /// on the frame it happened or it is not an arrival.</summary>
    private const float ImpactBlendRate = 26f;

    // --- Momentum ---

    /// <summary>The limbs' spring. Deliberately underdamped — the ratio of these two is
    /// the amount of overshoot, and overshoot is the entire read of a limb that has weight
    /// on the end of it. Damped critically the figure is precise and dead.</summary>
    private const float LimbStiffness = 190f;
    private const float LimbDamping = 17f;

    /// <summary>The spine's, which is stiffer and better damped: a torso that wobbled like
    /// an arm would read as a body with no core rather than as one with weight.</summary>
    private const float SpineStiffness = 260f;
    private const float SpineDamping = 24f;

    /// <summary>The head's — the loosest thing on the body, because a head is heavy and
    /// carried on a neck, and a head that arrives instantly at where it is looking is the
    /// single most robotic thing a figure can do.</summary>
    private const float HeadStiffness = 120f;
    private const float HeadDamping = 14f;

    /// <summary>How hard the body's own acceleration throws the limbs about. This is
    /// inertia, applied as a force rather than animated: the arms are pushed opposite to
    /// wherever the body just accelerated, which is why a hard stop swings them forward
    /// and a cable catching swings them back.</summary>
    private const float InertiaGain = 0.030f;

    /// <summary>Ceiling on that, so a teleport-sized velocity change doesn't spin the arms
    /// through the torso.</summary>
    private const float MaxInertia = 0.85f;

    // --- Weight shift ---

    /// <summary>How far the hips and the shoulders twist against each other at a full
    /// stride. Contrapposto: the right leg forward turns the chest left. It is a small
    /// angle and it is most of what separates a walking person from a walking chair.</summary>
    private const float Contrapposto = 0.26f;

    /// <summary>How far the body banks into a turn, per radian a second of turn rate, and
    /// the hard ceiling on it. Well past what the camera does, because the camera is
    /// carrying a horizon and the body is carrying itself.</summary>
    private const float BankPerTurn = 0.55f;
    private const float MaxBodyBank = 0.95f;

    /// <summary>One full stride, in metres. Sets how fast the legs cycle for a given
    /// ground speed, and getting it wrong is what makes feet skate.</summary>
    private const float StrideLength = 1.55f;

    // --- Landings ---

    /// <summary>The landing spring: how hard the knees resist and how quickly they stop
    /// arguing about it. Underdamped on purpose, so a body that lands stands back up
    /// through level and settles rather than rising to a stop like a lift.</summary>
    private const float SquashStiffness = 105f;
    private const float SquashDamping = 12.5f;

    /// <summary>Impact speed at which the knees are fully buckled. Matches the rig's own
    /// hard-landing threshold, so what the body does and what the camera does are the same
    /// event.</summary>
    private const float FullSquashSpeed = 15f;

    /// <summary>How far the hips can drop into an arrival, in metres of the model's scale.
    /// A third of the leg, which is a real crouch and not a curtsy.</summary>
    private const float MaxSquash = 0.36f;

    // --- Look-at ---

    /// <summary>How far the head will turn off the body's centre line before it gives up
    /// and lets the body do the turning, and how much of the total the chest takes. A head
    /// that swivels a hundred and eighty degrees is a horror effect, and this game has one
    /// of those already.</summary>
    private const float MaxHeadYaw = 1.15f;
    private const float MaxHeadNod = 0.85f;
    private const float ChestShare = 0.32f;

    // --- Attacks ---

    /// <summary>Seconds a shot's arc takes end to end, and a blade's. The blade is longer
    /// because it has a wind-up worth seeing; a rifle's anticipation is a twitch.</summary>
    private const float FireTime = 0.34f;
    private const float SlashTime = 0.62f;

    /// <summary>Where in a blade's arc the edge actually arrives, as a fraction. Everything
    /// before it is anticipation and everything after is follow-through, which is the only
    /// structure that makes a swing read as having hit something.</summary>
    private const float SlashStrike = 0.42f;

    // --- Flinch ---

    /// <summary>How long a flinch takes to play out, and how far it throws the head.</summary>
    private const float FlinchTime = 0.45f;
    private const float FlinchHead = 0.75f;

    // --- Secondary motion ---

    /// <summary>The kit's spring. Much looser than anything attached to bone, since a thing
    /// on a strap is meant to visibly not be part of the body.</summary>
    private const float KitStiffness = 70f;
    private const float KitDamping = 8.5f;

    /// <summary>How hard the body's acceleration throws the kit about, and the ceiling.</summary>
    private const float KitGain = 0.055f;
    private const float MaxKitSwing = 0.55f;

    // --- Breath ---

    /// <summary>How far a breath lifts the chest, and how much harder the body breathes
    /// after a hard arrival. A figure that has just fallen twenty metres and breathes
    /// exactly as it did standing in the hangar is a figure nothing happened to.</summary>
    private const float BreathRise = 0.013f;
    private const float BreathLean = 0.016f;

    // ================================================================================
    //  State
    // ================================================================================

    private SoldierPose _pose;

    /// <summary>How much of each reading is currently in the figure. Never one-hot except
    /// after a long time in one state — which is exactly the point.</summary>
    private readonly float[] _weight = new float[9];

    // Sprung copies of the base layer, one per joint channel. The base layer writes
    // targets; these are what actually gets drawn, a beat behind and overshooting.
    private Spring _rise, _sway, _surge;
    private Spring _pelvisPitch, _pelvisRoll, _pelvisYaw;
    private Spring _chestPitch, _chestRoll, _chestYaw;
    private Spring _headYaw, _headNod, _headTilt;
    private Spring _leftArmPitch, _leftArmRoll, _leftForePitch, _leftForeRoll;
    private Spring _rightArmPitch, _rightArmRoll, _rightForePitch, _rightForeRoll;
    private Spring _leftThighPitch, _leftThighRoll, _leftShinPitch, _leftShinRoll;
    private Spring _rightThighPitch, _rightThighRoll, _rightShinPitch, _rightShinRoll;

    // Separate channels for the things that are not joints.
    private Spring _squash;                       // the knees taking an arrival
    private Spring _chestTwist;                   // impulses from shots and hits
    private Spring _leftKit, _rightKit, _bottle;  // the loose layer
    private Spring _bank;                         // the committed lean, eased on its own

    // Derived signals, smoothed here rather than by any caller.
    private Vector3 _prevVel;
    private Vector2 _prevDir = new(0f, 1f);
    private Vector3 _accel;
    private float _turn;              // signed radians a second the travel direction is turning
    private float _speed, _planar;
    private bool _hadGround = true;
    private float _airTime;
    private float _sinceLand = 99f;
    private float _sinceLaunch = 99f;

    // The stride, advanced by distance rather than by the clock so the feet cycle at the
    // rate the body is actually covering ground.
    private float _stride;

    // Where each boot is currently nailed down, in world coordinates, and how firmly.
    private Vector2 _leftPlant, _rightPlant;
    private float _leftPlantY, _rightPlantY;
    private bool _leftPlanted, _rightPlanted;

    // The one-shot layers.
    private float _fire, _fireSide = 1f;
    private float _slash, _slashSide = 1f;
    private float _flinch, _flinchAngle;

    private bool _started;

    // ================================================================================
    //  The step
    // ================================================================================

    /// <summary>
    /// One frame of the whole performance. Order is not arbitrary and each stage depends on
    /// the one above it: read what the body is doing, decide what it should therefore look
    /// like, let the springs make that arrival physical, lay the additive layers over the
    /// result, and finally let the solver overrule any of it that has to touch something
    /// real.
    /// </summary>
    public void Step(in Signals s, float dt)
    {
        dt = Math.Clamp(dt, 1f / 480f, 1f / 15f);

        if (!_started)
        {
            _started = true;
            _prevVel = s.Velocity;
            _prevDir = Facing(s.Heading);
            _hadGround = s.Grounded;
            _leftPlant = _rightPlant = s.Position;
        }

        ReadSignals(s, dt);

        // 1. The base locomotion layer: nine readings, crossfaded, each a function of the
        //    live numbers rather than a fixed shape.
        SoldierPose target = BaseLayer(s, dt);

        // 2. Momentum. The base layer says where the body should be; the springs decide
        //    when it gets there, and whether it goes past first.
        SoldierPose body = Follow(target, s, dt);

        // 3. Additive layers, stacked and never replacing.
        body.Add(BreathLayer(s));
        body.Add(LookLayer(s, dt));
        body.Add(AttackLayer(s, dt));
        body.Add(FlinchLayer(s, dt));
        BladeStance(ref body, s, dt);
        SecondaryLayer(ref body, s, dt);

        // 4. And the solver last, because a foot that is meant to be on the floor is on
        //    the floor whatever the layers above wanted.
        SolveContacts(ref body, s, dt);

        _pose = body;
    }

    /// <summary>
    /// Everything derived from the raw signals: how fast, how hard the direction is
    /// changing, what the body's own acceleration is in its own frame, and the transitions
    /// that no caller has to report because they are visible in the numbers.
    /// </summary>
    private void ReadSignals(in Signals s, float dt)
    {
        _speed = s.Velocity.Length();
        var planar = new Vector2(s.Velocity.X, s.Velocity.Z);
        _planar = planar.Length();

        // Acceleration, smoothed. Raw frame-to-frame differences of a velocity that a
        // constraint solver is writing four times a step are spiky enough to make the arms
        // buzz; this is the same physical quantity with the buzz taken off it.
        Vector3 raw = (s.Velocity - _prevVel) / dt;
        _accel = Vector3.Lerp(_accel, raw, 1f - MathF.Exp(-14f * dt));
        _prevVel = s.Velocity;

        // Turn rate, from how far the direction of travel swung this frame. Only measured
        // while actually going somewhere — the direction of a body at rest is noise, and
        // banking to noise is a figure with a tic.
        if (_planar > 1.2f)
        {
            Vector2 dir = planar / _planar;
            float cross = _prevDir.X * dir.Y - _prevDir.Y * dir.X;
            float dot = Vector2.Dot(_prevDir, dir);
            float swung = MathF.Atan2(cross, dot);
            _prevDir = dir;
            _turn = Lerp(_turn, swung / dt, 1f - MathF.Exp(-9f * dt));
        }
        else
        {
            _turn = Lerp(_turn, 0f, 1f - MathF.Exp(-6f * dt));
        }

        // The stride, advanced by ground covered. A figure whose legs cycle on the clock
        // skates whenever it is going any speed but the one the cycle was tuned for.
        if (s.Grounded)
            _stride += _planar / StrideLength * MathF.Tau * dt;
        else
            _stride += dt * 2f;   // idles on in the air so the legs never resume mid-step
        if (_stride > MathF.Tau * 1024f) _stride -= MathF.Tau * 1024f;

        // Leaving the ground and arriving back on it, worked out rather than reported —
        // which means it is right for a remote body whose one-frame flags never crossed
        // the wire at all.
        if (s.Grounded && !_hadGround)
        {
            float impact = MathF.Max(0f, -_prevVelY);
            _squash.Kick(-3.1f * Math.Clamp(impact / FullSquashSpeed, 0.12f, 1.5f));
            _sinceLand = 0f;
            _airTime = 0f;
            _leftPlanted = _rightPlanted = false;
        }
        else if (!s.Grounded && _hadGround)
        {
            _sinceLaunch = 0f;
            _leftPlanted = _rightPlanted = false;
        }

        _prevVelY = s.Velocity.Y;
        _hadGround = s.Grounded;
        if (!s.Grounded) _airTime += dt; else _airTime = 0f;
        _sinceLand += dt;
        _sinceLaunch += dt;

        // A cable catching hard, or a body meeting something that stopped it. Both are the
        // same reading — a big acceleration the body did not ask for — and both want the
        // same answer, which is that the limbs keep going for a moment.
        float jerk = _accel.Length();
        if (jerk > 55f && s.Anchored) _chestTwist.Kick(Math.Clamp(jerk / 90f, 0f, 1.4f) * 3f);

        // The one-shot layers age.
        if (_fire > 0f) _fire = MathF.Max(0f, _fire - dt / FireTime);
        if (_slash > 0f) _slash = MathF.Max(0f, _slash - dt / SlashTime);
        if (_flinch > 0f) _flinch = MathF.Max(0f, _flinch - dt / FlinchTime);
    }

    private float _prevVelY;

    // ================================================================================
    //  The base locomotion layer
    // ================================================================================

    /// <summary>
    /// Decides how much of each reading the body is currently in, eases the weights toward
    /// it, and sums the nine poses by those weights.
    ///
    /// Nothing switches. A body coming off a swing into a fall is genuinely in both for
    /// about a fifth of a second, and a body arriving is in the fall, the arrival and the
    /// stand at once — which is the difference between an animation that transitions and a
    /// person who does.
    /// </summary>
    private SoldierPose BaseLayer(in Signals s, float dt)
    {
        Span<float> want = stackalloc float[9];
        Classify(s, want);

        // Ease every weight toward its share. Impacts get their own rate: an arrival that
        // faded in over a fifth of a second would land the body after it had stopped.
        for (int i = 0; i < 9; i++)
        {
            float rate = i is (int)SoldierMotion.Landed or (int)SoldierMotion.Launching
                ? ImpactBlendRate : BlendRate;
            _weight[i] = MoveToward(_weight[i], want[i], rate * dt);
        }

        float total = 0f;
        for (int i = 0; i < 9; i++) total += _weight[i];
        if (total < 1e-4f) { _weight[(int)SoldierMotion.Standing] = 1f; total = 1f; }

        // Whichever is loudest, for anything that wants to ask.
        int best = 0;
        for (int i = 1; i < 9; i++) if (_weight[i] > _weight[best]) best = i;
        Motion = (SoldierMotion)best;

        var pose = SoldierPose.Rest;
        float inv = 1f / total;
        for (int i = 0; i < 9; i++)
        {
            float w = _weight[i] * inv;
            if (w < 1e-3f) continue;
            SoldierPose p = PoseFor((SoldierMotion)i, s);
            pose.AddScaled(p, w);
        }

        // The bank rides on top of the blend rather than inside each pose, because it
        // applies to every one of them and easing it once keeps the horizon rolling at a
        // readable rate no matter what the body is doing underneath.
        float wantBank = -Math.Clamp(_turn * BankPerTurn, -MaxBodyBank, MaxBodyBank);
        // The simulation's own bank is a floor. It is computed off lateral travel rather
        // than turn rate, so it holds a lean through a long steady arc that a turn-rate
        // reading alone would let go of.
        if (MathF.Abs(s.Bank) > MathF.Abs(wantBank)) wantBank = -s.Bank;
        if (s.Grounded) wantBank *= 0.35f;   // a person leans into a corner, not a motorcycle
        _bank.Step(wantBank, 60f, 11f, dt);

        pose.ChestRoll += _bank.Value * 0.55f;
        pose.PelvisRoll += _bank.Value * 0.45f;
        pose.HeadTilt -= _bank.Value * 0.22f;   // the head stays nearer level than the body

        ReadFootLoad(s);
        return pose;
    }

    /// <summary>
    /// Which boot is carrying, in one place rather than written by whichever pose function
    /// happened to run last. Only meaningful with weight on the ground: in the air both
    /// are zero, which is what anything reading this — the viewmodel's footfall bob, a
    /// step cue — should see.
    /// </summary>
    private void ReadFootLoad(in Signals s)
    {
        if (!s.Grounded && !s.Perched) { LeftFootLoad = RightFootLoad = 0f; return; }
        if (s.Perched) { LeftFootLoad = RightFootLoad = 0.5f; return; }

        // Standing, both feet share it and the share follows the slow weight shift; moving,
        // it alternates with the stride.
        float pace = Math.Clamp(_planar / 5.5f, 0f, 1f);
        float swing = MathF.Sin(_stride);
        float shift = MathF.Sin(s.Time * 0.55f);
        float still = 0.5f - shift * 0.35f;
        float striding = MathF.Max(0f, -swing);
        LeftFootLoad = Lerp(still, striding, pace);
        RightFootLoad = Lerp(1f - still, MathF.Max(0f, swing), pace);
    }

    /// <summary>
    /// How much of each reading the body is in. Written as shares rather than as a single
    /// choice, because the honest answer is often two of them at once — reeling <em>is</em>
    /// swinging, harder, and a body a metre off the ground at the end of a fall is in the
    /// fall and reaching for the floor simultaneously.
    /// </summary>
    private void Classify(in Signals s, Span<float> w)
    {
        w.Clear();

        if (s.Grounded)
        {
            // The stand and the stride, shared out by how fast the boots are moving.
            float pace = Math.Clamp(_planar / 5.5f, 0f, 1f);
            w[(int)SoldierMotion.Standing] = 1f - pace;
            w[(int)SoldierMotion.Running] = pace;

            // And the arrival, on top of both, for as long as it is still ringing down.
            float land = Math.Clamp(1f - _sinceLand / 0.45f, 0f, 1f);
            if (land > 0f)
            {
                w[(int)SoldierMotion.Standing] *= 1f - land;
                w[(int)SoldierMotion.Running] *= 1f - land;
                w[(int)SoldierMotion.Landed] = land;
            }
            return;
        }

        if (s.Perched)
        {
            w[(int)SoldierMotion.Perched] = 1f;
            return;
        }

        // The extension off whatever was just left: the ground, or a cable released at
        // pace. Short, and it decays into whatever the body is actually doing next.
        float launch = Math.Clamp(1f - _sinceLaunch / 0.40f, 0f, 1f);
        if (_planar > 12f && !s.Anchored)
            launch = MathF.Max(launch, Math.Clamp(1f - _airTime / 0.30f, 0f, 1f));

        if (s.Anchored)
        {
            // Hanging. How much of it is a reel is simply whether the reel is running —
            // the two poses are the same body pointed differently, so the crossfade
            // between them is the reel visibly taking hold.
            float pull = s.Reeling ? 1f : 0f;
            float grip = MathF.Max(s.LeftTension, s.RightTension);
            w[(int)SoldierMotion.Reeling] = pull;
            w[(int)SoldierMotion.Swinging] = 1f - pull;
            // A slack cable is not a swing. The body falls until the line catches it, which
            // is the one moment the whole class is built around and it should look like it.
            if (grip < 0.15f && !s.Reeling)
            {
                w[(int)SoldierMotion.Swinging] = grip / 0.15f;
                w[(int)SoldierMotion.Falling] = 1f - grip / 0.15f;
            }
        }
        else
        {
            w[(int)SoldierMotion.Falling] = 1f;
        }

        // Reaching for the floor. Predicted rather than raycast: how long until this body,
        // travelling as it is, meets the surface it is over. Ground-proximity as a duration
        // rather than a distance is what makes the reach start at the same *moment* whether
        // the body is dropping gently or arriving at terminal speed.
        float drop = s.Height - s.GroundY;
        if (s.Velocity.Y < -0.5f && drop > 0f)
        {
            float eta = drop / -s.Velocity.Y;
            float reach = Math.Clamp(1f - eta / 0.55f, 0f, 1f);
            if (reach > 0f)
            {
                for (int i = 0; i < 9; i++) w[i] *= 1f - reach;
                w[(int)SoldierMotion.LandingPrep] = reach;
                launch *= 1f - reach;
            }
        }

        if (launch > 0f)
        {
            for (int i = 0; i < 9; i++) w[i] *= 1f - launch;
            w[(int)SoldierMotion.Launching] = launch;
        }
    }

    // ================================================================================
    //  The nine readings
    // ================================================================================

    private SoldierPose PoseFor(SoldierMotion m, in Signals s) => m switch
    {
        SoldierMotion.Standing => Standing(s),
        SoldierMotion.Running => Running(s),
        SoldierMotion.Launching => Launching(s),
        SoldierMotion.Swinging => Swinging(s),
        SoldierMotion.Reeling => Reeling(s),
        SoldierMotion.Falling => Falling(s),
        SoldierMotion.Perched => Perched(s),
        SoldierMotion.LandingPrep => LandingPrep(s),
        SoldierMotion.Landed => Landed(s),
        _ => SoldierPose.Rest,
    };

    /// <summary>
    /// At ease. Weight on one leg, then slowly on the other — a person standing still does
    /// not stand still, and the slow transfer is what stops an idle from reading as a
    /// pause button.
    /// </summary>
    private SoldierPose Standing(in Signals s)
    {
        float shift = MathF.Sin(s.Time * 0.55f);

        // The gesture: every seven seconds the figure brings its right launcher up and looks
        // down at it, then lets it drop back. A single raised-cosine window, so it eases in
        // and out rather than snapping. Four cycles running at deliberately unrelated rates
        // — this, the breath, the weight shift and the head's scan — is why an idle built
        // entirely out of sines never visibly loops.
        float phase = (s.Time % 7f) / 7f;
        float check = phase is > 0.55f and < 0.95f
            ? 0.5f - 0.5f * MathF.Cos((phase - 0.55f) / 0.4f * MathF.Tau)
            : 0f;

        var p = SoldierPose.Rest;
        p.Sway = shift * 0.035f;
        p.PelvisRoll = shift * 0.05f;
        p.ChestRoll = -shift * 0.075f;      // the shoulders stay level over tilted hips
        p.PelvisYaw = shift * 0.05f;
        p.ChestYaw = -shift * 0.06f;

        // The head scans, and drops to watch the rig during a check. Written here rather
        // than in the look layer because there is nothing to look at: this is a body with
        // no orders keeping its own eyes busy, which is a different thing from one tracking
        // a target, and it gives way to the look layer the moment there is one.
        p.HeadYaw = MathF.Sin(s.Time * 0.42f) * 0.4f - check * 0.5f;
        p.HeadNod = MathF.Sin(s.Time * 1.1f) * 0.03f + check * 0.30f;

        // Arms hang with a real bend. The elbows are the point of this figure, so the
        // resting pose is the one that shows them — and the right one lifts its launcher
        // into the eye-line for the check.
        p.LeftArm = LimbPose.FromBend(-0.10f - shift * 0.02f, -0.10f - shift * 0.03f, 0.42f, left: true);
        p.RightArm = LimbPose.FromBend(
            -0.10f + shift * 0.02f - check * 0.95f,
            -0.10f + shift * 0.03f - check * 0.18f,
            0.42f + check * 0.55f, left: false);
        p.RightRigLift = check * 0.30f;

        // The straightened leg carries; the other bends and takes almost nothing.
        p.LeftLeg = LimbPose.FromBend(0.05f - shift * 0.04f, 0.02f,
            -0.14f - MathF.Max(0f, shift) * 0.12f, left: true);
        p.RightLeg = LimbPose.FromBend(0.05f + shift * 0.04f, 0.02f,
            -0.14f - MathF.Max(0f, -shift) * 0.12f, left: false);

        return p;
    }

    /// <summary>
    /// A stride. Legs opposed, arms opposed to the legs, and — the part that matters — the
    /// hips and the shoulders turning against each other, so the body counter-rotates
    /// through every step instead of being carried along as one piece.
    /// </summary>
    private SoldierPose Running(in Signals s)
    {
        float swing = MathF.Sin(_stride);
        float lift = MathF.Max(0f, MathF.Cos(_stride));
        float fall = MathF.Max(0f, -MathF.Cos(_stride));
        float pace = Math.Clamp(_planar / 6f, 0f, 1f);
        float hurt = Math.Clamp(s.Stagger, 0f, 1f);

        var p = SoldierPose.Rest;
        // Two rises per stride — the body vaults over each planted leg — which is the
        // cheapest possible thing that makes a run read as pushing off a floor.
        p.Rise = -0.028f * pace * (0.5f + 0.5f * MathF.Cos(_stride * 2f)) - 0.10f * hurt;
        p.Surge = 0.02f * pace;
        p.PelvisPitch = 0.10f * pace + 0.30f * hurt;
        p.ChestPitch = 0.13f * pace + 0.35f * hurt;

        // Contrapposto. The leading leg's side of the pelvis comes forward and the chest
        // answers the other way.
        p.PelvisYaw = -Contrapposto * swing * pace;
        p.ChestYaw = Contrapposto * 1.15f * swing * pace;
        p.PelvisRoll = 0.09f * pace * MathF.Sin(_stride * 2f);
        p.ChestRoll = -0.06f * pace * MathF.Sin(_stride * 2f);

        p.HeadNod = -0.10f + 0.5f * hurt;

        float armSwing = 0.62f * pace;
        p.LeftArm = LimbPose.FromBend(-armSwing * swing, -0.12f, 0.75f + 0.25f * lift, left: true);
        p.RightArm = LimbPose.FromBend(armSwing * swing, -0.12f, 0.75f + 0.25f * fall, left: false);

        p.LeftLeg = LimbPose.FromBend(0.75f * swing * pace + 0.6f * hurt, 0.03f,
            -0.55f * lift * pace - 0.7f * hurt, left: true);
        p.RightLeg = LimbPose.FromBend(-0.75f * swing * pace + 0.2f * hurt, 0.03f,
            -0.55f * fall * pace - 0.4f * hurt, left: false);

        return p;
    }

    /// <summary>
    /// Thrown. The body extends into whatever it was just launched along: chest opening,
    /// arms sweeping back off the shoulders, legs trailing straight. A diver leaving the
    /// board, and the shape that says a great deal of speed has just been converted into
    /// height or the other way about.
    /// </summary>
    private SoldierPose Launching(in Signals s)
    {
        float commit = Math.Clamp(_planar / 22f, 0.3f, 1f);
        // Rising launches arch back; falling ones fold forward. Same extension, opposite
        // sign, and the body picks the one that matches where it is actually going.
        float rising = Math.Clamp(s.Velocity.Y / 12f, -1f, 1f);

        var p = SoldierPose.Rest;
        p.Surge = 0.05f * commit;
        p.PelvisPitch = 0.30f * commit - 0.25f * rising;
        p.ChestPitch = 0.40f * commit - 0.45f * rising;
        p.HeadNod = -0.45f - 0.2f * commit;

        // Arms swept back and out. The elbows stay nearly straight — an extension with
        // bent elbows reads as flailing rather than as commitment.
        p.LeftArm = LimbPose.FromBend(0.95f * commit, -0.62f * commit, 0.18f, left: true);
        p.RightArm = LimbPose.FromBend(0.95f * commit, -0.62f * commit, 0.18f, left: false);

        p.LeftLeg = LimbPose.FromBend(0.30f + 0.35f * commit, 0.10f, 0.20f, left: true);
        p.RightLeg = LimbPose.FromBend(0.22f + 0.40f * commit, 0.10f, 0.28f, left: false);

        return p;
    }

    /// <summary>
    /// Hanging off a taut line and carving. The chest pitches down the line of travel and
    /// the whole body corkscrews into the turn — not the limbs, the body, which is the one
    /// cue that separates flying from sliding sideways through the air.
    ///
    /// The fold is a function of speed alone, so how fast a soldier is going is legible in
    /// the shape itself rather than only in how quickly they cross the frame.
    /// </summary>
    private SoldierPose Swinging(in Signals s)
    {
        float fold = Math.Clamp(_planar / 26f, 0.25f, 1f);
        float twist = Math.Clamp(_turn * 0.22f, -0.75f, 0.75f);

        var p = SoldierPose.Rest;
        p.PelvisPitch = 0.30f + 0.35f * fold;
        p.ChestPitch = 0.35f + 0.55f * fold;
        // The corkscrew: hips into the new direction first, chest following a beat later
        // through the springs, which is what makes a hard corner read as being carved
        // rather than as the whole figure being rotated.
        p.PelvisYaw = twist;
        p.ChestYaw = twist * 0.55f;
        // The head stays up while the body goes flat. A person who has tucked their chin
        // has stopped looking where they are going, and has stopped being a person.
        p.HeadNod = -0.45f - 0.30f * fold;

        // Both arms up and forward on the launchers — the shape a body takes when its
        // weight is hanging off its own hips.
        float haul = Math.Clamp(MathF.Max(s.LeftTension, s.RightTension), 0f, 1f);
        p.LeftArm = LimbPose.FromBend(-0.95f * fold - 0.25f * haul, -0.26f - 0.14f * fold, 0.48f, left: true);
        p.RightArm = LimbPose.FromBend(-0.95f * fold - 0.25f * haul, -0.26f - 0.14f * fold, 0.48f, left: false);

        // Legs trailing, tucked tighter the faster it goes, and split slightly so the
        // silhouette has two of them rather than one thick one.
        p.LeftLeg = LimbPose.FromBend(0.55f + 0.55f * fold, 0.08f, 0.35f + 0.55f * fold, left: true);
        p.RightLeg = LimbPose.FromBend(0.45f + 0.60f * fold, 0.06f, 0.45f + 0.45f * fold, left: false);

        return p;
    }

    /// <summary>
    /// Being hauled. The same body as a swing, drawn tighter and aimed harder: knees up
    /// under the chest, arms in, everything gathered toward the line that is doing the
    /// pulling. A body being accelerated makes itself small.
    /// </summary>
    private SoldierPose Reeling(in Signals s)
    {
        float fold = Math.Clamp(_planar / 26f, 0.3f, 1f);

        var p = SoldierPose.Rest;
        p.PelvisPitch = 0.45f + 0.30f * fold;
        p.ChestPitch = 0.30f + 0.35f * fold;
        p.PelvisYaw = Math.Clamp(_turn * 0.16f, -0.5f, 0.5f);
        p.HeadNod = -0.55f - 0.20f * fold;

        // Arms drawn up and in, elbows hard — the pose of pulling on something rather than
        // hanging off it. The asymmetry follows which cable is actually taking the load.
        float lean = Math.Clamp(s.RightTension - s.LeftTension, -1f, 1f);
        p.LeftArm = LimbPose.FromBend(-1.35f - 0.15f * fold, -0.16f + 0.10f * lean, 1.05f, left: true);
        p.RightArm = LimbPose.FromBend(-1.35f - 0.15f * fold, -0.16f - 0.10f * lean, 1.05f, left: false);

        p.LeftLeg = LimbPose.FromBend(-0.35f, 0.10f, -0.95f - 0.25f * fold, left: true);
        p.RightLeg = LimbPose.FromBend(-0.25f, 0.10f, -0.85f - 0.25f * fold, left: false);

        return p;
    }

    /// <summary>
    /// In the air with nothing holding, which is two entirely different bodies depending on
    /// how fast it is already going.
    ///
    /// Slow, it tumbles and spreads to catch air — arms and legs out, the body loose and
    /// looking for a stable attitude, which is what a person who has simply come off
    /// something actually does. Fast, it streamlines: arms pulled back, legs trailing
    /// straight, the body a line pointed where it is going. Between them it is a blend, so
    /// coasting out of a boost visibly relaxes into a tumble as the speed bleeds off.
    /// </summary>
    private SoldierPose Falling(in Signals s)
    {
        float glide = Math.Clamp((_planar - 6f) / 16f, 0f, 1f);
        float spread = 1f - glide;
        // The slow tumble: a slow roll about the body's own axis, damped as the glide takes
        // over, since a streamlined body has stopped rotating by definition.
        float tumble = MathF.Sin(s.Time * 1.6f) * spread;

        var p = SoldierPose.Rest;
        p.PelvisPitch = 0.20f + 0.55f * glide - 0.18f * spread;
        p.ChestPitch = 0.15f + 0.75f * glide;
        p.ChestYaw = tumble * 0.22f + Math.Clamp(_turn * 0.14f, -0.4f, 0.4f);
        p.PelvisYaw = -tumble * 0.16f;
        p.ChestRoll = tumble * 0.20f;
        p.HeadNod = -0.30f - 0.35f * glide;

        // Spread: arms wide and high, elbows soft, legs open — a shape with a lot of
        // surface, which is exactly what it is meant to read as.
        var wideL = LimbPose.FromBend(-0.55f, 1.05f, 0.55f, left: true);
        var wideR = LimbPose.FromBend(-0.55f, 1.05f, 0.55f, left: false);
        // Streamlined: swept back and tight to the body.
        var tightL = LimbPose.FromBend(0.90f, 0.34f, 0.22f, left: true);
        var tightR = LimbPose.FromBend(0.90f, 0.34f, 0.22f, left: false);
        p.LeftArm = LimbPose.Lerp(wideL, tightL, glide);
        p.RightArm = LimbPose.Lerp(wideR, tightR, glide);
        p.LeftArm.UpperRoll += 0.10f * tumble;
        p.RightArm.UpperRoll -= 0.10f * tumble;

        var legWideL = LimbPose.FromBend(0.10f, 0.26f, -0.45f, left: true);
        var legWideR = LimbPose.FromBend(0.10f, 0.26f, -0.45f, left: false);
        var legTightL = LimbPose.FromBend(0.55f, 0.05f, 0.42f, left: true);
        var legTightR = LimbPose.FromBend(0.55f, 0.05f, 0.42f, left: false);
        p.LeftLeg = LimbPose.Lerp(legWideL, legTightL, glide);
        p.RightLeg = LimbPose.Lerp(legWideR, legTightR, glide);
        p.LeftLeg.UpperRoll += 0.12f * spread;
        p.RightLeg.UpperRoll -= 0.12f * spread;

        return p;
    }

    /// <summary>Coiled on a wall. Knees drawn up, weight forward on the line, head up and
    /// watching — a figure waiting rather than a figure resting.</summary>
    private SoldierPose Perched(in Signals s)
    {
        var p = SoldierPose.Rest;
        p.PelvisPitch = 0.16f;
        p.ChestPitch = 0.24f;
        p.HeadNod = -0.22f;
        p.HeadYaw = MathF.Sin(s.Time * 0.6f) * 0.18f;

        p.LeftArm = LimbPose.FromBend(-0.75f, -0.22f, 0.95f, left: true);
        p.RightArm = LimbPose.FromBend(-1.15f, -0.20f, 1.15f, left: false);
        p.LeftLeg = LimbPose.FromBend(-0.95f, 0.16f, -1.25f, left: true);
        p.RightLeg = LimbPose.FromBend(-0.80f, 0.14f, -1.05f, left: false);

        return p;
    }

    /// <summary>
    /// Reaching for the floor. Knees drawing up and forward, torso pitching over them,
    /// arms coming down and out to absorb — the shape a body makes in the last third of a
    /// second before it arrives, and the thing whose absence makes a landing look like a
    /// figure being teleported onto the ground.
    /// </summary>
    private SoldierPose LandingPrep(in Signals s)
    {
        float hard = Math.Clamp(-s.Velocity.Y / FullSquashSpeed, 0.2f, 1.3f);

        var p = SoldierPose.Rest;
        p.Rise = -0.03f * hard;
        p.PelvisPitch = 0.22f * hard;
        p.ChestPitch = 0.30f * hard;
        p.HeadNod = -0.15f;

        p.LeftArm = LimbPose.FromBend(-0.55f * hard, -0.42f * hard, 0.70f, left: true);
        p.RightArm = LimbPose.FromBend(-0.55f * hard, -0.42f * hard, 0.70f, left: false);

        // Knees up and ahead of the hips, so the legs get under the body before it lands
        // rather than after.
        p.LeftLeg = LimbPose.FromBend(-0.55f * hard, 0.10f, -0.80f * hard, left: true);
        p.RightLeg = LimbPose.FromBend(-0.48f * hard, 0.10f, -0.72f * hard, left: false);

        return p;
    }

    /// <summary>
    /// Arrived. The knees and hips take it, the chest folds over them, and one hand comes
    /// down toward the floor. How deep is decided by the squash spring rather than written
    /// here, so an arrival from two metres and one from twenty are the same pose at
    /// different amplitudes — and both spring back up through level and settle.
    /// </summary>
    private SoldierPose Landed(in Signals s)
    {
        float deep = Math.Clamp(-_squash.Value / MaxSquash, 0f, 1.6f);

        var p = SoldierPose.Rest;
        p.PelvisPitch = 0.30f * deep;
        p.ChestPitch = 0.45f * deep;
        p.HeadNod = 0.25f * deep;
        p.Surge = -0.02f * deep;

        p.LeftArm = LimbPose.FromBend(-0.30f - 0.55f * deep, -0.30f - 0.25f * deep, 0.85f, left: true);
        p.RightArm = LimbPose.FromBend(-0.20f - 0.45f * deep, -0.26f - 0.20f * deep, 0.75f, left: false);

        // Both knees fold. Not evenly — a person arrives on one leg harder than the other,
        // and a perfectly symmetric crouch is the one thing a real landing never is.
        p.LeftLeg = LimbPose.FromBend(-0.28f * deep, 0.10f, -1.15f * deep, left: true);
        p.RightLeg = LimbPose.FromBend(-0.18f * deep, 0.08f, -0.85f * deep, left: false);

        return p;
    }

    // ================================================================================
    //  Momentum
    // ================================================================================

    /// <summary>
    /// Runs the whole base layer through its springs, so what gets drawn is not what the
    /// layer asked for but where a body with mass would actually have got to by now.
    ///
    /// On top of that, the body's own acceleration is fed into the limbs as a force. That
    /// is inertia and it is why this reads as physical rather than as smoothed: when the
    /// body is accelerated by something outside itself — a cable catching, a wall, the
    /// floor — the arms are left behind by exactly as much as the shove was hard, and
    /// then catch up and overshoot. Nothing about that is animated.
    /// </summary>
    private SoldierPose Follow(in SoldierPose t, in Signals s, float dt)
    {
        // The acceleration, read in the body's own frame: forward, up and sideways as the
        // figure understands them, since that is the frame the joints are written in.
        // The acceleration, read in the body's own frame. Two quantities: how hard the body
        // is being slowed along the way it faces, and how hard it is being pushed to its own
        // left.
        Vector2 f = Facing(s.Heading);
        var leftward = new Vector2(f.Y, -f.X);
        float lag = Math.Clamp(-(_accel.X * f.X + _accel.Z * f.Y) * InertiaGain,
            -MaxInertia, MaxInertia);
        float side = Math.Clamp((_accel.X * leftward.X + _accel.Z * leftward.Y) * InertiaGain,
            -MaxInertia, MaxInertia);
        float drop = Math.Clamp(-_accel.Y * InertiaGain * 0.6f, -MaxInertia, MaxInertia);

        _rise.Step(t.Rise + drop * 0.02f, SpineStiffness, SpineDamping, dt);
        _sway.Step(t.Sway - side * 0.02f, SpineStiffness, SpineDamping, dt);
        _surge.Step(t.Surge + lag * 0.025f, SpineStiffness, SpineDamping, dt);

        // The spine leans against the shove — the part of a stop that reads as the body
        // continuing after the feet have stopped.
        //
        // The signs here look inconsistent and are not: a torso is a block standing up from
        // its pivot and a limb hangs down from one, so the same rotation tips them opposite
        // ways. Positive pitch leans a torso forward and swings a limb backward; positive
        // roll tips a torso right and swings a limb left. One shove, therefore, is added to
        // the spine and subtracted from everything hanging off it.
        _pelvisPitch.Step(t.PelvisPitch + lag * 0.16f, SpineStiffness, SpineDamping, dt);
        _pelvisRoll.Step(t.PelvisRoll + side * 0.14f, SpineStiffness, SpineDamping, dt);
        _pelvisYaw.Step(t.PelvisYaw, SpineStiffness, SpineDamping, dt);
        _chestPitch.Step(t.ChestPitch + lag * 0.24f, SpineStiffness * 0.8f, SpineDamping * 0.85f, dt);
        _chestRoll.Step(t.ChestRoll + side * 0.20f, SpineStiffness * 0.8f, SpineDamping * 0.85f, dt);
        _chestYaw.Step(t.ChestYaw, SpineStiffness * 0.8f, SpineDamping * 0.85f, dt);

        _headYaw.Step(t.HeadYaw, HeadStiffness, HeadDamping, dt);
        _headNod.Step(t.HeadNod + lag * 0.10f, HeadStiffness, HeadDamping, dt);
        _headTilt.Step(t.HeadTilt + side * 0.12f, HeadStiffness, HeadDamping, dt);

        // The limbs get the most of it, and the forearms and shins more than the segments
        // above them — the far end of a chain lags hardest, which is the whole read of
        // follow-through.
        FollowLimb(ref _leftArmPitch, ref _leftArmRoll, ref _leftForePitch, ref _leftForeRoll,
            t.LeftArm, lag, side, dt);
        FollowLimb(ref _rightArmPitch, ref _rightArmRoll, ref _rightForePitch, ref _rightForeRoll,
            t.RightArm, lag, side, dt);
        FollowLimb(ref _leftThighPitch, ref _leftThighRoll, ref _leftShinPitch, ref _leftShinRoll,
            t.LeftLeg, lag * 0.5f, side * 0.5f, dt);
        FollowLimb(ref _rightThighPitch, ref _rightThighRoll, ref _rightShinPitch, ref _rightShinRoll,
            t.RightLeg, lag * 0.5f, side * 0.5f, dt);

        // The arrival, which is its own spring because it is the only channel that gets hit
        // with an impulse rather than chased toward a target.
        // A launcher lifted by the hand holding it. Sprung like everything else, because a
        // piece of steel raised into the eye-line arrives with weight or it arrives like a
        // menu item sliding into place.
        _leftLift.Step(t.LeftRigLift, KitStiffness * 2.2f, KitDamping * 1.6f, dt);
        _rightLift.Step(t.RightRigLift, KitStiffness * 2.2f, KitDamping * 1.6f, dt);

        _squash.Step(0f, SquashStiffness, SquashDamping, dt);
        if (_squash.Value < -MaxSquash) { _squash.Value = -MaxSquash; _squash.Vel = MathF.Max(0f, _squash.Vel); }
        _chestTwist.Step(0f, 140f, 13f, dt);

        var p = SoldierPose.Rest;
        p.Rise = _rise.Value + _squash.Value;
        p.Sway = _sway.Value;
        p.Surge = _surge.Value;
        p.PelvisPitch = _pelvisPitch.Value;
        p.PelvisRoll = _pelvisRoll.Value;
        p.PelvisYaw = _pelvisYaw.Value;
        p.ChestPitch = _chestPitch.Value;
        p.ChestRoll = _chestRoll.Value;
        p.ChestYaw = _chestYaw.Value + _chestTwist.Value * 0.06f;
        p.HeadYaw = _headYaw.Value;
        p.HeadNod = _headNod.Value;
        p.HeadTilt = _headTilt.Value;

        p.LeftArm = Read(_leftArmPitch, _leftArmRoll, _leftForePitch, _leftForeRoll);
        p.RightArm = Read(_rightArmPitch, _rightArmRoll, _rightForePitch, _rightForeRoll);
        p.LeftLeg = Read(_leftThighPitch, _leftThighRoll, _leftShinPitch, _leftShinRoll);
        p.RightLeg = Read(_rightThighPitch, _rightThighRoll, _rightShinPitch, _rightShinRoll);

        p.LeftRigLift = _leftLift.Value;
        p.RightRigLift = _rightLift.Value;
        return p;
    }

    private Spring _leftLift, _rightLift;

    private static void FollowLimb(ref Spring up, ref Spring upRoll, ref Spring low, ref Spring lowRoll,
        in LimbPose t, float lag, float side, float dt)
    {
        up.Step(t.UpperPitch - lag, LimbStiffness, LimbDamping, dt);
        upRoll.Step(t.UpperRoll - side * 0.55f, LimbStiffness, LimbDamping, dt);
        // Softer springs down the chain, so the forearm trails the upper arm and arrives
        // after it. This is where follow-through actually comes from.
        low.Step(t.LowerPitch - lag * 1.45f, LimbStiffness * 0.7f, LimbDamping * 0.82f, dt);
        lowRoll.Step(t.LowerRoll - side * 0.72f, LimbStiffness * 0.7f, LimbDamping * 0.82f, dt);
    }

    private static LimbPose Read(in Spring up, in Spring upRoll, in Spring low, in Spring lowRoll)
        => new() { UpperPitch = up.Value, UpperRoll = upRoll.Value,
                   LowerPitch = low.Value, LowerRoll = lowRoll.Value };

    // ================================================================================
    //  Additive layers
    // ================================================================================

    /// <summary>
    /// Breathing. Small, always on, and harder when the body has been through something —
    /// a figure that has just fallen twenty metres and breathes exactly as it did standing
    /// in the hangar is a figure nothing happened to.
    /// </summary>
    private SoldierPose BreathLayer(in Signals s)
    {
        float effort = 1f + Math.Clamp(_planar / 18f, 0f, 1f) + Math.Clamp(s.Stagger, 0f, 1f);
        float breath = MathF.Sin(s.Time * (1.5f + 0.6f * effort));

        var p = SoldierPose.Rest;
        p.Rise = breath * BreathRise * effort;
        p.ChestPitch = -breath * BreathLean * effort;
        p.LeftArm.UpperPitch = breath * 0.022f;
        p.RightArm.UpperPitch = -breath * 0.022f;
        return p;
    }

    /// <summary>
    /// The head, and to a lesser degree the chest, tracking where the eyes are pointed —
    /// inside a cone, and sprung, so it arrives late and settles.
    ///
    /// This is the layer that makes the figure look <em>aware</em>. Everything else here is
    /// about weight; this one is the only thing on the body that says there is someone
    /// inside it deciding where to look, and it is worth more than any two of the others.
    /// </summary>
    private SoldierPose LookLayer(in Signals s, float dt)
    {
        float yaw = Math.Clamp(s.LookYaw, -MaxHeadYaw, MaxHeadYaw);
        float nod = Math.Clamp(-s.LookPitch, -MaxHeadNod, MaxHeadNod);

        // Past the cone the chest takes over, which is what a person does rather than
        // stopping their head at a limit and staring at a wall.
        float over = s.LookYaw - yaw;

        _lookYaw.Step(yaw * (1f - ChestShare), HeadStiffness, HeadDamping, dt);
        _lookNod.Step(nod * (1f - ChestShare), HeadStiffness, HeadDamping, dt);
        _lookChest.Step(yaw * ChestShare + Math.Clamp(over, -0.6f, 0.6f),
            SpineStiffness * 0.5f, SpineDamping * 0.9f, dt);

        var p = SoldierPose.Rest;
        p.HeadYaw = _lookYaw.Value;
        p.HeadNod = _lookNod.Value;
        p.ChestYaw = _lookChest.Value;
        p.ChestPitch = _lookNod.Value * 0.22f;
        return p;
    }

    private Spring _lookYaw, _lookNod, _lookChest;

    /// <summary>
    /// Shots and blade swings, as arcs rather than as poses.
    ///
    /// A rifle round is mostly recoil: the shoulder takes it and the chest twists out of
    /// the way, and both of those are already travelling through the springs from the
    /// impulse <see cref="Fire"/> put into them. What is added here is the small
    /// anticipation before it and the settle after.
    ///
    /// A blade is a real arc, and it has three parts or it has none: a wind-up that
    /// counter-rotates against the direction of the swing, a strike that crosses the whole
    /// range fast, and a follow-through that carries well past the contact point before
    /// the arms come back. Take away the wind-up and the swing has no weight; take away
    /// the follow-through and it reads as hitting a wall.
    /// </summary>
    private SoldierPose AttackLayer(in Signals s, float dt)
    {
        var p = SoldierPose.Rest;

        if (_fire > 0f)
        {
            // 1 at the shot, 0 by the end. The wind-up is the first sliver of it, inverted.
            float t = 1f - _fire;
            float wind = t < 0.12f ? -(1f - t / 0.12f) * 0.35f : 0f;
            float kick = MathF.Exp(-t * 9f) + wind;

            bool right = _fireSide > 0f;
            ref LimbPose arm = ref (right ? ref p.RightArm : ref p.LeftArm);
            arm.UpperPitch += 0.42f * kick;
            arm.LowerPitch += 0.30f * kick;
            arm.UpperRoll += (right ? -1f : 1f) * 0.14f * kick;
            p.ChestYaw += (right ? 0.16f : -0.16f) * kick;
            p.ChestPitch += 0.10f * kick;
            p.HeadNod += -0.07f * kick;
        }

        if (_slash > 0f)
        {
            float t = 1f - _slash;
            float side = _slashSide;

            // The three parts, as one continuous curve rather than three clips: the
            // wind-up eases back against the swing, the strike crosses fast, and the
            // follow-through decays out of the far end.
            float arc;
            if (t < SlashStrike)
            {
                float k = t / SlashStrike;
                arc = -MathF.Sin(k * MathF.PI * 0.5f) * 0.55f;   // back, and slowing as it loads
            }
            else
            {
                float k = (t - SlashStrike) / (1f - SlashStrike);
                // Across, past, and back — an overshoot that returns rather than stopping.
                arc = MathF.Sin(k * MathF.PI * 0.85f) * 1.55f * MathF.Exp(-k * 1.1f);
            }

            p.ChestYaw += arc * 0.42f * side;
            p.PelvisYaw += arc * 0.18f * side;
            p.ChestPitch += MathF.Abs(arc) * 0.14f;
            p.HeadYaw += arc * 0.18f * side;

            // Both arms are in it, one leading and one counter-balancing, because a person
            // swinging something heavy does not hold the other arm still.
            ref LimbPose lead = ref (side > 0f ? ref p.RightArm : ref p.LeftArm);
            ref LimbPose off = ref (side > 0f ? ref p.LeftArm : ref p.RightArm);
            lead.UpperPitch += -arc * 0.85f;
            lead.UpperRoll += -MathF.Abs(arc) * 0.45f;
            lead.LowerPitch += -arc * 0.55f;
            off.UpperPitch += arc * 0.35f;
            off.UpperRoll += MathF.Abs(arc) * 0.30f;
        }

        return p;
    }

    /// <summary>
    /// Being hit, laid over whatever the body was already doing — which is the whole
    /// reason it is additive. A soldier hit mid-swing should flinch <em>and go on
    /// swinging</em>; a flinch that replaced the base layer would drop them out of the arc
    /// they are in and read as a bug.
    ///
    /// Asymmetric, off the direction the damage came from: the head snaps away from it,
    /// the chest curls around it, and the arm on that side comes up. A symmetric recoil is
    /// an animation of being hit; this is a body reacting to something in particular.
    /// </summary>
    private SoldierPose FlinchLayer(in Signals s, float dt)
    {
        if (_flinch <= 0f) return SoldierPose.Rest;

        // Sharp in, slow out — an impact and then a recovery, never a symmetric bump.
        float t = 1f - _flinch;
        float amp = t < 0.18f ? t / 0.18f : MathF.Exp(-(t - 0.18f) * 4.5f);

        // Where the hit came from, in the body's own frame. Positive is from the left.
        float rel = Wrap(_flinchAngle - s.Heading);
        float fromLeft = MathF.Sin(rel);
        float fromFront = MathF.Cos(rel);

        var p = SoldierPose.Rest;
        p.HeadYaw = -fromLeft * FlinchHead * amp;
        p.HeadNod = fromFront * 0.55f * amp;      // hit from in front snaps the head back
        p.HeadTilt = fromLeft * 0.35f * amp;

        p.ChestPitch = fromFront * 0.42f * amp;
        p.ChestRoll = -fromLeft * 0.30f * amp;
        p.ChestYaw = -fromLeft * 0.34f * amp;
        p.PelvisPitch = fromFront * 0.16f * amp;
        p.Surge = -fromFront * 0.03f * amp;
        p.Sway = -fromLeft * 0.03f * amp;

        // The arm on the struck side comes up across the body; the other one flies out.
        ref LimbPose near = ref (fromLeft > 0f ? ref p.LeftArm : ref p.RightArm);
        ref LimbPose far = ref (fromLeft > 0f ? ref p.RightArm : ref p.LeftArm);
        near.UpperPitch += -0.55f * amp;
        near.UpperRoll += -0.45f * amp;
        near.LowerPitch += -0.65f * amp;
        far.UpperPitch += 0.30f * amp;
        far.UpperRoll += -0.30f * amp;

        // And the legs give slightly — a hit that the knees do not answer is a hit that
        // landed on a statue.
        p.LeftLeg.LowerPitch += -0.20f * amp;
        p.RightLeg.LowerPitch += -0.16f * amp;
        return p;
    }

    /// <summary>
    /// Blades out. Squares the shoulders and sweeps the edges back off them, over whatever
    /// flight pose is underneath — so a committed run keeps the fold and the bank it had
    /// and gains two bright edges.
    ///
    /// This one blends rather than adds, and it is the only layer here that does. The
    /// others are departures from wherever the body already was, which is what makes them
    /// stack; a stance is a place the arms have to <em>be</em>, and adding it to a pose
    /// that already had the arms somewhere would put them through the chest. So it eases in
    /// on a weight of its own and takes the arms over as it arrives.
    /// </summary>
    private void BladeStance(ref SoldierPose p, in Signals s, float dt)
    {
        _blades.Step(s.Blades ? 1f : 0f, 90f, 15f, dt);
        float w = Math.Clamp(_blades.Value, 0f, 1f);
        if (w < 0.002f) return;

        var readyL = LimbPose.FromBend(1.35f, 0.40f, -0.40f, left: true);
        var readyR = LimbPose.FromBend(1.35f, 0.40f, -0.40f, left: false);
        p.LeftArm = LimbPose.Lerp(p.LeftArm, readyL, w);
        p.RightArm = LimbPose.Lerp(p.RightArm, readyR, w);
        p.ChestPitch -= 0.14f * w;
    }

    private Spring _blades;

    /// <summary>
    /// The loose layer. This figure has no cape and no hair — what it has is kit, so the
    /// kit is what lags: the launchers swing on their hip mounts and the bottle settles a
    /// beat behind the chest it is strapped to.
    ///
    /// Driven by the body's own acceleration rather than by a noise function, which means
    /// it is quiet when the body is and thrown about when the body is, and never has to be
    /// told which is happening.
    /// </summary>
    private void SecondaryLayer(ref SoldierPose p, in Signals s, float dt)
    {
        Vector2 f = Facing(s.Heading);
        var leftward = new Vector2(f.Y, -f.X);

        // A launcher on a hip mount hangs like a limb, so it takes the shove the same way
        // one does — trailing the body rather than leading it.
        float swing = Math.Clamp(-(_accel.X * f.X + _accel.Z * f.Y) * -KitGain,
            -MaxKitSwing, MaxKitSwing);
        float roll = Math.Clamp((_accel.X * leftward.X + _accel.Z * leftward.Y) * -KitGain,
            -MaxKitSwing, MaxKitSwing);

        // Each launcher gets the lateral term with its own sign, so a hard turn throws the
        // outside one wide and presses the inside one against the hip.
        _leftKit.Step(swing + roll, KitStiffness, KitDamping, dt);
        _rightKit.Step(swing - roll, KitStiffness, KitDamping, dt);
        _bottle.Step(swing * 0.7f, KitStiffness * 1.3f, KitDamping * 1.2f, dt);

        p.LeftRigSwing += _leftKit.Value;
        p.RightRigSwing += _rightKit.Value;
        p.BottlePitch += _bottle.Value * 0.6f;
        p.BottleRoll += Math.Clamp(roll, -0.3f, 0.3f) * 0.5f;
    }

    // ================================================================================
    //  Inverse kinematics
    // ================================================================================

    /// <summary>
    /// The last word. Everything above decides what the body would like to be doing; this
    /// decides what it is actually touching, and where those disagree the contact wins.
    ///
    /// Two contacts matter on this chassis. Feet: while the boots are down, each one is
    /// nailed to the spot it was put on and stays there while the body travels over it,
    /// with the knee solving to keep it there — which is the difference between striding
    /// and skating, and the only way a foot ever lands on a surface that is not exactly
    /// where the pose assumed it was. Hands: while a cable is out, the hand goes to the
    /// launcher it is leaving through, so an arm is visibly holding the line that is
    /// carrying the body rather than waving near it.
    /// </summary>
    private void SolveContacts(ref SoldierPose p, in Signals s, float dt)
    {
        if (s.Grounded) SolveFeet(ref p, s, dt);
        SolveHands(ref p, s, dt);
    }

    /// <summary>
    /// Plants each boot in turn and solves the leg to it.
    ///
    /// A foot is planted when the stride says it is carrying, and it is planted at the
    /// place it happened to be at that moment, on the surface the probe reports under that
    /// place. It then stays at that world position — not that body-relative position —
    /// until the stride hands the weight to the other foot, so the body travels over a
    /// stationary foot exactly as a real one does.
    /// </summary>
    private void SolveFeet(ref SoldierPose p, in Signals s, float dt)
    {
        float swing = MathF.Sin(_stride);
        bool leftDown = swing <= 0f;
        float pace = Math.Clamp(_planar / 6f, 0f, 1f);

        // Where a boot goes down when it newly takes the weight: a little ahead of the hips
        // and out on its own side, on whatever surface the probe finds there. Where it goes
        // down is where it stays, and the body travels over it — which is the whole of not
        // skating.
        Vector2 f = Facing(s.Heading);
        // The figure's own left, in the world. Not the screen's: a model offset of +X is
        // drawn at (cos h, −sin h), and getting this backwards plants the left boot on the
        // right-hand side and walks the figure with its legs crossed.
        var across = new Vector2(f.Y, -f.X) * (SoldierSkeleton.HipX * s.Scale);
        Vector2 ahead = f * (0.18f + 0.42f * pace);

        if (!leftDown) _leftPlanted = false;
        else if (!_leftPlanted)
        {
            _leftPlant = Torus.Wrap(s.Position + ahead + across);
            _leftPlantY = GroundProbe?.Invoke(_leftPlant) ?? s.GroundY;
            _leftPlanted = true;
        }

        if (leftDown) _rightPlanted = false;
        else if (!_rightPlanted)
        {
            _rightPlant = Torus.Wrap(s.Position + ahead - across);
            _rightPlantY = GroundProbe?.Invoke(_rightPlant) ?? s.GroundY;
            _rightPlanted = true;
        }

        // A body barely moving keeps both boots down rather than hopping between them.
        if (_planar < 0.5f && !_leftPlanted)
        {
            _leftPlant = Torus.Wrap(s.Position + across);
            _leftPlantY = GroundProbe?.Invoke(_leftPlant) ?? s.GroundY;
            _leftPlanted = true;
        }
        if (_planar < 0.5f && !_rightPlanted)
        {
            _rightPlant = Torus.Wrap(s.Position - across);
            _rightPlantY = GroundProbe?.Invoke(_rightPlant) ?? s.GroundY;
            _rightPlanted = true;
        }

        // How far the solve gets to overrule the stride: exactly how much weight each boot
        // is carrying, which is already worked out once per frame. The hand-over is a ramp
        // rather than a switch, so the solve fades out of one leg as it fades into the
        // other and neither snaps.
        SolveLeg(ref p, ref p.LeftLeg, s, _leftPlant, _leftPlantY, LeftFootLoad, left: true);
        SolveLeg(ref p, ref p.RightLeg, s, _rightPlant, _rightPlantY, RightFootLoad, left: false);
    }

    /// <summary>
    /// Solves one leg onto a planted boot, and blends the answer in by how firmly that boot
    /// is actually carrying — so a foot swinging through the air keeps the stride the base
    /// layer wrote, and only a foot with weight on it is overruled.
    /// </summary>
    private static void SolveLeg(ref SoldierPose pose, ref LimbPose leg, in Signals s,
        Vector2 at, float y, float load, bool left)
    {
        if (load < 0.01f) return;

        // Both ends in the figure's own frame: the hip where the pose has put it, and the
        // boot at the world point it was planted on — raised by the height of a sole, since
        // the chain the solver is solving ends at an ankle and not at the floor.
        Vector3 hip = SoldierSkeleton.Hip(pose, left);
        Vector3 target = ToModel(at, y, s);
        target.Y += SoldierSkeleton.SoleDrop;

        // Knees point forward. The one pole vector in this file that is not a judgement
        // call: it is the only direction a knee has ever bent.
        if (Solve(hip, target, SoldierSkeleton.SegLen, SoldierSkeleton.SegLen,
                new Vector3(0f, 0f, 1f), out LimbPose solved))
            leg = LimbPose.Lerp(leg, solved, load);
    }

    /// <summary>
    /// Puts each hand on the launcher on its own hip whenever that launcher has a line out,
    /// and lets the base layer have the arm back when it does not.
    ///
    /// The point is not that the hand lands exactly on the grip — nobody can see that at
    /// this resolution. It is that the arm's whole shape is decided by where the load is,
    /// so a body hanging off its right cable has a right arm visibly taking the weight and
    /// a left one that is free to do something else.
    /// </summary>
    private void SolveHands(ref SoldierPose p, in Signals s, float dt)
    {
        // How far each hand has committed to its line. Sprung, and it has to be: a solved
        // hand overrules everything above it, so a grip that went from nothing to full in
        // one frame would teleport the arm onto the launcher — throwing away, at the exact
        // moment a cable bites, every bit of the momentum the layers above spent that whole
        // swing building. The hand takes hold of the grip; it does not appear on it.
        _leftGrip.Step(s.LeftOut ? Math.Clamp(0.40f + s.LeftTension * 0.60f, 0f, 1f) : 0f,
            GripStiffness, GripDamping, dt);
        _rightGrip.Step(s.RightOut ? Math.Clamp(0.40f + s.RightTension * 0.60f, 0f, 1f) : 0f,
            GripStiffness, GripDamping, dt);

        SolveHand(ref p, ref p.LeftArm, s, _leftGrip.Value, s.LeftHook, left: true);
        SolveHand(ref p, ref p.RightArm, s, _rightGrip.Value, s.RightHook, left: false);
    }

    /// <summary>How quickly a hand closes on a grip. Near critically damped — a hand that
    /// overshot the thing it was reaching for would look like a miss.</summary>
    private const float GripStiffness = 120f;
    private const float GripDamping = 21f;

    private Spring _leftGrip, _rightGrip;

    private static void SolveHand(ref SoldierPose pose, ref LimbPose arm, in Signals s,
        float grip, Vector3 hook, bool left)
    {
        if (grip < 0.01f) return;
        grip = Math.Clamp(grip, 0f, 1f);

        Vector3 shoulder = SoldierSkeleton.Shoulder(pose, left);
        Vector3 at = SoldierSkeleton.Launcher(pose, left);

        // The wrist is dragged a little along the line toward wherever the hook actually
        // is, so a cable running steeply up lifts the hand with it and one running away
        // behind pulls it back.
        Vector3 toward = ToModel(new Vector2(hook.X, hook.Z), hook.Y, s) - at;
        float len = toward.Length();
        if (len > 1e-3f) at += toward / len * (2.2f * SoldierSkeleton.U);

        // Elbows point back and out — the exact opposite of a knee, which is the whole
        // anatomical difference between the two limbs on this figure.
        var pole = new Vector3(left ? 0.6f : -0.6f, -0.25f, -1f);
        if (Solve(shoulder, at, SoldierSkeleton.SegLen, SoldierSkeleton.SegLen, pole,
                out LimbPose solved))
            arm = LimbPose.Lerp(arm, solved, grip);
    }

    /// <summary>
    /// Two-bone inverse kinematics: given where a limb hangs from, where its far end has to
    /// be, the two segment lengths and which way the joint between them points, solve both
    /// segments' absolute orientations.
    ///
    /// Straight trigonometry, and the only part worth explaining is the pole. A two-segment
    /// chain reaching a point has a whole circle of valid answers — the elbow can be
    /// anywhere on a ring around the line from shoulder to hand — and the pole picks which
    /// one, which is why a knee bends forward and an elbow does not. Without it the solver
    /// is free to choose, and it chooses differently from frame to frame.
    /// </summary>
    private static bool Solve(Vector3 root, Vector3 target, float l1, float l2, Vector3 pole,
        out LimbPose limb)
    {
        limb = default;

        Vector3 d = target - root;
        float dist = d.Length();
        if (dist < 1e-4f) return false;

        // Clamped just inside full extension: a chain solved dead straight has a
        // degenerate elbow, and the frame it crosses over the joint snaps.
        float max = (l1 + l2) * 0.999f;
        float min = MathF.Abs(l1 - l2) + 1e-3f;
        dist = Math.Clamp(dist, min, max);
        Vector3 u = d / d.Length();

        // Law of cosines, twice: the angle the upper segment makes with the straight line
        // to the target, and the angle the two segments make with each other.
        float ca = (l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist);
        float a = MathF.Acos(Math.Clamp(ca, -1f, 1f));

        Vector3 axis = Vector3.Cross(u, pole);
        if (axis.LengthSquared() < 1e-6f)
        {
            // The pole is along the reach — pick any perpendicular rather than dividing by
            // zero. Rare, and only in poses nothing holds for more than a frame.
            axis = Vector3.Cross(u, new Vector3(0f, 0f, 1f));
            if (axis.LengthSquared() < 1e-6f) axis = new Vector3(1f, 0f, 0f);
        }
        axis = Vector3.Normalize(axis);

        Vector3 upper = Rodrigues(u, axis, a);
        Vector3 joint = root + upper * l1;
        Vector3 lower = target - joint;
        if (lower.LengthSquared() < 1e-8f) return false;
        lower = Vector3.Normalize(lower);

        (limb.UpperPitch, limb.UpperRoll) = Aim(upper);
        (limb.LowerPitch, limb.LowerRoll) = Aim(lower);
        return true;
    }

    /// <summary>Rotates <paramref name="v"/> about a unit <paramref name="axis"/> by
    /// <paramref name="angle"/>.</summary>
    private static Vector3 Rodrigues(Vector3 v, Vector3 axis, float angle)
    {
        float c = MathF.Cos(angle), sn = MathF.Sin(angle);
        return v * c + Vector3.Cross(axis, v) * sn + axis * (Vector3.Dot(axis, v) * (1f - c));
    }

    /// <summary>
    /// The inverse of how a segment is drawn: given a direction a limb segment has to point,
    /// the pitch and roll that put it there.
    ///
    /// A segment hangs along −Y and is rolled about Z then pitched about X, which puts its
    /// far end at (sin r, −cos r · cos p, −cos r · sin p). Reading that backward is one
    /// arcsine and one arctangent, and it has to match the mesh transform exactly — a
    /// solver that disagrees with the draw by a few degrees leaves a visible gap at every
    /// joint.
    /// </summary>
    private static (float Pitch, float Roll) Aim(Vector3 dir)
    {
        float roll = MathF.Asin(Math.Clamp(dir.X, -1f, 1f));
        float cr = MathF.Cos(roll);
        if (MathF.Abs(cr) < 1e-4f) return (0f, roll);
        return (MathF.Atan2(-dir.Z, -dir.Y), roll);
    }

    /// <summary>
    /// A world point as the figure sees it: measured the short way round the wrap, turned
    /// into the body's own frame, and divided back down out of whatever size this figure is
    /// drawn at — since every measurement the solver works in is in the model's own metres.
    /// </summary>
    private static Vector3 ToModel(Vector2 at, float y, in Signals s)
    {
        Vector2 d = Torus.Delta(s.Position, at);
        float c = MathF.Cos(s.Heading), sn = MathF.Sin(s.Heading);
        float scale = MathF.Max(1e-3f, s.Scale);
        return new Vector3(
            (d.X * c - d.Y * sn) / scale,
            (y - s.Height) / scale,
            (d.X * sn + d.Y * c) / scale);
    }

    // ================================================================================
    //  Small maths
    // ================================================================================

    /// <summary>The direction a heading faces, on the plane. Matches
    /// <c>PlayerTank.Forward</c> exactly, because a body drawn facing one way and solved
    /// facing another is a body walking sideways.</summary>
    private static Vector2 Facing(float heading) => new(MathF.Sin(heading), MathF.Cos(heading));

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float MoveToward(float value, float target, float maxDelta)
    {
        if (MathF.Abs(target - value) <= maxDelta) return target;
        return value + MathF.Sign(target - value) * maxDelta;
    }

    /// <summary>An angle folded into −π..π, so a flinch from behind is measured the short
    /// way round rather than as five radians of turn.</summary>
    private static float Wrap(float a)
    {
        a %= MathF.Tau;
        if (a > MathF.PI) a -= MathF.Tau;
        if (a < -MathF.PI) a += MathF.Tau;
        return a;
    }
}
