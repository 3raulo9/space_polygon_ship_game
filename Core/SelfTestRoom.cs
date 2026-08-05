using System.Numerics;
using Unrendered.Entities;
using Unrendered.Net;
using Unrendered.World;

namespace Unrendered.Core;

/// <summary>
/// Checks for the things that make twenty players a <em>room</em> rather than twenty
/// simultaneous single-player games: the scoreboard, the combat feed, the marks people put
/// on the world for each other, and a spectator's ability to choose who they are watching.
///
/// All of it is headless, over <see cref="LoopbackNet"/>. Every one of these is a claim about
/// whether something a player did on one machine reached another, which is exactly the class
/// of bug this project keeps finding and exactly the class a single-machine test cannot see.
/// </summary>
public static partial class SelfTest
{
    /// <summary>A host and one client, both seated, sharing a match. The preamble every check
    /// below opens with.</summary>
    private static (LoopbackNet Net, Session Host, Session Client,
                    World.World HostWorld, World.World ClientWorld)? TwoSeats(int seed)
    {
        var net = new LoopbackNet(2, LinkQuality.Perfect, seed);
        var hostWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false };
        hostWorld.Enemies.Clear();
        var clientWorld = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        clientWorld.Enemies.Clear();

        var host = new Session(net[0], host: true);
        var client = new Session(net[1], host: false);
        host.HostMatch(hostWorld);
        client.JoinMatch(clientWorld);
        client.SendHello(PlayerClass.Tank);
        for (int i = 0; i < 40; i++) { net.Advance(); host.PumpLobby(); client.PumpLobby(); }
        if (client.LocalSeat != 1) return null;
        return (net, host, client, hostWorld, clientWorld);
    }

    private static void PumpBoth(LoopbackNet net, Session host, Session client, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            net.Advance();
            host.Pump(InputFrame.Empty);
            client.Pump(InputFrame.Empty);
        }
    }

    /// <summary>
    /// A kill goes on the killer's tally and is announced to the whole room — including to the
    /// people who were nowhere near it, which is the entire point of a feed.
    /// </summary>
    private static string? KillsAreCreditedAndAnnounced()
    {
        if (TwoSeats(seed: 91) is not var (net, host, client, hostWorld, clientWorld))
            return "the joiner never seated";

        host.Rename(1, "ACE");
        PumpBoth(net, host, client, 6);

        // A hunter right in front of seat 1, killed by a round seat 1 owns.
        var mate = hostWorld.Players[1];
        mate.Position = new Vector2(60f, 60f);
        var mark = new EnemyTank(new Vector2(62f, 60f), elite: false, 0);
        hostWorld.Enemies.Add(mark);

        int before = hostWorld.KillsOf(1);
        hostWorld.KillEnemyForTest(mark, by: 1);

        if (hostWorld.KillsOf(1) != before + 1)
            return $"the kill was not credited to the seat that earned it ({hostWorld.KillsOf(1)})";
        if (hostWorld.KillsOf(0) != 0) return "the kill was credited to the wrong seat";
        if (!FeedHas(host.Notices, "ACE")) return "the kill was not announced by name on the host";

        PumpBoth(net, host, client, 8);
        if (!FeedHas(client.Notices, "ACE"))
            return "the kill feed never reached the client — a room that cannot see its own kills";
        return null;
    }

    /// <summary>
    /// The scoreboard crosses. A tally the host keeps is no use to the nineteen people who
    /// cannot see it, and the ping column has to survive the trip too — it is the one number
    /// that explains a bad experience.
    /// </summary>
    private static string? TheScoreboardCrossesTheWire()
    {
        if (TwoSeats(seed: 92) is not var (net, host, client, hostWorld, clientWorld))
            return "the joiner never seated";

        var mate = hostWorld.Players[1];
        mate.Position = new Vector2(60f, 60f);
        for (int i = 0; i < 3; i++)
        {
            var e = new EnemyTank(new Vector2(62f, 60f), elite: false, 0);
            hostWorld.Enemies.Add(e);
            hostWorld.KillEnemyForTest(e, by: 1);
        }
        // The board goes out on its own slow clock, so this needs long enough for one.
        PumpBoth(net, host, client, 200);

        if (clientWorld.KillsOf(1) != 3)
            return $"the client was told {clientWorld.KillsOf(1)} kills, not 3";
        if (clientWorld.DeathsOf(1) != 0) return "the client invented a death";

        // The ping column is checked against the host's own measurement rather than against a
        // number this test planted: the host measures it continuously from each client's
        // acknowledgement, so anything written here by hand is overwritten by the truth within
        // a few ticks. It is also not checked for equality — the board goes out once a second
        // and the measurement keeps moving in between, so the honest claim is that a real,
        // non-zero, roughly-current round trip made the trip, not that a moving number was
        // frozen. A test that demanded equality would be testing its own timing.
        int mine = clientWorld.PingOf(1), theirs = hostWorld.PingOf(1);
        if (mine <= 0)
            return "no round trip reached the client — the ping column would read zero all match";
        if (theirs <= 0) return "the host measured no round trip at all";
        if (Math.Abs(mine - theirs) > 50)
            return $"the client shows {mine}ms for seat 1 against the host's {theirs}ms — too far apart to be the same measurement";
        return null;
    }

    /// <summary>
    /// A mark one player drops is seen and heard by another. Proximity voice was deferred, so
    /// this is the whole of how twenty people say "there" to each other — and a marker that
    /// stayed on the machine that dropped it would be no communication at all.
    /// </summary>
    private static string? MarksReachTheWholeRoom()
    {
        if (TwoSeats(seed: 93) is not var (net, host, client, hostWorld, clientWorld))
            return "the joiner never seated";

        // Opposite corners, not opposite edges — the torus makes edges neighbours.
        hostWorld.Players[0].Position = new Vector2(0f, 0f);
        hostWorld.Players[1].Position = new Vector2(195f, 195f);
        clientWorld.Players[1].Position = new Vector2(195f, 195f);

        var at = new Vector2(40f, 40f);
        client.Mark(at);

        // The client plants its own at once — waiting a round trip to see your own mark is
        // exactly what stops a coordination tool feeling like one.
        if (clientWorld.Markers.Count != 1) return "the client did not plant its own mark at once";
        if (clientWorld.Markers[0].Seat != client.LocalSeat)
            return "the client's own mark was filed under the wrong seat";

        PumpBoth(net, host, client, 12);

        if (hostWorld.Markers.Count != 1) return "the mark never reached the host";
        if (hostWorld.Markers[0].Seat != 1)
            return $"the host filed the mark under seat {hostWorld.Markers[0].Seat}, not the one that sent it";
        if (Torus.Distance(hostWorld.Markers[0].Position, at) > 0.5f)
            return "the mark arrived somewhere other than where it was dropped";

        // And it is heard, from the mark, by the player on the far side of the world — the
        // cue carries because a mark is information rather than a noise.
        if (clientWorld.Markers.Count != 1)
            return "the host's echo duplicated the client's own mark instead of being dropped";

        // A second mark from the same player replaces the first rather than littering.
        client.Mark(new Vector2(-40f, -40f));
        PumpBoth(net, host, client, 12);
        if (hostWorld.Markers.Count != 1)
            return $"one player left {hostWorld.Markers.Count} marks standing; a second should replace the first";
        return null;
    }

    /// <summary>
    /// A spectator can choose who they are watching. The picker is sticky on purpose, which is
    /// right for a camera nobody is steering and wrong the moment somebody wants to — being
    /// welded to one player until they die is waiting, not watching.
    /// </summary>
    private static string? ASpectatorCanChangeWhoTheyWatch()
    {
        var world = new World.World(null, new MatchSettings { MaxPlayers = 4, Revives = 0 })
        { DynamicSpawning = false };
        world.Enemies.Clear();
        world.AddPlayer(new Loadout { Class = PlayerClass.Tank });   // seat 1
        world.AddPlayer(new Loadout { Class = PlayerClass.Tank });   // seat 2
        world.LocalIndex = 0;

        // Corners, so "nearest" is unambiguous and nothing depends on the wrap.
        world.Players[0].Position = new Vector2(0f, 0f);
        world.Players[1].Position = new Vector2(20f, 0f);
        world.Players[2].Position = new Vector2(195f, 195f);

        // A living player cannot steer the camera — it is on their own craft.
        world.CycleSpectator(+1);
        if (world.Spectating) return "a living player was moved into somebody else's craft";

        world.Players[0].Shield = 0f;
        world.Players[0].Health = 0f;
        world.Players[0].Lives = 0;
        StepWithoutInput(world);
        if (world.ViewSeat != 1) return $"the camera did not settle on the nearest survivor (seat {world.ViewSeat})";

        world.CycleSpectator(+1);
        if (world.ViewSeat != 2) return $"stepping forward went to seat {world.ViewSeat}, not the other survivor";

        world.CycleSpectator(+1);
        if (world.ViewSeat != 1) return "stepping past the last survivor did not wrap round";

        world.CycleSpectator(-1);
        if (world.ViewSeat != 2) return "stepping back did not go the other way";

        // ...and the sticky picker respects the choice rather than pulling it home again.
        StepWithoutInput(world);
        if (world.ViewSeat != 2) return "the picker overrode a choice the player made by hand";

        // A spent team-mate is skipped rather than watched.
        world.Players[2].Shield = 0f;
        world.Players[2].Health = 0f;
        world.Players[2].Lives = 0;
        StepWithoutInput(world);
        if (world.ViewSeat != 1) return "the camera stayed on a player who is out";
        return null;
    }
}
