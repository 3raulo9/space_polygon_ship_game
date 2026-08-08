namespace Unrendered.Net;

/// <summary>
/// The short-lived lines that appear bottom-right under the HUD in a match — "&lt;name&gt; JOINED",
/// "&lt;name&gt; LEFT", a kill, and now what people say to each other.
///
/// Self-timed off a decrementing life rather than a wall clock, so it needs nothing from
/// Raylib and can be exercised in the headless self-test. The host fills it as people come
/// and go and mirrors each line to every client; a client fills it from those mirrored
/// messages. Both ends age it a frame at a time.
/// </summary>
public sealed class NoticeFeed
{
    /// <summary>
    /// What kind of line this is. The feed carries two very different things now and they
    /// cannot be treated the same: a kill announce is noise the game is generating about
    /// itself, and a chat line is a person trying to say something to nineteen other people.
    /// </summary>
    public enum Kind : byte
    {
        /// <summary>The game talking: joins, leaves, kills, the run's own narration.</summary>
        Notice,
        /// <summary>Somebody talking.</summary>
        Chat,
        /// <summary>The answer to a command — only ever shown on the machine that typed it.</summary>
        Console,
    }

    public sealed class Entry
    {
        public required string Text { get; init; }
        public required Kind Type { get; init; }
        public float Remaining { get; set; }
    }

    /// <summary>How long a notice lingers before it fades out, in seconds.</summary>
    public const float Life = 6f;

    /// <summary>
    /// And how long a chat line gets. Twice as long, because six seconds is about two seconds
    /// of a firefight: the feed fills with kills the instant anything happens, and a sentence
    /// somebody typed would be gone before the person it was aimed at had finished reloading.
    /// </summary>
    public const float ChatLife = 12f;

    /// <summary>The most lines kept at once; older ones drop off the top.</summary>
    private const int Cap = 5;

    private readonly List<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>
    /// Everything anybody has said this session, oldest first, whether or not it is still on
    /// screen. This is what the chat's history panel reads.
    ///
    /// <para>Kept separately from <see cref="Entries"/> rather than by simply not expiring
    /// them, because the two answer different questions: the feed is "what is happening right
    /// now" and this is "what did I miss". Notices are deliberately absent — nobody scrolls back
    /// to re-read who killed a hunter forty minutes ago.</para>
    /// </summary>
    public IReadOnlyList<string> History => _history;

    private readonly List<string> _history = new();

    /// <summary>How far back the history goes. Two hundred lines is a long session's worth of
    /// conversation and a few kilobytes.</summary>
    public const int HistoryCap = 200;

    public void Push(string text) => Push(text, Kind.Notice);

    public void Push(string text, Kind kind)
    {
        _entries.Add(new Entry
        {
            Text = text,
            Type = kind,
            Remaining = kind == Kind.Notice ? Life : ChatLife,
        });

        // Over the cap: drop the oldest NOTICE first, and only fall back to dropping a chat
        // line when there is nothing else to give. A room that is fighting generates announces
        // several times a second, and without this rule a sentence is pushed off the screen by
        // the game talking over it — which is the exact failure that makes an in-game chat
        // useless in the one situation people actually want one.
        while (_entries.Count > Cap)
        {
            int drop = -1;
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Type == Kind.Notice) { drop = i; break; }
            _entries.RemoveAt(drop >= 0 ? drop : 0);
        }

        // Only what people said is worth scrolling back through.
        if (kind == Kind.Notice) return;
        _history.Add(text);
        if (_history.Count > HistoryCap) _history.RemoveAt(0);
    }

    /// <summary>Ages every line by one frame and drops the ones that have faded out.</summary>
    public void Age(float dt)
    {
        for (int i = 0; i < _entries.Count; i++) _entries[i].Remaining -= dt;
        _entries.RemoveAll(e => e.Remaining <= 0f);
    }
}
