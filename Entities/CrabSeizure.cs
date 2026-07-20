using System.Numerics;
using VoidTanks.Core;

namespace VoidTanks.Entities;

/// <summary>
/// The Crab-Core's execution: what happens when it finally closes the distance and
/// gets a claw around the player's craft. For a few seconds the game stops being a
/// game — control is taken away outright, the boss lifts the player off the grid,
/// turns them to face its core, screams into them, clubs them with its free claw
/// and throws them across the arena.
///
/// The whole thing is possible because the camera is the player: the eye is built
/// every frame from <see cref="PlayerTank.Position"/>, <see cref="PlayerTank.Height"/>
/// and <see cref="PlayerTank.Heading"/>, so a cinematic here needs no separate camera
/// rig at all. It sets <see cref="PlayerTank.Captured"/> — which makes the craft
/// refuse to drive itself — and then writes those three fields directly each tick.
/// Everything the player sees follows from that.
///
/// This class is pure simulation: no Raylib, no wall-clock, no randomness, driven
/// entirely by the fixed timestep. It can therefore run in the headless self-test,
/// and it stays deterministic — the trembles and shakes below are shaped from sines
/// on its own clock rather than from a random source. The one thing it does reach
/// out to is <see cref="Audio"/>, which is itself a no-op until a device exists,
/// exactly as the boss's own protocol already does.
/// </summary>
public sealed class CrabSeizure
{
    /// <summary>The beats of the cinematic, in the order they play.</summary>
    public enum Stage
    {
        Seize,    // the claw closes and drags the craft off the grid
        Turn,     // it rotates the player round to face the core
        Scream,   // it screams into them, core blazing
        Strike,   // the free claw winds back and comes down — the 40
        Wind,     // it draws the player back for the throw
        Fly,      // released: a long ballistic arc across the arena
        Recover,  // grounded and back in control, but still rattled
        Done,
    }

    /// <summary>What happened this tick that the world needs to act on. Damage is
    /// reported rather than applied here, because the world owns what a hit to the
    /// player actually means — the shield, the lives, the low-health alarm.</summary>
    public enum Event { None, Struck, Landed }

    // --- Trigger --------------------------------------------------------------

    /// <summary>
    /// How close the boss's body centre must get before it grabs. The rig's legs
    /// reach roughly eleven units, so this is the point where the player is properly
    /// inside the thing rather than merely near it — you have to have let it walk
    /// right up to you, or driven into it yourself.
    /// </summary>
    public const float GrabRadius = 11f;

    /// <summary>
    /// True when the boss is in a position to seize this player: it has to be alive,
    /// actively hunting (a crab still running its threat display hasn't committed
    /// yet), and close enough. The player must be on the grid — a craft caught
    /// mid-leap is spared, which keeps the jump meaningful as the escape it already
    /// is everywhere else in the game.
    /// </summary>
    public static bool CanSeize(CrabCore boss, PlayerTank player)
        => boss.Alive
        && boss.Phase == CrabCore.State.Pursuit
        && !player.IsAirborne
        && Vector2.DistanceSquared(player.Position, boss.Position) <= GrabRadius * GrabRadius;

    // --- Geometry of the hold -------------------------------------------------

    /// <summary>
    /// How far out in front of the boss's centre the craft is held — solved off the
    /// rig rather than chosen, because the player is meant to be <em>in the claw</em>.
    /// This is the point the front-right limb's tip reaches when it swings onto the
    /// chassis centre line, so the hand closes on the craft instead of extending past
    /// it into empty space, which is what a shorter hold looks like from inside.
    ///
    /// It also frames the fight: at arm's length the whole boss sits in the view, the
    /// core gem included, where held close against the chassis the carapace fills the
    /// screen and the crystal is cropped out of frame.
    /// </summary>
    private static readonly float GripReach =
        CrabRig.CentreGripReach(CrabRig.Legs[CrabRig.GrabLeg]) + GripStandoff;

    /// <summary>
    /// How far past the claw the craft is actually carried. Not zero, and the reason is
    /// the leg mesh: it is a walking limb with a high knee, and the pose that raises it
    /// into an arm translates the whole thing bodily upward — shoulder and knee with it
    /// — so a claw parked exactly on the camera puts a limb the size of a building an
    /// arm's length from the player's eye and blacks out most of the screen.
    ///
    /// Held a little beyond the claw instead, the grip still reads: the arm sweeps down
    /// across the frame from the upper corner and passes just under the view, so the
    /// player plainly sees the thing holding them without it standing between them and
    /// the core. This is the number to turn if the hand ever crowds the screen again.
    /// </summary>
    private const float GripStandoff = 2.8f;

    /// <summary>
    /// How high off the grid the craft is carried, with the claw closing a little under
    /// it — see <see cref="CrabRig.GripDrop"/> for why the hand sits below rather than
    /// level with the eye.
    ///
    /// The core pyramid runs from world y≈6.7 to ≈12.7 and the eye rides
    /// <see cref="Config.CameraHeight"/> above the craft, so this puts the eye just
    /// under the middle of the gem: the player looks genuinely <em>up</em> into the
    /// crystal, and the craft still clears the chassis roof (≈5.5) so no part of the
    /// body comes between them and it.
    /// </summary>
    private static readonly float GripHeight = CrabRig.HoldWorldY;

    /// <summary>
    /// How far the craft is canted over in the grip, in radians. The thing has hold of
    /// you by one corner and is carrying you at arm's length — hanging perfectly level
    /// in that grip is the one thing that would read as the game having simply moved
    /// the camera. The tilt is what sells the hold: the horizon goes off-square, and
    /// the world stays that way for as long as it has you.
    ///
    /// Toward the holding hand, so the cant and the claw agree about which side is
    /// taking the weight. Kept modest — enough to feel wrong, not enough to make the
    /// core hard to look at.
    /// </summary>
    private const float HoldRoll = -0.17f;

    /// <summary>
    /// Where in the gem's own height the view is aimed, 0 at its base and 1 at the
    /// apex. Above the middle, so the player is looking up into the crystal and the
    /// apex is comfortably inside the frame rather than crowding the top of it.
    /// </summary>
    private const float GazeUpGem = 0.6f;

    /// <summary>Where the eye sits while the craft is held — the camera rides above
    /// the craft, and every framing decision here is measured from it. Declared before
    /// the gaze below, which reads it: static initialisers run in written order.</summary>
    private static readonly float EyeHeight = CrabRig.HoldWorldY + Config.CameraHeight;

    /// <summary>
    /// The aim held on the core through the turn, the scream and the wind-up, as an
    /// offset to the camera's standing tilt.
    ///
    /// Solved rather than dialled in, because the standing tilt makes eyeballing it
    /// actively misleading: <see cref="Config.CameraLookLift"/> already aims the eye
    /// well up, so a small <em>positive</em> number here — which reads like "look
    /// slightly up at the core" — actually points the camera some four units over the
    /// gem's apex and leaves the crystal in the bottom corner of the screen. What is
    /// wanted is the slope from the eye to a point up the gem, less the tilt that is
    /// already there; that arithmetic comes out negative, and it should.
    /// </summary>
    private static readonly float CoreGaze =
        (CrabRig.CoreWorldY + GazeUpGem * CrabRig.CoreMeshHeight - EyeHeight) / GripReach
        - Config.CameraLookLift;

    // --- Timings (seconds) ----------------------------------------------------

    private const float SeizeTime = 0.5f;
    private const float TurnTime = 0.55f;
    private const float ScreamTime = 1.15f;
    private const float StrikeTime = 0.55f;
    private const float StrikeImpact = 0.3f;   // when in the swing the blow lands
    private const float WindTime = 0.28f;
    private const float RecoverTime = 1.7f;

    // --- Damage ---------------------------------------------------------------

    /// <summary>The blow from the free claw. Flat, and by far the largest single hit
    /// in the game — being caught by this thing should cost most of a shield.</summary>
    public const float StrikeDamage = 40f;

    /// <summary>What hitting the grid at the end of the throw costs, as a fraction of
    /// the shield's maximum. Small on purpose: the landing is the cinematic's
    /// punctuation, not a second punishment.</summary>
    public const float LandingDamageFraction = 0.05f;

    // --- Throw ballistics -----------------------------------------------------
    // Tuned together so the arc runs a shade under two seconds and puts the player
    // some seventy units out — past the boss's give-up radius, so surviving a seizure
    // genuinely breaks the hunt and buys a moment to recover rather than dropping
    // them straight back inside its reach.

    private const float ThrowSpeed = 40f;      // horizontal, units/sec
    private const float ThrowLift = 17f;       // initial vertical kick
    private const float FlightGravity = 22f;

    private readonly CrabCore _boss;
    private readonly PlayerTank _player;

    private Stage _stage = Stage.Seize;
    private float _t;          // seconds inside the current stage
    private float _clock;      // seconds since the seizure began — drives the trembles

    // Where the craft was standing when the claw closed, so the drag into the grip
    // can be interpolated from its real position rather than snapping.
    private Vector2 _fromPos;
    private float _fromHeight;
    private float _fromHeading;

    // Flight state, set at the moment of release.
    private Vector2 _flightVel;
    private float _flightSpeedY;
    private float _flightHeading0;   // heading at release (still facing the boss)
    private float _flightHeading1;   // heading to arrive at (the way they're going)
    private float _flightTime;       // total seconds the arc will take, for easing

    private bool _screamed;
    private bool _struck;

    public CrabSeizure(CrabCore boss, PlayerTank player)
    {
        _boss = boss;
        _player = player;

        _fromPos = player.Position;
        _fromHeight = player.Height;
        _fromHeading = player.Heading;

        // Take the craft out of its own hands for the duration, and drop whatever
        // momentum it was carrying — it is being held, not driving.
        player.Captured = true;
        player.ResetMomentum();

        // The boss turns square onto its catch, so the grip, the scream and the blow
        // all arrive from directly in front of the player.
        boss.SnapToFace(player.Position);

        // The claw closing is the same brutal mechanical snap its clamp display makes
        // — the sound the player has been taught to dread, now with them in it.
        Audio.PlayClamp();
    }

    /// <summary>The beat currently playing.</summary>
    public Stage Phase => _stage;

    /// <summary>True until the cinematic has fully played out, recovery included. The
    /// world holds the seizure object for exactly this long.</summary>
    public bool Active => _stage != Stage.Done;

    /// <summary>True while the boss actually has hold of the craft — up to the throw,
    /// not through the flight. The world blocks a fresh seizure for the whole object's
    /// life, so this is only about how the rig is posed.</summary>
    public bool Held => _stage is Stage.Seize or Stage.Turn or Stage.Scream
                              or Stage.Strike or Stage.Wind;

    // --- What the renderer reads ---------------------------------------------

    /// <summary>How hard the view should judder, 0..1. Rides up through the scream,
    /// spikes on the blow, and rings down across the recovery.</summary>
    public float Shake { get; private set; }

    /// <summary>Camera roll in radians — the world tipping on its axis. Zero except
    /// during the flight, where it swells and settles so the player lands level.</summary>
    public float Roll { get; private set; }

    /// <summary>Extra vertical aim offset on the eye's look direction: positive tips
    /// the view up into the core, negative drives it down toward the grid.</summary>
    public float Pitch { get; private set; }

    /// <summary>0..1 blaze, driving both the boss's core and the full-screen wash
    /// that stands in for the player's own craft lighting up — this is a first-person
    /// game, so the only way to show the player glowing is to flood their view.</summary>
    public float Glow { get; private set; }

    /// <summary>
    /// Advances the cinematic one fixed step and writes the player's transform for
    /// this tick. Returns the damage moment that fell on this tick, if any — the
    /// world applies it, since only the world knows what a hit means.
    /// </summary>
    public Event Update(float dt)
    {
        if (_stage == Stage.Done) return Event.None;

        _t += dt;
        _clock += dt;

        // A core destroyed while it is holding you drops you immediately. Waiting out
        // the scream from inside the claw of something that is already dead would be
        // absurd, and the player has earned the release — so a kill mid-hold cuts
        // straight to the throw and they are flung clear by the last of its strength.
        if (Held && !_boss.Alive)
        {
            BeginFlight();
            return Event.None;
        }

        Event ev = Event.None;
        switch (_stage)
        {
            case Stage.Seize:   UpdateSeize(); break;
            case Stage.Turn:    UpdateTurn(); break;
            case Stage.Scream:  UpdateScream(); break;
            case Stage.Strike:  ev = UpdateStrike(); break;
            case Stage.Wind:    UpdateWind(); break;
            case Stage.Fly:     ev = UpdateFly(dt); break;
            case Stage.Recover: UpdateRecover(dt); break;
        }

        // Hand the boss this frame of the performance: how far each arm is committed
        // and how hard the core is blazing. Released, this zeroes and the rig falls
        // straight back into its ordinary pursuit pose.
        _boss.DriveSeizure(Held, GrabArm, StrikeArm, Glow);
        return ev;
    }

    // --- Stage 1: the claw closes and drags the craft up ----------------------

    private void UpdateSeize()
    {
        float f = Ease(_t / SeizeTime);

        // Dragged off the grid along an ease-out, so the yank is violent at the front
        // and settles into the grip rather than gliding there at a constant rate.
        Vector2 grip = GripPoint();
        _player.Position = Vector2.Lerp(_fromPos, grip, f);
        _player.Height = Lerp(_fromHeight, GripHeight, f);
        _player.Heading = _fromHeading;      // no turn yet: they're still reeling

        // Deliberately restrained. The grab is a firm, mechanical yank — one strong
        // movement — and the motion of being hauled off the grid is already doing the
        // work of selling it. Rattling the view here as well reads as noise and, worse,
        // spends the shake budget at the start: the scream and the blow have nowhere
        // left to escalate to. The whole cinematic's judder builds from here.
        Shake = 0.1f + 0.12f * f;
        Pitch = -0.2f * f;                   // wrenched downward as it takes the weight
        Roll = HoldRoll * f;                 // and canted over as the claw takes it
        Glow = 0.12f * f;

        if (_t >= SeizeTime) Enter(Stage.Turn);
    }

    // --- Stage 2: turned round to face the core ------------------------------

    private void UpdateTurn()
    {
        float f = Ease(_t / TurnTime);

        HoldInGrip(0.05f);

        // Rotated bodily to face the thing holding them. The shortest arc, so it
        // never takes the long way round no matter which way they were pointing.
        float target = FacingBoss();
        _player.Heading = LerpAngle(_fromHeading, target, f);

        // Still held quiet. This beat is the calm the scream detonates out of — the
        // player is turned round, sees what has them, and nothing has happened yet.
        Shake = 0.16f;
        Pitch = Lerp(-0.2f, CoreGaze, f);    // the view comes up onto the core
        Roll = HoldRoll;
        Glow = Lerp(0.12f, 0.45f, f);

        if (_t >= TurnTime) Enter(Stage.Scream);
    }

    // --- Stage 3: the scream -------------------------------------------------

    private void UpdateScream()
    {
        if (!_screamed)
        {
            Audio.PlayCrabScream();
            _screamed = true;
        }

        float f = _t / ScreamTime;

        // The tremble deepens across the scream: it is not just shaking the player,
        // it is shaking them harder the longer it goes on.
        HoldInGrip(0.05f + 0.12f * f);
        _player.Heading = FacingBoss();

        // Held wide open on the core the whole way through, and held off-square: the
        // cant drifts a little as the grip works, so the horizon is never quite still.
        Pitch = CoreGaze;
        Roll = HoldRoll + 0.03f * MathF.Sin(_clock * 4.3f);

        // The core doesn't ramp smoothly to white — it surges, so the gem pulses in
        // the player's face at roughly the rate the sub-layer of the scream heaves.
        float surge = 0.5f + 0.5f * MathF.Sin(_clock * 7.5f);
        Glow = Math.Clamp(0.45f + 0.55f * f * (0.55f + 0.45f * surge), 0f, 1f);

        // This is where the shaking belongs, and it starts the instant the sound
        // does: a hard step up out of the quiet hold — so the scream and the judder
        // arrive as one event and the view is visibly driven by the noise — then a
        // steady climb into the strike. The player is being screamed at, and it gets
        // worse the whole time it goes on.
        Shake = 0.45f + 0.55f * f;

        if (_t >= ScreamTime) Enter(Stage.Strike);
    }

    // --- Stage 4: the free claw comes down -----------------------------------

    private Event UpdateStrike()
    {
        Event ev = Event.None;

        HoldInGrip(0.1f);
        _player.Heading = FacingBoss();

        if (_t < StrikeImpact)
        {
            // The claw is travelling. Everything else goes quiet for the beat it takes
            // — the shake drops away and the glow dims — so the blow lands into a hole
            // rather than on top of the noise that preceded it, and so the only thing
            // moving in the view is the hand coming at the player.
            float f = _t / StrikeImpact;
            Shake = Lerp(1f, 0.15f, f);
            Glow = Lerp(1f, 0.35f, f);
            Pitch = CoreGaze;
            Roll = HoldRoll;
        }
        else
        {
            float f = (_t - StrikeImpact) / (StrikeTime - StrikeImpact);

            // The impact tick itself: the blow, and the only moment of the whole
            // cinematic that costs the player anything but the landing.
            if (!_struck)
            {
                _struck = true;
                Audio.PlayClawSlam();
                ev = Event.Struck;
            }

            // Driven down and sideways by the hit, snapping back toward the grip.
            float recoil = 1f - Ease(f);
            _player.Height = GripHeight - 1.6f * recoil;
            _player.Position = GripPoint() + Right(_boss.Heading) * (2.2f * recoil);
            _player.Heading = FacingBoss() + 0.45f * recoil;   // knocked off square

            Shake = 1f;
            Pitch = CoreGaze - 0.75f * recoil;  // the view slammed toward the floor
            // Knocked hard the other way by the blow, then dragged back to the cant the
            // grip holds them at — the horizon takes the hit along with the player.
            Roll = HoldRoll + 0.5f * recoil;
            Glow = 0.35f + 0.5f * recoil;       // a white flash on contact
        }

        if (_t >= StrikeTime) Enter(Stage.Wind);
        return ev;
    }

    // --- Stage 5: drawn back for the throw -----------------------------------

    private void UpdateWind()
    {
        float f = Ease(_t / WindTime);

        // Pulled in and up — the anticipation beat. Without it the throw reads as
        // the player being dropped rather than hurled.
        Vector2 grip = GripPoint();
        _player.Position = Vector2.Lerp(grip, _boss.Position, f * 0.3f);
        _player.Height = GripHeight + 1.4f * f;
        _player.Heading = FacingBoss();

        Shake = 0.45f;
        Pitch = Lerp(CoreGaze - 0.75f, 0.1f, f);   // recovering from the blow
        Roll = HoldRoll * (1f - f);                // levelling out as the grip opens
        Glow = 0.35f * (1f - f);

        if (_t >= WindTime) BeginFlight();
    }

    // --- Stage 6: the throw --------------------------------------------------

    /// <summary>
    /// Releases the craft on a ballistic arc away from the boss. Also reached early
    /// if the core is destroyed mid-hold, so there is exactly one way out of the
    /// grip and it always looks the same.
    /// </summary>
    private void BeginFlight()
    {
        Vector2 away = Forward(_boss.Heading);

        _flightVel = away * ThrowSpeed;
        _flightSpeedY = ThrowLift;

        // Solve the arc up front so the flight can ease its rotation against a known
        // duration: h + v·t − ½g·t² = 0, taking the positive root.
        float h = MathF.Max(0f, _player.Height);
        _flightTime = (ThrowLift + MathF.Sqrt(ThrowLift * ThrowLift + 2f * FlightGravity * h))
                    / FlightGravity;

        // They leave still facing the boss — so the first half of the flight is spent
        // watching it recede, which is a far better shot than the empty grid — and
        // rotate to face their direction of travel on the way down.
        _flightHeading0 = FacingBoss();
        _flightHeading1 = MathF.Atan2(away.X, away.Y);

        Audio.PlayThrowWhoosh();
        Enter(Stage.Fly);
    }

    private Event UpdateFly(float dt)
    {
        _flightSpeedY -= FlightGravity * dt;
        _player.Position += _flightVel * dt;
        _player.Height += _flightSpeedY * dt;

        float f = Math.Clamp(_t / MathF.Max(0.0001f, _flightTime), 0f, 1f);

        // Turning to face the way they're going, but only once past the apex — the
        // rotation belongs to the fall, not the launch.
        _player.Heading = LerpAngle(_flightHeading0, _flightHeading1,
            Ease(Math.Clamp((f - 0.45f) / 0.55f, 0f, 1f)));

        // A tumble that swells and settles, so however far it rolls the craft is
        // level again by the time the grid arrives.
        Roll = MathF.Sin(f * MathF.PI) * 0.5f * MathF.Sin(_clock * 2.2f + 1f);

        // Pitched up into the sky on the way out, down at the oncoming floor coming in.
        Pitch = Lerp(0.35f, -0.4f, Ease(f));
        Shake = 0.15f;
        Glow = 0f;

        if (_player.Height <= 0f)
        {
            _player.Height = 0f;
            Audio.PlayCrashLanding();
            Enter(Stage.Recover);
            return Event.Landed;
        }
        return Event.None;
    }

    // --- Stage 7: back in control, still ringing -----------------------------

    private void UpdateRecover(float dt)
    {
        // Control comes back the instant the craft touches down; the rest of this
        // stage is only the view settling, and a window in which the boss is barred
        // from taking hold again so the player is never caught in a loop they cannot
        // break out of.
        if (_player.Captured)
        {
            _player.Captured = false;
            _player.ResetMomentum();
        }

        float f = 1f - Math.Clamp(_t / RecoverTime, 0f, 1f);

        // Everything rings down together, with a fast wobble on the roll so the
        // settling reads as a craft rocking on its suspension.
        Shake = 0.4f * f * f;
        Roll = 0.12f * f * f * MathF.Sin(_clock * 13f);
        Pitch = -0.4f * f * f;
        Glow = 0f;

        if (_t >= RecoverTime)
        {
            Shake = 0f;
            Roll = 0f;
            Pitch = 0f;
            Enter(Stage.Done);
            _boss.DriveSeizure(false, 0f, 0f, 0f);
        }
    }

    // --- Arm poses ------------------------------------------------------------

    /// <summary>
    /// How far the holding limb is committed. It swings out across the seize and
    /// then simply stays there — it has the player, and a hand that kept animating
    /// while gripping would undercut the stillness the rest of the rig is selling.
    /// It lets go the instant the throw begins.
    /// </summary>
    private float GrabArm => _stage switch
    {
        Stage.Seize => Ease(_t / SeizeTime),
        Stage.Turn or Stage.Scream or Stage.Strike => 1f,
        Stage.Wind => 1f - 0.25f * (_t / WindTime),   // opening as it launches them
        _ => 0f,
    };

    /// <summary>
    /// The striking limb's swing. It winds back across the scream — visible in the
    /// player's peripheral vision the whole time, which is the telegraph — and then
    /// comes through, arriving on the player at the exact tick the blow is dealt.
    ///
    /// That alignment is the whole point of the beat, and it is why the swing occupies
    /// the run-up to <see cref="StrikeImpact"/> rather than the time after it: the
    /// claw has to travel <em>into</em> the hit. Landing the flash and the damage first
    /// and animating the arm afterwards is what makes a strike read as the screen
    /// simply flickering rather than as something hitting you. Past the impact the arm
    /// holds at full extension — the claw resting where it landed, on the player, while
    /// they reel off it.
    /// </summary>
    private float StrikeArm => _stage switch
    {
        // Drawing back to the cocked pose, which the renderer expresses as the claw
        // rising and swinging outward — so the scream ends with the blow obviously
        // loaded and hanging in the corner of the view.
        Stage.Scream => CrabRig.StrikeCock * Ease(_t / ScreamTime),
        Stage.Strike => _t < StrikeImpact
            ? CrabRig.StrikeCock + (1f - CrabRig.StrikeCock) * Ease(_t / StrikeImpact)
            : 1f,                                      // landed, and left on them
        Stage.Wind => 1f - Ease(_t / WindTime),        // withdrawing
        _ => 0f,
    };

    // --- Helpers --------------------------------------------------------------

    /// <summary>The world point the craft is held at: out in front of the boss, at
    /// claw height. Recomputed every tick so the grip tracks the rig rather than a
    /// spot it happened to occupy when it caught you.</summary>
    private Vector2 GripPoint() => _boss.Position + Forward(_boss.Heading) * GripReach;

    /// <summary>
    /// Parks the craft in the grip with a tremble of the given amplitude — the
    /// machine's grip is not steady, and a perfectly still hold would read as the
    /// game having frozen rather than as the player being restrained. The three axes
    /// run at deliberately unrelated rates so the motion never falls into a visible
    /// pattern, and it stays a pure function of the clock, so the sim is unchanged.
    /// </summary>
    private void HoldInGrip(float amp)
    {
        Vector2 grip = GripPoint();
        _player.Position = grip + new Vector2(
            MathF.Sin(_clock * 31f) * amp,
            MathF.Sin(_clock * 27f + 1.7f) * amp);
        _player.Height = GripHeight + MathF.Sin(_clock * 23f + 0.5f) * amp * 1.4f;
    }

    /// <summary>The heading that points the craft at the boss's core.</summary>
    private float FacingBoss()
    {
        Vector2 to = _boss.Position - _player.Position;
        return to.LengthSquared() > 0.0001f ? MathF.Atan2(to.X, to.Y) : _player.Heading;
    }

    private void Enter(Stage next)
    {
        _stage = next;
        _t = 0f;
    }

    /// <summary>Forward unit vector for a heading, matching the tanks' convention.</summary>
    private static Vector2 Forward(float heading) => new(MathF.Sin(heading), MathF.Cos(heading));

    /// <summary>The rightward unit vector for a heading.</summary>
    private static Vector2 Right(float heading) => new(MathF.Cos(heading), -MathF.Sin(heading));

    /// <summary>Smoothstep on a 0..1 fraction — everything here eases, because
    /// nothing about being picked up by this thing should move linearly.</summary>
    private static float Ease(float f)
    {
        f = Math.Clamp(f, 0f, 1f);
        return f * f * (3f - 2f * f);
    }

    private static float Lerp(float a, float b, float f) => a + (b - a) * Math.Clamp(f, 0f, 1f);

    /// <summary>Interpolates between two headings the short way round, so a turn
    /// never spins 350° to cover 10.</summary>
    private static float LerpAngle(float a, float b, float f)
    {
        float d = b - a;
        while (d > MathF.PI) d -= MathF.Tau;
        while (d < -MathF.PI) d += MathF.Tau;
        return a + d * Math.Clamp(f, 0f, 1f);
    }
}
