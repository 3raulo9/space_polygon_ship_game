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

    // A grenade is the heavy Button-B round: slower, bigger, and it damages
    // everything inside a blast radius on contact instead of a single target.
    public bool IsGrenade;
    public float SplashRadius;

    private const float Speed = 90f;
    private const float GrenadeSpeed = 60f;   // heavier, so it lobs slower
    private const float MaxLife = 2.4f;
    public const float GrenadeSplash = 7f;    // blast radius in world units

    public void Fire(Vector2 origin, Vector2 dir, bool fromPlayer)
    {
        Position = origin;
        Velocity = Vector2.Normalize(dir) * Speed;
        Life = MaxLife;
        FromPlayer = fromPlayer;
        IsGrenade = false;
        SplashRadius = 0f;
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
        Active = true;
    }

    public void Update(float dt)
    {
        if (!Active) return;
        Position += Velocity * dt;
        Life -= dt;
        if (Life <= 0f) Active = false;
    }
}
