using System.Numerics;
using VoidTanks.Core;

namespace VoidTanks.Entities;

/// <summary>What a mote is currently wearing. The whole class is which of these the
/// player is, so nearly everything downstream branches on it.</summary>
public enum VirusHost
{
    /// <summary>Nothing. The naked mote — fast, free-flying, and one good hit from dead.</summary>
    None,

    /// <summary>A standard hunter: grounded drive, a cannon, an honest suit of armour.</summary>
    Hunter,

    /// <summary>An elite hunter: the same suit, bigger and angrier, and it lasts longer.</summary>
    Elite,

    /// <summary>The Crab-Core itself, worn. Slow, enormous, and carrying the one weapon
    /// worth stealing in this whole world — its lance, which does not survive the theft
    /// intact (see <see cref="VirusRig.TryLance"/>).</summary>
    Crab,

    /// <summary>The Maw-Core, worn. It never learned to stand, so neither do you: this is
    /// the one host that hovers, and it spits the mouth's own acid bolts.</summary>
    Maw,
}

/// <summary>
/// The VIRUS chassis, and with it the whole of how that chassis moves — which is to say
/// the whole of how it survives, because on this class the two are the same problem.
///
/// Every other craft in this game <em>is</em> something. The tank is a hull, the spider a
/// salvaged core, the fish a body, the soldier a person. This one is none of those. It is
/// the thing the machine could never print a pattern for because there is no pattern to
/// print: a virus has no body of its own, it takes one. So the class is built out of two
/// states that play nothing alike, and the run is the loop of moving between them.
///
/// <list type="bullet">
/// <item><b>The mote.</b> Unhosted, you are a naked payload — a mote of corruption that
/// flies free in three dimensions, fast and weightless, with no armour worth the name. It
/// is the only thing in the game that flies where it looks: a beat of the fish is an
/// impulse in water, a leap of the tank is an arc under gravity, but the mote simply goes
/// where the crosshair points, up the face of a tower or down at the grid. It is one good
/// hit from dead (see <c>World.MoteVulnerability</c>) — and it is on a clock: code with no
/// body cannot hold itself together forever, and past <see cref="ExposureGrace"/> seconds
/// unhosted it begins to <em>wither</em>, which the world bills in shield until it reaches
/// a body or dies as static.</item>
/// <item><b>The host.</b> Fly the mote into a body and you <em>wear</em> it. Hunters are
/// taken by contact; the two big machines are taken the way everything else in this game
/// reaches them — through the bright core, which for a mote is a door rather than a weak
/// point. Whatever you wear is dying under you from the moment you take it: the
/// <see cref="Decay"/> meter counts down, damage eats it faster, and when it empties the
/// husk bursts and spits you back out. The right trigger cuts that short on purpose —
/// <see cref="Overload"/> dumps the host's whole remaining integrity at once as a
/// detonation, so a body you were going to lose anyway is spent as a bomb instead.</item>
/// </list>
///
/// The moment-to-moment is therefore a chain, the same shape as the fish's burst-and-glide
/// but paid in bodies instead of breath: infect, fight while the husk rots, hop to the next
/// before the withering finds you. A player with no host and no host in reach is a player
/// about to die, which is exactly the tension the class is built around.
///
/// Pure state and physics — no Raylib, no world queries. The world decides which body the
/// mote reached and what a detonation costs, so the rig stays testable without a screen.
/// </summary>
public sealed class VirusRig
{
    // --- The mote ---------------------------------------------------------------
    // Free flight, and the one genuinely three-dimensional movement model in the game.
    // Thrust runs along the full look (so nosing up climbs) with a sideways step off it;
    // drag is heavy enough that letting go coasts to a stop over a second rather than
    // sailing forever, and light enough that the mote is quick and slippery to fly.

    /// <summary>How hard the mote accelerates along the look, in m/s². Brisk: this is a
    /// speck of code, not a machine leaning into its own weight.</summary>
    private const float MoteAccel = 60f;

    /// <summary>Drag on the mote while it is being flown, per second (exponential). Sets
    /// how slippery it feels — high enough to stop, low enough to slide.</summary>
    private const float MoteDrag = 2.6f;

    /// <summary>And the harder drag once the player lets go, so an unflown mote settles
    /// rather than drifting off across the arena on its own.</summary>
    private const float MoteIdleDrag = 5.5f;

    /// <summary>The mote's top speed as a multiple of the craft's own — so the SPEED track
    /// still means what it means everywhere else, and a fast build genuinely runs down a
    /// hunter while a slow one has to ambush one. Well over a hunter's own pace at every
    /// build, because catching a body is the entire job of the exposed state.</summary>
    private const float MoteTopScale = 1.55f;

    /// <summary>How far the mote steps sideways compared with driving forward. Enough to
    /// juke, not a second full gear.</summary>
    private const float StrafeFactor = 0.75f;

    /// <summary>A ceiling on flight, so a player who noses up and holds thrust climbs out
    /// of the fight rather than off to infinity. Well above the skyline — the virus has no
    /// toxic roof the way the fish does; this is only a leash.</summary>
    private const float MoteCeiling = 70f;

    /// <summary>Where the eye sits on the mote: barely off its own origin. Like the fish,
    /// the position <em>is</em> the creature.</summary>
    private const float MoteEyeHeight = 0.7f;

    // --- Exposure ---------------------------------------------------------------
    // The mote's own clock. Code with no body around it cannot hold itself together: past
    // the grace it starts to wither, which the world bills in shield on a slow tick. The
    // grace is generous on purpose — long enough to cross the arena to a body, far too
    // short to make living unhosted a strategy.

    /// <summary>Seconds a mote can stay unhosted before the withering starts.</summary>
    public const float ExposureGrace = 15f;

    /// <summary>How long this exposure has lasted so far, in seconds. Reset by taking a
    /// body — and by losing one, so every ejection hands back the full grace to find the
    /// next host in.</summary>
    public float ExposureTime { get; private set; }

    /// <summary>True once the grace has run out and the mote is coming apart. The world
    /// reads it to bill the damage; the HUD and the screen effects read it to shout.</summary>
    public bool Withering => Exposed && ExposureTime > ExposureGrace;

    /// <summary>Seconds of grace left before the withering starts. Zero once it has.</summary>
    public float GraceRemaining => MathF.Max(0f, ExposureGrace - ExposureTime);

    // --- The hosts --------------------------------------------------------------
    // Worn bodies. The hunters drive like the tank, on a throttle and a strafe; the crab
    // is the same walk with more mass under it; the maw hovers, because it never learned
    // to stand. All of them are deliberately a shade slower than the mote, so hopping is
    // never a downgrade in mobility — only a trade of speed for armour.

    /// <summary>A worn hunter's top speed on the grid. Under the mote's, so the exposed
    /// state is the fast one and the safe state is the slow one — the trade in both
    /// directions.</summary>
    private const float HunterTopSpeed = 22f;

    /// <summary>A worn Crab-Core's. It is a building on legs; wearing it should feel like
    /// one — and the real crab's own pursuit run is no sprinter either.</summary>
    private const float CrabTopSpeed = 18f;

    /// <summary>The worn maw's hover speed. A heavy floating mouth, not a mote.</summary>
    private const float MawTopSpeed = 19f;

    /// <summary>How hard a grounded host accelerates toward its target velocity. A body
    /// has inertia; it leans into a move and coasts out of it.</summary>
    private const float HostAccel = 40f;

    /// <summary>And the hovering one, in free flight. Softer than the mote's — the same
    /// controls with a mouth's worth of mass behind them.</summary>
    private const float MawHostAccel = 36f;

    /// <summary>The floor a worn maw hovers at. It cannot land: it has no legs, and a
    /// mouth resting on the grid would just be a beached fish with teeth.</summary>
    private const float MawHoverFloor = 2.5f;

    /// <summary>How quickly a freshly-taken grounded host settles onto the grid from
    /// wherever the mote struck it. Fast enough to feel like seizing a body, not a
    /// landing.</summary>
    private const float HostSettle = 26f;

    /// <summary>Where the eye rides on each body: up on a hunter's hull, high in the
    /// crab's carapace where its gem sat, just behind the maw's crystal.</summary>
    private const float HunterEyeHeight = 2.6f;
    private const float CrabEyeHeight = 9f;
    private const float MawEyeHeight = 1.2f;

    // --- Decay ------------------------------------------------------------------

    /// <summary>How long a standard host lasts on time alone, in seconds, before it rots
    /// out from under a player who never takes a scratch. Long enough that the clock is
    /// rarely what ends a body — damage is — and short enough that no body is ever a
    /// home.</summary>
    private const float DecayLifetime = 44f;

    /// <summary>Bigger, angrier machines last proportionally longer once corrupted — on
    /// the clock and against damage both.</summary>
    private const float EliteToughness = 1.5f;
    private const float CrabToughness = 3f;
    private const float MawToughness = 2.5f;

    /// <summary>What fraction of the craft's own shield a standard host is worth as a
    /// damage buffer. The SHIELD track therefore buys host integrity rather than a wall
    /// around the mote — a high-shield virus wears a body for a long time and a low-shield
    /// one is barely more durable hosted than exposed.</summary>
    private const float HostCapacityScale = 1.4f;

    // --- The stolen lance -------------------------------------------------------
    // The Crab-Core's beam, fired by whatever is wearing the crab. It did not survive the
    // theft intact: a corrupted core cannot hold a line, so every shot leaves as one main
    // shaft roughly where it was aimed and a spray of breaks in directions nobody chose.
    // The world rolls the directions and applies the damage; the rig owns the trigger's
    // cost and the live shafts the renderer draws.

    /// <summary>How far a stolen shaft reaches. Short of the boss's own 150 — this is the
    /// same emitter running on borrowed, rotting hardware.</summary>
    public const float LanceLength = 120f;

    /// <summary>Half-width of each damaging shaft. A shade over the spider's cut-down 1.0
    /// and nowhere near the boss's 2.4, for the spider's own first-person reason: these
    /// shafts leave an emitter a few units in front of the player's eye, and at the boss's
    /// width one beam fills the frame — let alone four of them at once.</summary>
    public const float LanceRadius = 1.1f;

    /// <summary>How long a fired shaft burns on screen. Visual only — the damage lands
    /// once, on the frame it fires, exactly as the spider's lance bills.</summary>
    public const float LanceBurnTime = 0.55f;

    /// <summary>What one shaft deals to everything standing on it.</summary>
    public const float LanceDamage = 6f;

    /// <summary>The trigger's pace. Long enough that the weapon is a series of decisions
    /// rather than a hose.</summary>
    public const float LanceCooldownTime = 1.4f;

    /// <summary>What each discharge burns off the host's own meter. The stolen gun is
    /// powered by the body it is bolted to, so every shot is a bite out of your armour —
    /// and a shot fired on the last of the meter finishes the host outright.</summary>
    public const float LanceDecayCost = 0.07f;

    /// <summary>The most shafts one discharge can put in the air: the aimed one plus the
    /// breaks.</summary>
    public const int MaxShafts = 5;

    /// <summary>One live shaft of the broken beam: where it left from, which way it went,
    /// and how much burn it has left. Written by the world on fire; aged here.</summary>
    public struct LanceShaft
    {
        public Vector3 Origin;
        public Vector3 Dir;
        public float Life;
    }

    /// <summary>The shafts currently burning. The renderer walks this; slots with
    /// <c>Life &lt;= 0</c> are dead.</summary>
    public readonly LanceShaft[] Shafts = new LanceShaft[MaxShafts];

    private float _lanceCooldown;

    // --- Live state -------------------------------------------------------------

    /// <summary>What is currently being worn — <see cref="VirusHost.None"/> for the naked
    /// mote.</summary>
    public VirusHost HostKind { get; private set; } = VirusHost.None;

    /// <summary>True while any host is being worn.</summary>
    public bool Hosted => HostKind != VirusHost.None;

    /// <summary>The exposed state, named for the readers that care about the danger rather
    /// than the mechanic.</summary>
    public bool Exposed => !Hosted;

    /// <summary>The host's remaining integrity, 1 (just taken) .. 0 (about to burst). The
    /// meter the whole hosted phase is played against, and 0 whenever there is no host.</summary>
    public float Decay { get; private set; }

    /// <summary>The same number under the name the HUD reads it by.</summary>
    public float Integrity => Decay;

    /// <summary>How corrupted the picture should look, 0 (clean) .. 1 (tearing apart): the
    /// veins spreading over a rotting host, or the full static of being a naked mote.</summary>
    public float Corruption => Hosted ? 1f - Decay : 1f;

    /// <summary>Full 3D momentum. On the mote and the worn maw this is free flight; on a
    /// grounded host only the planar components are driven and Y is held at the grid.</summary>
    public Vector3 Velocity;

    /// <summary>This step's movement input as (strafe, forward), each -1..1, resolved from
    /// the keys by the world before the step — the same arrangement the soldier uses.</summary>
    public Vector2 MoveInput;

    /// <summary>Where the eye sits above the craft's origin — one per body.</summary>
    public float EyeHeight => HostKind switch
    {
        VirusHost.Hunter or VirusHost.Elite => HunterEyeHeight,
        VirusHost.Crab => CrabEyeHeight,
        VirusHost.Maw => MawEyeHeight,
        _ => MoteEyeHeight,
    };

    /// <summary>Broadband view shake, 0..1 — a possession landing, a hit soaked by the
    /// host, an overload going off in the player's own face.</summary>
    public float Shake { get; private set; }

    /// <summary>Planar speed, for the wind and the FOV stretch — a dead-vertical mote climb
    /// should not roar.</summary>
    public float PlanarSpeed => new Vector2(Velocity.X, Velocity.Z).Length();

    public float Speed => Velocity.Length();

    // --- Single-frame events the world drains each step -------------------------

    /// <summary>Set the tick a host is taken, so the world can voice the seizure.</summary>
    public bool JustPossessed { get; private set; }

    /// <summary>Set the tick a host is left behind — a decay burst, a host killed under
    /// fire, or an overload. The world reads it to stage whatever the ejection throws off
    /// and to sound it.</summary>
    public bool JustEjected { get; private set; }

    /// <summary>True on an ejection the player <em>chose</em> — the right-trigger overload,
    /// which spends the whole host as a detonation. False on the two ejections that are
    /// simply the body giving out (the clock, or the last hit), which throw off far less.</summary>
    public bool EjectWasOverload { get; private set; }

    private float _decayRate;      // per second, set from the host's toughness on possession
    private float _hostCapacity;   // damage the host soaks across a full meter

    /// <summary>Metres of reach the mote seizes a hunter at — the world adds the two radii
    /// and checks contact against it. Held here so the one number that says "you are close
    /// enough to infect" lives with the class it belongs to.</summary>
    public const float InfectMargin = 0.6f;

    /// <summary>
    /// Seizes a body and climbs inside it. Called by the world the tick the mote reaches
    /// one — the world owns which body, and consuming it, because the rig never queries the
    /// world. Resets the decay clock to full and scales both the clock and the damage
    /// buffer off the craft's shield and the host's own size; also hands back the exposure
    /// grace, since a mote inside a body is not exposed to anything.
    /// </summary>
    public void Possess(PlayerTank p, VirusHost kind)
    {
        if (kind == VirusHost.None) return;

        HostKind = kind;
        Decay = 1f;
        Velocity = Vector3.Zero;
        ExposureTime = 0f;

        float toughness = kind switch
        {
            VirusHost.Elite => EliteToughness,
            VirusHost.Crab => CrabToughness,
            VirusHost.Maw => MawToughness,
            _ => 1f,
        };
        _decayRate = 1f / (DecayLifetime * toughness);
        _hostCapacity = MathF.Max(1f, p.MaxShield * HostCapacityScale * toughness);
        _lanceCooldown = 0f;

        JustPossessed = true;
        Shake = MathF.Min(1f, MathF.Max(Shake, 0.35f));
    }

    /// <summary>
    /// Dumps the worn host's whole remaining integrity at once as a detonation and spits the
    /// player out as the mote. The class's committed heavy, and paid for the way this game's
    /// heavies always are — in a resource that is gone afterwards. Here the resource is the
    /// body itself: you cannot overload your way out of trouble and still be wearing armour.
    /// A no-op with no host to spend.
    /// </summary>
    public void Overload()
    {
        if (!Hosted) return;
        Eject(overload: true);
    }

    /// <summary>
    /// The stolen lance's trigger. Only a worn crab has the emitter; everything else is
    /// refused. Honours its own cooldown and burns <see cref="LanceDecayCost"/> off the
    /// meter — and a shot fired on the last of it finishes the host on the spot, which is a
    /// trade the world lets the player make. Returns whether a discharge actually left, so
    /// the world knows whether to roll the shafts and sound the break.
    /// </summary>
    public bool TryLance()
    {
        if (HostKind != VirusHost.Crab || _lanceCooldown > 0f) return false;

        _lanceCooldown = LanceCooldownTime;
        Decay -= LanceDecayCost;
        if (Decay <= 0f)
        {
            Decay = 0f;
            // The discharge still goes out — it is the body that doesn't survive firing it.
            Eject(overload: false);
        }
        return true;
    }

    /// <summary>Registers one fired shaft for the renderer. The world calls this once per
    /// direction it rolled; a full table quietly drops the extras.</summary>
    public void AddShaft(Vector3 origin, Vector3 dir)
    {
        for (int i = 0; i < Shafts.Length; i++)
        {
            if (Shafts[i].Life > 0f) continue;
            Shafts[i] = new LanceShaft { Origin = origin, Dir = dir, Life = LanceBurnTime };
            return;
        }
    }

    /// <summary>
    /// Feeds incoming damage into the host instead of the player while a body is being worn:
    /// the whole reason to be inside one. Drains the decay meter in proportion to the host's
    /// capacity, and — if this hit is the one that finishes the body — bursts it, spits the
    /// player out, and hands back whatever damage the husk couldn't soak so a truly huge blow
    /// still stings the mote it leaves behind.
    /// </summary>
    /// <returns>The overflow damage that spills past a broken host onto the player, or 0.</returns>
    public float AbsorbDamage(float amount)
    {
        if (!Hosted || amount <= 0f) return amount;

        float drain = amount / _hostCapacity;   // fraction of the meter this hit costs
        if (drain < Decay)
        {
            Decay -= drain;
            Shake = MathF.Min(1f, MathF.Max(Shake, 0.18f));
            return 0f;
        }

        // The hit broke the host. Work out how much of the blow the husk actually soaked
        // before it went, so the rest can be handed to the mote — a hosted player caught by
        // a detonation is exposed <em>and</em> hurt, not merely exposed.
        float soaked = Decay * _hostCapacity;
        Eject(overload: false);
        return MathF.Max(0f, amount - soaked);
    }

    /// <summary>Throws the view about by <paramref name="amount"/> (0..1), taking the larger
    /// rather than summing so a cluster of hits reads as one hard event.</summary>
    public void Jolt(float amount) => Shake = MathF.Min(1f, MathF.Max(Shake, amount));

    /// <summary>Leaves the host behind and becomes the mote again, flagging why for the world
    /// to dress. The mote pops out with the host's last scrap of momentum plus a small upward
    /// kick, so it visibly ejects rather than appearing on the spot — and its exposure grace
    /// starts over, so losing a body always leaves the full window to find the next one.</summary>
    private void Eject(bool overload)
    {
        HostKind = VirusHost.None;
        Decay = 0f;
        JustEjected = true;
        EjectWasOverload = overload;
        ExposureTime = 0f;

        Velocity = new Vector3(Velocity.X * 0.3f, EjectKick, Velocity.Z * 0.3f);
        Shake = MathF.Min(1f, MathF.Max(Shake, overload ? 0.6f : 0.3f));
    }

    /// <summary>The upward pop an ejection gives the mote, m/s.</summary>
    private const float EjectKick = 6f;

    /// <summary>
    /// One fixed step. Clears the single-frame flags, ages the live shafts and the lance's
    /// clock, then runs whichever body is live: the mote's free flight, the maw's hover, or
    /// a grounded host's drive with its decay. Writes the player's position and height
    /// directly; the virus has no heading of its own, since the mouse owns where it looks.
    /// </summary>
    public void Step(float dt, PlayerTank p)
    {
        JustPossessed = false;
        JustEjected = false;

        if (_lanceCooldown > 0f) _lanceCooldown = MathF.Max(0f, _lanceCooldown - dt);
        for (int i = 0; i < Shafts.Length; i++)
            if (Shafts[i].Life > 0f) Shafts[i].Life -= dt;

        switch (HostKind)
        {
            case VirusHost.None: StepMote(dt, p); break;
            case VirusHost.Maw: StepHover(dt, p); break;
            default: StepGroundedHost(dt, p); break;
        }

        Shake *= MathF.Exp(-6.5f * dt);
        if (Shake < 0.002f) Shake = 0f;
    }

    /// <summary>
    /// The mote's free flight: thrust along the full look, a sideways step off it, heavy
    /// drag, and integration in three dimensions with a floor at the grid and a leash at the
    /// ceiling. The one movement model in the game with no gravity and no ground to speak
    /// of — the payload goes where the crosshair points and nothing pulls it down. Also the
    /// only state whose clock is the <em>exposure</em>: every second spent in here is a
    /// second closer to withering.
    /// </summary>
    private void StepMote(float dt, PlayerTank p)
    {
        ExposureTime += dt;

        Fly(dt, p, MoteAccel, p.TopSpeed * MoteTopScale, floor: 0f);
    }

    /// <summary>
    /// The worn maw's hover: the mote's own flight model with a mouth's worth of mass on it
    /// — softer thrust, a lower ceiling on speed, and a floor it can never land through,
    /// because a body with no legs does not get to stand on the grid.
    /// </summary>
    private void StepHover(float dt, PlayerTank p)
    {
        Fly(dt, p, MawHostAccel, MawTopSpeed, floor: MawHoverFloor);
        StepDecay(dt);
    }

    /// <summary>The shared free-flight integrator behind the mote and the worn maw.</summary>
    private void Fly(float dt, PlayerTank p, float accel, float top, float floor)
    {
        Vector3 look = p.Forward3;
        Vector2 fwd = p.Forward;
        var right = new Vector3(-fwd.Y, 0f, fwd.X);

        float drive = Math.Clamp(MoveInput.Y, -1f, 1f);
        float strafe = Math.Clamp(MoveInput.X, -1f, 1f);

        Velocity += look * (drive * accel) * dt;
        Velocity += right * (strafe * accel * StrafeFactor) * dt;

        bool coasting = drive == 0f && strafe == 0f;
        Velocity *= MathF.Max(0f, 1f - (coasting ? MoteIdleDrag : MoteDrag) * dt);

        float speed = Velocity.Length();
        if (speed > top) Velocity *= top / speed;

        p.Position = Torus.Wrap(p.Position + new Vector2(Velocity.X, Velocity.Z) * dt);
        p.Height = Math.Clamp(p.Height + Velocity.Y * dt, floor, MoteCeiling);

        // Resting on the floor or pinned to the leash: kill the vertical push so the body
        // sits there rather than grinding into the limit.
        if ((p.Height <= floor && Velocity.Y < 0f) || (p.Height >= MoteCeiling && Velocity.Y > 0f))
            Velocity.Y = 0f;
    }

    /// <summary>
    /// A grounded host's step: settle onto the grid, drive on the throttle and strafe like
    /// the tank — at whatever pace this body can manage — and bleed the decay meter.
    /// </summary>
    private void StepGroundedHost(float dt, PlayerTank p)
    {
        p.Height = MoveToward(p.Height, 0f, HostSettle * dt);

        float top = HostKind == VirusHost.Crab ? CrabTopSpeed : HunterTopSpeed;

        Vector2 fwd = p.Forward;
        var right = new Vector2(-fwd.Y, fwd.X);

        float drive = Math.Clamp(MoveInput.Y, -1f, 1f);
        float strafe = Math.Clamp(MoveInput.X, -1f, 1f);

        Vector2 target = fwd * (drive * top) + right * (strafe * top * StrafeFactor);
        var planar = new Vector2(Velocity.X, Velocity.Z);
        planar = MoveToward(planar, target, HostAccel * dt);
        Velocity = new Vector3(planar.X, 0f, planar.Y);

        p.Position = Torus.Wrap(p.Position + planar * dt);

        StepDecay(dt);
    }

    /// <summary>The husk rotting under the player. Time alone runs it out; damage runs it
    /// out faster (see <see cref="AbsorbDamage"/>). At zero the body gives up and ejects.</summary>
    private void StepDecay(float dt)
    {
        Decay -= _decayRate * dt;
        if (Decay <= 0f)
        {
            Decay = 0f;
            Eject(overload: false);
        }
    }

    private static float MoveToward(float value, float target, float maxDelta)
    {
        if (MathF.Abs(target - value) <= maxDelta) return target;
        return value + MathF.Sign(target - value) * maxDelta;
    }

    private static Vector2 MoveToward(Vector2 value, Vector2 target, float maxDelta)
    {
        Vector2 d = target - value;
        float len = d.Length();
        if (len <= maxDelta || len < 1e-5f) return target;
        return value + d / len * maxDelta;
    }
}
