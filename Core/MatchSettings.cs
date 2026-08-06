namespace Unrendered.Core;

/// <summary>
/// What the host decides before anyone launches, and what every client is told once on
/// joining. Distinct from <see cref="Settings"/>, which is one person's keys and prefs and
/// never leaves their machine — this is the rules of the match, and all twenty players are
/// bound by the host's copy of it.
///
/// Deliberately small and flat. It goes over the wire exactly once, at join, on the
/// reliable channel, and after that it does not change for the life of the match: a
/// friendly-fire toggle that flips mid-run would mean re-deciding damage that has already
/// been dealt, and a revive count that moved would make the number on someone's HUD a lie.
/// </summary>
public sealed class MatchSettings
{
    /// <summary>The most players this session will seat, host included. The host picks it;
    /// the lobby refuses the next joiner once it is full.</summary>
    public int MaxPlayers { get; set; } = DefaultMaxPlayers;

    /// <summary>Whether players' rounds hurt each other. Off by default — with twenty
    /// craft on one torus, a stray shell finding a team-mate is the common case rather
    /// than the interesting one, and it should be something a lobby opts into.</summary>
    public bool FriendlyFire { get; set; }

    /// <summary>
    /// How many times a player can come back before they are out for the match. Zero is a
    /// legitimate choice and means one life: the count is <em>revives</em>, not lives.
    /// Feeds <see cref="Entities.PlayerTank.Lives"/> at spawn, rides the HUD, and when it
    /// is spent the player becomes a spectator rather than ending the run for everyone.
    /// </summary>
    public int Revives { get; set; } = DefaultRevives;

    /// <summary>The world this match is played on — settled at the star map, either by the
    /// host outright or by a vote of the room. SOLUNE by default, which is the one with the
    /// sun and the moon and so the one worth landing on first.</summary>
    public PlanetId Destination { get; set; } = PlanetId.Solune;

    /// <summary>
    /// SANDBOX or DESCENT. Chosen before the star map on both paths — from the title menu in
    /// single player, from the host console in a match — and fixed for the life of the run,
    /// like every other rule here.
    /// </summary>
    public GameMode Mode { get; set; } = GameMode.Sandbox;

    /// <summary>The conditions over the chosen world: sky, fog, gravity, how much of it wants
    /// you dead. Looked up rather than sent, so nothing about a planet has to survive a round
    /// trip and two machines can never disagree about how hard a place is.</summary>
    public Planet World => Planet.Get(Destination);

    /// <summary>
    /// Whether the world seeds and spawns anything hostile — hunters, bosses and squads alike.
    /// On by default and independent of the destination: a host can land on any world and still
    /// empty the field entirely, leaving the city and the salvage to move and mess about in.
    /// Off stops <em>every</em> hostile spawn, not just the horizon hunters — it is what the
    /// retired FLAT map used to be, without having to give up the sky you chose.
    /// A lobby choice only — single player always lands on a full world.
    /// </summary>
    public bool SpawnEnemies { get; set; } = true;

    public const int MinPlayers = 1;
    public const int MaxSeats = 20;
    public const int DefaultMaxPlayers = 8;

    public const int MaxRevives = 10;

    /// <summary>Two, which is three lives — exactly the craft this game has always shipped.
    /// The number the host sees is <em>revives</em> and the tank counts <em>lives</em>, so
    /// they differ by one, and picking 2 here is what keeps a solo run unchanged.</summary>
    public const int DefaultRevives = 2;

    /// <summary>Folds a host's choices into the legal range. Called on the way in <em>and</em>
    /// on the way out of the wire: a client must never be talked into a twenty-first seat or
    /// a negative revive count by a malformed join packet.</summary>
    public MatchSettings Clamped() => new()
    {
        MaxPlayers = Math.Clamp(MaxPlayers, MinPlayers, MaxSeats),
        FriendlyFire = FriendlyFire,
        Revives = Math.Clamp(Revives, 0, MaxRevives),
        Destination = Enum.IsDefined(Destination) ? Destination : PlanetId.Solune,
        Mode = Enum.IsDefined(Mode) ? Mode : GameMode.Sandbox,
        SpawnEnemies = SpawnEnemies,
    };

    /// <summary>The solo game: one seat, and the three lives the craft has always had.
    /// Every existing single-player run is this, which is why the world defaults to it.</summary>
    public static MatchSettings SinglePlayer => new()
    {
        MaxPlayers = 1,
        FriendlyFire = false,
        Revives = DefaultRevives,
        Destination = PlanetId.Solune,
        Mode = GameMode.Sandbox,
        SpawnEnemies = true,
    };

    // --- Wire format ---------------------------------------------------------------
    // Six bytes. Sent once, reliably, when a client joins. Grew by one when the single
    // MAP byte became a destination and a mode: builds either side of that change cannot
    // read each other's welcome, so both machines have to update together.

    public const int Size = 6;

    public void Write(Span<byte> dst)
    {
        dst[0] = (byte)MaxPlayers;
        dst[1] = (byte)(FriendlyFire ? 1 : 0);
        dst[2] = (byte)Revives;
        dst[3] = (byte)Destination;
        dst[4] = (byte)(SpawnEnemies ? 1 : 0);
        dst[5] = (byte)Mode;
    }

    public static MatchSettings Read(ReadOnlySpan<byte> src) => new MatchSettings
    {
        MaxPlayers = src[0],
        FriendlyFire = src[1] != 0,
        Revives = src[2],
        Destination = (PlanetId)src[3],
        SpawnEnemies = src[4] != 0,
        Mode = (GameMode)src[5],
    }.Clamped();
}
