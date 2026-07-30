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

    /// <summary>
    /// How far off the grid this hull currently is. Zero for its whole ordinary life — a
    /// hunter drives, it does not leave the floor — and non-zero for exactly one reason:
    /// something has picked it up. The SPIDER's claw carries one at head height and then
    /// throws it, and both the renderer and the height-aware hit tests read this so a
    /// machine in the air is drawn and struck where it actually is.
    /// </summary>
    public float Height;

    /// <summary>
    /// True while the SPIDER's claw has hold of this hull. Its brain is suspended for the
    /// duration — no drive, no fire — and the claw writes its transform instead, the same
    /// arrangement the Crab-Core's seizure has with the player. It is still very much
    /// alive and still very much shootable; it simply is not driving.
    /// </summary>
    public bool Grabbed;

    /// <summary>
    /// True while this hull is in the air after being thrown, travelling on
    /// <see cref="Toss"/> until it meets the grid. Also suspends the brain: a machine
    /// tumbling through the air is not taking a firing solution on anybody.
    /// </summary>
    public bool Flung;

    /// <summary>Ballistic velocity of a thrown hull, world units a second. Stepped by the
    /// world, which is also what decides what the landing costs and who else was standing
    /// there.</summary>
    public Vector3 Toss;

    private readonly float _moveSpeed;
    private readonly float _turnSpeed;
    private readonly float _preferredRange;   // hangs at this distance, not point-blank
    private float _fireCooldown;
    private readonly float _fireInterval;

    // Visual + collision size are one and the same: the renderer scales the mesh
    // by <see cref="Scale"/>, and the hitbox scales with it, so what you see is
    // what you can hit. Change this one number to resize the whole enemy.
    //
    // Doubled from the 1.6 it stood at for most of this game's life. At that size a hunter
    // read as a model of a tank sitting on a very large floor; at this one it is a machine
    // you have to drive around, it fills the frame when it closes, and — the thing that
    // actually decided it — it finally reads as belonging to the same world as a
    // forty-metre tower rather than to a diorama in front of one.
    public const float Scale = 3.2f;
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

        // Held in a claw, or tumbling through the air after being thrown out of one: the
        // brain is off. Something else owns where this hull is this tick, and a machine
        // being used as a shield should not be calmly lining up a shot from inside the
        // hand that is crushing it.
        if (Grabbed || Flung) return false;

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
            float reach = Radius + 0.6f;
            fireOrigin = Position + dirToPlayer * reach;

            // Elevate onto the craft's body centre. Straight-line aim at where the player
            // is right now: no lead, so a craft that keeps climbing or falling through the
            // shot's flight still slips it — the jump goes on being a dodge, it just isn't
            // a free one any more. The muzzle rides at barrel height like every flat shot.
            //
            // Solved from the muzzle rather than from the hull's centre, and the difference
            // is not academic: the round leaves a whole body-length nearer the target than
            // the middle of the hunter is, so an elevation worked out from the centre is
            // short by that fraction of the climb — an error that grows as it closes, and
            // grows with the size of the hunter. Get this wrong and a big enough tank
            // cannot hit a leaping craft at knife range at all.
            float rise = playerHeight + AimHeight - Projectile.BoltHeight;
            firePitch = MathF.Atan2(rise, MathF.Max(1f, dist - reach));
            return true;
        }
        return false;
    }

    public void TakeDamage(float amount) => Shield -= amount;

    // --- Client interpolation ------------------------------------------------------
    // A client is only ever shown hunters, never runs them, and used to be handed a wholly
    // new list every field packet — which threw away any chance of smoothing, so a hunter
    // stepped twenty times a second. Now the client keeps its hunters between packets, matched
    // by the host's id, and eases each toward the position the host last reported. None of this
    // is ever touched on the host, where the fields sit at their defaults.

    /// <summary>The host's stable id for this hunter, so a client can match the same one across
    /// packets and interpolate it rather than rebuilding the list. Assigned lazily host-side in
    /// <c>Snapshot.WriteField</c>; on a client it is the key it was matched on.</summary>
    public int NetId;

    /// <summary>Scratch flag for the client's adopt sweep: set on every hunter the latest field
    /// packet named, so the ones it did not name can be dropped.</summary>
    public bool NetSeen;

    /// <summary>The key this hunter goes by in the host's rewind history, so a laggy client's
    /// shot can be tested against where it actually was on that client's screen. Assigned
    /// lazily host-side; deliberately not <see cref="NetId"/>, which is a reused byte and
    /// would have two hunters sharing a past.</summary>
    public int HitId;

    private Vector2 _netPos;
    private float _netHeading;
    private bool _hasNet;

    /// <summary>Client-side: records where the host last put this hunter. Snaps on first sight,
    /// eases every time after.</summary>
    public void NetTarget(Vector2 pos, float heading)
    {
        if (!_hasNet) { Position = pos; Heading = heading; _hasNet = true; }
        _netPos = pos; _netHeading = heading;
    }

    /// <summary>Client-side: eases this hunter one frame toward its last reported transform.</summary>
    public void EaseToNet(float k)
    {
        if (!_hasNet) return;
        Position = Torus.Wrap(Position + Torus.Delta(Position, _netPos) * k);
        Heading += MathF.IEEERemainder(_netHeading - Heading, MathF.Tau) * k;
    }

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
