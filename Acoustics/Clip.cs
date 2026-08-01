namespace VoidTanks.Acoustics;

/// <summary>
/// One decoded sound, ready to be mixed: mono 32-bit float at the engine's rate.
///
/// Everything is mono on purpose. A stereo clip has already decided where it sits in the
/// field, and the whole point of this engine is that the <em>world</em> decides that — a
/// rifle shot has to be able to come from behind your left shoulder, which a pre-panned
/// pair of channels can never do. Anything stereo on disk is folded down at load.
///
/// Clips are immutable once built, so any number of voices can read the same one at once
/// from the mixer thread without a lock between them.
/// </summary>
public sealed class Clip
{
    /// <summary>The samples, mono, nominally in -1..1 (nothing enforces it — the master
    /// limiter is what actually keeps the mix in range).</summary>
    public readonly float[] Samples;

    /// <summary>The rate the samples were rendered or resampled to. Always
    /// <see cref="Mixer.SampleRate"/> for anything the engine plays; kept as a field so a
    /// clip built by a test at some other rate is still self-describing.</summary>
    public readonly int SampleRate;

    /// <summary>A name for diagnostics and the debug overlay. Never used to look anything
    /// up — clips are passed by reference.</summary>
    public readonly string Name;

    public Clip(float[] samples, int sampleRate, string name = "")
    {
        Samples = samples;
        SampleRate = sampleRate <= 0 ? Mixer.SampleRate : sampleRate;
        Name = name;
    }

    /// <summary>How long the clip runs, in seconds.</summary>
    public float Length => Samples.Length / (float)SampleRate;

    /// <summary>The loudest sample in the clip, for gain staging and for the tests that
    /// assert a rendered scene actually made a noise.</summary>
    public float Peak
    {
        get
        {
            float peak = 0f;
            foreach (float s in Samples) { float a = MathF.Abs(s); if (a > peak) peak = a; }
            return peak;
        }
    }

    /// <summary>
    /// Rebuilds this clip at another rate by linear interpolation. Only ever used at load,
    /// on the game thread — a 48kHz asset is resampled once and then costs nothing.
    /// Linear is enough here: the alternative (windowed sinc) buys a few dB of image
    /// rejection on material that is about to be low-passed by distance anyway.
    /// </summary>
    public Clip Resampled(int rate)
    {
        if (rate == SampleRate || Samples.Length == 0) return this;

        double ratio = SampleRate / (double)rate;
        int outLen = Math.Max(1, (int)(Samples.Length / ratio));
        var outp = new float[outLen];
        for (int i = 0; i < outLen; i++)
        {
            double src = i * ratio;
            int i0 = (int)src;
            int i1 = Math.Min(i0 + 1, Samples.Length - 1);
            float f = (float)(src - i0);
            outp[i] = Samples[i0] * (1f - f) + Samples[i1] * f;
        }
        return new Clip(outp, rate, Name);
    }

    /// <summary>
    /// Folds an interleaved multi-channel buffer down to mono. Averaging rather than
    /// summing, so a stereo asset does not arrive 6dB hotter than a mono one.
    /// </summary>
    public static float[] Downmix(float[] interleaved, int channels)
    {
        if (channels <= 1) return interleaved;
        int frames = interleaved.Length / channels;
        var mono = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++) sum += interleaved[i * channels + c];
            mono[i] = sum / channels;
        }
        return mono;
    }
}
