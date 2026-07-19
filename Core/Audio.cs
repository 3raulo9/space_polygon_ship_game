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

        // The rotor bed, synthesised once per session so each run's crab hums at a
        // slightly different pitch and throb. Rolled here rather than per boss: it
        // is a streaming loop, not a one-shot, and rebuilding it mid-fight would
        // mean tearing down a stream that is currently audible.
        _hum = Raylib.LoadMusicStreamFromMemory(".wav", SfxSynth.RenderWav(SfxSynth.Hum(_sfxRng)));
        _hum.Looping = true;
        _humReady = true;

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

    // --- Synthesised cues: built from nothing, fresh on every trigger ---------
    // These load no asset. SfxSynth rolls a new recipe each time and renders it to
    // .wav bytes in memory, so the sounds below never repeat themselves.

    /// <summary>How many generated clips stay alive at once. A clip is only unloaded
    /// once this many newer ones have been played after it, which is far longer than
    /// any of them lasts — so nothing is ever freed mid-playback. Shared by every
    /// synthesised cue, which also lets them overlap each other freely (a file-backed
    /// Sound can't overlap itself; these are all distinct objects).</summary>
    private const int SynthPoolSize = 16;

    private static readonly Sound[] _synthPool = new Sound[SynthPoolSize];
    private static readonly bool[] _synthLoaded = new bool[SynthPoolSize];
    private static int _synthSlot;

    private static readonly Random _sfxRng = new();

    /// <summary>
    /// Renders a recipe, hands it to the audio device and plays it, retiring
    /// whatever clip occupied this ring slot <see cref="SynthPoolSize"/> plays ago.
    /// </summary>
    private static void PlaySynth(SfxSynth.Params p, float volume = 1f)
    {
        // Raylib copies the samples into the Sound's own buffer, so the staging
        // Wave can go straight back after the handoff.
        Wave wave = Raylib.LoadWaveFromMemory(".wav", SfxSynth.RenderWav(p));
        Sound sound = Raylib.LoadSoundFromWave(wave);
        Raylib.UnloadWave(wave);

        if (_synthLoaded[_synthSlot]) Raylib.UnloadSound(_synthPool[_synthSlot]);
        _synthPool[_synthSlot] = sound;
        _synthLoaded[_synthSlot] = true;
        _synthSlot = (_synthSlot + 1) % SynthPoolSize;

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

    // --- The Crab-Core's rotor: a continuous hum that spools up ---------------

    // A looping Music stream rather than a Sound. A Sound is a one-shot: to hold a
    // continuous bed you would have to re-trigger it as it ended, and the frame of
    // slack between "finished" and "restarted" is an audible hole once per lap. A
    // Music stream loops in the mixer itself, seamlessly.
    private static Music _hum;
    private static bool _humReady;      // stream exists
    private static bool _humRunning;    // ...and is currently voiced

    private static float _humPitch = HumIdlePitch;
    private static float _humPitchTarget = HumIdlePitch;
    private static float _humVolume;
    private static float _humVolumeTarget;

    /// <summary>Playback rate of the rotor at rest, and wound fully up. Driving the
    /// rate is what makes it a faster <em>spin</em> rather than just a higher note —
    /// every part of the loop, throb included, speeds up together.</summary>
    private const float HumIdlePitch = 1f;
    private const float HumWokenPitch = 1.95f;

    /// <summary>Past this many world units the rotor can't be heard.</summary>
    public const float HumRange = 55f;

    // --- The core taking a hit -----------------------------------------------

    // Round-robin voices for the core ping, so rapid fire overlaps instead of
    // cutting itself off mid-ring.
    private const int CoreHitVoices = 3;
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

    /// <summary>How fast the rotor spools between rates, in units per second. Slow
    /// enough to hear it wind up — an instant jump would sound like a cut, not a
    /// machine coming to life.</summary>
    private const float HumSpoolRate = 0.9f;
    private const float HumFadeRate = 1.6f;

    /// <summary>
    /// Voices the boss's internal rotor for this frame. <paramref name="present"/>
    /// is false whenever there is no live boss, which fades the hum out and stops
    /// it; <paramref name="distance"/> is the world gap to the player, and
    /// <paramref name="agitation"/> is 0 while it idles and 1 once it has noticed
    /// the player — the rotor spools up toward <see cref="HumWokenPitch"/> as that
    /// rises, so the machine audibly winds up the moment it wakes.
    ///
    /// Both rate and level are eased toward their targets rather than set outright,
    /// so this is safe to call every tick with whatever the boss's current state is.
    /// </summary>
    public static void SetBossHum(bool present, float distance, float agitation)
    {
        if (!_enabled || !_humReady) return;

        _humPitchTarget = HumIdlePitch + (HumWokenPitch - HumIdlePitch) * Math.Clamp(agitation, 0f, 1f);
        _humVolumeTarget = present && distance < HumRange
            ? HumMaxVolume * (1f - distance / HumRange)
            : 0f;
    }

    /// <summary>Level of the rotor with the player standing on top of it. Kept well
    /// under the one-shot cues — it is a bed that sits under everything else, and
    /// it is running constantly, so it should be felt rather than listened to.</summary>
    private const float HumMaxVolume = 0.42f;

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

        if (!_humReady) return;
        float dt = Raylib.GetFrameTime();

        _humPitch = Approach(_humPitch, _humPitchTarget, HumSpoolRate * dt);
        _humVolume = Approach(_humVolume, _humVolumeTarget, HumFadeRate * dt);

        // Start the stream on the first frame it is wanted and stop it once it has
        // faded out, so a dormant arena costs nothing and no rotor is left running
        // after its crab is gone.
        if (_humVolume > 0.001f && !_humRunning)
        {
            Raylib.PlayMusicStream(_hum);
            _humRunning = true;
        }
        else if (_humVolume <= 0.001f && _humRunning)
        {
            Raylib.StopMusicStream(_hum);
            _humRunning = false;
        }

        if (!_humRunning) return;
        Raylib.SetMusicPitch(_hum, _humPitch);
        Raylib.SetMusicVolume(_hum, _humVolume);
        Raylib.UpdateMusicStream(_hum);     // refills the stream's buffers
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

    /// <summary>
    /// A pickup being absorbed — a battery cell or stray round. Reuses the menu
    /// blip (the only bright, non-combat transient in the bank) so the collect
    /// reads as a clean positive chirp against the grim combat clips, no new asset.
    /// </summary>
    public static void PlayPickup()
    {
        if (_enabled) Raylib.PlaySound(_blip);
    }

    /// <summary>Unloads the clips and closes the device. Mirrors <see cref="Init"/>.</summary>
    public static void Shutdown()
    {
        if (!_enabled) return;
        if (_humReady)
        {
            if (_humRunning) { Raylib.StopMusicStream(_hum); _humRunning = false; }
            Raylib.UnloadMusicStream(_hum);
            _humReady = false;
        }
        // Aliases first: they borrow their source clips' samples, so they must all
        // be released before the clips that own those samples go.
        for (int i = 0; i < BossBoomCount; i++) Raylib.UnloadSoundAlias(_bossBooms[i]);
        for (int i = 0; i < _stompVoices.Length; i++) Raylib.UnloadSoundAlias(_stompVoices[i]);
        for (int i = 0; i < CoreHitVoices; i++) Raylib.UnloadSoundAlias(_coreHits[i]);
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
