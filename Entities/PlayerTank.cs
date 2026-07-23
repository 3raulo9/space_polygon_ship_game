using System.Numerics;
using VoidTanks.Core;
using VoidTanks.Input;

namespace VoidTanks.Entities;

/// <summary>
/// The player's craft. Movement is heavy and a half-second behind intent
/// (Doc 03): momentum on drive, inertia on turn, an awkward jump for dodging.
/// You are piloting a machine, not a cursor. No strafing.
/// </summary>
public sealed class PlayerTank
{
    // Position on the flat grid plane (X, Z). Y is height for the jump arc.
    public Vector2 Position;
    public float Height;          // 0 = grounded; rises during a jump
    public float Heading;         // radians; 0 faces +Z (into the screen)

    // Signed forward speed and angular velocity — carried between frames so
    // the craft eases into motion and coasts to a stop rather than snapping.
    private float _speed;
    private float _turnRate;
    private float _verticalVel;

    // --- Feel tuning (units per second) ---
    // Public so the Crab-Core boss can hardcode its pursuit run to exactly the
    // player's top walking speed (the Stalker Protocol's cruelty: it moves no
    // faster than you, so you can never simply out-walk it once it locks on).
    public const float MaxSpeed = 26f;
    private const float Accel = 34f;        // how hard the engine pushes
    private const float Drag = 22f;         // coast-down when no input
    private const float ReverseFactor = 0.55f;

    /// <summary>
    /// The hangar's speed track, as a multiplier on both top speed and how hard the
    /// engine pushes toward it. Scaling acceleration alongside the ceiling is the whole
    /// difference between a fast craft and a craft that eventually gets fast: leaving
    /// Accel alone would give a speed-10 build the same sluggish half-second of
    /// wind-up, which is exactly the feel the track is supposed to buy out of.
    ///
    /// Note the boss's pursuit speed keys off the <em>const</em> MaxSpeed above, not
    /// off this — the Stalker Protocol runs at the standard craft's walking pace, so a
    /// player who spent points on speed can genuinely outrun it and one who spent them
    /// elsewhere genuinely cannot. That asymmetry is the point of the track.
    /// </summary>
    private float _speedScale = 1f;

    public float TopSpeed => MaxSpeed * _speedScale;

    private const float MaxTurn = 2.1f;     // rad/s
    private const float TurnAccel = 6.5f;
    private const float TurnDrag = 8.0f;

    private const float JumpVel = 17f;      // initial upward kick — a taller leap still
    private const float Gravity = 18f;      // upward pull → the rise still slows crisply
    private const float FallGravity = 13f;  // gentler pull coming down → floats + hangs longer
    private const float JumpForwardDrift = 4f; // small forward glide while airborne — carries you a bit further to the front

    public bool IsAirborne => Height > 0.001f;

    /// <summary>
    /// How high the peak of a leap carries the craft, in world units. Solved from the
    /// jump's own kick and pull (v²/2g) rather than measured or guessed, because one
    /// enemy is built entirely around this number: the Maw-Core hovers with its
    /// crystal exactly at the top of a jump, so it can only be shot by a player at the
    /// apex of one. Deriving it here means retuning the arc moves the monster with it
    /// instead of quietly making it unhittable.
    /// </summary>
    public static float JumpApex => JumpVel * JumpVel / (2f * Gravity);

    // --- Combat state (Doc 03) ---
    public float MaxShield = 100f;
    public float Shield;
    public int Lives = 3;
    public int MaxAmmo = 50;
    public int Ammo = 40;
    public bool Alive => Shield > 0f || Lives > 0;

    /// <summary>Which chassis the hangar sent out. Drives which trigger does what —
    /// see <see cref="World.World.Update"/> — and nothing about the physics, which are
    /// the same heavy momentum whatever you are piloting.</summary>
    public PlayerClass Class { get; private set; } = PlayerClass.Tank;

    /// <summary>
    /// The SPIDER's emitter, or null on every other chassis. Held here rather than in
    /// the world because it is part of the craft: it cools on the craft's clock and it
    /// is what roots the craft while it charges.
    /// </summary>
    public SpiderWeapon? Spider { get; private set; }

    /// <summary>
    /// True while something is holding the craft in place but leaving it otherwise in
    /// control — currently only the SPIDER winding its lance. Distinct from
    /// <see cref="Captured"/>: a rooted craft still runs its own physics (so it coasts
    /// to a stop rather than stopping dead), still cools its guns, still falls if it was
    /// airborne. It simply stops accepting drive and turn input.
    /// </summary>
    public bool Rooted;

    // Hyper Engine: the reserve for tactical moves. Slowly refills on its own,
    // so jumping and hyperspacing are rationed, not free. Jump takes a quarter of
    // the bar; hyperspace drains almost all of it to panic-warp across the map.
    public float MaxHyper = 100f;
    public float Hyper;
    private const float HyperRegen = 6f;        // points/sec, a slow trickle
    private const float JumpHyperCost = 25f;    // ~25% of the bar
    private const float HyperspaceCost = 90f;   // a massive drain to teleport
    private const float TeleportRange = 90f;    // how far a warp can fling you

    // A single heavy grenade eats ten cannon rounds at once — a burst you pay
    // dearly for. Cooldown is longer than the cannon so it can't be spammed.
    private const int GrenadeAmmoCost = 10;
    private const float GrenadeInterval = 0.9f;

    // Ammo is a resource, not infinite: a cooldown plus finite rounds forces
    // restraint and makes panic-firing into the fog feel costly.
    private float _fireCooldown;
    private const float FireInterval = 0.35f;

    // Collision radius on the plane, shared by shots and tank-tank checks.
    public const float Radius = 1.3f;

    public PlayerTank(Vector2 start, float heading = 0f, Loadout? loadout = null)
    {
        Position = start;
        Heading = heading;

        // No loadout means the caller doesn't care (the headless self-test, the capture
        // harness): fall back to the standard chassis on a straight 5/5/5, which is
        // exactly the craft this game shipped with before the hangar existed.
        loadout ??= new Loadout();
        Class = loadout.Class;
        MaxShield = loadout.MaxShield;
        MaxAmmo = loadout.MaxAmmo;
        _speedScale = loadout.SpeedScale;
        // Open on four fifths of the magazine, as the craft always has.
        Ammo = (int)MathF.Round(MaxAmmo * 0.8f);
        if (Class == PlayerClass.Spider) Spider = new SpiderWeapon();

        Shield = MaxShield;
        Hyper = MaxHyper;
    }

    /// <summary>
    /// True while a cinematic owns the craft's transform — currently only the
    /// Crab-Core's seizure, which lifts the player off the grid, turns them to face
    /// the core and throws them. While it is set, <see cref="Update"/> refuses to
    /// drive: input is ignored and the physics are suspended, so the script can write
    /// <see cref="Position"/>, <see cref="Height"/> and <see cref="Heading"/> outright
    /// without the craft's own momentum fighting it for the same fields.
    ///
    /// Being airborne for the whole hold also means enemy fire passes through, which
    /// is the behaviour we want: a player who cannot act must not be shot at by
    /// anything else while the boss has them.
    /// </summary>
    public bool Captured;

    /// <summary>
    /// Drops all carried momentum — forward speed, turn rate and vertical velocity.
    /// A cinematic calls this on release so the craft comes back under control dead
    /// still, rather than resuming whatever it happened to be doing several seconds
    /// ago when it was grabbed.
    /// </summary>
    public void ResetMomentum()
    {
        _speed = 0f;
        _turnRate = 0f;
        _verticalVel = 0f;
    }

    public void Update(float dt)
    {
        // The cannon cools whatever else is happening to the craft — it is a property
        // of the weapon, not of who is driving. This has to sit *above* the capture
        // check: the Maw-Core's digestion is escaped by shooting your way out from
        // inside it, and a cooldown frozen for the duration of the hold would let the
        // player fire exactly once and then leave them locked out of the only exit
        // they have. Whether a trigger pull is honoured at all is the world's call
        // (see World.FirePlayerShot) — this only keeps the gun alive.
        if (_fireCooldown > 0f) _fireCooldown -= dt;
        // Same reasoning for the spider's emitter: its laser cools and its beam burns
        // out on the weapon's own clock, not on whether the craft is currently allowed
        // to drive.
        Spider?.Update(dt);

        // A cinematic has the wheel: it writes the transform itself this tick.
        if (Captured) return;

        UpdateTurn(dt);
        UpdateDrive(dt);
        UpdateJump(dt);

        // The Hyper reserve creeps back up when it isn't being spent.
        if (Hyper < MaxHyper)
            Hyper = MathF.Min(MaxHyper, Hyper + HyperRegen * dt);

        // Apply planar motion along the current heading (no strafe: motion is
        // always along the facing axis).
        var dir = new Vector2(MathF.Sin(Heading), MathF.Cos(Heading));
        Position += dir * _speed * dt;

        // The world is a torus: drive off one edge and you come back on the opposite
        // one. Fold the craft back into the wrap window every tick — the jump drift
        // above has already been applied, so this catches all planar motion. Skipped
        // while a cinematic has the wheel (it returns early above), so a set piece can
        // write positions off the grid without this fighting it.
        Position = Torus.Wrap(Position);
    }

    /// <summary>
    /// Attempts to fire: succeeds only if off cooldown and ammo remains. On
    /// success returns the muzzle origin and direction; otherwise false.
    /// </summary>
    public bool TryFire(out Vector2 origin, out Vector2 direction, out float launchHeight)
    {
        origin = default;
        direction = default;
        launchHeight = Projectile.BoltHeight;
        if (_fireCooldown > 0f || Ammo <= 0) return false;

        direction = Forward;
        origin = Position + direction * (Radius + 0.6f); // out past the nose
        // Fired mid-jump, the bolt leaves the barrel level with the leaping craft
        // and sails out at that height — the airborne shot that can reach the boss's
        // raised core.
        launchHeight = Projectile.BoltHeight + Height;
        _fireCooldown = FireInterval;
        Ammo--;
        return true;
    }

    /// <summary>
    /// Attempts a heavy grenade: needs the full 10-round ammo cost and the (shared)
    /// fire cooldown clear. On success returns the muzzle origin and direction and
    /// spends the ammo. The projectile itself carries the splash — this just launches.
    /// </summary>
    public bool TryFireGrenade(out Vector2 origin, out Vector2 direction)
    {
        origin = default;
        direction = default;
        if (_fireCooldown > 0f || Ammo < GrenadeAmmoCost) return false;

        direction = Forward;
        origin = Position + direction * (Radius + 0.6f);
        _fireCooldown = GrenadeInterval;
        Ammo -= GrenadeAmmoCost;
        return true;
    }

    /// <summary>
    /// Panic-warp: drains the bulk of the Hyper reserve and flings the craft to a
    /// random spot within range. Blocked (returns false) when the reserve is too
    /// low. A grounded craft only — you can't warp mid-jump.
    /// </summary>
    public bool TryHyperspace()
    {
        if (Hyper < HyperspaceCost || IsAirborne) return false;

        // Random bearing and distance — a genuine gamble, not a controlled blink.
        float angle = Random.Shared.NextSingle() * MathF.Tau;
        float dist = TeleportRange * (0.4f + 0.6f * Random.Shared.NextSingle());
        Position = Torus.Wrap(Position + new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * dist);

        Hyper -= HyperspaceCost;
        _speed = 0f;        // the warp kills momentum — you arrive dead-stopped
        _turnRate = 0f;
        return true;
    }

    /// <summary>
    /// Tops up the shield by a fraction of its maximum, capped at full — the
    /// battery pickup's repair charge. A fraction of 0.3 restores 30 points on the
    /// 100-point shield.
    /// </summary>
    public void RefillShield(float fraction)
        => Shield = MathF.Min(MaxShield, Shield + MaxShield * fraction);

    /// <summary>
    /// Tops up the Hyper reserve by a fraction of its maximum, capped at full — the
    /// battery pickup also recharges tactical moves (jump / hyperspace).
    /// </summary>
    public void RefillHyper(float fraction)
        => Hyper = MathF.Min(MaxHyper, Hyper + MaxHyper * fraction);

    /// <summary>
    /// Restocks ammo by a fraction of the magazine, capped at full — the floating
    /// bullet pickup. Rounded up so a 30% pull always yields whole rounds.
    /// </summary>
    public void RefillAmmo(float fraction)
        => Ammo = Math.Min(MaxAmmo, Ammo + (int)MathF.Ceiling(MaxAmmo * fraction));

    /// <summary>Applies incoming damage; spends a life and resets shield at zero.</summary>
    public void TakeDamage(float amount)
    {
        Shield -= amount;
        if (Shield <= 0f)
        {
            Shield = 0f;
            if (Lives > 0)
            {
                Lives--;
                if (Lives > 0) Shield = MaxShield; // respawn with a fresh shield
            }
        }
    }

    private void UpdateTurn(float dt)
    {
        // Note the sign: the camera renders +X on the *left* of the screen, so a
        // heading increase (toward +X) swings the view left. To make "turn left"
        // actually look left, TurnLeft must *increase* heading and TurnRight
        // decrease it — hence left is +1 here.
        float input = 0f;
        // Rooted (the spider winding its lance): no steering. The turn rate still
        // bleeds off below, so a craft that was mid-turn settles rather than locking
        // the instant the trigger goes down.
        if (!Rooted && InputMap.TurnLeft) input += 1f;
        if (!Rooted && InputMap.TurnRight) input -= 1f;

        if (input != 0f)
            _turnRate += input * TurnAccel * dt;
        else
            _turnRate = MoveToward(_turnRate, 0f, TurnDrag * dt);

        _turnRate = Math.Clamp(_turnRate, -MaxTurn, MaxTurn);
        Heading += _turnRate * dt;
    }

    private void UpdateDrive(float dt)
    {
        float input = 0f;
        if (!Rooted && InputMap.Forward) input += 1f;
        if (!Rooted && InputMap.Back) input -= 1f;

        if (input > 0f)
            _speed += Accel * _speedScale * dt;
        else if (input < 0f)
            _speed -= Accel * _speedScale * ReverseFactor * dt;
        else
            _speed = MoveToward(_speed, 0f, Drag * dt);

        float top = TopSpeed;
        _speed = Math.Clamp(_speed, -top * ReverseFactor, top);
    }

    private void UpdateJump(float dt)
    {
        // Jumping is a Hyper move now: it costs a quarter of the bar and is
        // simply refused if the reserve is too low to pay for it.
        // Rooted craft don't leap either — the lance is charged with both feet planted.
        if (!Rooted && InputMap.JumpPressed && !IsAirborne && Hyper >= JumpHyperCost)
        {
            _verticalVel = JumpVel;
            Hyper -= JumpHyperCost;
        }

        if (IsAirborne || _verticalVel > 0f)
        {
            // Ascend under a firm pull but fall under a gentler one, so the arc
            // hangs at its peak and drifts back down slowly rather than dropping.
            float g = _verticalVel > 0f ? Gravity : FallGravity;
            _verticalVel -= g * dt;
            Height += _verticalVel * dt;

            // A slight forward glide the whole time you're off the ground carries
            // the craft a small bit further to the front than a dead-vertical hop.
            Position += Forward * JumpForwardDrift * dt;

            if (Height <= 0f)
            {
                Height = 0f;
                _verticalVel = 0f;
            }
        }
    }

    /// <summary>Normalized forward vector on the plane (X, Z).</summary>
    public Vector2 Forward => new(MathF.Sin(Heading), MathF.Cos(Heading));

    /// <summary>0..1 speed fraction — drives engine-hum pitch later (Doc 04).</summary>
    public float SpeedFraction => Math.Abs(_speed) / TopSpeed;

    // --- 0..1 fractions for the HUD bars ---
    public float ShieldFraction => MaxShield > 0f ? Math.Clamp(Shield / MaxShield, 0f, 1f) : 0f;
    public float AmmoFraction => MaxAmmo > 0 ? Math.Clamp((float)Ammo / MaxAmmo, 0f, 1f) : 0f;
    public float HyperFraction => MaxHyper > 0f ? Math.Clamp(Hyper / MaxHyper, 0f, 1f) : 0f;

    private static float MoveToward(float value, float target, float maxDelta)
    {
        if (Math.Abs(target - value) <= maxDelta) return target;
        return value + Math.Sign(target - value) * maxDelta;
    }
}
