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

    // A thrown CRAB CORE: lobbed like a grenade, but on impact/expiry the world
    // stages a radial beam blast where it lands instead of a plain splash. The flag
    // rides along so the world can tell the two heavy rounds apart at detonation.
    public bool IsCrabBomb;

    /// <summary>
    /// A SPIDER laser rather than a cannon bolt. Purely cosmetic — it travels, collides
    /// and damages exactly as an ordinary round does, because "the same damage as the
    /// bullet" is the class's spec. The flag only tells the renderer to draw a short
    /// neon streak instead of the flat-shaded octahedron.
    /// </summary>
    public bool IsLaser;

    /// <summary>
    /// A SOLDIER's rocket: fin-stabilised, contact-fused, and the only round in the game
    /// that flies a genuine 3D line rather than skimming the plane at a fixed height —
    /// it is fired down a mouse-aimed look, which can point at the sky or at the floor.
    /// It carries a splash like a grenade's and, unlike any other round, what it goes off
    /// against comes down.
    /// </summary>
    public bool IsRocket;

    private const float Speed = 90f;
    private const float GrenadeSpeed = 60f;   // heavier, so it lobs slower
    private const float MaxLife = 2.4f;
    public const float GrenadeSplash = 7f;    // blast radius in world units

    /// <summary>How fast a rocket travels. Slow enough to watch leave, and slow enough
    /// that firing one at a wall you are swinging toward is a real gamble.</summary>
    public const float RocketSpeed = 45f;

    /// <summary>Its blast radius. Everything inside takes the hit, and any building
    /// caught in it is cut down — including the one the player is hanging from.</summary>
    public const float RocketSplash = 6f;

    /// <summary>How fast a rifle round leaves — the cannon's own muzzle speed, since it
    /// is the same round out of a smaller weapon.</summary>
    public const float RifleSpeed = Speed;

    /// <summary>Barrel height of a grounded shot — where the bolt rides by default.</summary>
    public const float BoltHeight = 0.55f;

    // How long an air shot glides before it sinks to the floor far downrange. Paired
    // with the launch height, this fixes the descent rate so the shot always lands
    // out near the horizon regardless of how high the jump was.
    private const float AirGlideTime = 2.6f;

    public void Fire(Vector2 origin, Vector2 dir, bool fromPlayer, float launchHeight = BoltHeight,
        bool laser = false)
    {
        Position = origin;
        Velocity = Vector2.Normalize(dir) * Speed;
        FromPlayer = fromPlayer;
        IsGrenade = false;
        IsLaser = laser;
        IsCrabBomb = false;   // pooled slots are reused — clear the thrown-core flag or a
                              // plain bolt inherits it and detonates like one on expiry
        IsRocket = false;
        IsTracer = false;
        _climb = 0f;
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
        IsCrabBomb = false;
        IsLaser = false;
        IsRocket = false;
        IsTracer = false;
        SplashRadius = GrenadeSplash;
        Height = 0.7f;          // the fatter slug rides a touch higher than a bolt
        _descentRate = 0f;
        _climb = 0f;
        IsAirShot = false;
        JustExpired = false;
        Active = true;
    }

    /// <summary>
    /// Lobs a thrown CRAB CORE: it travels like a grenade but detonates into a ring of
    /// beams (staged by the world when <see cref="JustExpired"/> fires or it strikes
    /// something). Always from the player — it's a crafted weapon, not enemy ordnance.
    /// A shorter life than a grenade so it goes off close, out in front of the craft.
    /// </summary>
    public void FireCrabBomb(Vector2 origin, Vector2 dir)
    {
        Position = origin;
        Velocity = Vector2.Normalize(dir) * GrenadeSpeed;
        Life = 0.9f;            // detonates a short throw out in front
        FromPlayer = true;
        IsGrenade = true;       // reuse the fat-slug travel + splash-style handling
        IsCrabBomb = true;
        IsLaser = false;
        IsRocket = false;
        IsTracer = false;
        SplashRadius = 0f;      // the beams carry the damage, not a splash sphere
        Height = 0.7f;
        _descentRate = 0f;
        _climb = 0f;
        IsAirShot = false;
        JustExpired = false;
        Active = true;
    }

    /// <summary>
    /// Launches a round along a full 3D line: the SOLDIER's rifle and its rockets, both
    /// of which are aimed with a mouse at whatever the crosshair is on rather than fired
    /// along a chassis's heading.
    ///
    /// The climb is carried as its own velocity rather than as the air shot's fixed
    /// descent, so a round fired at the sky genuinely goes up and a round fired at the
    /// grid from the top of a swing genuinely comes down where it was pointed.
    /// </summary>
    public void FireDirected(Vector3 origin, Vector3 dir, float speed, bool rocket)
    {
        Vector3 d = Vector3.Normalize(dir);

        Position = new Vector2(origin.X, origin.Z);
        Velocity = new Vector2(d.X, d.Z) * speed;
        Height = MathF.Max(0.05f, origin.Y);
        _descentRate = 0f;
        _climb = d.Y * speed;

        FromPlayer = true;
        IsRocket = rocket;
        IsTracer = !rocket;
        IsGrenade = false;      // a rocket carries its own splash; it is not the heavy round
        IsCrabBomb = false;
        IsLaser = false;
        IsAirShot = false;
        SplashRadius = rocket ? RocketSplash : 0f;
        Life = rocket ? MaxLife : 1.6f;
        JustExpired = false;
        Active = true;
    }

    /// <summary>Vertical velocity for a directed round, in units a second. Zero for
    /// everything that travels flat.</summary>
    private float _climb;

    /// <summary>A directed round that isn't a rocket — the SOLDIER's rifle. Drawn as a
    /// tracer streak rather than as the cannon's tumbling shell.</summary>
    public bool IsTracer { get; private set; }

    /// <summary>
    /// The full 3D direction of travel, normalised — what a round that flies a real
    /// line has to be drawn along. Falls back to +Z for a shot with no velocity at all,
    /// which only a pooled slot mid-reset can be.
    /// </summary>
    public Vector3 Heading3
    {
        get
        {
            var v = new Vector3(Velocity.X, _climb, Velocity.Y);
            return v.LengthSquared() > 1e-6f ? Vector3.Normalize(v) : new Vector3(0f, 0f, 1f);
        }
    }

    public void Update(float dt)
    {
        if (!Active) return;
        JustExpired = false;
        Position += Velocity * dt;
        if (_descentRate != 0f) Height -= _descentRate * dt;
        if (_climb != 0f) Height += _climb * dt;
        Life -= dt;

        // A directed round that has flown into the grid has hit the grid: it goes off
        // there rather than sailing on underneath the floor.
        bool struckGround = _climb < 0f && Height <= 0f;
        if (Life <= 0f || (IsAirShot && Height <= 0f) || struckGround)
        {
            if (struckGround) Height = 0f;
            Active = false;
            JustExpired = true;   // ended on its own — stage the horizon blast
        }
    }
}
