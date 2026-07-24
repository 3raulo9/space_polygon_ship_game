using System.Runtime.InteropServices;
using Raylib_cs;
using VoidTanks.Entities;   // CrabRig, for the boss's leg count

namespace VoidTanks.Core;

/// <summary>
/// Central sound-effects bank. Loads the game's clips once and plays them by
/// name from anywhere in the sim. Every call is a no-op until <see cref="Init"/>
/// has run against a live audio device, so the headless self-test (which never
/// opens a window or an audio device) can drive the same simulation code without
/// touching Raylib's audio at all.
/// </summary>
public static class Audio
{
    // Guards every play/load call. The self-test leaves this false, so audio is
    // silently skipped; normal boot flips it on after InitAudioDevice succeeds.
    private static bool _enabled;

    private static Sound _blip;         // menu cursor moving between options
    private static Sound _detonation;   // a barrel firing — player or enemy shot
    private static Sound _explosion;    // a tank being destroyed (player or enemy)
    private static Sound _distantBoom;  // an air shot coming down far off on the horizon
    private static Sound _hit;          // the player's craft taking a hit
    private static Sound _warning;      // shield crosses the low-health line
    private static Sound _stomp;        // a Crab-Core foot planting on the grid
    private static Sound _alarm;        // Crab-Core threat-display lurch to one side

    // Assets are copied next to the executable by the .csproj, so a relative
    // path off the working directory resolves at runtime.
    private const string SfxDir = "Assets/Audio/SFX/";

    /// <summary>
    /// Opens the audio device and loads every clip. Call once at startup, after
    /// the window exists. Safe to skip entirely (the sim just stays silent).
    /// </summary>
    public static void Init()
    {
        Raylib.InitAudioDevice();
        _blip = Load("blip.wav");
        _detonation = Load("detonation.wav");
        _explosion = Load("explosion.wav");
        _distantBoom = Load("distantBoom.wav");
        _hit = Load("hit.wav");
        _warning = Load("warning.wav");
        _stomp = Load("stomping.wav");
        _alarm = Load("scaryAlarm.wav");

        // Aliases — must come after their source clips are loaded, since they borrow
        // those samples rather than owning any. Each set exists so one clip can
        // overlap itself: seven simultaneous death blasts, a whole tripod of feet
        // landing at once, and core pings that don't cut each other off.
        for (int i = 0; i < BossBoomCount; i++)
            _bossBooms[i] = Raylib.LoadSoundAlias(_explosion);
        for (int i = 0; i < _stompVoices.Length; i++)
            _stompVoices[i] = Raylib.LoadSoundAlias(_stomp);
        for (int i = 0; i < CoreHitVoices; i++)
            _coreHits[i] = Raylib.LoadSoundAlias(_hit);
        for (int i = 0; i < BeamWarnVoices; i++)
            _beamWarns[i] = Raylib.LoadSoundAlias(_warning);

        // The two continuous beds, synthesised once per session so each run's monsters
        // hum and hover at slightly different pitches and throbs. Rolled here rather
        // than per monster: they are streaming loops, not one-shots, and rebuilding
        // one mid-fight would mean tearing down a stream that is currently audible.
        _hum.Load(SfxSynth.RenderWav(SfxSynth.Hum(_sfxRng)));
        _mawHover.Load(SfxSynth.RenderWav(SfxSynth.MawHover(_sfxRng)));
        // The player's own bed: the SPIDER's lance winding up. Same machinery as the
        // monsters' — a loop whose playback rate is driven per frame — because the
        // charge has no fixed length, so no one-shot envelope could ever match it.
        _lanceCharge.Load(SfxSynth.RenderWav(SfxSynth.LanceCharge(_sfxRng)));

        // And the SOLDIER's two: the gas jet under a reel, and the wind. Beds for the
        // same reason — neither has a length. A swing lasts as long as it lasts, and the
        // one thing audio has to do on that chassis is carry the sense of speed
        // continuously rather than in events.
        _reelJet.Load(SfxSynth.RenderWav(SfxSynth.ReelJet(_sfxRng)));
        _wind.Load(SfxSynth.RenderWav(SfxSynth.WindRush(_sfxRng)));
        _cableStrain.Load(SfxSynth.RenderWav(SfxSynth.CableStrain(_sfxRng)));

        _enabled = true;
    }

    /// <summary>Menu cursor stepping to a new option. Mapped to blip.wav.</summary>
    public static void PlayBlip()
    {
        if (_enabled) Raylib.PlaySound(_blip);
    }

    /// <summary>A shot leaving a barrel — player or enemy. Mapped to detonation.wav.</summary>
    public static void PlayDetonation()
    {
        if (_enabled) Raylib.PlaySound(_detonation);
    }

    /// <summary>A tank being destroyed — player or enemy. Mapped to explosion.wav.</summary>
    public static void PlayExplosion()
    {
        if (!_enabled) return;
        Raylib.SetSoundVolume(_explosion, 1f);   // full-volume, up close
        Raylib.PlaySound(_explosion);
    }

    /// <summary>
    /// The dedicated far-off detonation — an air shot coming down out on the horizon.
    /// Its own low, rolling boom (distantBoom.wav), with volume falling off further
    /// with range so a shot landing deep downrange is a faint thud rather than a bang
    /// in your ear.
    /// </summary>
    public static void PlayExplosionAt(float distance)
    {
        if (!_enabled) return;
        // Linear falloff to a quiet floor: nearby is full, ~200 units out is barely
        // there. Clamped so it never goes fully silent or over unity.
        float vol = Math.Clamp(1f - distance / 200f, 0.1f, 1f);
        Raylib.SetSoundVolume(_distantBoom, vol);
        Raylib.PlaySound(_distantBoom);
    }

    /// <summary>The player's craft absorbing a hit.</summary>
    public static void PlayHit()
    {
        if (_enabled) Raylib.PlaySound(_hit);
    }

    /// <summary>Low-shield alarm — fired once when crossing the threshold, not per frame.</summary>
    public static void PlayWarning()
    {
        if (_enabled) Raylib.PlaySound(_warning);
    }

    /// <summary>
    /// A support giving way — the groan that warns a topple has begun and a second or so of
    /// falling mass is coming, giving anything under it time to move. A low stone-grind (the
    /// same the thrown core makes, dropped in level), mixed down with range.
    /// </summary>
    public static void PlayStructureGroan(float distance)
    {
        if (!_enabled) return;
        float vol = Math.Clamp(1f - distance / 220f, 0.12f, 0.9f);
        PlaySynth(SfxSynth.CrabBlastGrind(_sfxRng), vol);
    }

    /// <summary>
    /// A crack of masonry shearing off where a beam is biting a structure — short and hard,
    /// jittered across the synth pool so a dwelling beam reads as stone breaking rather than
    /// one clip stuttering. Quieter and shorter-ranged than a detonation: it is a chip, not
    /// the collapse.
    /// </summary>
    public static void PlayStructureCrack(float distance)
    {
        if (!_enabled) return;
        float vol = Math.Clamp(1f - distance / 160f, 0.1f, 0.6f);
        PlaySynth(SfxSynth.RifleCrack(_sfxRng), vol);
    }

    // --- The Crab-Core's lance: charge, three warnings, then the beam ---------

    /// <summary>
    /// The boss's crystal spinning up to fire. One shot, fired as the charge begins —
    /// <see cref="SfxSynth.BeamCharge"/>'s own envelope carries it across the whole
    /// wind-up, so nothing has to drive it per-frame.
    /// </summary>
    public static void PlayBeamCharge()
    {
        if (_enabled) PlaySynth(SfxSynth.BeamCharge(_sfxRng));
    }

    /// <summary>Voices for the beam's warnings. Aliases of warning.wav so the three
    /// can overlap each other and, more importantly, so re-pitching them never
    /// touches the pitch of the low-shield alarm, which shares the source clip and
    /// must always sound the same.</summary>
    private const int BeamWarnVoices = 3;
    private static readonly Sound[] _beamWarns = new Sound[BeamWarnVoices];

    /// <summary>
    /// One of the three warnings that count the charge down. <paramref name="step"/>
    /// is which it is (0, 1, 2), and both layers climb with it: the bank's alarm clip
    /// is re-pitched a clear step higher each time — the slide up — under a
    /// synthesised beep (<see cref="SfxSynth.WarningBeep"/>) that steps with it.
    ///
    /// The pairing is what makes it land: the clip alone is a sound the player has
    /// heard all game meaning "your shield is low", and hearing it here would read as
    /// the wrong alarm. Sliding it upward and welding a synthetic tone to it turns it
    /// into something the boss is doing rather than something the craft is reporting.
    /// </summary>
    public static void PlayBeamWarning(int step)
    {
        if (!_enabled) return;
        int i = Math.Clamp(step, 0, BeamWarnVoices - 1);

        Sound voice = _beamWarns[i];
        Raylib.SetSoundPitch(voice, 1f + i * 0.32f);   // the slide up
        Raylib.SetSoundVolume(voice, 0.85f);
        Raylib.PlaySound(voice);

        PlaySynth(SfxSynth.WarningBeep(_sfxRng, i));
    }

    /// <summary>
    /// The beam firing: two clean synthesised voices a fifth apart
    /// (<see cref="SfxSynth.BeamAngelic"/> and <see cref="SfxSynth.BeamChoir"/>),
    /// both exactly as long as the burn, so the sound ends when the light does. The
    /// only consonant, un-crushed thing in the bank — see the recipes for why the
    /// lethal attack is the pretty one.
    /// </summary>
    public static void PlayBeamFire()
    {
        if (!_enabled) return;
        PlaySynth(SfxSynth.BeamAngelic(_sfxRng));
        PlaySynth(SfxSynth.BeamChoir(_sfxRng));
    }

    /// <summary>
    /// A thrown CRAB CORE going off: the boss's own sung beam voices, layered under two
    /// crushed mechanical layers — a low dissonant grind and a metallic clatter — plus a
    /// clamp snap at the front, so the radial star reads as the crab's attack torn loose
    /// and misfiring. Creepier and more machined than the clean lance it echoes.
    /// </summary>
    public static void PlayCrabCoreBlast()
    {
        if (!_enabled) return;
        // The sung pair, still present but now buried under the wrongness.
        PlaySynth(SfxSynth.BeamAngelic(_sfxRng), 0.7f);
        PlaySynth(SfxSynth.BeamChoir(_sfxRng), 0.6f);
        // The two degraded layers that make it sound broken.
        PlaySynth(SfxSynth.CrabBlastGrind(_sfxRng));
        PlaySynth(SfxSynth.CrabBlastMetal(_sfxRng));
        // A mechanical snap of the housing as it discharges.
        PlayClamp();
    }

    /// <summary>
    /// One of the SPIDER's small lasers leaving the emitter: a dry zap and, a beat
    /// behind it, the same zap again at a fifth of the level and a touch lower — a tiny
    /// echo, and nothing more. The cannon's <see cref="PlayDetonation"/> is a report
    /// with body to it, which at the laser's cadence stacks into a continuous roar; this
    /// is built to get out of the way the instant the shot has left.
    /// </summary>
    public static void PlayLaser()
    {
        if (!_enabled) return;
        PlaySynth(SfxSynth.Laser(_sfxRng));

        // The echo is the same recipe rendered again rather than a delay line — there
        // is no delay in the synth — so it is a genuinely different roll of the same
        // sound, which is closer to a reflection than a duplicate would be.
        var tail = SfxSynth.Laser(_sfxRng);
        tail.StartFreq *= 0.82f;
        tail.EndFreq *= 0.82f;
        tail.Length *= 1.3f;
        PlaySynth(tail, 0.22f);
    }

    /// <summary>
    /// The SPIDER's lance discharging: a short crushed discharge, a clamp snap for the
    /// housing, a quiet tail a fifth down — and, underneath all of it, the Crab-Core's
    /// own sung beam.
    ///
    /// That last layer is the point of the class. The chassis is a boss's weapon cut
    /// down and bolted to a person, and the sung pair is the single most recognisable
    /// sound the boss makes; hearing a ghost of it every time you fire is what tells the
    /// player, without a line of text, whose gun they are holding. It is mixed low and
    /// — critically — rendered at the <em>player's</em> burn length rather than the
    /// boss's, since <see cref="PlayCrabCoreBlast"/>'s five-second voices are what left
    /// the old cue sounding for four seconds after the light had gone. Same voice,
    /// quarter the level, a tenth of the length: present, not playing.
    /// </summary>
    public static void PlayLanceFire()
    {
        if (!_enabled) return;
        PlaySynth(SfxSynth.LanceFire(_sfxRng));

        var tail = SfxSynth.LanceFire(_sfxRng);
        tail.StartFreq *= 0.66f;
        tail.EndFreq *= 0.66f;
        PlaySynth(tail, 0.3f);

        // The boss's beam, cut to the length of the shaft the player actually fired.
        // Keyed off SpiderWeapon.BeamTime rather than a literal so retuning the burn
        // moves the sound with it instead of quietly desynchronising the two.
        float burn = SpiderWeapon.BeamTime;

        var sung = SfxSynth.BeamAngelic(_sfxRng);
        sung.Length = burn;
        PlaySynth(sung, 0.26f);

        var choir = SfxSynth.BeamChoir(_sfxRng);
        choir.Length = burn;
        PlaySynth(choir, 0.18f);

        PlayClamp();
    }

    /// <summary>
    /// The worn Crab-Core's lance — the boss's own discharge fired through a corrupted
    /// core, and nothing about it sits right. The main shot goes out detuned off true by a
    /// fresh random amount every pull, with two more rolls of the same recipe under it
    /// shoved a long way apart in pitch — a weapon audibly firing several ways at once,
    /// which is exactly what the picture shows. Under that, the thrown core's grind and
    /// metal layers do the work they were built for: failing machinery. And where the
    /// intact lance carries a ghost of the boss's sung beam, this one gets a single sick,
    /// warbling fragment of it — the choir is in there, but it is not singing anymore.
    /// </summary>
    public static void PlayUnstableLance()
    {
        if (!_enabled) return;

        // The main discharge, off true — differently off true every time.
        var main = SfxSynth.LanceFire(_sfxRng);
        main.StartFreq *= 0.85f + (float)_sfxRng.NextDouble() * 0.35f;
        main.EndFreq *= 0.6f + (float)_sfxRng.NextDouble() * 0.6f;
        PlaySynth(main);

        // The breaks: the same recipe again, thrown high and short and low and dragging —
        // one roll per direction the beam visibly went.
        var high = SfxSynth.LanceFire(_sfxRng);
        high.StartFreq *= 1.5f;
        high.EndFreq *= 1.8f;
        high.Length *= 0.45f;
        PlaySynth(high, 0.4f);

        var low = SfxSynth.LanceFire(_sfxRng);
        low.StartFreq *= 0.45f;
        low.EndFreq *= 0.35f;
        low.Length *= 1.4f;
        PlaySynth(low, 0.5f);

        // Machinery failing under the shot.
        PlaySynth(SfxSynth.CrabBlastGrind(_sfxRng), 0.5f);
        PlaySynth(SfxSynth.CrabBlastMetal(_sfxRng), 0.45f);

        // The sung beam, sick: a short fragment bent a whole fourth downward mid-note.
        var sung = SfxSynth.BeamAngelic(_sfxRng);
        sung.Length = 0.5f;
        sung.EndFreq = sung.StartFreq * 0.75f;
        PlaySynth(sung, 0.2f);

        PlayClamp();
    }

    // --- Synthesised cues: built from nothing, fresh on every trigger ---------
    // These load no asset. SfxSynth rolls a new recipe each time and renders it to
    // .wav bytes in memory, so the sounds below never repeat themselves.

    /// <summary>How many generated clips stay alive at once. A slot is only reused
    /// once the mixer has actually finished with the clip in it — age alone is not
    /// enough, since a crab fight can fire sixteen short cues (footsteps, clamps,
    /// hunting calls) while a seconds-long beam voice is still sounding, and freeing
    /// a clip out from under the audio thread corrupts the mixer. Shared by every
    /// synthesised cue, which also lets them overlap each other freely (a file-backed
    /// Sound can't overlap itself; these are all distinct objects).</summary>
    private const int SynthPoolSize = 32;

    private static readonly Sound[] _synthPool = new Sound[SynthPoolSize];
    private static readonly bool[] _synthLoaded = new bool[SynthPoolSize];
    private static int _synthSlot;

    private static readonly Random _sfxRng = new();

    /// <summary>
    /// Renders a recipe, hands it to the audio device and plays it, reusing a ring
    /// slot whose previous clip has finished sounding. If every slot is still
    /// audible the cue is dropped rather than stealing a voice — silence for one
    /// footstep is cheaper than freeing a buffer the mixer is reading.
    /// </summary>
    private static void PlaySynth(SfxSynth.Params p, float volume = 1f)
    {
        // Claim a slot before rendering anything: a slot is free if it never held a
        // clip, or if the one it holds has run out.
        int slot = -1;
        for (int i = 0; i < SynthPoolSize; i++)
        {
            int c = (_synthSlot + i) % SynthPoolSize;
            if (!_synthLoaded[c] || !Raylib.IsSoundPlaying(_synthPool[c])) { slot = c; break; }
        }
        if (slot < 0) return;   // everything still sounding — drop this one

        // Raylib copies the samples into the Sound's own buffer, so the staging
        // Wave can go straight back after the handoff.
        Wave wave = Raylib.LoadWaveFromMemory(".wav", SfxSynth.RenderWav(p));
        Sound sound = Raylib.LoadSoundFromWave(wave);
        Raylib.UnloadWave(wave);

        if (_synthLoaded[slot]) Raylib.UnloadSound(_synthPool[slot]);
        _synthPool[slot] = sound;
        _synthLoaded[slot] = true;
        _synthSlot = (slot + 1) % SynthPoolSize;

        if (volume < 1f) Raylib.SetSoundVolume(sound, volume);
        Raylib.PlaySound(sound);
    }

    private static double _lastClampTime = double.NegativeInfinity;
    private static int _clampStep;

    /// <summary>A gap longer than this means a new charge cycle has begun, so the
    /// spin-up starts over from its lowest step instead of climbing forever.</summary>
    private const double ClampBurstGap = 1.2;

    /// <summary>
    /// The Crab-Core winding up to attack — its claw-plates snapping shut as the
    /// machine draws power. Synthesised on the spot, so no two clamps in the game
    /// ever sound alike.
    ///
    /// Consecutive snaps inside one burst climb in pitch (see
    /// <see cref="SfxSynth.CreepyPowerUp"/>), so the boss's three clicks build into
    /// a single spin-up rather than repeating; leave it alone for
    /// <see cref="ClampBurstGap"/> seconds and the next burst resets to the bottom.
    /// </summary>
    public static void PlayClamp()
    {
        if (!_enabled) return;

        // Where in the spin-up this snap falls — restart the climb after a lull.
        double now = Raylib.GetTime();
        _clampStep = now - _lastClampTime > ClampBurstGap ? 0 : _clampStep + 1;
        _lastClampTime = now;

        PlaySynth(SfxSynth.CreepyPowerUp(_sfxRng, _clampStep));
    }

    /// <summary>Past this many world units a hunting Crab-Core is inaudible.</summary>
    public const float HuntRange = 65f;

    private static double _nextHuntTime;

    /// <summary>
    /// The Crab-Core's hunting call, voiced while it is running you down: a low,
    /// wavering machine-groan that swells up out of the floor and sags away again,
    /// each one a different shape (see <see cref="SfxSynth.HuntingCall"/>).
    ///
    /// Safe to call every single tick of the pursuit — this method owns its own
    /// cadence and rate-limits itself to one call every <see cref="HuntMinGap"/>..
    /// <see cref="HuntMaxGap"/> seconds, re-rolled each time so the calls never fall
    /// into an audible rhythm. Keeping the jitter here rather than in the boss also
    /// keeps the simulation itself deterministic.
    ///
    /// <paramref name="distance"/> is the world gap to the player: the groan fades
    /// out with range so a crab hunting you from across the arena is a tremor
    /// somewhere behind you, and only becomes a throat-level growl as it closes.
    /// </summary>
    public static void PlayHuntCall(float distance)
    {
        if (!_enabled) return;

        double now = Raylib.GetTime();
        if (now < _nextHuntTime) return;
        _nextHuntTime = now + HuntMinGap + _sfxRng.NextDouble() * (HuntMaxGap - HuntMinGap);

        if (distance >= HuntRange) return;      // too far off to hear at all
        // Falls away with the square root of range rather than linearly, so the
        // groan stays present through the middle distance and only truly thins out
        // at the edge — it should feel like it is following you, not switching off.
        float vol = MathF.Sqrt(1f - distance / HuntRange);
        PlaySynth(SfxSynth.HuntingCall(_sfxRng), vol);
    }

    /// <summary>Bounds on the irregular gap between hunting calls, in seconds.</summary>
    private const double HuntMinGap = 0.75;
    private const double HuntMaxGap = 1.9;

    // --- The seizure: the boss has hold of the player ------------------------

    /// <summary>
    /// The Crab-Core screaming point-blank into the held player. Two synthesised
    /// voices fired on the same tick — a rising, dissonant shriek
    /// (<see cref="SfxSynth.CrabScream"/>) and a slow heaving sub-roar under it
    /// (<see cref="SfxSynth.CrabScreamUnder"/>). They are deliberately voiced as a
    /// pair: the shriek alone is thin and reads as a noise being played at the
    /// player, while the low layer gives it a body and makes it something with mass
    /// doing the screaming.
    ///
    /// The synth pool holds distinct Sound objects, so the two layers overlap
    /// cleanly rather than cutting each other off the way one re-triggered clip
    /// would. No distance falloff: this happens with the crab's face in yours.
    /// </summary>
    public static void PlayCrabScream()
    {
        if (!_enabled) return;
        PlaySynth(SfxSynth.CrabScream(_sfxRng));
        PlaySynth(SfxSynth.CrabScreamUnder(_sfxRng));
    }

    /// <summary>
    /// The free claw landing its blow on the held player. The bank's explosion clip
    /// carries the blast, with a synthesised crunch over the top
    /// (<see cref="SfxSynth.ClawSlam"/>) so the hit reads as a heavy mass connecting
    /// rather than another detonation — the player has heard that clip all game, and
    /// this moment should not sound like anything else in it.
    /// </summary>
    public static void PlayClawSlam()
    {
        if (!_enabled) return;
        Raylib.SetSoundVolume(_explosion, 1f);
        Raylib.PlaySound(_explosion);
        PlaySynth(SfxSynth.ClawSlam(_sfxRng));
    }

    /// <summary>The air tearing past across the thrown player's arc. One shot, fired
    /// as they leave the claw — the recipe's own envelope shapes it over the flight,
    /// so nothing has to drive it per-frame.</summary>
    public static void PlayThrowWhoosh()
    {
        if (_enabled) PlaySynth(SfxSynth.ThrowWhoosh(_sfxRng));
    }

    /// <summary>The craft coming down at the end of the throw: a low synthesised
    /// thud under the bank's standard impact clip, so the landing registers as
    /// damage taken and not just a sound effect.</summary>
    public static void PlayCrashLanding()
    {
        if (!_enabled) return;
        Raylib.PlaySound(_hit);
        PlaySynth(SfxSynth.LandThud(_sfxRng));
    }

    // --- The Crab-Core's death: a scream over a cascade of pitched blasts -----

    /// <summary>How many detonations tear through the rig as it comes apart.</summary>
    private const int BossBoomCount = 7;

    /// <summary>Seconds the cascade is spread across — matched to the length of the
    /// boss's death glitch, so the last blast lands as the rig finishes tearing.</summary>
    private const double BossBoomSpread = 1.15;

    // Aliases of the explosion clip. An alias shares the source's sample data but
    // carries its own playback head, pitch and volume — which is the only way one
    // clip can overlap itself. Playing _explosion seven times would just restart
    // the same voice and yield a single blast.
    private static readonly Sound[] _bossBooms = new Sound[BossBoomCount];

    // The pending cascade: each blast's due time, pitch and level.
    private static readonly double[] _boomDue = new double[BossBoomCount];
    private static readonly float[] _boomPitch = new float[BossBoomCount];
    private static readonly float[] _boomVol = new float[BossBoomCount];
    private static readonly bool[] _boomPending = new bool[BossBoomCount];

    /// <summary>
    /// The Crab-Core coming apart. Fires a long falling scream
    /// (<see cref="SfxSynth.DeathScream"/>) immediately, then schedules
    /// <see cref="BossBoomCount"/> detonations across the next
    /// <see cref="BossBoomSpread"/> seconds — the same explosion clip replayed
    /// through aliases, each pitched lower than the last so the cascade descends
    /// with the scream: it opens on tight, high cracks up where the core sat and
    /// ends on a slow, detuned boom as the carapace hits the grid.
    ///
    /// The blasts are only queued here; <see cref="Update"/> voices them as they
    /// come due.
    /// </summary>
    public static void PlayBossDeath()
    {
        if (!_enabled) return;

        PlaySynth(SfxSynth.DeathScream(_sfxRng));

        double now = Raylib.GetTime();
        for (int i = 0; i < BossBoomCount; i++)
        {
            float f = (float)i / (BossBoomCount - 1);        // 0..1 through the cascade

            // Jittered spacing — an even one would tick like a metronome and read
            // as mechanical rather than as a structure failing.
            double jitter = (_sfxRng.NextDouble() - 0.5) * 0.07;
            _boomDue[i] = now + f * BossBoomSpread + jitter;

            // Pitch walks down from a tight crack to a dragging sub-boom.
            _boomPitch[i] = 1.85f - f * 1.5f + (float)(_sfxRng.NextDouble() - 0.5) * 0.12f;

            // ...and the level climbs, so the deepest blast is also the heaviest and
            // the whole cascade lands on it rather than petering out.
            _boomVol[i] = 0.62f + f * 0.38f;

            _boomPending[i] = true;
        }
    }

    // --- Continuous beds: the crab's rotor and the maw's hover ----------------

    /// <summary>
    /// One looping ambience owned by a monster — a bed rather than an event, running
    /// for as long as its owner is on the field, with its playback rate driven by how
    /// worked-up that owner is.
    ///
    /// A looping Music stream rather than a Sound. A Sound is a one-shot: to hold a
    /// continuous bed you would have to re-trigger it as it ended, and the frame of
    /// slack between "finished" and "restarted" is an audible hole once per lap. A
    /// Music stream loops in the mixer itself, seamlessly.
    ///
    /// Both rate and level are eased toward their targets rather than set outright, so
    /// <see cref="Set"/> is safe to call every tick with whatever the monster's
    /// current state happens to be.
    /// </summary>
    private sealed class Bed
    {
        private Music _music;
        private bool _ready;        // stream exists
        private bool _running;      // ...and is currently voiced
        private IntPtr _wav;        // its encoded bytes, in unmanaged memory

        private float _pitch = 1f, _pitchTarget = 1f;
        private float _volume, _volumeTarget;

        private readonly float _wokenPitch;
        private readonly float _range;
        private readonly float _maxVolume;

        /// <param name="wokenPitch">Playback rate once its owner is fully agitated.
        /// Driving the rate rather than the frequency is what makes it a faster
        /// <em>spin</em> than a higher note — every part of the loop, throb included,
        /// speeds up together.</param>
        /// <param name="range">Past this many world units it can't be heard.</param>
        /// <param name="maxVolume">Level with the player standing underneath it. Kept
        /// well under the one-shot cues: a bed runs constantly, so it should be felt
        /// rather than listened to.</param>
        public Bed(float wokenPitch, float range, float maxVolume)
        {
            _wokenPitch = wokenPitch;
            _range = range;
            _maxVolume = maxVolume;
        }

        /// <summary>
        /// Renders the recipe and hands it to raylib as a streaming Music.
        ///
        /// The bytes must be copied into unmanaged memory first, and must stay there
        /// for as long as the stream lives. Unlike a Sound — which is decoded up front
        /// into its own buffer — a Music stream decodes lazily: raylib keeps a bare
        /// pointer to this buffer and reads more of it on every UpdateMusicStream.
        /// Handing it a managed byte[] pins that array only for the duration of the
        /// P/Invoke, so once Init returns the GC is free to move or collect it and the
        /// stream is left reading whatever now occupies that address. The result is a
        /// use-after-free that only bites once a collection happens to run — which is
        /// why it looked like a Crab-Core bug: the fight is what allocates hard enough
        /// (a fresh WAV per footstep, clamp and hunting call) to trigger the GC that
        /// pulls the rug.
        /// </summary>
        public unsafe void Load(byte[] wav)
        {
            _wav = Marshal.AllocHGlobal(wav.Length);
            Marshal.Copy(wav, 0, _wav, wav.Length);

            fixed (byte* type = ".wav"u8)   // u8 literals are NUL-terminated
                _music = Raylib.LoadMusicStreamFromMemory((sbyte*)type, (byte*)_wav, wav.Length);

            _music.Looping = true;
            _ready = true;
        }

        /// <summary>Sets this frame's targets. <paramref name="present"/> is false
        /// whenever there is no live owner, which fades the bed out and stops it.</summary>
        public void Set(bool present, float distance, float agitation)
        {
            if (!_ready) return;
            _pitchTarget = 1f + (_wokenPitch - 1f) * Math.Clamp(agitation, 0f, 1f);
            _volumeTarget = present && distance < _range
                ? _maxVolume * (1f - distance / _range)
                : 0f;
        }

        /// <summary>Eases toward the targets and refills the stream. Once per frame.</summary>
        public void Service(float dt)
        {
            if (!_ready) return;

            _pitch = Approach(_pitch, _pitchTarget, SpoolRate * dt);
            _volume = Approach(_volume, _volumeTarget, FadeRate * dt);

            // Start on the first frame it is wanted and stop once it has faded out, so
            // an empty arena costs nothing and no bed is left running after its owner
            // is gone.
            if (_volume > 0.001f && !_running)
            {
                Raylib.PlayMusicStream(_music);
                _running = true;
            }
            else if (_volume <= 0.001f && _running)
            {
                Raylib.StopMusicStream(_music);
                _running = false;
            }

            if (!_running) return;
            Raylib.SetMusicPitch(_music, _pitch);
            Raylib.SetMusicVolume(_music, _volume);
            Raylib.UpdateMusicStream(_music);     // refills the stream's buffers
        }

        /// <summary>Stops and frees the stream, then the buffer it was reading from —
        /// in that order, since the stream holds a bare pointer into it.</summary>
        public void Unload()
        {
            if (_ready)
            {
                if (_running) { Raylib.StopMusicStream(_music); _running = false; }
                Raylib.UnloadMusicStream(_music);
                _ready = false;
            }
            if (_wav != IntPtr.Zero) { Marshal.FreeHGlobal(_wav); _wav = IntPtr.Zero; }
        }

        /// <summary>How fast a bed spools between rates and levels, per second. Slow
        /// enough to hear it wind up — an instant jump sounds like a cut, not like a
        /// machine coming to life.</summary>
        private const float SpoolRate = 0.9f;
        private const float FadeRate = 1.6f;
    }

    /// <summary>The Crab-Core's internal rotor.</summary>
    private static readonly Bed _hum = new(wokenPitch: 1.95f, range: HumRange, maxVolume: 0.42f);

    /// <summary>The Maw-Core holding itself up. Carries further than the crab's rotor
    /// and sits a touch quieter: it is in the air, so there is nothing between it and
    /// the player, but it is also the sound of something hovering rather than of
    /// machinery grinding through a floor.</summary>
    private static readonly Bed _mawHover = new(wokenPitch: 1.7f, range: 70f, maxVolume: 0.38f);

    /// <summary>Past this many world units the rotor can't be heard.</summary>
    public const float HumRange = 55f;

    // --- The core taking a hit -----------------------------------------------

    // Round-robin voices for the core ping, so rapid fire overlaps instead of
    // cutting itself off mid-ring.
    private const int CoreHitVoices = 3;
    /// <summary>
    /// The SPIDER's lance charge. Ranged at 1 unit and always fed a distance of 0, since
    /// it is the player's own weapon and there is nothing to fall off with — the bed's
    /// distance channel is simply not the axis this one varies on. Its rate is driven
    /// hard (a wide spool) because the whole point of the cue is that the pitch tells
    /// you how full the meter is without looking at it.
    /// </summary>
    private static readonly Bed _lanceCharge = new(wokenPitch: 2.4f, range: 1f, maxVolume: 0.5f);

    private static readonly Sound[] _coreHits = new Sound[CoreHitVoices];
    private static int _coreHitSlot;

    /// <summary>
    /// An air shot threading the Crab-Core's exposed gem. Layers the bank's impact
    /// clip — pitched up, because this is glass and neon rather than the armour a
    /// tank hit lands on — under a synthesised high shriek that gets higher, faster
    /// and more unstable the closer the core is to going
    /// (<see cref="SfxSynth.CoreSting"/>).
    ///
    /// <paramref name="severity"/> runs 0 on the first hit to 1 as the last of the
    /// core's integrity goes, so the boss audibly comes apart across the fight
    /// instead of making the same noise four times.
    /// </summary>
    public static void PlayCoreHit(float severity)
    {
        if (!_enabled) return;
        severity = Math.Clamp(severity, 0f, 1f);

        Sound voice = _coreHits[_coreHitSlot];
        _coreHitSlot = (_coreHitSlot + 1) % CoreHitVoices;
        Raylib.SetSoundPitch(voice, 1.35f + severity * 0.45f);
        Raylib.SetSoundVolume(voice, 1f);
        Raylib.PlaySound(voice);

        PlaySynth(SfxSynth.CoreSting(_sfxRng, severity));
    }

    /// <summary>
    /// Voices the Crab-Core's internal rotor for this frame. <paramref name="present"/>
    /// is false whenever there is no live boss, which fades the hum out and stops it;
    /// <paramref name="distance"/> is the world gap to the player, and
    /// <paramref name="agitation"/> is 0 while it idles and 1 once it has noticed the
    /// player — the rotor spools up as that rises, so the machine audibly winds up the
    /// moment it wakes. Safe to call every tick with whatever state the boss is in.
    /// </summary>
    public static void SetBossHum(bool present, float distance, float agitation)
    {
        if (_enabled) _hum.Set(present, distance, agitation);
    }

    /// <summary>
    /// The Maw-Core holding station overhead — its equivalent bed, driven the same way
    /// and running the whole time one is on the field. Deliberately a different sound
    /// from the crab's rotor rather than a re-pitch of it: two monsters that hummed
    /// alike would be impossible to tell apart when both are out in the fog, and this
    /// one has to be identifiable as coming from <em>above</em> you.
    /// </summary>
    public static void SetMawHover(bool present, float distance, float agitation)
    {
        if (_enabled) _mawHover.Set(present, distance, agitation);
    }

    /// <summary>
    /// The SPIDER's lance winding up. <paramref name="charging"/> is whether the trigger
    /// is currently held and <paramref name="fraction"/> is the meter, 0..1 — the whine
    /// climbs with it and holds once the meter tops out. Fed every tick with whatever
    /// state the emitter is in; releasing simply stops feeding it, and the bed's own
    /// fade carries the tail out over a fraction of a second.
    /// </summary>
    public static void SetLanceCharge(bool charging, float fraction)
    {
        if (_enabled) _lanceCharge.Set(charging, 0f, fraction);
    }

    /// <summary>
    /// Drains any scheduled sounds that have come due and services the rotor hum.
    /// Call once per frame from the main loop. The death cascade has to be spread
    /// over time and the hum stream needs refilling every frame; everything else in
    /// the bank fires the instant it is asked to and needs no clock.
    /// </summary>
    public static void Update()
    {
        if (!_enabled) return;

        double now = Raylib.GetTime();
        for (int i = 0; i < BossBoomCount; i++)
        {
            if (!_boomPending[i] || now < _boomDue[i]) continue;
            _boomPending[i] = false;
            Raylib.SetSoundPitch(_bossBooms[i], _boomPitch[i]);
            Raylib.SetSoundVolume(_bossBooms[i], _boomVol[i]);
            Raylib.PlaySound(_bossBooms[i]);
        }

        float dt = Raylib.GetFrameTime();
        _hum.Service(dt);
        _mawHover.Service(dt);
        _lanceCharge.Service(dt);
        _reelJet.Service(dt);
        _wind.Service(dt);
        _cableStrain.Service(dt);

        // The lance bed is fed-or-it-dies, unlike the two monster beds. Those are
        // driven from the world's own step, which runs whenever their owner exists;
        // this one is driven from the player's trigger handler, which does not run at
        // all while the game is paused, while a cinematic has the craft, or after a
        // bail to the menu. Clearing the target here — after servicing, so a frame that
        // did set it still counts — means any frame that stops asking for the whine
        // lets it fade out on its own, rather than leaving a charge humming behind the
        // pause panel forever.
        _lanceCharge.Set(false, 0f, 0f);
        // The soldier's two beds are driven from the same player-side code and want the
        // same treatment: a paused game, a bail to the menu or a cinematic taking the
        // rig away all simply stop asking, and both fade rather than hanging.
        _reelJet.Set(false, 0f, 0f);
        _wind.Set(false, 0f, 0f);
        _cableStrain.Set(false, 0f, 0f);
    }

    /// <summary>Moves <paramref name="v"/> toward <paramref name="target"/> by at
    /// most <paramref name="step"/>, without overshooting.</summary>
    private static float Approach(float v, float target, float step)
    {
        if (v < target) return MathF.Min(target, v + step);
        return MathF.Max(target, v - step);
    }

    // One stomp voice per leg. Aliases again: the rig walks as a tripod, so three
    // feet land on the same tick, and a bare Sound would only be able to voice one
    // of them — the previous version had to pick the nearest foot and drop the rest.
    private static readonly Sound[] _stompVoices = new Sound[CrabRig.Legs.Length];

    /// <summary>
    /// One leg of the Crab-Core planting on the grid. Every foot is voiced
    /// separately, so a landing tripod is three overlapping impacts rather than one
    /// thud — that density is most of what makes the gait sound like a six-legged
    /// machine. <paramref name="leg"/> indexes <see cref="CrabRig.Legs"/> and fixes
    /// the limb's pitch, giving each joint a consistent voice; the servo whine
    /// layered on top is synthesised per step so no two are quite the same.
    ///
    /// <paramref name="distance"/> is the gap from that foot to the player, so the
    /// gait swells as the crab closes and stays a faint tremor while it is still
    /// stalking the far side of the arena.
    /// </summary>
    public static void PlayFootstep(int leg, float distance)
    {
        if (!_enabled) return;
        float vol = 1f - distance / StompRange;
        if (vol <= 0f) return;                  // too far off to hear at all

        // Each limb keeps its own pitch — heavier at the back, tighter at the front,
        // with a touch of jitter so repeated steps don't sound sampled.
        int i = Math.Clamp(leg, 0, _stompVoices.Length - 1);
        float pitch = 0.82f + i * 0.06f + (float)(_sfxRng.NextDouble() - 0.5) * 0.05f;
        Raylib.SetSoundPitch(_stompVoices[i], pitch);
        // Scaled down because these stack: the rig walks as a tripod, so three of
        // these land on the very same tick. At full level they would sum past unity
        // and clip the mixer on every single step.
        Raylib.SetSoundVolume(_stompVoices[i], vol * TripodMix);
        Raylib.PlaySound(_stompVoices[i]);

        // The actuator itself, over the top of the impact. Only on a fraction of
        // steps: on every one, six legs' worth of servo whine turns into a solid
        // mechanical drone and stops reading as individual joints.
        if (_sfxRng.NextDouble() < ServoStepChance)
            PlaySynth(SfxSynth.ServoStep(_sfxRng, i), vol * 0.75f);
    }

    /// <summary>How often a planting foot also gets its servo layer voiced.</summary>
    private const double ServoStepChance = 0.45;

    /// <summary>Per-foot level, set so a whole tripod landing at once stays inside
    /// the mixer's headroom instead of clipping. Three feet × this is still under
    /// two, which the pitch spread and short attack keep from sounding squashed.</summary>
    private const float TripodMix = 0.55f;

    /// <summary>Beyond this many world units a Crab-Core footfall is inaudible.</summary>
    public const float StompRange = 70f;

    /// <summary>
    /// The Crab-Core's threat-display lurch — a rising alarm blare fired each time
    /// it hard-slides to a new side, telegraphing the hunt before it commits.
    /// </summary>
    public static void PlayAlarm()
    {
        if (_enabled) Raylib.PlaySound(_alarm);
    }

    // --- The Maw-Core: the hanging mouth --------------------------------------
    // Every cue here is voiced through the synth pool rather than the clip bank, so
    // none of them ever plays the same twice — which matters more for this monster
    // than for anything else in the game, because several of them fire on a loop for
    // as long as it is on the field. A sampled drip repeating every half-second is a
    // fault; a drip that is a slightly different drip each time is weather.

    /// <summary>Past this many world units the mouth's cues stop being audible.</summary>
    public const float MawRange = 62f;

    /// <summary>Linear distance falloff to a quiet floor, shared by the ranged cues
    /// below so they all thin out at the same rate. Returns 0 past the range, which
    /// the callers take as "don't bother voicing it".</summary>
    private static float MawFalloff(float distance)
        => distance >= MawRange ? 0f : Math.Clamp(1f - distance / MawRange, 0f, 1f);

    /// <summary>One of the little lasers being spat at the player.</summary>
    public static void PlayMawSpit(float distance)
    {
        if (!_enabled) return;
        float vol = MawFalloff(distance);
        if (vol > 0f) PlaySynth(SfxSynth.MawSpit(_sfxRng), vol);
    }

    /// <summary>
    /// The rings of teeth grinding. Voiced on a cadence while it hunts, and far more
    /// often — with <paramref name="grinding"/> set, which lengthens and darkens it —
    /// while it is actually chewing someone. Distance is ignored in the grinding case
    /// on purpose: if you can hear that version, it is happening to you.
    /// </summary>
    public static void PlayMawTeeth(float distance, bool grinding = false)
    {
        if (!_enabled) return;
        float vol = grinding ? 1f : MawFalloff(distance);
        if (vol > 0f) PlaySynth(SfxSynth.ToothGrind(_sfxRng, grinding), vol);
    }

    /// <summary>The crystal turning in its well — the rotating-crystal layer over the
    /// hover bed, voiced on a slow cadence. <paramref name="agitation"/> winds the
    /// whole thing faster and higher as it fixes on the player.</summary>
    public static void PlayMawCrystal(float distance, float agitation)
    {
        if (!_enabled) return;
        float vol = MawFalloff(distance);
        if (vol > 0f) PlaySynth(SfxSynth.CrystalWhirr(_sfxRng, agitation), vol * 0.8f);
    }

    /// <summary>One bead of the black stuff letting go. Barely audible by design —
    /// see <see cref="SfxSynth.MawDrip"/> for why it has to stay that way.</summary>
    public static void PlayMawDrip()
    {
        if (_enabled) PlaySynth(SfxSynth.MawDrip(_sfxRng), 0.5f);
    }

    /// <summary>The mouth dropping on the player. Full volume, no falloff: it is
    /// directly overhead, which is the only situation this ever fires in.</summary>
    public static void PlayMawDive()
    {
        if (_enabled) PlaySynth(SfxSynth.MawDive(_sfxRng));
    }

    /// <summary>
    /// The throat closing and hauling the player up into it. Layered over the bank's
    /// impact clip so the swallow registers as something that happened <em>to</em> the
    /// craft, not just as a noise the world made — the same pairing the crab's claw
    /// slam uses, and for the same reason.
    /// </summary>
    public static void PlayMawSwallow()
    {
        if (!_enabled) return;
        Raylib.PlaySound(_hit);
        PlaySynth(SfxSynth.MawSwallow(_sfxRng));
    }

    /// <summary>One bite while it digests the player — fired per damage tick, so the
    /// player can count their shield going.</summary>
    public static void PlayMawDigest()
    {
        if (_enabled) PlaySynth(SfxSynth.MawDigestBite(_sfxRng));
    }

    /// <summary>
    /// The thing being shot from the inside. <paramref name="severity"/> runs toward 1
    /// as the escape count fills, so the third shot is audibly the one that broke its
    /// hold. This is the player's only confirmation that firing into a throat is doing
    /// anything at all, so it is layered over the bank's impact clip as well.
    /// </summary>
    public static void PlayMawHurt(float severity)
    {
        if (!_enabled) return;
        Raylib.PlaySound(_hit);
        PlaySynth(SfxSynth.MawWail(_sfxRng, severity));
    }

    /// <summary>The jaw springing open and throwing the player clear.</summary>
    public static void PlayMawRelease()
    {
        if (_enabled) PlaySynth(SfxSynth.MawRelease(_sfxRng));
    }

    /// <summary>
    /// The Maw-Core coming apart. Reuses the Crab-Core's death cascade wholesale — a
    /// falling scream over scheduled, descending blasts — because they are the same
    /// machine and should fail identically. Nothing about dying is specific to which
    /// half of it survived.
    /// </summary>
    public static void PlayMawDeath() => PlayBossDeath();

    // --- The SOLDIER's rig ----------------------------------------------------
    // Audio carries the entire sense of speed on this chassis. There is no engine note
    // to ride and no chassis to hear: what the player has is a gas bottle, two steel
    // cables and the air. So the bank below is deliberately mechanical and dry —
    // pressure, metal and wind — with nothing sung or synthetic-sounding anywhere in it.

    /// <summary>
    /// The high jump: the signature whoosh, and the loudest thing this class does. A
    /// hard pressurised release with a fabric-and-air rush layered over it, tailing into
    /// wind as the player rises. Plays every single jump, at full level, because the
    /// moment of leaving the ground is the moment the class is about.
    ///
    /// <paramref name="starvation"/> is how empty the reserve is, 0..1. As it rises the
    /// burst thins toward a hiss — quieter, higher, shorter — which is how a player
    /// learns they are nearly out of gas without ever reading the gauge.
    /// </summary>
    public static void PlayGasJump(float starvation)
    {
        if (!_enabled) return;
        starvation = Math.Clamp(starvation, 0f, 1f);

        PlaySynth(SfxSynth.GasBurst(_sfxRng, starvation), 1f - 0.45f * starvation);

        // The air rush over the top of the release, cut short and opened up so it reads
        // as the body accelerating rather than as the bottle emptying. Dropped entirely
        // on a nearly-dry tank: a thin hiss with no rush behind it is exactly the sound
        // of a jump that is about to not clear anything.
        if (starvation > 0.85f) return;
        var rush = SfxSynth.ThrowWhoosh(_sfxRng);
        rush.Length *= 0.55f;
        rush.Attack = 0.06f;
        rush.Decay = 0.7f;
        PlaySynth(rush, (1f - starvation) * 0.75f);
    }

    /// <summary>A hook leaving its launcher: a compressed-air pop with the cable's
    /// whipping hiss rising behind it as the line pays out.</summary>
    public static void PlayCableFire()
    {
        if (_enabled) PlaySynth(SfxSynth.CableLaunch(_sfxRng));
    }

    /// <summary>
    /// Steel biting in. Hard, short and metallic, and the single most important cue on
    /// the chassis — it is the difference between a swing and a fall, and the player has
    /// to know which they are in without looking at the HUD.
    ///
    /// <paramref name="distance"/> is how far off the bite landed, which only thins it a
    /// little: an anchor eighty metres away is still a thing that just happened to you.
    /// </summary>
    public static void PlayAnchorBite(float distance)
    {
        if (!_enabled) return;
        float vol = Math.Clamp(1f - distance / (SoldierAudioRange * 2f), 0.45f, 1f);
        PlaySynth(SfxSynth.AnchorClank(_sfxRng), vol);
    }

    /// <summary>A cable coming home: the fast metallic zip of the line retracting and
    /// the click of the hook seating in its launcher. Also what a shot at open sky
    /// sounds like, which is the point — a miss should be audible as a miss.</summary>
    public static void PlayCableZip()
    {
        if (_enabled) PlaySynth(SfxSynth.CableZip(_sfxRng));
    }

    /// <summary>An anchor tearing out of weak material: a splintering crack, and the
    /// sound of the ground getting closer. Deliberately unlike every other cue here —
    /// nothing else in the rig's bank splinters.</summary>
    public static void PlayAnchorTear()
    {
        if (!_enabled) return;
        PlaySynth(SfxSynth.AnchorTear(_sfxRng));
        PlaySynth(SfxSynth.CableZip(_sfxRng), 0.6f);
    }

    /// <summary>A rifle round: a sharp dry crack with none of the cannon's body. The
    /// cadence is 600 a minute, so anything with weight to it stacks into a roar within
    /// half a second.</summary>
    public static void PlayRifleShot()
    {
        if (_enabled) PlaySynth(SfxSynth.RifleCrack(_sfxRng));
    }

    /// <summary>A rocket leaving the tube: the motor lighting, then tearing away.</summary>
    public static void PlayRocketLaunch()
    {
        if (_enabled) PlaySynth(SfxSynth.RocketLaunch(_sfxRng));
    }

    /// <summary>
    /// A rocket going off. The bank's own blast clip carries the body — it is the
    /// heaviest thing in the bank and this is the heaviest thing the class does — under
    /// a bass-first synthesised concussion, both falling away with range.
    /// </summary>
    public static void PlayRocketBlast(float distance)
    {
        if (!_enabled) return;
        float vol = Math.Clamp(1f - distance / 160f, 0.15f, 1f);
        Raylib.SetSoundVolume(_explosion, vol);
        Raylib.PlaySound(_explosion);
        PlaySynth(SfxSynth.LandThud(_sfxRng), vol * 0.9f);
    }

    /// <summary>Past this the rig's own cues thin out. Generous: these are things
    /// happening to the player, not to something across the arena.</summary>
    private const float SoldierAudioRange = 90f;

    /// <summary>The gas jet under a reel. Ranged at 1 unit and fed a distance of 0 like
    /// the lance charge — it is on the player's own hips, so distance is not an axis it
    /// varies on. Its rate rides the reserve's pressure, which is what makes a starved
    /// reel audibly sag rather than merely pull less hard.</summary>
    private static readonly Bed _reelJet = new(wokenPitch: 1.9f, range: 1f, maxVolume: 0.45f);

    /// <summary>The air. Light rustle building to a roaring buffet — the single cue
    /// carrying most of the sense of speed, so it is the loudest bed in the game.</summary>
    private static readonly Bed _wind = new(wokenPitch: 2.2f, range: 1f, maxVolume: 0.55f);

    /// <summary>Steel under load: a low creak with a fine vibration hum in it. Quiet by
    /// design — it should be felt through the other two rather than heard over them, the
    /// way you notice a rope you are hanging from without listening to it.</summary>
    private static readonly Bed _cableStrain = new(wokenPitch: 1.6f, range: 1f, maxVolume: 0.3f);

    /// <summary>
    /// The cables taking weight. <paramref name="loaded"/> is whether either is actually
    /// taut and <paramref name="tension"/> (0..1) is how hard the harder-working of the
    /// two is pulling — the creak tightens with it, so a player at the bottom of a fast
    /// arc can hear how much the rig is being asked for.
    /// </summary>
    public static void SetCableStrain(bool loaded, float tension)
    {
        if (_enabled) _cableStrain.Set(loaded, 0f, tension);
    }

    /// <summary>
    /// The reserve running dry: a small warning tick, rate-limited here rather than by
    /// the caller so it can safely be asked for every tick the gauge is low. Deliberately
    /// tiny — the sag in the reel and the thinning of the jump are what actually tell the
    /// player, and this only confirms it.
    /// </summary>
    public static void PlayGasLow()
    {
        if (!_enabled) return;

        double now = Raylib.GetTime();
        if (now < _nextGasTick) return;
        _nextGasTick = now + GasTickGap;

        PlaySynth(SfxSynth.WarningBeep(_sfxRng, 0), 0.35f);
    }

    private static double _nextGasTick;
    private const double GasTickGap = 1.1;

    /// <summary>
    /// The reel's gas jet for this frame. <paramref name="reeling"/> is whether the
    /// player is actually pulling on a cable and <paramref name="pressure"/> is how much
    /// is left in the bottle, 0..1 — the roar climbs with it, so a dry reel is heard as
    /// a weak one before it is felt as one. Fed every tick; stopping feeding it fades it.
    /// </summary>
    public static void SetReel(bool reeling, float pressure)
    {
        if (_enabled) _reelJet.Set(reeling, 0f, pressure);
    }

    /// <summary>
    /// The wind past the ears. <paramref name="fast"/> gates it on at all and
    /// <paramref name="intensity"/> (0..1) drives it from a light rustle to a roaring
    /// buffet. Same contract as the reel: fed every tick, fades on its own.
    /// </summary>
    public static void SetWind(bool fast, float intensity)
    {
        if (_enabled) _wind.Set(fast, 0f, intensity);
    }

    // --- The FISH -------------------------------------------------------------
    // Built entirely out of the bank that already exists, and deliberately so. The
    // soldier's cues are dry and mechanical — pressure, steel, air — because that chassis
    // is a person wearing equipment. This one is a body in water, so the same voices are
    // reused with their envelopes opened up and their attacks softened: nothing here
    // should click or clank, and everything should sound like it is displacing something.

    /// <summary>
    /// One beat of the tail. The gas burst is the right transient for it — a shove of
    /// mass through a fluid — with the attack rounded off and the tail extended, which is
    /// the whole difference between a valve opening and a body pushing water.
    ///
    /// <paramref name="starvation"/> thins it as the reserve empties, exactly as it does
    /// for the soldier's jump, so a player learns their breath is going without ever
    /// reading the gauge. <paramref name="beached"/> swaps it for the flat, dry slap of
    /// something out of its element — the one cue on this chassis that is <em>meant</em>
    /// to sound wrong.
    /// </summary>
    public static void PlayTailBeat(float starvation, bool beached)
    {
        if (!_enabled) return;
        starvation = Math.Clamp(starvation, 0f, 1f);

        if (beached)
        {
            // Flopping: no rush behind it, just the slap and the grit. A beat that moves
            // nothing should sound like a beat that moved nothing.
            var slap = SfxSynth.LandThud(_sfxRng);
            slap.Length *= 0.4f;
            PlaySynth(slap, 0.55f);
            return;
        }

        var push = SfxSynth.GasBurst(_sfxRng, starvation);
        push.Attack = 0.12f;    // no valve click — water doesn't start instantly
        push.Decay = 0.75f;
        PlaySynth(push, (1f - 0.4f * starvation) * 0.7f);

        // The wash behind the stroke. Dropped on a nearly-dry reserve, where a beat is
        // barely displacing anything and should sound like it.
        if (starvation > 0.8f) return;
        var wash = SfxSynth.ThrowWhoosh(_sfxRng);
        wash.Length *= 0.7f;
        wash.Attack = 0.15f;
        wash.Decay = 0.8f;
        PlaySynth(wash, (1f - starvation) * 0.5f);
    }

    /// <summary>The body gathering before a strike: a short indrawn hiss, the one moment
    /// on this chassis where everything else goes quiet.</summary>
    public static void PlayFishCoil()
    {
        if (!_enabled) return;
        var draw = SfxSynth.ThrowWhoosh(_sfxRng);
        draw.Length *= 0.5f;
        draw.Attack = 0.4f;     // swelling in rather than hitting — an intake, not a blow
        draw.Decay = 0.25f;
        PlaySynth(draw, 0.5f);
    }

    /// <summary>
    /// The lunge. The loudest thing the class does and the only cue in this game that is
    /// a piece of music rather than a noise — a crushed, detuned power chord over its own
    /// octave, straight off a Mega Drive. See <see cref="SfxSynth.StrikeRiff"/> for why
    /// that exception is worth making here and nowhere else.
    ///
    /// Layered guitar-over-bass, then a thin wash of water on top so the moment still
    /// belongs to a body moving through a fluid and not to a jukebox. The wash goes last
    /// and quietest: if the synth pool is saturated it is the one that should be dropped,
    /// because the riff is the cue and the water is the garnish.
    /// </summary>
    public static void PlayFishStrike()
    {
        if (!_enabled) return;

        PlaySynth(SfxSynth.StrikeRiff(_sfxRng, bass: true), 0.85f);
        PlaySynth(SfxSynth.StrikeRiff(_sfxRng), 1f);

        var wash = SfxSynth.ThrowWhoosh(_sfxRng);
        wash.Length *= 0.6f;
        wash.Attack = 0.02f;
        PlaySynth(wash, 0.4f);
    }

    /// <summary>A strike connecting. Heavy and blunt — this is a body at fifty metres a
    /// second arriving somewhere, and it should sound like mass rather than like a
    /// weapon.</summary>
    public static void PlayFishImpact()
    {
        if (!_enabled) return;
        PlaySynth(SfxSynth.LandThud(_sfxRng), 0.95f);
        PlaySynth(SfxSynth.ClawSlam(_sfxRng), 0.5f);
    }

    /// <summary>The spit: a small wet pop with none of the rifle's dry crack. Fired at
    /// seven a second, so anything with weight to it would stack into a drone.</summary>
    public static void PlayFishSpit()
    {
        if (!_enabled) return;
        var pop = SfxSynth.MawSpit(_sfxRng);
        pop.Length *= 0.55f;
        PlaySynth(pop, 0.55f);
    }

    /// <summary>
    /// Approaching the bloom: a small, dry warning tick. Rate-limited here rather than by
    /// the caller so it is safe to ask for on every tick the body is in the warned band.
    /// Deliberately quiet and deliberately not an alarm — this is the free notice, and
    /// making it frightening would waste the alarm that comes after it.
    /// </summary>
    public static void PlayBloomWarning()
    {
        if (!_enabled) return;

        double now = Raylib.GetTime();
        if (now < _nextBloomTick) return;
        _nextBloomTick = now + BloomTickGap;

        PlaySynth(SfxSynth.WarningBeep(_sfxRng, 1), 0.4f);
    }

    /// <summary>
    /// Actually in it. The stock alarm clip — the same one the Crab-Core's threat display
    /// lurches to, and the most alarming sound in the bank — over a rising warning tone.
    /// Rate-limited on its own, slower clock, so it tolls rather than screams.
    /// </summary>
    public static void PlayBloomAlarm()
    {
        if (!_enabled) return;

        double now = Raylib.GetTime();
        if (now < _nextBloomAlarm) return;
        _nextBloomAlarm = now + BloomAlarmGap;

        PlaySynth(SfxSynth.WarningBeep(_sfxRng, 3), 0.8f);
        Raylib.SetSoundVolume(_alarm, 0.5f);
        Raylib.PlaySound(_alarm);
    }

    private static double _nextBloomTick;
    private static double _nextBloomAlarm;
    private const double BloomTickGap = 0.85;
    private const double BloomAlarmGap = 1.6;

    /// <summary>
    /// Meeting the seabed. <paramref name="force"/> (0..1) is how hard, and it scales the
    /// whole thing from a settling scrape to the full-weight thud of a body driven into
    /// the grid — plus, past halfway, the wail underneath it, which is the one place this
    /// chassis is allowed to sound like it is in distress.
    /// </summary>
    public static void PlayFishBeach(float force)
    {
        if (!_enabled) return;
        force = Math.Clamp(force, 0f, 1f);

        PlaySynth(SfxSynth.LandThud(_sfxRng), 0.4f + 0.6f * force);
        if (force > 0.5f) PlaySynth(SfxSynth.MawWail(_sfxRng, force), force * 0.55f);
    }

    /// <summary>
    /// A pickup being absorbed — a battery cell or stray round. Reuses the menu
    /// blip (the only bright, non-combat transient in the bank) so the collect
    /// reads as a clean positive chirp against the grim combat clips, no new asset.
    /// </summary>
    public static void PlayPickup()
    {
        if (_enabled) Raylib.PlaySound(_blip);
    }

    /// <summary>
    /// The refusal a full magazine gives when you right-click more rounds into it: a
    /// short low buzz that sags in pitch (<see cref="SfxSynth.FullBuzz"/>). The
    /// deliberate opposite of <see cref="PlayPickup"/>, so a rejected load never
    /// reads as a successful one.
    /// </summary>
    public static void PlayFull()
    {
        if (_enabled) PlaySynth(SfxSynth.FullBuzz(_sfxRng));
    }

    /// <summary>Unloads the clips and closes the device. Mirrors <see cref="Init"/>.</summary>
    public static void Shutdown()
    {
        if (!_enabled) return;
        _hum.Unload();
        _mawHover.Unload();
        _lanceCharge.Unload();
        _reelJet.Unload();
        _wind.Unload();
        _cableStrain.Unload();
        // Aliases first: they borrow their source clips' samples, so they must all
        // be released before the clips that own those samples go.
        for (int i = 0; i < BossBoomCount; i++) Raylib.UnloadSoundAlias(_bossBooms[i]);
        for (int i = 0; i < _stompVoices.Length; i++) Raylib.UnloadSoundAlias(_stompVoices[i]);
        for (int i = 0; i < CoreHitVoices; i++) Raylib.UnloadSoundAlias(_coreHits[i]);
        for (int i = 0; i < BeamWarnVoices; i++) Raylib.UnloadSoundAlias(_beamWarns[i]);
        Raylib.UnloadSound(_blip);
        Raylib.UnloadSound(_detonation);
        Raylib.UnloadSound(_explosion);
        Raylib.UnloadSound(_distantBoom);
        Raylib.UnloadSound(_hit);
        Raylib.UnloadSound(_warning);
        Raylib.UnloadSound(_stomp);
        Raylib.UnloadSound(_alarm);
        // Plus whatever synthesised clips are still held in the ring.
        for (int i = 0; i < SynthPoolSize; i++)
            if (_synthLoaded[i]) { Raylib.UnloadSound(_synthPool[i]); _synthLoaded[i] = false; }
        Raylib.CloseAudioDevice();
        _enabled = false;
    }

    private static Sound Load(string file) => Raylib.LoadSound(SfxDir + file);
}
