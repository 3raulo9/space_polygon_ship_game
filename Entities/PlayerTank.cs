using System.Numerics;
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
    private const float MaxSpeed = 26f;
    private const float Accel = 34f;        // how hard the engine pushes
    private const float Drag = 22f;         // coast-down when no input
    private const float ReverseFactor = 0.55f;

    private const float MaxTurn = 2.1f;     // rad/s
    private const float TurnAccel = 6.5f;
    private const float TurnDrag = 8.0f;

    private const float JumpVel = 11f;      // initial upward kick
    private const float Gravity = 30f;

    public bool IsAirborne => Height > 0.001f;

    // --- Combat state (Doc 03) ---
    public float MaxShield = 100f;
    public float Shield;
    public int Lives = 3;
    public int Ammo = 40;
    public bool Alive => Shield > 0f || Lives > 0;

    // Ammo is a resource, not infinite: a cooldown plus finite rounds forces
    // restraint and makes panic-firing into the fog feel costly.
    private float _fireCooldown;
    private const float FireInterval = 0.35f;

    // Collision radius on the plane, shared by shots and tank-tank checks.
    public const float Radius = 1.3f;

    public PlayerTank(Vector2 start, float heading = 0f)
    {
        Position = start;
        Heading = heading;
        Shield = MaxShield;
    }

    public void Update(float dt)
    {
        UpdateTurn(dt);
        UpdateDrive(dt);
        UpdateJump(dt);

        if (_fireCooldown > 0f) _fireCooldown -= dt;

        // Apply planar motion along the current heading (no strafe: motion is
        // always along the facing axis).
        var dir = new Vector2(MathF.Sin(Heading), MathF.Cos(Heading));
        Position += dir * _speed * dt;
    }

    /// <summary>
    /// Attempts to fire: succeeds only if off cooldown and ammo remains. On
    /// success returns the muzzle origin and direction; otherwise false.
    /// </summary>
    public bool TryFire(out Vector2 origin, out Vector2 direction)
    {
        origin = default;
        direction = default;
        if (_fireCooldown > 0f || Ammo <= 0) return false;

        direction = Forward;
        origin = Position + direction * (Radius + 0.6f); // out past the nose
        _fireCooldown = FireInterval;
        Ammo--;
        return true;
    }

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
        if (InputMap.TurnLeft) input += 1f;
        if (InputMap.TurnRight) input -= 1f;

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
        if (InputMap.Forward) input += 1f;
        if (InputMap.Back) input -= 1f;

        if (input > 0f)
            _speed += Accel * dt;
        else if (input < 0f)
            _speed -= Accel * ReverseFactor * dt;
        else
            _speed = MoveToward(_speed, 0f, Drag * dt);

        _speed = Math.Clamp(_speed, -MaxSpeed * ReverseFactor, MaxSpeed);
    }

    private void UpdateJump(float dt)
    {
        if (InputMap.JumpPressed && !IsAirborne)
            _verticalVel = JumpVel;

        if (IsAirborne || _verticalVel > 0f)
        {
            _verticalVel -= Gravity * dt;
            Height += _verticalVel * dt;
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
    public float SpeedFraction => Math.Abs(_speed) / MaxSpeed;

    private static float MoveToward(float value, float target, float maxDelta)
    {
        if (Math.Abs(target - value) <= maxDelta) return target;
        return value + Math.Sign(target - value) * maxDelta;
    }
}
