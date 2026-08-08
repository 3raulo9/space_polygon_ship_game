namespace Unrendered.Core;

/// <summary>
/// The session, above the planet. A <see cref="Descent"/> is one world — five waves and the
/// thing at the bottom of it; a campaign is the string of them a room crosses in one sitting,
/// and the only state that survives a portal.
///
/// <para>It exists because clearing a planet stopped being the end of anything. DESCENT used to
/// finish when the Colossus fell: the mode declared CLEAR and handed over the ending screen.
/// Now that is the middle — the arch opens, the room picks somewhere it has not been, and the
/// whole five-wave shape happens again on a harder world. This holds the handful of facts that
/// have to outlive the world object being thrown away and rebuilt.</para>
///
/// <para><b>Still no save file.</b> This is a session, not progression: closing the game loses
/// all of it, exactly as before. Nothing here is written to disk and nothing here unlocks
/// anything for next time.</para>
/// </summary>
public sealed class Campaign
{
    /// <summary>How many worlds a full crossing is. Five, because there are five worlds and you
    /// see each of them once — the visited set and the chart's locks between them make that a
    /// rule rather than an intention.</summary>
    public static int Length => Planet.All.Count;

    /// <summary>
    /// The seed everything downstream is rolled from. Each planet's <see cref="Descent"/> takes
    /// a seed mixed out of this and its own index, so one integer reproduces an entire
    /// session — twenty-five bosses, their bodies, their moves, their names, and which three
    /// arcs on each world are the way off it.
    /// </summary>
    public readonly int Seed;

    /// <summary>Which worlds have been crossed, as a bitmask over <see cref="PlanetId"/>. The
    /// chart at every arch reads this and strikes them out.</summary>
    public int Visited { get; private set; }

    /// <summary>How many portals have been walked through. 0 on the first world.</summary>
    public int Hops { get; private set; }

    public Campaign(int? seed = null) => Seed = seed ?? Random.Shared.Next();

    public bool HasVisited(PlanetId id) => (Visited & (1 << (int)id)) != 0;

    /// <summary>Marks a world as crossed. Called when a run lands, not when it leaves — a
    /// player standing on THALOS must not be offered THALOS by the arch above their head, and
    /// waiting until they left would make the current world the one thing the chart still
    /// let them pick.</summary>
    public void Arrive(PlanetId id) => Visited |= 1 << (int)id;

    /// <summary>Notes a portal crossed.</summary>
    public void Hop() => Hops++;

    /// <summary>Whether there is anywhere left. False once every world has been crossed, which
    /// is the state the last arch's panel reports as IN PROGRESS.</summary>
    public bool AnywhereLeft
    {
        get
        {
            foreach (var p in Planet.All) if (!HasVisited(p.Id)) return true;
            return false;
        }
    }

    /// <summary>How many worlds are done.</summary>
    public int Crossed
    {
        get
        {
            int n = 0;
            foreach (var p in Planet.All) if (HasVisited(p.Id)) n++;
            return n;
        }
    }

    /// <summary>
    /// The seed for the run on the <paramref name="hop"/>'th world. Mixed rather than
    /// incremented, so consecutive planets of one session do not roll suspiciously similar
    /// bosses — the same reasoning, and the same mix, as the one <see cref="Descent"/> already
    /// uses to keep wave one and wave two apart.
    /// </summary>
    public int SeedFor(int hop)
    {
        unchecked
        {
            uint h = (uint)Seed * 2654435761u;
            h ^= (uint)(hop * 2246822519);
            h ^= h >> 15;
            h *= 2654435761u;
            h ^= h >> 13;
            return (int)h;
        }
    }

    /// <summary>
    /// How far into the whole crossing this is, 0..1, for the boss roller.
    ///
    /// <para>This is the number that makes twenty-five fights a climb rather than five loops of
    /// five. <see cref="Descent"/> used to hand its roller a difficulty running 0..1 across one
    /// planet's five waves, so wave one of world three was rolled exactly as soft as wave one of
    /// world one and hour three played like hour one. Now the wave's own position is folded into
    /// the campaign's, so a herald on the fourth world is rolled bigger and pushier than the
    /// Colossus on the first.</para>
    ///
    /// <para>Deliberately not linear in <paramref name="hop"/> alone: the curve has to keep
    /// rising <em>within</em> a planet too, or every world would open at its own flat plateau
    /// and the five-wave shape would stop meaning anything.</para>
    /// </summary>
    public float DifficultyAt(int hop, int wave, int wavesPerPlanet)
    {
        if (wavesPerPlanet <= 1) return 0f;
        float within = Math.Clamp((wave - 1) / (float)(wavesPerPlanet - 1), 0f, 1f);
        float across = Math.Clamp(hop / MathF.Max(1f, Length - 1f), 0f, 1f);
        // The planet you are on sets the floor; the wave you are on lifts you toward the next
        // planet's floor. So the last wave of world two is about as hard as the first of
        // world three, and the curve never steps backwards across a portal.
        float step = 1f / MathF.Max(1f, Length - 1f);
        return Math.Clamp(across + within * step, 0f, 1f);
    }
}
