namespace Unrendered.Core;

/// <summary>
/// Which world a match is played on. PLANET is the game as it has always been — the city,
/// the hunters, the bosses rising out of the fog. FLAT keeps the same city to fight around
/// but seeds nothing hostile and never spawns: a sandbox for a few people to move, shoot and
/// mess about in. Single player is always PLANET.
/// </summary>
public enum GameMap : byte
{
    Planet = 0,
    Flat = 1,
}

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

    /// <summary>The world this match is played on. Host's choice; PLANET by default and the
    /// only thing single player ever is.</summary>
    public GameMap Map { get; set; } = GameMap.Planet;

    /// <summary>
    /// Whether the world seeds and spawns anything hostile — hunters, bosses and squads alike.
    /// On by default and independent of the map: a host can leave the city on PLANET but empty
    /// the field entirely, turning any map into a sandbox to move and mess about in without
    /// dropping to FLAT. Off stops <em>every</em> hostile spawn, not just the horizon hunters.
    /// A lobby choice only — single player is always a full PLANET.
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
        Map = Enum.IsDefined(Map) ? Map : GameMap.Planet,
        SpawnEnemies = SpawnEnemies,
    };

    /// <summary>The solo game: one seat, and the three lives the craft has always had.
    /// Every existing single-player run is this, which is why the world defaults to it.</summary>
    public static MatchSettings SinglePlayer => new()
    {
        MaxPlayers = 1,
        FriendlyFire = false,
        Revives = DefaultRevives,
        Map = GameMap.Planet,
        SpawnEnemies = true,
    };

    // --- Wire format ---------------------------------------------------------------
    // Five bytes. Sent once, reliably, when a client joins.

    public const int Size = 5;

    public void Write(Span<byte> dst)
    {
        dst[0] = (byte)MaxPlayers;
        dst[1] = (byte)(FriendlyFire ? 1 : 0);
        dst[2] = (byte)Revives;
        dst[3] = (byte)Map;
        dst[4] = (byte)(SpawnEnemies ? 1 : 0);
    }

    public static MatchSettings Read(ReadOnlySpan<byte> src) => new MatchSettings
    {
        MaxPlayers = src[0],
        FriendlyFire = src[1] != 0,
        Revives = src[2],
        Map = (GameMap)src[3],
        SpawnEnemies = src[4] != 0,
    }.Clamped();
}
