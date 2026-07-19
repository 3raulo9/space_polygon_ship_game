using System.Numerics;

namespace VoidTanks.Entities;

/// <summary>
/// A bolt travelling across the grid. Pooled and reused (Doc 05: keep per-frame
/// allocations near zero) — <see cref="Active"/> flags a live shot; dead ones
/// sit in the pool waiting to be respawned.
/// </summary>
public sealed class Projectile
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Life;          // seconds remaining before it fizzles
    public bool FromPlayer;     // so a shot can't hit its own owner
    public bool Active;

    // Height above the grid. A grounded shot rides at barrel height; a shot fired
    // mid-jump launches at the craft's leap height and sinks as it flies, so it
    // glides out level with you and finally comes down somewhere on the horizon.
    public float Height;
    private float _descentRate;  // units/sec the shot sinks (0 for a level shot)

    /// <summary>True for a shot fired while airborne — it rides high, sinks, and
    /// detonates on the horizon instead of fizzling silently.</summary>
    public bool IsAirShot;

    /// <summary>Set on the single frame the shot ends by running out of life or
    /// sinking to the floor (not by hitting something) — the caller reads this to
    /// stage the horizon explosion for an air shot.</summary>
    public bool JustExpired;

    // A grenade is the heavy Button-B round: slower, bigger, and it damages
    // everything inside a blast radius on contact instead of a single target.
    public bool IsGrenade;
    public float SplashRadius;

    private const float Speed = 90f;
    private const float GrenadeSpeed = 60f;   // heavier, so it lobs slower
    private const float MaxLife = 2.4f;
    public const float GrenadeSplash = 7f;    // blast radius in world units

    /// <summary>Barrel height of a grounded shot — where the bolt rides by default.</summary>
    public const float BoltHeight = 0.55f;

    // How long an air shot glides before it sinks to the floor far downrange. Paired
    // with the launch height, this fixes the descent rate so the shot always lands
    // out near the horizon regardless of how high the jump was.
    private const float AirGlideTime = 2.6f;

    public void Fire(Vector2 origin, Vector2 dir, bool fromPlayer, float launchHeight = BoltHeight)
    {
        Position = origin;
        Velocity = Vector2.Normalize(dir) * Speed;
        FromPlayer = fromPlayer;
        IsGrenade = false;
        SplashRadius = 0f;
        Height = launchHeight;
        JustExpired = false;

        // Anything launched appreciably above barrel height is an air shot: it holds
        // that height, sinks toward the floor over the glide, and lives long enough
        // to get there before fizzling.
        IsAirShot = launchHeight > BoltHeight + 0.5f;
        _descentRate = IsAirShot ? launchHeight / AirGlideTime : 0f;
        Life = IsAirShot ? AirGlideTime + 0.2f : MaxLife;
        Active = true;
    }

    /// <summary>Launches the heavy splash round (Button B).</summary>
    public void FireGrenade(Vector2 origin, Vector2 dir, bool fromPlayer)
    {
        Position = origin;
        Velocity = Vector2.Normalize(dir) * GrenadeSpeed;
        Life = MaxLife;
        FromPlayer = fromPlayer;
        IsGrenade = true;
        SplashRadius = GrenadeSplash;
        Height = 0.7f;          // the fatter slug rides a touch higher than a bolt
        _descentRate = 0f;
        IsAirShot = false;
        JustExpired = false;
        Active = true;
    }

    public void Update(float dt)
    {
        if (!Active) return;
        JustExpired = false;
        Position += Velocity * dt;
        if (_descentRate != 0f) Height -= _descentRate * dt;
        Life -= dt;
        if (Life <= 0f || (IsAirShot && Height <= 0f))
        {
            Active = false;
            JustExpired = true;   // ended on its own — stage the horizon blast
        }
    }
}
