using System.Numerics;
using Raylib_cs;
using VoidTanks.Acoustics;
using VoidTanks.Entities;   // CrabRig, for the boss's leg count

namespace VoidTanks.Core;

/// <summary>
/// The sound bank: what each cue in the game is <em>made of</em>. Which clip, which synth
/// recipe, how the layers stack, how fast a repeating cue is allowed to repeat.
///
/// <para>What it deliberately no longer decides is how loud anything is, or where it sits,
/// or how it is coloured by the distance and the city between it and the player. All of
/// that now belongs to <see cref="VoidTanks.Acoustics.AudioEngine"/>, which is handed a
/// world position and looks the rest up in the cue table. That split is the whole point:
/// before it, forty of the fifty cues in the game were played at full volume dead centre
/// whatever was happening, which is why a firefight on the far side of the map sounded
/// exactly like a firefight in your lap.</para>
///
/// <para>Every call is a no-op until <see cref="Init"/> has run against a live audio
/// device, so the headless self-test can drive the same simulation code and stay silent.
/// The engine's arithmetic is exercised separately, without a device, by the offline
/// renderer.</para>
/// </summary>
public static class Audio
{
    // Guards every play/load call. The self-test leaves this false, so audio is silently
    // skipped; normal boot flips it on after the device and the stream are up.
    private static bool _enabled;

    private static AudioEngine _engine = new(CueBank.BuildTable());

    /// <summary>The engine behind the bank. The world uses it to place the listener, to
    /// answer occlusion questions and to set the state of the player's ears.</summary>
    public static AudioEngine Engine => _engine;

    // --- The clip bank -------------------------------------------------------------
    // Decoded once at boot into mono float, which is what the mixer eats. Nothing here is
    // a raylib Sound any more: a Sound cannot overlap itself, which is why this file used
    // to carry four separate arrays of aliases just so a walking boss could put three feet
    // down on one tick. The engine's voice pool makes all of that unnecessary.

    private static Clip? _blip;         // menu cursor moving between options
    private static Clip? _detonation;   // a barrel firing — player or enemy shot
    private static Clip? _explosion;    // a tank being destroyed (player or enemy)
    private static Clip? _distantBoom;  // an air shot coming down far off on the horizon
    private static Clip? _hit;          // a craft taking a hit
    private static Clip? _warning;      // shield crosses the low-health line
    private static Clip? _stomp;        // a Crab-Core foot planting on the grid
    private static Clip? _alarm;        // Crab-Core threat-display lurch to one side

    // Assets are copied next to the executable by the .csproj, so a relative path off the
    // working directory resolves at runtime.
    private const string SfxDir = "Assets/Audio/SFX/";

    private static readonly Random _sfxRng = new();

    /// <summary>
    /// Opens the audio device, loads every clip and starts the mixer. Call once at
    /// startup, after the window exists.
    /// </summary>
    public static void Init()
    {
        Raylib.InitAudioDevice();

        _blip = AudioEngine.LoadFile(SfxDir + "blip.wav");
        _detonation = AudioEngine.LoadFile(SfxDir + "detonation.wav");
        _explosion = AudioEngine.LoadFile(SfxDir + "explosion.wav");
        _distantBoom = AudioEngine.LoadFile(SfxDir + "distantBoom.wav");
        _hit = AudioEngine.LoadFile(SfxDir + "hit.wav");
        _warning = AudioEngine.LoadFile(SfxDir + "warning.wav");
        _stomp = AudioEngine.LoadFile(SfxDir + "stomping.wav");
        _alarm = AudioEngine.LoadFile(SfxDir + "scaryAlarm.wav");

        // The beds, synthesised once per session so each run's monsters hum and hover at
        // slightly different pitches and throbs. Rolled here rather than per monster: they
        // are loops, and rebuilding one mid-fight would mean tearing down something
        // currently audible.
        _hum.Load(SfxSynth.Render(SfxSynth.Hum(_sfxRng)), "hum");
        _mawHover.Load(SfxSynth.Render(SfxSynth.MawHover(_sfxRng)), "mawhover");
        // The player's own beds: the SPIDER's lance winding up, and the SOLDIER's gas jet,
        // wind and cable strain. Beds because none of them has a length — a swing lasts as
        // long as it lasts, and what audio has to do on those chassis is carry speed
        // continuously rather than in events.
        _lanceCharge.Load(SfxSynth.Render(SfxSynth.LanceCharge(_sfxRng)), "lance");
        _reelJet.Load(SfxSynth.Render(SfxSynth.ReelJet(_sfxRng)), "reel");
        _wind.Load(SfxSynth.Render(SfxSynth.WindRush(_sfxRng)), "wind");
        _cableStrain.Load(SfxSynth.Render(SfxSynth.CableStrain(_sfxRng)), "strain");

        _engine.WorldWrap = Torus.Size;
        CueBank.ApplyOverrideFile(_engine.Specs);
        _engine.Open();

        // The soundtrack. Owns its own clip list and its own clock; it streams through
        // raylib rather than through the mixer, because it is the one sound in the game
        // that has no position and would gain nothing from having one.
        MusicBox.Init();

        _enabled = true;
    }

    /// <summary>Where the ears are this frame. Driven from the world's <c>Eye</c>, which is
    /// the craft whose screen this is — a spectator's ears ride the team-mate they are
    /// watching, not their own wreck.</summary>
    public static void Listen(Vector2 pos, float height, float heading, float pitch)
    {
        _ear.Position = pos;
        _ear.Height = height;
        _ear.Heading = heading;
        _ear.Pitch = pitch;
    }

    private static Listener _ear;

    /// <summary>The state of the room and of the player's ears — concussion, being
    /// swallowed, how much city is standing around. Driven by the world.</summary>
    public static Ambience Room => _engine.Env;

    // --- The mixer's faders --------------------------------------------------------

    /// <summary>
    /// Pushes the player's audio settings into the mixer. Cheap and idempotent, so the
    /// settings screen can simply call it on every change rather than diffing anything.
    /// </summary>
    public static void ApplySettings(Settings s)
    {
        _engine.Env.MasterVolume = s.MasterVolume;

        // Walked rather than listed. A bus with no fader of its own answers 1 from
        // VolumeOf, so adding a category to the mixer and a row to the settings page is two
        // edits and never a third one here that somebody forgets.
        foreach (var bus in Enum.GetValues<Bus>()) _engine.SetBusGain(bus, s.VolumeOf(bus));

        _engine.Mix.MonoDownmix = s.MonoAudio;
        _engine.Mix.SoftenLoud = s.SoftenLoudSounds;

        // The soundtrack streams through raylib rather than the mixer, so its bus gain has
        // to reach it by hand — see MusicBox.Fader.
        MusicBox.Fader = s.MusicVolume;
    }

    // --- Adaptive music ------------------------------------------------------------

    /// <summary>
    /// How hard the fight is right now, 0..1. Fed by the world each frame; the soundtrack
    /// rides it, and it also decides how far the music gets out of the way when something
    /// large happens. Not a mood system — the game has two tracks — but the difference
    /// between an empty grid and a boss fight ought to be audible in more than the gunfire.
    /// </summary>
    public static float Intensity { get; private set; }

    /// <summary>Set by the world each frame from what is actually on the field.</summary>
    public static void SetIntensity(float value) => _intensityTarget = Math.Clamp(value, 0f, 1f);

    private static float _intensityTarget;

    /// <summary>How far the music is pushed down at full intensity, and how fast it moves.
    /// Slow on the way back up: a soundtrack that pops back the instant a boss stops
    /// roaring sounds like a mistake, and one that creeps back sounds like relief.</summary>
    private const float MusicDuckAtPeak = 0.45f;
    private const float IntensityRise = 2.2f;
    private const float IntensityFall = 0.35f;

    private static void ServiceMusic(float dt)
    {
        // Rises fast, falls slowly. A firefight starting is news; a firefight ending is
        // something the player works out over several seconds.
        float rate = _intensityTarget > Intensity ? IntensityRise : IntensityFall;
        Intensity = Approach(Intensity, _intensityTarget, rate * dt);

        // The soundtrack gets out of the way of the world rather than the other way round.
        // A hard sidechain on top of that: a boss death or a beam firing drops it further
        // still for as long as the master limiter is actually working, which is exactly the
        // moments that need the room.
        float duck = 1f - MusicDuckAtPeak * Intensity;
        duck *= 1f - 0.4f * Math.Clamp(_engine.Mix.LimiterReduction * 2f, 0f, 1f);
        MusicBox.Duck = duck;
    }

    // --- Posting -------------------------------------------------------------------

    /// <summary>
    /// Hands one rendered sound to the engine at a place in the world. Every cue below
    /// funnels through here.
    ///
    /// <paramref name="gain"/> is a <em>layer</em> level — how loud this component is
    /// relative to the others in the same cue — and never a distance volume. The engine
    /// owns distance.
    /// </summary>
    private static void Post(Cue id, Clip? clip, Vector2 at, float pitch = 1f, float gain = 1f)
    {
        if (_enabled && clip != null) _engine.Play((int)id, clip, at, 0f, -1, pitch, gain);
    }

    /// <summary>Renders a synth recipe and posts it. A fresh roll every time, which is what
    /// keeps repeated cues from sounding sampled — and now cheaper than it was, since the
    /// mixer eats the float buffer directly instead of it being wrapped in a WAV header and
    /// uploaded to the sound card.</summary>
    private static void Synth(Cue id, SfxSynth.Params p, Vector2 at, float gain = 1f)
    {
        // Checked before rendering, not after. Rendering a recipe is real work — a second of
        // 44.1kHz float — and the argument to a Post that is about to throw it away is still
        // rendered. Without this the headless self-test synthesises a buffer for every cue
        // the whole simulation raises and drops all of them.
        if (!_enabled) return;
        Post(id, AudioEngine.FromSamples(SfxSynth.Render(p)), at, 1f, gain);
    }

    /// <summary>A sound with no place in the world — a menu click, a gauge on your own
    /// panel. Centred and dry however loud the fight outside is.</summary>
    private static void Flat(Cue id, Clip? clip, float pitch = 1f, float gain = 1f)
    {
        if (_enabled && clip != null) _engine.PlayFlat((int)id, clip, pitch, gain);
    }

    private static void FlatSynth(Cue id, SfxSynth.Params p, float gain = 1f)
    {
        if (!_enabled) return;
        Flat(id, AudioEngine.FromSamples(SfxSynth.Render(p)), 1f, gain);
    }

    // --- Guns, rounds and impacts --------------------------------------------------

    /// <summary>Menu cursor stepping to a new option.</summary>
    public static void PlayBlip() => Flat(Cue.Pickup, _blip);

    /// <summary>A shot leaving a barrel — player or enemy.</summary>
    public static void PlayDetonation(Vector2 at) => Post(Cue.Detonation, _detonation, at);

    /// <summary>A tank being destroyed — player or enemy, up close.</summary>
    public static void PlayExplosion(Vector2 at) => Post(Cue.Explosion, _explosion, at);

    /// <summary>The far-off detonation — an air shot coming down out on the horizon. Its
    /// own low, rolling boom, and the one cue in the bank that carries a travel delay, so
    /// a blast across the map flashes before it arrives.</summary>
    public static void PlayExplosionAt(Vector2 at) => Post(Cue.ExplosionAt, _distantBoom, at);

    /// <summary>A craft absorbing a hit.</summary>
    public static void PlayHit(Vector2 at) => Post(Cue.Hit, _hit, at);

    /// <summary>Low-shield alarm. Flat by design — it is your own panel talking, so it does
    /// not belong anywhere in the world and it is not muffled by anything.</summary>
    public static void PlayWarning(Vector2 at) => Flat(Cue.Warning, _warning);

    /// <summary>A support giving way — the groan that warns a topple has begun and a second
    /// or so of falling mass is coming.</summary>
    public static void PlayStructureGroan(Vector2 at)
        => Synth(Cue.StructureGroan, SfxSynth.CrabBlastGrind(_sfxRng), at);

    /// <summary>Masonry shearing off where a beam is biting. A chip, not the collapse.</summary>
    public static void PlayStructureCrack(Vector2 at)
        => Synth(Cue.StructureCrack, SfxSynth.RifleCrack(_sfxRng), at, 0.7f);

    // --- The Crab-Core's lance: charge, three warnings, then the beam ---------------

    /// <summary>The boss's crystal spinning up to fire. One shot — the recipe's own
    /// envelope carries it across the whole wind-up.</summary>
    public static void PlayBeamCharge(Vector2 at)
        => Synth(Cue.BeamCharge, SfxSynth.BeamCharge(_sfxRng), at);

    /// <summary>
    /// One of the three warnings counting the charge down. Both layers climb with
    /// <paramref name="step"/>: the alarm clip is re-pitched a clear step higher each time
    /// under a synthesised beep that steps with it.
    ///
    /// The pairing is what makes it land. The clip alone is a sound the player has heard
    /// all game meaning "your shield is low", and hearing it here would read as the wrong
    /// alarm; sliding it upward and welding a tone to it turns it into something the boss
    /// is doing rather than something the craft is reporting.
    /// </summary>
    public static void PlayBeamWarning(Vector2 at, int step)
    {
        int i = Math.Clamp(step, 0, 2);
        Post(Cue.BeamWarning, _warning, at, 1f + i * 0.32f, 0.85f);
        Synth(Cue.BeamWarning, SfxSynth.WarningBeep(_sfxRng, i), at);
    }

    /// <summary>The beam firing: two clean synthesised voices a fifth apart, both exactly
    /// as long as the burn. The only consonant, un-crushed thing in the bank.</summary>
    public static void PlayBeamFire(Vector2 at)
    {
        Synth(Cue.BeamFire, SfxSynth.BeamAngelic(_sfxRng), at);
        Synth(Cue.BeamFire, SfxSynth.BeamChoir(_sfxRng), at);
    }

    /// <summary>A thrown CRAB CORE going off: the boss's own sung beam voices layered under
    /// a low dissonant grind and a metallic clatter, plus a clamp snap at the front — the
    /// crab's attack torn loose and misfiring.</summary>
    public static void PlayCrabCoreBlast(Vector2 at)
    {
        Synth(Cue.CrabCoreBlast, SfxSynth.BeamAngelic(_sfxRng), at, 0.7f);
        Synth(Cue.CrabCoreBlast, SfxSynth.BeamChoir(_sfxRng), at, 0.6f);
        Synth(Cue.CrabCoreBlast, SfxSynth.CrabBlastGrind(_sfxRng), at);
        Synth(Cue.CrabCoreBlast, SfxSynth.CrabBlastMetal(_sfxRng), at);
        PlayClamp(at);
    }

    /// <summary>
    /// One of the SPIDER's small lasers leaving the emitter: a dry zap and, a beat behind
    /// it, the same zap again lower and far quieter — a tiny echo, and nothing more. The
    /// cannon's report has body to it, which at this cadence stacks into a continuous roar;
    /// this is built to get out of the way the instant the shot has left.
    /// </summary>
    public static void PlayLaser(Vector2 at)
    {
        Synth(Cue.Laser, SfxSynth.Laser(_sfxRng), at);

        var tail = SfxSynth.Laser(_sfxRng);
        tail.StartFreq *= 0.82f;
        tail.EndFreq *= 0.82f;
        tail.Length *= 1.3f;
        Synth(Cue.Laser, tail, at, 0.22f);
    }

    /// <summary>
    /// The SPIDER's lance discharging: a short crushed discharge, a clamp snap for the
    /// housing, a quiet tail a fifth down — and underneath all of it the Crab-Core's own
    /// sung beam.
    ///
    /// That last layer is the point of the class. The chassis is a boss's weapon cut down
    /// and bolted to a person, and hearing a ghost of the boss's most recognisable sound
    /// every time you fire is what tells the player whose gun they are holding.
    /// </summary>
    public static void PlayLanceFire(Vector2 at)
    {
        Synth(Cue.LanceFire, SfxSynth.LanceFire(_sfxRng), at);

        var tail = SfxSynth.LanceFire(_sfxRng);
        tail.StartFreq *= 0.66f;
        tail.EndFreq *= 0.66f;
        Synth(Cue.LanceFire, tail, at, 0.3f);

        // The boss's beam, cut to the length of the shaft the player actually fired. Keyed
        // off BeamTime rather than a literal so retuning the burn moves the sound with it.
        float burn = SpiderWeapon.BeamTime;

        var sung = SfxSynth.BeamAngelic(_sfxRng);
        sung.Length = burn;
        Synth(Cue.LanceFire, sung, at, 0.26f);

        var choir = SfxSynth.BeamChoir(_sfxRng);
        choir.Length = burn;
        Synth(Cue.LanceFire, choir, at, 0.18f);

        PlayClamp(at);
    }

    /// <summary>
    /// The worn Crab-Core's lance — the boss's own discharge through a corrupted core, and
    /// nothing about it sits right. The main shot goes out detuned off true by a fresh
    /// random amount every pull, with two more rolls shoved a long way apart in pitch: a
    /// weapon audibly firing several ways at once, which is what the picture shows. The
    /// choir is in there, but it is not singing anymore.
    /// </summary>
    public static void PlayUnstableLance(Vector2 at)
    {
        var main = SfxSynth.LanceFire(_sfxRng);
        main.StartFreq *= 0.85f + (float)_sfxRng.NextDouble() * 0.35f;
        main.EndFreq *= 0.6f + (float)_sfxRng.NextDouble() * 0.6f;
        Synth(Cue.UnstableLance, main, at);

        var high = SfxSynth.LanceFire(_sfxRng);
        high.StartFreq *= 1.5f;
        high.EndFreq *= 1.8f;
        high.Length *= 0.45f;
        Synth(Cue.UnstableLance, high, at, 0.4f);

        var low = SfxSynth.LanceFire(_sfxRng);
        low.StartFreq *= 0.45f;
        low.EndFreq *= 0.35f;
        low.Length *= 1.4f;
        Synth(Cue.UnstableLance, low, at, 0.5f);

        Synth(Cue.UnstableLance, SfxSynth.CrabBlastGrind(_sfxRng), at, 0.5f);
        Synth(Cue.UnstableLance, SfxSynth.CrabBlastMetal(_sfxRng), at, 0.45f);

        var sung = SfxSynth.BeamAngelic(_sfxRng);
        sung.Length = 0.5f;
        sung.EndFreq = sung.StartFreq * 0.75f;
        Synth(Cue.UnstableLance, sung, at, 0.2f);

        PlayClamp(at);
    }

    // --- The Crab-Core's protocol --------------------------------------------------

    private static double _lastClampTime = double.NegativeInfinity;
    private static int _clampStep;

    /// <summary>A gap longer than this means a new charge cycle has begun, so the spin-up
    /// starts over from its lowest step instead of climbing forever.</summary>
    private const double ClampBurstGap = 1.2;

    /// <summary>
    /// The Crab-Core winding up — claw-plates snapping shut as the machine draws power.
    /// Consecutive snaps inside one burst climb in pitch, so the boss's three clicks build
    /// into a single spin-up rather than repeating.
    /// </summary>
    public static void PlayClamp(Vector2 at)
    {
        if (!_enabled) return;

        double now = Raylib.GetTime();
        _clampStep = now - _lastClampTime > ClampBurstGap ? 0 : _clampStep + 1;
        _lastClampTime = now;

        Synth(Cue.Clamp, SfxSynth.CreepyPowerUp(_sfxRng, _clampStep), at);
    }

    /// <summary>Past this many world units a hunting Crab-Core is inaudible. The cue table
    /// is the authority now; this is kept because the boss's own code reads it.</summary>
    public const float HuntRange = 65f;

    private static double _nextHuntTime;

    /// <summary>Bounds on the irregular gap between hunting calls, in seconds.</summary>
    private const double HuntMinGap = 0.75;
    private const double HuntMaxGap = 1.9;

    /// <summary>
    /// The Crab-Core's hunting call, voiced while it is running you down: a low, wavering
    /// machine-groan that swells up out of the floor and sags away again.
    ///
    /// Safe to call every tick of the pursuit — this method owns its own cadence and
    /// rate-limits itself to one call every <see cref="HuntMinGap"/>..<see cref="HuntMaxGap"/>
    /// seconds, re-rolled each time so the calls never fall into an audible rhythm. Keeping
    /// the jitter here rather than in the boss is what keeps the simulation deterministic.
    /// </summary>
    public static void PlayHuntCall(Vector2 at)
    {
        if (!_enabled) return;

        double now = Raylib.GetTime();
        if (now < _nextHuntTime) return;
        _nextHuntTime = now + HuntMinGap + _sfxRng.NextDouble() * (HuntMaxGap - HuntMinGap);

        Synth(Cue.HuntCall, SfxSynth.HuntingCall(_sfxRng), at);
    }

    /// <summary>
    /// The Crab-Core screaming point-blank into the held player. Two voices on the same
    /// tick — a rising dissonant shriek and a slow heaving sub-roar under it. Voiced as a
    /// pair on purpose: the shriek alone is thin and reads as a noise being played at the
    /// player, while the low layer gives it a body and makes it something with mass doing
    /// the screaming.
    /// </summary>
    public static void PlayCrabScream(Vector2 at)
    {
        Synth(Cue.CrabScream, SfxSynth.CrabScream(_sfxRng), at);
        Synth(Cue.CrabScream, SfxSynth.CrabScreamUnder(_sfxRng), at);
    }

    /// <summary>The free claw landing its blow. The explosion clip carries the blast with a
    /// synthesised crunch over the top, so the hit reads as a heavy mass connecting rather
    /// than another detonation.</summary>
    public static void PlayClawSlam(Vector2 at)
    {
        Post(Cue.ClawSlam, _explosion, at);
        Synth(Cue.ClawSlam, SfxSynth.ClawSlam(_sfxRng), at);
    }

    /// <summary>The air tearing past across a thrown arc.</summary>
    public static void PlayThrowWhoosh(Vector2 at)
        => Synth(Cue.ThrowWhoosh, SfxSynth.ThrowWhoosh(_sfxRng), at);

    /// <summary>A craft coming down hard: a low thud under the standard impact clip, so the
    /// landing registers as damage taken and not just a sound effect.</summary>
    public static void PlayCrashLanding(Vector2 at)
    {
        Post(Cue.CrashLanding, _hit, at);
        Synth(Cue.CrashLanding, SfxSynth.LandThud(_sfxRng), at);
    }

    // --- A boss coming apart: a scream over a cascade of pitched blasts -------------

    /// <summary>How many detonations tear through the rig as it comes apart.</summary>
    private const int BossBoomCount = 7;

    /// <summary>Seconds the cascade is spread across — matched to the length of the boss's
    /// death glitch, so the last blast lands as the rig finishes tearing.</summary>
    private const double BossBoomSpread = 1.15;

    private static readonly double[] _boomDue = new double[BossBoomCount];
    private static readonly float[] _boomPitch = new float[BossBoomCount];
    private static readonly float[] _boomVol = new float[BossBoomCount];
    private static readonly Vector2[] _boomAt = new Vector2[BossBoomCount];
    private static readonly bool[] _boomPending = new bool[BossBoomCount];

    /// <summary>
    /// A boss coming apart. Fires a long falling scream immediately, then schedules
    /// <see cref="BossBoomCount"/> detonations across the next <see cref="BossBoomSpread"/>
    /// seconds, each pitched lower than the last so the cascade descends with the scream:
    /// it opens on tight, high cracks up where the core sat and ends on a slow, detuned
    /// boom as the carapace hits the grid.
    ///
    /// The blasts are only queued here; <see cref="Update"/> voices them as they come due —
    /// at the position the rig died, which is why that is stored with them.
    /// </summary>
    public static void PlayBossDeath(Vector2 at)
    {
        if (!_enabled) return;

        Synth(Cue.BossDeath, SfxSynth.DeathScream(_sfxRng), at);

        double now = Raylib.GetTime();
        for (int i = 0; i < BossBoomCount; i++)
        {
            float f = (float)i / (BossBoomCount - 1);        // 0..1 through the cascade

            // Jittered spacing — an even one would tick like a metronome and read as
            // mechanical rather than as a structure failing.
            double jitter = (_sfxRng.NextDouble() - 0.5) * 0.07;
            _boomDue[i] = now + f * BossBoomSpread + jitter;

            _boomPitch[i] = 1.85f - f * 1.5f + (float)(_sfxRng.NextDouble() - 0.5) * 0.12f;
            _boomVol[i] = 0.62f + f * 0.38f;
            _boomAt[i] = at;
            _boomPending[i] = true;
        }
    }

    /// <summary>The Maw-Core coming apart. The same failure as the crab's, because they are
    /// the same machine and nothing about dying is specific to which half survived.</summary>
    public static void PlayMawDeath(Vector2 at) => PlayBossDeath(at);

    /// <summary>
    /// An air shot threading the Crab-Core's exposed gem. The impact clip pitched up —
    /// this is glass and neon rather than the armour a tank hit lands on — under a
    /// synthesised shriek that gets higher, faster and more unstable the closer the core is
    /// to going. <paramref name="severity"/> runs 0 on the first hit to 1 as the last of
    /// the core's integrity goes, so the boss audibly comes apart across the fight instead
    /// of making the same noise four times.
    /// </summary>
    public static void PlayCoreHit(Vector2 at, float severity)
    {
        severity = Math.Clamp(severity, 0f, 1f);
        Post(Cue.CoreHit, _hit, at, 1.35f + severity * 0.45f);
        Synth(Cue.CoreHit, SfxSynth.CoreSting(_sfxRng, severity), at);
    }

    /// <summary>How often a planting foot also gets its servo layer voiced. On every one,
    /// six legs' worth of whine turns into a solid drone and stops reading as joints.</summary>
    private const double ServoStepChance = 0.45;

    /// <summary>Per-foot level. The rig walks as a tripod, so three of these land on the
    /// very same tick; at full level they would sum past unity on every step.</summary>
    private const float TripodMix = 0.55f;

    /// <summary>Beyond this many world units a Crab-Core footfall is inaudible.</summary>
    public const float StompRange = 70f;

    /// <summary>
    /// One leg of the Crab-Core planting on the grid. Every foot is voiced separately, so a
    /// landing tripod is three overlapping impacts rather than one thud — that density is
    /// most of what makes the gait sound like a six-legged machine. <paramref name="leg"/>
    /// fixes the limb's pitch, giving each joint a consistent voice.
    /// </summary>
    public static void PlayFootstep(Vector2 at, int leg)
    {
        if (!_enabled) return;
        int i = Math.Clamp(leg, 0, CrabRig.Legs.Length - 1);
        float pitch = 0.82f + i * 0.06f + (float)(_sfxRng.NextDouble() - 0.5) * 0.05f;
        Post(Cue.Footstep, _stomp, at, pitch, TripodMix);

        if (_sfxRng.NextDouble() < ServoStepChance)
            Synth(Cue.Footstep, SfxSynth.ServoStep(_sfxRng, i), at, 0.75f);
    }

    /// <summary>The Crab-Core's threat-display lurch — a rising blare fired each time it
    /// hard-slides to a new side, telegraphing the hunt before it commits.</summary>
    public static void PlayAlarm(Vector2 at) => Post(Cue.Alarm, _alarm, at);

    // --- The Maw-Core: the hanging mouth -------------------------------------------
    // Every cue here is synthesised rather than sampled, so none plays the same twice —
    // which matters more for this monster than anything else in the game, because several
    // fire on a loop for as long as it is on the field. A sampled drip repeating every half
    // second is a fault; a drip that is a slightly different drip each time is weather.

    /// <summary>Past this many world units the mouth's cues stop being audible.</summary>
    public const float MawRange = 62f;

    /// <summary>One of the little lasers being spat at a player.</summary>
    public static void PlayMawSpit(Vector2 at) => Synth(Cue.MawSpit, SfxSynth.MawSpit(_sfxRng), at);

    /// <summary>The rings of teeth grinding. Voiced on a cadence while it hunts, and far
    /// more often — with <paramref name="grinding"/> set, which lengthens and darkens it —
    /// while it is actually chewing someone.</summary>
    public static void PlayMawTeeth(Vector2 at, bool grinding = false)
        => Synth(Cue.MawTeeth, SfxSynth.ToothGrind(_sfxRng, grinding), at, grinding ? 1f : 0.8f);

    /// <summary>The crystal turning in its well. <paramref name="agitation"/> winds the
    /// whole thing faster and higher as it fixes on a player.</summary>
    public static void PlayMawCrystal(Vector2 at, float agitation)
        => Synth(Cue.MawCrystal, SfxSynth.CrystalWhirr(_sfxRng, agitation), at, 0.8f);

    /// <summary>One bead of the black stuff letting go.</summary>
    public static void PlayMawDrip(Vector2 at) => Synth(Cue.MawTeeth, SfxSynth.MawDrip(_sfxRng), at, 0.5f);

    /// <summary>The mouth dropping.</summary>
    public static void PlayMawDive(Vector2 at) => Synth(Cue.MawDive, SfxSynth.MawDive(_sfxRng), at);

    /// <summary>The throat closing and hauling a craft up into it. Layered over the impact
    /// clip so the swallow registers as something that happened <em>to</em> the craft.</summary>
    public static void PlayMawSwallow(Vector2 at)
    {
        Post(Cue.MawSwallow, _hit, at);
        Synth(Cue.MawSwallow, SfxSynth.MawSwallow(_sfxRng), at);
    }

    /// <summary>One bite while it digests, fired per damage tick so the player can count
    /// their shield going.</summary>
    public static void PlayMawDigest(Vector2 at)
        => Synth(Cue.MawTeeth, SfxSynth.MawDigestBite(_sfxRng), at);

    /// <summary>The thing being shot from the inside. <paramref name="severity"/> runs
    /// toward 1 as the escape count fills, so the third shot is audibly the one that broke
    /// its hold.</summary>
    public static void PlayMawHurt(Vector2 at, float severity)
    {
        Post(Cue.MawHurt, _hit, at);
        Synth(Cue.MawHurt, SfxSynth.MawWail(_sfxRng, severity), at);
    }

    /// <summary>The jaw springing open and throwing a craft clear.</summary>
    public static void PlayMawRelease(Vector2 at)
        => Synth(Cue.MawRelease, SfxSynth.MawRelease(_sfxRng), at);

    // --- The SOLDIER's rig ---------------------------------------------------------
    // Audio carries the entire sense of speed on this chassis. There is no engine note to
    // ride and no chassis to hear: what the player has is a gas bottle, two steel cables
    // and the air. So this bank is deliberately mechanical and dry — pressure, metal and
    // wind — with nothing sung or synthetic-sounding anywhere in it.

    /// <summary>
    /// The high jump: the signature whoosh, and the loudest thing this class does.
    /// <paramref name="starvation"/> is how empty the reserve is, 0..1 — as it rises the
    /// burst thins toward a hiss, which is how a player learns they are nearly out of gas
    /// without ever reading the gauge.
    /// </summary>
    public static void PlayGasJump(Vector2 at, float starvation)
    {
        starvation = Math.Clamp(starvation, 0f, 1f);
        Synth(Cue.GasJump, SfxSynth.GasBurst(_sfxRng, starvation), at, 1f - 0.45f * starvation);

        // The air rush over the top, cut short and opened up so it reads as the body
        // accelerating rather than as the bottle emptying. Dropped entirely on a nearly-dry
        // tank: a thin hiss with no rush behind it is exactly the sound of a jump that is
        // about to not clear anything.
        if (starvation > 0.85f) return;
        var rush = SfxSynth.ThrowWhoosh(_sfxRng);
        rush.Length *= 0.55f;
        rush.Attack = 0.06f;
        rush.Decay = 0.7f;
        Synth(Cue.GasJump, rush, at, (1f - starvation) * 0.75f);
    }

    /// <summary>A hook leaving its launcher: a compressed-air pop with the cable's whipping
    /// hiss rising behind it as the line pays out.</summary>
    public static void PlayCableFire(Vector2 at)
        => Synth(Cue.CableFire, SfxSynth.CableLaunch(_sfxRng), at);

    /// <summary>Steel biting in. Hard, short and metallic, and the single most important
    /// cue on the chassis — it is the difference between a swing and a fall, and the player
    /// has to know which they are in without looking at the HUD.</summary>
    public static void PlayAnchorBite(Vector2 at)
        => Synth(Cue.AnchorBite, SfxSynth.AnchorClank(_sfxRng), at);

    /// <summary>A cable coming home, and also what a shot at open sky sounds like — which
    /// is the point: a miss should be audible as a miss.</summary>
    public static void PlayCableZip(Vector2 at)
        => Synth(Cue.CableZip, SfxSynth.CableZip(_sfxRng), at);

    /// <summary>An anchor tearing out of weak material: a splintering crack, and the sound
    /// of the ground getting closer.</summary>
    public static void PlayAnchorTear(Vector2 at)
    {
        Synth(Cue.AnchorTear, SfxSynth.AnchorTear(_sfxRng), at);
        Synth(Cue.AnchorTear, SfxSynth.CableZip(_sfxRng), at, 0.6f);
    }

    /// <summary>A rifle round: a sharp dry crack with none of the cannon's body. The cadence
    /// is 600 a minute, so anything with weight to it stacks into a roar within half a
    /// second — which is also why the cue table caps how many may sound at once.</summary>
    public static void PlayRifleShot(Vector2 at)
        => Synth(Cue.RifleShot, SfxSynth.RifleCrack(_sfxRng), at);

    /// <summary>A rocket leaving the tube: the motor lighting, then tearing away.</summary>
    public static void PlayRocketLaunch(Vector2 at)
        => Synth(Cue.RocketLaunch, SfxSynth.RocketLaunch(_sfxRng), at);

    /// <summary>A rocket going off. The blast clip carries the body — it is the heaviest
    /// thing in the bank and this is the heaviest thing the class does — under a bass-first
    /// synthesised concussion.</summary>
    public static void PlayRocketBlast(Vector2 at)
    {
        Post(Cue.RocketBlast, _explosion, at);
        Synth(Cue.RocketBlast, SfxSynth.LandThud(_sfxRng), at, 0.9f);
    }

    // --- The FISH ------------------------------------------------------------------
    // Built entirely out of the bank that already exists, and deliberately so. The
    // soldier's cues are dry and mechanical because that chassis is a person wearing
    // equipment. This one is a body in water, so the same voices are reused with their
    // envelopes opened up and their attacks softened: nothing here should click or clank,
    // and everything should sound like it is displacing something.

    /// <summary>
    /// One beat of the tail. The gas burst is the right transient for it — a shove of mass
    /// through a fluid — with the attack rounded off and the tail extended, which is the
    /// whole difference between a valve opening and a body pushing water.
    /// <paramref name="beached"/> swaps it for the flat, dry slap of something out of its
    /// element: the one cue on this chassis that is <em>meant</em> to sound wrong.
    /// </summary>
    public static void PlayTailBeat(Vector2 at, float starvation, bool beached)
    {
        starvation = Math.Clamp(starvation, 0f, 1f);

        if (beached)
        {
            var slap = SfxSynth.LandThud(_sfxRng);
            slap.Length *= 0.4f;
            Synth(Cue.TailFlop, slap, at, 0.55f);
            return;
        }

        var push = SfxSynth.GasBurst(_sfxRng, starvation);
        push.Attack = 0.12f;    // no valve click — water doesn't start instantly
        push.Decay = 0.75f;
        Synth(Cue.TailBeat, push, at, (1f - 0.4f * starvation) * 0.7f);

        if (starvation > 0.8f) return;
        var wash = SfxSynth.ThrowWhoosh(_sfxRng);
        wash.Length *= 0.7f;
        wash.Attack = 0.15f;
        wash.Decay = 0.8f;
        Synth(Cue.TailBeat, wash, at, (1f - starvation) * 0.5f);
    }

    /// <summary>The body gathering before a strike: a short indrawn hiss, the one moment on
    /// this chassis where everything else goes quiet.</summary>
    public static void PlayFishCoil(Vector2 at)
    {
        var draw = SfxSynth.ThrowWhoosh(_sfxRng);
        draw.Length *= 0.5f;
        draw.Attack = 0.4f;     // swelling in rather than hitting — an intake, not a blow
        draw.Decay = 0.25f;
        Synth(Cue.FishCoil, draw, at, 0.5f);
    }

    /// <summary>
    /// The lunge. The loudest thing the class does and the only cue in this game that is a
    /// piece of music rather than a noise — a crushed, detuned power chord over its own
    /// octave. The wash of water goes last and quietest: if the pool is saturated it is the
    /// one that should be dropped, because the riff is the cue and the water is the garnish.
    /// </summary>
    public static void PlayFishStrike(Vector2 at)
    {
        Synth(Cue.FishStrike, SfxSynth.StrikeRiff(_sfxRng, bass: true), at, 0.85f);
        Synth(Cue.FishStrike, SfxSynth.StrikeRiff(_sfxRng), at);

        var wash = SfxSynth.ThrowWhoosh(_sfxRng);
        wash.Length *= 0.6f;
        wash.Attack = 0.02f;
        Synth(Cue.FishStrike, wash, at, 0.4f);
    }

    /// <summary>A strike connecting. Heavy and blunt — this is a body at fifty metres a
    /// second arriving somewhere, and it should sound like mass rather than a weapon.</summary>
    public static void PlayFishImpact(Vector2 at)
    {
        Synth(Cue.FishImpact, SfxSynth.LandThud(_sfxRng), at, 0.95f);
        Synth(Cue.FishImpact, SfxSynth.ClawSlam(_sfxRng), at, 0.5f);
    }

    /// <summary>The spit: a small wet pop with none of the rifle's dry crack.</summary>
    public static void PlayFishSpit(Vector2 at)
    {
        var pop = SfxSynth.MawSpit(_sfxRng);
        pop.Length *= 0.55f;
        Synth(Cue.FishSpit, pop, at, 0.55f);
    }

    /// <summary>Meeting the seabed. <paramref name="force"/> scales the whole thing from a
    /// settling scrape to the full-weight thud of a body driven into the grid — plus, past
    /// halfway, the wail underneath it, which is the one place this chassis is allowed to
    /// sound like it is in distress.</summary>
    public static void PlayFishBeach(Vector2 at, float force)
    {
        force = Math.Clamp(force, 0f, 1f);
        Synth(Cue.FishBeach, SfxSynth.LandThud(_sfxRng), at, 0.4f + 0.6f * force);
        if (force > 0.5f) Synth(Cue.FishBeach, SfxSynth.MawWail(_sfxRng, force), at, force * 0.55f);
    }

    // --- Things that happen to you rather than somewhere ---------------------------

    /// <summary>
    /// Somebody put a mark on the world. Two clean tones a fifth apart, rising — the only
    /// deliberately <em>pleasant</em> sound in a bank otherwise made of grinding and blast,
    /// which is exactly what makes it read as a person talking rather than as the world
    /// doing something to you. Placed at the mark and barely attenuated, because the whole
    /// job of the cue is to say <em>where</em> to somebody facing the other way.
    /// </summary>
    public static void PlayMarker(Vector2 at)
    {
        var low = SfxSynth.WarningBeep(_sfxRng, 1);
        low.Length *= 0.6f;
        Synth(Cue.Marker, low, at, 0.55f);

        var high = SfxSynth.WarningBeep(_sfxRng, 3);
        high.Length *= 0.8f;
        Synth(Cue.Marker, high, at, 0.7f);
    }

    /// <summary>Salvage being absorbed. Reuses the menu blip — the only bright, non-combat
    /// transient in the bank — so a collect reads as a clean positive chirp against the
    /// grim combat clips. Flat: it happens on your own hull.</summary>
    public static void PlayPickup(Vector2 at) => Flat(Cue.Pickup, _blip);

    /// <summary>The refusal a full magazine gives when you right-click more rounds into it.
    /// The deliberate opposite of a pickup, so a rejected load never reads as a good one.</summary>
    public static void PlayFull() => FlatSynth(Cue.Pickup, SfxSynth.FullBuzz(_sfxRng), 0.9f);

    private static double _nextGasTick;
    private const double GasTickGap = 1.1;

    /// <summary>The reserve running dry: a small warning tick, rate-limited here rather
    /// than by the caller so it can safely be asked for every tick the gauge is low.</summary>
    public static void PlayGasLow()
    {
        if (!_enabled) return;
        double now = Raylib.GetTime();
        if (now < _nextGasTick) return;
        _nextGasTick = now + GasTickGap;
        FlatSynth(Cue.Warning, SfxSynth.WarningBeep(_sfxRng, 0), 0.35f);
    }

    private static double _nextBloomTick;
    private static double _nextBloomAlarm;
    private const double BloomTickGap = 0.85;
    private const double BloomAlarmGap = 1.6;

    /// <summary>Approaching the bloom: a small, dry warning tick. Deliberately not an alarm
    /// — this is the free notice, and making it frightening would waste the alarm that comes
    /// after it.</summary>
    public static void PlayBloomWarning()
    {
        if (!_enabled) return;
        double now = Raylib.GetTime();
        if (now < _nextBloomTick) return;
        _nextBloomTick = now + BloomTickGap;
        FlatSynth(Cue.Warning, SfxSynth.WarningBeep(_sfxRng, 1), 0.4f);
    }

    /// <summary>Actually in it. The stock alarm clip — the most alarming sound in the bank —
    /// over a rising tone, on its own slower clock so it tolls rather than screams.</summary>
    public static void PlayBloomAlarm()
    {
        if (!_enabled) return;
        double now = Raylib.GetTime();
        if (now < _nextBloomAlarm) return;
        _nextBloomAlarm = now + BloomAlarmGap;
        FlatSynth(Cue.Warning, SfxSynth.WarningBeep(_sfxRng, 3), 0.8f);
        Flat(Cue.Alarm, _alarm, 1f, 0.5f);
    }

    // --- Continuous beds -----------------------------------------------------------

    /// <summary>
    /// One looping voice owned by something in the world — a bed rather than an event,
    /// running for as long as its owner is on the field, with its playback rate driven by
    /// how worked-up that owner is.
    ///
    /// <para>This used to be a raylib <c>Music</c> stream, because a one-shot <c>Sound</c>
    /// leaves a frame-sized hole once per lap. The engine's voices loop natively, so a bed
    /// is now simply a voice that never ends — and, more to the point, a bed is now
    /// <em>placed</em>: a boss's rotor is genuinely off to your left when the boss is, which
    /// a stream with a single volume knob could never be.</para>
    ///
    /// <para>Rate and level are eased toward their targets rather than set outright, so
    /// <see cref="Set"/> is safe to call every tick with whatever state its owner is in.</para>
    /// </summary>
    private sealed class Bed
    {
        private readonly Cue _cue;
        private readonly float _wokenPitch;
        private Clip? _clip;
        private int _handle;

        private float _pitch = 1f, _pitchTarget = 1f;
        private float _level, _levelTarget;
        private Vector2 _at;

        /// <param name="wokenPitch">Playback rate once its owner is fully agitated. Driving
        /// the rate rather than the frequency is what makes it a faster <em>spin</em> than a
        /// higher note — every part of the loop, throb included, speeds up together.</param>
        public Bed(Cue cue, float wokenPitch)
        {
            _cue = cue;
            _wokenPitch = wokenPitch;
        }

        public void Load(float[] samples, string name)
            => _clip = AudioEngine.FromSamples(samples, Mixer.SampleRate, name);

        /// <summary>Sets this frame's targets. <paramref name="present"/> false fades the
        /// bed out and eventually frees its voice.</summary>
        public void Set(bool present, Vector2 at, float agitation)
        {
            _pitchTarget = 1f + (_wokenPitch - 1f) * Math.Clamp(agitation, 0f, 1f);
            _levelTarget = present ? 1f : 0f;
            if (present) _at = at;
        }

        /// <summary>Eases toward the targets and keeps the voice fed. Once per frame.</summary>
        public void Service(float dt)
        {
            if (_clip == null) return;

            _pitch = Approach(_pitch, _pitchTarget, SpoolRate * dt);
            _level = Approach(_level, _levelTarget, FadeRate * dt);

            if (_level > 0.001f && _handle == 0)
                _handle = _engine.StartBed((int)_cue, _clip, _at, 0f, _pitch);

            if (_handle != 0 && !_engine.BedAlive(_handle)) _handle = 0;

            if (_handle != 0)
            {
                if (_level <= 0.001f) { _engine.StopBed(_handle); _handle = 0; }
                else _engine.SetBed(_handle, _at, 0f, _pitch, _level);
            }
        }

        public void Stop()
        {
            if (_handle != 0) { _engine.StopBed(_handle); _handle = 0; }
            _level = _levelTarget = 0f;
        }

        /// <summary>How fast a bed spools between rates and levels, per second. Slow enough
        /// to hear it wind up — an instant jump sounds like a cut, not like a machine coming
        /// to life.</summary>
        private const float SpoolRate = 0.9f;
        private const float FadeRate = 1.6f;
    }

    /// <summary>The Crab-Core's internal rotor.</summary>
    private static readonly Bed _hum = new(Cue.BossHum, wokenPitch: 1.95f);

    /// <summary>The Maw-Core holding itself up. Deliberately a different sound from the
    /// crab's rotor rather than a re-pitch of it: two monsters that hummed alike would be
    /// impossible to tell apart out in the fog, and this one has to be identifiable as
    /// coming from <em>above</em> you.</summary>
    private static readonly Bed _mawHover = new(Cue.MawHover, wokenPitch: 1.7f);

    /// <summary>The SPIDER's lance charge. Flat — it is the player's own weapon, on their
    /// own hull. Its rate is driven hard because the whole point of the cue is that the
    /// pitch tells you how full the meter is without looking at it.</summary>
    private static readonly Bed _lanceCharge = new(Cue.LanceCharge, wokenPitch: 2.4f);

    /// <summary>The reel's gas jet. Its rate rides the reserve's pressure, which is what
    /// makes a starved reel audibly sag rather than merely pull less hard.</summary>
    private static readonly Bed _reelJet = new(Cue.ReelJet, wokenPitch: 1.9f);

    /// <summary>The air. Light rustle building to a roaring buffet — the single cue carrying
    /// most of the sense of speed, so it is the loudest bed in the game.</summary>
    private static readonly Bed _wind = new(Cue.Wind, wokenPitch: 2.2f);

    /// <summary>Steel under load. Quiet by design — it should be felt through the other two
    /// rather than heard over them, the way you notice a rope you are hanging from without
    /// listening to it.</summary>
    private static readonly Bed _cableStrain = new(Cue.CableStrain, wokenPitch: 1.6f);

    /// <summary>Past this many world units the rotor can't be heard. The cue table owns the
    /// real number now; the boss reads this one.</summary>
    public const float HumRange = 55f;

    /// <summary>Voices the Crab-Core's rotor for this frame. Safe to call every tick with
    /// whatever state the boss is in; <paramref name="agitation"/> is 0 while it idles and 1
    /// once it has noticed a player, so the machine audibly winds up the moment it wakes.</summary>
    public static void SetBossHum(bool present, Vector2 at, float agitation)
    {
        if (_enabled) _hum.Set(present, at, agitation);
    }

    /// <summary>The Maw-Core holding station overhead — its equivalent bed, driven the same
    /// way and running the whole time one is on the field.</summary>
    public static void SetMawHover(bool present, Vector2 at, float agitation)
    {
        if (_enabled) _mawHover.Set(present, at, agitation);
    }

    /// <summary>The SPIDER's lance winding up. Fed every tick with whatever state the
    /// emitter is in; releasing simply stops feeding it and the bed fades on its own.</summary>
    public static void SetLanceCharge(bool charging, float fraction)
    {
        if (_enabled) _lanceCharge.Set(charging, Vector2.Zero, fraction);
    }

    /// <summary>The reel's gas jet for this frame. <paramref name="pressure"/> is how much
    /// is left in the bottle, 0..1 — the roar climbs with it, so a dry reel is heard as a
    /// weak one before it is felt as one.</summary>
    public static void SetReel(bool reeling, float pressure)
    {
        if (_enabled) _reelJet.Set(reeling, Vector2.Zero, pressure);
    }

    /// <summary>The wind past the ears — a light rustle to a roaring buffet.</summary>
    public static void SetWind(bool fast, float intensity)
    {
        if (_enabled) _wind.Set(fast, Vector2.Zero, intensity);
    }

    /// <summary>The cables taking weight. The creak tightens with
    /// <paramref name="tension"/>, so a player at the bottom of a fast arc can hear how much
    /// the rig is being asked for.</summary>
    public static void SetCableStrain(bool loaded, float tension)
    {
        if (_enabled) _cableStrain.Set(loaded, Vector2.Zero, tension);
    }

    // --- Per-frame -----------------------------------------------------------------

    /// <summary>
    /// Drains scheduled sounds that have come due, services the beds and hands the engine
    /// this frame's listener. Call once per frame from the main loop, above every early-out,
    /// so a queued death cascade still finishes if the player pauses or exits to the menu.
    ///
    /// <paramref name="inWorld"/> is whether the player is actually in a match, which is all
    /// <see cref="MusicBox"/> needs from the game.
    /// </summary>
    public static void Update(bool inWorld)
    {
        if (!_enabled) return;

        float dt = Raylib.GetFrameTime();
        ServiceMusic(dt);
        MusicBox.Service(dt, inWorld);

        double now = Raylib.GetTime();
        for (int i = 0; i < BossBoomCount; i++)
        {
            if (!_boomPending[i] || now < _boomDue[i]) continue;
            _boomPending[i] = false;
            Post(Cue.BossDeath, _explosion, _boomAt[i], _boomPitch[i], _boomVol[i]);
        }

        _hum.Service(dt);
        _mawHover.Service(dt);
        _lanceCharge.Service(dt);
        _reelJet.Service(dt);
        _wind.Service(dt);
        _cableStrain.Service(dt);

        // The player's beds are fed-or-they-die, unlike the two monster beds. Those are
        // driven from the world's own step, which runs whenever their owner exists; these
        // are driven from the player's trigger handlers, which do not run at all while the
        // game is paused, while a cinematic has the craft, or after a bail to the menu.
        // Clearing the target here — after servicing, so a frame that did set it still
        // counts — means any frame that stops asking lets the bed fade rather than hang.
        _lanceCharge.Set(false, Vector2.Zero, 0f);
        _reelJet.Set(false, Vector2.Zero, 0f);
        _wind.Set(false, Vector2.Zero, 0f);
        _cableStrain.Set(false, Vector2.Zero, 0f);

        _engine.Update(_ear, dt);
    }

    /// <summary>Cuts every voice at once — a scene change, a bail to the menu.</summary>
    public static void Silence()
    {
        if (!_enabled) return;
        _hum.Stop();
        _mawHover.Stop();
        _lanceCharge.Stop();
        _reelJet.Stop();
        _wind.Stop();
        _cableStrain.Stop();
        for (int i = 0; i < BossBoomCount; i++) _boomPending[i] = false;
        _engine.StopAll();
    }

    /// <summary>Moves <paramref name="v"/> toward <paramref name="target"/> by at most
    /// <paramref name="step"/>, without overshooting.</summary>
    private static float Approach(float v, float target, float step)
    {
        if (v < target) return MathF.Min(target, v + step);
        return MathF.Max(target, v - step);
    }

    /// <summary>Closes the mixer and the device. Mirrors <see cref="Init"/>.</summary>
    public static void Shutdown()
    {
        if (!_enabled) return;
        MusicBox.Shutdown();
        _engine.Close();
        Raylib.CloseAudioDevice();
        _enabled = false;
    }
}
