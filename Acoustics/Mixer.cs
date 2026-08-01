using System.Numerics;

namespace VoidTanks.Acoustics;

/// <summary>
/// The mix. Everything below this line is arithmetic on float arrays — no Raylib, no
/// world, no files — which is why the whole model can be rendered offline and asserted by
/// a headless test.
///
/// <para>Signal flow, per block:</para>
/// <code>
///   voices ──► per-bus dry L/R ──┐
///        └──► reverb send ──► room ──► wet L/R ──┤
///                                               ├──► master filter ──► tinnitus
///                                               │        ──► master gain ──► limiter ──► out
/// </code>
///
/// The master filter and gain are where "you have just been blown up" and "you are inside
/// the Maw" live: both are the same statement — the world gets quieter and duller and the
/// room gets bigger — so both are one code path with different numbers.
/// </summary>
public sealed class Mixer
{
    public const int SampleRate = 44100;
    public const int Channels = 2;

    /// <summary>The largest block the mixer will ever be handed in one call. Raylib asks
    /// for the stream's sub-buffer, which is far smaller; the scratch is generous so an
    /// offline render can push whatever it likes through in one go.</summary>
    public const int MaxBlock = 8192;

    private const int BusCount = 4;

    /// <summary>Gain put back after the softened ceiling. Just under the ratio between the
    /// two ceilings, so the quiet end lifts without the average arriving louder than the
    /// normal mix — softening should change the range, not the volume.</summary>
    private const float SoftMakeup = 2.4f;

    private readonly Voice[] _voices;
    private readonly float[][] _busL = new float[BusCount][];
    private readonly float[][] _busR = new float[BusCount][];
    private readonly float[] _wet = new float[MaxBlock];

    private readonly Reverb _reverb = new();
    private readonly Limiter _limiter = new();
    private OnePole _masterLpL, _masterLpR;
    private double _ringPhase;

    /// <summary>Per-bus gain, 0..1+. Set from the settings screen and by the ducker.</summary>
    public readonly float[] BusGain = new float[BusCount];

    /// <summary>Where every listener-relative number is measured from. Written by the game
    /// thread each frame.</summary>
    public Listener Ear;

    /// <summary>The world's wrap size, so a sound just over the seam is heard beside you
    /// rather than four hundred units away. Zero for a flat world.</summary>
    public float WorldWrap;

    // --- The state of the room and of your ears ------------------------------------

    /// <summary>How much of the room is mixed back in. Driven continuously from what is
    /// standing around the listener.</summary>
    public float WetLevel = 0.25f, WetTarget = 0.25f;

    /// <summary>Master level, before the limiter. The ducker and the concussion system
    /// pull this down; it eases back rather than snapping.</summary>
    public float MasterGain = 1f, MasterTarget = 1f;

    /// <summary>The corner the whole mix is put through. Wide open normally; slammed shut
    /// by a blast going off in your face, by being swallowed, by hyperspace.</summary>
    public float MasterCutoff = Spatializer.NearCutoff, MasterCutoffTarget = Spatializer.NearCutoff;

    /// <summary>Level of the ringing tone left behind by a concussion, and its frequency.
    /// A pure sine is exactly right here — it is what real tinnitus is, and it cuts through
    /// a muffled mix the way nothing else does.</summary>
    public float RingLevel, RingTarget;
    public float RingHz = 4200f;

    /// <summary>How fast the eased master parameters chase their targets, per second. Fast
    /// enough that a blast lands on the same frame it happens, slow enough that coming back
    /// out of it is a recovery rather than a switch.</summary>
    public float EaseRate = 6f;

    /// <summary>Folds the two channels together at the very end. An accessibility option:
    /// this engine puts things hard left and hard right, and on one speaker — or with one
    /// working ear — half of that is simply lost.</summary>
    public bool MonoDownmix;

    /// <summary>
    /// Squashes the loud end toward the quiet end. Everything the engine gained in dynamics
    /// — a boss dying genuinely dwarfing a footstep — is exactly what somebody playing late
    /// at night does not want, so the limiter's ceiling is dropped and the loss made up
    /// afterwards. The quiet things come up; the loud things do not go any further.
    /// </summary>
    public bool SoftenLoud
    {
        get => _softenLoud;
        set
        {
            _softenLoud = value;
            _limiter.Ceiling = value ? 0.34f : 0.95f;
        }
    }
    private bool _softenLoud;

    /// <summary>Rendered blocks, and how many of them ran with nothing to do. Diagnostics
    /// for the debug overlay.</summary>
    public long BlocksRendered { get; private set; }

    /// <summary>Peak of the last block, for the overlay's meter.</summary>
    public float LastPeak { get; private set; }

    /// <summary>How much the limiter is holding back right now.</summary>
    public float LimiterReduction => _limiter.Reduction;

    public Mixer(int voiceCount = 48)
    {
        _voices = new Voice[Math.Max(4, voiceCount)];
        for (int i = 0; i < _voices.Length; i++) _voices[i] = new Voice();
        for (int b = 0; b < BusCount; b++)
        {
            _busL[b] = new float[MaxBlock];
            _busR[b] = new float[MaxBlock];
            BusGain[b] = 1f;
        }
        _masterLpL.SetCutoff(Spatializer.NearCutoff, SampleRate);
        _masterLpR.SetCutoff(Spatializer.NearCutoff, SampleRate);
    }

    public IReadOnlyList<Voice> Voices => _voices;

    /// <summary>How many voices are sounding. The overlay's headline number, and the one a
    /// test uses to check that a cue was actually given a voice rather than culled.</summary>
    public int ActiveVoices
    {
        get { int n = 0; foreach (var v in _voices) if (v.Active) n++; return n; }
    }

    // --- Starting sounds -----------------------------------------------------------

    /// <summary>
    /// Finds a slot for a new sound and starts it. Returns a handle — index and generation
    /// packed together — or 0 if the sound was not worth a voice.
    ///
    /// Two things can refuse it. The cue's own instance cap, which is what stops twenty
    /// players' rifles becoming a single undifferentiated roar; and the pool being full, in
    /// which case the quietest voice of no greater priority is taken. A sound that cannot
    /// beat anything already sounding is simply dropped — silence is a better outcome than
    /// cutting off something the player was already listening to.
    /// </summary>
    public int Start(Clip clip, int key, in SoundSpec spec, in Placement place, Vector2 pos,
        float height, int owner, float pitch, bool loop, float occlusion)
    {
        if (clip == null || clip.Samples.Length == 0) return 0;
        if (place.GainL <= 0.0002f && place.GainR <= 0.0002f && !loop) return 0;

        int slot = ClaimSlot(key, spec, place);
        if (slot < 0) return 0;

        var v = _voices[slot];
        double step = pitch * (clip.SampleRate / (double)SampleRate);
        v.Begin(clip, key, spec, step, place, pos, height, owner, loop, occlusion);
        return Handle(slot, v.Generation);
    }

    private int ClaimSlot(int key, in SoundSpec spec, in Placement place)
    {
        int free = -1, instances = 0, quietestOfKind = -1, worst = -1;
        float quietKind = float.MaxValue, worstLevel = float.MaxValue;
        int worstPriority = int.MaxValue;

        for (int i = 0; i < _voices.Length; i++)
        {
            var v = _voices[i];
            if (!v.Active) { if (free < 0) free = i; continue; }

            if (v.Key == key && !v.Loop)
            {
                instances++;
                if (v.Level < quietKind) { quietKind = v.Level; quietestOfKind = i; }
            }

            // The candidate for eviction: lowest priority first, and within a priority the
            // quietest. Never a loop — a bed is held by a handle somebody is still driving,
            // and stealing it would leave that caller talking to a sound it does not own.
            if (v.Loop) continue;
            if (v.Priority < worstPriority || (v.Priority == worstPriority && v.Level < worstLevel))
            {
                worstPriority = v.Priority;
                worstLevel = v.Level;
                worst = i;
            }
        }

        // At the cap for this kind of sound: the new one only gets in if it is louder than
        // the quietest one already going, and then it takes that one's slot. Which is the
        // behaviour you want — a rifle shot beside you should displace one across the road,
        // never the other way round.
        if (instances >= spec.MaxInstances && quietestOfKind >= 0)
        {
            if (place.Gain <= quietKind) return -1;
            _voices[quietestOfKind].End();
            return quietestOfKind;
        }

        if (free >= 0) return free;
        if (worst < 0) return -1;
        if (worstPriority > spec.Priority) return -1;      // everything going matters more
        _voices[worst].End();
        return worst;
    }

    private static int Handle(int slot, int generation) => ((generation & 0xFFFF) << 16) | (slot + 1);

    private Voice? Resolve(int handle)
    {
        if (handle == 0) return null;
        int slot = (handle & 0xFFFF) - 1;
        if ((uint)slot >= (uint)_voices.Length) return null;
        var v = _voices[slot];
        if (!v.Active || (v.Generation & 0xFFFF) != ((handle >> 16) & 0xFFFF)) return null;
        return v;
    }

    /// <summary>Whether a handle still refers to the sound it was issued for.</summary>
    public bool IsAlive(int handle) => Resolve(handle) != null;

    /// <summary>
    /// Re-places a sound that is still going — a bed, or any voice long enough that the
    /// listener can move meaningfully while it sounds. Everything set here is a target the
    /// mixer ramps toward, so this may be called every frame without a click.
    /// </summary>
    public void Update(int handle, in Placement place, float pitch, Clip? clip = null)
    {
        var v = Resolve(handle);
        if (v == null) return;
        v.TargetL = place.GainL;
        v.TargetR = place.GainR;
        v.TargetSend = place.ReverbSend;
        v.TargetCutoff = place.Cutoff;
        if (pitch > 0f && v.Clip != null)
            v.Step = pitch * (v.Clip.SampleRate / (double)SampleRate);
    }

    /// <summary>Moves a still-sounding voice, so the game thread can re-place it next
    /// frame. Used for beds attached to something that walks.</summary>
    public void Move(int handle, Vector2 pos, float height)
    {
        var v = Resolve(handle);
        if (v == null) return;
        v.Position = pos;
        v.Height = height;
    }

    /// <summary>Asks a looping voice to fade out and free itself.</summary>
    public void Release(int handle)
    {
        var v = Resolve(handle);
        if (v == null) return;
        v.Releasing = true;
        v.TargetL = 0f;
        v.TargetR = 0f;
        v.TargetSend = 0f;
    }

    /// <summary>Stops everything at once, for a scene change. Beds included.</summary>
    public void StopAll()
    {
        foreach (var v in _voices) if (v.Active) v.End();
        _reverb.Clear();
        _limiter.Reset();
        _masterLpL.Reset();
        _masterLpR.Reset();
    }

    // --- Rendering -----------------------------------------------------------------

    /// <summary>
    /// Renders one block of interleaved stereo. The only method that runs off the game
    /// thread; everything it touches is preallocated, and it neither allocates nor throws.
    /// </summary>
    public void Render(Span<float> output, int frames)
    {
        frames = Math.Min(frames, Math.Min(MaxBlock, output.Length / Channels));
        if (frames <= 0) return;

        for (int b = 0; b < BusCount; b++)
        {
            Array.Clear(_busL[b], 0, frames);
            Array.Clear(_busR[b], 0, frames);
        }
        Array.Clear(_wet, 0, frames);

        foreach (var v in _voices)
            if (v.Active) RenderVoice(v, frames);

        RenderMaster(output, frames);
        BlocksRendered++;
    }

    private void RenderVoice(Voice v, int frames)
    {
        var clip = v.Clip;
        if (clip == null) { v.End(); return; }

        // Parameters move once per block. At 512 frames that is a step every 12ms, which
        // the ramps below smooth into something continuous.
        float inv = 1f / frames;
        float dl = (v.TargetL - v.GainL) * inv;
        float dr = (v.TargetR - v.GainR) * inv;
        float dsend = (v.TargetSend - v.Send) * inv;

        // The filter corner is eased in log space so it sweeps at a musical rate rather
        // than crawling at the top and lurching at the bottom.
        v.Cutoff = MathF.Exp(MathF.Log(MathF.Max(v.Cutoff, 20f))
                             + (MathF.Log(MathF.Max(v.TargetCutoff, 20f))
                                - MathF.Log(MathF.Max(v.Cutoff, 20f))) * 0.35f);
        v.Filter.SetCutoff(v.Cutoff, SampleRate);

        float[] bl = _busL[(int)v.Bus], br = _busR[(int)v.Bus];
        var src = clip.Samples;
        int last = src.Length - 1;
        double cursor = v.Cursor;
        double step = v.Step;
        float gl = v.GainL, gr = v.GainR, send = v.Send;

        for (int i = 0; i < frames; i++)
        {
            // Still on its way. Counted in output samples so the delay is exact.
            if (v.DelayFrames > 0) { v.DelayFrames--; continue; }

            if (cursor >= last)
            {
                if (!v.Loop) { v.End(); v.Cursor = 0; return; }
                cursor -= last;                     // seamless: the bed clips are rendered to loop
                if (cursor < 0) cursor = 0;
            }

            int i0 = (int)cursor;
            int i1 = i0 + 1 <= last ? i0 + 1 : (v.Loop ? 0 : last);
            float f = (float)(cursor - i0);
            float s = src[i0] * (1f - f) + src[i1] * f;
            cursor += step;

            s = v.Filter.Process(s);

            bl[i] += s * gl;
            br[i] += s * gr;
            _wet[i] += s * send;

            gl += dl; gr += dr; send += dsend;
        }

        v.Cursor = cursor;
        v.GainL = gl; v.GainR = gr; v.Send = send;

        // A released bed that has actually reached silence can go. Checking the ramp rather
        // than a timer means the fade is always complete, whatever rate it ran at.
        if (v.Releasing && MathF.Abs(gl) < 0.0005f && MathF.Abs(gr) < 0.0005f) v.End();
    }

    private void RenderMaster(Span<float> output, int frames)
    {
        // Master parameters ease across the block, same as a voice's.
        float dt = frames / (float)SampleRate;
        float k = 1f - MathF.Exp(-EaseRate * dt);
        WetLevel += (WetTarget - WetLevel) * k;
        MasterGain += (MasterTarget - MasterGain) * k;
        RingLevel += (RingTarget - RingLevel) * k;
        MasterCutoff = MathF.Exp(MathF.Log(MathF.Max(MasterCutoff, 20f))
            + (MathF.Log(MathF.Max(MasterCutoffTarget, 20f))
               - MathF.Log(MathF.Max(MasterCutoff, 20f))) * k);

        _masterLpL.SetCutoff(MasterCutoff, SampleRate);
        _masterLpR.SetCutoff(MasterCutoff, SampleRate);
        _reverb.Refresh();

        double ringStep = 2.0 * Math.PI * RingHz / SampleRate;
        float wetGain = WetLevel;
        float peak = 0f;

        // The UI bus deliberately bypasses the master filter and the room. A menu click has
        // no business being muffled because the player is concussed, and a click with a
        // reverb tail on it sounds like a bug.
        float[] uiL = _busL[(int)Bus.Ui], uiR = _busR[(int)Bus.Ui];
        float uiGain = BusGain[(int)Bus.Ui];

        for (int i = 0; i < frames; i++)
        {
            float l = 0f, r = 0f;
            for (int b = 0; b < BusCount; b++)
            {
                if (b == (int)Bus.Ui) continue;
                float g = BusGain[b];
                l += _busL[b][i] * g;
                r += _busR[b][i] * g;
            }

            _reverb.Process(_wet[i], out float wl, out float wr);
            l += wl * wetGain;
            r += wr * wetGain;

            l = _masterLpL.Process(l);
            r = _masterLpR.Process(r);

            if (RingLevel > 0.0005f)
            {
                float ring = (float)Math.Sin(_ringPhase) * RingLevel;
                _ringPhase += ringStep;
                if (_ringPhase > Math.PI * 2.0) _ringPhase -= Math.PI * 2.0;
                l += ring;
                r += ring;
            }

            l *= MasterGain;
            r *= MasterGain;

            l += uiL[i] * uiGain;
            r += uiR[i] * uiGain;

            _limiter.Process(ref l, ref r);

            // Make up what the lowered ceiling took, so softening is a reduction in range
            // rather than in volume. Applied after the limiter on purpose — before it, the
            // makeup would simply be limited straight back off again.
            if (_softenLoud)
            {
                l *= SoftMakeup;
                r *= SoftMakeup;
                if (l > 1f) l = 1f; else if (l < -1f) l = -1f;
                if (r > 1f) r = 1f; else if (r < -1f) r = -1f;
            }

            if (MonoDownmix)
            {
                float m = (l + r) * 0.5f;
                l = m;
                r = m;
            }

            output[i * 2] = l;
            output[i * 2 + 1] = r;

            float a = MathF.Max(MathF.Abs(l), MathF.Abs(r));
            if (a > peak) peak = a;
        }

        LastPeak = peak;
    }

    /// <summary>Room controls, set from <see cref="Ambience"/> on the game thread.</summary>
    public void SetRoom(float size, float damping)
    {
        _reverb.Size = Math.Clamp(size, 0f, 1f);
        _reverb.Damping = Math.Clamp(damping, 0f, 0.95f);
    }
}
