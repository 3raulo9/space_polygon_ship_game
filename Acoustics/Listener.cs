using System.Numerics;

namespace Unrendered.Acoustics;

/// <summary>
/// Where the ears are and which way they point. One per machine — this is the seat whose
/// screen you are looking at, which in a match is <c>world.Eye</c> and not necessarily your
/// own craft: when your revives are spent and you are riding a team-mate's shoulder, you
/// hear the fight from where <em>they</em> are standing.
///
/// Heading follows the game's convention — forward is <c>(sin h, cos h)</c> on the ground
/// plane — so a heading of zero looks down +Z and right is +X.
/// </summary>
public struct Listener
{
    /// <summary>Ground position of the ears.</summary>
    public Vector2 Position;

    /// <summary>How high off the grid they are. Drives the elevation tilt, which is the
    /// only reason a Maw hanging over your head sounds like it is over your head.</summary>
    public float Height;

    /// <summary>Where the view is pointing, radians, game convention.</summary>
    public float Heading;

    /// <summary>Up/down of the view, radians. Only used to narrow the stereo field when
    /// you are looking hard up or down, where left-and-right stops meaning much.</summary>
    public float Pitch;

    /// <summary>The unit vector pointing right of the view, on the ground plane. This is
    /// the axis a pan is measured along.</summary>
    public readonly Vector2 Right => new(MathF.Cos(Heading), -MathF.Sin(Heading));

    /// <summary>The unit vector the view faces, on the ground plane.</summary>
    public readonly Vector2 Forward => new(MathF.Sin(Heading), MathF.Cos(Heading));
}

/// <summary>
/// The result of placing one sound relative to the listener: two channel gains, a corner
/// frequency, a reverb send and how long the sound takes to get here.
///
/// Deliberately a plain value with no behaviour. Every one of these numbers can be asserted
/// by a headless test — which is the whole answer to "how do we know the mix is right
/// without two PCs and a pair of ears".
/// </summary>
public readonly record struct Placement(
    float GainL,
    float GainR,
    float Cutoff,
    float ReverbSend,
    float DelaySeconds,
    float Distance
)
{
    /// <summary>Combined level, ignoring where it sits in the field — the number a test
    /// asks about when it only cares whether something was audible at all.</summary>
    public float Gain => MathF.Sqrt(GainL * GainL + GainR * GainR);

    /// <summary>-1 hard left, 0 centred, +1 hard right. Derived rather than stored so
    /// there is only ever one description of the stereo position.</summary>
    public float Pan
    {
        get
        {
            float sum = GainL + GainR;
            return sum <= 1e-6f ? 0f : (GainR - GainL) / sum;
        }
    }

    /// <summary>Nothing to hear.</summary>
    public static readonly Placement Silent = new(0f, 0f, 20000f, 0f, 0f, float.MaxValue);
}

/// <summary>
/// Turns "this happened, there" into <see cref="Placement"/>. Pure arithmetic — no state,
/// no Raylib, no world — so the whole spatial model can be exercised in the self-test.
/// </summary>
public static class Spatializer
{
    /// <summary>Metres — world units, here — per second. Slow enough that a shot across
    /// the arena arrives visibly late, which is the point.</summary>
    public const float SpeedOfSound = 340f;

    /// <summary>The corner air absorption walks a sound down to at full range. Not silence:
    /// a distant blast still has body, it has just lost everything that made it a crack.</summary>
    public const float FarCutoff = 620f;

    /// <summary>Open. Nothing above this is worth filtering.</summary>
    public const float NearCutoff = 20000f;

    /// <summary>How far the stereo field collapses at maximum range. A distant firefight
    /// should feel like it is somewhere over there rather than pinned to one speaker —
    /// distance blurs direction, in ears as in eyes.</summary>
    private const float FarPanScale = 0.35f;

    /// <summary>The shortest ground vector from a to b, honouring the world's wrap. Zero
    /// wrap means an ordinary flat plane, which is what a test or another game would use.</summary>
    public static Vector2 Delta(Vector2 from, Vector2 to, float wrap)
    {
        Vector2 d = to - from;
        if (wrap <= 0f) return d;
        d.X -= wrap * MathF.Floor(d.X / wrap + 0.5f);
        d.Y -= wrap * MathF.Floor(d.Y / wrap + 0.5f);
        return d;
    }

    /// <summary>
    /// Places a sound. <paramref name="occlusion"/> is 0 for a clear line and 1 for a
    /// source buried behind the city; the engine measures it separately because it costs a
    /// ray march and this function is meant to stay free.
    /// </summary>
    public static Placement Place(in Listener ear, in SoundSpec spec, Vector2 source,
        float sourceHeight, float occlusion, float wrap)
    {
        // Anything that belongs in the player's head skips all of it: dead centre, full
        // level, dry. A menu click is not an event in the world and must not be treated
        // as one.
        if (!spec.Spatial)
        {
            float flat = spec.Gain * 0.70710678f;   // equal-power centre
            return new Placement(flat, flat, NearCutoff, 0f, 0f, 0f);
        }

        Vector2 to = Delta(ear.Position, source, wrap);
        float dist = to.Length();
        if (dist >= spec.MaxDistance) return Placement.Silent;

        // --- Level -------------------------------------------------------------------
        // A straight normalised fade raised to the cue's own exponent. It reaches exactly
        // zero at MaxDistance, which matters: it means "out of range" and "inaudible" are
        // the same statement, so culling a voice can never cut off something still audible.
        float span = MathF.Max(1e-3f, spec.MaxDistance - spec.MinDistance);
        float t = Math.Clamp((dist - spec.MinDistance) / span, 0f, 1f);   // 0 near .. 1 far
        float gain = spec.Gain * MathF.Pow(1f - t, MathF.Max(0.05f, spec.Rolloff));

        // Walls take level as well as brightness. Not to nothing — you can always hear
        // that something happened, you just cannot tell what.
        gain *= 1f - occlusion * 0.62f;

        // --- Brightness --------------------------------------------------------------
        // Air first. The curve is deliberately steeper than the level curve: the ear reads
        // dullness as distance long before it reads quietness as distance, and this is the
        // single change that stops everything sounding like it is inside your helmet.
        float absorb = MathF.Pow(t, 0.65f) * spec.AirAbsorb;
        float cutoff = NearCutoff * MathF.Pow(FarCutoff / NearCutoff, absorb);

        // Then the city, which is a far harder filter than air ever is.
        if (occlusion > 0f)
        {
            float occCut = 340f + (1f - occlusion) * 5000f;
            cutoff = MathF.Min(cutoff, occCut);
        }

        // --- Where it sits -----------------------------------------------------------
        float pan = 0f, elevation = 0f, front = 1f;
        if (dist > 1e-4f)
        {
            Vector2 dir = to / dist;
            pan = Vector2.Dot(dir, ear.Right);
            front = Vector2.Dot(dir, ear.Forward);
            elevation = (sourceHeight - ear.Height) / MathF.Max(dist, 1f);
        }

        // Behind you. A stereo pan alone cannot say front from back — something due east is
        // hard right whether you are facing north or south, and something directly behind
        // you is dead centre, exactly like something directly ahead. What actually tells a
        // real pair of ears is that a head and two pinnae take the top off anything coming
        // from behind, so that is what is modelled: rear sources go duller and a touch
        // quieter. Small numbers, but it is the difference between "someone is shooting" and
        // "someone is shooting at my back".
        if (front < 0f)
        {
            float back = -front;
            cutoff *= 1f - 0.45f * back;
            gain *= 1f - 0.14f * back;
        }

        // Distance blurs direction; height does too — a thing directly above you is
        // neither left nor right, and the Maw hanging over the grid should feel exactly
        // that unplaceable. Looking hard up or down narrows it for the same reason.
        float panScale = 1f - (1f - FarPanScale) * t;
        panScale *= 1f - 0.55f * Math.Clamp(MathF.Abs(elevation), 0f, 1f);
        panScale *= 1f - 0.35f * Math.Clamp(MathF.Abs(ear.Pitch) / 1.2f, 0f, 1f);
        pan = Math.Clamp(pan * panScale, -1f, 1f);

        // A source above the ear keeps its edge and loses body; one below is muffled by
        // the ground between you. Small numbers — this is a hint, not an effect.
        if (elevation > 0f) cutoff *= 1f + 0.25f * Math.Clamp(elevation, 0f, 1f);
        else cutoff *= 1f - 0.30f * Math.Clamp(-elevation, 0f, 1f);
        cutoff = Math.Clamp(cutoff, 120f, NearCutoff);

        // Equal-power pan: the total energy is constant as it crosses, so a sound sweeping
        // past does not dip in the middle.
        float angle = (pan + 1f) * (MathF.PI * 0.25f);
        float l = MathF.Cos(angle) * gain;
        float r = MathF.Sin(angle) * gain;

        // --- The room ----------------------------------------------------------------
        // Further away is wetter — the direct sound falls off with distance and the
        // reflected field does not, which is most of what "reverb" tells the ear.
        // Occluded sources are wetter still: what reaches you round a corner is nearly all
        // reflection.
        float send = spec.ReverbSend * (0.25f + 0.75f * t);
        send += occlusion * 0.35f * spec.ReverbSend;
        send = Math.Clamp(send, 0f, 1f);

        float delay = spec.TravelDelay ? dist / SpeedOfSound : 0f;

        return new Placement(l, r, cutoff, send, delay, dist);
    }
}
