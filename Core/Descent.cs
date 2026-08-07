using Unrendered.Entities;

namespace Unrendered.Core;

/// <summary>
/// Which of the two things a boss fight can leave behind. Fifty-fifty, every time, with no
/// pity and no memory — a run can genuinely end with five suns, and the fact that it can is
/// what makes a run that ends with three and two feel like it went somewhere.
/// </summary>
public enum Fragment : byte
{
    None = 0,
    Sun = 1,
    Moon = 2,
}

/// <summary>
/// Where a descent currently is. The order is the run: you land, you survive four crowds each
/// closed out by a herald, and then the thing at the bottom of the planet stands up.
/// </summary>
public enum DescentPhase : byte
{
    Landing,       // the drop. Nothing hostile yet; the field is being read out
    Wave,          // a crowd, counted down by the wave bar
    Herald,        // the crowd is spent and the thing that was leading it steps forward
    Intermission,  // salvage, repair, craft — and hold READY to cut it short
    Colossus,      // the fifth wave is not a crowd
    Cleared,
    Lost,
}

/// <summary>
/// What the director needs the world to actually do. The director decides <em>what</em> the
/// planet does to you and when; the world knows how to put a hunter on the grid.
///
/// This interface exists so the whole of a descent — five waves, four heralds, the Colossus,
/// the fragments, the intermissions — can be run in a headless self-test against a stub, at
/// whatever timescale, with no world, no renderer and no audio anywhere near it. A director
/// that could only be exercised by playing the game is a director nobody will ever change.
/// </summary>
public interface IDescentField
{
    /// <summary>Puts one hunter on the field out in the fog, and counts it against the wave.</summary>
    void SpawnWaveHunter(bool elite);

    /// <summary>Puts a squad of four on a tower. Costs four units of the wave's roster,
    /// because it is four people and the wave bar has to say so.</summary>
    void SpawnWaveSquad();

    /// <summary>Raises the rolled boss out of the fog.</summary>
    void RaiseBoss(BossGenome gene);

    /// <summary>How many of this wave's units are still standing on the field right now —
    /// hunters plus individual squad members. The director tops up against this.</summary>
    int LiveWaveUnits { get; }

    /// <summary>Whether the boss it raised is still fighting.</summary>
    bool BossAlive { get; }

    /// <summary>Scatters salvage for an intermission. The break is not empty time.</summary>
    void DropSalvage(int count);

    /// <summary>Clears whatever the wave left lying about before the next phase opens — a
    /// stray hunter that wandered off during a herald fight must not follow the run into the
    /// break and keep plinking at people who are trying to craft.</summary>
    void SweepStragglers();

    /// <summary>Prints one line across the middle of the screen. The run's whole narration.</summary>
    void Announce(string line);

    /// <summary>Sounds one cue at the listener, unpositioned — the run's own noises, which are
    /// not coming from anywhere in the world.</summary>
    void Signal(Cue id, float param = 0f);
}

/// <summary>
/// DESCENT: the mode with an end. A planet is a contract — you choose it at the chart, you are
/// held to it for the session, and it takes five waves off you before it lets go.
///
/// <para><b>Nothing here drips.</b> SANDBOX's director tops the field up forever against a
/// population ceiling; this one hands out an exact roster and then stops. A DESCENT wave is a
/// countable number of things that are all going to die, which is the entire reason the wave
/// bar can exist at all — you cannot draw a progress bar over a stream.</para>
///
/// <para>The five fights each take a different one of the five lineages, shuffled per session
/// (<see cref="BossGen.ShuffledLineages"/>), so a run meets all five and never in the same
/// order. What each of them actually <em>is</em> is rolled fresh from the run's seed, so the
/// STALKER you fight on wave two has essentially nothing in common with the last one you met
/// beyond the family it came from.</para>
/// </summary>
public sealed class Descent
{
    /// <summary>The roster of each crowd, in order. These are the numbers the mode was asked
    /// for, and they hold: ten to teach you the loop, fifty to test whether you learned it.</summary>
    public static readonly int[] WaveSizes = { 10, 20, 35, 50 };

    /// <summary>How many of a wave may stand on the field at once. A wave of fifty is not fifty
    /// hunters in view — it is fifty hunters' worth of fighting, fed in against this cap so the
    /// pressure is constant and the frame rate survives it. The cap climbing across the run is
    /// what makes wave four feel like a different game from wave one rather than a longer one.</summary>
    public static readonly int[] LiveCaps = { 5, 7, 9, 11 };

    /// <summary>What share of each wave arrives as elites. Wave four is nearly half.</summary>
    public static readonly float[] EliteShare = { 0.08f, 0.20f, 0.34f, 0.46f };

    /// <summary>And which waves are allowed to send squads — people on cables, four at a time.
    /// Held back until the third so the mode's opening is legible.</summary>
    public static readonly bool[] SquadsAllowed = { false, false, true, true };

    public const int WaveCount = 5;              // four crowds and the thing at the bottom
    public const int HeraldWaves = 4;

    /// <summary>
    /// The break between waves. Ninety seconds rather than the three minutes the mode was first
    /// sketched with, and skippable — four full three-minute holds is twelve minutes of a
    /// half-hour run spent standing still, which is not pacing, it is waiting. What is here
    /// instead is ninety seconds that has salvage in it and a way to leave early.
    /// </summary>
    public const float IntermissionLength = 90f;

    /// <summary>How long READY has to be held to cut the break short. Long enough that nobody
    /// does it by accident on the frame the boss dies.</summary>
    public const float ReadyHold = 1.4f;

    /// <summary>The drop. A few seconds of standing on a hostile planet with nothing coming yet,
    /// which is the only quiet this mode offers that is not earned.</summary>
    public const float LandingLength = 7f;

    /// <summary>The beat between a boss dying and the next phase opening, so a kill has room to
    /// land — the corpse comes apart, the fragment rises, and nothing is shooting at anybody.</summary>
    public const float AfterBossPause = 4.5f;

    /// <summary>How fast a wave feeds in when it is under its live cap. Not instant: a crowd
    /// that materialises whole is a wall, and one that arrives is a fight.</summary>
    private const float FeedInterval = 1.15f;

    // --- State ---------------------------------------------------------------------

    public DescentPhase Phase { get; private set; } = DescentPhase.Landing;

    /// <summary>Which fight the run is on, 1..5. Wave five is the Colossus.</summary>
    public int Wave { get; private set; } = 1;

    /// <summary>The planet this run is bound to. Set once, at the drop, and never again — the
    /// lock is the mode's premise, so it lives here as a readonly rather than as a rule
    /// somebody has to remember to enforce.</summary>
    public readonly PlanetId Destination;

    /// <summary>The run's seed. Everything rolled in this session comes off it, so an entire
    /// descent — five bosses, their bodies, their moves, their names — is reproducible from one
    /// integer. That is what makes a rolled encounter debuggable.</summary>
    public readonly int Seed;

    private readonly BossLineage[] _order;

    /// <summary>
    /// The session this planet is one leg of, or null for a run standing on its own (a test, a
    /// capture, the very first world before anybody has been anywhere).
    ///
    /// <para>The one thing the director takes from it is <see cref="Difficulty"/>: with a
    /// campaign behind it, a run's boss roller reads how far into the <em>whole crossing</em>
    /// it is rather than how far into this planet, which is what stops the fourth world playing
    /// exactly like the first.</para>
    /// </summary>
    public Campaign? Session { get; init; }

    /// <summary>Which leg of the crossing this is. 0 for the first world.</summary>
    public int Hop { get; init; }

    /// <summary>How many of this wave's roster have not been sent in yet.</summary>
    public int Reserve { get; private set; }

    /// <summary>How many of this wave's roster are dead. The wave bar is this over
    /// <see cref="WaveTotal"/>.</summary>
    public int Killed { get; private set; }

    /// <summary>The size of the wave currently being fought.</summary>
    public int WaveTotal { get; private set; }

    /// <summary>1 at the top of a wave, 0 when the last one falls. Exactly the bar the mode was
    /// asked for: full at twenty of twenty, gone at the last enemy.</summary>
    public float WaveFraction => WaveTotal <= 0 ? 0f
        : Math.Clamp(1f - Killed / (float)WaveTotal, 0f, 1f);

    /// <summary>Seconds left on whatever is being waited out — the landing, the break, the
    /// pause after a kill.</summary>
    public float Clock { get; private set; }

    /// <summary>0..1 of the READY hold. Drawn as a filling ring, so a room can see each other
    /// committing to skip the break.</summary>
    public float Ready { get; private set; }

    /// <summary>The boss currently up, or the one about to be. Null between fights.</summary>
    public BossGenome? BossGene { get; private set; }

    /// <summary>
    /// How many fragments this planet has given up so far, 0..5. The director counts what it
    /// has <em>dropped</em>, and nothing else — not who has them, not where they are.
    ///
    /// <para>That used to be the opposite way round. A fragment was a pure tag awarded to the
    /// seat that landed the killing blow, and this class held two per-seat counters that were
    /// the only record of it anywhere. Fragments are objects now: they fall at the corpse, they
    /// lie there, they are carried in packs, they are dropped when a carrier dies and they are
    /// spent into an arch. So the truth about who has what lives in the packs, which is the
    /// only place that can survive a fragment changing hands — see
    /// <c>World.FragmentsOf</c>.</para>
    /// </summary>
    public int Dropped { get; private set; }

    /// <summary>The last fragment rolled, so the world can put the right rock on the ground and
    /// the HUD can name it. Cleared once read.</summary>
    public Fragment PendingFragment { get; private set; }

    private float _feed;
    private bool _readyLatched;

    public Descent(PlanetId destination, int? seed = null)
    {
        Destination = destination;
        Seed = seed ?? Random.Shared.Next();
        _order = BossGen.ShuffledLineages(Seed);
        Clock = LandingLength;
    }

    /// <summary>Which lineage fight <paramref name="wave"/> (1-based) draws. Public so the HUD
    /// can foreshadow the Colossus's family on the last intermission, which is a genuinely
    /// useful thing to know while you are deciding what to craft.</summary>
    public BossLineage LineageOf(int wave)
        => _order[Math.Clamp(wave - 1, 0, _order.Length - 1)];

    /// <summary>How deep into the run this is, 0..1. Feeds the boss roller, which leans its
    /// continuous traits on it — a late boss is bigger and pushier, though never differently
    /// built.
    ///
    /// <para>With a <see cref="Session"/> behind it this is the position in the whole crossing
    /// rather than in this planet, so a herald on the fourth world outclasses the Colossus on
    /// the first. Without one — a lone run, a test, a capture — it is the old per-planet curve,
    /// unchanged, which is what keeps every existing boss-roll hatch reproducing what it
    /// always did.</para></summary>
    private float Difficulty => Session is { } c
        ? c.DifficultyAt(Hop, Wave, WaveCount)
        : Math.Clamp((Wave - 1) / (float)(WaveCount - 1), 0f, 1f);

    /// <summary>
    /// One tick of the run. <paramref name="readyHeld"/> is whether anybody is holding the
    /// READY key this frame, which only matters during an intermission.
    /// </summary>
    public void Update(float dt, IDescentField field, bool readyHeld = false)
    {
        switch (Phase)
        {
            case DescentPhase.Landing: TickLanding(dt, field); break;
            case DescentPhase.Wave: TickWave(dt, field); break;
            case DescentPhase.Herald:
            case DescentPhase.Colossus: TickBoss(dt, field); break;
            case DescentPhase.Intermission: TickIntermission(dt, field, readyHeld); break;
        }
    }

    private void TickLanding(float dt, IDescentField field)
    {
        Clock -= dt;
        if (Clock > 0f) return;
        OpenWave(field);
    }

    /// <summary>
    /// Opens a crowd. Wave five is not a crowd, so it goes straight to the thing at the bottom
    /// of the planet — the one place in the run where a wave has no roster at all.
    /// </summary>
    private void OpenWave(IDescentField field)
    {
        if (Wave > HeraldWaves)
        {
            OpenBoss(field, colossus: true);
            return;
        }

        int i = Wave - 1;
        WaveTotal = WaveSizes[i];
        Reserve = WaveTotal;
        Killed = 0;
        _feed = 0f;
        Phase = DescentPhase.Wave;
        field.Announce($"WAVE {Wave} OF {WaveCount}   {WaveTotal} CONTACTS");
        field.Signal(Cue.Alarm);
    }

    private void TickWave(float dt, IDescentField field)
    {
        int cap = LiveCaps[Math.Clamp(Wave - 1, 0, LiveCaps.Length - 1)];

        // Feed the crowd in against the live cap. Everything that is still in the reserve is
        // still counted by the bar — the bar is the whole wave, not the part of it you can
        // currently see, which is what makes it a promise rather than a readout.
        _feed -= dt;
        if (Reserve > 0 && field.LiveWaveUnits < cap && _feed <= 0f)
        {
            _feed = FeedInterval;
            int i = Math.Clamp(Wave - 1, 0, WaveSizes.Length - 1);

            // A squad is four bodies and costs four units, so a wave that sends one is
            // genuinely four contacts closer to being over.
            if (SquadsAllowed[i] && Reserve >= 4 && Random.Shared.NextSingle() < 0.22f)
            {
                field.SpawnWaveSquad();
                Reserve -= 4;
            }
            else
            {
                field.SpawnWaveHunter(Random.Shared.NextSingle() < EliteShare[i]);
                Reserve--;
            }
        }

        // The crowd is over when the roster is spent and the field is clear of it. Not when the
        // reserve empties — the last hunter fed in is still a hunter.
        if (Reserve <= 0 && field.LiveWaveUnits <= 0)
            OpenBoss(field, colossus: false);
    }

    private void OpenBoss(IDescentField field, bool colossus)
    {
        field.SweepStragglers();

        BossLineage lineage = LineageOf(Wave);
        // Seeded from the run and the wave together, so the five fights of one session are
        // five different monsters and replaying that seed brings the same five back.
        int seed = HashCombine(Seed, Wave * 7919);
        BossGene = colossus
            ? BossGen.Colossus(seed, lineage, Difficulty)
            : BossGen.Herald(seed, lineage, Difficulty);

        Phase = colossus ? DescentPhase.Colossus : DescentPhase.Herald;
        field.RaiseBoss(BossGene);
        field.Announce(colossus ? $"{BossGene.FullName}" : $"HERALD   {BossGene.FullName}");
        field.Signal(Cue.CrabScream, 1f);
    }

    private void TickBoss(float dt, IDescentField field)
    {
        if (field.BossAlive) return;

        // It is down. Hold the run still for a beat while the body comes apart and the shard
        // rises out of it, then move on.
        Clock -= dt;
        if (Clock > 0f) return;

        if (Phase == DescentPhase.Colossus)
        {
            Phase = DescentPhase.Cleared;
            field.Announce($"{Planet.Get(Destination).Name} IS CLEAR");
            field.Signal(Cue.MawCrystal, 1f);
            return;
        }

        Wave++;
        Phase = DescentPhase.Intermission;
        Clock = IntermissionLength;
        Ready = 0f;
        _readyLatched = false;
        BossGene = null;
        field.DropSalvage(6);
        field.SweepStragglers();
        field.Announce("SALVAGE WINDOW   HOLD READY TO ADVANCE");
    }

    private void TickIntermission(float dt, IDescentField field, bool readyHeld)
    {
        Clock -= dt;

        // The hold has to be continuous. Letting go drains it fast but not instantly, so a
        // dropped key for one frame in a firefight does not cost the whole commitment.
        if (readyHeld && !_readyLatched)
        {
            Ready = MathF.Min(1f, Ready + dt / ReadyHold);
            if (Ready >= 1f) { _readyLatched = true; Clock = 0f; }
        }
        else if (!readyHeld)
        {
            Ready = MathF.Max(0f, Ready - dt / (ReadyHold * 0.6f));
        }

        // A little salvage across the whole break rather than all of it at the top, so there is
        // a reason to still be moving at second seventy.
        if (Clock > 0f && (int)(Clock / 18f) != (int)((Clock + dt) / 18f))
            field.DropSalvage(2);

        if (Clock <= 0f)
        {
            Clock = 0f;
            OpenWave(field);
        }
    }

    /// <summary>
    /// Books one of this wave's units as dead. Called by the world for every hunter and every
    /// squad member that falls while a wave is running — and deliberately not for anything
    /// killed during a herald fight or a break, which is why the world tags what it spawns.
    /// </summary>
    public void CountKill()
    {
        if (Phase != DescentPhase.Wave) return;
        Killed = Math.Min(WaveTotal, Killed + 1);
    }

    /// <summary>
    /// Rolls the fragment a boss just gave up. Fifty-fifty, every time, with no pity and no
    /// memory — a run can genuinely end with five suns, and the fact that it can is the whole
    /// reason a run that ends with three and two feels like it went somewhere.
    ///
    /// <para>It goes to <em>nobody</em>. The world puts it on the ground at the corpse and
    /// whoever walks over it first has it, which is the change that turned a fragment from a
    /// medal into a thing: it can be missed, carried, argued over, dropped when its carrier
    /// dies, and picked back up out of the wreck by somebody else.</para>
    /// </summary>
    public Fragment RollFragment()
    {
        Fragment f = Random.Shared.Next(2) == 0 ? Fragment.Sun : Fragment.Moon;
        PendingFragment = f;
        Dropped = Math.Min(WaveCount, Dropped + 1);
        // The beat after a boss dies, during which nothing happens on purpose.
        Clock = AfterBossPause;
        return f;
    }

    /// <summary>Reads and clears the last roll, so the world lays down exactly one rock for
    /// it.</summary>
    public Fragment TakePendingFragment()
    {
        Fragment f = PendingFragment;
        PendingFragment = Fragment.None;
        return f;
    }

    /// <summary>The tag that hangs under a nickname, given what that seat is actually carrying.
    /// Empty for anybody holding nothing, which is most people for most of a run — that is what
    /// makes it worth wearing.
    ///
    /// <para>Takes the counts rather than looking them up, because the answer lives in a pack
    /// now and this class has never known about packs. Kept here anyway so the wording is
    /// decided in one place: the HUD, the nameplate and the ending screen all say it the
    /// same way.</para></summary>
    public static string TagFor(int suns, int moons)
    {
        if (suns + moons == 0) return "";
        if (moons == 0) return suns == 1 ? "SUN" : $"SUN {suns}";
        if (suns == 0) return moons == 1 ? "MOON" : $"MOON {moons}";
        return $"SUN {suns} MOON {moons}";
    }

    /// <summary>Whether the run is over, either way. The pause panel and the star chart both
    /// ask, so it is one property rather than two comparisons in two places.</summary>
    public bool Finished => Phase is DescentPhase.Cleared or DescentPhase.Lost;

    /// <summary>What the HUD prints as the phase label.</summary>
    public string PhaseLabel => Phase switch
    {
        DescentPhase.Landing => "LANDING",
        DescentPhase.Wave => $"WAVE {Wave}",
        DescentPhase.Herald => "HERALD",
        DescentPhase.Intermission => "SALVAGE",
        DescentPhase.Colossus => "DESCENT",
        DescentPhase.Cleared => "CLEAR",
        _ => "LOST",
    };

    /// <summary>
    /// Client-side: takes the host's account of where the run has got to.
    ///
    /// <para>Only the readouts. A client's director never <em>runs</em> — it spawns nothing,
    /// raises no boss and rolls no fragment, because all of that is the host's and arrives as
    /// entities through the field packets. What this exists for is the HUD: the phase label, the
    /// wave bar and the salvage clock, which are otherwise the one part of DESCENT a client
    /// could see no evidence of at all.</para>
    /// </summary>
    public void NetAdopt(DescentPhase phase, int wave, int killed, int total, float clock)
    {
        Phase = phase;
        Wave = Math.Clamp(wave, 1, WaveCount);
        WaveTotal = Math.Max(0, total);
        Killed = Math.Clamp(killed, 0, WaveTotal);
        Clock = MathF.Max(0f, clock);
    }

    // --- What {skip wave} reaches for ------------------------------------------------------
    //
    // Three narrow doors rather than one wide one. The console could have been given a setter
    // for Clock and Killed and left to arrange the phases itself, and that would put the rules
    // of the mode in two places — the day a wave gains a fourth thing to bookkeep, one of them
    // would be updated. Each of these leaves the director in a state it could have reached by
    // being played.

    /// <summary>Cuts the drop short. The wave opens on the next tick exactly as it would have.</summary>
    public void SkipLanding()
    {
        if (Phase == DescentPhase.Landing) Clock = 0f;
    }

    /// <summary>
    /// Books the whole of the current wave as dead: nothing left in the reserve and nothing
    /// left standing. The director's own rule — a crowd is over when the roster is spent and
    /// the field is clear of it — then opens the herald on the next tick, so the fight that
    /// closes the wave still happens.
    /// </summary>
    public void SpendTheWave()
    {
        if (Phase != DescentPhase.Wave) return;
        Reserve = 0;
        Killed = WaveTotal;
    }

    /// <summary>Ends a salvage window now. The same thing holding READY does, without the
    /// hold — so what follows is the ordinary path into the next wave.</summary>
    public void SkipTheBreak()
    {
        if (Phase != DescentPhase.Intermission) return;
        Clock = 0f;
        Ready = 1f;
        _readyLatched = true;
    }

    /// <summary>Marks the run failed. Solo, that is the craft running out of lives; in a match
    /// it is the whole room being spent.</summary>
    public void Fail(IDescentField field)
    {
        if (Finished) return;
        Phase = DescentPhase.Lost;
        field.Announce("THE DESCENT ENDS HERE");
    }

    /// <summary>
    /// Capture/test hatch: drops the run straight into a given phase of a given wave, so the
    /// boss's layer stack and the salvage window can be photographed without playing four waves
    /// to reach them. Raises the boss through <paramref name="field"/> where the phase needs one.
    ///
    /// <para>Never called in play — the phases are reached by fighting, which is the point.</para>
    /// </summary>
    public void SkipTo(DescentPhase phase, int wave, IDescentField field)
    {
        Wave = Math.Clamp(wave, 1, WaveCount);
        switch (phase)
        {
            case DescentPhase.Intermission:
                Phase = DescentPhase.Intermission;
                Clock = IntermissionLength;
                Ready = 0f;
                _readyLatched = false;
                BossGene = null;
                field.DropSalvage(6);
                break;

            case DescentPhase.Herald:
            case DescentPhase.Colossus:
                OpenBoss(field, colossus: phase == DescentPhase.Colossus);
                break;

            case DescentPhase.Cleared:
                // The one phase that cannot be "opened" — it is what is left when the Colossus
                // falls. Set outright so the ending screen's winning face can be photographed
                // and tested without killing five bosses first.
                Phase = DescentPhase.Cleared;
                Wave = WaveCount;
                break;

            case DescentPhase.Lost:
                Phase = DescentPhase.Lost;
                break;

            default:
                OpenWave(field);
                break;
        }
    }

    /// <summary>A stable mix of two ints. Not a hash for security — just something that does not
    /// leave the wave number visible in the low bits, so waves one and two of a run do not roll
    /// suspiciously similar bodies.</summary>
    private static int HashCombine(int a, int b)
    {
        unchecked
        {
            uint h = (uint)a * 2654435761u;
            h ^= (uint)b * 2246822519u;
            h ^= h >> 15;
            h *= 2654435761u;
            h ^= h >> 13;
            return (int)h;
        }
    }
}
