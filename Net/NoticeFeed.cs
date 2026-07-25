namespace VoidTanks.Net;

/// <summary>
/// The short-lived lines that appear bottom-right under the HUD in a match — "<name> JOINED",
/// "<name> LEFT" — so everyone can see the room change around them without leaving the game.
///
/// Self-timed off a decrementing life rather than a wall clock, so it needs nothing from
/// Raylib and can be exercised in the headless self-test. The host fills it as people come
/// and go and mirrors each line to every client; a client fills it from those mirrored
/// messages. Both ends age it a frame at a time.
/// </summary>
public sealed class NoticeFeed
{
    public sealed class Entry
    {
        public required string Text { get; init; }
        public float Remaining { get; set; }
    }

    /// <summary>How long a line lingers before it fades out, in seconds.</summary>
    public const float Life = 6f;

    /// <summary>The most lines kept at once; older ones drop off the top.</summary>
    private const int Cap = 5;

    private readonly List<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries => _entries;

    public void Push(string text)
    {
        _entries.Add(new Entry { Text = text, Remaining = Life });
        if (_entries.Count > Cap) _entries.RemoveAt(0);
    }

    /// <summary>Ages every line by one frame and drops the ones that have faded out.</summary>
    public void Age(float dt)
    {
        for (int i = 0; i < _entries.Count; i++) _entries[i].Remaining -= dt;
        _entries.RemoveAll(e => e.Remaining <= 0f);
    }
}
