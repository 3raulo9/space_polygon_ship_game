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

    /// <summary>Below this the release is a fizzle, not a shot: no beam, no ammo spent.
    /// Stops a stray right-click from wasting rounds on a beam that does nothing.</summary>
    public const float MinCharge = 15f;

    /// <summary>How long the shaft burns. Purely visual — the damage is applied once,
    /// on the frame it fires (see <see cref="World.World"/>), so a target can't be
    /// double-billed by standing in a beam that has already hit it.</summary>
    public const float BeamTime = 0.6f;

    /// <summary>Reach of the lance. Well short of the boss's 150-unit shaft: this is a
    /// salvaged core, not the thing it was cut out of.</summary>
    public const float BeamLength = 95f;

    /// <summary>
    /// Half-width of the damaging shaft. Kept well under the boss's 2.4 for a reason
    /// that only shows up in first person: this beam leaves an emitter a couple of
    /// units in front of the player's own eye, so its near end is the widest thing in
    /// the frame by a long way. At the boss's width the shaft fills half the screen and
    /// the player fires blind through their own weapon.
    /// </summary>
    public const float BeamRadius = 1.0f;

    // Lasers deliberately have no cooldown of their own: they go out through the
    // craft's ordinary cannon path (PlayerTank.TryFire), so they inherit its interval
    // and its one-round cost. The class's moment-to-moment damage is therefore exactly
    // the tank's, which is what "same damage as the bullet" has to mean to be true.

    /// <summary>Rounds a full-charge lance costs. The bill scales with the meter, so a
    /// snap shot at 20 is cheap and a held 100 costs what a grenade does.</summary>
    public const int MaxBeamAmmo = 10;

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

    /// <summary>Ages the live beam. Called every fixed step whatever else is happening
    /// to the craft, exactly as the cannon's cooldown is.</summary>
    public void Update(float dt)
    {
        if (_beamTimer > 0f) _beamTimer = MathF.Max(0f, _beamTimer - dt);
    }

    /// <summary>
    /// Winds the meter up by one step. Holding is the whole input: the world calls this
    /// every step the right trigger is down and <see cref="Release"/> the step it isn't.
    /// </summary>
    public void Hold(float dt)
    {
        Charging = true;
        Charge = MathF.Min(MaxCharge, Charge + ChargeRate * dt);
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
        if (!fires) return false;

        BeamPower = power;
        BeamOrigin = origin;
        BeamDirection = Vector3.Normalize(direction);
        _beamTimer = BeamTime;
        return true;
    }

    /// <summary>
    /// Drops the charge without firing — used when the run takes the wheel away (a
    /// cinematic grabs the player, the inventory panel opens). The meter empties rather
    /// than firing blind at whatever the craft happens to be pointed at.
    /// </summary>
    public void Cancel()
    {
        Charging = false;
        Charge = 0f;
    }

    /// <summary>Distance from a point to the live shaft's axis, clamped to its length —
    /// the same test the renderer's cylinder is drawn along, so what looks like standing
    /// in the light is standing in the light.</summary>
    public float MissDistance(Vector3 target)
    {
        float along = Math.Clamp(Vector3.Dot(target - BeamOrigin, BeamDirection), 0f, BeamLength);
        return Vector3.Distance(target, BeamOrigin + BeamDirection * along);
    }
}
