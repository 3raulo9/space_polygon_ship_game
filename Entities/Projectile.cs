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

    private const float Speed = 90f;
    private const float MaxLife = 2.4f;

    public void Fire(Vector2 origin, Vector2 dir, bool fromPlayer)
    {
        Position = origin;
        Velocity = Vector2.Normalize(dir) * Speed;
        Life = MaxLife;
        FromPlayer = fromPlayer;
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
