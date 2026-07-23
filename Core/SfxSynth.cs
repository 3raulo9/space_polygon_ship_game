namespace VoidTanks.Core;

/// <summary>
/// A small BFXR/sfxr-style sound engine. It builds a retro sound effect from
/// nothing — an oscillator, an envelope and a handful of sweeps — and hands back
/// the bytes of a finished .wav, ready for Raylib to load straight out of memory.
///
/// The point of synthesising rather than loading a file is <em>mutation</em>: a
/// cue driven from here can be rolled fresh on every single trigger, so it never
/// plays the same twice. Nothing in this class touches Raylib or the audio
/// device, so it is safe to call from the headless self-test.
/// </summary>
public static class SfxSynth
{
    public const int SampleRate = 44100;

    /// <summary>The oscillator a voice is built on.</summary>
    public enum Osc { Square, Saw, Sine, Noise }

    /// <summary>
    /// One sound's full recipe. Defaults describe a plain half-second square
    /// beep; every field below bends it somewhere else.
    /// </summary>
    public sealed class Params
    {
        public Osc Wave = Osc.Square;
        public float Length = 0.4f;        // seconds

        // Pitch is swept exponentially from Start to End across the whole sound,
        // so a rising End is a "charging" glide and a falling one is a drop.
        public float StartFreq = 220f;
        public float EndFreq = 220f;

        // Envelope, as fractions of Length. They are normalised on render, so
        // they only have to be in the right proportion to each other.
        public float Attack = 0.1f;
        public float Sustain = 0.4f;
        public float Decay = 0.5f;

        // Square-wave pulse width, and how far it drifts over the sound. Sweeping
        // duty is what gives sfxr sounds their hollow, phasing metallic character.
        public float Duty = 0.5f;
        public float DutySweep = 0f;

        // Pitch wobble — depth as a fraction of the current frequency.
        public float VibratoDepth = 0f;
        public float VibratoSpeed = 0f;

        // Amplitude wobble: the level dips to (1 - TremoloDepth) and back, this many
        // times a second. Where vibrato bends the note, this chops it — which is what
        // makes something sound like it is *rotating* rather than merely droning,
        // since each revolution of a real motor lands as a pulse in the level.
        public float TremoloDepth = 0f;
        public float TremoloSpeed = 0f;

        // A second voice at this ratio of the main pitch, mixed in at DetuneGain.
        // Set it to something dissonant (~1.4) and the sound turns queasy.
        public float Detune = 0f;
        public float DetuneGain = 0.5f;

        // Resonant low-pass, sweeping by LpSweep per sample, plus a one-pole
        // high-pass to thin out the mud. Cutoff 1 = wide open.
        public float LpCutoff = 1f;
        public float LpResonance = 0f;
        public float LpSweep = 1f;
        public float HpCutoff = 0f;

        // Deliberate degradation: quantise to CrushBits bits, and hold each
        // sample for CrushRate frames. Both off at 0.
        public int CrushBits = 0;
        public int CrushRate = 0;

        // Target peak, not a raw gain: the render normalises to this, so two
        // recipes with wildly different filtering still come out matched in level.
        public float Volume = 0.5f;
        public int Seed = 0;               // drives the noise oscillator only

        /// <summary>
        /// Render a seamlessly loopable bed rather than a one-shot. This is a
        /// different job from everything else here, so it overrides a few fields:
        /// the envelope and the end fades are dropped (they would pump once per
        /// lap), the pitch glide and every sweep are pinned flat (they cannot
        /// arrive back where they started), and each oscillator's frequency is
        /// snapped so a whole number of cycles fits the buffer exactly — without
        /// that the waveform jumps at the seam and the loop ticks audibly.
        /// </summary>
        public bool Loop = false;
    }

    /// <summary>
    /// Renders a recipe to mono 16-bit PCM wrapped in a canonical 44-byte .wav
    /// header — the exact bytes a .wav file on disk would hold, so Raylib's
    /// in-memory loader accepts it unchanged.
    /// </summary>
    public static byte[] RenderWav(Params p)
    {
        float[] samples = Render(p);
        var bytes = new byte[44 + samples.Length * 2];
        int dataSize = samples.Length * 2;

        WriteTag(bytes, 0, "RIFF");
        WriteI32(bytes, 4, 36 + dataSize);
        WriteTag(bytes, 8, "WAVE");
        WriteTag(bytes, 12, "fmt ");
        WriteI32(bytes, 16, 16);                 // fmt chunk length
        WriteI16(bytes, 20, 1);                  // 1 = uncompressed PCM
        WriteI16(bytes, 22, 1);                  // mono
        WriteI32(bytes, 24, SampleRate);
        WriteI32(bytes, 28, SampleRate * 2);     // byte rate (mono, 2 bytes/sample)
        WriteI16(bytes, 32, 2);                  // block align
        WriteI16(bytes, 34, 16);                 // bits per sample
        WriteTag(bytes, 36, "data");
        WriteI32(bytes, 40, dataSize);

        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)(Math.Clamp(samples[i], -1f, 1f) * 32767f);
            bytes[44 + i * 2] = (byte)(s & 0xFF);
            bytes[45 + i * 2] = (byte)((s >> 8) & 0xFF);
        }
        return bytes;
    }

    /// <summary>
    /// Renders a recipe to raw mono samples in -1..1. Split out from
    /// <see cref="RenderWav"/> so the DSP can be exercised without the container.
    /// </summary>
    public static float[] Render(Params p)
    {
        int total = Math.Max(1, (int)(SampleRate * p.Length));
        var outBuf = new float[total];
        var noise = new Random(p.Seed);

        // Envelope stage lengths in samples, from the normalised proportions.
        float envSum = MathF.Max(0.0001f, p.Attack + p.Sustain + p.Decay);
        int atk = (int)(total * (p.Attack / envSum));
        int sus = (int)(total * (p.Sustain / envSum));

        double phase = 0, phase2 = 0;
        float duty = p.Duty;
        // Exponential pitch glide: the per-sample ratio that walks Start to End.
        double startF = Math.Max(1.0, p.StartFreq);
        double endF = Math.Max(1.0, p.EndFreq);
        double freqStep = Math.Pow(endF / startF, 1.0 / total);
        double freq = startF;

        // Loop mode pins the pitch flat and snaps every periodic component so a
        // whole number of cycles lands exactly in the buffer — the seam then falls
        // on a zero crossing for all of them at once and the lap is inaudible.
        double dur = total / (double)SampleRate;
        double detune = p.Detune;
        float vibSpeed = p.VibratoSpeed;
        float tremSpeed = p.TremoloSpeed;
        float dutySweep = p.DutySweep;
        float lpSweep = p.LpSweep;
        if (p.Loop)
        {
            freqStep = 1.0;                                   // no glide
            dutySweep = 0f;                                   // no sweeps: they
            lpSweep = 1f;                                     // can't come back round
            startF = freq = Math.Max(1.0, Math.Round(startF * dur)) / dur;
            if (detune > 0)
                detune = Math.Max(1.0, Math.Round(freq * detune * dur)) / dur / freq;
            if (p.VibratoDepth > 0f)
                vibSpeed = (float)(Math.Max(1.0, Math.Round(vibSpeed * dur)) / dur);
            if (p.TremoloDepth > 0f)
                tremSpeed = (float)(Math.Max(1.0, Math.Round(tremSpeed * dur)) / dur);
        }

        // In loop mode the buffer is rendered twice and only the second lap kept:
        // the filter and crusher carry state, so a cold start would leave the head
        // of the loop settling while the tail is already steady, and that mismatch
        // is exactly what you hear at the seam. Discarding a warm-up lap means the
        // returned samples begin in the same steady state they end in.
        int warm = p.Loop ? total : 0;

        // Filter state, carried between samples.
        double fltP = 0, fltDp = 0, fltPhp = 0;
        double fltW = Math.Pow(p.LpCutoff, 3.0) * 0.1;
        double fltDmp = Math.Clamp(1.0 - p.LpResonance, 0.0, 1.0) * 0.8;
        double fltHp = Math.Pow(p.HpCutoff, 2.0) * 0.1;

        float held = 0f;                 // sample-and-hold value for the crusher
        int crushLevels = p.CrushBits > 0 ? (1 << p.CrushBits) / 2 : 0;

        for (int n = 0; n < warm + total; n++)
        {
            int i = n - warm;                   // <0 while warming up
            float t = (float)(n % total) / SampleRate;

            // --- Pitch: the glide, wobbled by vibrato -------------------------
            freq *= freqStep;
            double f = freq;
            if (p.VibratoDepth > 0f)
                f *= 1.0 + p.VibratoDepth * Math.Sin(2 * Math.PI * vibSpeed * t);
            f = Math.Clamp(f, 1.0, SampleRate / 2.0);

            phase += f / SampleRate;
            if (phase >= 1.0) phase -= Math.Floor(phase);

            duty = Math.Clamp(duty + dutySweep / SampleRate, 0.02f, 0.98f);

            float sample = Oscillate(p.Wave, phase, duty, noise);

            // The dissonant partner voice, if this recipe asked for one.
            if (p.Detune > 0f)
            {
                phase2 += f * detune / SampleRate;
                if (phase2 >= 1.0) phase2 -= Math.Floor(phase2);
                sample += Oscillate(p.Wave, phase2, duty, noise) * p.DetuneGain;
            }

            // --- Filters (sfxr's resonant low-pass, then a one-pole high-pass) -
            if (p.LpCutoff < 1f)
            {
                fltW = Math.Clamp(fltW * lpSweep, 0.0, 0.1);
                double prev = fltP;
                fltDp += (sample - fltP) * fltW;
                fltDp -= fltDp * fltDmp;
                fltP += fltDp;
                fltPhp += fltP - prev;
                fltPhp -= fltPhp * (p.HpCutoff > 0f ? fltHp : 0.0);
                sample = (float)(p.HpCutoff > 0f ? fltPhp : fltP);
            }

            if (i < 0) continue;                // still warming the filter up

            // --- Envelope (a loop is a steady bed: it has none) ----------------
            float env = 1f;
            if (!p.Loop)
            {
                if (i < atk) env = atk > 0 ? (float)i / atk : 1f;
                else if (i < atk + sus) env = 1f;
                else
                {
                    int decLen = Math.Max(1, total - atk - sus);
                    env = 1f - (float)(i - atk - sus) / decLen;
                }
            }

            // The rotation pulse, on top of whatever the envelope is doing.
            if (p.TremoloDepth > 0f)
                env *= 1f - p.TremoloDepth * (0.5f + 0.5f * MathF.Sin(2 * MathF.PI * tremSpeed * t));

            outBuf[i] = sample * env;
        }

        // --- Normalise, THEN degrade ------------------------------------------
        // Order matters here, and getting it wrong is silent (literally). The
        // bit-crusher quantises onto a fixed absolute grid, but how much signal
        // survives the oscillator/filter stage varies enormously with the dice —
        // a heavily low-passed recipe can land entirely below the first quantisation
        // step, where every sample rounds to zero and the whole clip renders as
        // silence. So the raw signal is lifted to full scale first; the crusher then
        // always has something to bite on, and CrushBits means the same thing in
        // every recipe. This pass also keeps a mutating cue from jumping ~10dB
        // between triggers, which reads as a bug rather than as variety.
        float peak = 0f;
        for (int i = 0; i < total; i++) peak = MathF.Max(peak, MathF.Abs(outBuf[i]));
        if (peak <= 0.0001f) return outBuf;          // genuinely nothing to voice
        float gain = 1f / peak;
        for (int i = 0; i < total; i++) outBuf[i] *= gain;

        for (int i = 0; i < total; i++)
        {
            float sample = outBuf[i];
            if (crushLevels > 0)
                sample = MathF.Round(sample * crushLevels) / crushLevels;
            if (p.CrushRate > 1)
            {
                if (i % p.CrushRate == 0) held = sample;
                sample = held;
            }
            outBuf[i] = sample * p.Volume;
        }

        // A couple of milliseconds of fade at each end kills the DC click that an
        // oscillator starting or stopping mid-cycle would otherwise leave behind.
        // A loop must NOT be faded: the whole point is that its ends already meet,
        // and notching them to zero would put a hole in the bed once per lap.
        if (p.Loop) return outBuf;
        int fade = Math.Min(96, total / 2);
        for (int i = 0; i < fade; i++)
        {
            float g = (float)i / fade;
            outBuf[i] *= g;
            outBuf[total - 1 - i] *= g;
        }
        return outBuf;
    }

    /// <summary>One oscillator sample for a phase in 0..1.</summary>
    private static float Oscillate(Osc wave, double phase, float duty, Random noise) => wave switch
    {
        Osc.Square => phase < duty ? 0.5f : -0.5f,
        Osc.Saw => (float)(1.0 - phase * 2.0) * 0.5f,
        Osc.Sine => (float)Math.Sin(phase * 2 * Math.PI) * 0.5f,
        _ => (float)(noise.NextDouble() * 2.0 - 1.0) * 0.5f,
    };

    // --- Recipes --------------------------------------------------------------

    /// <summary>
    /// The Crab-Core's charge-up: a creepy, dead-eyed machine drawing power. A
    /// bit-crushed square glides <em>upward</em> the whole way — the "powering
    /// up" — under a slow attack that swells in rather than striking, with a
    /// dissonant second voice a sour interval above it and a heavy vibrato wobble
    /// so the climb never sounds steady or safe.
    ///
    /// Every value is rolled inside a deliberately wide band, so no two triggers
    /// produce the same sound; <paramref name="step"/> is the index of this snap
    /// within the boss's clamp burst (0, 1, 2…), which lifts the whole thing a
    /// little higher and tightens it each time, so three clicks in a row read as
    /// one machine spinning up toward the attack rather than three unrelated noises.
    /// </summary>
    public static Params CreepyPowerUp(Random rng, int step)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        // Each successive clamp starts higher and climbs further — the spin-up.
        float climb = 1f + step * 0.28f;
        float baseF = Range(70f, 130f) * climb;

        return new Params
        {
            Wave = rng.NextDouble() < 0.75 ? Osc.Square : Osc.Saw,
            Length = Range(0.34f, 0.52f),

            StartFreq = baseF,
            // Rises to somewhere between 4x and 9x — the wide band is most of why
            // one trigger sounds like a whine and the next like a growl.
            EndFreq = baseF * Range(4f, 9f),

            Attack = Range(0.30f, 0.55f),     // swells in; no percussive front
            Sustain = Range(0.10f, 0.25f),
            Decay = Range(0.25f, 0.45f),

            Duty = Range(0.12f, 0.5f),
            DutySweep = Range(-0.9f, 0.9f),   // hollows out or thickens as it climbs

            VibratoDepth = Range(0.02f, 0.075f),
            VibratoSpeed = Range(14f, 38f),   // fast enough to sound mechanical, not musical

            Detune = Range(1.34f, 1.52f),     // roughly a tritone: the queasy interval
            DetuneGain = Range(0.3f, 0.55f),

            LpCutoff = Range(0.45f, 0.85f),
            LpResonance = Range(0.25f, 0.6f),
            LpSweep = 1f + Range(0.000004f, 0.000022f),  // filter opens as it charges
            HpCutoff = Range(0.01f, 0.06f),

            CrushBits = rng.Next(4, 8),       // grimy, early-hardware edge
            CrushRate = rng.Next(1, 4),
            Volume = 0.72f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The Crab-Core's hunting call, voiced over and over while it runs the player
    /// down. Deliberately the inverse of <see cref="CreepyPowerUp"/> in every
    /// dimension: where the charge-up is short, tight, bright and <em>rising</em>,
    /// this is long, muffled, subterranean and mostly <em>sagging</em> — a groan
    /// that swells up out of the floor and dies away rather than a machine snapping
    /// to attention. The two never get confused for each other.
    ///
    /// Its creep comes from two tricks. The detune sits only a hair off unison
    /// (~1.02), so the two voices beat against each other in a slow, seasick throb
    /// instead of forming an interval. And the low-pass is clamped right down, so
    /// almost all of it is felt as a rumble under the floor with only the edge of
    /// the harmonics audible — the sound of something large behind you rather than
    /// a noise being played at you.
    ///
    /// Roughly one call in four bends <em>upward</em> instead, which lands as a
    /// questioning, searching inflection — the moment it thinks it has found you.
    /// </summary>
    public static Params HuntingCall(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(48f, 95f);
        bool searching = rng.NextDouble() < 0.25;   // the rising, questioning one

        return new Params
        {
            Wave = rng.NextDouble() < 0.7 ? Osc.Saw : Osc.Square,  // saw = guttural
            Length = Range(0.55f, 1.05f),           // drawn out, never a blip

            StartFreq = baseF,
            EndFreq = searching ? baseF * Range(1.25f, 1.7f)   // "...where are you"
                                : baseF * Range(0.55f, 0.85f), // a sagging groan

            Attack = Range(0.25f, 0.45f),           // breathes in
            Sustain = Range(0.15f, 0.3f),
            Decay = Range(0.35f, 0.55f),            // and out

            Duty = Range(0.2f, 0.6f),
            DutySweep = Range(-0.35f, 0.35f),       // slow drift, not a whoop

            VibratoDepth = Range(0.045f, 0.13f),
            VibratoSpeed = Range(3.5f, 11f),        // slow waver — a moan, not a buzz

            Detune = Range(1.008f, 1.035f),         // near-unison: a beating throb
            DetuneGain = Range(0.55f, 0.85f),

            LpCutoff = Range(0.16f, 0.34f),         // buried; felt more than heard
            LpResonance = Range(0.3f, 0.65f),
            LpSweep = 1f + Range(-0.000006f, 0.000010f),
            HpCutoff = Range(0.005f, 0.025f),

            CrushBits = rng.Next(3, 7),             // grimier than the charge-up
            CrushRate = rng.Next(2, 7),
            Volume = 0.68f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The Crab-Core dying: a long, falling scream. It enters near the top of its
    /// range and slides continuously down some four or five octaves into sub-bass
    /// over the whole death glitch, so the thing sounds like it is being dragged
    /// down rather than simply switched off — bright and shrieking at the start,
    /// a floor-shaking groan by the end.
    ///
    /// The weight at the bottom comes from a second voice pitched a hair off a full
    /// octave <em>below</em> the fundamental (a Detune near 0.5, where every other
    /// recipe here sits above 1.0). Being slightly off an exact octave is the whole
    /// trick: an exact 0.5 would just read as a fatter, cleaner tone, while 0.49
    /// beats slowly against the fundamental and turns the low end unstable and
    /// sick. A deep, fast vibrato on top keeps the slide wailing instead of gliding
    /// smoothly, and the filter closes as it descends so the scream darkens into
    /// the bass rather than staying bright all the way down.
    /// </summary>
    public static Params DeathScream(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        return new Params
        {
            Wave = Osc.Saw,                         // richest harmonics: the shriek
            Length = Range(1.35f, 1.75f),           // spans the whole death glitch

            StartFreq = Range(900f, 1500f),         // up at the top of its register
            EndFreq = Range(38f, 55f),              // dragged down into sub-bass

            Attack = Range(0.02f, 0.05f),           // starts abruptly — a scream, not a swell
            Sustain = Range(0.25f, 0.4f),
            Decay = Range(0.6f, 0.8f),              // long fall-away into nothing

            Duty = Range(0.25f, 0.5f),
            DutySweep = Range(-0.5f, 0.5f),

            VibratoDepth = Range(0.05f, 0.11f),     // deep — a wail, not a glide
            VibratoSpeed = Range(11f, 24f),

            Detune = Range(0.485f, 0.515f),         // sub-octave, deliberately not exact
            DetuneGain = Range(0.7f, 0.95f),        // loud: this is where the bass lives

            LpCutoff = Range(0.7f, 0.95f),          // wide open at the top of the slide
            LpResonance = Range(0.35f, 0.65f),
            LpSweep = 1f - Range(0.000002f, 0.000008f),  // closes as it falls: darkens
            HpCutoff = 0f,                          // keep every bit of the low end

            CrushBits = rng.Next(5, 9),
            CrushRate = rng.Next(1, 3),
            Volume = 0.8f,                          // the loudest thing in the bank
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The Crab-Core's idling machinery — a seamless, continuous hum, as if
    /// something heavy is spinning inside the chassis. This is the boss's bed
    /// rather than an event: it runs the entire time the thing exists, and the
    /// caller shifts its playback rate to spool the rotor up when the crab wakes.
    ///
    /// Pitched low and buried under a tight filter so it reads as machinery heard
    /// through armour. The detune again sits just off unison, which at these
    /// frequencies is heard not as two notes but as a slow throb cycling through
    /// the drone — the wow of something rotating slightly out of true.
    /// </summary>
    public static Params Hum(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(52f, 72f);
        return new Params
        {
            Loop = true,
            Wave = Osc.Saw,
            Length = Range(1.1f, 1.5f),        // long enough that the lap isn't a pattern

            StartFreq = baseF,
            EndFreq = baseF,                   // pinned flat by Loop anyway

            VibratoDepth = Range(0.012f, 0.03f),   // faint wow, not a wobble
            VibratoSpeed = Range(2.5f, 6f),

            Detune = Range(1.004f, 1.016f),    // the slow rotational throb
            DetuneGain = Range(0.6f, 0.85f),

            Duty = Range(0.3f, 0.5f),
            LpCutoff = Range(0.13f, 0.24f),    // machinery behind a bulkhead
            LpResonance = Range(0.2f, 0.45f),
            HpCutoff = 0f,

            CrushBits = rng.Next(5, 9),
            CrushRate = 1,                     // rate-crush can't be made seamless
            Volume = 0.5f,                     // the caller scales this by range
            Seed = rng.Next(),
        };
    }

    // --- The player's SPIDER emitter ------------------------------------------

    /// <summary>
    /// The SPIDER's lance winding up — a seamless bed, not an event, because unlike the
    /// boss's fixed 2.6-second charge the player's is held for as long as they dare. It
    /// therefore cannot carry the rise in its own envelope: the caller drives the
    /// playback rate off the meter, so the whine climbs exactly as far as the charge
    /// does and holds there when the meter tops out.
    ///
    /// Deliberately thinner and higher than <see cref="Hum"/>. That one is a heavy thing
    /// turning over inside armour; this is a small salvaged core in your own hands, so
    /// it wants to sound close, electrical, and a bit unshielded — which is the tremolo
    /// doing most of the work, chopping the tone into something that reads as charging
    /// rather than merely droning.
    /// </summary>
    public static Params LanceCharge(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(120f, 150f);
        return new Params
        {
            Loop = true,
            Wave = Osc.Saw,
            Length = Range(0.7f, 0.95f),

            StartFreq = baseF,
            EndFreq = baseF,                       // pinned flat by Loop anyway

            TremoloDepth = Range(0.3f, 0.45f),     // the audible cycling of the charge
            TremoloSpeed = Range(11f, 16f),

            VibratoDepth = Range(0.01f, 0.025f),
            VibratoSpeed = Range(5f, 9f),

            Detune = Range(1.49f, 1.51f),          // a fifth up: a tone that sounds *loaded*
            DetuneGain = Range(0.35f, 0.5f),

            Duty = Range(0.2f, 0.36f),
            LpCutoff = Range(0.34f, 0.5f),         // brighter than the boss's rotor
            LpResonance = Range(0.35f, 0.6f),
            HpCutoff = Range(0.01f, 0.04f),

            CrushBits = rng.Next(5, 9),
            CrushRate = 1,                         // rate-crush can't be made seamless
            Volume = 0.5f,                         // the caller scales this
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The lance discharging. Short on purpose — a fraction of a second, against the
    /// boss's five-second sung beam, because the player's shaft burns for barely half a
    /// second and a cue that outlives its own light reads as a sound that got stuck.
    /// A hard downward swoop with the crush on, so it lands as the salvaged core letting
    /// go of everything at once rather than as the clean thing it was cut out of.
    /// </summary>
    public static Params LanceFire(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(520f, 640f);
        return new Params
        {
            Wave = Osc.Saw,
            Length = Range(0.36f, 0.46f),

            StartFreq = baseF,
            EndFreq = baseF * Range(0.16f, 0.26f),  // dumps its pitch as it empties

            Attack = 0.01f,
            Sustain = Range(0.18f, 0.28f),
            Decay = Range(0.7f, 0.8f),

            TremoloDepth = Range(0.15f, 0.3f),
            TremoloSpeed = Range(24f, 38f),

            Detune = Range(1.48f, 1.52f),           // the charge's own interval, released
            DetuneGain = Range(0.4f, 0.6f),

            Duty = Range(0.25f, 0.45f),
            DutySweep = Range(-1.2f, -0.4f),
            LpCutoff = Range(0.55f, 0.85f),
            LpResonance = Range(0.35f, 0.6f),
            HpCutoff = Range(0.01f, 0.03f),

            CrushBits = rng.Next(4, 8),
            CrushRate = rng.Next(1, 3),
            Volume = 0.5f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// One of the SPIDER's small lasers. Tiny and dry: a fast downward zap with almost
    /// no body, so a stream of them at the cannon's cadence reads as a rate of fire
    /// rather than as a wall of noise. Sits well above the cannon's report in pitch so
    /// the two chassis are audibly different weapons even with your eyes shut.
    /// </summary>
    public static Params Laser(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(1050f, 1400f);
        return new Params
        {
            Wave = Osc.Square,
            Length = Range(0.07f, 0.1f),

            StartFreq = baseF,
            EndFreq = baseF * Range(0.28f, 0.42f),

            Attack = 0.008f,
            Sustain = Range(0.12f, 0.2f),
            Decay = Range(0.78f, 0.88f),

            Duty = Range(0.1f, 0.24f),              // thin, so it cuts without weight
            DutySweep = Range(-1f, 1f),

            Detune = Range(1.49f, 1.51f),
            DetuneGain = Range(0.2f, 0.35f),

            LpCutoff = Range(0.6f, 0.9f),
            LpResonance = Range(0.2f, 0.45f),
            HpCutoff = Range(0.05f, 0.12f),

            CrushBits = rng.Next(4, 8),
            CrushRate = rng.Next(1, 3),
            Volume = 0.4f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// A single leg's actuator working as the foot plants — the servo whine and
    /// creak layered over the impact itself. Short, dry and mechanical.
    ///
    /// <paramref name="leg"/> is the limb's index, and it deliberately shifts the
    /// pitch band: each leg keeps its own voice from step to step, as though the six
    /// joints were built to slightly different tolerances. That consistency is what
    /// sells it as one machine walking rather than a pile of random clanks — you
    /// start to recognise which side it is coming down on. Within that band the
    /// values still roll freely, so no two steps of the same leg are identical.
    /// </summary>
    public static Params ServoStep(Random rng, int leg)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        // Each limb sits a fixed step up the band; the odd/even split follows the
        // rig's alternating tripod, so the two halves of the gait answer each other.
        // Kept narrow — spread these too far and the legs stop sounding like six of
        // the same part and start sounding like six different instruments.
        float legBias = 1f + leg * 0.05f + (leg % 2 == 0 ? 0f : 0.07f);
        float baseF = Range(58f, 88f) * legBias;    // down in motor territory

        return new Params
        {
            Wave = Osc.Square,                      // flat and machined; a saw sings
            Length = Range(0.13f, 0.23f),           // long enough to hear it turn

            StartFreq = baseF,
            EndFreq = baseF * Range(0.55f, 0.78f),  // bogs down as it takes the load

            Attack = Range(0.01f, 0.05f),           // hard front: the joint catching
            Sustain = Range(0.25f, 0.45f),
            Decay = Range(0.55f, 0.75f),

            // The rotation itself. A few dozen pulses a second is the chug of a
            // geared motor turning — this is what the sound is actually built on.
            TremoloDepth = Range(0.55f, 0.85f),
            TremoloSpeed = Range(26f, 46f),

            Duty = Range(0.35f, 0.5f),              // full-bodied, not nasal
            DutySweep = Range(-0.5f, 0.5f),         // mild: big sweeps read as comic

            VibratoDepth = Range(0.004f, 0.018f),   // barely there — a machine holds
            VibratoSpeed = Range(8f, 18f),          // its pitch; wobble sounds alive

            // Near-unison, so the two voices grind against each other instead of
            // forming the cartoonish interval a tritone gave.
            Detune = Range(1.006f, 1.022f),
            DetuneGain = Range(0.5f, 0.75f),

            LpCutoff = Range(0.22f, 0.4f),          // dark; heard through a housing
            LpResonance = Range(0.25f, 0.5f),
            HpCutoff = 0f,                          // keep the weight down low

            CrushBits = rng.Next(3, 6),
            CrushRate = rng.Next(3, 9),             // heavy aliasing reads as gearing
            Volume = 0.5f,                          // a layer over the stomp, not a
                                                    // replacement — caller scales it
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The Crab-Core screaming into the player's face while it holds them off the
    /// grid — the loudest, ugliest thing in the bank. This is the shriek layer; it is
    /// always voiced together with <see cref="CrabScreamUnder"/>, and the pair is the
    /// sound rather than either half alone.
    ///
    /// Where the death scream <em>falls</em> (something being dragged down), this one
    /// climbs the whole way: a rising cry reads as aggression aimed at you, a falling
    /// one as collapse. Two things make it wrong rather than merely loud. The detune
    /// sits on a tritone at almost the same level as the fundamental, so there is no
    /// root note to hold on to — the ear can't decide which voice is the sound. And
    /// the vibrato is far too deep to be a machine holding a pitch, so the cry keeps
    /// buckling as it rises, like something that has to force the note out.
    /// </summary>
    public static Params CrabScream(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(190f, 280f);
        return new Params
        {
            Wave = Osc.Saw,                         // the harshest harmonics available
            Length = Range(1.5f, 1.9f),             // it holds you there for all of it

            StartFreq = baseF,
            EndFreq = baseF * Range(3.4f, 5.2f),    // climbing at you, never sagging

            Attack = Range(0.05f, 0.1f),            // lunges in, barely a swell
            Sustain = Range(0.45f, 0.6f),           // and then just... keeps going
            Decay = Range(0.3f, 0.45f),

            Duty = Range(0.15f, 0.4f),
            DutySweep = Range(-1.2f, 1.2f),         // the timbre writhes as it climbs

            VibratoDepth = Range(0.13f, 0.2f),      // far too deep to sound controlled
            VibratoSpeed = Range(7f, 15f),

            Detune = Range(1.40f, 1.47f),           // tritone — no root to settle on
            DetuneGain = Range(0.8f, 0.95f),        // nearly as loud as the fundamental

            LpCutoff = Range(0.85f, 1f),            // nothing softens this at all
            LpResonance = Range(0.3f, 0.6f),
            HpCutoff = Range(0.02f, 0.05f),

            CrushBits = rng.Next(3, 6),             // the grimiest setting in the bank
            CrushRate = rng.Next(1, 4),
            Volume = 0.92f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The body underneath <see cref="CrabScream"/>, voiced at the same instant. The
    /// shriek alone is thin and reads as a noise being played at you; this puts a
    /// chest behind it, so it lands as something with mass doing the screaming.
    ///
    /// Pitched down where the shriek is up, and slow where it is frantic: a near-
    /// unison detune throbs against itself, and a heavy tremolo in the few-hertz
    /// range heaves the level in and out. That heave is the trick — a steady drone
    /// reads as machinery, but something that surges and drops at roughly the rate a
    /// large animal breathes reads as alive, which is much worse.
    /// </summary>
    public static Params CrabScreamUnder(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(42f, 68f);
        return new Params
        {
            Wave = Osc.Saw,
            Length = Range(1.5f, 1.9f),             // matched to the shriek it sits under

            StartFreq = baseF,
            EndFreq = baseF * Range(1.3f, 1.85f),   // rises with the cry, far more slowly

            Attack = Range(0.1f, 0.18f),
            Sustain = Range(0.5f, 0.65f),
            Decay = Range(0.25f, 0.4f),

            // Roughly breathing rate. This is what makes the low end sound like a
            // thing rather than a tone.
            TremoloDepth = Range(0.3f, 0.5f),
            TremoloSpeed = Range(4.5f, 9f),

            Duty = Range(0.3f, 0.55f),
            VibratoDepth = Range(0.02f, 0.05f),
            VibratoSpeed = Range(2.5f, 6f),

            Detune = Range(1.01f, 1.03f),           // near-unison: a slow, sick throb
            DetuneGain = Range(0.7f, 0.9f),

            LpCutoff = Range(0.14f, 0.26f),         // felt through the floor
            LpResonance = Range(0.3f, 0.6f),
            HpCutoff = 0f,                          // every bit of the low end kept

            CrushBits = rng.Next(4, 8),
            CrushRate = rng.Next(2, 6),
            Volume = 0.88f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The free claw coming down on the player: a single, dry, extremely short crunch.
    /// Built on noise rather than a pitched oscillator, because an impact has no note
    /// — what makes it read as <em>heavy</em> is the filter slamming shut across the
    /// clip, which is the acoustic signature of a big dull mass rather than a snap.
    /// Bit-crushed hard so it lands as damage in this palette rather than as a
    /// realistic thud.
    /// </summary>
    public static Params ClawSlam(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        return new Params
        {
            Wave = Osc.Noise,
            Length = Range(0.28f, 0.42f),

            Attack = 0.005f,                        // no front at all: it is already here
            Sustain = Range(0.1f, 0.2f),
            Decay = Range(0.75f, 0.9f),             // and then collapses away

            LpCutoff = Range(0.42f, 0.6f),
            LpResonance = Range(0.35f, 0.65f),
            LpSweep = 1f - Range(0.00002f, 0.00005f), // slams shut: the weight of it
            HpCutoff = 0f,

            CrushBits = rng.Next(3, 6),
            CrushRate = rng.Next(2, 7),
            Volume = 0.85f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The air tearing past while the player is in the air after being thrown. Noise
    /// again — and noise is the only oscillator here that ignores frequency entirely,
    /// so a whoosh can only be shaped by its envelope and its filter. This one opens
    /// its low-pass as it goes, which is heard as the rush building rather than a
    /// pitch rising, and its envelope swells and falls across the whole arc so the
    /// loudest moment is the top of the flight.
    /// </summary>
    public static Params ThrowWhoosh(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        return new Params
        {
            Wave = Osc.Noise,
            Length = Range(1.1f, 1.5f),             // spans the arc

            Attack = Range(0.25f, 0.4f),            // builds as the ground drops away
            Sustain = Range(0.15f, 0.25f),
            Decay = Range(0.4f, 0.55f),

            LpCutoff = Range(0.12f, 0.2f),
            LpResonance = Range(0.15f, 0.35f),
            LpSweep = 1f + Range(0.000008f, 0.00002f),  // the rush opening up
            HpCutoff = Range(0.01f, 0.04f),

            CrushBits = rng.Next(5, 9),
            CrushRate = rng.Next(1, 4),
            Volume = 0.55f,                         // under the action, not over it
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The player's craft hitting the grid at the end of the throw. Short, low and
    /// falling — the pitch drop is what sells impact with the floor rather than with
    /// another object, and the whole thing is over fast enough to read as one event.
    /// </summary>
    public static Params LandThud(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(80f, 130f);
        return new Params
        {
            Wave = rng.NextDouble() < 0.5 ? Osc.Square : Osc.Saw,
            Length = Range(0.3f, 0.45f),

            StartFreq = baseF,
            EndFreq = baseF * Range(0.28f, 0.42f),  // drops away into the floor

            Attack = 0.01f,
            Sustain = Range(0.15f, 0.25f),
            Decay = Range(0.7f, 0.85f),

            Duty = Range(0.3f, 0.55f),
            DutySweep = Range(-0.6f, 0.6f),

            Detune = Range(1.005f, 1.02f),          // a touch of grind on the impact
            DetuneGain = Range(0.5f, 0.75f),

            LpCutoff = Range(0.2f, 0.35f),
            LpResonance = Range(0.25f, 0.5f),
            HpCutoff = 0f,

            CrushBits = rng.Next(3, 7),
            CrushRate = rng.Next(2, 6),
            Volume = 0.75f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The neon core taking a hit: a short, high, wrong-sounding shriek layered over
    /// the impact clip. <paramref name="severity"/> runs 0 on the first hit to 1 as
    /// the last of the core's integrity goes, and drives the whole thing higher,
    /// tighter and more unstable — the boss audibly losing composure as it is worn
    /// down, so the final hit before it dies is the most frantic sound it makes.
    /// </summary>
    public static Params CoreSting(Random rng, float severity)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
        severity = Math.Clamp(severity, 0f, 1f);

        // Climbs roughly an octave across the fight.
        float baseF = Range(1250f, 1750f) * (1f + severity * 0.85f);

        return new Params
        {
            Wave = rng.NextDouble() < 0.6 ? Osc.Saw : Osc.Square,
            Length = Range(0.14f, 0.26f),

            StartFreq = baseF,
            // Kicks up hard at the top of its range — a flinch, not a fall.
            EndFreq = baseF * Range(1.25f, 1.9f),

            Attack = 0.01f,                         // instantaneous: it is a stab
            Sustain = Range(0.15f, 0.3f),
            Decay = Range(0.7f, 0.85f),

            Duty = Range(0.08f, 0.3f),              // thin, piercing
            DutySweep = Range(-1.5f, 1.5f),

            // Both wobble and dissonance widen as it weakens — the shriek loses hold.
            VibratoDepth = Range(0.03f, 0.06f) + severity * 0.06f,
            VibratoSpeed = Range(30f, 55f) + severity * 25f,

            Detune = Range(1.38f, 1.48f) + severity * 0.08f,
            DetuneGain = Range(0.35f, 0.6f),

            LpCutoff = Range(0.75f, 1f),            // wide open: nothing softens this
            LpResonance = Range(0.2f, 0.5f),
            HpCutoff = Range(0.05f, 0.14f),         // strip the body; leave the edge

            CrushBits = rng.Next(4, 8),
            CrushRate = rng.Next(1, 3),
            Volume = 0.6f,
            Seed = rng.Next(),
        };
    }

    // --- The lance: charge, warn, fire ---------------------------------------

    /// <summary>
    /// The crystal spinning up to fire — the sound of the charge, voiced once at the
    /// start and shaped by its own envelope across the whole wind-up.
    ///
    /// A laser charging and a thing <em>spinning</em> are two different sounds, and
    /// this has to be both. The charge is the pitch glide: a saw climbing better than
    /// two octaves, never once sagging. The spin is the tremolo — the level chopped
    /// dozens of times a second, which is what a rotating emitter does to a tone and
    /// is the same trick the leg servos are built on. Under it a near-unison detune
    /// beats against the fundamental so the climb sounds like it is being forced
    /// rather than played, and the filter opens the whole way up so the top of the
    /// charge is the brightest, nastiest moment in it.
    /// </summary>
    public static Params BeamCharge(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(85f, 130f);
        return new Params
        {
            Wave = Osc.Saw,
            Length = Range(2.5f, 2.75f),            // spans the charge window

            StartFreq = baseF,
            EndFreq = baseF * Range(7f, 11f),       // a long, unbroken climb

            Attack = Range(0.15f, 0.25f),           // spools up; no percussive front
            Sustain = Range(0.55f, 0.7f),
            Decay = Range(0.12f, 0.22f),            // still going when the shot lands

            // The rotation. Fast enough to read as a rotor rather than a wobble, and
            // deep enough that you hear each pass of the emitter.
            TremoloDepth = Range(0.45f, 0.7f),
            TremoloSpeed = Range(22f, 34f),

            Duty = Range(0.2f, 0.45f),
            DutySweep = Range(-0.6f, 0.6f),

            VibratoDepth = Range(0.01f, 0.03f),     // a machine holds its pitch
            VibratoSpeed = Range(16f, 30f),

            Detune = Range(1.008f, 1.028f),         // near-unison: the forced grind
            DetuneGain = Range(0.55f, 0.8f),

            LpCutoff = Range(0.35f, 0.55f),
            LpResonance = Range(0.35f, 0.65f),
            LpSweep = 1f + Range(0.000006f, 0.000014f),  // opens as it fills
            HpCutoff = Range(0.005f, 0.02f),

            CrushBits = rng.Next(4, 8),
            CrushRate = rng.Next(1, 4),
            Volume = 0.6f,                          // a bed under the warnings
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// One of the three warnings that punctuate the charge — the synth layer voiced
    /// alongside the bank's alarm clip. <paramref name="step"/> is which of the three
    /// this is (0, 1, 2), and it lifts the whole beep a clear interval each time.
    ///
    /// That climb is doing the actual work: three identical beeps read as a repeating
    /// alert, which the player learns to ignore, while three that step upward read as
    /// a countdown running out — you know without being told that there is a fourth
    /// thing coming and that it is not a beep. Short, hard-edged and unfiltered so
    /// each one cuts through the charge droning underneath it.
    /// </summary>
    public static Params WarningBeep(Random rng, int step)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        // Roughly a fourth per step: far enough apart to hear as a climb rather than
        // as the same note drifting.
        float baseF = Range(620f, 720f) * MathF.Pow(1.34f, step);

        return new Params
        {
            Wave = Osc.Square,                      // flat and synthetic: an instrument panel
            Length = Range(0.16f, 0.22f),

            StartFreq = baseF,
            EndFreq = baseF * Range(1.0f, 1.06f),   // near-flat: a tone, not a chirp

            Attack = 0.01f,                         // instantaneous
            Sustain = Range(0.55f, 0.7f),
            Decay = Range(0.25f, 0.4f),

            Duty = Range(0.2f, 0.35f),              // thin and piercing

            Detune = Range(1.49f, 1.51f),           // a fifth: harmonically "correct"
            DetuneGain = Range(0.4f, 0.6f),         // and therefore clearly an alarm

            LpCutoff = Range(0.8f, 1f),             // nothing softens it
            HpCutoff = Range(0.03f, 0.07f),

            CrushBits = rng.Next(5, 9),
            CrushRate = rng.Next(1, 3),
            Volume = 0.62f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The dull "no" a full magazine gives back when you try to cram more rounds into
    /// it: a short, low, flat square buzz that sags in pitch rather than climbing.
    /// Deliberately the inverse of the bright pickup blip — same era of sound, but
    /// dropping and thin, so it reads as "rejected" without being an alarm.
    /// </summary>
    public static Params FullBuzz(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(150f, 170f);
        return new Params
        {
            Wave = Osc.Square,
            Length = Range(0.16f, 0.2f),

            StartFreq = baseF,
            EndFreq = baseF * Range(0.7f, 0.78f),    // sags downward: a refusal, not a chirp

            Attack = 0.02f,
            Sustain = Range(0.4f, 0.5f),
            Decay = Range(0.45f, 0.55f),

            Duty = Range(0.45f, 0.55f),              // fat and buzzy, not piercing

            LpCutoff = Range(0.5f, 0.65f),           // rolled off — a muffled thud
            HpCutoff = Range(0.02f, 0.04f),

            CrushBits = rng.Next(4, 7),
            CrushRate = rng.Next(1, 3),
            Volume = 0.5f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The beam itself: a five-second choral tone, and deliberately the only
    /// <em>beautiful</em> sound in the entire bank. Everything else the Crab-Core
    /// makes is crushed, detuned and sick; the thing that actually kills you sings.
    /// That inversion is the point — the attack should feel like a judgement rather
    /// than an attack.
    ///
    /// Built as pure sine with no bit-crush at all (nothing else here is), a very
    /// slow swell in and out, and a detune only a few cents off unison so the two
    /// voices drift in and out of phase across the burn like a held choir. The slow
    /// shallow tremolo is the shimmer. Voiced together with
    /// <see cref="BeamChoir"/> a fifth above it.
    /// </summary>
    public static Params BeamAngelic(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(300f, 360f);
        return new Params
        {
            Wave = Osc.Sine,                        // pure: no harmonics to sound harsh
            Length = 5f,                            // exactly the length of the burn

            StartFreq = baseF,
            EndFreq = baseF * Range(1.0f, 1.04f),   // barely moves: a held note

            Attack = Range(0.06f, 0.1f),            // arrives with the beam
            Sustain = Range(0.7f, 0.8f),
            Decay = Range(0.12f, 0.22f),            // and fades as it cuts out

            TremoloDepth = Range(0.12f, 0.22f),     // the shimmer
            TremoloSpeed = Range(5f, 8f),

            VibratoDepth = Range(0.004f, 0.01f),    // a voice, not a siren
            VibratoSpeed = Range(4.5f, 6.5f),

            Detune = Range(1.002f, 1.006f),         // cents apart: a chorus, not an interval
            DetuneGain = Range(0.7f, 0.9f),

            LpCutoff = 1f,                          // wide open; a sine needs no filtering
            HpCutoff = 0f,

            CrushBits = 0,                          // the one clean sound in the game
            CrushRate = 0,
            Volume = 0.55f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The upper voice of the beam, a fifth above <see cref="BeamAngelic"/> and
    /// quieter. A single held sine is a test tone; two in a consonant interval is a
    /// chord, and that is the whole difference between "a beam is on" and something
    /// that sounds like it is being sung at you. Kept under the root so the pair
    /// reads as one voice with an overtone rather than as two notes.
    /// </summary>
    public static Params BeamChoir(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(300f, 360f) * 1.5f;     // the fifth
        return new Params
        {
            Wave = Osc.Sine,
            Length = 5f,

            StartFreq = baseF,
            EndFreq = baseF * Range(1.0f, 1.03f),

            Attack = Range(0.2f, 0.32f),            // swells in behind the root
            Sustain = Range(0.55f, 0.65f),
            Decay = Range(0.15f, 0.25f),

            TremoloDepth = Range(0.2f, 0.35f),
            TremoloSpeed = Range(3f, 5f),           // slower than the root: they drift

            Detune = Range(2.0f, 2.005f),           // an octave over the fifth
            DetuneGain = Range(0.3f, 0.45f),

            LpCutoff = 1f,
            HpCutoff = Range(0.01f, 0.03f),

            CrushBits = 0,
            CrushRate = 0,
            Volume = 0.34f,                         // clearly the upper voice
            Seed = rng.Next(),
        };
    }

    // --- The thrown CRAB CORE's blast ----------------------------------------
    // The boss's beam is the one clean, sung sound in the game. A *thrown* core is that
    // sound gone wrong: the same held voices, but dragged down, detuned into a dissonant
    // interval and welded to two crushed, mechanical layers — a low grinding drone and a
    // metallic clatter — so the star reads as the crab's attack ripped out of the crab
    // and misfiring in every direction at once. Creepier and more machined, on purpose.

    /// <summary>
    /// The low grind under the blast: a bit-crushed, detuned saw dragged downward over
    /// the burn. Dissonant and mechanical where the boss's beam is consonant and pure —
    /// this is the layer that makes the star sound broken.
    /// </summary>
    public static Params CrabBlastGrind(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(70f, 95f);
        return new Params
        {
            Wave = Osc.Saw,
            Length = 1.3f,                          // the length of the star's burn

            StartFreq = baseF * Range(1.1f, 1.3f),
            EndFreq = baseF * Range(0.6f, 0.8f),    // sags downward as it fires

            Attack = 0.02f,
            Sustain = 0.6f,
            Decay = 0.38f,

            Duty = 0.5f,
            DutySweep = Range(-0.3f, -0.1f),        // hollows out as it goes

            TremoloDepth = Range(0.35f, 0.55f),     // a heavy mechanical chop
            TremoloSpeed = Range(14f, 22f),

            Detune = Range(1.45f, 1.52f),           // a queasy, dissonant second voice
            DetuneGain = Range(0.6f, 0.8f),

            LpCutoff = Range(0.5f, 0.7f),
            LpResonance = Range(0.2f, 0.4f),
            LpSweep = 0.9999f,                       // closes slowly, darkening the tail

            CrushBits = 5,                           // machined, degraded
            CrushRate = 3,
            Volume = 0.6f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The metallic clatter riding on top: crushed noise-and-square, high and fast, like
    /// the core's housing rattling itself apart as it discharges. Short — a burst at the
    /// front of the blast rather than a held layer.
    /// </summary>
    public static Params CrabBlastMetal(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        return new Params
        {
            Wave = Osc.Square,
            Length = Range(0.7f, 0.95f),

            StartFreq = Range(520f, 700f),
            EndFreq = Range(180f, 260f),             // clatters downward

            Attack = 0.01f,
            Sustain = 0.3f,
            Decay = 0.5f,

            Duty = Range(0.15f, 0.3f),               // thin, nasal
            DutySweep = Range(0.2f, 0.5f),

            TremoloDepth = Range(0.5f, 0.75f),       // a fast metallic stutter
            TremoloSpeed = Range(28f, 44f),

            Detune = Range(1.33f, 1.42f),            // dissonant again
            DetuneGain = Range(0.4f, 0.6f),

            LpCutoff = Range(0.7f, 0.9f),
            HpCutoff = Range(0.05f, 0.12f),          // thinned so it sits over the grind

            CrushBits = 4,                           // hard degradation — the wrongest layer
            CrushRate = 4,
            Volume = 0.4f,
            Seed = rng.Next(),
        };
    }

    // --- The Maw-Core: a hovering mouth --------------------------------------
    // The Crab-Core is a machine and every sound it makes is machined: crushed,
    // geared, mechanical. The Maw-Core is built from the same parts but the half that
    // walked has been replaced by something wet, and its whole sound bank is built on
    // that contrast. Where the crab's cues are tight and rhythmic, these are loose,
    // breathy and irregular — the same chassis with an animal living in it.

    /// <summary>
    /// The hover: the bed that runs the entire time the thing is on the field. A
    /// seamless loop like the crab's rotor, and deliberately its opposite in
    /// character — where the rotor is a tight low drone heard through armour, this is
    /// airier and unsteady, with a slow heave in the level at roughly the rate
    /// something large breathes.
    ///
    /// That heave is what makes it read as flight rather than as machinery. A
    /// perfectly steady drone is a motor holding a speed; a drone that surges and
    /// sags is something working to stay up. The caller drives the playback rate, so
    /// the whole bed — heave included — speeds up as it notices you.
    /// </summary>
    public static Params MawHover(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(74f, 96f);
        return new Params
        {
            Loop = true,
            Wave = Osc.Saw,
            Length = Range(1.3f, 1.7f),        // long enough that the lap isn't a pattern

            StartFreq = baseF,
            EndFreq = baseF,                   // pinned flat by Loop anyway

            // The breathing. Slower and deeper than anything the crab does — this is
            // the single field that separates "hovering" from "idling".
            TremoloDepth = Range(0.3f, 0.45f),
            TremoloSpeed = Range(1.6f, 3.2f),

            VibratoDepth = Range(0.03f, 0.055f),   // an unsteady hold, not a machine's
            VibratoSpeed = Range(4f, 7.5f),

            Detune = Range(1.012f, 1.032f),    // further off unison than the rotor: sicker
            DetuneGain = Range(0.6f, 0.85f),

            Duty = Range(0.25f, 0.45f),
            LpCutoff = Range(0.2f, 0.32f),     // airier than the crab's bulkhead drone
            LpResonance = Range(0.25f, 0.5f),
            HpCutoff = 0f,

            CrushBits = rng.Next(4, 8),
            CrushRate = 1,                     // rate-crush can't be made seamless
            Volume = 0.5f,                     // the caller scales this by range
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The crystal turning in its well — voiced on a cadence while the thing hunts,
    /// so the spin is something you hear as well as see.
    ///
    /// Built almost entirely out of tremolo. A pitched tone with its level chopped
    /// thirty-odd times a second <em>is</em> the sound of a thing rotating: each pass
    /// of a facet past you is one pulse in the level, and the ear reads a train of
    /// those as revolution rather than as a note. The pitch barely moves; it is the
    /// chop that carries the whole effect, which is why this is the one recipe here
    /// where the tremolo is the loudest decision in it.
    /// </summary>
    public static Params CrystalWhirr(Random rng, float agitation)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
        agitation = Math.Clamp(agitation, 0f, 1f);

        float baseF = Range(380f, 520f) * (1f + agitation * 0.5f);
        return new Params
        {
            Wave = Osc.Square,                      // hard-edged: it is a faceted crystal
            Length = Range(0.5f, 0.75f),

            StartFreq = baseF,
            EndFreq = baseF * Range(0.94f, 1.12f),  // near-flat: a held ring

            Attack = Range(0.15f, 0.3f),            // it is already turning when you hear it
            Sustain = Range(0.35f, 0.5f),
            Decay = Range(0.3f, 0.45f),

            // The revolution itself, faster the more worked-up it is.
            TremoloDepth = Range(0.6f, 0.85f),
            TremoloSpeed = Range(24f, 38f) * (1f + agitation * 0.6f),

            Duty = Range(0.1f, 0.28f),              // thin and glassy
            DutySweep = Range(-0.8f, 0.8f),

            VibratoDepth = Range(0.008f, 0.022f),
            VibratoSpeed = Range(12f, 26f),

            Detune = Range(1.48f, 1.52f),           // a fifth: it rings rather than growls
            DetuneGain = Range(0.3f, 0.5f),

            LpCutoff = Range(0.55f, 0.8f),
            LpResonance = Range(0.35f, 0.65f),
            HpCutoff = Range(0.04f, 0.09f),         // strip the body, keep the shimmer

            CrushBits = rng.Next(4, 8),
            CrushRate = rng.Next(1, 3),
            Volume = 0.42f,                         // under everything; a texture
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The rings of teeth grinding against each other. Noise rather than a tone,
    /// because bone on bone has no note — and heavily rate-crushed, which is what
    /// turns smooth hiss into a stuttering scrape. <paramref name="grinding"/> is set
    /// while it is actually chewing someone, which lengthens it and drops the filter
    /// so the sound goes from a dry rattle overhead to something happening around you.
    /// </summary>
    public static Params ToothGrind(Random rng, bool grinding)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        return new Params
        {
            Wave = Osc.Noise,
            Length = grinding ? Range(0.4f, 0.55f) : Range(0.22f, 0.34f),

            Attack = Range(0.08f, 0.2f),            // scrapes in rather than striking
            Sustain = Range(0.3f, 0.45f),
            Decay = Range(0.4f, 0.6f),

            // The chop of individual teeth passing. Slower than a motor's gearing —
            // these are big and there are only nine of them.
            TremoloDepth = Range(0.5f, 0.8f),
            TremoloSpeed = grinding ? Range(14f, 22f) : Range(9f, 15f),

            LpCutoff = grinding ? Range(0.3f, 0.45f) : Range(0.45f, 0.65f),
            LpResonance = Range(0.3f, 0.6f),
            LpSweep = 1f - Range(0f, 0.00002f),
            HpCutoff = grinding ? 0f : Range(0.02f, 0.06f),

            CrushBits = rng.Next(2, 5),             // the grimiest in the bank
            CrushRate = rng.Next(4, 12),            // heavy aliasing = the scrape
            Volume = grinding ? 0.72f : 0.5f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// One of the little lasers being spat out. Short, thin and <em>falling</em> — a
    /// descending blip reads as something being expelled, where the rising blip every
    /// other weapon in the game uses reads as something being launched. It is a small
    /// distinction and it is the entire reason these never get confused with the
    /// player's own cannon.
    /// </summary>
    public static Params MawSpit(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(700f, 1000f);
        return new Params
        {
            Wave = rng.NextDouble() < 0.5 ? Osc.Square : Osc.Saw,
            Length = Range(0.16f, 0.26f),

            StartFreq = baseF,
            EndFreq = baseF * Range(0.3f, 0.5f),    // spat downward

            Attack = 0.01f,
            Sustain = Range(0.2f, 0.35f),
            Decay = Range(0.65f, 0.8f),

            Duty = Range(0.12f, 0.32f),             // thin: it is a small thing
            DutySweep = Range(-1f, 1f),

            VibratoDepth = Range(0.02f, 0.06f),
            VibratoSpeed = Range(20f, 40f),

            Detune = Range(1.33f, 1.45f),           // sour, so it never sounds friendly
            DetuneGain = Range(0.3f, 0.5f),

            LpCutoff = Range(0.5f, 0.8f),
            LpResonance = Range(0.3f, 0.6f),
            HpCutoff = Range(0.03f, 0.08f),

            CrushBits = rng.Next(3, 7),
            CrushRate = rng.Next(1, 4),
            Volume = 0.45f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The thing coming down on the player. Noise with its filter thrown wide open
    /// across the clip, which is heard as a rush arriving rather than as a pitch
    /// moving — the same trick the Crab-Core's throw whoosh uses, run much faster and
    /// much louder, because this one is coming <em>at</em> you rather than carrying
    /// you. It is over in a third of a second: the dive is not something to react to,
    /// it is the consequence of having already stood still too long.
    /// </summary>
    public static Params MawDive(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        return new Params
        {
            Wave = Osc.Noise,
            Length = Range(0.35f, 0.5f),

            Attack = Range(0.05f, 0.12f),           // arrives almost fully formed
            Sustain = Range(0.25f, 0.4f),
            Decay = Range(0.5f, 0.65f),

            LpCutoff = Range(0.1f, 0.18f),
            LpResonance = Range(0.3f, 0.55f),
            LpSweep = 1f + Range(0.00006f, 0.00013f),  // tears open as it falls on you
            HpCutoff = 0f,

            CrushBits = rng.Next(4, 8),
            CrushRate = rng.Next(1, 4),
            Volume = 0.85f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The gulp: the throat closing over the player and hauling them up. Low, wet and
    /// rising — rising because they are being drawn <em>upward</em> into it, and a
    /// falling swallow would read as something going down a drain instead.
    ///
    /// The wetness is the near-unison detune beating against a heavily closed filter.
    /// There is no clean way to synthesise "wet" out of an oscillator and an envelope;
    /// what actually reads as wet is a low sound whose two voices are slightly out of
    /// tune with each other and whose top end has been taken away entirely, so it
    /// sounds like it is happening inside something rather than in the air.
    /// </summary>
    public static Params MawSwallow(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(55f, 85f);
        return new Params
        {
            Wave = Osc.Saw,
            Length = Range(0.6f, 0.85f),

            StartFreq = baseF,
            EndFreq = baseF * Range(2.2f, 3.4f),    // drawn up the throat

            Attack = Range(0.1f, 0.2f),
            Sustain = Range(0.3f, 0.45f),
            Decay = Range(0.4f, 0.6f),

            TremoloDepth = Range(0.3f, 0.55f),
            TremoloSpeed = Range(7f, 13f),          // the peristalsis of it

            Duty = Range(0.3f, 0.55f),
            DutySweep = Range(-0.8f, 0.8f),

            VibratoDepth = Range(0.06f, 0.12f),     // nothing here holds a pitch
            VibratoSpeed = Range(5f, 11f),

            Detune = Range(1.015f, 1.045f),         // the beating that reads as wet
            DetuneGain = Range(0.7f, 0.95f),

            LpCutoff = Range(0.12f, 0.22f),         // no top end at all: it is internal
            LpResonance = Range(0.35f, 0.65f),
            HpCutoff = 0f,

            CrushBits = rng.Next(3, 7),
            CrushRate = rng.Next(2, 7),
            Volume = 0.85f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// One bite while it is digesting the player — the sound of losing fifteen percent
    /// of a shield. A crunch layered onto a pitch drop: the noise front is the teeth
    /// closing, and the low fall under it is what makes it land as damage rather than
    /// as texture. Short, so each bite is unmistakably one event and the player can
    /// count them.
    /// </summary>
    public static Params MawDigestBite(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(90f, 150f);
        return new Params
        {
            Wave = Osc.Saw,
            Length = Range(0.3f, 0.45f),

            StartFreq = baseF,
            EndFreq = baseF * Range(0.22f, 0.36f),  // bottoms out hard

            Attack = 0.005f,                        // no front: it is already happening
            Sustain = Range(0.12f, 0.22f),
            Decay = Range(0.7f, 0.85f),

            TremoloDepth = Range(0.4f, 0.7f),
            TremoloSpeed = Range(18f, 30f),         // the grind inside the bite

            Duty = Range(0.2f, 0.45f),
            DutySweep = Range(-1f, 1f),

            Detune = Range(1.02f, 1.06f),
            DetuneGain = Range(0.7f, 0.95f),

            LpCutoff = Range(0.18f, 0.32f),
            LpResonance = Range(0.35f, 0.65f),
            LpSweep = 1f - Range(0.00001f, 0.00004f),
            HpCutoff = 0f,

            CrushBits = rng.Next(2, 5),
            CrushRate = rng.Next(3, 9),
            Volume = 0.9f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The thing being shot from the inside. <paramref name="severity"/> runs up
    /// toward 1 as the escape count fills, and everything about it tightens with that
    /// — so the third shot, the one that frees the player, is audibly the one that
    /// broke it. This is the only feedback the player has that shooting into a throat
    /// is achieving anything, so it is loud and it is unambiguous.
    /// </summary>
    public static Params MawWail(Random rng, float severity)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
        severity = Math.Clamp(severity, 0f, 1f);

        float baseF = Range(240f, 340f) * (1f + severity * 0.7f);
        return new Params
        {
            Wave = Osc.Saw,
            Length = Range(0.35f, 0.55f),

            StartFreq = baseF,
            EndFreq = baseF * Range(1.6f, 2.6f),    // a yelp, climbing

            Attack = 0.01f,                         // instantaneous: it is a flinch
            Sustain = Range(0.2f, 0.35f),
            Decay = Range(0.6f, 0.8f),

            Duty = Range(0.15f, 0.4f),
            DutySweep = Range(-1.4f, 1.4f),

            // Both widen with severity: it loses hold of the note as it loses hold of
            // the player.
            VibratoDepth = Range(0.09f, 0.15f) + severity * 0.07f,
            VibratoSpeed = Range(12f, 22f) + severity * 14f,

            Detune = Range(1.38f, 1.48f),           // tritone: no root to settle on
            DetuneGain = Range(0.7f, 0.9f),

            LpCutoff = Range(0.7f, 0.95f),
            LpResonance = Range(0.3f, 0.6f),
            HpCutoff = Range(0.02f, 0.06f),

            CrushBits = rng.Next(3, 6),
            CrushRate = rng.Next(1, 4),
            Volume = 0.86f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The jaw springing open and throwing the player clear. A hard noise burst with
    /// the filter tearing open — the exact inverse of <see cref="MawSwallow"/>, which
    /// closed everything down. Hearing the top end come back is the release.
    /// </summary>
    public static Params MawRelease(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        return new Params
        {
            Wave = Osc.Noise,
            Length = Range(0.4f, 0.6f),

            Attack = 0.005f,                        // it lets go all at once
            Sustain = Range(0.15f, 0.25f),
            Decay = Range(0.7f, 0.85f),

            LpCutoff = Range(0.15f, 0.25f),
            LpResonance = Range(0.35f, 0.6f),
            LpSweep = 1f + Range(0.00004f, 0.0001f),  // blown wide open: the air returning
            HpCutoff = 0f,

            CrushBits = rng.Next(3, 7),
            CrushRate = rng.Next(2, 6),
            Volume = 0.8f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// A single bead of the black stuff letting go and falling. Tiny, dull and
    /// pitched down — barely a sound at all, and that is correct: it plays every
    /// half-second or so for as long as the thing is on the field, so anything with
    /// presence would become maddening inside a minute. It exists to be noticed
    /// subliminally, as a wrongness under the hover.
    /// </summary>
    public static Params MawDrip(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(300f, 520f);
        return new Params
        {
            Wave = Osc.Sine,                        // soft: a drip has no edge
            Length = Range(0.1f, 0.18f),

            StartFreq = baseF,
            EndFreq = baseF * Range(0.35f, 0.6f),   // the falling "plip"

            Attack = 0.01f,
            Sustain = Range(0.1f, 0.2f),
            Decay = Range(0.7f, 0.88f),

            VibratoDepth = Range(0.01f, 0.04f),
            VibratoSpeed = Range(15f, 30f),

            LpCutoff = Range(0.3f, 0.55f),
            LpResonance = Range(0.2f, 0.45f),
            HpCutoff = 0f,

            CrushBits = rng.Next(4, 8),
            CrushRate = rng.Next(1, 4),
            Volume = 0.28f,                         // deliberately almost inaudible
            Seed = rng.Next(),
        };
    }

    // --- The SOLDIER's cable rig ----------------------------------------------
    // Everything below is pressure, steel or air. Nothing in this set sings, glows or
    // whirrs: the chassis is a person with a gas bottle and two hooks, and the moment
    // any of it sounds electrical it stops being that. The one indulgence is how loud
    // the jump is — it is the opener of every traversal and it earns the room.

    /// <summary>
    /// The gas burst that throws a soldier fifteen metres into the air. Noise, which is
    /// the only oscillator here that ignores frequency entirely, so the whole shape is
    /// in the envelope and the filter: an instantaneous release, then the low-pass
    /// slamming open across the launch, which is heard as the body accelerating rather
    /// than as a pitch moving.
    ///
    /// <paramref name="starvation"/> (0..1) thins it toward a hiss as the reserve runs
    /// down — the filter starts higher and opens less far, which strips the body out and
    /// leaves the top. The player hears the bottle emptying several jumps before it does.
    /// </summary>
    public static Params GasBurst(Random rng, float starvation)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
        starvation = Math.Clamp(starvation, 0f, 1f);

        return new Params
        {
            Wave = Osc.Noise,
            Length = Range(0.55f, 0.75f) * (1f - 0.35f * starvation),

            Attack = 0.02f,                         // the valve opening, not a swell
            Sustain = Range(0.18f, 0.3f),
            Decay = Range(0.6f, 0.78f),             // tails out into the rise

            // Full: opens from a deep chest thump into an airy rush. Starved: starts
            // thin and stays thin — all hiss, no body.
            LpCutoff = Range(0.05f, 0.1f) + starvation * 0.45f,
            LpResonance = Range(0.3f, 0.5f),
            LpSweep = 1f + Range(0.00002f, 0.00004f) * (1f - 0.7f * starvation),
            HpCutoff = Range(0.01f, 0.03f) + starvation * 0.2f,

            CrushBits = rng.Next(5, 9),
            CrushRate = rng.Next(1, 3),
            Volume = 0.9f,                          // the loudest cue the class has
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// A hook leaving the launcher: a compressed-air pop with the line whipping out
    /// behind it. Two things at once, so the pitch climbs (the cable's hiss rising as it
    /// pays out) while the envelope falls (the pop already spent) — which is what stops
    /// it reading as a gunshot, since the two never do that together.
    /// </summary>
    public static Params CableLaunch(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(420f, 620f);
        return new Params
        {
            Wave = Osc.Noise,
            Length = Range(0.22f, 0.32f),

            StartFreq = baseF,
            EndFreq = baseF * Range(2.2f, 3.4f),    // the line hissing away from you

            Attack = 0.01f,
            Sustain = Range(0.1f, 0.18f),
            Decay = Range(0.75f, 0.88f),

            LpCutoff = Range(0.3f, 0.45f),
            LpResonance = Range(0.35f, 0.6f),
            LpSweep = 1f + Range(0.00001f, 0.000025f),
            HpCutoff = Range(0.08f, 0.16f),         // strip the mud; it is a small pop

            CrushBits = rng.Next(4, 8),
            CrushRate = rng.Next(1, 3),
            Volume = 0.55f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// Steel biting stone. Short, hard, and metallic — a square wave dropping fast
    /// through a resonant filter, with a dissonant second voice a fourth or so off it so
    /// the impact rings rather than thuds. This is the cue the whole class turns on, so
    /// it is deliberately the sharpest transient in the rig's bank.
    /// </summary>
    public static Params AnchorClank(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(620f, 880f);
        return new Params
        {
            Wave = Osc.Square,
            Length = Range(0.16f, 0.26f),

            StartFreq = baseF,
            EndFreq = baseF * Range(0.35f, 0.5f),   // metal struck, not metal sounded

            Attack = 0.005f,                        // instantaneous: it is an impact
            Sustain = Range(0.08f, 0.15f),
            Decay = Range(0.8f, 0.92f),

            Duty = Range(0.12f, 0.28f),             // thin and hard
            DutySweep = Range(-1.2f, 1.2f),

            Detune = Range(1.32f, 1.46f),           // the ring: deliberately not consonant
            DetuneGain = Range(0.4f, 0.65f),

            LpCutoff = Range(0.55f, 0.8f),
            LpResonance = Range(0.3f, 0.55f),
            HpCutoff = Range(0.06f, 0.12f),

            CrushBits = rng.Next(3, 7),             // crushed hard: it is old steel
            CrushRate = rng.Next(1, 3),
            Volume = 0.8f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// A cable retracting: a fast metallic zip climbing as the line comes in, ending on
    /// the click of the hook seating. Short — a returning cable is dead time and should
    /// not sound like an event.
    /// </summary>
    public static Params CableZip(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(700f, 1000f);
        return new Params
        {
            Wave = Osc.Saw,
            Length = Range(0.14f, 0.22f),

            StartFreq = baseF,
            EndFreq = baseF * Range(1.6f, 2.4f),    // reeling home, not paying out

            Attack = 0.02f,
            Sustain = Range(0.25f, 0.4f),
            Decay = Range(0.55f, 0.7f),

            Duty = Range(0.2f, 0.4f),
            DutySweep = Range(0.4f, 1.4f),

            LpCutoff = Range(0.4f, 0.65f),
            LpResonance = Range(0.25f, 0.5f),
            HpCutoff = Range(0.1f, 0.2f),

            CrushBits = rng.Next(3, 6),             // the mechanical rattle of the line
            CrushRate = rng.Next(2, 5),
            Volume = 0.4f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// An anchor tearing out of weak material. The one splintering sound in the bank,
    /// and the only one that <em>falls</em> in both pitch and level at once — everything
    /// else here either climbs or holds. Hard rate-crush is what does the splintering:
    /// holding each sample for several frames breaks the noise into audible grains,
    /// which is heard as material coming apart rather than as a filtered rush.
    /// </summary>
    public static Params AnchorTear(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(240f, 380f);
        return new Params
        {
            Wave = Osc.Noise,
            Length = Range(0.35f, 0.5f),

            StartFreq = baseF,
            EndFreq = baseF * Range(0.3f, 0.5f),

            Attack = 0.01f,
            Sustain = Range(0.3f, 0.45f),           // it gives way over a moment
            Decay = Range(0.5f, 0.68f),

            Detune = Range(1.5f, 1.7f),             // as wrong as this bank gets
            DetuneGain = Range(0.4f, 0.6f),

            LpCutoff = Range(0.35f, 0.55f),
            LpResonance = Range(0.4f, 0.65f),
            LpSweep = 1f - Range(0.000008f, 0.00002f),   // closing: the sound of losing it
            HpCutoff = Range(0.03f, 0.08f),

            CrushBits = rng.Next(2, 5),             // coarse — this is the splinter
            CrushRate = rng.Next(4, 9),
            Volume = 0.75f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// A rifle round. Sharp, dry and gone — the opposite of the cannon's detonation
    /// clip, which has enough body that ten of them a second stack into a solid roar.
    /// Almost all attack and decay, with the low end filtered out entirely so what is
    /// left is the crack and nothing under it.
    /// </summary>
    /// <summary>
    /// The FISH's strike, and the one cue in this game that is unashamedly a piece of
    /// <em>music</em> rather than a noise.
    ///
    /// Everything else in this bank is diegetic — machinery, weather, meat. This is a
    /// distorted guitar stab, and it earns the exception because of what the moment is: the
    /// strike is the only action in the game where the player commits everything on one
    /// press and then has a second and a half in which they cannot do anything but watch.
    /// That is a moment that wants a hook, not a report.
    ///
    /// It is built to the YM2612's own recipe, because that chip's distorted-guitar patch
    /// is the exact sound in question and it was made out of parts this synth already has:
    ///
    /// <list type="bullet">
    /// <item>A <b>saw</b>, which is the buzzy end of what an FM operator stack lands on.</item>
    /// <item>A <b>fifth</b> in the detune. This is not a tuning trick — a root and a fifth
    /// <em>is</em> a power chord, and it is the entire reason the sound reads as a guitar
    /// rather than as a loud synth note.</item>
    /// <item><b>Bit crush</b>, hard. The Mega Drive's 9-bit DAC is most of why its guitars
    /// sound like that, and the grit is not a flaw being tolerated — it is the timbre.</item>
    /// <item><b>Finger vibrato</b>: shallow, around six a second. A sustained note held
    /// dead straight reads as an organ; the wobble is what makes it a played string.</item>
    /// <item>A <b>filter snapping open</b> across the note, which is the pick attack and
    /// the sense of the body accelerating away, in one move.</item>
    /// </list>
    ///
    /// <paramref name="bass"/> renders the same riff an octave down and darker. The two are
    /// layered — guitar over bass, exactly the way the console's own soundtracks did it —
    /// because a single voice at this pitch is thin on the small speakers this game is
    /// realistically played through.
    /// </summary>
    public static Params StrikeRiff(Random rng, bool bass = false)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        // E-ish, down where a guitar's low strings live. Rolled a little each time so a
        // chain of strikes is a riff rather than the same stab on repeat.
        float baseF = Range(158f, 176f) * (bass ? 0.5f : 1f);

        return new Params
        {
            Wave = Osc.Saw,
            // Rings a little past the lunge it accompanies, so the note is still decaying
            // as the body comes out the far side of the attack.
            Length = bass ? Range(0.58f, 0.66f) : Range(0.5f, 0.58f),

            StartFreq = baseF,
            // A shallow settle rather than a dive. A big glide here would read as a siren;
            // holding near the root is what keeps it a *note*, and the small drop at the
            // end is the string relaxing off the pick.
            EndFreq = baseF * Range(0.86f, 0.92f),

            // Straight in, held, then let go. No swell at all — a guitar stab that eased
            // in would be a pad, and the whole job of this cue is to land on one frame.
            Attack = 0.004f,
            Sustain = Range(0.42f, 0.52f),
            Decay = Range(0.46f, 0.56f),

            // The power chord.
            Detune = Range(1.49f, 1.51f),
            DetuneGain = bass ? Range(0.3f, 0.4f) : Range(0.55f, 0.7f),

            // Finger vibrato, and — under it — a fast amplitude chop that gives the note
            // the growl of something mechanical driving it rather than a clean sustain.
            VibratoDepth = Range(0.010f, 0.018f),
            VibratoSpeed = Range(5.4f, 6.8f),
            TremoloDepth = bass ? Range(0.06f, 0.12f) : Range(0.12f, 0.2f),
            TremoloSpeed = Range(26f, 38f),

            // Shut at the pick and torn open across the note: the attack and the
            // acceleration in a single sweep.
            LpCutoff = bass ? Range(0.18f, 0.28f) : Range(0.3f, 0.42f),
            LpResonance = Range(0.45f, 0.68f),
            LpSweep = 1f + Range(0.00002f, 0.00004f),
            // The bass keeps every bit of its low end; the guitar has its mud stripped so
            // the two stack instead of fighting over the same octave.
            HpCutoff = bass ? 0f : Range(0.02f, 0.05f),

            // The chip. Coarse on purpose — this is the grit, not damage to it.
            CrushBits = rng.Next(4, 7),
            CrushRate = rng.Next(1, 3),

            Volume = bass ? 0.5f : 0.58f,
            Seed = rng.Next(),
        };
    }

    public static Params RifleCrack(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(900f, 1300f);
        return new Params
        {
            Wave = rng.NextDouble() < 0.5 ? Osc.Square : Osc.Noise,
            Length = Range(0.07f, 0.11f),           // over before the next round is due

            StartFreq = baseF,
            EndFreq = baseF * Range(0.25f, 0.4f),

            Attack = 0.004f,
            Sustain = Range(0.06f, 0.12f),
            Decay = Range(0.85f, 0.94f),

            Duty = Range(0.1f, 0.25f),
            DutySweep = Range(-1f, 1f),

            LpCutoff = Range(0.6f, 0.9f),
            LpResonance = Range(0.15f, 0.35f),
            HpCutoff = Range(0.12f, 0.22f),         // no body at all — just the crack

            CrushBits = rng.Next(4, 8),
            CrushRate = 1,
            Volume = 0.45f,                         // it fires six times a second
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// A rocket leaving the tube: the motor catching, then the whole thing tearing away
    /// from the player. The pitch falls while the filter opens, which is the doppler of
    /// something departing at speed — and is why it reads as leaving rather than as
    /// simply being fired.
    /// </summary>
    public static Params RocketLaunch(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(180f, 260f);
        return new Params
        {
            Wave = Osc.Noise,
            Length = Range(0.5f, 0.7f),

            StartFreq = baseF,
            EndFreq = baseF * Range(0.4f, 0.6f),    // departing

            Attack = 0.03f,                         // the motor catching
            Sustain = Range(0.2f, 0.32f),
            Decay = Range(0.6f, 0.75f),

            Detune = Range(1.01f, 1.04f),           // the motor's own roughness
            DetuneGain = Range(0.5f, 0.7f),

            LpCutoff = Range(0.12f, 0.2f),
            LpResonance = Range(0.3f, 0.5f),
            LpSweep = 1f + Range(0.000006f, 0.000016f),
            HpCutoff = Range(0.01f, 0.04f),

            CrushBits = rng.Next(4, 8),
            CrushRate = rng.Next(2, 5),
            Volume = 0.7f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The reel's gas jet — a seamless bed, because a reel lasts exactly as long as the
    /// player holds W and no one-shot envelope could ever match that. The caller drives
    /// its playback rate off the pressure left in the bottle, so a starved reel sags in
    /// rate as well as in level: the whole jet slows down rather than merely getting
    /// quieter, which is the difference between a weak push and a failing one.
    ///
    /// Filtered wide open compared with the monsters' beds. Those are machinery heard
    /// through armour; this one is a gas jet a foot from the player's own hip.
    /// </summary>
    public static Params ReelJet(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        return new Params
        {
            Loop = true,
            Wave = Osc.Noise,
            Length = Range(0.9f, 1.3f),

            // The jet pulsing rather than droning — a real gas release chugs as the
            // regulator opens and closes, and a perfectly smooth hiss reads as a synth.
            TremoloDepth = Range(0.12f, 0.24f),
            TremoloSpeed = Range(11f, 19f),

            LpCutoff = Range(0.28f, 0.42f),
            LpResonance = Range(0.25f, 0.45f),
            HpCutoff = Range(0.02f, 0.06f),

            CrushBits = rng.Next(5, 9),
            CrushRate = 1,                          // rate-crush can't be made seamless
            Volume = 0.5f,                          // the caller scales this
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// The air past the ears. Also a bed, and the loudest one in the game: on this
    /// chassis the wind <em>is</em> the speedometer, and a player crossing the city at
    /// thirty metres a second should be able to hear that with their eyes shut.
    ///
    /// Almost unfiltered — wind has no pitch and hiding it behind a low-pass just makes
    /// it a rumble. The slow tremolo is the buffet: air breaking over a body, which is
    /// what separates it from tape hiss.
    /// </summary>
    public static Params WindRush(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        return new Params
        {
            Loop = true,
            Wave = Osc.Noise,
            Length = Range(1.3f, 1.8f),             // long: a short lap would pulse

            TremoloDepth = Range(0.18f, 0.32f),     // the buffet
            TremoloSpeed = Range(1.6f, 3.4f),

            LpCutoff = Range(0.5f, 0.75f),
            LpResonance = Range(0.05f, 0.2f),       // barely resonant: it is air, not a tube
            HpCutoff = Range(0.04f, 0.09f),

            CrushBits = rng.Next(6, 10),            // lightly crushed, in keeping
            CrushRate = 1,
            Volume = 0.5f,
            Seed = rng.Next(),
        };
    }

    /// <summary>
    /// Steel taking weight — the third of the SOLDIER's beds, and the quietest. A low
    /// creak with a fine vibration hum sitting in it, which is two things at once and
    /// needs to be: the creak alone is a door, and the hum alone is a machine, but a
    /// slow wow underneath a tight high buzz is unmistakably a loaded cable.
    ///
    /// Kept well below the wind and the jet on purpose. This is a sound the player
    /// should notice they have been hearing rather than one they listen to.
    /// </summary>
    public static Params CableStrain(Random rng)
    {
        float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);

        float baseF = Range(88f, 118f);
        return new Params
        {
            Loop = true,
            Wave = Osc.Saw,
            Length = Range(1.0f, 1.4f),

            StartFreq = baseF,
            EndFreq = baseF,                        // pinned flat by Loop anyway

            // The creak: a slow, uneven bend in the pitch, as a rope under load does.
            VibratoDepth = Range(0.02f, 0.045f),
            VibratoSpeed = Range(3.5f, 7f),

            // And the vibration hum, as a fast shallow chop on the level.
            TremoloDepth = Range(0.1f, 0.2f),
            TremoloSpeed = Range(26f, 44f),

            Detune = Range(1.49f, 1.51f),           // a fifth up: metal, not a note
            DetuneGain = Range(0.25f, 0.4f),

            Duty = Range(0.25f, 0.45f),
            LpCutoff = Range(0.2f, 0.32f),
            LpResonance = Range(0.35f, 0.55f),
            HpCutoff = 0f,

            CrushBits = rng.Next(5, 9),
            CrushRate = 1,                          // rate-crush can't be made seamless
            Volume = 0.4f,
            Seed = rng.Next(),
        };
    }

    // --- .wav header helpers --------------------------------------------------

    private static void WriteTag(byte[] b, int at, string tag)
    {
        for (int i = 0; i < tag.Length; i++) b[at + i] = (byte)tag[i];
    }

    private static void WriteI32(byte[] b, int at, int v)
    {
        b[at] = (byte)v; b[at + 1] = (byte)(v >> 8);
        b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24);
    }

    private static void WriteI16(byte[] b, int at, int v)
    {
        b[at] = (byte)v; b[at + 1] = (byte)(v >> 8);
    }
}
