using Unrendered.Core;
using Unrendered.Entities;

namespace Unrendered.World;

/// <summary>
/// The half of the chat that touches the game: carrying out a parsed command against the real
/// run and the real packs.
///
/// <para>Split from <see cref="ChatCommand"/> on the usual seam — that class decides what a line
/// <em>means</em> with no world in sight and is therefore exhaustively testable; this one does
/// the thing, and can assume it is being handed something well-formed.</para>
///
/// <para><b>Host only, always.</b> Every command here reaches into shared state — the run's phase
/// or somebody else's inventory — so a client running one locally would be a second referee, and
/// the correction a snapshot later would look like the game malfunctioning. A solo player is the
/// host of a room of one, which is what makes the same code path serve as the single-player
/// console.</para>
/// </summary>
public sealed partial class World
{
    /// <summary>
    /// Runs a command. Returns the line to show the person who typed it — every path returns
    /// something, because a command that appears to do nothing is indistinguishable from a
    /// broken game.
    /// </summary>
    /// <param name="by">The seat that typed it, for "ME" and for the announcement.</param>
    public string RunCommand(in ChatCommand cmd, int by) => cmd.Verb switch
    {
        ChatVerb.Skip => SkipCommand(by),
        ChatVerb.Item => ItemCommand(cmd, by),
        _ => cmd.Complaint ?? "NOTHING HAPPENED",
    };

    // --- {skip wave} -----------------------------------------------------------------------

    /// <summary>
    /// Skips whatever the run is currently doing.
    ///
    /// <para>One command that always does something, rather than one that only works during a
    /// crowd: in a wave it clears the roster, in a boss fight it kills the boss — dropping the
    /// fragment, so the planet stays finishable — and in a break it ends the break. That is the
    /// shape a testing command has to have, because the phase you want to get past is never the
    /// phase you happen to be in.</para>
    /// </summary>
    private string SkipCommand(int by)
    {
        if (Run is not { } run) return "THERE IS NO DESCENT TO SKIP";
        if (run.Finished) return "THE RUN IS ALREADY OVER";

        switch (run.Phase)
        {
            case DescentPhase.Landing:
                run.SkipLanding();
                return "SKIPPED THE LANDING";

            case DescentPhase.Wave:
            {
                // Everything on the field dies and everything still in the reserve is counted
                // as dead, so the wave is genuinely spent and the herald steps forward on its
                // own — rather than the bar being left half full for ever with nothing to fill
                // it, which is what merely clearing the field would do.
                int killed = 0;
                foreach (var e in Enemies) if (e.Alive) killed++;
                foreach (var s in Soldiers) if (s.Alive) killed++;
                ((IDescentField)this).SweepStragglers();
                run.SpendTheWave();
                return $"SKIPPED WAVE {run.Wave} — {killed} SWEPT";
            }

            case DescentPhase.Herald:
            case DescentPhase.Colossus:
            {
                // Killed rather than deleted, so everything a death is supposed to cause still
                // happens: the fragment hits the ground, the layers shed, the corpse pays out.
                // A boss quietly removed would leave a planet with four fragments on it and no
                // way to open an arch.
                //
                // Through KillOutright rather than a very large hit: damage is refused during
                // the arrival, between layers and while a PHASED body is away, and a command
                // that did nothing whenever it caught one of those would be untrustworthy.
                ModularBoss? boss = Headline;
                if (boss is null || !boss.KillOutright()) return "THERE IS NOTHING TO SKIP";
                // The death is run here rather than left to the tick's JustDied check, which is
                // cleared at the top of the boss's own Update and would swallow a kill staged
                // from outside the step — see KillOutright. This is the call that drops the
                // fragment, sheds the layers and pays the corpse out.
                BossWentDown(boss);
                return $"KILLED {boss.Gene.Name}";
            }

            case DescentPhase.Intermission:
                run.SkipTheBreak();
                return "ENDED THE SALVAGE WINDOW";
        }

        return "THERE IS NOTHING TO SKIP";
    }

    // --- {item kind amount who} -------------------------------------------------------------

    private string ItemCommand(in ChatCommand cmd, int by)
    {
        if (!FindSeat(cmd, by, out int seat, out string? complaint)) return complaint!;

        // What actually fits. Add returns the overflow, so this is the honest count rather than
        // what was asked for — a pack with two free slots takes forty rounds out of a hundred
        // and the player is told which.
        int leftover = InventoryOf(seat).Add(cmd.Item, cmd.Amount);
        int given = cmd.Amount - leftover;
        string what = ItemNames.Of(cmd.Item);
        string who = seat == by ? "YOU" : NameOrSeat(seat);

        if (given <= 0) return $"{who} HAVE NO ROOM FOR {what}";

        // The receiver is told too, when it is not the person who typed it — things silently
        // appearing in your pack mid-run is the sort of thing that reads as a bug.
        if (seat != by) Whisper(seat, $"{NameOrSeat(by)} GAVE YOU {given} {what}");

        return leftover > 0
            ? $"GAVE {who} {given} OF {cmd.Amount} {what} — PACK FULL"
            : $"GAVE {who} {given} {what}";
    }

    /// <summary>
    /// Works out which seat a command's target names. "ME" is the person typing; anything else
    /// is a nickname, matched whole and without case.
    ///
    /// <para>Whole rather than by prefix, deliberately, even though the chat's Up key will
    /// complete a name for you. The completion is there so nobody has to type a long name; the
    /// matching is strict so a hundred rounds never land in the wrong pack because two people
    /// happened to start with the same letter.</para>
    /// </summary>
    private bool FindSeat(in ChatCommand cmd, int by, out int seat, out string? complaint)
    {
        seat = by;
        complaint = null;
        if (cmd.TargetsSelf) return true;

        for (int i = 0; i < Players.Count; i++)
        {
            if (!NameOf(i).Equals(cmd.Target, StringComparison.OrdinalIgnoreCase)) continue;
            seat = i;
            return true;
        }

        seat = -1;
        complaint = $"NOBODY HERE IS CALLED \"{cmd.Target}\"";
        return false;
    }

    /// <summary>
    /// Something said to one seat and nobody else. Raised through the announce channel so it
    /// reaches a client the same way every other line does; the seat filter is applied by the
    /// session, which is the only thing that knows who is on the far end of which socket.
    /// </summary>
    public Action<int, string>? Tell { get; set; }

    private void Whisper(int seat, string line) => Tell?.Invoke(seat, line);
}
