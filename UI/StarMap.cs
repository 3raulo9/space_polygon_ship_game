using Unrendered.Core;

namespace Unrendered.UI;

/// <summary>
/// The chart of the five worlds, and — in a match — the room's argument about which one to
/// drop onto. Pure state, like every other screen in this game: it owns a cursor, a tally and
/// a countdown, and knows nothing about holograms, keyboards or sockets. The lobby's holo
/// table drives one of these; so does the single-player map between the hangar and the drop,
/// where the tally is simply never used.
///
/// The host is the only machine that ever <see cref="Resolve"/>s a vote. Clients are told the
/// tally and told the result; a tie broken independently on twenty machines would be twenty
/// different answers.
/// </summary>
public sealed class StarMap
{
    /// <summary>How long a vote runs once the host opens it. Long enough to walk to the table
    /// from anywhere in the room and read the readouts; short enough that nobody is waiting on
    /// one person who has wandered off.</summary>
    public const float VoteDuration = 20f;

    /// <summary>The world under the local player's cursor. Also what a solo player will land
    /// on, since there is nobody to argue with.</summary>
    public PlanetId Cursor { get; private set; } = PlanetId.Solune;

    /// <summary>Whether a countdown is running. While it is, confirming at the table casts a
    /// vote rather than settling the destination outright.</summary>
    public bool VoteOpen { get; private set; }

    /// <summary>Seconds left on the countdown, for the readout.</summary>
    public float SecondsLeft { get; private set; }

    /// <summary>Who voted for what. Seat → world; a seat that changes its mind overwrites its
    /// own entry, so a player can move their vote right up to the lock.</summary>
    public readonly Dictionary<int, PlanetId> Votes = new();

    /// <summary>Set true on the frame the local player casts, for the loop to put on the wire
    /// and clear. The same shape as the room's other dirty flags.</summary>
    public bool CastDirty { get; private set; }

    public void ClearDirty() => CastDirty = false;

    /// <summary>
    /// Worlds this chart will not let anybody pick, as a bitmask over <see cref="PlanetId"/>.
    ///
    /// <para>Empty at the lobby's table, where every world is open. It is the arch that uses
    /// this: a gate cannot send you to the planet you are standing on, and it cannot send you
    /// anywhere the session has already been — the campaign is five worlds and you see each of
    /// them once. A locked world is still drawn, struck through, because "you have been here"
    /// is information about the run and hiding it would make the chart shrink for no visible
    /// reason.</para>
    /// </summary>
    public int Locked { get; private set; }

    public void Lock(PlanetId id) => Locked |= 1 << (int)id;
    public void ClearLocks() => Locked = 0;
    public bool IsLocked(PlanetId id) => (Locked & (1 << (int)id)) != 0;

    /// <summary>Whether anything at all is still pickable. False only if a campaign has been
    /// everywhere, which is the state the last planet's panel reports as IN PROGRESS.</summary>
    public bool AnyOpen
    {
        get
        {
            foreach (var p in Planet.All) if (!IsLocked(p.Id)) return true;
            return false;
        }
    }

    /// <summary>Walks the cursor along the chart. Hard edges, no wrap — the map is a row of
    /// five worlds, not a carousel, and running off the end should feel like a wall.
    ///
    /// <para>Locked worlds are stepped over rather than stopped on, so a cursor never rests
    /// somewhere the player cannot commit to. Running out of open worlds in the direction of
    /// travel is the wall.</para></summary>
    public void Move(int step)
    {
        if (step == 0) return;
        int i = (int)Cursor;
        int last = Planet.All.Count - 1;
        for (int guard = 0; guard <= last; guard++)
        {
            i += step;
            if (i < 0 || i > last) return;              // walked off the end
            if (IsLocked((PlanetId)i)) continue;        // been there; keep going
            Cursor = (PlanetId)i;
            Audio.PlayBlip();
            return;
        }
    }

    /// <summary>Point the cursor somewhere outright — used when the wire says the destination
    /// changed, so a client's chart follows the host's choice, and by the arch's panel when a
    /// world is clicked directly rather than walked to.</summary>
    public void PointAt(PlanetId id)
    {
        if (IsLocked(id)) return;
        Cursor = id;
    }

    /// <summary>Puts the cursor on the first world that is still open. Called when a chart
    /// opens, so it never comes up pointing at somewhere the run has already been.</summary>
    public void PointAtFirstOpen()
    {
        foreach (var p in Planet.All)
            if (!IsLocked(p.Id)) { Cursor = p.Id; return; }
    }

    /// <summary>The local player commits to what is under the cursor. Only meaningful while a
    /// vote is open; outside one the host settles it and everyone else is a spectator to that.</summary>
    public void CastLocal(int seat)
    {
        if (!VoteOpen || IsLocked(Cursor)) return;
        Cast(seat, Cursor);
        CastDirty = true;
    }

    /// <summary>Records a vote — the local player's, or one that arrived off the wire. A ballot
    /// for a locked world is dropped rather than counted: an old build, a stale packet or a
    /// client that has not been told about a lock must not be able to send the room somewhere
    /// it has already been.</summary>
    public void Cast(int seat, PlanetId choice)
    {
        if (seat < 0 || IsLocked(choice)) return;
        Votes[seat] = choice;
    }

    /// <summary>A seat left the room; their vote leaves with them.</summary>
    public void Withdraw(int seat) => Votes.Remove(seat);

    /// <summary>Host-side: start the countdown. Clears the previous round's votes so a second
    /// vote is not haunted by the first.</summary>
    public void OpenVote()
    {
        Votes.Clear();
        VoteOpen = true;
        SecondsLeft = VoteDuration;
    }

    /// <summary>Client-side: adopt the host's account of the countdown.</summary>
    public void AdoptClock(bool open, float secondsLeft)
    {
        VoteOpen = open;
        SecondsLeft = secondsLeft;
    }

    /// <summary>
    /// Runs the clock down. Returns true on the single frame the countdown expires, which is
    /// the host's cue to <see cref="Resolve"/> and tell everyone. Ticked on the host only —
    /// clients read the number off the wire so the two can never disagree about how long is
    /// left, which is the one thing a countdown must not be vague about.
    /// </summary>
    public bool Tick(float dt)
    {
        if (!VoteOpen) return false;
        SecondsLeft -= dt;
        if (SecondsLeft > 0f) return false;
        SecondsLeft = 0f;
        VoteOpen = false;
        return true;
    }

    /// <summary>How many are behind a given world.</summary>
    public int Tally(PlanetId id)
    {
        int n = 0;
        foreach (var v in Votes.Values) if (v == id) n++;
        return n;
    }

    /// <summary>
    /// Host-side: what the room decided. The most votes wins; a tie is broken at random among
    /// the worlds that tied, which is the only fair answer when the room is genuinely split —
    /// a host-breaks-ties rule would quietly make the host's vote worth two. An empty ballot
    /// falls back to <paramref name="fallback"/>, the destination that was already set.
    /// </summary>
    /// <param name="fallback">Where an empty ballot lands. At the lobby's table that is the
    /// destination already set; at an arch it is meaningless, so the arch passes the first open
    /// world instead — a gate that resolved to "the planet you are standing on" would be a
    /// three-hour run ending in a door back into the room you just left.</param>
    public PlanetId Resolve(PlanetId fallback)
    {
        if (Votes.Count == 0) return fallback;

        int best = 0;
        foreach (var p in Planet.All)
            if (!IsLocked(p.Id)) best = Math.Max(best, Tally(p.Id));

        var leaders = new List<PlanetId>();
        foreach (var p in Planet.All)
            if (!IsLocked(p.Id) && Tally(p.Id) == best) leaders.Add(p.Id);

        // Every ballot was for somewhere locked, which the casting rules above are meant to
        // make impossible — but a resolve that returns nothing at all would strand the room,
        // so it falls back rather than throwing.
        if (leaders.Count == 0) return fallback;

        return leaders[Random.Shared.Next(leaders.Count)];
    }
}
