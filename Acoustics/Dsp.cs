namespace Unrendered.Acoustics;

/// <summary>
/// A one-pole low-pass. Cheap enough to run one per voice per channel, which is what
/// distance and walls are made of here: air eats the top of a sound as it travels, and a
/// tower between you and it eats a great deal more. Volume alone never reads as "far away";
/// dullness does.
/// </summary>
public struct OnePole
{
    private float _z;      // the one sample of state
    private float _a;      // the coefficient the cutoff maps to

    /// <summary>Sets the corner frequency. Anything at or above Nyquist opens the filter
    /// fully, so a cue with no absorption at all costs a multiply and nothing else.</summary>
    public void SetCutoff(float hz, float sampleRate)
    {
        if (hz >= sampleRate * 0.45f) { _a = 1f; return; }
        if (hz <= 1f) { _a = 0f; return; }
        // Standard RC → one-pole mapping. exp is fine: this runs once per block, not
        // once per sample.
        _a = 1f - MathF.Exp(-2f * MathF.PI * hz / sampleRate);
    }

    public float Process(float x)
    {
        _z += _a * (x - _z);
        return _a >= 1f ? x : _z;
    }

    public void Reset() => _z = 0f;
}

/// <summary>
/// A one-pole high-pass, built as "the part the low-pass threw away". Used for the tilt
/// that sells elevation — a source above you loses body and keeps its edge — and to keep
/// DC out of the master.
/// </summary>
public struct OneZeroHigh
{
    private OnePole _low;

    public void SetCutoff(float hz, float sampleRate) => _low.SetCutoff(hz, sampleRate);
    public float Process(float x) => x - _low.Process(x);
    public void Reset() => _low.Reset();
}

/// <summary>
/// One comb filter with its own damping — the building block of the reverb below. Feedback
/// sets how long the tail runs; the damping low-pass inside the loop is what stops it
/// ringing like a metal pipe, because each lap round the delay line loses a little more of
/// its top end, exactly as a real room does.
/// </summary>
public sealed class Comb
{
    private readonly float[] _line;
    private int _at;
    private float _store;

    public float Feedback = 0.84f;
    public float Damp = 0.2f;

    public Comb(int length) => _line = new float[Math.Max(1, length)];

    public float Process(float x)
    {
        float y = _line[_at];
        _store = y * (1f - Damp) + _store * Damp;
        _line[_at] = x + _store * Feedback;
        if (++_at >= _line.Length) _at = 0;
        return y;
    }

    public void Clear() { Array.Clear(_line); _store = 0f; _at = 0; }
}

/// <summary>An all-pass, which smears the combs' echoes into something that stops sounding
/// like a row of discrete repeats and starts sounding like a space.</summary>
public sealed class AllPass
{
    private readonly float[] _line;
    private int _at;
    public float Feedback = 0.5f;

    public AllPass(int length) => _line = new float[Math.Max(1, length)];

    public float Process(float x)
    {
        float buf = _line[_at];
        float y = -x + buf;
        _line[_at] = x + buf * Feedback;
        if (++_at >= _line.Length) _at = 0;
        return y;
    }

    public void Clear() { Array.Clear(_line); _at = 0; }
}

/// <summary>
/// The room. A Schroeder reverb — four damped combs in parallel into two all-passes in
/// series, per channel, with the right channel's delay lines offset by a few dozen samples
/// so the two sides decorrelate and the tail opens out instead of sitting in the middle of
/// your head.
///
/// Its parameters are not authored anywhere. <see cref="Environment"/> measures how much
/// city is standing around the listener and drives size and damping from that, so walking
/// out of the blocks onto the open grid opens the space up on its own.
/// </summary>
public sealed class Reverb
{
    // Lengths in samples at 44.1kHz. Mutually prime-ish on purpose: shared factors make
    // the combs agree with each other, and combs that agree are a resonance, not a room.
    private static readonly int[] CombLengths = { 1557, 1617, 1491, 1422 };
    private static readonly int[] AllPassLengths = { 225, 556 };
    private const int StereoSpread = 23;

    private readonly Comb[] _combL, _combR;
    private readonly AllPass[] _apL, _apR;

    /// <summary>0..1 — how long the tail runs. Drives comb feedback.</summary>
    public float Size = 0.5f;

    /// <summary>0..1 — how fast the top end dies inside the tail. A concrete street is
    /// bright and damps slowly; a soft, cluttered interior swallows the highs at once.</summary>
    public float Damping = 0.4f;

    public Reverb()
    {
        _combL = new Comb[CombLengths.Length];
        _combR = new Comb[CombLengths.Length];
        for (int i = 0; i < CombLengths.Length; i++)
        {
            _combL[i] = new Comb(CombLengths[i]);
            _combR[i] = new Comb(CombLengths[i] + StereoSpread);
        }
        _apL = new AllPass[AllPassLengths.Length];
        _apR = new AllPass[AllPassLengths.Length];
        for (int i = 0; i < AllPassLengths.Length; i++)
        {
            _apL[i] = new AllPass(AllPassLengths[i]);
            _apR[i] = new AllPass(AllPassLengths[i] + StereoSpread);
        }
    }

    /// <summary>Pushes the current <see cref="Size"/>/<see cref="Damping"/> into the combs.
    /// Called once per block rather than per sample — the parameters move at walking pace.</summary>
    public void Refresh()
    {
        float fb = 0.7f + Size * 0.28f;      // 0.70 (tight) .. 0.98 (cavernous)
        float damp = Math.Clamp(Damping, 0f, 0.95f);
        foreach (var c in _combL) { c.Feedback = fb; c.Damp = damp; }
        foreach (var c in _combR) { c.Feedback = fb; c.Damp = damp; }
    }

    /// <summary>Runs one mono send sample through the room, returning the stereo tail.</summary>
    public void Process(float x, out float l, out float r)
    {
        // A touch of input gain trim so a hot send does not run the combs into the ceiling.
        x *= 0.15f;

        float sl = 0f, sr = 0f;
        for (int i = 0; i < _combL.Length; i++) { sl += _combL[i].Process(x); sr += _combR[i].Process(x); }
        for (int i = 0; i < _apL.Length; i++) { sl = _apL[i].Process(sl); sr = _apR[i].Process(sr); }
        l = sl;
        r = sr;
    }

    public void Clear()
    {
        foreach (var c in _combL) c.Clear();
        foreach (var c in _combR) c.Clear();
        foreach (var a in _apL) a.Clear();
        foreach (var a in _apR) a.Clear();
    }
}

/// <summary>
/// The master limiter. Twenty players, six legs of a walking boss and a death cascade can
/// all land on the same sample, and the old bank simply clipped when they did. This rides
/// the gain down fast when the mix goes over and lets it back slowly, so a loud moment
/// ducks rather than tears.
/// </summary>
public sealed class Limiter
{
    private float _gain = 1f;

    /// <summary>The level above which gain is pulled back. Slightly under unity so the
    /// interleaved output has somewhere to go.</summary>
    public float Ceiling = 0.95f;

    /// <summary>Per-sample coefficients. Attack is near-instant so nothing gets through;
    /// release is ~300ms so the duck is heard as loudness, not as pumping.</summary>
    private const float Attack = 0.4f;
    private const float Release = 0.00002f;

    /// <summary>How much gain reduction is currently applied, for the debug overlay.</summary>
    public float Reduction => 1f - _gain;

    public void Process(ref float l, ref float r)
    {
        float peak = MathF.Max(MathF.Abs(l), MathF.Abs(r)) * _gain;
        if (peak > Ceiling)
        {
            float want = Ceiling / MathF.Max(peak / _gain, 1e-6f);
            _gain += (want - _gain) * Attack;
        }
        else if (_gain < 1f)
        {
            _gain += Release;
            if (_gain > 1f) _gain = 1f;
        }
        l *= _gain;
        r *= _gain;

        // Belt and braces: a transient steeper than the attack can still poke through for
        // a sample or two, and a hard clip there is far kinder than a wrapped sample.
        if (l > 1f) l = 1f; else if (l < -1f) l = -1f;
        if (r > 1f) r = 1f; else if (r < -1f) r = -1f;
    }

    public void Reset() => _gain = 1f;
}
