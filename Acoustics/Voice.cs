using System.Numerics;

namespace VoidTanks.Acoustics;

/// <summary>
/// One sound currently making noise. A voice owns a read cursor into a shared
/// <see cref="Clip"/>, the two channel gains it is heading toward, and the single low-pass
/// that distance and walls act through.
///
/// Gains and cutoff are <em>targets</em>, not settings. The mixer ramps toward them across
/// each block, which is what lets the game thread recompute a voice's placement sixty times
/// a second while the audio thread runs at four thousand — without a click at every seam.
/// </summary>
public sealed class Voice
{
    // --- What it is ---------------------------------------------------------------
    public Clip? Clip;
    public int Key = -1;            // the cue id, for instance caps and diagnostics
    public Bus Bus;
    public byte Priority;
    public bool Loop;
    public bool Active;

    /// <summary>Bumped every time the slot is reused, so a handle held by the game can be
    /// checked against the voice it was issued for. Without this, a bed whose voice was
    /// stolen would go on being driven by whoever holds the stale handle — and would drive
    /// somebody else's sound instead.</summary>
    public int Generation;

    // --- Where it is --------------------------------------------------------------
    public Vector2 Position;
    public float Height;
    public bool Spatial;
    public bool Occludes;
    public int Owner = -1;          // the seat that caused it, or -1

    /// <summary>Eased toward <see cref="OcclusionTarget"/> on the game thread. The march
    /// that measures it runs for only a few voices per frame, and the answer flips hard
    /// when a corner passes between you and a source, so it is smoothed rather than set.</summary>
    public float Occlusion;
    public float OcclusionTarget;

    /// <summary>How much of the pool's per-frame occlusion budget this voice has had. Used
    /// to round-robin the marching so the cost stays flat however busy the field is.</summary>
    public int OcclusionStamp;

    // --- How it sounds ------------------------------------------------------------
    public double Cursor;           // fractional read position, in source samples
    public double Step;             // source samples per output sample — pitch and rate in one
    public float GainL, GainR, TargetL, TargetR;
    public float Send, TargetSend;
    public float Cutoff = Spatializer.NearCutoff;
    public float TargetCutoff = Spatializer.NearCutoff;
    public OnePole Filter;

    /// <summary>Output samples still to wait before this voice starts sounding — the time
    /// the sound spends travelling. Counted down by the mixer.</summary>
    public int DelayFrames;

    /// <summary>Set when the game asks a looping voice to stop. The mixer fades it out over
    /// a short ramp and then frees it, because cutting a bed dead is a click.</summary>
    public bool Releasing;

    /// <summary>The spec row this voice was started from, kept so the game thread can
    /// re-place it every frame without looking it up again.</summary>
    public SoundSpec Spec;

    /// <summary>Sets everything up for a fresh sound. The filter is opened rather than
    /// carried over — the previous occupant's state has nothing to do with this one.</summary>
    public void Begin(Clip clip, int key, in SoundSpec spec, double step, in Placement place,
        Vector2 pos, float height, int owner, bool loop, float occlusion)
    {
        Clip = clip;
        Key = key;
        Spec = spec;
        Bus = spec.Bus;
        Priority = spec.Priority;
        Spatial = spec.Spatial;
        Occludes = spec.Occludes;
        Loop = loop;
        Owner = owner;
        Position = pos;
        Height = height;
        Occlusion = OcclusionTarget = occlusion;

        Cursor = 0.0;
        Step = step;
        // Start already at level. Ramping up from silence would soften every transient in
        // the game — and a transient is most of what a gunshot is.
        GainL = TargetL = place.GainL;
        GainR = TargetR = place.GainR;
        Send = TargetSend = place.ReverbSend;
        Cutoff = TargetCutoff = place.Cutoff;
        Filter = default;
        Filter.SetCutoff(Cutoff, Mixer.SampleRate);
        DelayFrames = (int)(place.DelaySeconds * Mixer.SampleRate);
        Releasing = false;
        Active = true;
    }

    /// <summary>Hands the slot back. The generation bump is what invalidates any handle
    /// still pointing here.</summary>
    public void End()
    {
        Active = false;
        Releasing = false;
        Clip = null;
        Key = -1;
        Loop = false;
        Generation++;
    }

    /// <summary>Roughly how loud this voice is right now — the number stealing compares.
    /// A voice still travelling counts as loud so it cannot be stolen before it is heard.</summary>
    public float Level => DelayFrames > 0 ? 1f : MathF.Max(MathF.Abs(GainL), MathF.Abs(GainR));
}
