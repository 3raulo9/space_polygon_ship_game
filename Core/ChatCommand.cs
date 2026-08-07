namespace Unrendered.Core;

/// <summary>What a typed line turned out to be.</summary>
public enum ChatVerb : byte
{
    /// <summary>Not a command at all — say it to the room.</summary>
    Say,
    /// <summary><c>{skip ...}</c></summary>
    Skip,
    /// <summary><c>{item ...}</c></summary>
    Item,
    /// <summary>It was wrapped in braces but is not something we know how to do. Never sent to
    /// the room — a mistyped command must not become a message everybody reads.</summary>
    Unknown,
}

/// <summary>
/// One line the player typed, parsed.
///
/// <para>Pure text in, a decision out: no world, no session, no inventory. That separation is
/// what lets every rule about what a command <em>says</em> be tested exhaustively in a headless
/// run, leaving the world with nothing to do but carry out something already known to be
/// well-formed.</para>
/// </summary>
public readonly record struct ChatCommand(
    ChatVerb Verb,
    string Text,
    string Argument,
    ItemKind Item,
    int Amount,
    string Target,
    string? Complaint)
{
    /// <summary>Whether this needs to be the host (or a solo player) to run.</summary>
    public bool NeedsAuthority => Verb is ChatVerb.Skip or ChatVerb.Item;

    /// <summary>Whether the target names this player rather than somebody else.</summary>
    public bool TargetsSelf => Target.Equals(Self, StringComparison.OrdinalIgnoreCase);

    /// <summary>The word that means "the person typing".</summary>
    public const string Self = "ME";

    /// <summary>The longest a message may be. Long enough for a sentence, short enough that one
    /// line cannot paper over the bottom of somebody's screen — and short enough to be a single
    /// byte of length on the wire.</summary>
    public const int MaxLength = 96;

    /// <summary>
    /// Reads a typed line.
    ///
    /// <para>A command is the <em>whole</em> line wrapped in braces — <c>{skip wave}</c>. Not a
    /// prefix: braces at both ends means there is no ambiguity about where a command stops, and
    /// a half-typed one is simply not a command yet. Anything else, including a line that opens
    /// a brace and never closes it, is just something you said.</para>
    /// </summary>
    public static ChatCommand Parse(string line)
    {
        line = (line ?? "").Trim();
        if (line.Length > MaxLength) line = line[..MaxLength];

        if (line.Length < 2 || line[0] != '{' || line[^1] != '}')
            return new ChatCommand(ChatVerb.Say, line, "", ItemKind.Battery, 0, "", null);

        string body = line[1..^1].Trim();
        string[] parts = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return Bad(line, "THAT COMMAND IS EMPTY");

        switch (parts[0].ToUpperInvariant())
        {
            case "SKIP":
            {
                // Only one thing is skippable so far, and it is named rather than implied: a
                // bare {skip} would be a command whose meaning quietly changed the day a second
                // thing became skippable.
                if (parts.Length < 2) return Bad(line, "SKIP WHAT — TRY {SKIP WAVE}");
                string what = parts[1].ToUpperInvariant();
                if (what != "WAVE") return Bad(line, $"CANNOT SKIP \"{what}\"");
                return new ChatCommand(ChatVerb.Skip, line, "WAVE",
                    ItemKind.Battery, 0, "", null);
            }

            case "ITEM":
            {
                if (parts.Length < 4)
                    return Bad(line, "TRY {ITEM BULLET 100 ME}");

                if (!TryReadItem(parts[1], out ItemKind kind))
                    return Bad(line, $"\"{parts[1].ToUpperInvariant()}\" IS NOT A KIND — {ItemList}");

                if (!int.TryParse(parts[2], out int amount) || amount <= 0)
                    return Bad(line, "THE AMOUNT HAS TO BE A WHOLE NUMBER ABOVE ZERO");
                if (amount > MaxGive)
                    return Bad(line, $"{MaxGive} AT A TIME AT MOST");

                // The name may have spaces in it, so everything after the amount is the target
                // rather than only the next word.
                string who = string.Join(' ', parts[3..]).Trim();
                if (who.Length == 0) return Bad(line, "GIVE IT TO WHOM");

                return new ChatCommand(ChatVerb.Item, line, "", kind, amount, who, null);
            }
        }

        return Bad(line, $"\"{parts[0].ToUpperInvariant()}\" IS NOT A COMMAND");
    }

    /// <summary>The most one command may hand over. A whole pack is twenty slots and the
    /// biggest stack is twenty, so four hundred is "fill everything" — past that the number is
    /// not a quantity, it is a typo.</summary>
    public const int MaxGive = 400;

    /// <summary>
    /// Which items a command may conjure. Deliberately a short list rather than every
    /// <see cref="ItemKind"/>: this is a testing and hosting tool, and being able to type a
    /// fragment of the sun into existence would make the arch — and therefore the whole
    /// crossing — optional for anyone who read the help line.
    /// </summary>
    public static bool TryReadItem(string word, out ItemKind kind)
    {
        switch (word.ToUpperInvariant())
        {
            case "BATTERY": kind = ItemKind.Battery; return true;
            case "BULLET": kind = ItemKind.Bullet; return true;
            default: kind = ItemKind.Battery; return false;
        }
    }

    /// <summary>What the refusal prints when somebody names something that is not on the list.
    /// Built from the same place the list is read, so a kind added above is a kind named
    /// here.</summary>
    public const string ItemList = "TRY BATTERY OR BULLET";

    private static ChatCommand Bad(string line, string why)
        => new(ChatVerb.Unknown, line, "", ItemKind.Battery, 0, "", why);

    /// <summary>How the room sees a line somebody said.</summary>
    public static string Spoken(string who, string text) => $"{who}: {text}";
}
