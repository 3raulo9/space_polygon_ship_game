using System.Numerics;
using Unrendered.Acoustics;
using Unrendered.World;

namespace Unrendered.Core;

/// <summary>
/// The sound engine's half of the self-test.
///
/// <para>Every check here runs with no audio device, no window and no assets. That is the
/// whole reason the mixer was built as arithmetic on float arrays rather than as a stack of
/// raylib calls: a claim like "a rifle fired a hundred and twenty units away and off to your
/// left comes out quieter, duller and mostly in the left speaker" is a claim about six
/// numbers, and six numbers can be asserted. The alternative — two machines, headphones and
/// an opinion — is not a test.</para>
///
/// <para>What it cannot check is whether any of it <em>sounds good</em>. That is what
/// <see cref="RenderScene"/> is for: it drives the same mixer offline and writes a .wav.</para>
/// </summary>
public static partial class SelfTest
{
    /// <summary>An engine with no device behind it, a synthetic clip and a listener at the
    /// origin facing +Z. Every check below starts here.</summary>
    private static (AudioEngine Engine, Clip Clip) Bench(int voices = 48)
    {
        var engine = new AudioEngine(CueBank.BuildTable(), voices) { WorldWrap = Torus.Size };
        return (engine, Tone(440f, 0.5f));
    }

    /// <summary>A plain sine. Predictable, so an assertion about a rendered block is an
    /// assertion about the mixer and not about whatever the synth happened to roll.</summary>
    private static Clip Tone(float hz, float seconds, float level = 0.7f)
    {
        int n = (int)(Mixer.SampleRate * seconds);
        var s = new float[n];
        for (int i = 0; i < n; i++) s[i] = MathF.Sin(2f * MathF.PI * hz * i / Mixer.SampleRate) * level;
        return AudioEngine.FromSamples(s, Mixer.SampleRate, $"tone{hz:0}");
    }

    private static Listener EarAt(Vector2 pos, float heading = 0f, float height = 1.6f)
        => new() { Position = pos, Heading = heading, Height = height };

    // --- Distance ------------------------------------------------------------------

    /// <summary>
    /// The complaint that started all of this: everything played at full volume, dead
    /// centre, as if it were an mp3. Level has to fall with range — and, more importantly,
    /// so does brightness, because the ear reads dullness as distance long before it reads
    /// quietness as distance.
    /// </summary>
    private static string? DistanceDullsAndQuietens()
    {
        var spec = CueBank.BuildTable()[(int)Cue.RifleShot];
        var ear = EarAt(Vector2.Zero);

        var near = Spatializer.Place(ear, spec, new Vector2(0f, 8f), 1.6f, 0f, Torus.Size);
        var mid = Spatializer.Place(ear, spec, new Vector2(0f, 60f), 1.6f, 0f, Torus.Size);
        var far = Spatializer.Place(ear, spec, new Vector2(0f, 120f), 1.6f, 0f, Torus.Size);

        if (!(near.Gain > mid.Gain && mid.Gain > far.Gain))
            return $"level did not fall with range: {near.Gain:0.000} / {mid.Gain:0.000} / {far.Gain:0.000}";
        if (!(near.Cutoff > mid.Cutoff && mid.Cutoff > far.Cutoff))
            return $"the top end did not go with it: {near.Cutoff:0} / {mid.Cutoff:0} / {far.Cutoff:0}";
        if (far.Cutoff > 4000f)
            return $"a shot 120 units off should be a thump, not a crack (cutoff {far.Cutoff:0}Hz)";

        // Past its reach it is silent — which is also what lets the engine refuse it a voice.
        var beyond = Spatializer.Place(ear, spec, new Vector2(0f, spec.MaxDistance + 5f), 1.6f, 0f, Torus.Size);
        if (beyond.Gain > 0f) return "a cue past its own reach was still audible";

        // ...and the room takes over as the direct sound goes. Further away is wetter.
        if (far.ReverbSend <= near.ReverbSend)
            return "distant sounds were not wetter than near ones";
        return null;
    }

    /// <summary>A sound to the right of where you are looking comes out of the right
    /// speaker, and turning your head moves it. Before the engine there was no panning in
    /// the game at all — <c>SetSoundPan</c> appeared exactly zero times.</summary>
    private static string? PanFollowsTheView()
    {
        var spec = CueBank.BuildTable()[(int)Cue.Detonation];
        var source = new Vector2(30f, 0f);      // due east

        // Facing +Z (north): east is on our right.
        var facingNorth = Spatializer.Place(EarAt(Vector2.Zero), spec, source, 1.6f, 0f, Torus.Size);
        if (facingNorth.Pan < 0.4f)
            return $"a sound due east should be well right of centre, got pan {facingNorth.Pan:0.00}";
        if (facingNorth.GainR <= facingNorth.GainL) return "the right channel was not the louder one";

        // Turn to face east and the same sound is now straight ahead.
        var facingEast = Spatializer.Place(EarAt(Vector2.Zero, MathF.PI * 0.5f), spec, source, 1.6f, 0f, Torus.Size);
        if (MathF.Abs(facingEast.Pan) > 0.15f)
            return $"turning to face it should centre it, got pan {facingEast.Pan:0.00}";

        // Turn to face south-east and it swings to our left.
        var facingSouthEast = Spatializer.Place(EarAt(Vector2.Zero, MathF.PI * 0.75f), spec, source, 1.6f, 0f, Torus.Size);
        if (facingSouthEast.Pan > -0.4f)
            return $"with it over our left shoulder the pan should invert, got {facingSouthEast.Pan:0.00}";

        // Turn our back on it and a pan can no longer say anything — behind is as centred as
        // ahead. What separates them is the head: a rear source comes through duller and a
        // little quieter, which is the only cue two speakers have for "that is behind you".
        var facingWest = Spatializer.Place(EarAt(Vector2.Zero, -MathF.PI * 0.5f), spec, source, 1.6f, 0f, Torus.Size);
        if (MathF.Abs(facingWest.Pan) > 0.15f) return "a sound directly behind should be centred";
        if (facingWest.Cutoff >= facingEast.Cutoff * 0.8f)
            return $"a sound behind you should be duller than the same one ahead: "
                 + $"{facingWest.Cutoff:0}Hz behind vs {facingEast.Cutoff:0}Hz ahead";
        if (facingWest.Gain >= facingEast.Gain)
            return "a sound behind you should be a shade quieter than the same one ahead";

        // Equal power: the total does not dip as a sound sweeps across the front.
        float energy = facingEast.GainL * facingEast.GainL + facingEast.GainR * facingEast.GainR;
        float sideEnergy = facingNorth.GainL * facingNorth.GainL + facingNorth.GainR * facingNorth.GainR;
        if (MathF.Abs(energy - sideEnergy) > 0.02f)
            return "panning across the front changed the total level — the law is not equal-power";

        // And distance blurs direction: the same angle much further off is less pinned.
        var farEast = Spatializer.Place(EarAt(Vector2.Zero), spec, new Vector2(130f, 0f), 1.6f, 0f, Torus.Size);
        if (farEast.Pan >= facingNorth.Pan)
            return "a distant sound should be more diffuse than a near one at the same angle";
        return null;
    }

    /// <summary>
    /// A tower between you and a gunshot muffles it. This is measured against the real city
    /// through <see cref="World.World.MeasureOcclusion"/>, not a stub — the point of the
    /// check is that the world's own geometry answers the question.
    /// </summary>
    private static string? TheCityMufflesWhatIsBehindIt()
    {
        var world = new World.World();

        // Specifically a tower. An arch is two thin legs eighteen units apart, so a line
        // through the middle of one passes between them and is honestly not blocked — which
        // is correct behaviour and a useless thing to build the check on.
        Structure? tower = null;
        foreach (var s in world.Structures)
            if (s.Kind == StructureKind.Tower) { tower = s; break; }
        if (tower == null) return "the city has no towers to hide behind";

        // Stand on one side of it and put the sound on the other, on the line through its
        // middle, close to the ground so nothing clears the roof.
        Vector2 dir = new(1f, 0f);
        Vector2 ear = Torus.Wrap(tower.Position - dir * 30f);
        Vector2 shot = Torus.Wrap(tower.Position + dir * 30f);

        float through = world.MeasureOcclusion(ear, 1.6f, shot, 1.6f);
        if (through < 0.4f)
            return $"a shot straight through a tower should be well blocked, got {through:0.00}";

        // Step the sound round to the same range with nothing in the way.
        Vector2 clear = Torus.Wrap(tower.Position + new Vector2(0f, 1f) * 90f);
        float around = world.MeasureOcclusion(ear, 1.6f, clear, 1.6f);
        if (around > 0.35f)
            return $"an unobstructed line should be mostly clear, got {around:0.00}";

        // Up on the skyline it is clear again — the line passes over the parapet, which is
        // why a soldier on a roof hears the fight a tank in the street cannot.
        float overhead = world.MeasureOcclusion(ear, 200f, shot, 200f);
        if (overhead > 0.05f) return $"a line above the roofs should not be blocked, got {overhead:0.00}";

        // And the muffling has to actually reach the mix.
        var spec = CueBank.BuildTable()[(int)Cue.RifleShot];
        var open = Spatializer.Place(EarAt(ear), spec, shot, 1.6f, 0f, Torus.Size);
        var walled = Spatializer.Place(EarAt(ear), spec, shot, 1.6f, through, Torus.Size);
        if (walled.Gain >= open.Gain) return "occlusion did not lower the level";
        if (walled.Cutoff >= open.Cutoff * 0.8f)
            return $"occlusion barely dulled it: {open.Cutoff:0}Hz clear vs {walled.Cutoff:0}Hz walled";
        if (walled.ReverbSend <= open.ReverbSend)
            return "what reaches you round a corner should be wetter, not drier";
        return null;
    }

    /// <summary>A blast on the far side of the arena is heard well after it is seen. Sound
    /// travels; at 340 units a second a detonation 200 units off is most of a second late.</summary>
    private static string? SoundTakesTimeToArrive()
    {
        var table = CueBank.BuildTable();
        var ear = EarAt(Vector2.Zero);

        var far = Spatializer.Place(ear, table[(int)Cue.ExplosionAt], new Vector2(0f, 200f), 1f, 0f, Torus.Size);
        float expected = 200f / Spatializer.SpeedOfSound;
        if (MathF.Abs(far.DelaySeconds - expected) > 0.01f)
            return $"expected ~{expected:0.000}s of travel, got {far.DelaySeconds:0.000}s";

        var close = Spatializer.Place(ear, table[(int)Cue.ExplosionAt], new Vector2(0f, 10f), 1f, 0f, Torus.Size);
        if (close.DelaySeconds > 0.05f) return "a blast beside you should not be late";

        // Only the cues that opted in travel. A rifle crack arriving late would read as a
        // bug, not as physics.
        var rifle = Spatializer.Place(ear, table[(int)Cue.RifleShot], new Vector2(0f, 100f), 1f, 0f, Torus.Size);
        if (rifle.DelaySeconds != 0f) return "a cue with no travel delay was delayed anyway";

        // And the delay has to be honoured by the mixer, not just reported: the voice must
        // still be silent in the block the sound was fired in.
        var (engine, clip) = Bench();
        engine.Update(ear, 1f / 60f);
        engine.Play((int)Cue.ExplosionAt, clip, new Vector2(0f, 200f));
        var block = new float[512 * 2];
        engine.RenderOffline(block, 512);
        foreach (float f in block) if (f != 0f) return "a travelling sound was heard before it arrived";
        return null;
    }

    // --- The voice pool ------------------------------------------------------------

    /// <summary>Twenty players on full auto is the case that decides whether a firefight is
    /// legible or a wall of mush. Each cue carries its own cap.</summary>
    private static string? InstanceCapsHoldTheLine()
    {
        var (engine, clip) = Bench();
        engine.Update(EarAt(Vector2.Zero), 1f / 60f);

        byte cap = engine.Specs[(int)Cue.RifleShot].MaxInstances;
        // Twenty rifles, all at the same range so none can out-shout another.
        for (int i = 0; i < 20; i++)
            engine.Play((int)Cue.RifleShot, clip, new Vector2(i * 0.1f, 20f));

        if (engine.ActiveVoices > cap)
            return $"cap is {cap} but {engine.ActiveVoices} rifle voices are sounding";
        if (engine.ActiveVoices == 0) return "the cap silenced the cue entirely";

        // A different cue has its own budget and is not crowded out by the rifles.
        int boom = engine.Play((int)Cue.Explosion, clip, new Vector2(0f, 20f));
        if (boom == 0) return "an explosion could not be heard through capped rifle fire";
        return null;
    }

    /// <summary>At the cap, the shot beside you displaces the one across the road — never
    /// the other way round.</summary>
    private static string? LoudNearbyBeatsQuietFarOff()
    {
        var (engine, clip) = Bench();
        engine.Update(EarAt(Vector2.Zero), 1f / 60f);

        byte cap = engine.Specs[(int)Cue.RifleShot].MaxInstances;
        for (int i = 0; i < cap; i++)
            engine.Play((int)Cue.RifleShot, clip, new Vector2(0f, 110f));   // all far off

        int near = engine.Play((int)Cue.RifleShot, clip, new Vector2(0f, 6f));
        if (near == 0) return "a rifle beside the player was refused in favour of distant ones";

        // And the reverse: with the cap full of close shots, one across the map is dropped.
        var (e2, c2) = Bench();
        e2.Update(EarAt(Vector2.Zero), 1f / 60f);
        for (int i = 0; i < cap; i++) e2.Play((int)Cue.RifleShot, c2, new Vector2(0f, 6f));
        if (e2.Play((int)Cue.RifleShot, c2, new Vector2(0f, 115f)) != 0)
            return "a distant shot cut off a close one";
        return null;
    }

    /// <summary>A boss coming apart is the loudest thing in the game and must never be
    /// dropped, however busy the field is.</summary>
    private static string? PriorityProtectsTheBigMoments()
    {
        // A deliberately tiny pool, so it is full after a handful of cues.
        var (engine, clip) = Bench(voices: 6);
        engine.Update(EarAt(Vector2.Zero), 1f / 60f);

        for (int i = 0; i < 12; i++)
            engine.Play((int)Cue.Footstep, clip, new Vector2(i, 30f));

        int death = engine.Play((int)Cue.BossDeath, clip, new Vector2(0f, 40f));
        if (death == 0) return "a boss death was dropped for a pile of footsteps";

        // ...and now the reverse. With the pool full of the loudest cue in the game, a
        // footstep is simply not heard rather than stealing one of them.
        var (e2, c2) = Bench(voices: 4);
        e2.Update(EarAt(Vector2.Zero), 1f / 60f);
        for (int i = 0; i < 4; i++) e2.Play((int)Cue.BossDeath, c2, new Vector2(i, 30f));
        if (e2.Play((int)Cue.Footstep, c2, new Vector2(0f, 12f)) != 0)
            return "a footstep stole a voice from a boss death";
        return null;
    }

    /// <summary>Three feet of a walking boss land on the same tick, twenty players fire at
    /// once, and a death cascade runs under all of it. The old bank simply clipped; this has
    /// to duck instead of tearing.</summary>
    private static string? TheLimiterHoldsTheCeiling()
    {
        var (engine, _) = Bench();
        var loud = Tone(180f, 1f, level: 1f);
        engine.Update(EarAt(Vector2.Zero), 1f / 60f);

        // Everything the game can throw at one moment, all on top of the listener.
        for (int i = 0; i < 8; i++) engine.Play((int)Cue.Footstep, loud, new Vector2(0f, 2f));
        for (int i = 0; i < 4; i++) engine.Play((int)Cue.Explosion, loud, new Vector2(0f, 2f));
        for (int i = 0; i < 4; i++) engine.Play((int)Cue.BossDeath, loud, new Vector2(0f, 2f));

        var block = new float[2048 * 2];
        float peak = 0f;
        bool heard = false;
        for (int b = 0; b < 8; b++)
        {
            engine.RenderOffline(block, 2048);
            foreach (float f in block)
            {
                float a = MathF.Abs(f);
                if (a > peak) peak = a;
                if (a > 0.05f) heard = true;
            }
        }

        if (!heard) return "the whole pile-up rendered silence";
        if (peak > 1.0001f) return $"the mix left the rails at {peak:0.000}";
        return null;
    }

    // --- The state of your ears ----------------------------------------------------

    /// <summary>A blast close enough drops the world away, slams a low-pass over it and
    /// leaves a tone ringing. Far enough off and it does none of that — which is the whole
    /// point: being caught in something has to be different from watching it happen.</summary>
    private static string? ConcussionDucksAndDulls()
    {
        var mix = new Mixer();
        var room = new Ambience();

        room.Apply(mix, 0f);
        float openGain = mix.MasterTarget, openCutoff = mix.MasterCutoffTarget;

        room.Blast(distance: 60f);                     // across the street
        room.Apply(mix, 0f);
        if (mix.MasterTarget < openGain - 0.001f) return "a blast 60 units away should not deafen anybody";

        room.Blast(distance: 2f);                      // in your face
        room.Apply(mix, 0f);
        if (mix.MasterTarget >= openGain - 0.2f)
            return $"a blast at point-blank barely ducked the mix ({mix.MasterTarget:0.00} vs {openGain:0.00})";
        if (mix.MasterCutoffTarget >= openCutoff * 0.2f)
            return $"the world should go dull as well as quiet, got {mix.MasterCutoffTarget:0}Hz";
        if (mix.RingTarget <= 0f) return "nothing was left ringing";

        // And it wears off rather than latching.
        for (int i = 0; i < 400; i++) room.Apply(mix, 1f / 60f);
        if (room.Concussion > 0.001f) return "the concussion never recovered";
        if (mix.MasterTarget < openGain - 0.001f) return "the mix stayed ducked after recovery";

        // Being swallowed is the same three knobs with different numbers.
        room.Where = Interior.Swallowed;
        room.Apply(mix, 0f);
        if (mix.MasterCutoffTarget > 1000f) return "inside the Maw the world should be drowned, not merely quieter";
        if (mix.WetTarget < 0.5f) return "the throat should be an enormous wet space";
        return null;
    }

    /// <summary>
    /// When your revives run out you ride a team-mate's shoulder — and your ears have to go
    /// with you. This was a real leak: several attenuations measured to
    /// <c>Players[LocalIndex]</c>, so a spectator heard the fight from their own wreck.
    /// </summary>
    private static string? EarsRideTheCamera()
    {
        var world = new World.World(null, new MatchSettings { MaxPlayers = 4, Revives = 0 })
        { DynamicSpawning = false };
        world.Enemies.Clear();
        world.AddPlayer(new Loadout { Class = PlayerClass.Tank });   // seat 1
        world.LocalIndex = 0;
        var mate = world.Players[1];

        // Opposite corners of the torus, not opposite edges — the wrap makes edges close.
        world.Player.Position = new Vector2(0f, 0f);
        mate.Position = new Vector2(195f, 195f);

        world.StepForTest(1f / 60f);
        if (!ReferenceEquals(world.Eye, world.Player)) return "the camera did not start on our own craft";

        // Spend our lives. The spectator picker should hand us the survivor.
        world.Player.Shield = 0f;
        world.Player.Health = 0f;
        world.Player.Lives = 0;
        world.StepForTest(1f / 60f);

        if (!world.Spectating) return "a spent player was not made a spectator";
        if (!ReferenceEquals(world.Eye, mate)) return "the camera did not move to the survivor";

        // A sound at the team-mate's feet is now the near one, and one at our corpse is far.
        var spec = CueBank.BuildTable()[(int)Cue.Detonation];
        var ear = EarAt(world.Eye.Position);
        var atMate = Spatializer.Place(ear, spec, mate.Position, 1.6f, 0f, Torus.Size);
        var atCorpse = Spatializer.Place(ear, spec, world.Player.Position, 1.6f, 0f, Torus.Size);
        if (atMate.Gain <= atCorpse.Gain)
            return "a spectator still heard from their own wreck rather than from the craft they are watching";
        return null;
    }

    /// <summary>The world wraps, so a sound just over the seam is beside you rather than
    /// four hundred units away. Everything else in the sim measures the short way round;
    /// the ears have to as well.</summary>
    /// <summary>
    /// The hand that takes the world away at the end of a run. Two claims, and the second is
    /// the one worth the test: wound to zero it silences everything the world makes, and it
    /// leaves the UI bus alone, so the ending screen the player is being handed still answers
    /// their keys. Fading the menu out with the world would leave them pressing buttons at a
    /// screen that made no sound, which reads as a hung game rather than as a quiet one.
    /// </summary>
    private static string? WorldFadeSilencesTheWorldNotTheUi()
    {
        const float Dt = 1f / 60f;
        var block = new float[AudioEngine.BufferFrames * 2];

        // How loud a thing is over a second of rendering, with the fade at a given level.
        float Peak(Cue cue, float fade, bool spatial)
        {
            var (engine, clip) = Bench();
            engine.Mix.WorldFade = fade;          // no ease: this is the level under test
            engine.Mix.WorldFadeTarget = fade;
            var ear = EarAt(Vector2.Zero);
            engine.Update(ear, Dt);

            float peak = 0f;
            for (int frame = 0; frame < 60; frame++)
            {
                if (frame % 12 == 0)
                {
                    if (spatial) engine.Play((int)cue, clip, new Vector2(0f, 6f));
                    else engine.PlayFlat((int)cue, clip);
                }
                engine.Update(ear, Dt);
                engine.RenderOffline(block, AudioEngine.BufferFrames);
                foreach (float f in block) { float a = MathF.Abs(f); if (a > peak) peak = a; }
            }
            return peak;
        }

        // A gun going off out in the world.
        float worldOpen = Peak(Cue.Detonation, 1f, spatial: true);
        if (worldOpen <= 0.01f) return "the test failed to make any world noise at all";
        float worldFaded = Peak(Cue.Detonation, 0f, spatial: true);
        if (worldFaded > 0.001f)
            return $"the world was still audible with the fade shut ({worldFaded:0.0000})";

        // Half way down is half way down, not a switch.
        float worldHalf = Peak(Cue.Detonation, 0.5f, spatial: true);
        if (!(worldHalf < worldOpen * 0.85f && worldHalf > worldOpen * 0.2f))
            return $"a half fade did not read as half a world ({worldHalf:0.000} of {worldOpen:0.000})";

        // ...and the panel keeps its voice through all of it.
        float uiOpen = Peak(Cue.Pickup, 1f, spatial: false);
        if (uiOpen <= 0.01f) return "the test failed to make any menu noise at all";
        float uiFaded = Peak(Cue.Pickup, 0f, spatial: false);
        if (uiFaded < uiOpen * 0.95f)
            return $"the fade took the menu's voice with the world's ({uiFaded:0.000} of {uiOpen:0.000})";
        return null;
    }

    private static string? TheTorusDoesNotBreakTheEars()
    {
        var spec = CueBank.BuildTable()[(int)Cue.Detonation];
        var ear = EarAt(new Vector2(195f, 0f));                 // just inside the east edge
        var justOver = new Vector2(-195f, 0f);                  // ten units away, over the seam

        var wrapped = Spatializer.Place(ear, spec, justOver, 1.6f, 0f, Torus.Size);
        if (wrapped.Gain <= 0f) return "a sound ten units away across the seam was inaudible";
        if (wrapped.Distance > 15f) return $"measured the long way round: {wrapped.Distance:0} units";
        if (wrapped.Pan <= 0.3f) return "a sound over the eastern seam should still be on our right";

        // On a flat world the same pair really is a world apart — the wrap is a choice.
        var flat = Spatializer.Place(ear, spec, justOver, 1.6f, 0f, wrap: 0f);
        if (flat.Gain > 0f) return "with the wrap off it should have been far out of range";
        return null;
    }

    // --- The listening harness -----------------------------------------------------

    /// <summary>
    /// Renders a scripted scene straight to a stereo .wav, with no audio device involved.
    ///
    /// <para>This is the answer to the part no assertion can reach. The checks above prove
    /// the numbers are right; this is how you find out whether the result is any
    /// <em>good</em> — a rifle walking past you from left to right, a firefight receding
    /// into the distance, stepping behind a tower, a blast in your face. Play the file.</para>
    ///
    /// <para>Run with <c>dotnet run -- --audioscene</c>. Writes next to the executable.</para>
    /// </summary>
    public static int RenderScene(string path = "scene.wav")
    {
        var engine = new AudioEngine(CueBank.BuildTable()) { WorldWrap = Torus.Size };
        var world = new World.World();

        // The real city answers the occlusion questions, so the walk-behind-a-tower beat
        // below is the actual skyline getting in the way rather than a scripted dip.
        engine.OcclusionProbe = world.MeasureOcclusion;

        var crack = Tone(1400f, 0.12f);       // stands in for a rifle
        var boom = Tone(70f, 1.4f);           // ...and for something heavy
        var bed = Tone(110f, 1f, 0.5f);       // a monster's rotor

        var samples = new List<float>();
        var block = new float[AudioEngine.BufferFrames * 2];
        float t = 0f;
        const float Dt = 1f / 60f;

        // Stand next to a tower so the fourth beat has something to hide behind.
        Vector2 home = Torus.Wrap(world.Structures[0].Position + new Vector2(-26f, 0f));
        int bedHandle = 0;
        float heading = 0f;

        void Frame(Listener ear)
        {
            engine.Update(ear, Dt);
            engine.RenderOffline(block, AudioEngine.BufferFrames);
            samples.AddRange(block);
            t += Dt;
        }

        // 1. A shot walking past, left to right, close. Should sweep across the head.
        for (int i = 0; i < 180; i++)
        {
            if (i % 12 == 0)
                engine.Play((int)Cue.RifleShot, crack, home + new Vector2(-24f + i * 0.26f, 6f));
            Frame(EarAt(home));
        }

        // 2. The same shots receding. Should get quieter AND duller AND wetter.
        for (int i = 0; i < 220; i++)
        {
            if (i % 14 == 0)
                engine.Play((int)Cue.RifleShot, crack, home + new Vector2(8f, 10f + i * 0.6f));
            Frame(EarAt(home));
        }

        // 3. Turning on the spot with a gun firing due east — the sound should swing round.
        for (int i = 0; i < 240; i++)
        {
            heading += Dt * 1.4f;
            if (i % 10 == 0) engine.Play((int)Cue.Detonation, crack, home + new Vector2(28f, 0f));
            Frame(EarAt(home, heading));
        }

        // 4. Walking behind the tower while a firefight continues on the far side of it.
        Vector2 fight = Torus.Wrap(world.Structures[0].Position + new Vector2(30f, 0f));
        for (int i = 0; i < 300; i++)
        {
            Vector2 walk = Torus.Wrap(home + new Vector2(0f, -20f + i * 0.16f));
            if (i % 9 == 0) engine.Play((int)Cue.RifleShot, crack, fight);
            Frame(EarAt(walk));
        }

        // 5. A rotor bed circling the listener, so a long-lived voice is heard being moved.
        bedHandle = engine.StartBed((int)Cue.BossHum, bed, home + new Vector2(0f, 30f));
        for (int i = 0; i < 360; i++)
        {
            float a = i * Dt * 1.1f;
            engine.SetBed(bedHandle, home + new Vector2(MathF.Sin(a) * 26f, MathF.Cos(a) * 26f),
                0f, 1f + 0.4f * MathF.Sin(a * 0.5f), 1f);
            Frame(EarAt(home));
        }
        engine.StopBed(bedHandle);

        // 6. A blast in the face, then the recovery — with a rifle firing throughout, so the
        //    ringing and the muffling are audible against something familiar.
        engine.Env.Blast(1f);
        for (int i = 0; i < 420; i++)
        {
            if (i % 20 == 0) engine.Play((int)Cue.RifleShot, crack, home + new Vector2(6f, 8f));
            Frame(EarAt(home));
        }

        // 7. Swallowed: the world drowned, then let go.
        engine.Env.Where = Interior.Swallowed;
        for (int i = 0; i < 180; i++)
        {
            if (i % 16 == 0) engine.Play((int)Cue.Explosion, boom, home + new Vector2(20f, 20f));
            Frame(EarAt(home));
        }
        engine.Env.Where = Interior.Open;
        for (int i = 0; i < 120; i++) Frame(EarAt(home));

        WriteWav(path, samples, Mixer.SampleRate, Mixer.Channels);
        Console.WriteLine($"AUDIOSCENE: wrote {path} — {t:0.0}s, {samples.Count / 2} frames");
        return 0;
    }

    /// <summary>A 16-bit PCM stereo .wav, written by hand. The engine works in float and
    /// there is no reason to drag an encoder in for a diagnostic.</summary>
    private static void WriteWav(string path, List<float> interleaved, int rate, int channels)
    {
        int frames = interleaved.Count / channels;
        int dataBytes = frames * channels * 2;
        using var f = new BinaryWriter(File.Create(path));
        f.Write("RIFF"u8.ToArray());
        f.Write(36 + dataBytes);
        f.Write("WAVE"u8.ToArray());
        f.Write("fmt "u8.ToArray());
        f.Write(16);
        f.Write((short)1);                       // PCM
        f.Write((short)channels);
        f.Write(rate);
        f.Write(rate * channels * 2);            // byte rate
        f.Write((short)(channels * 2));          // block align
        f.Write((short)16);
        f.Write("data"u8.ToArray());
        f.Write(dataBytes);
        foreach (float s in interleaved)
            f.Write((short)(Math.Clamp(s, -1f, 1f) * 32767f));
    }
}
