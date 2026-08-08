using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.UI;

namespace Unrendered.Rendering;

/// <summary>
/// The chat, drawn down the left of the running world: a line you are typing along the bottom,
/// and — when it is asked for — the session's conversation scrolling above it.
///
/// <para><b>It is not a screen.</b> There is no full-frame wash and no panel across the middle:
/// the world stays visible everywhere the text is not, because the whole point of an in-game
/// chat is that you are still in the game. The history gets a backing only behind itself, and
/// only enough of one to make small text legible against a bright grid.</para>
///
/// <para>Left-hand side deliberately. The fading feed lives bottom-right and the instruments
/// live along the top; the left is the one edge of this HUD with nothing on it, so the chat can
/// have it without anything having to move.</para>
/// </summary>
internal static class ChatRenderer
{
    private const int W = Config.InternalWidth;
    private const int H = Config.InternalHeight;

    /// <summary>How wide the box is. A third of the frame, as asked — wide enough for a real
    /// sentence at this font, narrow enough that two thirds of the world is untouched.</summary>
    private const int BoxWidth = W / 3;

    private const int Pad = 4;
    private const int LineHeight = 8;

    /// <summary>Where the line being typed sits. Clear of the bottom-left ability readouts.</summary>
    private const int InputY = H - 20;

    public static void Draw(ChatBox chat, Net.NoticeFeed feed, float time)
    {
        if (!chat.Open) return;

        if (chat.ShowHistory) DrawHistory(chat, feed);
        DrawInput(chat, time);
    }

    // --- What you are typing ----------------------------------------------------------------

    private static void DrawInput(ChatBox chat, float time)
    {
        // The backing runs to the bottom of the frame, and it is fully opaque.
        //
        // That is not decoration: the bottom-left corner is the chassis's own status column —
        // SIEGE, SMK, AP and their equivalents on the other five — and the line being typed
        // lands squarely on top of it. Covering it is the honest answer rather than the lazy
        // one, because the chat plants the craft and kills the trigger: while this is open,
        // whether the dischargers have cooled is not information anybody can act on. It comes
        // straight back the moment the box closes.
        //
        // Opaque, not nearly-opaque: at 236 the column showed through as ghost letters under
        // the line being typed, which reads as a rendering fault rather than as two things
        // sharing a corner.
        Raylib.DrawRectangle(0, InputY - 13, BoxWidth + Pad * 2, H - InputY + 13,
            new Color(6, 10, 14, 255));
        Raylib.DrawRectangle(0, InputY - 13, 1, H - InputY + 13, Palette.HudChrome);

        // A command is coloured as one the instant it is recognisable — the moment the closing
        // brace lands, the line goes from chrome to the flag's yellow. That is the whole of the
        // feedback for "the game understood what you are about to do", and it arrives before
        // you commit rather than after.
        var cmd = ChatCommand.Parse(chat.Buffer);
        Color ink = cmd.Verb switch
        {
            ChatVerb.Say => Palette.HudChrome,
            ChatVerb.Unknown => Palette.Warning,
            _ => Palette.Flag,
        };

        // Blinking block cursor. Solid rather than an underscore because at this size an
        // underscore under a capital is indistinguishable from the letter's own foot.
        bool caret = MathF.Sin(time * 6f) > 0f;
        int x = PixelFont.Draw(chat.Buffer, Pad, InputY, 1, ink);
        if (caret) Raylib.DrawRectangle(x, InputY, 4, 7, ink);

        // What a badly-formed command is wrong about, under the line as you type it. This is the
        // difference between a console you can learn and one you have to be told about.
        if (cmd.Verb == ChatVerb.Unknown && cmd.Complaint is { } why)
            PixelFont.Draw(why, Pad, InputY - 10, 1, Scale(Palette.Warning, 0.85f));
        else if (chat.Buffer.Length == 0)
            PixelFont.Draw("ENTER SENDS   UP NAMES   C HISTORY", Pad, InputY - 10, 1,
                Scale(Palette.HudChrome, 0.45f));
    }

    // --- What was said --------------------------------------------------------------------

    private static void DrawHistory(ChatBox chat, Net.NoticeFeed feed)
    {
        // Bottom-anchored, and only as tall as it has something to say.
        //
        // It was written top-anchored at a fixed height first, and that was wrong twice over: a
        // session with five lines in it drew a two-hundred-pixel empty box with the text at the
        // bottom and the heading floating alone at the top, and the heading itself landed inside
        // the instrument strip. A panel that grows upward out of the line you are typing reads
        // as belonging to it, and never covers more of the world than it is using.
        var rows = Wrap(feed.History);
        int room = (Bottom - (TopStrip + 8)) / LineHeight;
        int shown = Math.Clamp(rows.Count - chat.Scroll, 0, room);

        int height = Math.Max(1, shown) * LineHeight + 10;
        int top = Bottom - height;

        Raylib.DrawRectangle(0, top, BoxWidth + Pad * 2, height, new Color(6, 10, 14, 190));
        Raylib.DrawRectangle(0, top, 1, height, Scale(Palette.HudChrome, 0.6f));

        if (rows.Count == 0)
        {
            PixelFont.Draw("NOTHING SAID YET", Pad, top + 4, 1, Scale(Palette.HudChrome, 0.45f));
            return;
        }

        // Newest at the bottom and older ones climbing, which is the direction every chat in
        // the world reads. Scroll counts display rows back from the newest.
        int last = rows.Count - 1 - chat.Scroll;
        for (int i = 0; i < shown; i++)
        {
            int at = last - i;
            if (at < 0) break;
            int y = top + 5 + (shown - 1 - i) * LineHeight;
            // A wrapped continuation is indented, so a long message reads as one thing rather
            // than as two people talking.
            PixelFont.Draw(rows[at].Text, Pad + (rows[at].Wrapped ? 6 : 0), y, 1,
                Palette.HudChrome);
        }

        // Where you are in the scrollback, and only when you are not at the bottom of it —
        // otherwise it is a number that reads "0" for the entire life of the panel.
        if (chat.Scroll > 0)
            PixelFont.Draw($"-{chat.Scroll}", BoxWidth - 12, top + 2, 1,
                Scale(Palette.Flag, 0.8f));
    }

    /// <summary>The bottom of the instrument strip, which the history must never climb into —
    /// the radar overhangs it on the right and the bars own the left.</summary>
    private const int TopStrip = 44;

    /// <summary>Where the history stops and the line being typed begins.</summary>
    private const int Bottom = InputY - 14;

    /// <summary>How many characters fit across the box. The pixel font advances six per glyph at
    /// scale 1 — five wide plus a blank column — and getting that wrong as four was what ran the
    /// first version's text off the side of its own backing.</summary>
    private const int Columns = (BoxWidth + Pad) / (PixelFont.GlyphW + 1);

    private readonly record struct Row(string Text, bool Wrapped);

    /// <summary>
    /// Breaks the conversation into rows that fit the box.
    ///
    /// <para>Wrapped rather than truncated, which is a reversal: cutting each message at the box
    /// edge was simpler and kept the scroll offset counting <em>messages</em>, and at a third of
    /// a 320-pixel screen it left eighteen characters — which is not a sentence. "ACE: I HAVE
    /// BOTH FRAGMENTS" came out as "ACE: I HAVE BOTH >", and a history you cannot read is not a
    /// history. So the scroll counts display rows instead, which is the honest unit anyway: it
    /// is what the wheel moves past.</para>
    ///
    /// <para>Broken on spaces where there is one to break on, and mid-word only when a single
    /// word is longer than the box — a name can be sixteen characters and there is nowhere else
    /// to put it.</para>
    /// </summary>
    private static List<Row> Wrap(IReadOnlyList<string> lines)
    {
        var rows = new List<Row>();
        foreach (var line in lines)
        {
            string rest = line;
            bool wrapped = false;
            while (rest.Length > Columns)
            {
                int width = wrapped ? Columns - 1 : Columns;
                int cut = rest.LastIndexOf(' ', Math.Min(width, rest.Length - 1));
                if (cut <= 0) cut = width;
                rows.Add(new Row(rest[..cut].TrimEnd(), wrapped));
                rest = rest[cut..].TrimStart();
                wrapped = true;
            }
            if (rest.Length > 0 || !wrapped) rows.Add(new Row(rest, wrapped));
        }
        return rows;
    }

    private static Color Scale(Color c, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color((int)(c.R * t), (int)(c.G * t), (int)(c.B * t), (int)c.A);
    }
}
