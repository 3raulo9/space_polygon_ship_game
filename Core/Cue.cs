using System.Numerics;
using Unrendered.Acoustics;

namespace Unrendered.Core;

/// <summary>
/// A one-shot sound the simulation asks for, named rather than played directly. Every combat
/// noise in the game goes out as one of these through <see cref="World.World.Emit"/>, which is
/// what makes multiplayer audio possible: the host records the cue with a world position and
/// broadcasts it, and every machine plays it back placed against <em>its own</em> listener — so
/// a firefight two players are both near sounds right to both of them, and different to each.
///
/// The ids are wire vocabulary. Append new ones at the end; never renumber.
/// </summary>
public enum Cue : byte
{
    None = 0,

    // Guns and their rounds.
    Detonation,     // a cannon/enemy barrel firing
    Laser,          // a SPIDER laser leaving the emitter
    RifleShot,      // a SOLDIER rifle round
    RocketLaunch,   // a rocket leaving the tube
    RocketBlast,    // a rocket going off
    LanceFire,      // the SPIDER lance discharging
    UnstableLance,  // a worn/corrupted lance
    CrabCoreBlast,  // a thrown CRAB CORE going off
    ThrowWhoosh,    // a lobbed thing / a claw throw
    FishSpit,       // the FISH's spit
    FishStrike,     // the FISH's lunge connecting
    FishImpact,     // the FISH meeting a wall / round
    FishBeach,      // the FISH meeting the seabed (param = how hard, 0..1)
    FishCoil,       // the FISH winding a strike
    TailBeat,       // one beat of the FISH's tail, swimming (param = starvation 0..1)
    TailFlop,       // the same beat beached — a body flopping on the grid (param = starvation)
    MawSpit,        // one of the mouth's little lasers

    // Impacts and deaths.
    Explosion,      // a craft or hunter destroyed, up close
    ExplosionAt,    // a detonation heard from range — the one cue that takes time to arrive
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
    Footstep,       // a leg planting (param = leg index)
    HuntCall,       // the hunting groan
    CrabScream,     // the seizure scream
    ClawSlam,       // the free claw's blow
    BeamCharge,     // the crystal spinning up
    BeamWarning,    // one of the three countdown warnings (param = step)
    BeamFire,       // the beam firing

    // The Maw-Core.
    MawSwallow,     // the throat closing on a craft
    MawDive,        // the mouth dropping
    MawRelease,     // the jaw throwing a craft clear
    MawTeeth,       // the teeth working over each other
    MawCrystal,     // the crystal ringing (param = agitation 0..1)

    // The SOLDIER's rig.
    GasJump,        // the gas kick of a jump (param = starvation 0..1)
    CableFire,      // a hook leaving the launcher
    CableZip,       // a cable coming home / a miss
    AnchorBite,     // steel biting in
    AnchorTear,     // an anchor tearing out

    // Structures.
    StructureGroan, // a support giving way
    StructureCrack, // masonry shearing where a beam bites

    // --- Beds ---------------------------------------------------------------------
    // Never sent over the wire and never raised by Emit: a bed has nothing to "fire", so
    // there is nothing to broadcast. Every machine drives its own from the state the
    // snapshots already carry. They live in this enum only so they can own a row in the
    // cue table and be placed in the world like everything else.
    BossHum,        // the Crab-Core's internal rotor
    MawHover,       // the Maw-Core holding itself up
    LanceCharge,    // the SPIDER's lance winding up (on your own hull)
    ReelJet,        // the SOLDIER's reel (on your own hull)
    Wind,           // the air past your ears (on your own hull)
    CableStrain,    // steel under load (on your own hull)

    // Two of the mouth's own noises that used to be played straight at the host's speaker
    // from inside the entity, and so were heard by exactly one of the twenty players in the
    // room. They are their own ids rather than shades of MawTeeth because they are their
    // own recipes: a bite is not a grind, and a drip is neither.
    MawDigest,      // one bite while it chews somebody
    MawDrip,        // a bead of the black stuff letting go

    /// <summary>Somebody dropped a mark on the world. Placed at the mark, not at the player
    /// who dropped it, so a team-mate facing the other way still hears <em>where</em>.</summary>
    Marker,

    // --- The FLOWER ---------------------------------------------------------------------
    // One of these is not like the others. Everything in this bank grinds, crushes or tears;
    // the petal throw is *sung*, and it is meant to sit wrong in the mix on purpose — the same
    // trick the Crab-Core's beam plays, turned around and handed to the player. See
    // SfxSynth.PetalHymn.

    FlowerPetalThrow,  // a petal leaving the ring — the holy one
    FlowerPetalCatch,  // one re-seating in its gap
    FlowerBloom,       // a petal going off in somebody: the rosette
    FlowerHarvest,     // a ripe head setting seed
    FlowerWilt,        // roots tearing out of the plate
    FlowerUproot,      // the plant gone, a hole where it was
    FlowerBloomOpen,   // the ring unfurling on new ground
}

/// <summary>
/// A cue an entity raised, with the place it happened.
///
/// <para>Entities have no world reference, so for a long time the bosses and the two
/// cinematics reached for the sound bank directly — which meant every noise they made
/// existed on the host's machine and nowhere else. Naming a cue into a buffer instead lets
/// the world drain it after the step and <c>Emit</c> it, which is what puts it on the wire.
/// The position rides along because a seizure is two places at once: the machine doing the
/// gripping and the craft being gripped.</para>
/// </summary>
public readonly record struct EntityCue(Cue Id, Vector2 At, float Param = 0f);

/// <summary>
/// Where cue ids meet the sound bank, on both ends of the wire — and where each cue's
/// <em>acoustics</em> are declared: how far it carries, how sharply it fades, how many of it
/// may sound at once, whether the city can get in the way of it.
///
/// <para>The table below is the single most tunable thing in the game's audio, so it can be
/// overridden at runtime by <c>Assets/Audio/cues.cfg</c> without a rebuild — see
/// <see cref="ApplyOverrideFile"/>.</para>
/// </summary>
public static class CueBank
{
    /// <summary>Whether a cue's parameter is a 0..1 fraction (severity, starvation) rather
    /// than a small whole number (a leg index, a warning step). The wire packs it to one byte
    /// either way; this decides whether to scale it by 255 on the way through.</summary>
    public static bool IsFraction(Cue id)
        => id is Cue.CoreHit or Cue.MawHurt or Cue.GasJump or Cue.FishBeach
              or Cue.TailBeat or Cue.TailFlop or Cue.MawCrystal;

    /// <summary>
    /// Whether only the host can ever raise this cue. Nearly every noise in the game is
    /// raised by the machine that caused it — a client fires and hears its own shot at once,
    /// and then ignores the host's echo of it. Salvage is the exception: clients deliberately
    /// do not predict pickups, so a collect chime is decided on the host and nowhere else,
    /// and the owner has to be allowed to hear the echo or they never hear it at all.
    /// </summary>
    public static bool RaisedOnlyByHost(Cue id) => id is Cue.Pickup;

    /// <summary>
    /// Builds the acoustic table. Read it as a description of the world: a rifle is a sharp
    /// thing that does not carry and there may be four of them; a boss coming apart carries
    /// across the whole arena, is never culled and is never dropped for anything.
    ///
    /// <para><c>Max</c> is the radius past which a cue is silent — and therefore the radius
    /// past which it is never even given a voice, so these numbers are the game's audio
    /// budget as much as its sound design. <c>Rolloff</c> above 1 dies away near you;
    /// below 1 stays present most of the way out.</para>
    /// </summary>
    public static SpecTable BuildTable()
    {
        int count = Enum.GetValues<Cue>().Length;
        var t = new SpecTable(count);

        // A world sound, with the knobs most cues want. Everything below is this with
        // reasons attached.
        //
        // The bus comes first because it is the one field a reader of this table most often
        // wants to check: it is what the settings screen's sliders actually address, and a
        // cue on the wrong one is a cue the player cannot turn down.
        static SoundSpec S(Bus bus, float gain, float max, float rolloff, byte priority, byte cap,
            float reverb = 0.30f, float air = 0.7f, float jitter = 0.03f,
            bool occludes = true, bool delay = false, float min = 5f)
            => new(bus, gain, min, max, rolloff, priority, cap, Spatial: true,
                   Occludes: occludes, ReverbSend: reverb, AirAbsorb: air,
                   PitchJitter: jitter, TravelDelay: delay);

        // A bed: quiet, long-ranged, dry, one instance, never stolen.
        static SoundSpec Bed(Bus bus, float gain, float max, float rolloff, float reverb = 0.10f,
            float air = 0.5f)
            => new(bus, gain, MinDistance: 4f, MaxDistance: max, Rolloff: rolloff,
                   Priority: 240, MaxInstances: 1, Spatial: true, Occludes: true,
                   ReverbSend: reverb, AirAbsorb: air, PitchJitter: 0f, TravelDelay: false);

        // Something on your own panel: centred, dry, unmissable. Always the player's bus —
        // a panel by definition belongs to whoever is looking at it.
        static SoundSpec Panel(float gain, byte priority = 220)
            => SoundSpec.Flat with
            {
                Bus = Bus.Ui, Gain = gain, Priority = priority, MaxInstances = 2,
            };

        // --- Guns -------------------------------------------------------------------
        // Caps matter more here than anywhere. Twenty players on full auto is the case
        // that decides whether a firefight is legible or a wall of noise.
        //
        // All of it is Bus.Shooting whoever pulled the trigger, and it could not be
        // otherwise: Detonation is a barrel firing, and the cue does not carry — cannot
        // carry, since it crosses the wire as one byte — whether the hull under that barrel
        // belonged to a player or a hunter. Which turns out to be the right answer anyway.
        // Gunfire is a single texture in a mix, and a player who reaches for that slider is
        // asking for less of the texture, not for a side to be silenced.
        const Bus Gun = Bus.Shooting;
        t.Set((int)Cue.Detonation, "Detonation", S(Gun, 0.85f, 150f, 1.5f, 120, 6, air: 0.75f, jitter: 0.05f));
        t.Set((int)Cue.Laser, "Laser", S(Gun, 0.55f, 110f, 1.8f, 90, 5, reverb: 0.22f, air: 0.85f, jitter: 0.07f));
        t.Set((int)Cue.RifleShot, "RifleShot", S(Gun, 0.62f, 130f, 1.9f, 95, 4, reverb: 0.38f, air: 0.9f, jitter: 0.06f));
        t.Set((int)Cue.RocketLaunch, "RocketLaunch", S(Gun, 0.85f, 155f, 1.4f, 130, 3, air: 0.6f));
        t.Set((int)Cue.LanceFire, "LanceFire", S(Gun, 0.9f, 160f, 1.3f, 140, 3, reverb: 0.35f, air: 0.6f));
        t.Set((int)Cue.UnstableLance, "UnstableLance", S(Gun, 0.95f, 165f, 1.3f, 145, 3, reverb: 0.4f, air: 0.6f));
        t.Set((int)Cue.FishSpit, "FishSpit", S(Gun, 0.5f, 100f, 1.9f, 80, 5, reverb: 0.25f, air: 0.85f, jitter: 0.08f));
        t.Set((int)Cue.MawSpit, "MawSpit", S(Gun, 0.6f, 110f, 1.6f, 100, 5, reverb: 0.25f, air: 0.8f));
        // A throw is the delivery of a weapon, so it belongs with the guns rather than with
        // the blast it precedes — you hear it in time to move, which is what it is for.
        t.Set((int)Cue.ThrowWhoosh, "ThrowWhoosh", S(Gun, 0.6f, 90f, 1.7f, 70, 4, reverb: 0.2f, air: 0.8f));

        // Ordnance arriving rather than leaving.
        t.Set((int)Cue.RocketBlast, "RocketBlast", S(Bus.Explosions, 1.0f, 175f, 1.2f, 170, 4, reverb: 0.45f, air: 0.5f));
        t.Set((int)Cue.CrabCoreBlast, "CrabCoreBlast", S(Bus.Explosions, 1.0f, 190f, 1.1f, 180, 2, reverb: 0.5f, air: 0.5f));

        // --- The FISH's body --------------------------------------------------------
        // A player chassis, so all of it is on the player's fader — including the strike,
        // which is a body hitting something rather than a weapon going off.
        t.Set((int)Cue.FishStrike, "FishStrike", S(Bus.Player, 1.0f, 150f, 1.2f, 160, 2, reverb: 0.35f, air: 0.55f));
        t.Set((int)Cue.FishImpact, "FishImpact", S(Bus.Player, 0.9f, 130f, 1.4f, 150, 3, reverb: 0.4f, air: 0.6f));
        t.Set((int)Cue.FishBeach, "FishBeach", S(Bus.Player, 0.8f, 120f, 1.5f, 120, 2, reverb: 0.35f));
        t.Set((int)Cue.FishCoil, "FishCoil", S(Bus.Player, 0.5f, 70f, 1.8f, 70, 2, reverb: 0.15f, air: 0.9f));
        t.Set((int)Cue.TailBeat, "TailBeat", S(Bus.Player, 0.45f, 80f, 1.8f, 60, 4, reverb: 0.2f, air: 0.85f, jitter: 0.06f));
        t.Set((int)Cue.TailFlop, "TailFlop", S(Bus.Player, 0.55f, 85f, 1.7f, 65, 3, reverb: 0.3f));

        // --- Impacts and deaths -----------------------------------------------------
        t.Set((int)Cue.Explosion, "Explosion", S(Bus.Explosions, 1.0f, 200f, 1.1f, 190, 4, reverb: 0.45f, air: 0.5f));
        // The one cue that travels. Long reach, slow rolloff, heavy absorption — a blast
        // across the map should be a dull roll that arrives after the flash.
        t.Set((int)Cue.ExplosionAt, "ExplosionAt",
            S(Bus.Explosions, 0.9f, 260f, 0.85f, 175, 4, reverb: 0.55f, air: 1f, delay: true, min: 30f));
        // Never culled, never stolen. A boss dying is the loudest event in the game and
        // the player must hear all of it wherever they are standing.
        t.Set((int)Cue.BossDeath, "BossDeath",
            S(Bus.Explosions, 1.0f, 300f, 0.8f, 250, 8, reverb: 0.6f, air: 0.55f, min: 20f));

        // A craft absorbing a hit and a craft coming down hard are both things happening to
        // a hull, which is what the player fader is for.
        t.Set((int)Cue.Hit, "Hit", S(Bus.Player, 0.75f, 90f, 1.7f, 110, 5, reverb: 0.2f, air: 0.8f));
        t.Set((int)Cue.CrashLanding, "CrashLanding", S(Bus.Player, 0.8f, 110f, 1.6f, 120, 3, reverb: 0.35f));
        t.Set((int)Cue.Warning, "Warning", Panel(0.75f, 235));
        t.Set((int)Cue.Pickup, "Pickup", Panel(0.55f, 200));

        // A shot landing on a monster is the monster's noise, not the gun's: it is how the
        // player learns they are hurting the thing.
        t.Set((int)Cue.CoreHit, "CoreHit", S(Bus.Enemies, 0.9f, 140f, 1.4f, 165, 3, reverb: 0.35f, air: 0.7f));
        t.Set((int)Cue.MawHurt, "MawHurt", S(Bus.Enemies, 0.9f, 140f, 1.4f, 165, 3, reverb: 0.4f, air: 0.7f));

        // --- The Crab-Core ----------------------------------------------------------
        const Bus Foe = Bus.Enemies;
        t.Set((int)Cue.Clamp, "Clamp", S(Foe, 0.7f, 120f, 1.5f, 110, 4, reverb: 0.35f, air: 0.75f));
        t.Set((int)Cue.Alarm, "Alarm", S(Foe, 0.9f, 180f, 1.1f, 200, 2, reverb: 0.5f, air: 0.6f));
        // Six legs on a tripod gait: three land on the same tick, so the cap is generous
        // and the level is not.
        t.Set((int)Cue.Footstep, "Footstep", S(Foe, 0.85f, 100f, 1.5f, 85, 8, reverb: 0.4f, air: 0.7f, jitter: 0f));
        // Carries a long way and stays present in the middle distance — it should feel
        // like it is following you, not switching off.
        t.Set((int)Cue.HuntCall, "HuntCall", S(Foe, 0.75f, 145f, 0.75f, 150, 2, reverb: 0.45f, air: 0.85f, min: 10f));
        t.Set((int)Cue.CrabScream, "CrabScream", S(Foe, 1.0f, 120f, 1.3f, 245, 2, reverb: 0.3f, air: 0.5f));
        t.Set((int)Cue.ClawSlam, "ClawSlam", S(Foe, 1.0f, 140f, 1.3f, 210, 2, reverb: 0.4f, air: 0.6f));
        t.Set((int)Cue.BeamCharge, "BeamCharge", S(Foe, 0.9f, 200f, 1.0f, 215, 2, reverb: 0.4f, air: 0.6f));
        t.Set((int)Cue.BeamWarning, "BeamWarning", S(Foe, 0.9f, 210f, 0.9f, 225, 3, reverb: 0.35f, air: 0.6f, jitter: 0f));
        // The beam is the boss, not an artillery piece: it stays with the thing that fired
        // it so turning the monsters down turns down all of it and not most of it.
        t.Set((int)Cue.BeamFire, "BeamFire", S(Foe, 1.0f, 240f, 0.85f, 240, 2, reverb: 0.5f, air: 0.55f));

        // --- The Maw-Core -----------------------------------------------------------
        t.Set((int)Cue.MawSwallow, "MawSwallow", S(Foe, 1.0f, 130f, 1.3f, 235, 2, reverb: 0.4f, air: 0.6f));
        t.Set((int)Cue.MawDive, "MawDive", S(Foe, 0.95f, 150f, 1.2f, 200, 2, reverb: 0.45f, air: 0.6f));
        t.Set((int)Cue.MawRelease, "MawRelease", S(Foe, 0.9f, 130f, 1.3f, 195, 2, reverb: 0.4f));
        t.Set((int)Cue.MawTeeth, "MawTeeth", S(Foe, 0.6f, 95f, 1.6f, 75, 4, reverb: 0.5f, air: 0.85f, jitter: 0.06f));
        t.Set((int)Cue.MawCrystal, "MawCrystal", S(Foe, 0.55f, 105f, 1.5f, 80, 2, reverb: 0.5f, air: 0.8f));
        // A bite lands on somebody, so it carries; a drip is a detail of the weather under
        // the mouth and is not meant to be heard from anywhere but under it.
        t.Set((int)Cue.MawDigest, "MawDigest", S(Foe, 0.85f, 120f, 1.4f, 190, 2, reverb: 0.45f, air: 0.7f));
        t.Set((int)Cue.MawDrip, "MawDrip", S(Foe, 0.5f, 45f, 1.6f, 40, 3, reverb: 0.45f, air: 0.9f, jitter: 0.1f));

        // A mark is information, so it carries a very long way and is barely attenuated by
        // anything: the whole point is that a player anywhere in the match hears WHERE. On
        // the player bus — it is a team-mate talking to you, and the one slider it must
        // never hide behind is the one named for the things trying to kill you.
        t.Set((int)Cue.Marker, "Marker", S(Bus.Player, 0.7f, 300f, 0.5f, 245, 3, reverb: 0.1f, air: 0.25f, jitter: 0f, occludes: false, min: 20f));

        // --- The SOLDIER's rig ------------------------------------------------------
        // These are things happening to a person. Generous ranges — a team-mate's grapple
        // firing somewhere behind you is information — and light absorption. All on the
        // player's bus: a cable is not a gun, whoever is on the end of it.
        const Bus Rig = Bus.Player;
        t.Set((int)Cue.GasJump, "GasJump", S(Rig, 0.8f, 120f, 1.5f, 115, 4, reverb: 0.25f, air: 0.8f));
        t.Set((int)Cue.CableFire, "CableFire", S(Rig, 0.7f, 105f, 1.6f, 100, 4, reverb: 0.25f, air: 0.8f));
        t.Set((int)Cue.CableZip, "CableZip", S(Rig, 0.6f, 95f, 1.7f, 85, 4, reverb: 0.25f, air: 0.85f));
        t.Set((int)Cue.AnchorBite, "AnchorBite", S(Rig, 0.85f, 125f, 1.3f, 145, 4, reverb: 0.45f, air: 0.7f));
        t.Set((int)Cue.AnchorTear, "AnchorTear", S(Rig, 0.85f, 115f, 1.4f, 150, 3, reverb: 0.4f, air: 0.7f));

        // --- Structures -------------------------------------------------------------
        // A tower failing is heard through the city, so occlusion is left ON but the reach
        // is long: it is the loudest warning the world gives. Filed under explosions — a
        // building coming down is the same event to a listener as ordnance landing, and it
        // is nearly always ordnance that brought it down.
        t.Set((int)Cue.StructureGroan, "StructureGroan", S(Bus.Explosions, 0.85f, 200f, 1.0f, 185, 3, reverb: 0.55f, air: 0.6f));
        t.Set((int)Cue.StructureCrack, "StructureCrack", S(Bus.Explosions, 0.55f, 140f, 1.7f, 90, 5, reverb: 0.5f, air: 0.85f, jitter: 0.08f));

        // --- Beds -------------------------------------------------------------------
        // Long, slow rolloffs so a monster is a presence in the middle distance rather
        // than an on/off switch, and low sends so the room does not turn to soup.
        t.Set((int)Cue.BossHum, "BossHum", Bed(Bus.Enemies, 0.42f, 130f, 0.9f, reverb: 0.15f));
        t.Set((int)Cue.MawHover, "MawHover", Bed(Bus.Enemies, 0.38f, 150f, 0.85f, reverb: 0.12f));
        // The four on your own hull. Flat, dry, unoccluded — a bed cannot be muffled by a
        // wall it is standing inside with you — and squarely the player's own noise.
        t.Set((int)Cue.LanceCharge, "LanceCharge", SoundSpec.Flat with { Bus = Bus.Player, Gain = 0.5f, MaxInstances = 1, Priority = 240 });
        t.Set((int)Cue.ReelJet, "ReelJet", SoundSpec.Flat with { Bus = Bus.Player, Gain = 0.45f, MaxInstances = 1, Priority = 240 });
        t.Set((int)Cue.Wind, "Wind", SoundSpec.Flat with { Bus = Bus.Player, Gain = 0.55f, MaxInstances = 1, Priority = 240 });
        t.Set((int)Cue.CableStrain, "CableStrain", SoundSpec.Flat with { Bus = Bus.Player, Gain = 0.3f, MaxInstances = 1, Priority = 240 });

        // --- The FLOWER --------------------------------------------------------------
        // The throw carries further than anything else a player owns and is barely absorbed
        // by air, which is deliberate: a sung chord going up somewhere across the city is
        // information — it says a flower has committed a petal, and that petal is now coming
        // back through whatever is between you and it. It is the one player noise in the game
        // that is meant to be heard by people it is not aimed at.
        t.Set((int)Cue.FlowerPetalThrow, "FlowerPetalThrow",
            S(Bus.Player, 0.85f, 210f, 0.8f, 200, 3, reverb: 0.55f, air: 0.3f, jitter: 0f, min: 12f));
        t.Set((int)Cue.FlowerPetalCatch, "FlowerPetalCatch",
            S(Bus.Player, 0.5f, 70f, 1.8f, 70, 4, reverb: 0.2f, air: 0.85f, jitter: 0.05f));
        // The rosette lands on somebody, so it goes with the ordnance rather than with the
        // plant — a player turning the explosions down is asking for less of exactly this.
        t.Set((int)Cue.FlowerBloom, "FlowerBloom",
            S(Bus.Explosions, 0.95f, 170f, 1.15f, 175, 4, reverb: 0.5f, air: 0.55f));
        // The three that are the plant's own body. Short reach: a harvest is a private event
        // and nobody across a street needs to know how anybody's crop is doing.
        t.Set((int)Cue.FlowerHarvest, "FlowerHarvest", S(Bus.Player, 0.6f, 75f, 1.7f, 90, 2, reverb: 0.3f, air: 0.85f));
        t.Set((int)Cue.FlowerWilt, "FlowerWilt", S(Bus.Player, 0.7f, 90f, 1.6f, 130, 2, reverb: 0.35f, air: 0.8f));
        t.Set((int)Cue.FlowerUproot, "FlowerUproot", S(Bus.Player, 0.8f, 110f, 1.5f, 140, 2, reverb: 0.4f, air: 0.7f));
        t.Set((int)Cue.FlowerBloomOpen, "FlowerBloomOpen", S(Bus.Player, 0.7f, 100f, 1.6f, 135, 2, reverb: 0.45f, air: 0.75f));

        return t;
    }

    /// <summary>Where a tuning file may sit. Absent by default — the compiled table is the
    /// game's real answer, and this exists so a number can be changed and heard without a
    /// rebuild.</summary>
    public const string OverridePath = "Assets/Audio/cues.cfg";

    /// <summary>How many fields the last override load actually applied. Reported by the
    /// debug overlay so a file that silently did nothing is visible.</summary>
    public static int OverridesApplied { get; private set; }

    /// <summary>Folds <see cref="OverridePath"/> over a table, if it exists. Never throws:
    /// a tuning aid must not be able to take the game's audio down.</summary>
    public static void ApplyOverrideFile(SpecTable table)
    {
        try
        {
            if (!File.Exists(OverridePath)) { OverridesApplied = 0; return; }
            OverridesApplied = table.ApplyOverrides(File.ReadAllLines(OverridePath));
        }
        catch { OverridesApplied = 0; }
    }

    /// <summary>
    /// Plays a cue at a place in the world. The one door between the simulation's vocabulary
    /// and the sound bank; everything about how it will actually sound is decided downstream
    /// of here, from the table above and from where the listener happens to be standing.
    /// </summary>
    public static void Play(Cue id, Vector2 at, float param)
    {
        switch (id)
        {
            case Cue.Detonation: Audio.PlayDetonation(at); break;
            case Cue.Laser: Audio.PlayLaser(at); break;
            case Cue.RifleShot: Audio.PlayRifleShot(at); break;
            case Cue.RocketLaunch: Audio.PlayRocketLaunch(at); break;
            case Cue.RocketBlast: Audio.PlayRocketBlast(at); break;
            case Cue.LanceFire: Audio.PlayLanceFire(at); break;
            case Cue.UnstableLance: Audio.PlayUnstableLance(at); break;
            case Cue.CrabCoreBlast: Audio.PlayCrabCoreBlast(at); break;
            case Cue.ThrowWhoosh: Audio.PlayThrowWhoosh(at); break;
            case Cue.FishSpit: Audio.PlayFishSpit(at); break;
            case Cue.FishStrike: Audio.PlayFishStrike(at); break;
            case Cue.FishImpact: Audio.PlayFishImpact(at); break;
            case Cue.FishBeach: Audio.PlayFishBeach(at, param); break;
            case Cue.FishCoil: Audio.PlayFishCoil(at); break;
            // One clip with a flag rather than two cues would need a second parameter the
            // wire does not carry, so the beached beat is simply its own id.
            case Cue.TailBeat: Audio.PlayTailBeat(at, param, beached: false); break;
            case Cue.TailFlop: Audio.PlayTailBeat(at, param, beached: true); break;
            case Cue.MawSpit: Audio.PlayMawSpit(at); break;

            case Cue.Explosion: Audio.PlayExplosion(at); break;
            case Cue.ExplosionAt: Audio.PlayExplosionAt(at); break;
            case Cue.Hit: Audio.PlayHit(at); break;
            case Cue.Warning: Audio.PlayWarning(at); break;
            case Cue.CoreHit: Audio.PlayCoreHit(at, param); break;
            case Cue.MawHurt: Audio.PlayMawHurt(at, param); break;
            case Cue.BossDeath: Audio.PlayBossDeath(at); break;
            case Cue.CrashLanding: Audio.PlayCrashLanding(at); break;
            case Cue.Pickup: Audio.PlayPickup(at); break;

            case Cue.Clamp: Audio.PlayClamp(at); break;
            case Cue.Alarm: Audio.PlayAlarm(at); break;
            case Cue.Footstep: Audio.PlayFootstep(at, (int)param); break;
            case Cue.HuntCall: Audio.PlayHuntCall(at); break;
            case Cue.CrabScream: Audio.PlayCrabScream(at); break;
            case Cue.ClawSlam: Audio.PlayClawSlam(at); break;
            case Cue.BeamCharge: Audio.PlayBeamCharge(at); break;
            case Cue.BeamWarning: Audio.PlayBeamWarning(at, (int)param); break;
            case Cue.BeamFire: Audio.PlayBeamFire(at); break;

            case Cue.MawSwallow: Audio.PlayMawSwallow(at); break;
            case Cue.MawDive: Audio.PlayMawDive(at); break;
            case Cue.MawRelease: Audio.PlayMawRelease(at); break;
            // The one cue whose parameter is a mode rather than a level: a grind heard from
            // outside and a grind heard from inside a mouth are the same teeth doing very
            // different things, and the wire's single byte is enough to say which.
            case Cue.MawTeeth: Audio.PlayMawTeeth(at, grinding: param > 0.5f); break;
            case Cue.MawCrystal: Audio.PlayMawCrystal(at, param); break;
            case Cue.MawDigest: Audio.PlayMawDigest(at); break;
            case Cue.MawDrip: Audio.PlayMawDrip(at); break;

            case Cue.GasJump: Audio.PlayGasJump(at, param); break;
            case Cue.CableFire: Audio.PlayCableFire(at); break;
            case Cue.CableZip: Audio.PlayCableZip(at); break;
            case Cue.AnchorBite: Audio.PlayAnchorBite(at); break;
            case Cue.AnchorTear: Audio.PlayAnchorTear(at); break;

            case Cue.StructureGroan: Audio.PlayStructureGroan(at); break;
            case Cue.StructureCrack: Audio.PlayStructureCrack(at); break;
            case Cue.Marker: Audio.PlayMarker(at); break;

            case Cue.FlowerPetalThrow: Audio.PlayPetalThrow(at); break;
            case Cue.FlowerPetalCatch: Audio.PlayPetalCatch(at); break;
            case Cue.FlowerBloom: Audio.PlayFlowerBloom(at); break;
            case Cue.FlowerHarvest: Audio.PlayFlowerHarvest(at); break;
            case Cue.FlowerWilt: Audio.PlayFlowerWilt(at); break;
            case Cue.FlowerUproot: Audio.PlayFlowerUproot(at); break;
            case Cue.FlowerBloomOpen: Audio.PlayFlowerOpen(at); break;
        }
    }
}
