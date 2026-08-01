using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Unrendered.Acoustics;

/// <summary>
/// The engine's front door: the one class the game talks to.
///
/// <para>It owns a <see cref="Mixer"/>, an <see cref="Ambience"/> and a
/// <see cref="SpecTable"/>, and it owns the single raylib audio stream everything is
/// rendered into. Raylib's own <c>Sound</c> API is not used at all — it can set a volume,
/// a pitch and a pan and nothing else, and a world where a tower can stand between you and
/// a gunshot needs a filter, a room and a delay line as well.</para>
///
/// <para>Threading: raylib calls <see cref="StreamCallback"/> from its audio thread. The
/// game thread posts cues and re-places voices. A single lock covers both; the game-thread
/// side of it is a few hundred floats of arithmetic, so the audio thread is never kept
/// waiting long enough to matter.</para>
/// </summary>
public sealed class AudioEngine
{
    /// <summary>Frames per callback. ~12ms at 44.1kHz — standard game-audio latency, and
    /// enough of a block that the per-block parameter work disappears into the noise.</summary>
    public const int BufferFrames = 512;

    /// <summary>
    /// What raylib's stream buffer default is put back to once our own stream has claimed
    /// its size.
    ///
    /// <para><see cref="Raylib.SetAudioStreamBufferSizeDefault"/> is <em>global</em>, not a
    /// parameter of the next stream: it decides the sub-buffer of every audio stream loaded
    /// after it, and <c>LoadMusicStream</c> is an audio stream. Leaving it at
    /// <see cref="BufferFrames"/> handed the soundtrack a 512-frame (~12ms) sub-buffer that
    /// is refilled from <c>UpdateMusicStream</c> once per <em>rendered</em> frame — 16.7ms
    /// at 60fps. The buffer ran dry before every single refill, which is heard as a
    /// continuous tear. Ours is fine at 512 because it is filled from the audio thread's own
    /// callback and never waits on the display.</para>
    ///
    /// <para>Deliberately larger than raylib's own default (<c>sampleRate/30</c>, ~1470
    /// frames): music has no latency requirement whatsoever, and ~93ms of sub-buffer means a
    /// frame spike four times the length of a normal one still cannot starve it.</para>
    /// </summary>
    public const int MusicBufferFrames = 4096;

    private static AudioEngine? _instance;

    private readonly object _lock = new();
    private readonly Mixer _mixer;
    private readonly Ambience _env = new();
    private readonly SpecTable _specs;
    private readonly Random _rng = new();

    private AudioStream _stream;
    private bool _streamLive;

    /// <summary>Answers "how much is in the way between these two points", 0 clear to 1
    /// buried. Left null by anything that has no world — the offline renderer, a test — and
    /// then nothing is ever occluded.</summary>
    public Func<Vector2, float, Vector2, float, float>? OcclusionProbe;

    public AudioEngine(SpecTable specs, int voices = 48)
    {
        _specs = specs;
        _mixer = new Mixer(voices);
    }

    public Mixer Mix => _mixer;
    public Ambience Env => _env;
    public SpecTable Specs => _specs;

    /// <summary>True once a real audio device is behind this. False in the self-test, where
    /// every call below still runs its arithmetic and simply renders to nowhere.</summary>
    public bool Live => _streamLive;

    /// <summary>The world's wrap size, so a cue just over the seam is heard beside you.</summary>
    public float WorldWrap { get => _mixer.WorldWrap; set => _mixer.WorldWrap = value; }

    // --- Life ----------------------------------------------------------------------

    /// <summary>
    /// Opens the stream and starts it running. The audio device must already exist. Safe to
    /// skip entirely — everything else works without it, silently, which is what lets the
    /// headless self-test drive the same code.
    /// </summary>
    public unsafe void Open()
    {
        if (_streamLive) return;
        _instance = this;

        Raylib.SetAudioStreamBufferSizeDefault(BufferFrames);
        _stream = Raylib.LoadAudioStream(Mixer.SampleRate, 32, Mixer.Channels);

        // Put the global back before anything else loads a stream — see MusicBufferFrames.
        // Unconditional, and before the validity check: a failed load must not leave the
        // default holding a size only this stream could have survived.
        Raylib.SetAudioStreamBufferSizeDefault(MusicBufferFrames);

        if (!Raylib.IsAudioStreamValid(_stream)) return;

        Raylib.SetAudioStreamCallback(_stream, &StreamCallback);
        Raylib.PlayAudioStream(_stream);
        _streamLive = true;
    }

    public void Close()
    {
        if (!_streamLive) return;
        _streamLive = false;
        lock (_lock) _mixer.StopAll();
        Raylib.StopAudioStream(_stream);
        Raylib.UnloadAudioStream(_stream);
        if (ReferenceEquals(_instance, this)) _instance = null;
    }

    /// <summary>
    /// The audio thread's entry point. Kept to a lock and a call: a managed callback on a
    /// real-time thread must not allocate and must not let an exception escape into native
    /// code, so it does neither.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void StreamCallback(void* buffer, uint frames)
    {
        var engine = _instance;
        if (engine == null) return;
        try
        {
            var span = new Span<float>(buffer, (int)frames * Mixer.Channels);
            lock (engine._lock) engine._mixer.Render(span, (int)frames);
        }
        catch
        {
            // Silence is survivable; an exception crossing into miniaudio is not.
            try { new Span<float>(buffer, (int)frames * Mixer.Channels).Clear(); } catch { }
        }
    }

    // --- Clips ---------------------------------------------------------------------

    /// <summary>
    /// Decodes a file to a mono clip at the engine's rate. raylib does the decoding — the
    /// assets are a mix of 16-bit PCM, 8-bit µ-law and 48kHz stereo, and dr_wav already
    /// knows about all of it — and everything after that is ours.
    /// </summary>
    public static unsafe Clip? LoadFile(string path)
    {
        if (!File.Exists(path)) return null;
        Wave wave = Raylib.LoadWave(path);
        if (wave.SampleCount == 0) { Raylib.UnloadWave(wave); return null; }

        float* raw = Raylib.LoadWaveSamples(wave);
        int channels = (int)wave.Channels;
        int total = (int)wave.SampleCount * channels;
        var interleaved = new float[total];
        for (int i = 0; i < total; i++) interleaved[i] = raw[i];
        Raylib.UnloadWaveSamples(raw);

        int rate = (int)wave.SampleRate;
        Raylib.UnloadWave(wave);

        var name = Path.GetFileNameWithoutExtension(path);
        return new Clip(Clip.Downmix(interleaved, channels), rate, name).Resampled(Mixer.SampleRate);
    }

    /// <summary>Wraps samples the game generated itself. The synth already renders mono
    /// float at 44.1kHz, so this costs nothing.</summary>
    public static Clip FromSamples(float[] samples, int rate = Mixer.SampleRate, string name = "")
        => new(samples, rate, name);

    // --- Playing -------------------------------------------------------------------

    /// <summary>
    /// Plays a clip at a place in the world. This is the only way a sound gets made.
    ///
    /// <paramref name="cueId"/> indexes the spec table, which decides everything about how
    /// it carries. <paramref name="gainScale"/> is for the handful of cues that layer two
    /// clips at different levels; it is not a distance volume, and nothing outside the
    /// sound bank should ever pass it.
    /// </summary>
    /// <returns>A handle, valid while the sound lasts, or 0 if it was not worth a voice.</returns>
    public int Play(int cueId, Clip? clip, Vector2 pos, float height = 0f, int owner = -1,
        float pitch = 1f, float gainScale = 1f)
    {
        if (clip == null) return 0;
        var spec = _specs[cueId];
        if (gainScale != 1f) spec = spec with { Gain = spec.Gain * gainScale };

        if (spec.PitchJitter > 0f)
            pitch *= 1f + ((float)_rng.NextDouble() * 2f - 1f) * spec.PitchJitter;

        lock (_lock)
        {
            float occ = Probe(spec, pos, height);
            var place = Spatializer.Place(_mixer.Ear, spec, pos, height, occ, _mixer.WorldWrap);
            if (place.Gain <= 0.0002f) return 0;
            return _mixer.Start(clip, cueId, spec, place, pos, height, owner, pitch, false, occ);
        }
    }

    /// <summary>A sound with no place in the world — a menu click, an alarm on your own
    /// panel. Centred, dry, untouched by distance or by the state of your ears.</summary>
    public int PlayFlat(int cueId, Clip? clip, float pitch = 1f, float gainScale = 1f)
    {
        if (clip == null) return 0;
        var spec = _specs[cueId] with { Spatial = false };
        if (gainScale != 1f) spec = spec with { Gain = spec.Gain * gainScale };
        lock (_lock)
        {
            var place = Spatializer.Place(_mixer.Ear, spec, Vector2.Zero, 0f, 0f, 0f);
            return _mixer.Start(clip, cueId, spec, place, Vector2.Zero, 0f, -1, pitch, false, 0f);
        }
    }

    /// <summary>
    /// Starts a bed: a looping voice the game holds and drives. Returns a handle to feed to
    /// <see cref="SetBed"/> and <see cref="StopBed"/>. A bed is placed in the world like
    /// anything else, so a boss's rotor is genuinely off to your left when it is.
    /// </summary>
    public int StartBed(int cueId, Clip? clip, Vector2 pos, float height = 0f, float pitch = 1f)
    {
        if (clip == null) return 0;
        var spec = _specs[cueId];
        lock (_lock)
        {
            float occ = Probe(spec, pos, height);
            var place = Spatializer.Place(_mixer.Ear, spec, pos, height, occ, _mixer.WorldWrap);
            return _mixer.Start(clip, cueId, spec, place, pos, height, -1, pitch, true, occ);
        }
    }

    /// <summary>Moves a bed and sets its rate and level. The gain here is a scale on the
    /// cue's own — how hard the thing making the noise is working — and is combined with
    /// distance rather than replacing it.</summary>
    public void SetBed(int handle, Vector2 pos, float height, float pitch, float gainScale)
    {
        if (handle == 0) return;
        lock (_lock)
        {
            _mixer.Move(handle, pos, height);
            var v = FindLocked(handle);
            if (v != null) v.Spec = _specs[v.Key] with { Gain = _specs[v.Key].Gain * gainScale };
            RePlaceLocked(handle, pitch);
        }
    }

    public void StopBed(int handle)
    {
        if (handle == 0) return;
        lock (_lock) _mixer.Release(handle);
    }

    public bool BedAlive(int handle)
    {
        lock (_lock) return _mixer.IsAlive(handle);
    }

    // --- Per-frame -----------------------------------------------------------------

    /// <summary>
    /// One frame of engine upkeep, from the game thread: where the ears are, what the room
    /// is doing, and a fresh placement for every voice still sounding.
    ///
    /// Re-placing live voices is what makes the world feel solid rather than snapshotted —
    /// drive past a burning wreck and its noise sweeps across you properly, because its
    /// voice is being told where you are sixty times a second rather than once when it
    /// started.
    /// </summary>
    public void Update(in Listener ear, float dt)
    {
        lock (_lock)
        {
            _mixer.Ear = ear;
            _env.Apply(_mixer, dt);
            RePlaceAll(dt);
        }
    }

    /// <summary>How many voices get a fresh occlusion march each frame. A ray march is the
    /// only expensive thing in here, so it is rationed and round-robined: the answer is
    /// eased anyway, and a voice that waits three frames for a new one is inaudible.</summary>
    private const int OcclusionBudget = 6;

    /// <summary>How fast a voice's occlusion chases the truth, per second. Slow enough that
    /// driving past a corner opens the sound up rather than switching it.</summary>
    private const float OcclusionEase = 7f;

    private int _occlusionCursor;

    private void RePlaceAll(float dt)
    {
        float k = 1f - MathF.Exp(-OcclusionEase * dt);
        var voices = _mixer.Voices;
        int budget = OcclusionBudget;

        for (int i = 0; i < voices.Count; i++)
        {
            var v = voices[i];
            if (!v.Active || v.Releasing) continue;

            if (v.Spatial)
            {
                // Round-robin the marching. The cursor walks the pool independently of this
                // loop so a long-lived bed cannot monopolise the budget.
                if (v.Occludes && budget > 0 && OcclusionProbe != null && Due(i))
                {
                    v.OcclusionTarget = Math.Clamp(
                        OcclusionProbe(_mixer.Ear.Position, _mixer.Ear.Height, v.Position, v.Height),
                        0f, 1f);
                    budget--;
                }
                v.Occlusion += (v.OcclusionTarget - v.Occlusion) * k;

                var place = Spatializer.Place(_mixer.Ear, v.Spec, v.Position, v.Height,
                    v.Occlusion, _mixer.WorldWrap);
                v.TargetL = place.GainL;
                v.TargetR = place.GainR;
                v.TargetSend = place.ReverbSend;
                v.TargetCutoff = place.Cutoff;
            }
        }

        _occlusionCursor++;
    }

    /// <summary>Whether this slot's turn has come round. Cheap and fair: every slot is
    /// visited once per pool-sized sweep regardless of how many are busy.</summary>
    private bool Due(int slot) => (slot + _occlusionCursor) % 4 == 0;

    private float Probe(in SoundSpec spec, Vector2 pos, float height)
    {
        if (!spec.Spatial || !spec.Occludes || OcclusionProbe == null) return 0f;
        return Math.Clamp(OcclusionProbe(_mixer.Ear.Position, _mixer.Ear.Height, pos, height), 0f, 1f);
    }

    private Voice? FindLocked(int handle)
    {
        int slot = (handle & 0xFFFF) - 1;
        var voices = _mixer.Voices;
        if ((uint)slot >= (uint)voices.Count) return null;
        var v = voices[slot];
        return v.Active && (v.Generation & 0xFFFF) == ((handle >> 16) & 0xFFFF) ? v : null;
    }

    private void RePlaceLocked(int handle, float pitch)
    {
        var v = FindLocked(handle);
        if (v == null) return;
        float occ = v.Occludes ? v.Occlusion : 0f;
        var place = Spatializer.Place(_mixer.Ear, v.Spec, v.Position, v.Height, occ, _mixer.WorldWrap);
        _mixer.Update(handle, place, pitch);
    }

    // --- Buses ---------------------------------------------------------------------

    public void SetBusGain(Bus bus, float gain)
    {
        lock (_lock) _mixer.BusGain[(int)bus] = Math.Clamp(gain, 0f, 2f);
    }

    public float GetBusGain(Bus bus)
    {
        lock (_lock) return _mixer.BusGain[(int)bus];
    }

    public void StopAll()
    {
        lock (_lock) { _mixer.StopAll(); _env.Clear(); }
    }

    // --- Offline -------------------------------------------------------------------

    /// <summary>
    /// Renders a block without a device — the hook the listening harness and the self-test
    /// both hang off. Same mixer, same code path, straight into an array.
    /// </summary>
    public void RenderOffline(Span<float> stereo, int frames)
    {
        lock (_lock) _mixer.Render(stereo, frames);
    }

    /// <summary>Reads a live voice's placement, for tests and the debug overlay. Returns
    /// false if the handle has expired.</summary>
    public bool TryReadVoice(int handle, out Placement place)
    {
        lock (_lock)
        {
            var v = FindLocked(handle);
            if (v == null) { place = Placement.Silent; return false; }
            place = new Placement(v.TargetL, v.TargetR, v.TargetCutoff, v.TargetSend, 0f,
                Spatializer.Delta(_mixer.Ear.Position, v.Position, _mixer.WorldWrap).Length());
            return true;
        }
    }

    /// <summary>Active voice count — the overlay's headline, and a test's proof that a cue
    /// was heard rather than culled.</summary>
    public int ActiveVoices { get { lock (_lock) return _mixer.ActiveVoices; } }
}
