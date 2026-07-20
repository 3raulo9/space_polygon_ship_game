using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;

namespace VoidTanks.Entities;

/// <summary>
/// VECTOR.CRAB-CORE — the Stalker. A specialized boss whose whole horror is in
/// its timing: it moves with the stark, rigid, algorithmic gait of an early-3D
/// engine — constant-velocity slides that start and stop on a dime, never eased,
/// never biological — and it makes you wait through a deliberate threat display
/// before it ever comes for you.
///
/// It runs a four-phase state machine (the "Stalker Protocol"):
///   0 Idle       — rests on the grid, neon pyramid core spinning lazily in magenta.
///   1 Threat     — on noticing the player it faces them, then hard-slides
///                  Right / (pause) / Left / (pause) / Right, each slide 0.5s,
///                  like a robotic arm calibrating.
///   2 Clamping   — plants itself and snaps its two claw-plates shut 3 times,
///                  each snap a bit-crushed CLANG and a red flash of the core.
///   3 Pursuit    — locks the player's coordinates and slides straight at them at
///                  exactly the player's walking speed, legs skittering.
///
/// The class owns the AI + movement (the sim) and exposes a <see cref="CrabPose"/>
/// each frame for the renderer. The same visual mapping is reused, decoupled from
/// the live brain, to loop any single phase on the hidden test screen.
/// </summary>
public sealed class CrabCore
{
    // Aiming and Firing are appended after Pursuit rather than slotted in beside it
    // on purpose: the test screen scrubs phases by their raw ordinal (keys 1..4 →
    // 0..3), so Idle/ThreatDisplay/Clamping/Pursuit have to keep the indices they
    // have always had. The lance states are only ever reached from the live brain.
    public enum State { Idle, ThreatDisplay, Clamping, Pursuit, Aiming, Firing, Dying, Dead }

    public Vector2 Position;
    public float Heading;               // radians; 0 faces +Z, matching the tanks
    public State Phase { get; private set; } = State.Idle;

    // --- The vulnerable core --------------------------------------------------
    // Only the exposed neon core takes damage, and only an airborne shot rides high
    // enough to reach it: the whole fight is timing a jump so a level bolt threads
    // the raised gem. Its chassis, legs and claws are inert armour.
    public const float CoreMaxHealth = 4f;
    private float _coreHealth = CoreMaxHealth;

    /// <summary>The core's strike zone, in world space. Anchored a little up into the
    /// gem from its base, with a generous planar reach (the boss is huge) and a
    /// vertical band roughly the height of a leap — so a shot fired near the top of a
    /// jump threads it, but a grounded bolt sails harmlessly underneath.</summary>
    public static float CoreHitHeight => CrabRig.CoreWorldY + 1.4f;
    public const float CoreHitRadius = 3.6f;    // planar reach around the core
    public const float CoreHitVertical = 3.2f;  // half-height of the strike band

    private float _deathTime;                   // seconds since the core blew
    private const float DeathDuration = 1.3f;   // length of the glitch-apart death

    /// <summary>True while the core still holds — it hunts and can be hit.</summary>
    public bool Alive => Phase is not (State.Dying or State.Dead);

    /// <summary>Within this range an active boss rattles the player's view.</summary>
    public const float ShakeRadius = 34f;

    /// <summary>
    /// How hard the boss should shake the camera, 0..1, given the player's spot: it
    /// ramps up the nearer an <em>active</em> (waking, clamping or hunting) boss
    /// gets, and is flat zero while it's idle, dying or already dead — a dormant
    /// crab you happen to walk past never shakes the screen.
    /// </summary>
    public float ProximityShake(Vector2 playerPos)
    {
        if (Phase is State.Idle or State.Dying or State.Dead) return 0f;
        float dist = Vector2.Distance(playerPos, Position);
        return Math.Clamp(1f - dist / ShakeRadius, 0f, 1f);
    }

    /// <summary>True once the death glitch has fully played out and the rig is gone.</summary>
    public bool Dead => Phase == State.Dead;

    /// <summary>
    /// 0 while the boss is dormant, 1 once it has noticed the player and is working
    /// — the threat display counts, so its internal rotor is already winding up
    /// before it has taken a step toward you. Drives the pitch of the machinery hum;
    /// a dying crab spins back down.
    /// </summary>
    public float Agitation => Phase is State.ThreatDisplay or State.Clamping or State.Pursuit
                                    or State.Aiming or State.Firing ? 1f : 0f;

    /// <summary>0 while alive, ramping 0→1 across the death glitch — the renderer
    /// reads this to fling the parts apart and tear the whole rig with static.</summary>
    public float DeathProgress => Phase == State.Dying
        ? Math.Clamp(_deathTime / DeathDuration, 0f, 1f)
        : (Phase == State.Dead ? 1f : 0f);

    /// <summary>0..1 remaining core integrity, for a HUD boss bar later if wanted.</summary>
    public float CoreFraction => Math.Clamp(_coreHealth / CoreMaxHealth, 0f, 1f);

    // --- Protocol tuning ------------------------------------------------------
    public const float DetectRadius = 45f;   // the player crosses this and it wakes
    public const float GiveUpRadius = 75f;   // outrun it past this and the hunt breaks off
    public const float SlideSpeed = 10f;     // slow, mechanical strafe (units/sec) — a deliberate, telegraphed lurch
    public const float SlideTime = 0.5f;     // each hard slide lasts exactly this
    public const float PauseTime = 0.2f;     // the creepy dead beat between slides
    public const int ClampCount = 3;         // three snaps, always
    public const float ClampPeriod = 0.34f;  // time budget per open+snap
    public const float JawOpenTime = 0.18f;  // claws yawn open, then snap the rest

    /// <summary>
    /// Pursuit speed: deliberately slower than the player's top walking speed, so a
    /// player who keeps moving can always outrun it. Its menace is relentlessness —
    /// it never stops coming — not raw pace.
    /// </summary>
    public const float RunSpeed = PlayerTank.MaxSpeed * 0.62f;

    // Core spin: lazy at rest, agitated once the threat display begins.
    private const float IdleSpin = 1.2f;
    private const float AgitatedSpin = 4.5f;

    // Leg skitter rates per mood: a slow idle twitch, a shuffle during the display,
    // a fast loop-synchronous scuttle in pursuit.
    private const float IdleLeg = 0.9f;
    private const float ShuffleLeg = 3.0f;
    private const float PursuitLeg = 13.0f;

    // --- Live animation accumulators ---
    private float _stateTime;
    private float _coreSpin;
    private float _legPhase;
    private float _clawOpen;
    private float _flash;               // 0..1 white-hot pop on a clamp snap, decays
    private bool _jawWasOpen;           // edge-detect the open→shut snap
    private float _lastSlideDir;        // threat-display slide dir last tick, to catch a new lurch

    // Per-leg gait sign last tick, to catch the instant a foot plants (the +→-
    // zero-crossing of its lift sine), plus the world spots those plants happened.
    private readonly float[] _legSinLast = new float[CrabRig.Legs.Length];
    private readonly List<Footfall> _footfalls = new();

    /// <summary>Feet that struck the floor this tick — the caller spawns dust at each
    /// and voices each limb separately. Refilled every <see cref="Update"/>.</summary>
    public IReadOnlyList<Footfall> Footfalls => _footfalls;

    public CrabCore(Vector2 start, float heading = MathF.PI)
    {
        Position = start;
        Heading = heading;
    }

    /// <summary>
    /// Advances the protocol one tick against the player's position. Returns true on
    /// the exact tick a claw-plate snaps shut, so the caller can fire the CLANG.
    /// </summary>
    public bool Update(float dt, Vector2 playerPos)
    {
        _stateTime += dt;
        _footfalls.Clear();
        bool snapped = false;

        // Mid-seizure the protocol is suspended: it has already caught you, so there
        // is nothing left to stalk. It plants where it stands and only the core keeps
        // turning — the stillness is the point, since a boss that carried on
        // skittering while holding someone would read as an animation glitch rather
        // than as a thing giving you its full attention. Dying overrides even this:
        // a core destroyed mid-hold still tears itself apart on schedule.
        if (Seizing && Phase is not (State.Dying or State.Dead))
        {
            _legPhase += IdleLeg * 0.35f * dt;      // the barest brace-and-shift
            _clawOpen = 0f;
            _coreSpin += AgitatedSpin * dt;
            if (_flash > 0f) _flash = MathF.Max(0f, _flash - dt * 4f);
            DetectFootfalls();
            return false;
        }

        switch (Phase)
        {
            case State.Idle:        UpdateIdle(dt, playerPos); break;
            case State.ThreatDisplay: UpdateThreat(dt); break;
            case State.Clamping:    snapped = UpdateClamping(dt); break;
            case State.Pursuit:     UpdatePursuit(dt, playerPos); break;
            case State.Aiming:      UpdateAiming(dt, playerPos); break;
            case State.Firing:      UpdateFiring(dt); break;
            case State.Dying:       UpdateDying(dt); break;
            case State.Dead:        return false;   // gone; nothing left to do
        }

        // Core always turns; flash always cools. Outside the lance the chassis
        // settles back onto its legs, so a boss interrupted mid-charge (killed, or
        // dropped back to idle) rights itself instead of walking away tipped over.
        _coreSpin += CoreSpinRate * dt;
        if (_flash > 0f) _flash = MathF.Max(0f, _flash - dt * 4f);
        if (Phase is not (State.Aiming or State.Firing)) SettleTilt(0f, 0f, dt, TiltRelaxRate);

        DetectFootfalls();
        return snapped;
    }

    // --- Death: the core is spent; freeze the brain and let the glitch play out ---
    private void UpdateDying(float dt)
    {
        _deathTime += dt;
        if (_deathTime >= DeathDuration) Phase = State.Dead;
        // No footfalls, no pursuit — the legs are busy tearing off.
    }

    /// <summary>
    /// True only when a shot's world position falls inside the core's strike zone:
    /// within the planar reach of the gem *and* inside the vertical band a leaping
    /// bolt rides through. Grounded shots sit far below the band and always miss.
    /// </summary>
    public bool HitsCore(Vector2 shotXZ, float shotHeight)
    {
        if (!Alive) return false;
        if (MathF.Abs(shotHeight - CoreHitHeight) > CoreHitVertical) return false;
        return Vector2.DistanceSquared(shotXZ, Position) <= CoreHitRadius * CoreHitRadius;
    }

    /// <summary>
    /// Deals a hit to the core and flares it white-hot. Returns true on the exact hit
    /// that spends the last of its integrity, so the caller can stage the death blast.
    /// </summary>
    public bool DamageCore(float amount)
    {
        if (!Alive) return false;
        _coreHealth -= amount;
        _flash = 1f;                            // the gem flares on every hit
        if (_coreHealth <= 0f)
        {
            _coreHealth = 0f;
            Phase = State.Dying;
            _stateTime = 0f;
            _deathTime = 0f;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Records the world spots where a foot just planted this tick — the +→- zero
    /// crossing of each leg's lift sine, the moment it comes down onto the grid.
    /// Mirrors the renderer's per-leg phase so dust lands under the visible feet.
    /// </summary>
    private void DetectFootfalls()
    {
        var legs = CrabRig.Legs;
        for (int i = 0; i < legs.Length; i++)
        {
            float s = MathF.Sin(_legPhase + legs[i].PhaseOffset);
            if (_legSinLast[i] > 0f && s <= 0f)
                _footfalls.Add(new Footfall(i, CrabRig.FootWorldXZ(legs[i], Position, Heading)));
            _legSinLast[i] = s;
        }
    }

    private float CoreSpinRate => Phase switch
    {
        State.Idle => IdleSpin,
        // The crystal winds up hard as it charges and is still tearing round while
        // the beam burns — the spin is the charge, visually and audibly.
        State.Aiming => AgitatedSpin + (ChargeSpin - AgitatedSpin) * ChargeProgress,
        State.Firing => FiringSpin,
        _ => AgitatedSpin,
    };

    // --- State 0: idle, until the player strays inside the detection radius ---
    private void UpdateIdle(float dt, Vector2 playerPos)
    {
        _legPhase += IdleLeg * dt;
        _clawOpen = 0f;

        if (Vector2.DistanceSquared(playerPos, Position) <= DetectRadius * DetectRadius)
        {
            // Shift the chassis to face the player — a hard snap, not a glide.
            Vector2 to = playerPos - Position;
            if (to.LengthSquared() > 0.0001f) Heading = MathF.Atan2(to.X, to.Y);
            Enter(State.ThreatDisplay);
        }
    }

    // --- State 1: the threat display — R / pause / L / pause / R hard slides ---
    private void UpdateThreat(float dt)
    {
        _legPhase += ShuffleLeg * dt;
        _clawOpen = 0f;

        float t = _stateTime;
        float dir = ThreatSlideDir(t, out bool done);   // +1 right, -1 left, 0 paused
        if (done) { Enter(State.Clamping); return; }

        // The instant it commits to a new side (a pause/zero → moving edge), blare
        // the alarm — it lurches right, then left, then right, sounding each time.
        if (dir != 0f && _lastSlideDir == 0f) Audio.PlayAlarm();
        _lastSlideDir = dir;

        Position += RightVector() * (SlideSpeed * dir) * dt;
    }

    /// <summary>
    /// The display's timeline as a signed slide direction: Right, pause, Left,
    /// pause, Right. Shared with the test-screen loop so both read identically.
    /// </summary>
    public static float ThreatSlideDir(float t, out bool done)
    {
        done = false;
        float s = SlideTime, p = PauseTime;
        if (t < s) return +1f;                    // slide right
        t -= s;
        if (t < p) return 0f;                     // pause
        t -= p;
        if (t < s) return -1f;                    // slide left
        t -= s;
        if (t < p) return 0f;                     // pause
        t -= p;
        if (t < s) return +1f;                    // slide right
        done = true;
        return 0f;
    }

    // --- State 2: clamp the claws shut 3 times, hard ---
    private bool UpdateClamping(float dt)
    {
        _legPhase += ShuffleLeg * 0.3f * dt;       // legs almost still, braced

        int idx = (int)(_stateTime / ClampPeriod);
        if (idx >= ClampCount) { Enter(State.Pursuit); return false; }

        float local = _stateTime - idx * ClampPeriod;
        bool open = local < JawOpenTime;
        // Yawn open on a ramp, then hold shut for the rest of the period.
        _clawOpen = open ? local / JawOpenTime : 0f;

        bool snapped = _jawWasOpen && !open;       // the open→shut edge is the snap
        _jawWasOpen = open;
        if (snapped) _flash = 1f;                   // core flares toward white-red
        return snapped;
    }

    // --- State 3: relentless pursuit at the player's own walking speed ---
    private void UpdatePursuit(float dt, Vector2 playerPos)
    {
        _legPhase += PursuitLeg * dt;
        _clawOpen = 0f;

        Vector2 to = playerPos - Position;
        float dist = to.Length();

        // Keep enough distance for long enough and the hunt breaks off — it stops
        // dead and sinks back to idle, spinning lazily until the player strays into
        // detection range again. The give-up radius sits well past detection so a
        // player hovering at the edge doesn't flip it on and off.
        if (dist > GiveUpRadius) { Enter(State.Idle); return; }

        // It calls while it runs. Fired every tick on purpose — Audio owns the
        // cadence and the irregular gaps between groans, so the sim stays
        // deterministic and this stays a plain "I am hunting, at this range".
        Audio.PlayHuntCall(dist);

        if (dist < 0.0001f) return;
        Vector2 dir = to / dist;

        Heading = MathF.Atan2(dir.X, dir.Y);        // locked onto the coordinates
        Position += dir * RunSpeed * dt;            // rigid, direct, no easing

        // ...and every so often it stops running and lines up the lance instead.
        // Only from out here: inside BeamMinRange it already has the player within
        // reach of its claws, and charging a beam at something it could simply grab
        // would read as the boss forgetting what it was doing.
        _beamCooldown -= dt;
        if (_beamCooldown <= 0f && dist >= BeamMinRange && dist <= BeamMaxRange)
            BeginCharge(playerPos);
    }

    // --- State 4/5: the lance — tilt onto the target, lock, and fire ----------
    // The whole point of this attack is that it is escapable and that the escape is
    // spatial rather than reactive. It telegraphs hard (a body visibly tipping onto
    // you, a spinning charge, three climbing warnings), it commits to one direction
    // at the moment the warnings stop, and then it holds that direction for a full
    // five seconds without tracking. So the answer is never "dodge at the right
    // instant" — it is "see where it is pointing and be somewhere else".

    /// <summary>Nearest range the boss will bother charging from. Closer than this
    /// it goes for the grab instead.</summary>
    public const float BeamMinRange = 18f;

    /// <summary>Past this it can't line the shot up at all and keeps running.</summary>
    public const float BeamMaxRange = 70f;

    /// <summary>Bounds on the wait between lance shots, re-rolled each time so the
    /// player can never count the beats between them.</summary>
    private const float BeamCooldownMin = 5f;
    private const float BeamCooldownMax = 10f;

    /// <summary>How long the charge runs — the window the player has to get out of
    /// the line of fire. Three warnings are spaced across it.</summary>
    public const float ChargeTime = 2.6f;

    /// <summary>How long the beam burns, held on the direction locked at the end of
    /// the charge. Long enough that walking back into it is entirely possible.</summary>
    public const float BeamTime = 5f;

    /// <summary>How far the beam reaches, and how wide it bites.</summary>
    public const float BeamLength = 150f;
    public const float BeamRadius = 2.4f;

    /// <summary>The three warnings' moments inside the charge, as fractions of it.
    /// Front-loaded so the last one lands with most of a second still to run: a
    /// warning that fires as the shot does is not a warning.</summary>
    private static readonly float[] WarnAt = { 0.16f, 0.42f, 0.68f };

    // Core spin while it charges and while it burns — far past even the agitated
    // rate, so the gem visibly winds up into the shot.
    private const float ChargeSpin = 13f;
    private const float FiringSpin = 20f;

    /// <summary>How fast the chassis eases onto its aim, and how fast it rights
    /// itself afterwards. Slow enough to read as mass being shifted rather than a
    /// value being set — this is the one thing the boss does that isn't a hard snap,
    /// because a body bracing to aim is the one thing it does that isn't a lurch.</summary>
    private const float TiltAimRate = 2.6f;
    private const float TiltRelaxRate = 1.8f;

    /// <summary>Hard ceiling on how far it will tip. Past this the rig's legs stop
    /// plausibly holding it up and it reads as the model falling over.</summary>
    private const float MaxTiltPitch = 0.42f;
    private const float MaxTiltRoll = 0.30f;

    private float _beamCooldown = BeamCooldownMin;
    private float _tiltPitch, _tiltRoll;    // live chassis lean, radians
    private float _braceRoll;               // the side it braced onto, rolled per shot
    private float _lockHeading;             // planar bearing frozen at the lock
    private float _lockPitch;               // and the elevation with it
    private int _warnStep;                  // how many of the three have sounded
    private readonly Random _rng = new();

    /// <summary>0..1 across the charge, 0 otherwise — the renderer swells the gem's
    /// glare on this and the world can telegraph it however it likes.</summary>
    public float ChargeProgress => Phase == State.Aiming
        ? Math.Clamp(_stateTime / ChargeTime, 0f, 1f) : 0f;

    /// <summary>True for the five seconds the beam is actually burning.</summary>
    public bool BeamActive => Phase == State.Firing;

    /// <summary>0..1 through the burn.</summary>
    public float BeamProgress => Phase == State.Firing
        ? Math.Clamp(_stateTime / BeamTime, 0f, 1f) : 0f;

    /// <summary>The chassis's lean this frame — pitch (nosing down onto the target)
    /// and roll (braced onto one side). Both zero unless it is working the lance.</summary>
    public float TiltPitch => _tiltPitch;
    public float TiltRoll => _tiltRoll;

    /// <summary>Where the beam leaves the gem: the middle of the crystal, so the
    /// bolt visibly comes out of the thing that spent three seconds spinning.</summary>
    public Vector3 BeamOrigin => new(Position.X, CrabRig.CoreWorldY + 3.0f, Position.Y);

    /// <summary>
    /// The beam's unit direction — the bearing and elevation frozen at the lock, not
    /// the player's current spot. This is the whole attack: once <see cref="State.Firing"/>
    /// begins, nothing the player does moves this vector.
    /// </summary>
    public Vector3 BeamDirection
    {
        get
        {
            float cp = MathF.Cos(_lockPitch);
            return new Vector3(MathF.Sin(_lockHeading) * cp, -MathF.Sin(_lockPitch),
                               MathF.Cos(_lockHeading) * cp);
        }
    }

    /// <summary>
    /// Breaks off the chase and starts lining the lance up: it rolls which side it
    /// braces onto, wakes the spinning charge, and hands the first warning over.
    /// </summary>
    private void BeginCharge(Vector2 playerPos)
    {
        // Which way it tips is rolled fresh every time, so two charges never look
        // like the same canned animation — but the magnitude stays inside the rig's
        // means, and the sign is what actually varies.
        _braceRoll = (float)(_rng.NextDouble() * 2.0 - 1.0) * MaxTiltRoll;
        _warnStep = 0;
        SnapToFace(playerPos);
        Enter(State.Aiming);
        Audio.PlayBeamCharge();
    }

    /// <summary>
    /// The charge. It stands still and tips its whole body onto the player — turning
    /// to keep the bearing and nosing down to the elevation, so the crystal is
    /// visibly pointed at them the entire time — while the gem spins up and the three
    /// warnings climb. Everything here still tracks; nothing here is committed yet.
    /// </summary>
    private void UpdateAiming(float dt, Vector2 playerPos)
    {
        _legPhase += ShuffleLeg * 0.5f * dt;    // shuffling into a brace, not walking
        _clawOpen = 0f;

        Vector2 to = playerPos - Position;
        float dist = to.Length();

        // Bearing eases rather than snaps — this is the boss taking aim, and an
        // instant turn would give the player nothing to read off the body.
        if (dist > 0.0001f)
            Heading = ApproachAngle(Heading, MathF.Atan2(to.X, to.Y), 2.2f * dt);

        // Nose down by however far below the gem the player actually is. Taken off
        // the rig's own geometry rather than picked: the crystal sits CoreWorldY up,
        // the craft is on the grid, so this is the true angle between them — which is
        // what makes the tilt read as aiming rather than as a posing animation.
        float drop = MathF.Atan2(CrabRig.CoreWorldY + 3.0f, MathF.Max(dist, 1f));

        // A slow organic sway on top, at two unrelated rates, so the hold never
        // freezes into a still frame — something this heavy braced against its own
        // recoil should be visibly fighting to keep the line.
        float sway = MathF.Sin(_stateTime * 3.1f) * 0.035f;
        float lean = MathF.Sin(_stateTime * 2.3f + 1.1f) * 0.045f;

        float f = ChargeProgress;
        SettleTilt(
            Math.Clamp(drop * f + sway, -MaxTiltPitch, MaxTiltPitch),
            Math.Clamp(_braceRoll * f + lean, -MaxTiltRoll, MaxTiltRoll),
            dt, TiltAimRate);

        // The three climbing warnings, each fired once as its moment passes.
        if (_warnStep < WarnAt.Length && _stateTime >= WarnAt[_warnStep] * ChargeTime)
            Audio.PlayBeamWarning(_warnStep++);

        if (_stateTime >= ChargeTime)
        {
            // The lock. Bearing and elevation are frozen here, off the last position
            // the player was seen in, and the beam rides them for its whole life.
            _lockHeading = Heading;
            _lockPitch = MathF.Atan2(CrabRig.CoreWorldY + 3.0f, MathF.Max(dist, 1f));
            Enter(State.Firing);
            Audio.PlayBeamFire();
        }
    }

    /// <summary>
    /// The burn. It is committed: the chassis holds exactly the attitude it locked,
    /// the heading does not move, and it does not chase. All it does is hold the line
    /// and let the beam sit there. Only the gem keeps turning.
    /// </summary>
    private void UpdateFiring(float dt)
    {
        _legPhase += ShuffleLeg * 0.15f * dt;   // planted, barely bracing
        _clawOpen = 0f;
        Heading = _lockHeading;                 // frozen: it shoots where you were

        // Held on the locked attitude, with a fine tremor of recoil through it — the
        // sway of the charge is gone, because it has stopped adjusting.
        float shudder = MathF.Sin(_stateTime * 31f) * 0.012f;
        SettleTilt(_lockPitch + shudder, _braceRoll + shudder * 0.5f, dt, TiltAimRate * 2f);

        if (_stateTime >= BeamTime)
        {
            // Spent — back to running the player down, with a fresh wait before it
            // can do this again.
            _beamCooldown = BeamCooldownMin
                + (float)_rng.NextDouble() * (BeamCooldownMax - BeamCooldownMin);
            Enter(State.Pursuit);
        }
    }

    /// <summary>Eases the chassis's lean toward a target attitude at the given rate,
    /// without overshooting. Both axes move together so the body arrives on its aim
    /// as one movement rather than pitching and rolling in sequence.</summary>
    private void SettleTilt(float pitch, float roll, float dt, float rate)
    {
        float step = rate * dt;
        _tiltPitch = Approach(_tiltPitch, pitch, step);
        _tiltRoll = Approach(_tiltRoll, roll, step);
    }

    private static float Approach(float v, float target, float step)
        => v < target ? MathF.Min(target, v + step) : MathF.Max(target, v - step);

    /// <summary>Turns a heading toward a target the short way round, by at most
    /// <paramref name="step"/> radians.</summary>
    private static float ApproachAngle(float a, float b, float step)
    {
        float d = b - a;
        while (d > MathF.PI) d -= MathF.Tau;
        while (d < -MathF.PI) d += MathF.Tau;
        return a + Math.Clamp(d, -step, step);
    }

    private void Enter(State next)
    {
        Phase = next;
        _stateTime = 0f;
        _jawWasOpen = false;
        _lastSlideDir = 0f;
    }

    /// <summary>Unit vector pointing to the boss's own right, given its heading.</summary>
    private Vector2 RightVector()
    {
        // forward = (sin, cos); its right is a 90° clockwise turn of that.
        return new Vector2(MathF.Cos(Heading), -MathF.Sin(Heading));
    }

    /// <summary>The live visual snapshot the renderer poses the parts from.</summary>
    public CrabPose Pose => new(
        _coreSpin, _clawOpen, _legPhase,
        CoreColorFor(Hostility, MathF.Max(MathF.Max(_flash, SeizureGlow), LanceGlare)),
        Vector2.Zero, GrabArm, StrikeArm, _tiltPitch, _tiltRoll);

    private float Hostility => Phase is State.Clamping or State.Pursuit
                                     or State.Aiming or State.Firing ? 1f : 0f;

    /// <summary>
    /// How white-hot the gem is running for the lance: it swells across the charge —
    /// the crystal filling with the shot — and then pins near white for the whole
    /// burn. Squared on the way up so most of the brightening happens late, which is
    /// what makes the last second before the lock the alarming one.
    /// </summary>
    private float LanceGlare => Phase switch
    {
        State.Aiming => ChargeProgress * ChargeProgress * 0.85f,
        State.Firing => 0.9f,
        _ => 0f,
    };

    // --- Seizure: the boss has the player in its claw -------------------------
    // The cinematic itself lives in CrabSeizure, which owns the timing and drives
    // the player's transform. The boss only needs to know it is mid-seizure so it
    // stops walking, and to expose the three channels the cinematic writes into:
    // the two arms and the core's glow. Keeping the drive one-way like this means
    // the protocol state machine above never has to know the seizure exists.

    /// <summary>True while the boss is holding the player. Its whole body stops —
    /// it has what it came for — but the core keeps turning and it keeps facing
    /// them, so it reads as attending to you rather than as a paused animation.</summary>
    public bool Seizing { get; private set; }

    /// <summary>Front-right limb's commitment to the grip, 0..1. Written by the
    /// cinematic, read by the renderer through <see cref="Pose"/>.</summary>
    public float GrabArm { get; private set; }

    /// <summary>Front-left limb's swing, 0 wound back to 1 struck through.</summary>
    public float StrikeArm { get; private set; }

    /// <summary>Extra white-hot glow on the core, 0..1, on top of whatever the
    /// combat flash is doing — the gem blazing in the player's face as it screams.
    /// Folded in with <see cref="MathF.Max"/> so a hit landing mid-seizure can still
    /// flare brighter than the hold does.</summary>
    public float SeizureGlow { get; private set; }

    /// <summary>
    /// Hands the boss the cinematic's current frame: whether it still has hold of
    /// the player, how far each arm is committed, and how hard the core is blazing.
    /// Called every tick of a seizure and once more on release with
    /// <paramref name="held"/> false, which lets the rig fall straight back into its
    /// normal pursuit pose.
    /// </summary>
    public void DriveSeizure(bool held, float grabArm, float strikeArm, float glow)
    {
        Seizing = held;
        GrabArm = Math.Clamp(grabArm, 0f, 1f);
        StrikeArm = Math.Clamp(strikeArm, 0f, 1f);
        SeizureGlow = Math.Clamp(glow, 0f, 1f);
    }

    /// <summary>
    /// Turns the chassis to face a world point in one hard snap — no easing, the way
    /// every other move this thing makes lands. Used when it takes hold of the player,
    /// so the grip, the scream and the blow all come from directly in front of them.
    /// </summary>
    public void SnapToFace(Vector2 target)
    {
        Vector2 to = target - Position;
        if (to.LengthSquared() > 0.0001f) Heading = MathF.Atan2(to.X, to.Y);
    }

    // --- Shared visual mapping ------------------------------------------------

    /// <summary>
    /// The core's colour for a given hostility (0 = calm magenta, 1 = hunting red)
    /// with a white-hot flash mixed on top. Kept here so the live brain and the
    /// test-screen loop tint the gem the same way.
    /// </summary>
    public static Color CoreColorFor(float hostility, float flash)
    {
        Color baseC = LerpColor(Palette.NeonMagenta, Palette.NeonRed, hostility);
        return LerpColor(baseC, Color.White, Math.Clamp(flash, 0f, 1f) * 0.75f);
    }

    /// <summary>
    /// Builds a looping pose for one phase, for the hidden test screen — no player,
    /// no real movement; each phase just plays on repeat so its animation can be
    /// eyeballed. <paramref name="t"/> is free-running seconds.
    /// </summary>
    public static CrabPose ShowcasePose(State phase, float t)
    {
        switch (phase)
        {
            case State.ThreatDisplay:
            {
                // Loop the R/pause/L/pause/R timeline as a visible lateral drift so
                // the mechanical slide reads on the turntable.
                const float loop = 2f * SlideTime + 2f * PauseTime + SlideTime; // 1.9s
                float lt = t % (loop + 0.4f);
                float slide = ThreatSlideAmount(lt);
                return new CrabPose(t * IdleSpin, 0f, t * ShuffleLeg,
                    CoreColorFor(0f, 0f), new Vector2(slide, 0f));
            }
            case State.Clamping:
            {
                const float loop = ClampCount * ClampPeriod + 0.5f; // clamps + rest
                float lt = t % loop;
                float open = 0f, flash = 0f;
                int idx = (int)(lt / ClampPeriod);
                if (idx < ClampCount)
                {
                    float local = lt - idx * ClampPeriod;
                    open = local < JawOpenTime ? local / JawOpenTime : 0f;
                    // Flash decays across the ~0.16s after each snap.
                    float sinceSnap = local - JawOpenTime;
                    if (sinceSnap >= 0f) flash = MathF.Max(0f, 1f - sinceSnap * 6f);
                }
                return new CrabPose(t * AgitatedSpin, open, t * ShuffleLeg * 0.3f,
                    CoreColorFor(1f, flash), Vector2.Zero);
            }
            case State.Pursuit:
            {
                // A little fore/aft surge sells the relentless skitter in place.
                float surge = MathF.Sin(t * 4f) * 0.4f;
                float pulse = 0.5f + 0.5f * MathF.Sin(t * 8f);
                return new CrabPose(t * AgitatedSpin, 0f, t * PursuitLeg,
                    CoreColorFor(1f, pulse * 0.25f), new Vector2(0f, surge));
            }
            case State.Aiming:
            {
                // The charge on repeat, with a beat of flat rig between laps so the
                // tilt is visibly something the body does rather than its rest pose.
                float lt = t % (ChargeTime + 0.6f);
                float f = Math.Clamp(lt / ChargeTime, 0f, 1f);
                // Rolls to one side and back over the loop rather than picking a
                // random side — the turntable should show the range of the lean.
                float roll = MathF.Sin(t * 0.7f) * MaxTiltRoll;
                return new CrabPose(t * ChargeSpin, 0f, t * ShuffleLeg * 0.5f,
                    CoreColorFor(1f, f * f * 0.85f), Vector2.Zero, 0f, 0f,
                    MaxTiltPitch * f + MathF.Sin(t * 3.1f) * 0.035f, roll * f);
            }
            case State.Firing:
            {
                // Committed: the attitude is held dead still bar the recoil tremor,
                // which is the whole difference between this and the charge above.
                float shudder = MathF.Sin(t * 31f) * 0.012f;
                return new CrabPose(t * FiringSpin, 0f, t * ShuffleLeg * 0.15f,
                    CoreColorFor(1f, 0.9f), Vector2.Zero, 0f, 0f,
                    MaxTiltPitch + shudder, MaxTiltRoll * 0.6f + shudder * 0.5f);
            }
            default: // Idle
                return new CrabPose(t * IdleSpin, 0f, t * IdleLeg,
                    CoreColorFor(0f, 0f), Vector2.Zero);
        }
    }

    /// <summary>Net lateral offset traced by the threat-display slides, for showcase.</summary>
    private static float ThreatSlideAmount(float t)
    {
        // Integrate the slide direction over the timeline into a position.
        float amt = 0f, step = 0.01f;
        for (float u = 0f; u < t; u += step)
        {
            float dir = ThreatSlideDir(u, out bool done);
            if (done) break;
            amt += dir * SlideSpeed * step;
        }
        return amt;
    }

    private static Color LerpColor(Color a, Color b, float f)
    {
        f = Math.Clamp(f, 0f, 1f);
        return new Color(
            (int)(a.R + (b.R - a.R) * f),
            (int)(a.G + (b.G - a.G) * f),
            (int)(a.B + (b.B - a.B) * f),
            255);
    }
}

/// <summary>
/// One foot striking the grid: which leg it was (an index into
/// <see cref="CrabRig.Legs"/>) and where it landed in world XZ. The index matters
/// because each limb is voiced as its own actuator — a tripod lands together, and
/// hearing three distinct joints rather than one clank is what makes the gait read
/// as a machine with six legs instead of a thing that goes thud.
/// </summary>
public readonly record struct Footfall(int Leg, Vector2 Pos);

/// <summary>
/// A frame's worth of Crab-Core animation state, produced by the entity (or the
/// test-screen loop) and consumed by the renderer. Purely visual — the real
/// position/heading live on the entity.
/// </summary>
public readonly record struct CrabPose(
    float CoreSpin,       // radians the gem has turned
    float ClawOpen,       // 0 = plates shut, 1 = plates yawned open
    float LegPhase,       // radians driving the skitter bob
    Color CoreColor,      // gem tint this frame (magenta..red..white flash)
    Vector2 SlideOffset,  // showcase-only world drift; zero for the live boss
    // The two front legs double as the boss's hands during a seizure: it has no
    // dedicated arms, so the front-right limb swings forward to hold the player and
    // the front-left winds back and clubs them. Both run 0 (a normal walking leg) to
    // 1 (fully committed), and both are zero for every other phase — so the rig
    // draws exactly as it always has unless it has actually got hold of someone.
    float GrabArm = 0f,   // front-right limb extended into the grip
    float StrikeArm = 0f, // front-left limb: 0 wound back, 1 swung through
    // The chassis's lean while it lines up its lance: nosed down onto the target and
    // braced onto one side. Zero for every other phase, so the rig sits flat on its
    // legs exactly as it always has unless it is actually aiming.
    float BodyPitch = 0f,
    float BodyRoll = 0f);
