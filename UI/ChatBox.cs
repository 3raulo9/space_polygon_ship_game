using Raylib_cs;
using Unrendered.Core;

namespace Unrendered.UI;

/// <summary>
/// The chat: a line you type down the side of the screen, the history behind it, and the
/// nickname completion that makes naming somebody in a room of twenty bearable.
///
/// <para>Pure state — a buffer, a scroll offset and a completion cursor. It reads the keyboard,
/// as every screen in this game does, and hands back what the player committed to; what happens
/// to that line is the game loop's business and the wire's.</para>
///
/// <para><b>It takes the whole keyboard.</b> That is the one thing to hold on to about this
/// class: while it is open, W is a letter. Every other control in the game has to be held back
/// by the caller, which is also why opening it plants the craft — a chassis that could still be
/// driven while its driver was typing would be driven by accident, constantly.</para>
/// </summary>
public sealed class ChatBox
{
    public bool Open { get; private set; }

    /// <summary>What is being typed.</summary>
    public string Buffer { get; private set; } = "";

    /// <summary>
    /// Whether the scrollable history is showing.
    ///
    /// <para>Reached by pressing the chat key again with an empty line, which is the shape asked
    /// for: C opens the box, C again shows what you missed. Typing anything drops it again, so
    /// the history never sits over the top of a message being composed.</para>
    /// </summary>
    public bool ShowHistory { get; private set; }

    /// <summary>How far up the history the view is, in lines from the bottom. 0 is the newest.</summary>
    public int Scroll { get; private set; }

    /// <summary>What the completion is offering, or -1 for nothing. An index into the last set
    /// of matches rather than a name, so pressing Up repeatedly walks them.</summary>
    private int _complete = -1;
    private string _completeStem = "";
    private readonly List<string> _matches = new();

    /// <summary>Something to tell the player, from the last thing they did.</summary>
    public string? Reply { get; private set; }

    public void Show()
    {
        Open = true;
        Buffer = "";
        ShowHistory = false;
        Scroll = 0;
        ResetCompletion();
    }

    public void Hide()
    {
        Open = false;
        Buffer = "";
        ShowHistory = false;
        ResetCompletion();
    }

    /// <summary>What one frame of the chat decided.</summary>
    public enum Action : byte
    {
        None,
        /// <summary>Escape, or the chat key on an empty line for the second time.</summary>
        Close,
        /// <summary>Enter with something in the buffer: send it, then close.</summary>
        Send,
    }

    /// <summary>
    /// One frame. <paramref name="names"/> is every nickname in the room, for the completion.
    /// </summary>
    public Action Update(IReadOnlyList<string> names, int historyLines)
    {
        if (!Open) return Action.None;

        // Escape always gets you out, and never sends. A half-typed line is thrown away rather
        // than said, which is what a player expects of every text field they have ever used.
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) return Action.Close;

        // The chat key, pressed again. On an empty line it opens the history; with something
        // typed it does nothing at all, because C is a letter and somebody spelling "COVER ME"
        // must not have their message eaten by the third keystroke.
        if (Buffer.Length == 0 && Input.InputMap.ChatToggle)
        {
            if (ShowHistory) return Action.Close;
            ShowHistory = true;
            Scroll = 0;
            return Action.None;
        }

        // The wheel walks the history. The only input with no other job while the box is open —
        // typing owns the keyboard and the arrow keys are the completion's.
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0f && ShowHistory)
        {
            Scroll = Math.Clamp(Scroll + (int)MathF.Round(wheel), 0,
                Math.Max(0, historyLines - 1));
        }

        // Up cycles the names that match whatever word is being typed at the cursor. The stem is
        // taken once, on the first press, and held: without that, filling the buffer in would
        // change what "matches" on the next press and the second Up would offer nothing.
        if (Raylib.IsKeyPressed(KeyboardKey.Up)) CycleCompletion(names, +1);
        else if (Raylib.IsKeyPressed(KeyboardKey.Down)) CycleCompletion(names, -1);

        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && Buffer.Length > 0)
        {
            Buffer = Buffer[..^1];
            ResetCompletion();
        }

        for (int c = Raylib.GetCharPressed(); c != 0; c = Raylib.GetCharPressed())
        {
            if (Buffer.Length >= ChatCommand.MaxLength) break;
            char ch = (char)c;
            // The same range the name field accepts. Uppercased because the pixel font has one
            // case and every name in this game is already shown in it.
            if (ch >= ' ' && ch < 127) Buffer += char.ToUpperInvariant(ch);
            ShowHistory = false;
            ResetCompletion();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter))
            return Buffer.Trim().Length > 0 ? Action.Send : Action.Close;

        return Action.None;
    }

    /// <summary>
    /// Walks the completion. The word being completed is whatever follows the last space, so
    /// <c>{item bullet 100 a</c> completes the name and not the command.
    /// </summary>
    private void CycleCompletion(IReadOnlyList<string> names, int step)
    {
        if (names.Count == 0) return;

        if (_complete < 0)
        {
            // First press: work out what is being completed and gather the matches. An empty
            // stem matches everybody, which is what makes Up-on-an-empty-word a roster walk.
            _completeStem = StemOf(Buffer);
            _matches.Clear();
            _matches.AddRange(MatchesFor(Buffer, names));
            if (_matches.Count == 0) return;
            _complete = step > 0 ? 0 : _matches.Count - 1;
        }
        else
        {
            if (_matches.Count == 0) return;
            _complete = (_complete + step + _matches.Count) % _matches.Count;
        }

        // Replace the stem with the offered name, leaving everything before it alone.
        int at = Buffer.Length - _completeStem.Length;
        Buffer = Buffer[..at] + _matches[_complete];
        Audio.PlayBlip();
    }

    /// <summary>
    /// The word the completion is working on: whatever follows the last space, so
    /// <c>{item bullet 100 a</c> completes the name and not the command. Its own method, and
    /// public, because it and <see cref="MatchesFor"/> are the whole of the rule and the rest of
    /// this class needs a window to exercise.
    /// </summary>
    public static string StemOf(string buffer)
    {
        int cut = buffer.LastIndexOf(' ');
        return cut < 0 ? buffer : buffer[(cut + 1)..];
    }

    /// <summary>
    /// Every name the word being typed could become, in roster order. An empty stem matches
    /// everybody — which is what makes pressing Up on a blank word a walk through the room
    /// rather than nothing happening.
    /// </summary>
    public static List<string> MatchesFor(string buffer, IReadOnlyList<string> names)
    {
        string stem = StemOf(buffer);
        var found = new List<string>();
        foreach (var n in names)
            if (n.StartsWith(stem, StringComparison.OrdinalIgnoreCase)) found.Add(n);
        return found;
    }

    private void ResetCompletion()
    {
        _complete = -1;
        _matches.Clear();
        _completeStem = "";
    }

    /// <summary>Capture hatch: puts a character in without a keyboard, so a half-typed command
    /// and its colouring can be photographed.</summary>
    public void TypeForTest(char ch)
    {
        if (Buffer.Length < ChatCommand.MaxLength && ch >= ' ' && ch < 127)
            Buffer += char.ToUpperInvariant(ch);
    }

    /// <summary>Capture hatch: opens the scrollback.</summary>
    public void ShowHistoryForTest() => ShowHistory = true;

    /// <summary>Takes what was typed and clears the buffer, ready for the next opening.</summary>
    public string Take()
    {
        string said = Buffer.Trim();
        Buffer = "";
        ResetCompletion();
        return said;
    }

    /// <summary>Shows an answer to the last command, on this machine only.</summary>
    public void Answer(string? text) => Reply = text;
}
