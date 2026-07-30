namespace VoidTanks.Core;

/// <summary>
/// A one-shot sound the simulation asks for, named rather than played directly. Every combat
/// noise in the game goes out as one of these through <see cref="World.World.Cue"/>, which is
/// what makes multiplayer audio possible: the host records the cue with a world position and
/// broadcasts it, and every machine plays it back attenuated to <em>its own</em> craft — so a
/// firefight two players are both near sounds right to both of them.
///
/// Only one-shots live here. The continuous beds (the crab's rotor, the soldier's wind, the
/// lance charge) stay local on every machine, driven from the state the snapshots already
/// carry — there is nothing to "fire", so nothing to send.
/// </summary>
public enum Cue : byte
{
    None = 0,

    // Guns and their rounds.
    Detonation,     // a cannon/enemy barrel firing
    Laser,          // a SPIDER laser leaving the emitter
    RifleShot,      // a SOLDIER rifle round
    RocketLaunch,   // a rocket leaving the tube
    RocketBlast,    // a rocket going off (dist)
    LanceFire,      // the SPIDER lance discharging
    UnstableLance,  // a worn/corrupted lance
    CrabCoreBlast,  // a thrown CRAB CORE going off
    ThrowWhoosh,    // a lobbed thing / a claw throw
    FishSpit,       // the FISH's spit
    FishStrike,     // the FISH's lunge connecting
    FishImpact,     // the FISH meeting a wall / round
    FishBeach,      // the FISH meeting the seabed (param = how hard, 0..1)
    MawSpit,        // one of the mouth's little lasers (dist)

    // Impacts and deaths.
    Explosion,      // a craft or hunter destroyed, up close
    ExplosionAt,    // a detonation heard from range (dist)
    Hit,            // a craft absorbing a hit
    Warning,        // shield crossing the low line
    CoreHit,        // a shot threading the crab's gem (param = severity 0..1)
    MawHurt,        // the mouth taking a shot from inside (param = severity)
    BossDeath,      // a boss coming apart
    CrashLanding,   // a craft coming down hard
    Pickup,         // salvage collected

    // The Crab-Core's protocol.
    Clamp,          // a claw-plate snapping / tracks locking
    Alarm,          // the threat-display lurch
    Footstep,       // a leg planting (param = leg index; dist)
    HuntCall,       // the hunting groan (dist)
    CrabScream,     // the seizure scream
    ClawSlam,       // the free claw's blow
    BeamCharge,     // the crystal spinning up
    BeamWarning,    // one of the three countdown warnings (param = step)
    BeamFire,       // the beam firing

    // The Maw-Core.
    MawSwallow,     // the throat closing on a craft
    MawDive,        // the mouth dropping
    MawRelease,     // the jaw throwing a craft clear

    // The SOLDIER's rig.
    GasJump,        // the gas kick of a jump (param = starvation 0..1)
    CableFire,      // a hook leaving the launcher
    CableZip,       // a cable coming home / a miss
    AnchorBite,     // steel biting in (dist)
    AnchorTear,     // an anchor tearing out

    // Structures.
    StructureGroan, // a support giving way (dist)
    StructureCrack, // masonry shearing where a beam bites (dist)
}

/// <summary>
/// Turns a <see cref="Cue"/> back into the right call on <see cref="Audio"/>, given how far it
/// is from the listener and its one packed parameter. The single place cue ids and the sound
/// bank meet, on both ends of the wire.
/// </summary>
public static class CueBank
{
    /// <summary>Whether a cue's parameter is a 0..1 fraction (severity, starvation) rather than
    /// a small whole number (a leg index, a warning step). The wire packs it to one byte either
    /// way; this decides whether to scale it by 255 on the way through.</summary>
    public static bool IsFraction(Cue id)
        => id is Cue.CoreHit or Cue.MawHurt or Cue.GasJump or Cue.FishBeach;

    /// <summary>
    /// Whether only the host can ever raise this cue. Nearly every noise in the game is
    /// raised by the machine that caused it — a client fires and hears its own shot at once,
    /// and then ignores the host's echo of it. Salvage is the exception: clients deliberately
    /// do not predict pickups, so a collect chime is decided on the host and nowhere else, and
    /// the owner has to be allowed to hear the echo or they never hear it at all.
    /// </summary>
    public static bool RaisedOnlyByHost(Cue id) => id is Cue.Pickup;

    public static void Play(Cue id, float distance, float param)
    {
        switch (id)
        {
            case Cue.Detonation: Audio.PlayDetonation(); break;
            case Cue.Laser: Audio.PlayLaser(); break;
            case Cue.RifleShot: Audio.PlayRifleShot(); break;
            case Cue.RocketLaunch: Audio.PlayRocketLaunch(); break;
            case Cue.RocketBlast: Audio.PlayRocketBlast(distance); break;
            case Cue.LanceFire: Audio.PlayLanceFire(); break;
            case Cue.UnstableLance: Audio.PlayUnstableLance(); break;
            case Cue.CrabCoreBlast: Audio.PlayCrabCoreBlast(); break;
            case Cue.ThrowWhoosh: Audio.PlayThrowWhoosh(); break;
            case Cue.FishSpit: Audio.PlayFishSpit(); break;
            case Cue.FishStrike: Audio.PlayFishStrike(); break;
            case Cue.FishImpact: Audio.PlayFishImpact(); break;
            case Cue.FishBeach: Audio.PlayFishBeach(param); break;
            case Cue.MawSpit: Audio.PlayMawSpit(distance); break;

            case Cue.Explosion: Audio.PlayExplosion(); break;
            case Cue.ExplosionAt: Audio.PlayExplosionAt(distance); break;
            case Cue.Hit: Audio.PlayHit(); break;
            case Cue.Warning: Audio.PlayWarning(); break;
            case Cue.CoreHit: Audio.PlayCoreHit(param); break;
            case Cue.MawHurt: Audio.PlayMawHurt(param); break;
            case Cue.BossDeath: Audio.PlayBossDeath(); break;
            case Cue.CrashLanding: Audio.PlayCrashLanding(); break;
            case Cue.Pickup: Audio.PlayPickup(); break;

            case Cue.Clamp: Audio.PlayClamp(); break;
            case Cue.Alarm: Audio.PlayAlarm(); break;
            case Cue.Footstep: Audio.PlayFootstep((int)param, distance); break;
            case Cue.HuntCall: Audio.PlayHuntCall(distance); break;
            case Cue.CrabScream: Audio.PlayCrabScream(); break;
            case Cue.ClawSlam: Audio.PlayClawSlam(); break;
            case Cue.BeamCharge: Audio.PlayBeamCharge(); break;
            case Cue.BeamWarning: Audio.PlayBeamWarning((int)param); break;
            case Cue.BeamFire: Audio.PlayBeamFire(); break;

            case Cue.MawSwallow: Audio.PlayMawSwallow(); break;
            case Cue.MawDive: Audio.PlayMawDive(); break;
            case Cue.MawRelease: Audio.PlayMawRelease(); break;

            case Cue.GasJump: Audio.PlayGasJump(param); break;
            case Cue.CableFire: Audio.PlayCableFire(); break;
            case Cue.CableZip: Audio.PlayCableZip(); break;
            case Cue.AnchorBite: Audio.PlayAnchorBite(distance); break;
            case Cue.AnchorTear: Audio.PlayAnchorTear(); break;

            case Cue.StructureGroan: Audio.PlayStructureGroan(distance); break;
            case Cue.StructureCrack: Audio.PlayStructureCrack(distance); break;
        }
    }
}
