using System.Numerics;
using VoidTanks.Core;

namespace VoidTanks.Entities;

/// <summary>
/// A hunter. Cold and impersonal — no personality, no sound of its own beyond
/// movement and fire (Doc 03). Its brain is a small loop: acquire the player,
/// pursue and reposition, fire on line of sight. Movement is tuned to feel
/// purposeful but *slightly too smooth* — not human, not cartoonishly robotic,
/// just off. Elites reuse this same brain with higher speed/shield and the cone
/// mesh.
/// </summary>
public sealed class EnemyTank
{
    public Vector2 Position;
    public float Heading;         // radians; 0 faces +Z
    public float Shield;
    public bool IsElite;
    public bool Alive => Shield > 0f;

    private readonly float _moveSpeed;
    private readonly float _turnSpeed;
    private readonly float _preferredRange;   // hangs at this distance, not point-blank
    private float _fireCooldown;
    private readonly float _fireInterval;

    // Visual + collision size are one and the same: the renderer scales the mesh
    // by <see cref="Scale"/>, and the hitbox scales with it, so what you see is
    // what you can hit. Change this one number to resize the whole enemy.
    public const float Scale = 1.6f;
    private const float BaseRadius = 1.3f;   // hitbox on the unscaled mesh
    public const float Radius = BaseRadius * Scale;

    /// <summary>How tall the hull stands, in world units — the mesh's pyramid apex
    /// carried through the same <see cref="Scale"/>. Only anything that has to care
    /// about height reads it (the SPIDER's lance, which passes over a grounded tank
    /// when it is loosed from the top of a jump); the ordinary bolt-vs-tank test is
    /// still a flat planar one.</summary>
    public const float BodyHeight = 2.15f * Scale;

    /// <summary>
    /// How high up the player's column a hunter aims — the craft's body centre above its
    /// feet, so a shot arrives at the middle of the hull rather than at its ankles. Shared
    /// with the world's hit test (see <c>World.EnemyHitVertical</c>) so the height the
    /// enemy elevates to and the height the world scores a hit at are the same number, the
    /// way the Maw-Core's crystal geometry is shared between aiming and hitting.
    /// </summary>
    public const float AimHeight = 1.4f;

    public EnemyTank(Vector2 start, bool elite, int shieldBonus = 0)
    {
        Position = start;
        IsElite = elite;

        if (elite)
        {
            Shield = 5f + shieldBonus;
            _moveSpeed = 20f;
            _turnSpeed = 1.9f;
            _preferredRange = 34f;
            _fireInterval = 1.4f;
        }
        else
        {
            Shield = 3f + shieldBonus;
            _moveSpeed = 14f;
            _turnSpeed = 1.5f;
            _preferredRange = 40f;
            _fireInterval = 1.9f;
        }
        // Desync initial cooldowns so a group never fires in lockstep.
        _fireCooldown = _fireInterval * (0.4f + 0.6f * Random.Shared.NextSingle());
    }

    /// <summary>
    /// Steps the AI. Returns true (with a firing solution) when it looses a shot this
    /// tick, so the caller can spawn the projectile from the enemy pool.
    /// <paramref name="playerHeight"/> is how far off the grid the player currently is;
    /// the hunter elevates its shot to reach a leaping or hanging craft rather than firing
    /// dead flat and passing underneath. <paramref name="firePitch"/> is that elevation in
    /// radians, positive up.
    /// </summary>
    public bool Update(float dt, Vector2 playerPos, float playerHeight,
        out Vector2 fireOrigin, out Vector2 fireDir, out float firePitch)
    {
        fireOrigin = default;
        fireDir = default;
        firePitch = 0f;

        // Chase across the seam the short way: work against the player's nearest image
        // on the torus, not their raw coordinates, so a hunter by the world's edge homes
        // in on a player just over it instead of driving the long way round the arena.
        playerPos = Torus.NearestImage(playerPos, Position);

        Vector2 toPlayer = playerPos - Position;
        float dist = toPlayer.Length();
        if (dist < 0.001f) return false;

        Vector2 dirToPlayer = toPlayer / dist;
        float desiredHeading = MathF.Atan2(dirToPlayer.X, dirToPlayer.Y);

        // Turn toward the player smoothly — the "slightly too smooth" tell: it
        // never overshoots, never jitters, just glides its aim onto you.
        Heading = TurnToward(Heading, desiredHeading, _turnSpeed * dt);

        // Hold a preferred stand-off range: close if far, back off if too near.
        // Motion is deliberate, never frantic.
        float advance;
        if (dist > _preferredRange + 4f) advance = 1f;
        else if (dist < _preferredRange - 8f) advance = -0.6f;
        else advance = 0f;

        // Only drive along facing (no strafe for enemies either), so it arcs
        // toward its stand-off point as it turns to face you.
        var facing = new Vector2(MathF.Sin(Heading), MathF.Cos(Heading));
        Position += facing * (_moveSpeed * advance) * dt;
        Position = Torus.Wrap(Position);

        // Fire when roughly lined up and off cooldown — line-of-sight is trivial
        // on the open plane, so "aimed" stands in for "can see you".
        _fireCooldown -= dt;
        float aimError = MathF.Abs(AngleDelta(Heading, desiredHeading));
        if (_fireCooldown <= 0f && aimError < 0.15f && dist < 120f)
        {
            _fireCooldown = _fireInterval;
            fireDir = dirToPlayer;
            fireOrigin = Position + dirToPlayer * (Radius + 0.6f);

            // Elevate onto the craft's body centre. Straight-line aim at where the player
            // is right now: no lead, so a craft that keeps climbing or falling through the
            // shot's flight still slips it — the jump goes on being a dodge, it just isn't
            // a free one any more. The muzzle rides at barrel height like every flat shot.
            float rise = playerHeight + AimHeight - Projectile.BoltHeight;
            firePitch = MathF.Atan2(rise, dist);
            return true;
        }
        return false;
    }

    public void TakeDamage(float amount) => Shield -= amount;

    // --- angle helpers ---

    private static float TurnToward(float current, float target, float maxStep)
    {
        float delta = AngleDelta(current, target);
        if (MathF.Abs(delta) <= maxStep) return target;
        return current + MathF.Sign(delta) * maxStep;
    }

    /// <summary>Shortest signed angle from <paramref name="a"/> to b, in [-π, π].</summary>
    private static float AngleDelta(float a, float b)
    {
        float d = (b - a) % MathF.Tau;
        if (d > MathF.PI) d -= MathF.Tau;
        if (d < -MathF.PI) d += MathF.Tau;
        return d;
    }
}
