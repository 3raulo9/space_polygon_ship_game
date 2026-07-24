using System.Numerics;
using VoidTanks.Core;

namespace VoidTanks.Entities;

/// <summary>
/// The SPIDER chassis's armament — the salvaged Crab-Core's own two weapons, cut down
/// to something a person can carry.
///
/// Left trigger throws small lasers: no wind-up, the same bite as a cannon round, paced
/// by the same cooldown, so the ordinary moment-to-moment of the class is the tank's
/// rhythm with a different-looking bolt. Right trigger is the lance: hold it and the
/// meter fills 0 → 100 over a couple of seconds, and the craft is <em>rooted</em> the
/// whole time — the charge is paid for in the one currency this game actually charges
/// in, which is the ability to walk away. Let go and whatever was in the meter comes
/// out as a beam.
///
/// Pure state and timing; the world applies the damage and the renderer draws the light,
/// so the whole thing stays testable without a screen.
/// </summary>
public sealed class SpiderWeapon
{
    public const float MaxCharge = 100f;

    /// <summary>Meter points per second — a full charge takes ~2.5 seconds of standing
    /// perfectly still in the open, which is the trade.</summary>
    public const float ChargeRate = 40f;

    /// <summary>
    /// Below this the release is a fizzle, not a shot: no beam, no ammo spent.
    /// Stops a stray right-click from wasting rounds on a beam that does nothing.
    ///
    /// Raised to a third of the meter from the sixth it sat at, because the old floor
    /// was worse than either of the things it could have been. Half a second of holding
    /// bought a beam that dealt about a point and a half for a whole round and a rooted
    /// frame — not a snap shot worth taking, and not a clean refusal either, just a
    /// quiet waste. At this figure the bottom of the meter is a decision: you either
    /// commit to about three quarters of a second and get a shot that means something,
    /// or you let go and lose nothing at all.
    /// </summary>
    public const float MinCharge = 32f;

    /// <summary>How long the shaft burns. Purely visual — the damage is applied once,
    /// on the frame it fires (see <see cref="World.World"/>), so a target can't be
    /// double-billed by standing in a beam that has already hit it.</summary>
    public const float BeamTime = 0.6f;

    /// <summary>
    /// Reach of the lance at an empty meter and at a full one. Well short of the boss's
    /// 150-unit shaft at the top: this is a salvaged core, not the thing it was cut out
    /// of.
    ///
    /// It scales because a meter that only bought damage was a meter that only ever
    /// showed up as a number. A charge you can watch getting <em>longer and wider</em>
    /// tells the player what they are buying while they are buying it, and it separates
    /// the two ways the weapon is actually used: a short hold is a room-clearing swipe
    /// at what is already on top of you, and a full one is a siege shot that reaches
    /// across the engagement — and, at the top of the meter, out past the range a hunter
    /// will stand off and shoot you from, which the flat 95 never did.
    /// </summary>
    public const float MinBeamLength = 55f;
    public const float MaxBeamLength = 132f;

    /// <summary>
    /// Half-width of the damaging shaft, likewise scaled by the meter. Kept well under
    /// the boss's 2.4 even at full charge, for a reason that only shows up in first
    /// person: this beam leaves an emitter a couple of units in front of the player's
    /// own eye, so its near end is the widest thing in the frame by a long way. At the
    /// boss's width the shaft fills half the screen and the player fires blind through
    /// their own weapon.
    /// </summary>
    public const float MinBeamRadius = 0.6f;
    public const float MaxBeamRadius = 1.4f;

    /// <summary>The reach and the width a beam of a given 0..1 charge actually gets —
    /// one place, so the shaft that is drawn and the shaft that burns are the same
    /// shaft.</summary>
    public static float LengthAt(float power)
        => MinBeamLength + (MaxBeamLength - MinBeamLength) * Math.Clamp(power, 0f, 1f);

    public static float RadiusAt(float power)
        => MinBeamRadius + (MaxBeamRadius - MinBeamRadius) * Math.Clamp(power, 0f, 1f);

    /// <summary>The live beam's own reach and width, from the charge it went off at.</summary>
    public float BeamLength => LengthAt(BeamPower);
    public float BeamRadius => RadiusAt(BeamPower);

    // Lasers deliberately have no cooldown of their own: they go out through the
    // craft's ordinary cannon path (PlayerTank.TryFire), so they inherit its interval
    // and its one-round cost. The class's moment-to-moment damage is therefore exactly
    // the tank's, which is what "same damage as the bullet" has to mean to be true.

    /// <summary>
    /// Rounds a full-charge lance costs, on top of the one every release pays. The bill
    /// scales with the meter, so a snap shot is nearly free and a held 100 costs five.
    ///
    /// Halved from the ten it used to be, because the old price made the weapon
    /// arithmetically pointless against anything it could not line two targets up
    /// through: a full lance dealt eight for ten rounds where ten laser shots dealt ten
    /// for the same ten rounds and cost no root at all. The <em>root</em> is what a
    /// lance is supposed to cost — two and a half seconds standing still in the open is
    /// the most expensive thing this game asks of anybody — and charging twice for one
    /// decision meant the interesting half of it never got taken. At five, a full shot
    /// is worth its rounds on a single target and worth rather more than them on a line,
    /// which is what a siege weapon should be.
    /// </summary>
    public const int MaxBeamAmmo = 5;

    /// <summary>Damage a lance deals at an empty meter, and at a full one. Anything on
    /// the shaft takes the whole amount once. Full charge is worth eight cannon rounds
    /// — enough to put down the Crab-Core's four-point core in a single shot, if the
    /// player is willing to stand still in front of it for two and a half seconds.</summary>
    public const float MinDamage = 1f;
    public const float MaxDamage = 8f;

    /// <summary>Where the lance leaves the chassis, relative to the craft — out past
    /// the nose and at about chest height, so the shaft reads as coming from the core
    /// in the middle of the body rather than from the player's feet.</summary>
    public const float MuzzleForward = PlayerTank.Radius + 3.2f;
    public const float MuzzleHeight = 1.6f;

    /// <summary>How far the charge flare is scaled down from the boss's — see the
    /// <c>flare</c> parameter on <c>CrabRenderer.DrawLance</c>. Half size, because the
    /// emitter is a few units in front of the player's own eye rather than across the
    /// arena from it.</summary>
    public const float FlareScale = 0.5f;

    /// <summary>The meter, 0..100. What the HUD's right-hand gauge shows.</summary>
    public float Charge { get; private set; }

    /// <summary>True while the trigger is held and the meter is filling. The craft is
    /// rooted for exactly as long as this is set.</summary>
    public bool Charging { get; private set; }

    /// <summary>Charge the live beam was fired at, 0..1 — scales its damage and how
    /// fat the shaft draws.</summary>
    public float BeamPower { get; private set; }

    public Vector3 BeamOrigin { get; private set; }
    public Vector3 BeamDirection { get; private set; } = new(0f, 0f, 1f);

    private float _beamTimer;

    public bool BeamActive => _beamTimer > 0f;

    /// <summary>0 at ignition … 1 as it cuts out — what the renderer's envelope reads.</summary>
    public float BeamProgress => BeamActive ? 1f - _beamTimer / BeamTime : -1f;

    /// <summary>The meter as a 0..1 fraction, for the HUD gauge.</summary>
    public float ChargeFraction => Math.Clamp(Charge / MaxCharge, 0f, 1f);

    /// <summary>Rounds the current meter would cost to loose. Always at least one.</summary>
    public int AmmoCost => Math.Max(1, (int)MathF.Round(ChargeFraction * MaxBeamAmmo));

    /// <summary>What the current meter would deal to everything on the shaft.</summary>
    public float Damage => MinDamage + (MaxDamage - MinDamage) * ChargeFraction;

    // --- The brace ---------------------------------------------------------------
    // Winding the lance used to be two and a half seconds in which nothing happened
    // except a bar filling: no stance, no decision, nothing for the player to do and
    // nothing for the field to do about it. It is a stance now, and it cuts both ways.
    // Planting six legs and turning the carapace to the threat genuinely hardens
    // everything that isn't the core (see PlayerTank.ArmorMultiplierFromShot) — but a
    // round that finds the core while the meter is up breaks the charge outright and
    // locks the emitter out for a beat. So where you root is the skill, and putting
    // something solid at your back is the answer, which is the first time this class has
    // had a reason to care about the city it walks through.

    /// <summary>True while the legs are planted and the shell is turned — which is
    /// exactly while the meter is filling.</summary>
    public bool Braced => Charging;

    /// <summary>Seconds the emitter stays dead after a break. Short: the punishment is
    /// losing the two seconds you already spent, not the two after it.</summary>
    public const float BreakLock = 0.7f;

    private float _breakLock;

    /// <summary>True while a broken charge is still locked out — what the HUD flashes
    /// and what refuses a fresh hold.</summary>
    public bool Broken => _breakLock > 0f;

    /// <summary>0..1 through the lockout, for the gauge's stutter.</summary>
    public float BreakFraction => Math.Clamp(_breakLock / BreakLock, 0f, 1f);

    /// <summary>
    /// A part-charge set aside rather than spent. The panel and the cinematics stow the
    /// meter instead of dumping it — a player who opened their inventory mid-wind should
    /// not be billed two seconds for it — and the next hold picks it back up. A
    /// <see cref="Break"/> is the one thing that takes it away, because being shot in the
    /// core is supposed to cost something.
    /// </summary>
    private float _stowed;

    /// <summary>Ages the live beam and the break lockout. Called every fixed step whatever
    /// else is happening to the craft, exactly as the cannon's cooldown is.</summary>
    public void Update(float dt)
    {
        if (_beamTimer > 0f) _beamTimer = MathF.Max(0f, _beamTimer - dt);
        if (_breakLock > 0f) _breakLock = MathF.Max(0f, _breakLock - dt);
    }

    /// <summary>
    /// Winds the meter up by one step. Holding is the whole input: the world calls this
    /// every step the right trigger is down and <see cref="Release"/> the step it isn't.
    /// Refused outright while the emitter is locked out from a break — the trigger is
    /// dead for that beat, and the craft is not rooted by it either.
    /// </summary>
    public void Hold(float dt)
    {
        if (Broken) return;
        if (!Charging && _stowed > 0f)
        {
            // Picking up a wind that was interrupted by something that wasn't a shot.
            Charge = _stowed;
            _stowed = 0f;
        }
        Charging = true;
        Charge = MathF.Min(MaxCharge, Charge + ChargeRate * dt);
    }

    /// <summary>
    /// A round has found the core while the meter was up. The charge is gone — not
    /// stowed, gone — and the emitter is dead for <see cref="BreakLock"/>. Returns true
    /// when there was actually a wind to break, so the world only sounds the fault and
    /// jolts the hull on the shot that did something.
    /// </summary>
    public bool Break()
    {
        if (!Charging) return false;
        Charging = false;
        Charge = 0f;
        _stowed = 0f;
        _breakLock = BreakLock;
        return true;
    }

    /// <summary>
    /// Lets go. Returns true when the meter had enough in it to actually fire, in which
    /// case the beam is now burning from <paramref name="origin"/> along
    /// <paramref name="direction"/> and <see cref="BeamPower"/> holds what it went off
    /// at. Either way the meter is emptied — you don't get to keep a part-charge.
    /// </summary>
    public bool Release(Vector3 origin, Vector3 direction, out float power)
    {
        power = ChargeFraction;
        bool fires = Charging && Charge >= MinCharge;
        Charging = false;
        Charge = 0f;
        _stowed = 0f;    // a release is a decision; nothing is carried out of it either way
        if (!fires) return false;

        BeamPower = power;
        BeamOrigin = origin;
        BeamDirection = Vector3.Normalize(direction);
        _beamTimer = BeamTime;
        return true;
    }

    /// <summary>
    /// Puts the charge down without firing — used when the run takes the wheel away (a
    /// cinematic grabs the player, the inventory panel opens, the claw needs the hand).
    /// The craft stops being rooted and nothing comes out of the emitter, but the meter
    /// is <em>stowed</em> rather than dumped: the next hold picks it up where it left
    /// off. None of these are the player's doing, and none of them should cost them the
    /// two seconds they already stood still for. Being shot in the core is the one thing
    /// that takes a wind away — see <see cref="Break"/>.
    /// </summary>
    public void Cancel()
    {
        if (Charging) _stowed = Charge;
        Charging = false;
        Charge = 0f;
    }

    /// <summary>Distance from a point to the live shaft's axis, clamped to its length —
    /// the same test the renderer's cylinder is drawn along, so what looks like standing
    /// in the light is standing in the light.</summary>
    public float MissDistance(Vector3 target)
    {
        float along = Math.Clamp(Vector3.Dot(target - BeamOrigin, BeamDirection),
            0f, BeamLength);
        return Vector3.Distance(target, BeamOrigin + BeamDirection * along);
    }
}
