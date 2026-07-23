using System.Numerics;
using VoidTanks.Core;
using VoidTanks.Input;

namespace VoidTanks.Entities;

/// <summary>
/// The player's craft. Movement is heavy and a half-second behind intent
/// (Doc 03): momentum on drive, inertia on turn, an awkward hop for dodging —
/// the SPIDER's hop, that is. The TANK is refused it outright (see TryJump):
/// treads never leave the grid. You are piloting a machine, not a cursor.
/// </summary>
public sealed class PlayerTank
{
    // Position on the flat grid plane (X, Z). Y is height for the jump arc.
    public Vector2 Position;
    public float Height;          // 0 = grounded; rises during a jump
    public float Heading;         // radians; 0 faces +Z (into the screen)

    /// <summary>
    /// Where the eye is aimed vertically, in radians — positive is up. Every chassis owns
    /// this now; what differs is how far it is allowed to go, which
    /// <see cref="LookElevation"/> decides.
    /// </summary>
    public float Pitch;

    /// <summary>How far the look can be thrown before it would tip past vertical. Kept
    /// just short of a right angle so straight up and straight down are both reachable
    /// and neither can flip the horizon over.</summary>
    public const float MaxPitch = 1.45f;

    // --- Where the craft points -------------------------------------------------
    // The mouse turns the whole craft, on every chassis. It used to move a turret on top
    // of the tank and the spider while A and D turned the hull under it, and that split —
    // the view swinging one way and the body another — read as disorienting rather than
    // as a turret. So the head was folded back into the hull: the mouse is now the only
    // thing that turns you, A and D strafe, and the class differences that remain are how
    // far the gun can crane and how its rounds fly, not who owns the yaw.

    /// <summary>
    /// How far the TANK's gun elevates: fifteen degrees either way, about what a gun in a
    /// ring does. It is also the number that keeps the Maw-Core honest — its crystal hangs
    /// at the top of a jump, and at this stop an arced bolt from the grid tops out well
    /// under the strike band, so leaping at it stays the answer and sniping it from
    /// underneath is not one.
    /// </summary>
    public const float TurretElevation = 0.26f;

    /// <summary>
    /// True on the two chassis that are machines rather than bodies — the TANK and the
    /// SPIDER. They drive on a throttle and strafe on A/D; the three bodies (SOLDIER, FISH,
    /// VIRUS) run their own rigs instead. What it still gates: the shallow gun elevation and
    /// the arced cannon shell, both of which are the tank's alone.
    /// </summary>
    public bool IsMachine => Soldier == null && Fish == null && Virus == null;

    /// <summary>How far the eye — and the gun with it — can crane up or down. The TANK's
    /// gun is stopped short at <see cref="TurretElevation"/>; everything else looks the
    /// full way, since a spider's ring, a soldier's neck and a fish's whole body all can.</summary>
    public float LookElevation => IsMachine && Spider == null && !Planted ? TurretElevation : MaxPitch;

    /// <summary>
    /// Turns the craft by a mouse frame's worth of yaw and pitch, both in radians — the
    /// whole craft, on every chassis, since the mouse is now the only thing that steers.
    /// Yaw runs straight into the shared <see cref="Heading"/> and pitch into the eye,
    /// clamped to whatever this chassis can crane to.
    /// </summary>
    public void Look(float yaw, float pitch)
    {
        Heading += yaw;
        Pitch = Math.Clamp(Pitch + pitch, -LookElevation, LookElevation);
    }

    /// <summary>
    /// The elevation a round actually leaves at. Identical to <see cref="Pitch"/> in
    /// ordinary play — you look and shoot along the same line — and clamped here anyway
    /// because a cinematic writes <see cref="Pitch"/> outright and must not be able to
    /// loose a shot at an angle the chassis cannot hold.
    /// </summary>
    public float GunElevation => Math.Clamp(Pitch, -LookElevation, LookElevation);

    // Signed forward and lateral speed — carried between frames so the craft eases into
    // motion and coasts to a stop rather than snapping. Strafe is the newer of the two:
    // now that the mouse owns the turn, A and D step sideways instead of steering.
    private float _speed;
    private float _strafe;
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

    /// <summary>How much slower the craft steps sideways than it drives forward. A tank
    /// on its tracks is happiest going where it points, so a strafe is a shove off to the
    /// side rather than a second full-speed gear — enough to sidestep a shot, not enough
    /// to circle-strafe a hunter the way a soldier can.</summary>
    private const float StrafeFactor = 0.7f;

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
    /// The SOLDIER's twin cable launcher, or null on every other chassis. Unlike the
    /// spider's emitter this is not a weapon bolted onto the standard physics — it
    /// <em>replaces</em> them. While it exists, <see cref="Update"/> hands the whole
    /// transform to it: no heading momentum, no throttle, no jump arc, just a velocity
    /// vector and two ropes.
    /// </summary>
    public SoldierRig? Soldier { get; private set; }

    /// <summary>
    /// The FISH's body, or null on every other chassis. Replaces the physics outright the
    /// way the soldier's rig does, and for a stronger reason: this chassis does not drive
    /// on the grid at all. While it exists there is no heading momentum, no throttle and
    /// no jump arc — only a velocity vector in water, and a body that has to keep beating
    /// to stay in it.
    /// </summary>
    public FishRig? Fish { get; private set; }

    /// <summary>
    /// The VIRUS chassis, or null on every other. Replaces the physics outright the way the
    /// soldier's and the fish's rigs do, and swings between two of them: a naked mote in free
    /// flight, or a hunter worn as a suit. While it exists there is no throttle, no jump arc
    /// and no heading momentum of the craft's own — only whichever of the rig's two states
    /// currently owns the transform.
    /// </summary>
    public VirusRig? Virus { get; private set; }

    /// <summary>
    /// How high the eye sits above the craft's own origin. A tank's camera rides up on
    /// the hull; a soldier is a person standing on the grid, and dropping the eye to
    /// head height is most of what makes the same city read as something you are small
    /// inside rather than something you drive past. A fish is smaller still and its
    /// position <em>is</em> its body, so the eye barely clears the origin at all. A virus
    /// moves its eye between the two: low on the mote, up on the hull of a worn host.
    /// </summary>
    public float EyeHeight => Soldier != null ? SoldierRig.EyeHeight
                            : Fish != null ? FishRig.EyeHeight
                            : Virus != null ? Virus.EyeHeight
                            : Config.CameraHeight;

    // --- Rockets: the SOLDIER's right trigger --------------------------------
    // Carried, not drawn from the magazine, and deliberately few: a rocket is the only
    // thing on this chassis that can take a building down, which means it is also the
    // only thing that can take away the anchor the player is currently hanging from.

    public int MaxRockets = 6;
    public int Rockets;

    /// <summary>
    /// True while something is holding the craft in place but leaving it otherwise in
    /// control — currently only the SPIDER winding its lance. Distinct from
    /// <see cref="Captured"/>: a rooted craft still runs its own physics (so it coasts
    /// to a stop rather than stopping dead), still cools its guns, still falls if it was
    /// airborne. It simply stops accepting drive and turn input.
    /// </summary>
    public bool Rooted;

    // --- The TANK's siege kit ---------------------------------------------------
    // Everything the heavy chassis gained the day it gave up the jump. A tank never
    // leaves the grid, so it owns the grid instead: it digs in, it lurches, it crushes,
    // it blinds the field with smoke and it punches through cover. Every piece is gated
    // on Class == Tank at its own entry point; the state lives here because it is part of
    // the craft's own clock — a plant holds, a lurch decays and the dischargers cool
    // whether or not anyone is watching the HUD.

    /// <summary>
    /// Dug in: tracks locked, gun craning the full way up, front plate turned to the
    /// threat. A firing stance the tank trades all of its mobility for — and, being
    /// anchored, one the Crab-Core is too weak to lift (see <see cref="CrabSeizure.CanSeize"/>).
    /// Tank-only; toggled by <see cref="TogglePlant"/>, force-dropped by <see cref="Unplant"/>.
    /// </summary>
    public bool Planted { get; private set; }

    /// <summary>Broadband view shake, 0..1 — a lurch kicking off, a ram connecting. Read by
    /// the Renderer exactly as the FISH's and SOLDIER's Shake are, and rung down here on the
    /// craft's own clock so it settles whatever the camera is otherwise doing.</summary>
    public float Shake { get; private set; }
    public void Jolt(float amount) => Shake = MathF.Min(1f, MathF.Max(Shake, amount));

    // The lurch: a violent track-boost dodge, paid for out of the same Hyper the jump used
    // to cost. Carried as its own decaying world velocity rather than folded into _speed, so
    // it can briefly throw the craft well past its own top speed and can't be clamped away
    // by the drive governor. This is the tank's dodge now the air is closed to it.
    private Vector2 _lurchVel;
    private float _lurchTime;
    private const float LurchSpeed = 68f;     // peak surge — well over TopSpeed
    private const float LurchTime = 0.34f;    // how long it lasts
    private const float LurchDecay = 3f;      // gentle bleed so the dash eases rather than stops dead
    private const float LurchHyperCost = JumpHyperCost;   // a quarter-bar, the old jump's price

    // Smoke dischargers: a bank of screening smoke vented to blind the field. Not a Hyper
    // move — its own slow cooldown, because it is a break-contact tool and sharing the lurch's
    // meter would force a choice between dodging and hiding that neither should have to make.
    private float _smokeCooldown;
    public const float SmokeInterval = 9f;
    public bool SmokeReady => _smokeCooldown <= 0f;
    public float SmokeCooldownFraction => Math.Clamp(1f - _smokeCooldown / SmokeInterval, 0f, 1f);

    // The AP slug: a heavy piercing round that eats a fistful of the magazine at once and
    // rides a longer cooldown, so firing one is a decision the way the soldier's rockets are.
    public const int SlugAmmoCost = 5;
    private const float SlugInterval = 1.1f;

    /// <summary>The velocity the craft actually travelled this tick, world units a second —
    /// drive plus strafe plus any live lurch. The world reads it for the ram: the hull only
    /// crushes what it is genuinely driving into. Meaningful on the machine chassis; left at
    /// whatever it was on the bodies, which never ram.</summary>
    public Vector2 DriveVelocity { get; private set; }

    /// <summary>The ram's speed floor: below this the hull just bumps, above it it crushes.
    /// Keyed off the <em>const</em> top speed rather than the build's, so a committed charge or
    /// any lurch always rams while an idle roll never does — and a slow build still rams,
    /// because its lurch clears this easily.</summary>
    public float RamThreshold => MaxSpeed * 0.6f;

    /// <summary>How much faster the tank reloads while planted — a dug-in gun crew working the
    /// breech instead of driving. Folded into every one of the chassis's cooldowns.</summary>
    private float ReloadScale => Planted ? PlantedReloadScale : 1f;
    private const float PlantedReloadScale = 0.6f;

    // Directional armour: the tank's passive. A sloped glacis turns a frontal shot, the flanks
    // take it square, and the thin rear plate takes it worse — so which way the hull is facing
    // when a round arrives is the whole difference between builds. Planting turns the front
    // harder still. Applied only to attributable fire (see World.ArmorMultiplierFromShot).
    private const float FrontArmor = 0.55f;
    private const float FrontArmorPlanted = 0.35f;
    private const float RearArmor = 1.5f;

    // Hyper Engine: the reserve for tactical moves. Slowly refills on its own,
    // so jumping and hyperspacing are rationed, not free. Jump takes a quarter of
    // the bar (the SPIDER's move — a TANK cannot jump at all); hyperspace drains
    // almost all of it to panic-warp across the map.
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

    /// <summary>
    /// The SOLDIER's rifle cadence: 600 rounds a minute, against the cannon's roughly
    /// 170. It is a different weapon on a different body — a person with a rifle, not a
    /// craft with a gun — and firing it has to be usable mid-swing without breaking the
    /// arc, which a third-of-a-second between shots simply is not.
    /// </summary>
    private const float RifleInterval = 0.1f;

    /// <summary>What a rocket costs in reload time. Long enough that the six carried
    /// rounds can't be emptied into one wall in a second.</summary>
    private const float RocketInterval = 0.85f;

    /// <summary>
    /// How fast the gas reserve refills for a soldier. Faster than the tank's Hyper
    /// trickle, because on this chassis the reserve is not a panic button held for
    /// emergencies — it is the engine, and it is drained continuously by the thing the
    /// player does every few seconds.
    /// </summary>
    private const float SoldierGasRegen = 11f;

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
        if (Class == PlayerClass.Soldier)
        {
            Soldier = new SoldierRig();
            Rockets = MaxRockets;
        }
        if (Class == PlayerClass.Fish) Fish = new FishRig();
        if (Class == PlayerClass.Virus) Virus = new VirusRig();

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
        _strafe = 0f;
        _verticalVel = 0f;
        // The soldier's momentum lives in the rig, and a cinematic that has just put the
        // player back down must not hand them back a velocity from before it grabbed
        // them. Both cables go with it: whatever they were holding is a long way away.
        if (Soldier is { } rig)
        {
            rig.Velocity = Vector3.Zero;
            rig.ReleaseBoth();
        }
        // Same for a fish, which carries all of its momentum in the water rather than in
        // the fields above. A cinematic that has just put one back down must not hand it
        // back the sprint it was in the middle of several seconds ago.
        if (Fish is { } body) body.Velocity = Vector3.Zero;
        // ...and a virus, whose flight momentum lives in its own rig for the same reason.
        if (Virus is { } mote) mote.Velocity = Vector3.Zero;
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

        // The SOLDIER's rig owns the transform outright — it is a different set of
        // physics, not a modifier on these ones. It wraps its own position and lands
        // its own landings, so nothing below runs for that chassis.
        if (Soldier is { } rig)
        {
            rig.Step(dt, this);
            if (Hyper < MaxHyper)
                Hyper = MathF.Min(MaxHyper, Hyper + SoldierGasRegen * dt);
            return;
        }

        // The FISH's body owns the transform for the same reason, and its reserve refills
        // on a different rule from every other chassis: only while the tail is <em>not</em>
        // beating. That one condition is what turns the class's movement into a rhythm —
        // burst, coast, burst — instead of a key held down.
        if (Fish is { } body)
        {
            body.Step(dt, this);
            if (body.Recovering && Hyper < MaxHyper)
                Hyper = MathF.Min(MaxHyper, Hyper + FishRig.GlideRegen * dt);
            return;
        }

        // The VIRUS owns the transform for the same reason, and reads no reserve at all: the
        // thing this class rations is not gas or breath but <em>bodies</em>, which live in
        // the rig's own decay clock rather than in the Hyper bar.
        if (Virus is { } mote)
        {
            mote.Step(dt, this);
            return;
        }

        UpdateDrive(dt);
        UpdateJump(dt);
        UpdateSiege(dt);   // plant hold, lurch decay, discharger cooldown, shake ring-down

        // The Hyper reserve creeps back up when it isn't being spent.
        if (Hyper < MaxHyper)
            Hyper = MathF.Min(MaxHyper, Hyper + HyperRegen * dt);

        // Planar motion: forward along the heading the mouse aims, a sideways step from the
        // strafe, and — on the tank — whatever a live lurch is throwing on top. The heading
        // itself is turned by the mouse (see Look), so there is no turn integration here any
        // more. The resulting velocity is stashed for the ram, which only bites when the hull
        // is genuinely moving into something.
        Vector2 fwd = Forward;                    // (sin h, cos h)
        var right = new Vector2(-fwd.Y, fwd.X);   // screen-right on the plane, as the soldier uses it
        Vector2 velocity = fwd * _speed + right * _strafe + _lurchVel;
        DriveVelocity = velocity;
        Position += velocity * dt;

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

        // Along the craft's own heading, which the mouse now aims — the caller pairs this
        // with GunElevation for the vertical half of the same look.
        direction = Forward;
        origin = Position + direction * (Radius + 0.6f); // out past the muzzle
        // Fired mid-jump, the bolt leaves the barrel level with the leaping craft
        // and sails out at that height — the airborne shot that can reach the boss's
        // raised core.
        launchHeight = Projectile.BoltHeight + Height;
        _fireCooldown = FireInterval * ReloadScale;
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
        _fireCooldown = GrenadeInterval * ReloadScale;
        Ammo -= GrenadeAmmoCost;
        return true;
    }

    // --- The TANK's siege triggers ---------------------------------------------

    /// <summary>
    /// Toggles the siege plant. Tank-only, and refused off the grid (a tank has no air of
    /// its own, but a cinematic can loft one). Planting kills all momentum and locks the
    /// tracks; unplanting hands the drive back and re-clamps the gun to its shallow stop so
    /// a shot fired on the way out can't leave at an angle the moving chassis can't hold.
    /// Returns the resulting state so the caller can voice the clank.
    /// </summary>
    public bool TogglePlant()
    {
        if (Class != PlayerClass.Tank || IsAirborne) return Planted;
        Planted = !Planted;
        if (Planted)
        {
            _speed = 0f;
            _strafe = 0f;
            _lurchTime = 0f;
            _lurchVel = Vector2.Zero;
        }
        else
        {
            Pitch = Math.Clamp(Pitch, -TurretElevation, TurretElevation);
        }
        return Planted;
    }

    /// <summary>Force-drops the plant — what opening the crafting panel does, so a player
    /// can't leave a stance locked behind an overlay they're busy dragging items around in.</summary>
    public void Unplant()
    {
        if (!Planted) return;
        Planted = false;
        Pitch = Math.Clamp(Pitch, -TurretElevation, TurretElevation);
    }

    /// <summary>
    /// Fires the lurch: a track-boost dash in the current drive direction (straight ahead
    /// with no input), paid for out of the Hyper reserve. Tank-only, grounded-only, and
    /// refused while planted, already lurching, or too drained. Returns true when it kicks,
    /// so the world can shove the hull and sound the thrust.
    /// </summary>
    public bool TryLurch()
    {
        if (Class != PlayerClass.Tank || Planted || IsAirborne) return false;
        if (_lurchTime > 0f || Hyper < LurchHyperCost) return false;

        // Direction off the live drive keys, in world space; default straight ahead.
        Vector2 fwd = Forward;
        var right = new Vector2(-fwd.Y, fwd.X);
        Vector2 dir = Vector2.Zero;
        if (InputMap.Forward) dir += fwd;
        if (InputMap.Back) dir -= fwd;
        if (InputMap.TurnRight) dir += right;
        if (InputMap.TurnLeft) dir -= right;
        if (dir.LengthSquared() < 1e-4f) dir = fwd;
        dir = Vector2.Normalize(dir);

        _lurchVel = dir * LurchSpeed;
        _lurchTime = LurchTime;
        Hyper -= LurchHyperCost;
        Jolt(0.5f);
        return true;
    }

    /// <summary>Vents the smoke dischargers if they've cooled. Tank-only. Returns true when a
    /// screen is actually laid, so the world can place the clouds and sound the hiss; the
    /// clouds themselves are the world's to age. Spends the cooldown on success.</summary>
    public bool TryDeploySmoke()
    {
        if (Class != PlayerClass.Tank || _smokeCooldown > 0f) return false;
        _smokeCooldown = SmokeInterval;
        return true;
    }

    /// <summary>
    /// The AP slug: a heavy piercing round down the gun line. Costs <see cref="SlugAmmoCost"/>
    /// from the magazine at once and paces on a longer cooldown than the cannon. Tank-only.
    /// The piercing and the wall-punch live in the world's projectile pass — this only meters
    /// the shot and hands back the muzzle line, along the same <see cref="GunElevation"/> the
    /// cannon uses (so a planted tank lobs its slug up the full crane like everything else).
    /// </summary>
    public bool TryFireSlug(out Vector2 origin, out Vector2 direction, out float launchHeight)
    {
        origin = default;
        direction = Forward;
        launchHeight = Projectile.BoltHeight + Height;
        if (Class != PlayerClass.Tank || _fireCooldown > 0f || Ammo < SlugAmmoCost) return false;
        origin = Position + direction * (Radius + 0.6f);
        _fireCooldown = SlugInterval * ReloadScale;
        Ammo -= SlugAmmoCost;
        return true;
    }

    /// <summary>
    /// The tank's plating multiplier for a shot travelling along <paramref name="shotVelocity"/>.
    /// A sloped glacis shrugs a frontal hit down, the flanks take it in full, and the thin
    /// rear plate takes it worse; planting turns the front harder still. Non-tank chassis have
    /// no plating and always take the whole blow (returns 1). This is why facing is the tank's
    /// discipline: a hull that keeps its nose to the threat is a different amount of alive from
    /// one caught from behind.
    /// </summary>
    public float ArmorMultiplierFromShot(Vector2 shotVelocity)
    {
        if (Class != PlayerClass.Tank) return 1f;
        if (shotVelocity.LengthSquared() < 1e-6f) return 1f;
        // The round travels toward the hull, so the plate it meets faces back up the incoming
        // line — the source direction is the negated velocity.
        Vector2 srcDir = Vector2.Normalize(-shotVelocity);
        float align = Vector2.Dot(srcDir, Forward);   // +1 dead ahead, -1 dead behind
        if (align > 0.5f) return Planted ? FrontArmorPlanted : FrontArmor;
        if (align < -0.5f) return RearArmor;
        return 1f;
    }

    // --- The SOLDIER's two triggers -------------------------------------------
    // Both fire along the eye's full 3D line rather than along a heading, because on
    // this chassis the mouse is the aim and the aim is very often nothing like where
    // the body is travelling. Both are usable mid-swing: nothing here touches the rig.

    /// <summary>
    /// A rifle round. Costs one from the magazine and paces itself at
    /// <see cref="RifleInterval"/>. The origin is set out past the eye so the round
    /// never spawns inside the player's own collision radius.
    /// </summary>
    public bool TryFireRifle(out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = Forward3;
        if (_fireCooldown > 0f || Ammo <= 0) return false;

        origin = Eye + direction * (Radius + 0.4f);
        _fireCooldown = RifleInterval;
        Ammo--;
        return true;
    }

    /// <summary>
    /// A rocket. Spends one of the carried few — never magazine rounds, so a soldier out
    /// of bullets still has the thing that opens a wall, and a soldier out of rockets
    /// still has a gun.
    /// </summary>
    public bool TryFireRocket(out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = Forward3;
        if (_fireCooldown > 0f || Rockets <= 0) return false;

        origin = Eye + direction * (Radius + 0.8f);
        _fireCooldown = RocketInterval;
        Rockets--;
        return true;
    }

    /// <summary>Restocks rockets, capped at what the rig carries — what a supply point
    /// and a battery cell top up alongside the reserve.</summary>
    public void RefillRockets(int count) => Rockets = Math.Min(MaxRockets, Rockets + count);

    /// <summary>
    /// The FISH's spit: a fast, weak round down the eye's line. It exists to be usable
    /// <em>mid-carve</em> and for no other reason — the strike is where this chassis's
    /// damage actually lives, and the spit is what keeps a player who is drifting through
    /// a reef at thirty metres a second from being unarmed until they can line one up.
    ///
    /// Costs a magazine round like everything else, so the ammo track and the bullet
    /// salvage go on meaning what they mean on every other chassis.
    /// </summary>
    public bool TryFireSpit(out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = Forward3;
        if (_fireCooldown > 0f || Ammo <= 0) return false;

        origin = Eye + direction * (Radius + 0.4f);
        _fireCooldown = SpitInterval;
        Ammo--;
        return true;
    }

    /// <summary>
    /// Cadence of the spit — a shade slower than the soldier's rifle. It is a body doing
    /// this rather than a weapon, and the class already has a heavy attack, so this one is
    /// deliberately chip damage rather than a second kill button.
    /// </summary>
    private const float SpitInterval = 0.14f;

    /// <summary>
    /// The VIRUS's round, down the eye's line — the mote's spit and the worn host's cannon
    /// are the same corruption bolt thrown by whatever the player currently is. Identical in
    /// damage and cadence in both states on purpose: this class's power is positional and
    /// explosive (survive by hopping bodies, spend one as an overload), never raw firepower,
    /// exactly the way the SPIDER's laser is "the same damage as the bullet". Costs a
    /// magazine round like everything else, so the AMMO track goes on meaning what it means.
    /// </summary>
    public bool TryFireVirus(out Vector3 origin, out Vector3 direction)
    {
        origin = default;
        direction = Forward3;
        if (_fireCooldown > 0f || Ammo <= 0) return false;

        origin = Eye + direction * (Radius + 0.4f);
        _fireCooldown = VirusFireInterval;
        Ammo--;
        return true;
    }

    /// <summary>Cadence of the virus round — the soldier's rifle pace, so it is usable in the
    /// middle of a dart or a drive without ever being a stream that trivialises the field.</summary>
    private const float VirusFireInterval = 0.11f;

    /// <summary>
    /// Panic-warp: drains the bulk of the Hyper reserve and flings the craft to a
    /// random spot within range. Blocked (returns false) when the reserve is too
    /// low. A grounded craft only — you can't warp mid-jump.
    /// </summary>
    public bool TryHyperspace()
    {
        // A dug-in tank can't warp — you're anchored, not mobile. Unplant first.
        if (Hyper < HyperspaceCost || IsAirborne || Planted) return false;

        // Random bearing and distance — a genuine gamble, not a controlled blink.
        float angle = Random.Shared.NextSingle() * MathF.Tau;
        float dist = TeleportRange * (0.4f + 0.6f * Random.Shared.NextSingle());
        Position = Torus.Wrap(Position + new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * dist);

        Hyper -= HyperspaceCost;
        _speed = 0f;        // the warp kills momentum — you arrive dead-stopped
        _strafe = 0f;
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

    /// <summary>
    /// The two throttles: W/S along the heading, A/D across it. The heading itself is the
    /// mouse's now, so nothing here turns the craft — A and D that used to steer step the
    /// hull sideways instead. Both carry momentum, so the craft leans into a move and
    /// coasts out of it rather than snapping, which is the whole of how the class drives.
    /// </summary>
    private void UpdateDrive(float dt)
    {
        // Rooted (the spider winding its lance) and planted (the tank dug in) both refuse
        // the throttle — the craft coasts to a stop under the drag below and takes no drive.
        bool locked = Rooted || Planted;

        // Forward / back.
        float fwd = 0f;
        if (!locked && InputMap.Forward) fwd += 1f;
        if (!locked && InputMap.Back) fwd -= 1f;

        if (fwd > 0f)
            _speed += Accel * _speedScale * dt;
        else if (fwd < 0f)
            _speed -= Accel * _speedScale * ReverseFactor * dt;
        else
            _speed = MoveToward(_speed, 0f, Drag * dt);

        float top = TopSpeed;
        _speed = Math.Clamp(_speed, -top * ReverseFactor, top);

        // Strafe. The keys that once turned the craft — TurnLeft/TurnRight, so a rebind
        // or a turn-swap still moves the hand the player expects — now shove it sideways.
        // Left steps toward the craft's screen-right-negated side; the sign matches the
        // (-fwd.Y, fwd.X) right axis the integrator applies it along, so D is right.
        float lat = 0f;
        if (!locked && InputMap.TurnRight) lat += 1f;
        if (!locked && InputMap.TurnLeft) lat -= 1f;

        if (lat != 0f)
            _strafe += lat * Accel * _speedScale * dt;
        else
            _strafe = MoveToward(_strafe, 0f, Drag * dt);

        float topLat = top * StrafeFactor;
        _strafe = Math.Clamp(_strafe, -topLat, topLat);
    }

    /// <summary>
    /// Attempts the machine hop: refused while rooted, airborne or too drained to pay
    /// for it — and refused outright on the TANK, which has no answer to gravity.
    /// Treads never leave the grid, and the class reads better for admitting it: its
    /// escape is the hyperspace gamble, not a leap. The SPIDER keeps the hop — legs
    /// are legs. The falling half of the arc below stays live for every chassis,
    /// because a cinematic can still put a tank in the air and it has to come down.
    /// </summary>
    public bool TryJump()
    {
        if (Class == PlayerClass.Tank) return false;
        if (Rooted || IsAirborne || Hyper < JumpHyperCost) return false;
        _verticalVel = JumpVel;
        Hyper -= JumpHyperCost;
        return true;
    }

    private void UpdateJump(float dt)
    {
        // Jumping is a Hyper move: a quarter of the bar, and simply refused if the
        // reserve can't pay — or if the chassis is a TANK, which never can. See TryJump.
        if (InputMap.JumpPressed) TryJump();

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

    /// <summary>
    /// Runs the tank's siege clocks: the discharger cooldown, the shake ring-down, and the
    /// lurch — which bleeds its surge out over its short life so the dash eases rather than
    /// stopping dead. All harmless no-ops on a craft that never plants, lurches or rams, so
    /// it costs nothing to run for the SPIDER that shares this integrator.
    /// </summary>
    private void UpdateSiege(float dt)
    {
        if (_smokeCooldown > 0f) _smokeCooldown -= dt;
        if (Shake > 0f)
        {
            Shake -= dt * 4f;
            if (Shake < 0f) Shake = 0f;
        }

        if (_lurchTime > 0f)
        {
            _lurchTime -= dt;
            _lurchVel *= MathF.Exp(-LurchDecay * dt);
            if (_lurchTime <= 0f) _lurchVel = Vector2.Zero;
        }
    }

    /// <summary>Normalized forward vector on the plane (X, Z).</summary>
    public Vector2 Forward => new(MathF.Sin(Heading), MathF.Cos(Heading));

    /// <summary>
    /// The full look direction, pitch included — the craft's heading laid over its eye
    /// elevation. Everything that fires down the eye rather than flat along the plane
    /// leaves along this.
    /// </summary>
    public Vector3 Forward3
    {
        get
        {
            float cp = MathF.Cos(Pitch);
            return new Vector3(MathF.Sin(Heading) * cp, MathF.Sin(Pitch), MathF.Cos(Heading) * cp);
        }
    }

    /// <summary>Where the eye actually is in the world, which is where a first-person
    /// weapon fires from and where a cable leaves the rig.</summary>
    public Vector3 Eye => new(Position.X, EyeHeight + Height, Position.Y);

    /// <summary>0..1 speed fraction — drives engine-hum pitch later (Doc 04). On a
    /// soldier it reads the rig's real planar travel against the same ceiling, so the
    /// number means the same thing on both chassis even though nothing about how it is
    /// arrived at is shared.</summary>
    public float SpeedFraction => Soldier is { } rig
        ? Math.Clamp(rig.PlanarSpeed / TopSpeed, 0f, 1f)
        : Fish is { } body
        ? Math.Clamp(body.PlanarSpeed / TopSpeed, 0f, 1f)
        : Virus is { } mote
        ? Math.Clamp(mote.PlanarSpeed / TopSpeed, 0f, 1f)
        : Math.Abs(_speed) / TopSpeed;

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
