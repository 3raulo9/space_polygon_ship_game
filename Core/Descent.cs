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

    /// <summary>What each seat is carrying, indexed by seat. Two counts, because a player who
    /// has taken four fragments should be wearing all four.</summary>
    private readonly int[] _suns = new int[MatchSettings.MaxSeats];
    private readonly int[] _moons = new int[MatchSettings.MaxSeats];

    public int SunsOf(int seat) => (uint)seat < (uint)_suns.Length ? _suns[seat] : 0;
    public int MoonsOf(int seat) => (uint)seat < (uint)_moons.Length ? _moons[seat] : 0;
    public bool Carrying(int seat) => SunsOf(seat) + MoonsOf(seat) > 0;

    /// <summary>The last fragment awarded and who took it, so the world can drop a physical
    /// shard at the corpse and the HUD can call it out. Cleared once read.</summary>
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
    /// built.</summary>
    private float Difficulty => Math.Clamp((Wave - 1) / (float)(WaveCount - 1), 0f, 1f);

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
    /// Awards the fragment a boss just gave up, to the seat that finished it. Fifty-fifty, as
    /// specified. Returns which one it was so the world can raise the right shard out of the
    /// corpse and the HUD can name it.
    /// </summary>
    public Fragment AwardFragment(int seat)
    {
        Fragment f = Random.Shared.Next(2) == 0 ? Fragment.Sun : Fragment.Moon;
        if ((uint)seat < (uint)_suns.Length)
        {
            if (f == Fragment.Sun) _suns[seat]++; else _moons[seat]++;
        }
        PendingFragment = f;
        // The beat after a boss dies, during which nothing happens on purpose.
        Clock = AfterBossPause;
        return f;
    }

    /// <summary>Reads and clears the last award, so the world raises exactly one shard for it.</summary>
    public Fragment TakePendingFragment()
    {
        Fragment f = PendingFragment;
        PendingFragment = Fragment.None;
        return f;
    }

    /// <summary>The tag that hangs under a player's nickname. Empty for anybody carrying
    /// nothing, which is most people for most of a run — that is what makes it worth wearing.</summary>
    public string TagFor(int seat)
    {
        int s = SunsOf(seat), m = MoonsOf(seat);
        if (s + m == 0) return "";
        if (m == 0) return s == 1 ? "SUN" : $"SUN {s}";
        if (s == 0) return m == 1 ? "MOON" : $"MOON {m}";
        return $"SUN {s} MOON {m}";
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
