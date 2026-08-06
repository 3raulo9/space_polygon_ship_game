namespace Unrendered.Acoustics;

/// <summary>Special places that override the ordinary room entirely. Everything else — the
/// open grid, an alley, a plaza — is derived continuously and needs no name.</summary>
public enum Interior : byte
{
    /// <summary>Outside, wherever that is. The room is measured, not chosen.</summary>
    Open = 0,
    /// <summary>Inside the Maw. Everything outside your hull is drowned.</summary>
    Swallowed,
    /// <summary>In the claw. The world is pushed back and the machine holding you is not.</summary>
    Seized,
    /// <summary>Mid-jump. Nothing is where it was.</summary>
    Hyperspace,
}

/// <summary>
/// Decides what the world sounds like from where the listener is standing, and what state
/// their ears are in. Two separate stories that end up on the same three knobs — room,
/// master filter, master gain — because deafness and drowning are the same shape.
///
/// Nothing here knows about the game. It is fed two measurements a frame (how much city is
/// around, how much open sky is above), an <see cref="Interior"/>, and blast events; it
/// writes to the mixer's master and room controls.
/// </summary>
public sealed class Ambience
{
    /// <summary>0 = standing in the open, 1 = boxed in on every side. Set each frame by the
    /// game from what is actually standing around the listener.</summary>
    public float Enclosure;

    /// <summary>0 = a lid overhead, 1 = nothing but sky. Separates a plaza (enclosed at
    /// ground level, open above — a long, thin, bright tail) from a covered arcade
    /// (enclosed both ways — short, dark and close).</summary>
    public float Openness = 1f;

    public Interior Where = Interior.Open;

    /// <summary>How stunned the listener is, 1 down to 0. Set by a blast and decayed here.</summary>
    public float Concussion { get; private set; }

    /// <summary>How long a full concussion takes to wear off. Long enough to be an event
    /// you have to survive rather than a flourish.</summary>
    public const float ConcussionRecovery = 4.2f;

    /// <summary>Extra ducking asked for by the game — the music sidechain, a cinematic.
    /// Multiplied into the master, kept separate from the concussion so the two cannot
    /// fight over one variable.</summary>
    public float Duck = 1f;

    /// <summary>Master fader, 0..1, from the settings screen.</summary>
    public float MasterVolume = 1f;

    /// <summary>
    /// A blast went off <paramref name="distance"/> away with this much force. Only the
    /// close ones register: the whole point is that being caught in something is different
    /// from watching it happen.
    /// </summary>
    public void Blast(float distance, float force = 1f)
    {
        const float DeafenRange = 26f;
        if (distance >= DeafenRange) return;
        float hit = (1f - distance / DeafenRange) * Math.Clamp(force, 0f, 1.5f);
        // Takes the worse of the two rather than summing: two blasts do not deafen you
        // twice as much as one, they deafen you as much as the worse one did.
        if (hit > Concussion) Concussion = Math.Clamp(hit, 0f, 1f);
    }

    /// <summary>A softer version for the things that ought to sit heavily without actually
    /// stunning — your shield crossing the low line, being grabbed.</summary>
    public void Daze(float amount)
    {
        float hit = Math.Clamp(amount, 0f, 0.55f);
        if (hit > Concussion) Concussion = hit;
    }

    public void Clear()
    {
        Concussion = 0f;
        Duck = 1f;
        Where = Interior.Open;
    }

    /// <summary>
    /// Folds a frame of it into the mixer. Everything written here is a <em>target</em>;
    /// the mixer eases toward it, so this can be called every frame from anywhere.
    /// </summary>
    public void Apply(Mixer mix, float dt)
    {
        if (Concussion > 0f)
        {
            Concussion -= dt / ConcussionRecovery;
            if (Concussion < 0f) Concussion = 0f;
        }

        // --- The room ---------------------------------------------------------------
        // Out on the open grid there is almost nothing to reflect off, so the tail is
        // short and quiet. Between towers it grows, and it grows longest where the walls
        // are close but the sky is open — which is what a street is, and why a shot in one
        // rings the way it does.
        float size = 0.18f + Enclosure * 0.55f + Openness * Enclosure * 0.2f;
        float damp = 0.62f - Openness * 0.3f;      // open sky keeps the top end alive
        float wet = 0.05f + Enclosure * 0.42f;

        float cutoff = Spatializer.NearCutoff;
        float gain = 1f;

        switch (Where)
        {
            case Interior.Swallowed:
                // Inside the throat. A huge, soft, drowned space; the outside world only
                // just gets in at all.
                size = 0.92f; damp = 0.85f; wet = 0.85f;
                cutoff = 420f; gain = 0.55f;
                break;

            case Interior.Seized:
                // Held. The world is at arm's length and the thing holding you is not —
                // the machine's own noise is a cue like any other and reaches you through
                // the same filter, but everything past it is pushed back.
                size = 0.55f; damp = 0.55f; wet = 0.5f;
                cutoff = 2600f; gain = 0.82f;
                break;

            case Interior.Hyperspace:
                // Nowhere. An enormous bright room and almost no direct sound.
                size = 0.99f; damp = 0.1f; wet = 0.95f;
                cutoff = 9000f; gain = 0.7f;
                break;
        }

        // --- Your ears --------------------------------------------------------------
        // Concussion is the same three knobs again, applied on top of wherever you are.
        if (Concussion > 0f)
        {
            float c = Concussion;
            cutoff = MathF.Min(cutoff, 20000f * MathF.Pow(0.035f, c));   // → ~700Hz at full
            gain *= 1f - 0.68f * c;
            wet = MathF.Max(wet, 0.35f * c);
            // Cubed rather than squared, and quiet even at the top. The tone is a pure sine
            // sitting above a mix that has just been ducked and slammed shut, so it has the
            // whole top end to itself — it reads far louder than its level suggests, and only
            // a genuinely point-blank blast should leave much of it behind at all.
            mix.RingTarget = 0.018f * c * c * c;
            mix.RingHz = 4200f - 400f * c;
        }
        else mix.RingTarget = 0f;

        mix.SetRoom(size, damp);
        mix.WetTarget = Math.Clamp(wet, 0f, 1f);
        mix.MasterCutoffTarget = cutoff;
        mix.MasterTarget = Math.Clamp(gain * Duck * MasterVolume, 0f, 1.2f);
    }
}
