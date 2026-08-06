using System.Numerics;
using Unrendered.Core;

namespace Unrendered.Entities;

/// <summary>
/// The FLOWER's body. Like the SOLDIER's, the FISH's and the VIRUS's rigs it replaces the
/// craft's physics outright rather than modifying them — and it replaces them with almost
/// nothing, because the whole of this chassis is the refusal to move.
///
/// <para>Everything else on the roster answers the question "where do I go" every single
/// tick. This one answers it about twice a minute, deliberately, at a price, by dying where
/// it stands and coming up somewhere else. In between it is a fixed emplacement with a
/// three-metre stalk and a head that turns the full way round, and the only travel it has
/// is <em>lean</em>: WASD bends the stem, sliding the head a couple of metres off the root
/// and back. That is the dodge. It is not much, and it is not supposed to be — what this
/// class trades mobility for is a weapon nothing else has and a harvest nothing else has,
/// and if it could also step out of the way it would simply be better than everything.</para>
///
/// <para>The rig is deliberately dumb about the world, exactly like the FISH's: it knows
/// where its own petals are and how ripe it is, and it knows nothing about enemies, walls or
/// damage. The world steers the petals (it is the only thing that knows what is worth
/// chasing), the world drops the harvest, and the world is the only thing here that ever
/// hurts anybody. What crosses back is a queue of <see cref="EntityCue"/>s, same bargain
/// every other rig strikes — which is what puts this chassis's noises on the wire instead of
/// on the speaker of whoever happened to be flying it.</para>
/// </summary>
public sealed class FlowerRig
{
    /// <summary>How high the eye sits: up at the head, which on a plant this size is well
    /// above where a tank's camera rides. The city looks different from up here and that is
    /// most of what the class feels like before it has done anything at all.</summary>
    public const float EyeHeight = 3.4f;

    /// <summary>How far off the root the head can be pushed by bending the stalk. Read by the
    /// renderer to bend the model and by the world to place the eye — the collision radius
    /// still lives at the <em>root</em>, so leaning genuinely moves the thing that gets shot
    /// at without moving the thing that occupies ground.</summary>
    /// <remarks>
    /// Raised from 2.6 on 2026-08-06. Speed alone was not the whole complaint: a dodge that
    /// arrives instantly but only travels two and a half metres still leaves the head inside
    /// the shot. Three and a half is a full craft-width of clearance, which is the difference
    /// between leaning out of the way of something and leaning while it hits you.
    /// </remarks>
    public const float MaxLean = 3.5f;

    // --- Stance -------------------------------------------------------------------
    // A replant is not a teleport with a delay on it. It is four states with different
    // rules, and the middle two are the price: for most of a second the player is a seed
    // under the plate with no gun, no petals and no view worth having.

    public enum Stance
    {
        /// <summary>Standing in soil. The only state that can shoot, throw or harvest.</summary>
        Rooted,
        /// <summary>Pulling out. The stalk curls, the head folds into the crown.</summary>
        Wilting,
        /// <summary>Under the plate, in transit. Nothing can see you and very little can
        /// hurt you — see <see cref="BuriedArmor"/>.</summary>
        Seeded,
        /// <summary>Coming up on the new ground: the shoot unrolls and the six petals
        /// unfurl one at a time. Helpless, but visible, which is the half of the cost that
        /// the player standing next to you gets to act on.</summary>
        Sprouting,
    }

    public Stance State { get; private set; } = Stance.Rooted;

    /// <summary>How far through the current stance we are, 0..1. The renderer's whole
    /// replant animation is a function of this and <see cref="State"/>, so the sim never
    /// has to know what a wilt looks like.</summary>
    public float StancePhase { get; private set; }

    private float _stanceTime;

    // Cut twice on 2026-08-06, from 1.65s end to end down to about two thirds of a second. On a
    // chassis whose *only* travel is this, the helpless middle is not a dramatic beat — it is
    // the thing standing between the player and playing, and a second and a half of it every
    // time you want to be somewhere else reads as the class being slow rather than as it being
    // costly.
    //
    // Two thirds of a second still buys the whole shape: a wilt you can see coming, a moment of
    // genuine nothing, and a sprout somebody standing there could punish. Below this the states
    // stop being legible to anyone watching and the replant turns into a blink, at which point
    // it may as well be the hyperspace this chassis is deliberately denied.
    private const float WiltTime = 0.18f;
    private const float SeedTime = 0.12f;
    private const float SproutTime = 0.36f;

    /// <summary>What a hit is multiplied by while the body is under the plate. Not immunity:
    /// a mortar dropped on the spot a flower just went into still finds it, and it should,
    /// because otherwise the replant is a dodge button with no answer.</summary>
    public const float BuriedArmor = 0.3f;

    /// <summary>True while a replant owns the body — no trigger is read, and the world holds
    /// the transform still until <see cref="TakeReplant"/> hands over the destination.</summary>
    public bool Replanting => State != Stance.Rooted;

    /// <summary>Where the seed is headed. Meaningless while <see cref="Rooted"/>.</summary>
    public Vector2 Destination { get; private set; }

    /// <summary>Set for exactly one tick, on the tick the seed should actually be moved:
    /// the world reads it, wraps the position onto the torus and clears it. The rig does not
    /// move anything itself, for the same reason the fish's rig doesn't hurt anything — it
    /// has no idea whether that ground is inside a building.</summary>
    private bool _arrival;

    /// <summary>
    /// How far away a replant can pick ground. Raised from 44 on 2026-08-06, alongside the
    /// transit: how fast this chassis gets anywhere is the distance per hop and the time per
    /// hop and the wait between hops, and shortening only the middle one of those three leaves
    /// the class exactly as slow to cross a city as it was.
    ///
    /// Genuine travel — two or three streets — while staying short enough that crossing the map
    /// is still several of them with the reserve refilling in between, which is the pacing the
    /// class is built on.
    /// </summary>
    public const float ReplantRange = 58f;

    /// <summary>
    /// What one costs out of the reserve: <b>nothing</b>. Changed 2026-08-06 — it used to drain
    /// nearly half of it.
    ///
    /// <para>The reasoning is that on this chassis the replant is not an <em>ability</em>, it is
    /// the movement. Every other craft in the game walks for free and pays only for its tricks;
    /// metering this one was metering how fast the player was allowed to get anywhere at all,
    /// which is why the class kept reading as slow however far the transit was cut. A tank does
    /// not run out of driving.</para>
    ///
    /// <para>What limits it now is the transit itself: about two thirds of a second as a seed
    /// with no gun and no petals, every single time. That is a real price paid in the one
    /// currency nobody can farm, and it self-balances in a way a bar never did — hop constantly
    /// and you spend the fight underground while the field carries on without you.</para>
    ///
    /// <para>Kept as a named zero rather than deleted so the intent is legible at every call
    /// site, and so putting a price back is one number rather than an archaeology exercise. The
    /// reserve still does something on this chassis — see <see cref="SapPerPetal"/>.</para>
    /// </summary>
    public const float ReplantCost = 0f;

    // --- The lean -----------------------------------------------------------------
    // Spring-damped rather than driven directly, because a stalk is a spring: the head
    // arrives late, overshoots the input and settles, and when the keys are let go it
    // swings back through centre before it stops. That overshoot is the entire feel of the
    // dodge, and a lerp toward the target would delete it.

    public Vector2 Lean { get; private set; }
    private Vector2 _leanVel;

    // Stiffened twice on 2026-08-06, ending here. The spring's *character* is unchanged — it
    // still arrives late, overshoots and settles, which is the whole feel of a stalk — but it
    // now does all of it in about a third of a second rather than the better part of a second.
    // At the original numbers the dodge was reliably slower than the round it was dodging,
    // which made the class's only defensive tool decorative.
    //
    // Damping is raised alongside the stiffness rather than left alone, so the damping *ratio*
    // stays where it has always been (a touch under 0.6). Raising one without the other buys
    // speed by turning the settle into a wobble, which is a different bug wearing the fix's
    // clothes — if this is retuned again, move both.
    private const float LeanStiffness = 145f;
    private const float LeanDamping = 14.2f;

    /// <summary>What the player is asking the stalk to do, as (strafe, forward) in −1..1 —
    /// the same movement vector every other body reads, pointed at a chassis that answers it
    /// with two metres instead of twenty-six a second.</summary>
    public Vector2 MoveInput;

    /// <summary>How far the head is leaning as a fraction of what the stalk allows. The HUD
    /// draws it, and the world reads it to decide how much a hit staggers the plant.</summary>
    public float LeanFraction => Lean.Length() / MaxLean;

    // --- Ripeness: the harvest clock ------------------------------------------------

    /// <summary>0..1. At one the head is ripe and Q will set seed. This is the bar the class
    /// is really played around: it is the only economy in the game that is not something you
    /// took off somebody.</summary>
    public float Ripeness { get; private set; }

    public bool Ripe => Ripeness >= 1f;

    /// <summary>How many things one harvest drops. Three, as asked, and three is also about
    /// right: enough to be worth waiting for, few enough that it is not a substitute for
    /// going and looking.</summary>
    public const int HarvestYield = 3;

    /// <summary>Seconds from empty to ripe in flat daylight.</summary>
    public const float RipenTime = 26f;

    /// <summary>
    /// What the sun is worth. A flower photosynthesises, so the clock runs on light rather
    /// than on a timer: at noon it ripens half again as fast, and at the dead of night it
    /// nearly stops. Four of the five worlds have no cycle and sit permanently at the day
    /// value, so this only ever bites on SOLUNE — where it turns the harvest into something
    /// the player has to plan an evening around, which is exactly the kind of thing that
    /// world exists to do.
    /// </summary>
    public static float RipenRateAt(float night) => 1.5f - 1.25f * Math.Clamp(night, 0f, 1f);

    /// <summary>How dark it is where this plant is standing, 0..1 — written by the world each
    /// tick from the sky, because the rig has no idea what planet it is on.</summary>
    public float Night;

    /// <summary>
    /// What fresh soil is worth. Landing a replant does not merely move the plant, it puts it
    /// in ground that has not been drained — so a fifth of the harvest comes back the moment
    /// the roots take.
    ///
    /// This is the one line that stops the two abilities being unrelated buttons: a player
    /// who keeps replanting harvests more often than one who sits still, so the class's escape
    /// and the class's economy pull in the same direction and "stay mobile" is advice rather
    /// than a contradiction in terms on a chassis that cannot move.
    /// </summary>
    public const float FreshSoilRipeness = 0.2f;

    // --- The petals ------------------------------------------------------------------

    /// <summary>
    /// Twelve. Raised from six on 2026-08-06.
    ///
    /// <para>Worth knowing what that changed, because it is more than a bigger number. At six,
    /// with a round trip of about a second and a half and a third of a second between throws,
    /// the ring genuinely ran dry under sustained fire and the class's rhythm was <em>throw,
    /// wait, catch</em>. At twelve it does not: the steady state is roughly four or five in the
    /// air at once and the rest seated, so the ring has stopped being a magazine and become a
    /// rate limit. The thing that rations the weapon now is the throw cooldown and the cost of
    /// petals you fail to catch, not the count.</para>
    ///
    /// <para>The model and the HUD both read this rather than the literal six they were drawn
    /// around, so the ring on the head and the ring on the panel followed it up on their own.</para>
    /// </summary>
    public const int PetalCount = 12;

    public enum PetalState
    {
        /// <summary>In the ring, where the player can throw it.</summary>
        Seated,
        /// <summary>Out, on the way to whatever it decided to chase.</summary>
        Outbound,
        /// <summary>Out, coming home. Still lethal — a petal on the way back cuts through
        /// whatever it crosses, which is most of why the weapon is worth its cooldown.</summary>
        Homing,
        /// <summary>Lost: shot down, or it ran its life out too far from the plant to make
        /// it back. Growing again on the slow clock.</summary>
        Regrowing,
    }

    /// <summary>
    /// One petal. Seated it is just a slot in the ring; thrown it is a small flying thing
    /// that steers, and the whole of the steering is <see cref="Steer"/> — the world hands it
    /// a point and it turns toward it as hard as it can.
    /// </summary>
    public sealed class Petal
    {
        public PetalState State = PetalState.Seated;

        /// <summary>Where it is, while it is anywhere. Plane position and height, so a petal
        /// genuinely climbs at a soldier crossing the sky and drops at a hunter on the grid.</summary>
        public Vector2 Position;
        public float Height;

        /// <summary>Which way it is going, as a unit vector plus a climb — kept separately
        /// from a speed so the turn below is a rotation and not a lerp that would bleed the
        /// thing to a halt every time it changed its mind.</summary>
        public Vector2 Direction = new(0f, 1f);
        public float Climb;

        /// <summary>Seconds it has been in the air. The outbound leg is capped by
        /// <see cref="OutTime"/>; past that it turns for home whatever it was chasing.</summary>
        public float Life;

        /// <summary>Spin about its own long axis, for the renderer. A thrown petal tumbles,
        /// and a tumbling petal is the difference between a thrown blade and a sliding
        /// polygon.</summary>
        public float Spin;

        /// <summary>Which slot in the ring this one goes back into, so a petal that comes home
        /// re-seats in the gap it left rather than wherever there happened to be room.</summary>
        public int Slot;

        /// <summary>Seconds left before a lost petal is back in the ring.</summary>
        public float Regrow;

        /// <summary>Bodies it has already bitten this flight. A boomerang crosses the same
        /// hunter twice — once out and once back — and it should score both, but it must not
        /// score the same one on four consecutive ticks while it travels through it.</summary>
        public float Refractory;

        public bool InAir => State is PetalState.Outbound or PetalState.Homing;

        /// <summary>Rotates the flight one tick's worth toward <paramref name="toward"/> — the
        /// aimbot, and it is an honest one: the turn rate is capped, so a target that jinks
        /// hard across the petal's nose genuinely makes it overshoot and come round again.
        /// Given no target it flies straight, which is what a petal thrown into empty ground
        /// does until something wanders into its lock.</summary>
        public void Steer(Vector3 toward, float dt, float turnRate)
        {
            Vector2 planar = toward.X == 0f && toward.Z == 0f
                ? Direction
                : Vector2.Normalize(new Vector2(toward.X, toward.Z));

            float want = MathF.Atan2(planar.X, planar.Y);
            float have = MathF.Atan2(Direction.X, Direction.Y);
            float turn = Math.Clamp(WrapAngle(want - have), -turnRate * dt, turnRate * dt);
            float now = have + turn;
            Direction = new Vector2(MathF.Sin(now), MathF.Cos(now));

            // The climb is chased rather than rotated: a petal's vertical envelope is small
            // and treating it as a third axis of a real 3D turn would let it loop, which
            // looks like a bug however correct it is.
            float wantClimb = Math.Clamp(toward.Y, -Speed * 0.5f, Speed * 0.5f);
            Climb += Math.Clamp(wantClimb - Climb, -ClimbRate * dt, ClimbRate * dt);
        }

        /// <summary>Flies one tick along whatever the steering last settled on.</summary>
        public void Advance(float dt)
        {
            Position = Torus.Wrap(Position + Direction * Speed * dt);
            Height = MathF.Max(0.35f, Height + Climb * dt);
            Life += dt;
            Spin += SpinRate * dt;
            if (Refractory > 0f) Refractory -= dt;
        }

        private static float WrapAngle(float a) => MathF.IEEERemainder(a, MathF.Tau);
    }

    private readonly Petal[] _petals = BuildRing();

    private static Petal[] BuildRing()
    {
        var ring = new Petal[PetalCount];
        for (int i = 0; i < PetalCount; i++) ring[i] = new Petal { Slot = i };
        return ring;
    }

    /// <summary>The ring, in slot order. The renderer walks it to draw the head (a gap is a
    /// petal that is out) and the flight (everything in the air).</summary>
    public IReadOnlyList<Petal> Petals => _petals;

    /// <summary>How fast a petal travels. Slower than a bullet on purpose: the whole weapon
    /// is a thing you watch go out and come back, and a round that arrives instantly is a
    /// round, not a boomerang.</summary>
    public const float Speed = 62f;

    /// <summary>How hard it can turn, radians a second. This is the aimbot's one number. High
    /// enough that a hunter standing still is simply dead and a hunter driving is very
    /// probably dead; low enough that a player who sidesteps across its nose at close range
    /// makes it swing wide.</summary>
    public const float TurnRate = 5.0f;

    /// <summary>How fast the flight can change its climb, units a second a second.</summary>
    public const float ClimbRate = 34f;

    /// <summary>Tumble, radians a second.</summary>
    public const float SpinRate = 15f;

    /// <summary>How long the outbound leg runs before it turns for home regardless. Paired
    /// with the speed this is the reach: about forty-five metres out.</summary>
    public const float OutTime = 0.72f;

    /// <summary>How long the whole flight may last before the petal gives up and drops. A
    /// petal that spends this long trying to get home was thrown at something that led it
    /// somewhere stupid, and losing it is the price of that.</summary>
    public const float MaxFlight = 3.4f;

    /// <summary>How close to the plant counts as caught.</summary>
    public const float CatchRadius = 2.2f;

    /// <summary>
    /// How long a lost petal takes to grow back. Shortened from seven seconds on 2026-08-06.
    ///
    /// Still the thing that makes the weapon a decision — catching is free and missing is not —
    /// but seven was long enough that a bad exchange took a player out of their own class for
    /// most of a minute, and the punishment for throwing badly should be that this throw was
    /// wasted, not that the next several are unavailable.
    /// </summary>
    public const float RegrowTime = 4.5f;

    /// <summary>
    /// Between throws. A whole ring in the air at once is a legitimate and quite alarming thing
    /// to have done; a whole ring in the air on one tick is a bug that looks like an exploit.
    ///
    /// <para>Since the ring went to twelve this is the thing actually rationing the weapon —
    /// the count no longer runs out under sustained fire, so what limits a flower's output is
    /// this number and the petals it fails to catch. Treat it as the balance knob.</para>
    /// </summary>
    public const float ThrowInterval = 0.34f;

    private float _throwCooldown;

    /// <summary>How many are in the ring right now — the number on the HUD.</summary>
    public int Seated
    {
        get
        {
            int n = 0;
            foreach (var p in _petals) if (p.State == PetalState.Seated) n++;
            return n;
        }
    }

    public bool CanThrow => State == Stance.Rooted && _throwCooldown <= 0f && Seated > 0;

    // --- Feedback the camera reads ---------------------------------------------------
    // Same three fields the fish and the soldier expose, for the same reason: the renderer
    // should read what the body is doing, not be told separately.

    /// <summary>Kick on the eye's pitch, in radians — a shot going off, a petal leaving.</summary>
    public float Recoil { get; private set; }

    /// <summary>How far the horizon is tipped, from the stalk's own lean. Small: this is a
    /// plant swaying, not a fish carving.</summary>
    public float Bank => -Lean.X * 0.06f;

    /// <summary>Broadband shake, 0..1 — a hit landing, roots tearing out, a bloom going off
    /// near enough to feel.</summary>
    public float Shake { get; private set; }

    public void Kick(float radians) => Recoil -= radians;
    public void Jolt(float amount) => Shake = MathF.Min(1f, MathF.Max(Shake, amount));

    /// <summary>Zero. The one chassis in the game for which this is a constant, and the HUD's
    /// speed readout says so — it is not that the number is small, it is that there is no
    /// number.</summary>
    public float PlanarSpeed => 0f;

    // --- The event queue -------------------------------------------------------------

    private readonly List<EntityCue> _cues = new();

    /// <summary>What the body has to say since the last drain. Drained by the world after the
    /// step, exactly as the soldier's and the fish's are, so a team-mate's petals and harvests
    /// are audible on every machine rather than only on theirs.</summary>
    public List<EntityCue> Cues => _cues;

    private void Say(Cue id, Vector2 at, float param = 0f) => _cues.Add(new EntityCue(id, at, param));

    // --- The step ---------------------------------------------------------------------

    /// <summary>
    /// One tick of being a plant. Called from <see cref="PlayerTank.Update"/> in place of the
    /// drive/jump integrators, which this chassis has no use for whatsoever.
    ///
    /// <para>Note what is <em>not</em> here: no position integration. The craft's
    /// <see cref="PlayerTank.Position"/> is the root, and the root does not move except on the
    /// one tick a replant lands — which is the world's to apply, since it is the only thing
    /// that knows whether the chosen ground has a building on it.</para>
    /// </summary>
    public void Step(float dt, PlayerTank craft)
    {
        if (Recoil != 0f)
        {
            Recoil += (0f - Recoil) * MathF.Min(1f, 9f * dt);
            if (MathF.Abs(Recoil) < 1e-4f) Recoil = 0f;
        }
        if (Shake > 0f) Shake = MathF.Max(0f, Shake - dt * 3.6f);
        if (_throwCooldown > 0f) _throwCooldown -= dt;

        craft.Height = craft.GroundHeight;   // a plant is on the ground, always, by definition

        StepStance(dt, craft);
        StepLean(dt);
        StepRipeness(dt);
        StepRegrowth(dt, craft);
    }

    /// <summary>Runs the replant's four states. Everything here is a clock; what the states
    /// actually forbid is enforced by the triggers, which refuse anything but
    /// <see cref="Stance.Rooted"/>.</summary>
    private void StepStance(float dt, PlayerTank craft)
    {
        if (State == Stance.Rooted) { StancePhase = 0f; return; }

        _stanceTime += dt;
        float span = State switch
        {
            Stance.Wilting => WiltTime,
            Stance.Seeded => SeedTime,
            _ => SproutTime,
        };
        StancePhase = Math.Clamp(_stanceTime / span, 0f, 1f);
        if (StancePhase < 1f) return;

        _stanceTime = 0f;
        switch (State)
        {
            case Stance.Wilting:
                // The roots come out. Everything in the air is cut loose with them: a petal
                // cannot chase a plant that is no longer where it was thrown from, and
                // letting it try was the first thing that went visibly wrong here.
                State = Stance.Seeded;
                foreach (var p in _petals) if (p.InAir) Lose(p);
                Say(Cue.FlowerUproot, craft.Position);
                break;

            case Stance.Seeded:
                // The one tick the world is allowed to move the root. It does the moving,
                // because it owns the question of whether that ground is standable.
                _arrival = true;
                State = Stance.Sprouting;
                break;

            default:
                State = Stance.Rooted;
                Ripeness = MathF.Min(1f, Ripeness + FreshSoilRipeness);
                Lean = Vector2.Zero;
                _leanVel = Vector2.Zero;
                Say(Cue.FlowerBloomOpen, craft.Position);
                break;
        }
    }

    /// <summary>The stalk as a spring: the input is a target offset, the head is a mass on the
    /// end of it, and the difference between those two is the whole of how the class dodges.</summary>
    private void StepLean(float dt)
    {
        Vector2 want = Vector2.Zero;
        if (State == Stance.Rooted && MoveInput.LengthSquared() > 1e-4f)
        {
            Vector2 m = MoveInput.LengthSquared() > 1f ? Vector2.Normalize(MoveInput) : MoveInput;
            want = m * MaxLean;
        }

        Vector2 accel = (want - Lean) * LeanStiffness - _leanVel * LeanDamping;
        _leanVel += accel * dt;
        Lean += _leanVel * dt;

        // Hard stop at the stalk's limit, and the velocity along the limit goes with it —
        // without that the spring keeps integrating into a wall and the head shivers.
        float len = Lean.Length();
        if (len > MaxLean)
        {
            Vector2 dir = Lean / len;
            Lean = dir * MaxLean;
            float along = Vector2.Dot(_leanVel, dir);
            if (along > 0f) _leanVel -= dir * along;
        }
    }

    private void StepRipeness(float dt)
    {
        if (State != Stance.Rooted || Ripeness >= 1f) return;
        Ripeness = MathF.Min(1f, Ripeness + dt / RipenTime * RipenRateAt(Night));
    }

    /// <summary>
    /// What a petal draws out of the reserve per second while it is growing back. This is what
    /// SAP is <em>for</em> now that the replant is free: the plant's own energy going into
    /// putting its blades back, which is a far better fit for a bar on this chassis than paying
    /// a toll to walk was.
    ///
    /// <para>Read against <c>PlayerTank.SapRegen</c> (15/s) it makes a curve rather than a
    /// cliff: two or three petals regrowing sits comfortably inside the trickle, four or five
    /// starts eating into it, and a whole ring lost at once outruns it badly and the last few
    /// crawl back at <see cref="StarvedRegrowScale"/>. Losing petals is supposed to compound,
    /// and this is where it compounds.</para>
    /// </summary>
    public const float SapPerPetal = 4.2f;

    /// <summary>How fast a petal grows with the reserve empty. Slower, never stopped — a
    /// starved plant that could <em>never</em> get its ring back would be a player with nothing
    /// to do but wait, which is not a cost, it is a punishment.</summary>
    public const float StarvedRegrowScale = 0.55f;

    /// <summary>True while the reserve is dry and the ring is coming back at the slow rate.
    /// The HUD says so — it is the only thing SAP does on this chassis, so its one failure
    /// state has to be legible.</summary>
    public bool Starved { get; private set; }

    private void StepRegrowth(float dt, PlayerTank craft)
    {
        int growing = 0;
        foreach (var p in _petals) if (p.State == PetalState.Regrowing) growing++;

        Starved = false;
        if (growing == 0) return;

        // Fed or starved is decided once, for the whole ring, before anything is spent — so
        // petals never race each other for the last drop of the reserve and finish in a
        // different order from the one they were lost in.
        bool fed = craft.Hyper > 0f;
        Starved = !fed;
        if (fed)
            craft.Hyper = MathF.Max(0f, craft.Hyper - SapPerPetal * growing * dt);

        float rate = fed ? dt : dt * StarvedRegrowScale;
        foreach (var p in _petals)
        {
            if (p.State != PetalState.Regrowing) continue;
            p.Regrow -= rate;
            if (p.Regrow > 0f) continue;
            p.State = PetalState.Seated;
            p.Life = 0f;
        }
    }

    // --- Triggers ---------------------------------------------------------------------

    /// <summary>
    /// Throws one petal along <paramref name="line"/> — the eye's full 3D look, since the head
    /// turns and cranes the whole way and a petal is aimed with it. Hands back the petal so the
    /// world can place it out past the plant's own body and start steering it.
    ///
    /// Refuses with the ring empty, off cooldown, or mid-replant. Does not spend ammo: the ring
    /// <em>is</em> the magazine, and making a petal cost a round as well would tax the same
    /// decision twice.
    /// </summary>
    public Petal? Throw(Vector3 from, Vector3 line)
    {
        if (!CanThrow) return null;

        Petal? pick = null;
        foreach (var p in _petals)
            if (p.State == PetalState.Seated) { pick = p; break; }
        if (pick == null) return null;

        Vector3 d = line.LengthSquared() > 1e-6f ? Vector3.Normalize(line) : new Vector3(0f, 0f, 1f);
        var planar = new Vector2(d.X, d.Z);
        pick.Direction = planar.LengthSquared() > 1e-6f ? Vector2.Normalize(planar) : new Vector2(0f, 1f);
        pick.Climb = d.Y * Speed;
        pick.Position = new Vector2(from.X, from.Z);
        pick.Height = MathF.Max(0.5f, from.Y);
        pick.Life = 0f;
        pick.Spin = 0f;
        pick.Refractory = 0f;
        pick.State = PetalState.Outbound;

        _throwCooldown = ThrowInterval;
        Kick(0.045f);
        Say(Cue.FlowerPetalThrow, new Vector2(from.X, from.Z));
        return pick;
    }

    /// <summary>
    /// Spends a ripe head. Returns false unripe or mid-replant — the world only drops salvage
    /// on a true. Empties the bar outright rather than subtracting a cost: a harvest is the
    /// whole crop, and a player should never be sitting on a fraction of one wondering whether
    /// it is worth pressing.
    /// </summary>
    public bool Harvest(Vector2 at)
    {
        if (State != Stance.Rooted || Ripeness < 1f) return false;
        Ripeness = 0f;
        Kick(0.03f);
        Say(Cue.FlowerHarvest, at);
        return true;
    }

    /// <summary>
    /// Starts a replant toward <paramref name="target"/>. The world has already decided the
    /// point is legal and already spent the reserve; this only commits the body to the four
    /// states. Refused if one is already running.
    /// </summary>
    public bool BeginReplant(Vector2 target, Vector2 at)
    {
        if (State != Stance.Rooted) return false;
        Destination = target;
        State = Stance.Wilting;
        _stanceTime = 0f;
        StancePhase = 0f;
        Jolt(0.35f);
        Say(Cue.FlowerWilt, at);
        return true;
    }

    /// <summary>
    /// True on the single tick the root should actually be picked up and put down, consuming
    /// the flag. The world calls this every tick and moves the craft when it answers.
    /// </summary>
    public bool TakeReplant(out Vector2 to)
    {
        to = Destination;
        if (!_arrival) return false;
        _arrival = false;
        return true;
    }

    /// <summary>A petal that will not be coming home: cut loose, and on the slow clock.
    /// Public because the world loses them too — a petal that flies into a tower is gone the
    /// same way one that outlives its flight is.</summary>
    public void Lose(Petal p)
    {
        p.State = PetalState.Regrowing;
        p.Regrow = RegrowTime;
        p.Climb = 0f;
    }

    /// <summary>Catches a petal back into its slot. The one free part of the weapon, and the
    /// reason the class rewards throwing at things that are coming toward you.</summary>
    public void Catch(Petal p, Vector2 at)
    {
        p.State = PetalState.Seated;
        p.Life = 0f;
        p.Climb = 0f;
        Say(Cue.FlowerPetalCatch, at);
    }

    /// <summary>Turns a petal for home — what the world does once it has run its outbound leg
    /// or bitten something worth turning for.</summary>
    public static void SendHome(Petal p)
    {
        if (p.State == PetalState.Outbound) p.State = PetalState.Homing;
    }

    /// <summary>
    /// Blows the ring off. What a death does, so a killed flower visibly comes apart into the
    /// thing it was made of rather than simply stopping — and what a revive undoes, since a
    /// rebuilt craft gets its petals back with its hull.
    /// </summary>
    public void Shed()
    {
        foreach (var p in _petals)
        {
            p.State = PetalState.Regrowing;
            p.Regrow = RegrowTime;
        }
    }

    /// <summary>
    /// Straightens the stalk and nothing else. What a cinematic — or the tick a replant
    /// surfaces — needs: a plant that has just been put down should not be handed back a bend
    /// it had before it was picked up, but nor should it be handed back its petals.
    /// </summary>
    public void ClearLean()
    {
        Lean = Vector2.Zero;
        _leanVel = Vector2.Zero;
    }

    /// <summary>Puts the whole plant back: every petal in the ring, no lean, rooted. What a
    /// comeback rebuilds, alongside the hull and the charges.</summary>
    public void Restore()
    {
        foreach (var p in _petals)
        {
            p.State = PetalState.Seated;
            p.Regrow = 0f;
            p.Life = 0f;
        }
        State = Stance.Rooted;
        StancePhase = 0f;
        _stanceTime = 0f;
        Lean = Vector2.Zero;
        _leanVel = Vector2.Zero;
    }
}
