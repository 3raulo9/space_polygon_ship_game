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
    public enum State { Idle, ThreatDisplay, Clamping, Pursuit, Dying, Dead }

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

    /// <summary>True once the death glitch has fully played out and the rig is gone.</summary>
    public bool Dead => Phase == State.Dead;

    /// <summary>0 while alive, ramping 0→1 across the death glitch — the renderer
    /// reads this to fling the parts apart and tear the whole rig with static.</summary>
    public float DeathProgress => Phase == State.Dying
        ? Math.Clamp(_deathTime / DeathDuration, 0f, 1f)
        : (Phase == State.Dead ? 1f : 0f);

    /// <summary>0..1 remaining core integrity, for a HUD boss bar later if wanted.</summary>
    public float CoreFraction => Math.Clamp(_coreHealth / CoreMaxHealth, 0f, 1f);

    // --- Protocol tuning ------------------------------------------------------
    public const float DetectRadius = 45f;   // the player crosses this and it wakes
    public const float SlideSpeed = 16f;     // slow, mechanical strafe (units/sec)
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

    // Per-leg gait sign last tick, to catch the instant a foot plants (the +→-
    // zero-crossing of its lift sine), plus the world spots those plants happened.
    private readonly float[] _legSinLast = new float[CrabRig.Legs.Length];
    private readonly List<Vector2> _footfalls = new();

    /// <summary>World XZ points where a foot struck the floor this tick — the caller
    /// spawns dust there. Refilled every <see cref="Update"/>.</summary>
    public IReadOnlyList<Vector2> Footfalls => _footfalls;

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

        switch (Phase)
        {
            case State.Idle:        UpdateIdle(dt, playerPos); break;
            case State.ThreatDisplay: UpdateThreat(dt); break;
            case State.Clamping:    snapped = UpdateClamping(dt); break;
            case State.Pursuit:     UpdatePursuit(dt, playerPos); break;
            case State.Dying:       UpdateDying(dt); break;
            case State.Dead:        return false;   // gone; nothing left to do
        }

        // Core always turns; flash always cools.
        _coreSpin += CoreSpinRate * dt;
        if (_flash > 0f) _flash = MathF.Max(0f, _flash - dt * 4f);

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
                _footfalls.Add(CrabRig.FootWorldXZ(legs[i], Position, Heading));
            _legSinLast[i] = s;
        }
    }

    private float CoreSpinRate => Phase == State.Idle ? IdleSpin : AgitatedSpin;

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
        if (dist < 0.0001f) return;
        Vector2 dir = to / dist;

        Heading = MathF.Atan2(dir.X, dir.Y);        // locked onto the coordinates
        Position += dir * RunSpeed * dt;            // rigid, direct, no easing
    }

    private void Enter(State next)
    {
        Phase = next;
        _stateTime = 0f;
        _jawWasOpen = false;
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
        CoreColorFor(Hostility, _flash), Vector2.Zero);

    private float Hostility => Phase is State.Clamping or State.Pursuit ? 1f : 0f;

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
/// A frame's worth of Crab-Core animation state, produced by the entity (or the
/// test-screen loop) and consumed by the renderer. Purely visual — the real
/// position/heading live on the entity.
/// </summary>
public readonly record struct CrabPose(
    float CoreSpin,       // radians the gem has turned
    float ClawOpen,       // 0 = plates shut, 1 = plates yawned open
    float LegPhase,       // radians driving the skitter bob
    Color CoreColor,      // gem tint this frame (magenta..red..white flash)
    Vector2 SlideOffset); // showcase-only world drift; zero for the live boss
