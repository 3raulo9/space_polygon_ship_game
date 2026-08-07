using Unrendered.Entities;
using Unrendered.UI;

namespace Unrendered.Core;

/// <summary>
/// Headless checks for the chat and its console: what a typed line parses to, what a command
/// does to the run and to a pack, who is allowed to run one, and how the feed treats a sentence
/// against the noise of a firefight.
///
/// <para>None of it needs a window. <see cref="ChatCommand"/> is pure text-in-decision-out and
/// the completion's rule was pulled out of <see cref="ChatBox"/> for exactly this reason — the
/// parts of a console that are worth getting right are all decisions, and a decision that can
/// only be checked by typing into a running game is a decision nobody will check.</para>
/// </summary>
public static partial class SelfTest
{
    private static int RunChatChecks()
    {
        int failures = 0;
        failures += Check("plain text is a message and braces make it a command", BracesMakeACommand);
        failures += Check("a malformed command is refused and never said out loud", BadCommandsAreNeverSpoken);
        failures += Check("a command can only conjure the two kinds it is allowed", ItemKindsAreWhitelisted);
        failures += Check("an amount has to be a number, positive, and not absurd", AmountsAreBounded);
        failures += Check("{item} fills what it can and says what it could not", ItemGivesWhatFits);
        failures += Check("{item} reaches another player by name, and nobody else", ItemFindsPlayersByName);
        failures += Check("{skip wave} does something in every phase of a run", SkipWorksInEveryPhase);
        failures += Check("only the host may run a command in a room", CommandsAreTheHostsToGive);
        failures += Check("what somebody said outlives what the game was shouting", ChatOutlivesTheNoise);
        failures += Check("the chat remembers what was said and not what was announced", HistoryIsOnlyConversation);
        failures += Check("pressing up completes a nickname from what is typed", NamesCompleteFromWhatIsTyped);
        return failures;
    }

    // --- Parsing -----------------------------------------------------------------------------

    private static string? BracesMakeACommand()
    {
        var said = ChatCommand.Parse("COVER ME");
        if (said.Verb != ChatVerb.Say) return "ordinary text was taken for a command";
        if (said.Text != "COVER ME") return "a message was mangled on the way through";

        var skip = ChatCommand.Parse("{skip wave}");
        if (skip.Verb != ChatVerb.Skip) return "{skip wave} was not read as a command";
        if (skip.Argument != "WAVE") return "{skip wave} did not name what it skips";
        if (!skip.NeedsAuthority) return "{skip wave} is not marked as needing the host";

        var give = ChatCommand.Parse("{item bullet 100 me}");
        if (give.Verb != ChatVerb.Item) return "{item ...} was not read as a command";
        if (give.Item != ItemKind.Bullet) return "the kind did not survive the parse";
        if (give.Amount != 100) return "the amount did not survive the parse";
        if (!give.TargetsSelf) return "\"me\" was not read as the person typing";

        // Case and spacing are not part of the syntax. Somebody typing in a hurry with the
        // shift key down must not be told their command is wrong.
        var loud = ChatCommand.Parse("  {  ITEM   BATTERY  4   ME  }  ");
        if (loud.Verb != ChatVerb.Item || loud.Item != ItemKind.Battery || loud.Amount != 4)
            return "a command in capitals with loose spacing was refused";

        // Half a command is not a command. A brace that is opened and never closed is somebody
        // typing, and it must reach the room as what it is.
        var half = ChatCommand.Parse("{item bullet 100 me");
        if (half.Verb != ChatVerb.Say) return "an unclosed brace was treated as a command";

        // And a name may have spaces in it, so everything past the amount is the target.
        var spaced = ChatCommand.Parse("{item bullet 5 THE ACE}");
        if (spaced.Target != "THE ACE") return $"a two-word name came through as \"{spaced.Target}\"";
        return null;
    }

    private static string? BadCommandsAreNeverSpoken()
    {
        // Every one of these is wrapped in braces and none of them is runnable. The important
        // half is the Verb: an Unknown never reaches the room, so a mistyped command cannot
        // become a message nineteen people read.
        string[] wrong =
        {
            "{}", "{ }", "{fly}", "{skip}", "{skip everything}",
            "{item}", "{item bullet}", "{item bullet 10}",
        };
        foreach (var line in wrong)
        {
            var cmd = ChatCommand.Parse(line);
            if (cmd.Verb != ChatVerb.Unknown)
                return $"\"{line}\" parsed as {cmd.Verb} rather than being refused";
            if (string.IsNullOrEmpty(cmd.Complaint))
                return $"\"{line}\" was refused without saying why";
        }
        return null;
    }

    private static string? ItemKindsAreWhitelisted()
    {
        if (!ChatCommand.TryReadItem("battery", out var b) || b != ItemKind.Battery)
            return "battery is not on the list";
        if (!ChatCommand.TryReadItem("BULLET", out var r) || r != ItemKind.Bullet)
            return "bullet is not on the list";

        // The list is short on purpose. Being able to type a fragment into existence would make
        // the arch — and with it the whole crossing — optional for anyone who read the syntax,
        // and a moonstone is the rarest thing in the game.
        foreach (var word in new[] { "sunfragment", "moonfragment", "moonstone", "repairkit",
                                     "crabcore", "scrapmetal", "" })
            if (ChatCommand.TryReadItem(word, out _))
                return $"\"{word}\" can be conjured and should not be";
        return null;
    }

    private static string? AmountsAreBounded()
    {
        foreach (var bad in new[] { "{item bullet 0 me}", "{item bullet -5 me}",
                                    "{item bullet lots me}", "{item bullet 99999 me}" })
            if (ChatCommand.Parse(bad).Verb != ChatVerb.Unknown)
                return $"\"{bad}\" was accepted";

        if (ChatCommand.Parse($"{{item bullet {ChatCommand.MaxGive} me}}").Verb != ChatVerb.Item)
            return "the largest allowed amount was refused";
        return null;
    }

    // --- What a command does -----------------------------------------------------------------

    private static string? ItemGivesWhatFits()
    {
        var w = new World.World(null, MatchSettings.SinglePlayer) { DynamicSpawning = false };
        w.InventoryOf(0).Slots.AsSpan().Clear();

        string reply = w.RunCommand(ChatCommand.Parse("{item bullet 100 me}"), 0);
        if (CountOf(w, 0, ItemKind.Bullet) != 100)
            return $"100 bullets came out as {CountOf(w, 0, ItemKind.Bullet)}";
        if (!reply.Contains("100")) return "the reply did not say how many arrived";

        // A pack with nowhere to put it takes what fits and says so, rather than silently
        // losing the rest or dropping forty pieces of salvage on the floor.
        var full = new World.World(null, MatchSettings.SinglePlayer) { DynamicSpawning = false };
        Inventory inv = full.InventoryOf(0);
        for (int i = 0; i < Inventory.SlotCount; i++)
            inv.Slots[i] = new ItemStack(ItemKind.ScrapMetal, Inventory.MaxStack(ItemKind.ScrapMetal));

        string refused = full.RunCommand(ChatCommand.Parse("{item bullet 100 me}"), 0);
        if (CountOf(full, 0, ItemKind.Bullet) != 0) return "a full pack took bullets anyway";
        if (!refused.Contains("ROOM")) return $"a full pack answered \"{refused}\"";

        // One slot free is 20 of 100, and the shortfall is reported rather than swallowed.
        inv.Slots[3] = ItemStack.Empty;
        string partial = full.RunCommand(ChatCommand.Parse("{item bullet 100 me}"), 0);
        int got = CountOf(full, 0, ItemKind.Bullet);
        if (got != Inventory.MaxStack(ItemKind.Bullet))
            return $"one free slot took {got} bullets";
        if (!partial.Contains("OF 100")) return $"a partial give answered \"{partial}\"";
        return null;
    }

    private static string? ItemFindsPlayersByName()
    {
        var w = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        if (w.AddPlayer(new Loadout { Class = PlayerClass.Tank }) == null)
            return "the match refused a second seat";
        w.SeatNames[0] = "HOST";
        w.SeatNames[1] = "ACE";
        w.InventoryOf(0).Slots.AsSpan().Clear();
        w.InventoryOf(1).Slots.AsSpan().Clear();

        // Naming somebody else reaches their pack and nobody else's.
        w.RunCommand(ChatCommand.Parse("{item battery 3 ACE}"), by: 0);
        if (CountOf(w, 1, ItemKind.Battery) != 3) return "a named player did not get the items";
        if (CountOf(w, 0, ItemKind.Battery) != 0) return "the giver got them too";

        // Case does not matter — every name in this game is displayed in capitals anyway.
        w.RunCommand(ChatCommand.Parse("{item battery 1 ace}"), by: 0);
        if (CountOf(w, 1, ItemKind.Battery) != 4) return "a lowercase name did not match";

        // A partial name does not. The chat's Up key exists so nobody has to type a long name;
        // the matching stays strict so a hundred rounds never land in the wrong pack.
        string miss = w.RunCommand(ChatCommand.Parse("{item battery 9 AC}"), by: 0);
        if (CountOf(w, 1, ItemKind.Battery) != 4) return "a partial name reached somebody's pack";
        if (!miss.Contains("NOBODY")) return $"a bad name answered \"{miss}\"";
        return null;
    }

    private static string? SkipWorksInEveryPhase()
    {
        // Landing.
        World.World w = ConsoleDescent();
        if (w.Run!.Phase != DescentPhase.Landing) return "a run did not open on the landing";
        w.RunCommand(ChatCommand.Parse("{skip wave}"), 0);
        StepWithoutInput(w);
        if (w.Run.Phase != DescentPhase.Wave) return "skipping the landing did not open the wave";

        // A crowd. The roster is spent AND the field swept, so the director's own rule opens
        // the herald — a skip that only cleared the field would leave the bar half full with
        // nothing left alive to empty it, and the run would hang there for ever.
        for (int i = 0; i < 200 && w.Run.Phase == DescentPhase.Wave; i++)
        {
            StepWithoutInput(w);
            if (w.Enemies.Count > 0) break;
        }
        w.RunCommand(ChatCommand.Parse("{skip wave}"), 0);
        for (int i = 0; i < 600 && w.Run.Phase == DescentPhase.Wave; i++) StepWithoutInput(w);
        if (w.Run.Phase != DescentPhase.Herald)
            return $"skipping a wave left the run on {w.Run.Phase}";

        // A boss. Killed rather than deleted, so the fragment still hits the ground — a boss
        // quietly removed would leave a planet with four fragments and no way to open an arch.
        int before = w.Run.Dropped;
        w.RunCommand(ChatCommand.Parse("{skip wave}"), 0);
        for (int i = 0; i < 600 && w.Run.Phase == DescentPhase.Herald; i++) StepWithoutInput(w);
        if (w.Run.Dropped != before + 1) return "a skipped boss did not give up its fragment";
        bool onGround = false;
        foreach (var pk in w.Pickups) if (World.World.IsFragment(pk)) onGround = true;
        if (!onGround) return "a skipped boss's fragment never reached the grid";
        if (w.Run.Phase != DescentPhase.Intermission)
            return $"a skipped boss left the run on {w.Run.Phase}";

        // A break.
        w.RunCommand(ChatCommand.Parse("{skip wave}"), 0);
        for (int i = 0; i < 200 && w.Run.Phase == DescentPhase.Intermission; i++)
            StepWithoutInput(w);
        if (w.Run.Phase != DescentPhase.Wave) return "skipping a break did not open the next wave";

        // And in SANDBOX there is nothing to skip, said plainly rather than crashing.
        var sandbox = new World.World(null, MatchSettings.SinglePlayer) { DynamicSpawning = false };
        string none = sandbox.RunCommand(ChatCommand.Parse("{skip wave}"), 0);
        if (!none.Contains("NO DESCENT")) return $"a sandbox answered \"{none}\"";
        return null;
    }

    // --- Who may -----------------------------------------------------------------------------

    private static string? CommandsAreTheHostsToGive()
    {
        // Both halves of the rule are on the command itself, so the seam cannot be forgotten by
        // one of the two places that route a typed line.
        if (!ChatCommand.Parse("{skip wave}").NeedsAuthority)
            return "{skip wave} does not claim to need the host";
        if (!ChatCommand.Parse("{item bullet 1 me}").NeedsAuthority)
            return "{item ...} does not claim to need the host — handing out items is a control";
        if (ChatCommand.Parse("HELLO").NeedsAuthority)
            return "an ordinary message claims to need the host";
        if (ChatCommand.Parse("{nonsense}").NeedsAuthority)
            return "a refused command claims to need the host, which would hide the real reason";
        return null;
    }

    // --- The feed ----------------------------------------------------------------------------

    private static string? ChatOutlivesTheNoise()
    {
        var feed = new Net.NoticeFeed();
        feed.Push("ACE: COVER ME", Net.NoticeFeed.Kind.Chat);

        // A firefight. Six seconds of kill announces would have pushed a five-line feed clean
        // over twice, and before this rule the sentence went with them.
        for (int i = 0; i < 12; i++) feed.Push($"ACE KILLED A HUNTER {i}");

        bool held = false;
        foreach (var e in feed.Entries) if (e.Text.Contains("COVER ME")) held = true;
        if (!held) return "a firefight pushed what somebody said off the screen";

        // And it lives longer once there: a notice is gone at six seconds and a chat line is not.
        feed.Age(Net.NoticeFeed.Life + 0.5f);
        held = false;
        foreach (var e in feed.Entries)
        {
            if (e.Text.Contains("KILLED")) return "a notice outlived its own life";
            if (e.Text.Contains("COVER ME")) held = true;
        }
        if (!held) return "a chat line expired on the notice clock";

        feed.Age(Net.NoticeFeed.ChatLife);
        if (feed.Entries.Count != 0) return "a chat line never expires at all";
        return null;
    }

    private static string? HistoryIsOnlyConversation()
    {
        var feed = new Net.NoticeFeed();
        feed.Push("ACE JOINED");
        feed.Push("ACE: HELLO", Net.NoticeFeed.Kind.Chat);
        feed.Push("ACE KILLED AN ELITE");
        feed.Push("GAVE YOU 100 ROUNDS", Net.NoticeFeed.Kind.Console);

        // Nobody scrolls back to re-read who killed a hunter forty minutes ago.
        if (feed.History.Count != 2)
            return $"the history kept {feed.History.Count} lines of the 2 worth keeping";
        if (feed.History[0] != "ACE: HELLO") return "the history lost what was said";

        // It survives the lines expiring off the screen, which is the entire point of it.
        feed.Age(Net.NoticeFeed.ChatLife * 2f);
        if (feed.Entries.Count != 0) return "the feed did not clear";
        if (feed.History.Count != 2) return "the history went with the feed";

        // And it is bounded, so a very long session cannot grow without limit.
        for (int i = 0; i < Net.NoticeFeed.HistoryCap * 2; i++)
            feed.Push($"LINE {i}", Net.NoticeFeed.Kind.Chat);
        if (feed.History.Count != Net.NoticeFeed.HistoryCap)
            return $"the history grew to {feed.History.Count}";
        if (feed.History[^1] != $"LINE {Net.NoticeFeed.HistoryCap * 2 - 1}")
            return "the history dropped the newest line rather than the oldest";
        return null;
    }

    // --- Completion ---------------------------------------------------------------------------

    private static string? NamesCompleteFromWhatIsTyped()
    {
        var room = new[] { "ACE", "ANVIL", "MERC", "BRICK" };

        // The word being completed is whatever follows the last space, so the command in front
        // of it is left alone.
        if (ChatBox.StemOf("{item bullet 100 a") != "a")
            return "the completion tried to complete the whole line";
        if (ChatBox.StemOf("ACE") != "ACE") return "a single word is not its own stem";

        var fromA = ChatBox.MatchesFor("{item bullet 100 a", room);
        if (fromA.Count != 2 || fromA[0] != "ACE" || fromA[1] != "ANVIL")
            return "\"a\" did not offer the two names beginning with it, in roster order";

        // Case-insensitive, because every name is displayed in capitals and nobody types the
        // shift key mid-command.
        if (ChatBox.MatchesFor("{item bullet 1 ME", room).Count != 1)
            return "an uppercase stem did not match a name";

        // An empty word offers everybody, which is what makes Up on a blank a walk through the
        // room rather than nothing happening.
        if (ChatBox.MatchesFor("{item bullet 100 ", room).Count != room.Length)
            return "an empty word did not offer the whole roster";

        // And something nobody is called offers nothing rather than the first name in the list.
        if (ChatBox.MatchesFor("{item bullet 100 zz", room).Count != 0)
            return "a stem nobody matches still offered a name";
        return null;
    }

    // --- Helpers -------------------------------------------------------------------------------

    /// <summary>A DESCENT world with nothing hostile seeded, for the console to poke at.</summary>
    private static World.World ConsoleDescent()
    {
        var w = new World.World(null,
            new MatchSettings { MaxPlayers = 1, Mode = GameMode.Descent },
            new Campaign(seed: 77))
        { DynamicSpawning = false };
        w.Enemies.Clear();
        w.Pickups.Clear();
        return w;
    }

    private static int CountOf(World.World w, int seat, ItemKind kind)
    {
        int n = 0;
        foreach (var s in w.InventoryOf(seat).Slots)
            if (!s.IsEmpty && s.Kind == kind) n += s.Count;
        return n;
    }
}
