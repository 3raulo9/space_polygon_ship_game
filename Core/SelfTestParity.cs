using System.Numerics;
using Unrendered.Entities;
using Unrendered.Net;
using Unrendered.World;

namespace Unrendered.Core;

/// <summary>
/// Whether a room is one game or several.
///
/// <para>Everything here is about the same question asked from the far side of the wire: the
/// host is never the machine that finds these, because on the host every one of them works. A
/// cue raised in a host-only path, a body only the host steps, a packet only the host writes —
/// each of them is perfect in a solo run and perfect in a two-player run played from the host's
/// chair, and each of them is a hole on somebody else's screen. So these checks are all written
/// from the client's point of view, and several of them drive the real
/// <see cref="Session"/> rather than calling the adopt methods directly: the bug that started
/// this file was a length guard in a packet reader that every hand-rolled test had bypassed.</para>
/// </summary>
public static partial class SelfTest
{
    private static int RunParityChecks()
    {
        int failures = 0;
        Console.WriteLine("-- host and client see the same game --");
        failures += Check("a gate's state survives the real packet, not just the adopt call",
            GateStateSurvivesTheRealWire);
        failures += Check("a rolled boss keeps moving on a client", RolledBossesAnimateOnAClient);
        failures += Check("a client drives the rolled boss's rotor", RolledBossHumReachesAClient);
        failures += Check("a craft hears its own low-hull alarm", TheAlarmReachesTheCraftItWarns);
        failures += Check("a private cue stays private", PersonalCuesDoNotGoToTheRoom);
        failures += Check("the run's own voice is heard by the machine running it",
            RunSignalsAreAudibleWhereTheyAreRaised);
        return failures;
    }

    // --- The gate ---------------------------------------------------------------------------

    /// <summary>
    /// The way off a planet, over the wire that ships rather than over a direct call.
    ///
    /// <see cref="GateSurvivesTheWire"/> already asserts that <c>NetAdoptGate</c> takes a gate
    /// faithfully — and it does, which is why the guard in front of it went unnoticed. This one
    /// puts a real <see cref="Session"/> on both ends so the packet is written and read by the
    /// code the game uses.
    /// </summary>
    private static string? GateStateSurvivesTheRealWire()
    {
        var rules = new MatchSettings
        { MaxPlayers = 4, Mode = GameMode.Descent, Destination = PlanetId.Solune };

        var net = new LoopbackNet(2, LinkQuality.Perfect, seed: 8192);
        var hostWorld = new World.World(null, rules, new Campaign(seed: 1234))
        { DynamicSpawning = false };
        hostWorld.Enemies.Clear();
        hostWorld.Pickups.Clear();
        var clientWorld = new World.World(null, rules) { DynamicSpawning = false, Authoritative = false };

        var host = new Session(net[0], host: true);
        var client = new Session(net[1], host: false);
        host.HostMatch(hostWorld);
        client.JoinMatch(clientWorld);
        client.SendHello(PlayerClass.Tank);
        for (int i = 0; i < 40; i++) { net.Advance(); host.PumpLobby(); client.PumpLobby(); }
        if (client.LocalSeat != 1) return $"the joiner never seated (at {client.LocalSeat})";

        // The host clears the run, claims an arch and seats two fragments in it.
        ClearTheColossus(hostWorld);
        Arch gate = hostWorld.Gates[0];
        if (!hostWorld.ClaimGate(gate, 0)) return "the host could not claim its own gate";
        host.BroadcastArchClaim(gate.StructureIndex);
        gate.SetDestination(PlanetId.Verene);
        gate.Feed(Fragment.Sun, 0);
        RunHaul(gate);
        gate.Feed(Fragment.Moon, 0);
        RunHaul(gate);

        for (int i = 0; i < 30; i++)
        {
            net.Advance();
            host.Pump(InputFrame.Empty);
            client.Pump(InputFrame.Empty);
        }

        if (clientWorld.ClaimedGate is not { } got)
            return "the client never heard which arch the room is using";
        if (got.StructureIndex != gate.StructureIndex)
            return "the client is standing at a different arch from the host";
        if (got.Filled != gate.Filled)
            return $"the host has {gate.Filled} fragments seated and the client sees {got.Filled}";
        if (got.Destination != gate.Destination)
            return "the course the gate is aimed at did not cross the wire";
        if (got.State != gate.State)
            return $"the host's gate is {gate.State} and the client's is {got.State}";
        return null;
    }

    // --- The rolled bosses ------------------------------------------------------------------

    /// <summary>A client's copy of a rolled boss, adopted exactly as a snapshot adopts one.</summary>
    private static World.World ClientWithARolledBoss(out ModularBoss body)
    {
        var w = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        w.Enemies.Clear();

        BossGenome gene = BossGen.Herald(seed: 5150, BossLineage.Stalker, difficulty: 0.5f);
        w.BeginAdoptRolled();
        w.AdoptRolled(gene, new Vector2(20f, 0f), heading: 0f, height: 4f,
            ModularBoss.State.Stalking, layersLeft: gene.Layers, topFraction: 1f);
        w.EndAdoptRolled();

        if (w.Bosses.Count != 1) throw new InvalidOperationException("the adopt did not stand a body up");
        body = w.Bosses[0];
        return w;
    }

    /// <summary>
    /// The gait and the core have to keep turning on a machine that is only being told where the
    /// body is. Nothing streams a boss's pose — it is a pure function of the phase, run on each
    /// machine's own clock — so if nothing on the client calls into that clock, the whole animal
    /// is a statue sliding around the city.
    /// </summary>
    private static string? RolledBossesAnimateOnAClient()
    {
        World.World w = ClientWithARolledBoss(out ModularBoss boss);
        float spinWas = boss.CoreSpin, gaitWas = boss.GaitPhase;

        for (int i = 0; i < 30; i++) StepWithoutInput(w);

        if (MathF.Abs(boss.CoreSpin - spinWas) < 1e-4f)
            return "half a second passed on a client and the boss's core had not turned at all";
        if (MathF.Abs(boss.GaitPhase - gaitWas) < 1e-4f)
            return "half a second passed on a client and the boss's gait had not moved at all";
        return null;
    }

    /// <summary>
    /// The rotor is the loudest continuous thing in a boss fight, and in DESCENT every boss is a
    /// rolled one. The bed the client drives is the one that only ever knew about the two
    /// hand-built monsters, so it silenced the channel for the whole of every fight.
    /// </summary>
    private static string? RolledBossHumReachesAClient()
    {
        World.World w = ClientWithARolledBoss(out _);
        StepWithoutInput(w);
        if (!w.BossHumOn)
            return "a boss was standing on a client's field and nothing was asking for its rotor";

        // And it lets go when the host stops describing the body.
        w.BeginAdoptRolled();
        w.EndAdoptRolled();
        StepWithoutInput(w);
        if (w.BossHumOn) return "the boss left the field and the rotor kept running";
        return null;
    }

    // --- The cues that belong to one person --------------------------------------------------

    /// <summary>
    /// The low-hull alarm is raised on the host, because damage is settled there and nowhere
    /// else — so a client never raises its own and has nothing to echo. It was being dropped by
    /// the very machine it exists to warn, and played by everybody else instead.
    /// </summary>
    private static string? TheAlarmReachesTheCraftItWarns()
    {
        if (TwoSeats(seed: 606) is not var (net, host, client, hostWorld, clientWorld))
            return "the joiner never seated";
        if (hostWorld.Players.Count < 2) return "the host never seated the joiner's craft";

        // Walk the client's craft down to where the alarm lives, then hurt it once more.
        PlayerTank mark = hostWorld.Players[1];
        mark.Shield = 0f;
        mark.Health = mark.MaxHealth * 0.6f;   // still comfortably above the line
        clientWorld.LastPlayedCue = null;
        hostWorld.HurtSeatForTest(1, mark.MaxHealth * 0.25f);   // and now under it

        PumpBoth(net, host, client, 12);

        if (clientWorld.LastPlayedCue != Cue.Warning)
            return "a client was driven to the danger line and never heard its own alarm "
                 + $"(it played {clientWorld.LastPlayedCue?.ToString() ?? "nothing"})";
        return null;
    }

    /// <summary>
    /// A cue marked personal belongs to one seat. The host already declines to play somebody
    /// else's; the wire carried no such rule, so a collect chime, a pack-full refusal and a
    /// low-hull alarm went to the whole room — and none of those clips carry any distance
    /// attenuation, so they arrived at full volume however far off they happened.
    /// </summary>
    private static string? PersonalCuesDoNotGoToTheRoom()
    {
        if (TwoSeats(seed: 909) is not var (net, host, client, hostWorld, clientWorld))
            return "the joiner never seated";

        // Seat 0's private business, raised right on top of the client so nothing else could
        // explain it being dropped.
        clientWorld.LastPlayedCue = null;
        int before = clientWorld.RemoteCuesPlayed;
        hostWorld.Emit(Cue.Pickup, hostWorld.Players[1].Position, owner: 0, personal: true);
        PumpBoth(net, host, client, 12);

        if (clientWorld.RemoteCuesPlayed != before)
            return "a client heard the chime for salvage somebody else drove over";

        // And the owner's own still reaches them, which is the whole reason these are on the
        // wire at all: a client never predicts a pickup, so this is its only account of one.
        clientWorld.LastPlayedCue = null;
        hostWorld.Emit(Cue.Pickup, hostWorld.Players[1].Position, owner: 1, personal: true);
        PumpBoth(net, host, client, 12);

        if (clientWorld.LastPlayedCue != Cue.Pickup)
            return "a client drove over salvage and never heard it collect";
        return null;
    }

    // --- The run's own voice -----------------------------------------------------------------

    /// <summary>
    /// The Colossus's scream and the maw's crystal are the director talking to the room. Raised
    /// <c>personal</c> with no owner, they reached nobody who raised them: the host never heard
    /// one, and a solo run — which has no client to broadcast to — never produced them at all.
    ///
    /// <para>Checked on the scream rather than on a wave opening, because a wave no longer
    /// signals anything: the alarm it used to sound is the loudest clip in the bank and a run
    /// opens five waves, so it was pulled and the line on the feed carries it instead.</para>
    /// </summary>
    private static string? RunSignalsAreAudibleWhereTheyAreRaised()
    {
        World.World w = DescentWorld();
        w.LastPlayedCue = null;
        ((IDescentField)w).Signal(Cue.CrabScream, 1f);
        if (w.LastPlayedCue != Cue.CrabScream)
            return "the run raised a signal on the one machine running it and nothing played it";

        // And it still reaches a room, which is the half that did work.
        if (TwoSeats(seed: 4141) is not var (net, host, client, hostWorld, clientWorld))
            return "the joiner never seated";
        clientWorld.LastPlayedCue = null;
        hostWorld.Emit(Cue.CrabScream, hostWorld.Players[1].Position, 1f);
        PumpBoth(net, host, client, 12);
        if (clientWorld.LastPlayedCue != Cue.CrabScream)
            return "the run's signals no longer reach the rest of the room";
        return null;
    }
}
