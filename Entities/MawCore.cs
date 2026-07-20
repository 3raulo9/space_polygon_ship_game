using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;

namespace VoidTanks.Entities;

/// <summary>
/// VECTOR.MAW-CORE — the Hanging Mouth. A Crab-Core with its middle torn out: the
/// carapace and the spinning crystal are the same parts the Stalker wears, but the
/// tier that carried them and the six legs that walked it are simply gone. What is
/// left hovers, and where its body used to bolt on there is a throat — a ring of
/// bone teeth turning in two directions at once, leaking a slow black drool onto
/// the grid beneath it.
///
/// Its whole design is one rule: <em>it hangs at the top of your jump</em>. See
/// <see cref="MawRig.CrystalWorldY"/> — the crystal sits exactly where a bolt fired
/// at the apex of a leap travels, so every shot from the ground sails under it and
/// the only way to hurt the thing is to be in the air. That inverts what the jump
/// has meant everywhere else in the game, where it is the dodge; here it is the only
/// offence, and taking it puts you off the ground for a second at a time.
///
/// The protocol:
///   Hover   — drifts overhead, tracks the player, spits slow little lasers at them.
///   Lunge   — the player stands directly underneath for a beat: it comes straight
///             down on them, and on contact the digestion begins.
///   Digest  — <see cref="MawDigestion"/> owns the scene; the rig hangs still, jaw
///             clamped, and grinds. The way out is to shoot it from the inside.
///   Spent   — jaw sprung open, reeling, for as long as it takes to recover.
///
/// The class owns the simulation and hands the renderer a <see cref="MawPose"/> each
/// frame, exactly the way <see cref="CrabCore"/> does.
/// </summary>
public sealed class MawCore
{
    public enum State { Hover, Lunge, Digest, Spent, Dying, Dead }

    /// <summary>Planar position. Its height is fixed by the rig except during a
    /// lunge, which is what <see cref="Drop"/> tracks.</summary>
    public Vector2 Position;

    public State Phase { get; private set; } = State.Hover;

    /// <summary>How far the body has come down off its hover height, in world units.
    /// Zero except while it is dropping onto the player or climbing back up.</summary>
    public float Drop { get; private set; }

    // --- The crystal ----------------------------------------------------------
    // Same bargain as the Crab-Core: only the exposed gem takes damage. Here the
    // reach is vertical rather than positional — the shell, the jaw and the teeth are
    // all inert, and a shot has to arrive at apex height to reach the crystal at all.

    public const float CrystalMaxHealth = 5f;
    private float _health = CrystalMaxHealth;

    private float _deathTime;
    private const float DeathDuration = 1.15f;

    public bool Alive => Phase is not (State.Dying or State.Dead);
    public bool Dead => Phase == State.Dead;

    /// <summary>0..1 remaining crystal integrity, for the HUD and the hit cues.</summary>
    public float CrystalFraction => Math.Clamp(_health / CrystalMaxHealth, 0f, 1f);

    /// <summary>0 while alive, ramping 0→1 across the death glitch.</summary>
    public float DeathProgress => Phase == State.Dying
        ? Math.Clamp(_deathTime / DeathDuration, 0f, 1f)
        : (Phase == State.Dead ? 1f : 0f);

    // --- Tuning ---------------------------------------------------------------

    /// <summary>It notices the player at this range and starts drifting onto them.</summary>
    public const float DetectRadius = 52f;

    /// <summary>Drift past this and it loses interest and hangs where it is.</summary>
    public const float GiveUpRadius = 85f;

    /// <summary>
    /// How fast it slides across the sky. Well under the player's walking speed —
    /// like the Crab-Core it cannot run anybody down, and unlike the Crab-Core it does
    /// not need to: it only has to be overhead, and the player has to come to a stop
    /// under it to be caught at all.
    /// </summary>
    public const float DriftSpeed = PlayerTank.MaxSpeed * 0.34f;

    /// <summary>How long the player has to stand in the strike column before it
    /// commits. The whole tell of the attack: it is enough time to feel the teeth
    /// start turning overhead and step out from under them.</summary>
    public const float LungeWindup = 0.65f;

    /// <summary>How fast it comes down, and how fast it hauls itself back up. The
    /// drop is violent and the climb is laboured — it is carrying someone.</summary>
    public const float DiveSpeed = 26f;
    public const float RiseSpeed = 7f;

    /// <summary>How long it hangs open and reeling after losing a meal, before it can
    /// hunt again. Long enough that escaping is worth something.</summary>
    private const float SpentTime = 3.4f;

    // --- Live animation --------------------------------------------------------
    private float _stateTime;
    private float _crystalSpin;
    private float _toothSpin;      // outer ring
    private float _toothSpinInner; // inner ring, turning the other way
    private float _bob;            // the hover's slow heave
    private float _jawOpen = 1f;   // 1 wide open, 0 clamped shut
    private float _flash;          // white-hot pop on a crystal hit, decays
    private float _columnTime;     // how long the player has been directly beneath
    private float _laserCooldown;
    private float _dripTimer;
    private float _teethAudioTimer;
    private readonly Random _rng = new();

    // Crystal spin rates: idle turn, agitated once it has seen you, and a hard wind-up
    // while it is grinding — the spin is how the thing shows effort.
    private const float IdleSpin = 1.4f;
    private const float HuntSpin = 5.5f;
    private const float DigestSpin = 12f;

    // The teeth turn far faster than the crystal, and the two rings turn opposite
    // ways. Counter-rotation is the entire effect: one ring spinning is a machine
    // part, two grinding against each other is something you do not want to be near.
    private const float ToothRate = 3.2f;
    private const float ToothRateInner = -4.6f;
    private const float ToothDigestRate = 11f;

    public MawCore(Vector2 start)
    {
        Position = start;
        // Rolled so two of them on one field are never in phase with each other.
        _crystalSpin = (float)_rng.NextDouble() * MathF.Tau;
        _toothSpin = (float)_rng.NextDouble() * MathF.Tau;
        _bob = (float)_rng.NextDouble() * MathF.Tau;
    }

    // --- The little lasers ----------------------------------------------------
    // Its own pool rather than the world's projectile ring. These are a different
    // kind of shot — they start high, travel in 3D toward where the player's eye is,
    // and crawl — and threading that through a pooled ground-plane bolt would mean
    // teaching every other shot in the game about height and slowness for the sake of
    // one enemy.

    /// <summary>One drifting acid-green bolt.</summary>
    public struct Laser
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Life;
        public readonly bool Active => Life > 0f;
    }

    private const int MaxLasers = 12;
    private readonly Laser[] _lasers = new Laser[MaxLasers];

    /// <summary>The live pool for the renderer and the world's damage check to walk;
    /// skip entries where !Active.</summary>
    public Laser[] Lasers => _lasers;

    /// <summary>
    /// How fast a laser travels. Deliberately slow enough to watch coming and walk
    /// out of — it is a pressure weapon, not a hitscan. Standing still under this
    /// thing should be what kills you, and the lasers exist to make standing still
    /// expensive while the mouth does the real work.
    /// </summary>
    public const float LaserSpeed = 15f;

    /// <summary>How close a laser has to pass to count as a hit.</summary>
    public const float LaserRadius = 0.9f;

    private const float LaserLife = 4.5f;
    private const float LaserGapMin = 1.15f;
    private const float LaserGapMax = 2.3f;

    // --- The drool ------------------------------------------------------------
    // Beads of black polygon that let go of the teeth and fall to the grid. Cosmetic
    // only, and owned here rather than by the debris system because they have to keep
    // falling from a moving body whether or not anything has died.

    /// <summary>One bead of ichor falling off the teeth.</summary>
    public struct Drip
    {
        public Vector3 Position;
        public float Fall;      // downward speed, units/sec
        public float Spin;
        public float Life;
        public float MaxLife;
        public readonly bool Active => Life > 0f;
        /// <summary>1 at birth, 0 at death — the renderer fades and stretches on it.</summary>
        public readonly float LifeFrac => MaxLife > 0f ? Life / MaxLife : 0f;
    }

    private const int MaxDrips = 18;
    private readonly Drip[] _drips = new Drip[MaxDrips];

    /// <summary>The live drool for the renderer to walk; skip entries where !Active.</summary>
    public Drip[] Drips => _drips;

    private const float DripGapMin = 0.25f;
    private const float DripGapMax = 0.8f;

    // --- What the world reads -------------------------------------------------

    /// <summary>The body's world height this frame, hover minus whatever it has
    /// dropped. Everything hanging off the rig is placed from this.</summary>
    public float BodyY => MawRig.BodyWorldY - Drop + Bob;

    /// <summary>The hover's slow vertical heave. Small, unhurried and never still —
    /// a thing holding itself up rather than a model parked at a height.</summary>
    public float Bob => MathF.Sin(_bob) * 0.55f;

    /// <summary>Where the lasers leave it: the rim of the jaw, so they are visibly
    /// spat out of the mouth rather than appearing beside the shell.</summary>
    public Vector3 MuzzleWorld => new(Position.X, BodyY + MawRig.ToothLocalY * MawRig.Scale, Position.Y);

    /// <summary>0 while it drifts unaware, 1 once it is working — drives the hover
    /// bed's pitch, the way the crab's agitation drives its rotor.</summary>
    public float Agitation => Phase switch
    {
        State.Hover => _aware ? 0.55f : 0f,
        State.Lunge or State.Digest => 1f,
        State.Spent => 0.3f,
        _ => 0f,
    };

    private bool _aware;

    /// <summary>True while it has the player in its throat. The rig freezes: it has
    /// what it came for.</summary>
    public bool Digesting => Phase == State.Digest;

    /// <summary>True on the tick the descending body reaches the player — the world
    /// turns this into the start of the digestion.</summary>
    public bool JustCaught { get; private set; }

    /// <summary>The visual snapshot the renderer poses from.</summary>
    public MawPose Pose => new(
        _crystalSpin, _toothSpin, _toothSpinInner, _jawOpen,
        CrystalColorFor(Hostility, MathF.Max(_flash, _digestGlow)));

    private float Hostility => Phase is State.Lunge or State.Digest ? 1f : (_aware ? 0.5f : 0f);

    private float _digestGlow;

    /// <summary>
    /// The crystal's colour for a hostility (0 calm magenta, 1 feeding red) with a
    /// white flash mixed over it. Split out so the live brain and the bestiary's
    /// turntable tint the gem identically — the same arrangement the crab uses.
    /// </summary>
    public static Color CrystalColorFor(float hostility, float flash)
    {
        Color baseC = LerpColor(Palette.NeonMagenta, Palette.NeonRed, Math.Clamp(hostility, 0f, 1f));
        return LerpColor(baseC, Color.White, Math.Clamp(flash, 0f, 1f) * 0.75f);
    }

    // --- The tick -------------------------------------------------------------

    /// <summary>
    /// Advances the monster one step against the player. Returns true on the exact
    /// tick it spits a laser, so the caller can voice it.
    /// </summary>
    public bool Update(float dt, Vector2 playerPos, float playerHeight)
    {
        _stateTime += dt;
        JustCaught = false;

        // Drift onto the player across the seam the short way: reason about their
        // nearest image on the torus so a mouth by the world's edge hunts a player just
        // over it. Its own position is folded back into the wrap window below.
        playerPos = Torus.NearestImage(playerPos, Position);

        // The crystal and the teeth turn no matter what the brain is doing — right
        // through the death glitch, where the rig is coming apart around them.
        _crystalSpin += CrystalSpinRate * dt;
        float toothRate = Phase == State.Digest ? ToothDigestRate : ToothRate;
        _toothSpin += toothRate * dt;
        _toothSpinInner += (Phase == State.Digest ? -ToothDigestRate * 0.8f : ToothRateInner) * dt;
        _bob += dt * 1.35f;
        if (_flash > 0f) _flash = MathF.Max(0f, _flash - dt * 4f);

        UpdateDrips(dt);
        UpdateLasers(dt);

        bool spat = false;
        switch (Phase)
        {
            case State.Hover: spat = UpdateHover(dt, playerPos, playerHeight); break;
            case State.Lunge: UpdateLunge(dt, playerPos); break;
            case State.Digest: UpdateDigest(dt); break;
            case State.Spent: UpdateSpent(dt); break;
            case State.Dying: UpdateDying(dt); break;
            case State.Dead: return false;
        }

        // Fold its drift back into the wrap window; the renderer re-images it near the
        // player, and everything hanging off it is placed from this same Position.
        Position = Torus.Wrap(Position);
        return spat;
    }

    private float CrystalSpinRate => Phase switch
    {
        State.Digest => DigestSpin,
        State.Lunge => HuntSpin * 1.6f,
        State.Hover => _aware ? HuntSpin : IdleSpin,
        _ => IdleSpin,
    };

    // --- Hover: drift onto the player, spit, and wait for them to stand still ---

    private bool UpdateHover(float dt, Vector2 playerPos, float playerHeight)
    {
        // The jaw eases back open after a meal or a lunge that missed.
        _jawOpen = Approach(_jawOpen, 1f, dt * 1.4f);

        Vector2 to = playerPos - Position;
        float dist = to.Length();

        if (!_aware)
        {
            if (dist > DetectRadius) return false;
            _aware = true;
        }
        else if (dist > GiveUpRadius)
        {
            // Lost them. It simply stops — a thing that hangs does not go home.
            _aware = false;
            _columnTime = 0f;
            return false;
        }

        // Slides toward being directly overhead. It never turns to face anything:
        // a mouth on the end of a hover has no front, and the lack of a facing is
        // part of why it reads as wrong.
        if (dist > 0.001f)
        {
            float step = MathF.Min(DriftSpeed * dt, dist);
            Position += to / dist * step;
        }

        bool spat = UpdateSpit(dt, playerPos, playerHeight);

        // Directly beneath, on the grid, and staying there. A player in the air is
        // spared — they are busy doing the one thing that can hurt it, and swallowing
        // them mid-leap would punish exactly the behaviour the fight is teaching.
        bool beneath = dist <= MawRig.StrikeColumn && playerHeight < 0.5f;
        _columnTime = beneath ? _columnTime + dt : 0f;

        if (_columnTime >= LungeWindup)
        {
            _columnTime = 0f;
            Enter(State.Lunge);
            Audio.PlayMawDive();
        }
        return spat;
    }

    /// <summary>
    /// The little lasers. Aimed at where the player's eye is right now and then
    /// forgotten — they do not steer. At <see cref="LaserSpeed"/> a bolt takes a
    /// couple of seconds to cross the gap, which is the point: it is aimed at where
    /// you were, so it only lands on a player who has not moved since.
    /// </summary>
    private bool UpdateSpit(float dt, Vector2 playerPos, float playerHeight)
    {
        _laserCooldown -= dt;
        if (_laserCooldown > 0f) return false;
        _laserCooldown = LaserGapMin + (float)_rng.NextDouble() * (LaserGapMax - LaserGapMin);

        int slot = -1;
        for (int i = 0; i < _lasers.Length; i++)
            if (!_lasers[i].Active) { slot = i; break; }
        if (slot < 0) return false;

        Vector3 from = MuzzleWorld;
        var at = new Vector3(playerPos.X, playerHeight + Config.CameraHeight * 0.6f, playerPos.Y);
        Vector3 dir = at - from;
        if (dir.LengthSquared() < 0.0001f) return false;
        dir = Vector3.Normalize(dir);

        _lasers[slot] = new Laser
        {
            Position = from,
            Velocity = dir * LaserSpeed,
            Life = LaserLife,
        };
        return true;
    }

    private void UpdateLasers(float dt)
    {
        for (int i = 0; i < _lasers.Length; i++)
        {
            ref Laser l = ref _lasers[i];
            if (!l.Active) continue;
            l.Life -= dt;
            l.Position += l.Velocity * dt;
            if (l.Position.Y <= 0f) l.Life = 0f;   // spent on the grid
        }
    }

    /// <summary>Kills a laser — the world calls this on the one that hit the player,
    /// so a single bolt cannot bite twice.</summary>
    public void ConsumeLaser(int index)
    {
        if (index >= 0 && index < _lasers.Length) _lasers[index].Life = 0f;
    }

    // --- Lunge: straight down onto whatever is underneath ---------------------

    private void UpdateLunge(float dt, Vector2 playerPos)
    {
        _jawOpen = Approach(_jawOpen, 1f, dt * 4f);     // gaping on the way down
        Drop += DiveSpeed * dt;

        // It does not steer on the way down: it committed to a column, and if the
        // player has left it the thing hits empty grid. That is the escape, and it is
        // spatial rather than reactive — the same bargain the Crab-Core's lance makes.
        bool overThem = Vector2.DistanceSquared(playerPos, Position)
                        <= MawRig.StrikeColumn * MawRig.StrikeColumn;

        if (overThem && BodyY <= CatchBodyY)
        {
            JustCaught = true;
            Enter(State.Digest);
            return;
        }

        // Bottomed out on nothing: it hangs there with its jaw still gaping, which is
        // the tell that it missed, then hauls itself back up.
        if (Drop >= DiveDepth)
        {
            Drop = DiveDepth;
            Enter(State.Spent);
        }
    }

    /// <summary>
    /// The body height at which the throat has come down over a craft standing on the
    /// grid. Below the player's eye, so the last thing the view sees before the
    /// cinematic takes it is the lip passing them.
    /// </summary>
    public static float CatchBodyY => 2.6f;

    /// <summary>How far a lunge travels. A little past the catch line, so a lunge that
    /// misses visibly overshoots and buries its teeth in empty grid rather than
    /// stopping exactly where it would have caught someone.</summary>
    public static float DiveDepth => MawRig.BodyWorldY - CatchBodyY + 0.9f;

    // --- Digest: it has you, and it is grinding -------------------------------

    private void UpdateDigest(float dt)
    {
        // Clamped shut and hauling itself back up to its hover with the player inside.
        // The climb is laboured — RiseSpeed is a quarter of the dive — and it is what
        // makes the trap frightening rather than merely damaging: the ground drops away
        // underneath someone who has already lost control of their craft, so escaping
        // late means falling a long way. The cinematic parks the player relative to
        // this height, so they simply come up with it.
        _jawOpen = Approach(_jawOpen, 0f, dt * 6f);
        Drop = Approach(Drop, 0f, RiseSpeed * dt);
        _digestGlow = 0.35f + 0.35f * (0.5f + 0.5f * MathF.Sin(_stateTime * 6.5f));

        // The teeth are audible the whole time, on their own cadence — this is the
        // sound of being chewed, and it has to keep arriving rather than play once.
        _teethAudioTimer -= dt;
        if (_teethAudioTimer <= 0f)
        {
            _teethAudioTimer = 0.42f;
            Audio.PlayMawTeeth(0f, grinding: true);
        }
    }

    /// <summary>
    /// The digestion has ended — either the player shot their way out or the crystal
    /// died mid-meal. It springs open, spits them clear and hangs there recovering.
    /// </summary>
    public void ReleasePrey()
    {
        _digestGlow = 0f;
        if (Phase == State.Digest) Enter(State.Spent);
    }

    // --- Spent: jaw sprung open, hauling itself back up -----------------------

    private void UpdateSpent(float dt)
    {
        _jawOpen = Approach(_jawOpen, 1f, dt * 3f);
        Drop = Approach(Drop, 0f, RiseSpeed * dt);

        if (_stateTime >= SpentTime && Drop <= 0.001f)
        {
            Drop = 0f;
            Enter(State.Hover);
        }
    }

    private void UpdateDying(float dt)
    {
        // Nothing holds it up any more: it sinks as it comes apart, which is most of
        // what makes killing a floating thing satisfying — you watch it stop floating.
        _deathTime += dt;
        Drop += DiveSpeed * 0.4f * dt;
        if (_deathTime >= DeathDuration) Phase = State.Dead;
    }

    // --- Damage ---------------------------------------------------------------

    /// <summary>
    /// True when a shot's world position falls inside the crystal's strike band. The
    /// vertical test comes first and does the real work: a grounded bolt rides some
    /// six units under the band and can never satisfy it, whatever its aim.
    /// </summary>
    public bool HitsCrystal(Vector2 shotXZ, float shotHeight)
    {
        if (!Alive) return false;
        float band = BodyY + MawRig.CrystalLocalY * MawRig.Scale;
        if (MathF.Abs(shotHeight - band) > MawRig.HitVertical) return false;
        return Torus.DistanceSquared(shotXZ, Position) <= MawRig.HitRadius * MawRig.HitRadius;
    }

    /// <summary>
    /// Deals a hit to the crystal and flares it. Returns true on the hit that spends
    /// the last of it, so the caller can stage the death.
    /// </summary>
    public bool DamageCrystal(float amount)
    {
        if (!Alive) return false;
        _health -= amount;
        _flash = 1f;
        if (_health <= 0f)
        {
            _health = 0f;
            Enter(State.Dying);
            _deathTime = 0f;
            return true;
        }
        return false;
    }

    // --- Drool ----------------------------------------------------------------

    private void UpdateDrips(float dt)
    {
        for (int i = 0; i < _drips.Length; i++)
        {
            ref Drip d = ref _drips[i];
            if (!d.Active) continue;
            d.Life -= dt;
            d.Fall += 16f * dt;                 // it accelerates: it is falling, not floating
            d.Position.Y -= d.Fall * dt;
            if (d.Position.Y <= 0f) d.Life = 0f;
        }

        if (Phase is State.Dead) return;

        // A new bead lets go on an irregular cadence — an even drip reads as a
        // metronome, and the whole point of the drool is that it is organic.
        _dripTimer -= dt;
        if (_dripTimer > 0f) return;
        _dripTimer = DripGapMin + (float)_rng.NextDouble() * (DripGapMax - DripGapMin);
        // It runs harder while it is feeding.
        if (Phase == State.Digest) _dripTimer *= 0.4f;

        int slot = -1;
        for (int i = 0; i < _drips.Length; i++)
            if (!_drips[i].Active) { slot = i; break; }
        if (slot < 0) return;

        // Off the tip of a tooth, wherever that tooth has turned to — so the drool
        // visibly comes off the rotating ring rather than out of the body's centre.
        int tooth = _rng.Next(MawRig.ToothCount);
        float a = MawRig.ToothAngle(tooth, _toothSpin);
        float r = MawRig.ToothRadius * MawRig.Scale * 0.85f;

        _drips[slot] = new Drip
        {
            Position = new Vector3(
                Position.X + MathF.Cos(a) * r,
                BodyY + MawRig.ToothLocalY * MawRig.Scale - 1.1f * MawRig.Scale,
                Position.Y + MathF.Sin(a) * r),
            Fall = 0.5f + (float)_rng.NextDouble() * 1.5f,
            Spin = ((float)_rng.NextDouble() - 0.5f) * 4f,
            MaxLife = 2.2f,
        };
        _drips[slot].Life = _drips[slot].MaxLife;
        Audio.PlayMawDrip();
    }

    // --- Helpers --------------------------------------------------------------

    private void Enter(State next)
    {
        Phase = next;
        _stateTime = 0f;
    }

    private static float Approach(float v, float target, float step)
        => v < target ? MathF.Min(target, v + step) : MathF.Max(target, v - step);

    private static Color LerpColor(Color a, Color b, float f)
    {
        f = Math.Clamp(f, 0f, 1f);
        return new Color(
            (int)(a.R + (b.R - a.R) * f),
            (int)(a.G + (b.G - a.G) * f),
            (int)(a.B + (b.B - a.B) * f),
            255);
    }

    /// <summary>
    /// A looping pose for the bestiary turntable — no player, no brain, just the
    /// crystal and the two tooth rings turning so the grind can be eyeballed.
    /// <paramref name="t"/> is free-running seconds.
    /// </summary>
    public static MawPose ShowcasePose(float t)
        => new(t * HuntSpin, t * ToothRate, t * ToothRateInner, 1f,
               CrystalColorFor(0.5f, 0f));
}

/// <summary>
/// A frame's worth of Maw-Core animation, produced by the entity (or the bestiary's
/// loop) and consumed by the renderer. Purely visual — the position and the drop
/// live on the entity.
/// </summary>
public readonly record struct MawPose(
    float CrystalSpin,   // radians the gem has turned
    float ToothSpin,     // outer tooth ring's bearing
    float ToothSpinInner,// inner ring, turning against it
    float JawOpen,       // 1 gaping, 0 clamped around something
    Color CrystalColor); // gem tint this frame
