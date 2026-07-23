using System.Numerics;
using VoidTanks.Core;
using VoidTanks.World;

namespace VoidTanks.Entities;

/// <summary>Where one hook is in its cycle.</summary>
public enum HookState
{
    /// <summary>Seated in the launcher. Nothing is drawn but the grip.</summary>
    Stowed,

    /// <summary>In the air, cable paying out behind it, on its way to something.</summary>
    Flying,

    /// <summary>Bitten in. The cable is a constraint and the rig can reel on it.</summary>
    Anchored,

    /// <summary>Coming home — a miss, a release, or an anchor that let go.</summary>
    Returning,
}

/// <summary>
/// One of the two steel hooks. Pure state: where its tip is, what it has hold of, and
/// the single-frame flags the world reads to sound the cue that matches. It never
/// touches the world itself — <see cref="SoldierRig"/> moves it and
/// <see cref="World.World"/> decides what it was aimed at.
/// </summary>
public sealed class GrappleHook
{
    /// <summary>Right hip (E) or left hip (Q). Fixed at construction — it decides which
    /// side of the view the cable leaves from and which HUD indicator it drives.</summary>
    public readonly bool IsRight;

    public GrappleHook(bool right) => IsRight = right;

    public HookState State { get; internal set; }

    /// <summary>The tip, in absolute (unwrapped-around-the-player) coordinates while
    /// flying, and in canonical torus coordinates once it has bitten.</summary>
    public Vector2 Tip;
    public float TipY;

    /// <summary>Flight direction, normalised. Held so a flying hook keeps its line even
    /// as the player who loosed it swings away underneath.</summary>
    public Vector3 Dir = new(0f, 0f, 1f);

    /// <summary>Metres of cable paid out so far, and how many there are to pay: the
    /// distance to whatever the crosshair was resting on, or the rig's whole reach on a
    /// shot at nothing.</summary>
    public float Flown;
    public float Reach;

    /// <summary>False when this shot was aimed at empty sky — it flies its full reach and
    /// comes back rather than biting.</summary>
    public bool Bites;

    /// <summary>The building it has hold of, or null. Watched every tick: a structure that
    /// is coming down stops being something to hang from, mid-swing or not.</summary>
    public Structure? Holding;

    /// <summary>The live constraint length. Reeling shortens it; paying out lets it go.</summary>
    public float Length;

    /// <summary>Seconds this anchor has spent under load. Weak material only holds for
    /// <see cref="SoldierRig.TearTime"/> of it.</summary>
    public float Load;

    /// <summary>True the tick the cable is actually taut and taking weight — what the HUD
    /// indicator lights and what ages <see cref="Load"/>.</summary>
    public bool Taut;

    /// <summary>How hard it is pulling, 0..1. Drives the whitening near the anchor and
    /// the hum under load.</summary>
    public float Tension;

    // --- Single-frame flags, cleared at the top of every step -------------------
    public bool JustBit;
    public bool JustMissed;
    public bool JustReleased;
    public bool JustTore;

    public bool Anchored => State == HookState.Anchored;
    public bool Out => State is HookState.Flying or HookState.Anchored;

    internal void ClearEvents()
    {
        JustBit = JustMissed = JustReleased = JustTore = false;
    }

    internal void Stow()
    {
        State = HookState.Stowed;
        Holding = null;
        Load = 0f;
        Taut = false;
        Tension = 0f;
    }
}

/// <summary>
/// The SOLDIER's twin cable launcher, and with it the whole of how that chassis moves.
///
/// Every other class in this game drives: a heading, a throttle, and a heavy machine a
/// half-second behind your intent. This one does not. It is a person with two steel
/// hooks on their hips, and the entire loop is converting height into speed and speed
/// back into height — fire a hook at a building, swing past it rather than into it,
/// fire the second before the first arc dies, and never touch the ground again.
///
/// So the rig owns a genuine velocity vector rather than a speed and a facing, and every
/// change of direction in it comes from one of exactly three places: gravity, a gas
/// burst, or a cable that has run out of slack. Nothing here snaps, nothing teleports,
/// and nothing is scripted — which is what makes a clean swing feel earned rather than
/// played back.
///
/// The cables are hard constraints, not springs. A spring is easier to write and feels
/// like being pulled through treacle; a rope that simply <em>catches</em> you at its
/// length and swings you is what the whole class is for.
///
/// Pure state and physics — no Raylib, no world queries. The world decides what a hook
/// was aimed at (see <c>World.TryFindAnchor</c>) and hands the answer in, so the rig
/// itself stays testable without a screen or a city.
/// </summary>
public sealed class SoldierRig
{
    // --- On foot ----------------------------------------------------------------
    // Deliberately slow and heavy. Being on the ground is meant to read to the player
    // as the wrong state to be in: the vulnerable, slow, walking-target state that
    // leaving costs one press of ENTER to escape.

    public const float GroundSpeed = 6f;
    private const float GroundAccel = 44f;
    private const float GroundFriction = 34f;

    /// <summary>Eye height of a person on foot, against the tank's 3.2. The single
    /// number that makes the same city read as something you are small inside.</summary>
    public const float EyeHeight = 1.7f;

    // --- Gravity ----------------------------------------------------------------
    // Three different pulls, which is a lie the player never notices and would miss
    // immediately if it were removed. The rise is heavy so the leap decelerates
    // crisply, the apex is nearly weightless so there is a beat to aim a cable in, and
    // the fall is honest 9.8 so height converts into speed at the rate it should.

    public const float Gravity = 9.8f;
    private const float RiseGravity = 20.8f;
    private const float HangGravity = 5.2f;
    private const float HangBand = 3.2f;      // |vertical speed| inside which the hang applies

    // --- The high jump (ENTER) --------------------------------------------------
    // 25 m/s against the rise pull is a 15-metre apex reached in 1.2 seconds. Both
    // numbers are load-bearing for the feel: much lower and a jump doesn't clear
    // anything worth clearing, much slower and the opener stops being an opener.

    public const float JumpVelocity = 25f;
    public const float JumpGasCost = 22f;

    /// <summary>How high a jump carries, solved from the kick and the rise pull rather
    /// than guessed, so retuning either moves the number with it.</summary>
    public static float JumpApex => JumpVelocity * JumpVelocity / (2f * RiseGravity);

    // --- The cables -------------------------------------------------------------

    /// <summary>How far a hook can be thrown. Past this the shot is a miss whatever it
    /// was aimed at.</summary>
    public const float MaxRange = 78f;

    /// <summary>How fast the cable pays out. Fast enough not to be a wait, slow enough
    /// that a hook thrown across a gap is visibly travelling.</summary>
    public const float CableSpeed = 90f;

    /// <summary>And how fast it zips back on a miss or a release — quicker than it went
    /// out, because a returning cable is dead time.</summary>
    private const float RetractSpeed = 150f;

    /// <summary>Metres per second the reel takes in, and how hard the gas jet pushes
    /// toward the anchor while it does. The thrust is what makes reeling an
    /// acceleration rather than a winch dragging you along a line.</summary>
    private const float ReelRate = 15f;
    private const float ReelThrust = 26f;
    private const float PayOutRate = 13f;

    /// <summary>Gas per second burned while reeling. A full reserve buys about six
    /// seconds of continuous pull — enough for a long chain, not enough to hold a hover.</summary>
    private const float ReelGasRate = 17f;

    /// <summary>The shortest a cable can be reeled to. Stops the player winching
    /// themselves into the face of an anchor and sticking there.</summary>
    private const float MinLength = 3.5f;

    /// <summary>How hard A / D bias tension across the two cables — one shortens as the
    /// other lengthens, which is what curves the arc rather than merely steering it.</summary>
    private const float BiasRate = 11f;

    /// <summary>What a taut cable gives back when it catches you. Just under one: a
    /// pendulum that returned everything would swing forever, and one that returned
    /// nothing would feel like hitting a wall.</summary>
    private const float CableRestitution = 0.02f;

    /// <summary>Constraint passes per step. Two cables pulling on one body is a system,
    /// not two independent ropes, so it takes a few sweeps to settle — at one, a player
    /// hanging between two anchors visibly jitters between them.</summary>
    private const int ConstraintPasses = 4;

    // --- Weak anchors -----------------------------------------------------------

    /// <summary>Below this scale a building is thin enough that a person swinging on it
    /// tears the anchor out. Towers are rolled from 0.6, so the smallest quarter of the
    /// skyline is genuinely untrustworthy — which is what makes reading the city at a
    /// glance a skill rather than a formality.</summary>
    public const float WeakScale = 0.82f;

    /// <summary>How long weak material holds before it goes.</summary>
    public const float TearTime = 1.15f;

    // --- Air control ------------------------------------------------------------

    /// <summary>How hard WASD pushes while off the ground. Enough to steer an arc,
    /// nowhere near enough to fly: air control that can build speed on its own would
    /// make every cable optional.</summary>
    private const float AirAccel = 11f;

    /// <summary>The speed air steering alone will not push you past. Swinging can carry
    /// you well over it and keep it — this only stops the air from being an engine.</summary>
    private const float AirSteerCap = 11f;

    /// <summary>Hard ceiling on the whole velocity vector. A long reeled dive can
    /// otherwise stack cable pull on top of gravity indefinitely.</summary>
    private const float MaxSpeed = 38f;

    // --- Landings ---------------------------------------------------------------

    /// <summary>Coming down faster than this staggers the camera and drops the soldier
    /// to a knee. Roughly a twelve-metre drop.</summary>
    public const float HardLanding = 15f;

    /// <summary>And faster than this hurts — about the twenty metres the spec calls for
    /// (v = sqrt(2gh) with h = 20 is 19.8).</summary>
    public const float FallDamageSpeed = 19.8f;

    /// <summary>Planar speed above which meeting a wall is a crash rather than a scrape:
    /// the tumble, the total loss of momentum, the camera thrown.</summary>
    public const float CrashSpeed = 13f;

    // --- Live state -------------------------------------------------------------

    public readonly GrappleHook Left = new(right: false);
    public readonly GrappleHook Right = new(right: true);

    /// <summary>Full 3D momentum: X and Z on the grid plane, Y up. This, not a heading
    /// and a throttle, is what the class actually is.</summary>
    public Vector3 Velocity;

    /// <summary>This step's WASD, as (strafe, forward) each in -1..1, already resolved
    /// from the keys by the world. Interpreted against the look direction on foot and in
    /// the air, and as cable tension whenever a hook is anchored.</summary>
    public Vector2 MoveInput;

    /// <summary>The camera roll, in radians — the bank into an arc. Eased rather than
    /// snapped, because the single most important thing about how a swing feels is that
    /// the horizon tips <em>over</em> rather than jumping.</summary>
    public float Bank { get; private set; }

    /// <summary>Seconds of stagger left after a hard landing or a crash. The camera
    /// reads it; so does the world, which refuses to let a staggered soldier jump.</summary>
    public float Stagger { get; private set; }

    /// <summary>True while both feet are on the grid — the slow, wrong state.</summary>
    public bool Grounded { get; private set; } = true;

    /// <summary>True while the reel is actually pulling, which is what the gas jet's
    /// roar is keyed to.</summary>
    public bool Reeling { get; private set; }

    /// <summary>How starved the reserve is, 0 (full pressure) .. 1 (running on fumes).
    /// The reel goes sluggish and the jump whoosh thins as it rises — the spec's
    /// "running low is felt, not read".</summary>
    public float Starvation { get; private set; }

    // Single-frame events the world drains each step.
    public bool JustJumped { get; private set; }
    public bool JustLanded { get; private set; }

    /// <summary>Impact speed of the landing that just happened, in m/s. Zero unless
    /// <see cref="JustLanded"/> is set this step.</summary>
    public float LandingSpeed { get; private set; }

    /// <summary>Set on the tick a swing ends in a wall. The world spends it on damage,
    /// dust and the tumble.</summary>
    public bool JustCrashed { get; private set; }

    // --- What the camera does about all this ------------------------------------
    // Three separate channels, because they are three separate physical things and
    // folding them into one "shake" number makes every event feel like the same event.
    // The rig owns them rather than the renderer because they decay on the sim's fixed
    // clock: a frame-rate-dependent recoil is a recoil that means something different on
    // every machine.

    /// <summary>A small upward nudge on the view, in radians, from the rifle's recoil.
    /// Deliberately applied to the <em>camera</em> and not to the aim: the shot has
    /// already left, and pushing the player's actual crosshair off target sixty times a
    /// second would make the weapon unusable mid-swing, which is the one thing it has to
    /// be.</summary>
    public float Recoil { get; private set; }

    /// <summary>How far the eye has dropped into a landing, in metres. A hard arrival
    /// buckles the knees and the view goes with them, then springs back.</summary>
    public float Dip { get; private set; }

    /// <summary>Broadband shake, 0..1 — a rocket going off nearby, a crash, a hard
    /// landing. Rings down on its own.</summary>
    public float Shake { get; private set; }

    /// <summary>How much muzzle flash is left, 0..1. Set by a shot and gone within a
    /// couple of frames — a flash that lasts long enough to look at is not a flash.</summary>
    public float Flash { get; private set; }

    /// <summary>Which launcher the last shot came out of. The rifle is fired from
    /// alternating hands as the arms swap the weapon over, so the flash and the brass
    /// have a side to belong to.</summary>
    public bool FlashOnRight { get; private set; } = true;

    /// <summary>Kicks the view up by <paramref name="radians"/> — one rifle round — and
    /// lights the muzzle it came out of.</summary>
    public void Kick(float radians)
    {
        Recoil = MathF.Min(0.09f, Recoil + radians);
        Flash = 1f;
        FlashOnRight = !FlashOnRight;
    }

    /// <summary>Throws the whole view about by <paramref name="amount"/> (0..1) — a
    /// blast, a crash, a mass landing. Takes the larger rather than summing, so a
    /// cluster of hits reads as one hard event instead of building past the ceiling.</summary>
    public void Jolt(float amount) => Shake = MathF.Min(1f, MathF.Max(Shake, amount));

    /// <summary>Planar speed, which is what the wind, the FOV stretch and the vignette
    /// all key off — a dead-vertical drop should not roar.</summary>
    public float PlanarSpeed => new Vector2(Velocity.X, Velocity.Z).Length();

    public float Speed => Velocity.Length();

    public bool AnyAnchored => Left.Anchored || Right.Anchored;
    public bool BothAnchored => Left.Anchored && Right.Anchored;

    /// <summary>The hook on the given hip, so callers can speak in E and Q rather than
    /// in fields.</summary>
    public GrappleHook Hook(bool right) => right ? Right : Left;

    /// <summary>
    /// Throws a hook. <paramref name="anchor"/> is where the world's raycast says the
    /// crosshair was resting — null for a shot at open sky, which flies the rig's full
    /// reach and comes back with nothing. The cable pays out over the flight either way,
    /// so a miss costs the same second of travel a hit does.
    /// </summary>
    public void FireHook(bool right, Vector2 fromXZ, float fromY, Vector3 dir,
        Vector3? anchor = null, Structure? holding = null)
    {
        GrappleHook h = Hook(right);
        if (h.Out) return;   // already committed — E and Q release rather than re-fire

        h.State = HookState.Flying;
        h.Tip = fromXZ;
        h.TipY = fromY;
        h.Dir = Vector3.Normalize(dir);
        h.Flown = 0f;
        h.Holding = holding;
        h.Load = 0f;
        h.Taut = false;
        h.Tension = 0f;

        if (anchor is { } at)
        {
            h.Bites = true;
            // Measured in the flight's own frame: the world hands back an absolute point
            // near the player, so this is a straight distance and not a wrapped one.
            h.Reach = MathF.Min(MaxRange,
                Vector3.Distance(new Vector3(fromXZ.X, fromY, fromXZ.Y), at));
        }
        else
        {
            h.Bites = false;
            h.Reach = MaxRange;
        }
    }

    /// <summary>
    /// Lets a hook go. An anchored one drops its constraint on the spot — which is the
    /// whole of the slingshot: nothing is applied to the player, the rope simply stops
    /// being there, and whatever the arc had built stays built.
    /// </summary>
    public void ReleaseHook(bool right)
    {
        GrappleHook h = Hook(right);
        if (!h.Out) return;
        h.State = HookState.Returning;
        h.Holding = null;
        h.Taut = false;
        h.Tension = 0f;
        h.Load = 0f;
        h.JustReleased = true;
    }

    /// <summary>Drops both cables — the double release at the bottom of an arc, and what
    /// a cinematic or a death does to the rig on the way past.</summary>
    public void ReleaseBoth()
    {
        ReleaseHook(true);
        ReleaseHook(false);
    }

    /// <summary>
    /// The high jump: a downward gas burst off the hips. Refused mid-air, refused while
    /// staggered, and refused on an empty reserve — the one press that gets a soldier
    /// off the ground is also the one that most reliably tells them the tank is dry.
    /// </summary>
    public bool Jump(PlayerTank p)
    {
        if (!Grounded || Stagger > 0f || p.Hyper < JumpGasCost) return false;

        Velocity.Y = JumpVelocity;
        p.Hyper -= JumpGasCost;
        Grounded = false;
        JustJumped = true;
        return true;
    }

    /// <summary>
    /// One fixed step of the whole chassis: hooks, then intent, then constraints, then
    /// integration. Writes the player's position, height and momentum directly — a
    /// soldier has no heading of their own to drive, since the mouse owns where they
    /// look and the cables own where they go.
    /// </summary>
    public void Step(float dt, PlayerTank p)
    {
        JustJumped = false;
        JustLanded = false;
        JustCrashed = false;
        LandingSpeed = 0f;
        Left.ClearEvents();
        Right.ClearEvents();

        if (Stagger > 0f) Stagger = MathF.Max(0f, Stagger - dt);

        StepHook(Left, dt, p);
        StepHook(Right, dt, p);

        Reeling = false;
        ApplyIntent(dt, p);
        ApplyGravity(dt);

        for (int i = 0; i < ConstraintPasses; i++)
        {
            SolveCable(Left, p, dt, i == 0);
            SolveCable(Right, p, dt, i == 0);
        }

        Integrate(dt, p);
        UpdateBank(dt, p);
        DecayViewFeedback(dt);

        Starvation = 1f - Math.Clamp(p.Hyper / 30f, 0f, 1f);
    }

    /// <summary>
    /// Rings the three camera channels down. Each at its own rate and for its own
    /// reason: recoil settles fast because the next round is a tenth of a second away,
    /// shake decays exponentially because that is what a physical ring-down does, and the
    /// landing dip springs back rather than fading, since a knee straightening is a
    /// spring and the eye should come back up past level and settle.
    /// </summary>
    private void DecayViewFeedback(float dt)
    {
        Recoil = MathF.Max(0f, Recoil - RecoilRecovery * dt);
        Flash = MathF.Max(0f, Flash - dt / FlashTime);
        Shake *= MathF.Exp(-ShakeDecay * dt);
        if (Shake < 0.002f) Shake = 0f;

        // Critically-damped-ish spring back to standing.
        _dipVel += (-Dip * DipStiffness - _dipVel * DipDamping) * dt;
        Dip += _dipVel * dt;
        if (MathF.Abs(Dip) < 0.001f && MathF.Abs(_dipVel) < 0.01f) { Dip = 0f; _dipVel = 0f; }
    }

    private float _dipVel;

    private const float RecoilRecovery = 0.55f;   // radians a second
    private const float FlashTime = 0.05f;        // three frames at sixty
    private const float ShakeDecay = 6.5f;
    private const float DipStiffness = 90f;
    private const float DipDamping = 13f;

    /// <summary>
    /// Registers a crash into the skyline. Called by the world's collision pass, which
    /// is the only thing that knows a wall was there: a swing that meets one loses
    /// everything it had built and the camera is thrown, while a walk into the same wall
    /// is just a scrape. Momentum is the price, and it is the only price that matters to
    /// this class.
    /// </summary>
    public bool RegisterWallHit()
    {
        float speed = PlanarSpeed;
        if (speed < CrashSpeed)
        {
            // A scrape. Kill the component going into the wall by simply damping the
            // planar carry, and leave the player driving.
            Velocity.X *= 0.5f;
            Velocity.Z *= 0.5f;
            return false;
        }

        Velocity = new Vector3(0f, MathF.Min(Velocity.Y, 0f), 0f);
        Stagger = 0.85f;
        Jolt(MathF.Min(1f, speed / 26f));
        _dipVel -= 1.4f;
        JustCrashed = true;
        return true;
    }

    /// <summary>Tears an anchor loose from outside — what the world calls when the thing
    /// a hook is holding is cut down under it.</summary>
    public void TearAnchor(GrappleHook h)
    {
        if (!h.Anchored) return;
        h.State = HookState.Returning;
        h.Holding = null;
        h.Taut = false;
        h.Tension = 0f;
        h.Load = 0f;
        h.JustTore = true;
    }

    // --- Steps ------------------------------------------------------------------

    /// <summary>
    /// Advances one hook: a flying one pays out cable until it arrives (and bites, or
    /// finds nothing and turns round), a returning one zips home, an anchored one
    /// watches what it is holding for the building coming down under it.
    /// </summary>
    private void StepHook(GrappleHook h, float dt, PlayerTank p)
    {
        switch (h.State)
        {
            case HookState.Flying:
            {
                float step = CableSpeed * dt;
                h.Flown += step;
                h.Tip += new Vector2(h.Dir.X, h.Dir.Z) * step;
                h.TipY += h.Dir.Y * step;

                if (h.Flown < h.Reach) return;

                if (!h.Bites)
                {
                    h.State = HookState.Returning;
                    h.JustMissed = true;
                    return;
                }

                // Bitten. The tip is folded into canonical coordinates here and stays
                // there: an anchor is a place on the torus, not a place near the player,
                // and the player is about to swing a long way from it.
                h.Tip = Torus.Wrap(h.Tip);
                h.State = HookState.Anchored;
                h.Length = MathF.Max(MinLength, DistanceTo(h, p));
                h.JustBit = true;
                return;
            }

            case HookState.Returning:
            {
                h.Flown -= RetractSpeed * dt;
                if (h.Flown <= 0f) h.Stow();
                return;
            }

            case HookState.Anchored:
            {
                // Whatever it bit is on its way down — a rocket took the building out
                // from under the player, and a falling mass is not something to hang
                // from. Also covers a structure the field has already dropped.
                if (h.Holding is { Falling: true })
                {
                    TearAnchor(h);
                    return;
                }

                // Weak material gives way under sustained load. Only while genuinely
                // taut: a slack cable on a thin spire is not pulling on anything.
                if (h.Taut && h.Holding is { } s && s.Scale < WeakScale)
                {
                    h.Load += dt;
                    if (h.Load >= TearTime) TearAnchor(h);
                }
                else if (!h.Taut)
                {
                    h.Load = MathF.Max(0f, h.Load - dt * 0.5f);
                }
                return;
            }
        }
    }

    /// <summary>
    /// Turns this step's WASD into force. Which force depends entirely on what the rig
    /// is hanging from: on foot it is walking, in free air it is a weak steer, and on a
    /// cable it stops being movement at all and becomes tension — W reels in, S pays
    /// out, A and D bias the pull between the two lines and curve the arc.
    /// </summary>
    private void ApplyIntent(float dt, PlayerTank p)
    {
        Vector2 fwd = p.Forward;                       // (sin h, cos h)
        var right = new Vector2(-fwd.Y, fwd.X);        // screen-right on the plane
        Vector2 wish = right * MoveInput.X + fwd * MoveInput.Y;

        if (AnyAnchored)
        {
            ApplyCableTension(dt, p, wish);
            return;
        }

        if (Grounded)
        {
            // Weighted and deliberate. The wish speed is low and the friction is high,
            // so a soldier leans into a turn and never darts.
            Vector2 target = wish.LengthSquared() > 1e-4f
                ? Vector2.Normalize(wish) * GroundSpeed * MathF.Min(1f, wish.Length())
                : Vector2.Zero;

            var planar = new Vector2(Velocity.X, Velocity.Z);
            float rate = target.LengthSquared() > 1e-4f ? GroundAccel : GroundFriction;
            planar = MoveToward(planar, target, rate * dt);
            Velocity.X = planar.X;
            Velocity.Z = planar.Y;
            return;
        }

        AirSteer(dt, wish);
    }

    /// <summary>
    /// Free-air steering: a shove in the wished direction that can bend a fall but can
    /// never build speed on its own. Anything already travelling faster than the steer
    /// cap only gets to change direction, which is the rule that keeps the cables the
    /// only real engine on this chassis.
    /// </summary>
    private void AirSteer(float dt, Vector2 wish)
    {
        if (wish.LengthSquared() < 1e-4f) return;

        var planar = new Vector2(Velocity.X, Velocity.Z);
        float before = planar.Length();
        planar += Vector2.Normalize(wish) * AirAccel * dt;

        float after = planar.Length();
        float ceiling = MathF.Max(AirSteerCap, before);
        if (after > ceiling) planar *= ceiling / after;

        Velocity.X = planar.X;
        Velocity.Z = planar.Y;
    }

    /// <summary>
    /// WASD as tension. This is the signature state of the class and the reason both
    /// hooks exist: hanging between two anchors, W and S trade height for length while A
    /// and D bias the pull to one side and bend the whole arc that way.
    ///
    /// The reel is a genuine thrust as well as a shortening, because a winch alone drags
    /// the player along the line like a lift, and what the class wants is an
    /// acceleration you can then <em>keep</em> by letting go at the right moment.
    /// </summary>
    private void ApplyCableTension(float dt, PlayerTank p, Vector2 wish)
    {
        float reel = MoveInput.Y;
        float bias = MoveInput.X;

        // A dry tank reels weakly rather than not at all — the player should feel the
        // pull go sluggish and understand it before they are dropped by it.
        float gas = p.Hyper > 0f ? 1f : 0f;
        float power = gas * (0.35f + 0.65f * Math.Clamp(p.Hyper / 30f, 0f, 1f));

        if (reel > 0f && p.Hyper > 0f)
        {
            Reeling = true;
            p.Hyper = MathF.Max(0f, p.Hyper - ReelGasRate * reel * dt);
            if (Left.Anchored) Reel(Left, p, reel, power, dt);
            if (Right.Anchored) Reel(Right, p, reel, power, dt);
        }
        else if (reel < 0f)
        {
            // Slack: the arc drops and widens. Free — paying out costs no gas, which is
            // what makes a long lazy pendulum the cheap way to cross a gap.
            if (Left.Anchored)
                Left.Length = MathF.Min(MaxRange, Left.Length - PayOutRate * reel * dt);
            if (Right.Anchored)
                Right.Length = MathF.Min(MaxRange, Right.Length - PayOutRate * reel * dt);
        }

        if (bias != 0f)
        {
            if (BothAnchored)
            {
                // One cable in, the other out. Shortening the left line pulls the body
                // toward it, which is a turn rather than a strafe — the arc curves.
                GrappleHook tighten = bias < 0f ? Left : Right;
                GrappleHook slacken = bias < 0f ? Right : Left;
                float amount = BiasRate * MathF.Abs(bias) * dt;
                tighten.Length = MathF.Max(MinLength, tighten.Length - amount);
                slacken.Length = MathF.Min(MaxRange, slacken.Length + amount);
            }
            else
            {
                // A single line has nothing to bias against, so A and D go on steering
                // the body laterally against the pull — the spec's "WASD steering
                // laterally" on a one-hook reel-in.
                Vector2 fwd = p.Forward;
                AirSteer(dt, new Vector2(-fwd.Y, fwd.X) * bias);
            }
        }
    }

    private void ApplyGravity(float dt)
    {
        if (Grounded && Velocity.Y <= 0f) return;

        // Three pulls, and which one applies is decided by where in the arc the body is.
        //
        // The rise is heavy the whole way up, which is what puts the apex at exactly the
        // 1.2 seconds the jump is specified to take. The hang is on the way back *down*
        // — a band just under the top where the pull eases off before real gravity takes
        // over, so there is a beat at the peak to read the city and pick an anchor out
        // of it. Applying the hang on the way up as well would be the same amount of
        // float and would push the apex most of half a second late.
        float g = Velocity.Y > 0f
            ? RiseGravity
            : (Velocity.Y > -HangBand ? HangGravity : Gravity);

        Velocity.Y -= g * dt;
    }

    /// <summary>
    /// The cable as a hard constraint. Inside its length it is a slack rope and does
    /// nothing at all; at its length it catches the player and takes away exactly the
    /// part of their momentum that was trying to travel further from the anchor —
    /// leaving everything sideways untouched, which is the entire pendulum.
    /// </summary>
    private void SolveCable(GrappleHook h, PlayerTank p, float dt, bool firstPass)
    {
        if (!h.Anchored) return;
        if (firstPass) { h.Taut = false; h.Tension *= 0.6f; }

        Vector3 to = ToAnchor(h, p);
        float dist = to.Length();
        if (dist <= h.Length || dist < 1e-4f) return;

        Vector3 n = to / dist;                     // unit vector toward the anchor

        // Pull the body back onto the sphere. Positional, not a force: a rope of a given
        // length simply does not permit being further away than that.
        float over = dist - h.Length;
        p.Position = Torus.Wrap(p.Position + new Vector2(n.X, n.Z) * over);
        p.Height += n.Y * over;
        if (p.Height < 0f) p.Height = 0f;

        // And take back the outward momentum, keeping the tangential part whole.
        float outward = -Vector3.Dot(Velocity, n);
        if (outward > 0f)
        {
            Velocity += n * (outward * (1f + CableRestitution));
            h.Tension = MathF.Max(h.Tension, Math.Clamp(outward / 18f, 0f, 1f));
        }

        h.Taut = true;
        if (h.Tension < 0.12f) h.Tension = 0.12f;   // hanging still is still hanging
    }

    private void Integrate(float dt, PlayerTank p)
    {
        float speed = Velocity.Length();
        if (speed > MaxSpeed) Velocity *= MaxSpeed / speed;

        p.Position = Torus.Wrap(p.Position + new Vector2(Velocity.X, Velocity.Z) * dt);
        p.Height += Velocity.Y * dt;

        if (p.Height > 0f)
        {
            Grounded = false;
            return;
        }

        // Down. How hard decides whether this was a landing, a stagger or a fall that
        // costs shield — the world reads LandingSpeed and bills accordingly.
        p.Height = 0f;
        if (!Grounded)
        {
            JustLanded = true;
            LandingSpeed = -Velocity.Y;

            // The knees taking it. Even a gentle arrival buckles a little — the spec's
            // "hard vertical shake on landing" — and a bad one drops the eye most of a
            // metre and throws the whole view with it.
            float force = Math.Clamp(LandingSpeed / FallDamageSpeed, 0f, 1.4f);
            _dipVel -= 2.2f * force;
            if (LandingSpeed > HardLanding)
            {
                Stagger = MathF.Max(Stagger, 0.6f);
                Jolt(0.35f + 0.5f * force);
            }
        }
        Grounded = true;
        if (Velocity.Y < 0f) Velocity.Y = 0f;

        // A landing scrubs most of the carry: a person hitting the ground at thirty
        // metres a second does not skid off down the street at thirty metres a second.
        if (LandingSpeed > HardLanding)
        {
            Velocity.X *= 0.25f;
            Velocity.Z *= 0.25f;
        }
    }

    /// <summary>
    /// Eases the camera roll toward the bank the current arc earns. Taken from how much
    /// of the travel is sideways relative to where the player is looking, so facing
    /// backward mid-swing banks the view the other way — which is correct, and is the
    /// whole reason look and travel are decoupled on this chassis.
    /// </summary>
    private void UpdateBank(float dt, PlayerTank p)
    {
        float target = 0f;

        if (AnyAnchored && !Grounded)
        {
            Vector2 fwd = p.Forward;
            var right = new Vector2(-fwd.Y, fwd.X);
            float lateral = Vector2.Dot(new Vector2(Velocity.X, Velocity.Z), right);
            // Full bank at 22 m/s of sideways travel — a hard, committed arc.
            target = -Math.Clamp(lateral / 22f, -1f, 1f) * MaxBank;
        }

        // Eased at a fixed rate rather than lerped, so the horizon rolls at a readable
        // speed instead of snapping over on the frame a cable catches.
        Bank = MoveToward(Bank, target, BankRate * dt);
    }

    /// <summary>How far the horizon tips at a full committed arc — 25 degrees, banking
    /// like an aircraft. The single most important feel element in the class.</summary>
    public const float MaxBank = 0.436f;

    private const float BankRate = 1.5f;   // radians a second

    // --- Geometry ---------------------------------------------------------------

    /// <summary>
    /// The vector from the player to an anchor, measured the short way round the torus
    /// on the plane and straight up the Y axis. Everything the constraint does is built
    /// on this, so a swing over the world's seam behaves exactly as one in the middle
    /// of the map does.
    /// </summary>
    private static Vector3 ToAnchor(GrappleHook h, PlayerTank p)
    {
        Vector2 planar = Torus.Delta(p.Position, h.Tip);
        return new Vector3(planar.X, h.TipY - p.Height - ShoulderHeight, planar.Y);
    }

    private static float DistanceTo(GrappleHook h, PlayerTank p) => ToAnchor(h, p).Length();

    /// <summary>Where the cables actually attach to the body: the hips, near enough. The
    /// constraint hangs the rig from here rather than from the feet, so a player reeled
    /// flat against an anchor ends up level with it instead of buried under it.</summary>
    public const float ShoulderHeight = 1.2f;

    /// <summary>
    /// Winds one cable in: the line shortens and the gas jet pushes the body along it.
    /// Both halves matter. Shortening alone is a winch dragging the player up a rope
    /// like a lift; the thrust is what turns it into an <em>acceleration</em>, which is
    /// the thing they then get to keep by letting go at the right moment.
    /// </summary>
    private void Reel(GrappleHook h, PlayerTank p, float amount, float power, float dt)
    {
        h.Length = MathF.Max(MinLength, h.Length - ReelRate * amount * power * dt);

        Vector3 to = ToAnchor(h, p);
        float d = to.Length();
        if (d > 1e-3f) Velocity += to / d * (ReelThrust * amount * power * dt);
    }

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
