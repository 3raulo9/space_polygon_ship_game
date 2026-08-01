namespace Unrendered.Rendering;

/// <summary>
/// One animator per body, kept alive between frames and thrown away when the body it
/// belonged to stops being drawn.
///
/// The animators have memory — that is the entire point of them — so they cannot be built
/// per frame the way a pose function could be, and they cannot be hung on the entities
/// either: those are simulation, they are replicated, and a squad member on a client is a
/// different object from the one on the host. So they live here, keyed by whatever the
/// renderer is drawing, and are reaped when it stops asking for them.
///
/// Reference equality, deliberately: two soldiers with identical state are two soldiers,
/// and they should breathe out of step.
/// </summary>
public sealed class SoldierAnimatorSet
{
    private readonly Dictionary<object, Entry> _live = new(ReferenceEqualityComparer.Instance);
    private int _frame;
    private int _swept;

    private sealed class Entry
    {
        public readonly SoldierAnimator Animator = new();
        public int Seen;
        public int Flinch = -1;
        public int Slash = -1;
        public int Shot = -1;
    }

    /// <summary>Starts a frame. Everything asked for between here and the next call
    /// survives it.</summary>
    public void Begin() => _frame++;

    /// <summary>The animator belonging to <paramref name="who"/>, made on first sight.</summary>
    public SoldierAnimator For(object who)
    {
        if (!_live.TryGetValue(who, out Entry? e)) _live[who] = e = new Entry();
        e.Seen = _frame;
        return e.Animator;
    }

    /// <summary>
    /// Plays a one-shot exactly once per event, however the render rate compares to the
    /// simulation's. The entity counts its hits and its swings; this remembers which count
    /// it last acted on, so a frame that sees the same number does nothing and a frame that
    /// sees it jump by three still plays one.
    /// </summary>
    public void Flinch(object who, int seq, float angle, float strength)
    {
        if (!_live.TryGetValue(who, out Entry? e)) return;
        if (e.Flinch == seq) return;
        // First sight of a body is not a hit — otherwise every soldier that walks into view
        // flinches at nothing.
        if (e.Flinch >= 0) e.Animator.Flinch(angle, strength);
        e.Flinch = seq;
    }

    /// <summary>The same, for a blade swung.</summary>
    public void Slash(object who, int seq)
    {
        if (!_live.TryGetValue(who, out Entry? e)) return;
        if (e.Slash == seq) return;
        if (e.Slash >= 0) e.Animator.Slash();
        e.Slash = seq;
    }

    /// <summary>And for a round loosed.</summary>
    public void Fire(object who, int seq, bool right)
    {
        if (!_live.TryGetValue(who, out Entry? e)) return;
        if (e.Shot == seq) return;
        if (e.Shot >= 0) e.Animator.Fire(right);
        e.Shot = seq;
    }

    /// <summary>
    /// Drops the animators of anything that was not drawn this frame. Swept every so often
    /// rather than every frame: a body behind a building for two frames should not lose the
    /// swing it was in the middle of, and rebuilding the dictionary sixty times a second to
    /// reclaim a few hundred bytes would cost more than the bytes.
    /// </summary>
    public void End()
    {
        if (++_swept < SweepInterval) return;
        _swept = 0;

        List<object>? gone = null;
        foreach (var (who, e) in _live)
        {
            if (_frame - e.Seen <= SweepInterval) continue;
            (gone ??= new List<object>()).Add(who);
        }
        if (gone == null) return;
        foreach (object who in gone) _live.Remove(who);
    }

    /// <summary>Frames a body can go undrawn before its animator is let go. Two seconds at
    /// sixty, which is comfortably longer than anything is behind a tower for and far
    /// shorter than a match.</summary>
    private const int SweepInterval = 120;
}
