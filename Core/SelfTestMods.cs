using System.Numerics;
using Unrendered.Entities;
using Unrendered.Modding;
using Unrendered.Net;

namespace Unrendered.Core;

/// <summary>
/// Headless checks for the mods folder and the first thing in it: the loader, the join check
/// that keeps two machines on the same files, and the swords — taken by walking over them,
/// swung on FIRE, let go of, dropped by the dead, and carried across the wire.
///
/// <para>Every check stands up exactly the mods it needs and the block puts the registry back
/// to empty when it is done, so nothing here leaks into the checks that run after it — the
/// rest of the suite was written for a game with no mods installed and must still be one.</para>
/// </summary>
public static partial class SelfTest
{
    private static int RunModChecks()
    {
        int failures = 0;
        try
        {
            failures += Check("the shipped swords mod loads two blades", ShippedSwordsLoad);
            failures += Check("a broken mod file is refused with a reason, not a crash", BrokenModsSayWhy);
            failures += Check("the mods folder reads every .json and nothing else", FolderScanReadsEveryJson);
            failures += Check("the mod fingerprint follows the content, not the comments", FingerprintFollowsContent);
            failures += Check("a match opens with the field's swords on the grid", FieldOpensWithSwords);
            failures += Check("a soldier walks over a sword and holds it; a tank does not", SoldierTakesASword);
            failures += Check("FIRE swings a held sword instead of the rifle", FireSwingsTheSword);
            failures += Check("a swing cuts what is in front and spares what is not", SwingCutsTheArc);
            failures += Check("a swing waits out its cooldown", SwingKeepsItsPace);
            failures += Check("letting go puts it down, and not straight back in the hand", DropPutsItDown);
            failures += Check("a dead soldier can drop a blade, and it falls to the grid", DeadSoldierDropsABlade);
            failures += Check("a held sword goes down with a spent life", SwordGoesDownWithTheLife);
            failures += Check("swords on the grid and in hand cross the wire", SwordsCrossTheWire);
            failures += Check("a guest with different mods is refused at the door", MismatchedModsAreRefused);
        }
        finally
        {
            Mods.Install(Array.Empty<ModInfo>(), log: false);
        }
        return failures;
    }

    // --- Helpers -----------------------------------------------------------------------------

    private static string ShippedSwordsPath => Path.Combine(AppContext.BaseDirectory, "Mods", "swords.json");

    /// <summary>Installs the swords file the game ships, exactly as the build copied it.</summary>
    private static ModInfo InstallSwords()
    {
        ModInfo mod = Mods.Parse(File.ReadAllText(ShippedSwordsPath), "swords.json");
        Mods.Install(new[] { mod }, log: false);
        return mod;
    }

    /// <summary>A soldier on a quiet stage with the swords installed and the opening scatter
    /// swept off it, so each check puts down exactly the weapons it is about.</summary>
    private static World.World SwordWorld(PlayerClass chassis = PlayerClass.Soldier,
        MatchSettings? match = null)
    {
        InstallSwords();
        var world = new World.World(new Loadout { Class = chassis }, match) { DynamicSpawning = false };
        world.Enemies.Clear();
        world.Soldiers.Clear();
        world.Squads.Clear();
        world.GroundWeapons.Clear();
        return world;
    }

    private static void StepFor(World.World world, float seconds, Btn down = Btn.None)
    {
        int ticks = (int)MathF.Ceiling(seconds / (float)Config.FixedDt);
        for (int i = 0; i < ticks; i++)
            world.Update((float)Config.FixedDt, new InputFrame(down, Btn.None, Vector2.Zero));
    }

    private static void Press(World.World world, Btn b)
        => world.Update((float)Config.FixedDt, new InputFrame(b, b, Vector2.Zero));

    // --- The loader ------------------------------------------------------------------------

    private static string? ShippedSwordsLoad()
    {
        if (!File.Exists(ShippedSwordsPath)) return $"the build did not copy {ShippedSwordsPath}";
        ModInfo mod = InstallSwords();
        if (!mod.Loaded) return "swords.json failed: " + mod.Error;
        if (mod.Id != "swords") return $"its id came out as \"{mod.Id}\"";
        if (Mods.Weapons.Count != 2) return $"it added {Mods.Weapons.Count} weapons, wanted 2";

        ModWeapon sword = Mods.Weapons[0];
        if (sword.Key != "swords:sword" || sword.Name != "SWORD") return $"the first weapon is {sword.Key}";
        if (sword.Damage != 3f || sword.PlayerDamage != 25f) return "the sword's damage did not survive the read";
        if (MathF.Abs(sword.Reach - 3.2f) > 1e-4f) return "the sword's reach did not survive the read";
        if (sword.FieldCount != 3 || MathF.Abs(sword.SoldierDrop - 0.25f) > 1e-4f)
            return "the sword's spawn rules did not survive the read";
        if (sword.Model.Length != 6) return $"the sword is {sword.Model.Length} boxes, wanted 6";
        if (MathF.Abs(sword.Length - 0.72f) > 1e-4f) return $"the sword's tip is at {sword.Length}";
        if (sword.Index != 0 || Mods.Weapons[1].Index != 1) return "the weapons were not numbered in order";
        if (Mods.Weapon(-1) != null || Mods.Weapon(2) != null) return "an index to nothing found a weapon";
        if (Mods.Summary != "SWORDS 1.0") return $"the summary reads \"{Mods.Summary}\"";
        return null;
    }

    private static string? BrokenModsSayWhy()
    {
        (string json, string expect)[] cases =
        {
            ("{ nope", "NOT VALID JSON"),
            ("[1, 2]", "ONE { } OBJECT"),
            ("{ \"format\": 2 }", "NEWER GAME"),
            ("{ \"id\": \"Has Spaces\" }", "ID MAY ONLY"),
            ("{ \"weapons\": [ { \"id\": \"x\", \"damage\": 1000 } ] }", "\"damage\""),
            ("{ \"weapons\": [ { \"id\": \"x\", \"reach\": \"far\" } ] }", "\"reach\" MUST BE A NUMBER"),
            ("{ \"weapons\": [ { \"name\": \"NO ID\" } ] }", "NEEDS AN \"id\""),
            ("{ \"weapons\": [ { \"id\": \"x\" }, { \"id\": \"x\" } ] }", "USED TWICE"),
            ("{ \"weapons\": [ { \"id\": \"x\", \"sounds\": { \"hit\": \"Kazoo\" } } ] }", "NO SOUND CALLED"),
            ("{ \"weapons\": [ { \"id\": \"x\", \"model\": [ { \"from\": [0,0,0], \"to\": [0,1,0], \"color\": \"red\" } ] } ] }", "\"color\""),
            ("{ \"weapons\": [ { \"id\": \"x\", \"model\": [ { \"from\": [0,0], \"to\": [0,1,0] } ] } ] }", "[x, y, z]"),
            ("{ \"weapons\": [ { \"id\": \"x\", \"model\": [ { \"from\": [0,0,0], \"to\": [0,9,0] } ] } ] }", "WITHIN"),
        };
        foreach (var (json, expect) in cases)
        {
            ModInfo m = Mods.Parse(json, "bad.json");
            if (m.Loaded) return $"{json} loaded";
            if (m.Error is null || !m.Error.Contains(expect))
                return $"{json} failed with \"{m.Error}\", which does not say {expect}";
        }

        // Comments and trailing commas are part of the format — the shipped file documents
        // itself with them — and a weapon with nothing but an id takes every default.
        ModInfo loose = Mods.Parse("// a note\n{ \"id\": \"ok\", \"weapons\": [ { \"id\": \"k\", }, ], }", "ok.json");
        if (!loose.Loaded) return "comments and trailing commas were refused: " + loose.Error;
        if (loose.Weapons[0].Model.Length == 0) return "a weapon with no model was left with nothing to draw";

        // Two files claiming one id: the first keeps it, the second is listed as failed.
        Mods.Install(new[] { Mods.Parse("{ \"id\": \"twin\" }", "a.json"), Mods.Parse("{ \"id\": \"twin\" }", "b.json") },
            log: false);
        if (Mods.All.Count != 2 || !Mods.All[0].Loaded || Mods.All[1].Loaded)
            return "a duplicate id was not refused on the second file";
        return null;
    }

    private static string? FolderScanReadsEveryJson()
    {
        string dir = Path.Combine(Path.GetTempPath(), "unrendered-mods-" + Guid.NewGuid().ToString("N"));
        string? before = Environment.GetEnvironmentVariable("UNRENDERED_MODS_DIR");
        try
        {
            Directory.CreateDirectory(dir);
            File.Copy(ShippedSwordsPath, Path.Combine(dir, "b-swords.json"));
            File.WriteAllText(Path.Combine(dir, "a-broken.json"), "{ \"weapons\": ");
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "not a mod");
            Environment.SetEnvironmentVariable("UNRENDERED_MODS_DIR", dir);

            Mods.Reload(log: false);
            if (Mods.All.Count != 2) return $"the scan found {Mods.All.Count} files, wanted the 2 .json";
            if (Mods.All[0].File != "a-broken.json") return "the files were not read in name order";
            if (Mods.All[0].Loaded) return "a truncated file loaded";
            if (!Mods.All[1].Loaded) return "the good file beside a broken one did not load";
            if (Mods.Weapons.Count != 2) return "a broken file cost the good one its weapons";

            // And an empty folder is an install with no mods, not an error.
            foreach (string f in Directory.GetFiles(dir)) File.Delete(f);
            Mods.Reload(log: false);
            if (Mods.All.Count != 0 || Mods.Weapons.Count != 0 || Mods.Fingerprint != 0u)
                return "an empty folder still reported mods";
            return null;
        }
        finally
        {
            Environment.SetEnvironmentVariable("UNRENDERED_MODS_DIR", before);
            try { Directory.Delete(dir, recursive: true); } catch { /* the OS will have it */ }
        }
    }

    private static string? FingerprintFollowsContent()
    {
        Mods.Install(Array.Empty<ModInfo>(), log: false);
        if (Mods.Fingerprint != 0u) return "no mods must fingerprint as 0, which is what an old build sends";

        string text = File.ReadAllText(ShippedSwordsPath);
        Mods.Install(new[] { Mods.Parse(text, "swords.json") }, log: false);
        uint shipped = Mods.Fingerprint;
        if (shipped == 0u) return "an installed mod fingerprinted as no mods";

        Mods.Install(new[] { Mods.Parse("// my copy\n" + text, "renamed.json") }, log: false);
        if (Mods.Fingerprint != shipped) return "a comment and a new file name changed the fingerprint";

        string harder = text.Replace("\"damage\": 3,", "\"damage\": 4,");
        if (harder == text) return "the test could not find the sword's damage to change";
        Mods.Install(new[] { Mods.Parse(harder, "swords.json") }, log: false);
        if (Mods.Fingerprint == shipped) return "a sword that hits harder fingerprinted the same";
        return null;
    }

    // --- The field ---------------------------------------------------------------------------

    private static string? FieldOpensWithSwords()
    {
        InstallSwords();
        var world = new World.World(new Loadout { Class = PlayerClass.Soldier });
        int swords = world.GroundWeapons.Count(g => g.Weapon == 0);
        int great = world.GroundWeapons.Count(g => g.Weapon == 1);
        if (swords != 3 || great != 1)
            return $"the match opened with {swords} swords and {great} greatswords, wanted 3 and 1";
        foreach (var g in world.GroundWeapons)
        {
            if (!g.Landed) return "an opening sword was still in the air";
            float d = Torus.Distance(g.Position, world.Player.Position);
            if (d < 15f || d > 90f) return $"an opening sword lay {d:0} m out — outside the scatter";
        }

        Mods.Install(Array.Empty<ModInfo>(), log: false);
        var bare = new World.World(new Loadout { Class = PlayerClass.Soldier });
        if (bare.GroundWeapons.Count != 0) return "a world with no mods installed still laid weapons";
        return null;
    }

    private static string? SoldierTakesASword()
    {
        var world = SwordWorld();
        PlayerTank p = world.Player;
        world.GroundWeapons.Add(new GroundWeapon(Torus.Wrap(p.Position + p.Forward), 0));
        StepWithoutInput(world);
        if (p.HeldWeapon != 0) return "standing on a sword did not put it in the hand";
        if (world.GroundWeapons.Count != 0) return "the sword was taken and also left lying there";

        // A second one underfoot stays on the grid: one blade at a time.
        world.GroundWeapons.Add(new GroundWeapon(Torus.Wrap(p.Position + p.Forward), 1));
        StepWithoutInput(world);
        if (p.HeldWeapon != 0 || world.GroundWeapons.Count != 1)
            return "a soldier already holding a sword picked up a second";

        // Hanging well above one is not walking over it.
        var high = SwordWorld();
        high.Player.Height = 20f;
        high.GroundWeapons.Add(new GroundWeapon(high.Player.Position, 0));
        StepWithoutInput(high);
        if (high.Player.HeldWeapon >= 0) return "a soldier twenty metres up took a sword off the grid";

        var tank = SwordWorld(PlayerClass.Tank);
        tank.GroundWeapons.Add(new GroundWeapon(tank.Player.Position, 0));
        for (int i = 0; i < 10; i++) StepWithoutInput(tank);
        if (tank.Player.HeldWeapon >= 0) return "a TANK picked up a sword";
        if (tank.GroundWeapons.Count != 1) return "a tank driving over a sword made it vanish";
        return null;
    }

    // --- The swing ---------------------------------------------------------------------------

    private static string? FireSwingsTheSword()
    {
        var world = SwordWorld();
        PlayerTank p = world.Player;
        p.HeldWeapon = 0;
        var mark = new EnemySoldier(Torus.Wrap(p.Position + p.Forward * 3f), p.Height, leader: false, slot: 0);
        world.Soldiers.Add(mark);

        for (int i = 0; i < 30 && mark.Alive; i++)
            world.Update((float)Config.FixedDt,
                new InputFrame(Btn.Fire, i == 0 ? Btn.Fire : Btn.None, Vector2.Zero));

        if (mark.Alive) return "half a second of FIRE with a sword in hand did not cut down a soldier at arm's length";
        if (p.SwingSeq == 0) return "the soldier died but no swing was counted";
        if (world.Projectiles.Any(r => r.Active)) return "the rifle fired while a sword was in hand";

        // Put it down and FIRE is the rifle again.
        p.HeldWeapon = -1;
        Press(world, Btn.Fire);
        if (!world.Projectiles.Any(r => r.Active)) return "with the sword gone, FIRE did not fire the rifle";
        return null;
    }

    private static string? SwingCutsTheArc()
    {
        var world = SwordWorld();
        PlayerTank p = world.Player;
        p.HeldWeapon = 0;
        Vector2 at(float metres, float side = 0f)
            => Torus.Wrap(p.Position + p.Forward * metres + new Vector2(p.Forward.Y, -p.Forward.X) * side);

        float edge = EnemyTank.Radius + 1.5f;
        var front = new EnemyTank(at(edge), elite: true);
        var behind = new EnemyTank(at(-edge), elite: true);
        var far = new EnemyTank(at(EnemyTank.Radius + PlayerTank.Radius + 3.2f + 4f), elite: true);
        world.Enemies.AddRange(new[] { front, behind, far });
        float full = front.Shield;

        world.SwingForTest();
        if (front.Shield >= full) return "the hunter in front of the blade was not cut";
        if (MathF.Abs(full - front.Shield - 3f) > 1e-3f) return $"the cut took {full - front.Shield}, the sword deals 3";
        if (behind.Shield < full) return "the hunter behind the swing was cut";
        if (far.Shield < full) return "a hunter four metres past the reach was cut";

        // The spider's hostage, the boss's plates and the rest all ride the ordinary damage
        // paths; what has to be checked here is only that a craft is spared by default.
        var duel = SwordWorld(match: new MatchSettings { MaxPlayers = 4 });
        duel.Player.HeldWeapon = 0;
        PlayerTank? other = duel.AddPlayer(new Loadout { Class = PlayerClass.Tank });
        if (other is null) return "could not seat a second craft";
        other.Position = Torus.Wrap(duel.Player.Position + duel.Player.Forward * 2.5f);
        float hull = other.Health + other.Shield;
        duel.SwingForTest();
        if (other.Health + other.Shield < hull) return "a swing hurt a team-mate with friendly fire off";

        // With it on, the same swing lands — through BiteCraft, on the player scale.
        var ff = SwordWorld(match: new MatchSettings { MaxPlayers = 4, FriendlyFire = true });
        ff.Player.HeldWeapon = 0;
        PlayerTank? foe = ff.AddPlayer(new Loadout { Class = PlayerClass.Soldier });
        if (foe is null) return "could not seat a second craft";
        foe.Position = Torus.Wrap(ff.Player.Position + ff.Player.Forward * 2.5f);
        foe.Height = ff.Player.Height;
        float whole = foe.Health + foe.Shield;
        ff.SwingForTest();
        float taken = whole - (foe.Health + foe.Shield);
        if (taken <= 0f) return "with friendly fire on, a swing at arm's length missed a craft";
        if (taken > Mods.Weapons[0].PlayerDamage + 0.01f)
            return $"a swing took {taken} off a craft; the sword's player damage is {Mods.Weapons[0].PlayerDamage}";
        return null;
    }

    private static string? SwingKeepsItsPace()
    {
        var world = SwordWorld();
        PlayerTank p = world.Player;
        p.HeldWeapon = 0;
        world.SwingForTest();
        world.SwingForTest();
        if (p.SwingSeq != 1) return $"two swings on one tick counted {p.SwingSeq}";
        StepFor(world, Mods.Weapons[0].Cooldown + 0.05f);
        world.SwingForTest();
        if (p.SwingSeq != 2) return "the sword never came round for a second swing";
        return null;
    }

    // --- Letting go ----------------------------------------------------------------------------

    private static string? DropPutsItDown()
    {
        var world = SwordWorld();
        PlayerTank p = world.Player;
        p.HeldWeapon = 1;
        Press(world, Btn.DropWeapon);
        if (p.HeldWeapon != -1) return "the drop key left the greatsword in hand";
        if (world.GroundWeapons.Count != 1 || world.GroundWeapons[0].Weapon != 1)
            return "the dropped greatsword is not lying on the grid";

        GroundWeapon g = world.GroundWeapons[0];
        float d = Torus.Distance(g.Position, p.Position);
        if (d < PlayerTank.Radius + GroundWeapon.Radius)
            return $"it landed {d:0.0} m away — inside the reach that picks it straight back up";

        // Stood still, it stays down.
        StepFor(world, 2.5f);
        if (p.HeldWeapon >= 0) return "a soldier who stood still took the sword they just dropped";

        // Walked straight back onto, it is refused for a moment and then taken.
        var again = SwordWorld();
        PlayerTank q = again.Player;
        q.HeldWeapon = 0;
        Press(again, Btn.DropWeapon);
        q.Position = again.GroundWeapons[0].Position;
        StepFor(again, 0.4f);
        if (q.HeldWeapon >= 0) return "a dropped sword went back in the same hand on the next step";
        StepFor(again, 2.5f);
        if (q.HeldWeapon != 0) return "the refusal never ended — the sword could not be taken back";
        return null;
    }

    private static string? DeadSoldierDropsABlade()
    {
        var world = SwordWorld();
        var always = Mods.Parse("{ \"id\": \"drop\", \"weapons\": [ { \"id\": \"blade\", \"spawn\": { \"soldierDrop\": 1 } } ] }",
            "drop.json");
        var never = Mods.Parse("{ \"id\": \"keep\", \"weapons\": [ { \"id\": \"blade\" } ] }", "keep.json");

        Mods.Install(new[] { never }, log: false);
        var spared = new EnemySoldier(world.Player.Position + new Vector2(0f, 12f), 20f, leader: false, slot: 0);
        world.Soldiers.Add(spared);
        world.HurtSoldierForTest(spared, 50f);
        if (world.GroundWeapons.Count != 0) return "a mod with no soldierDrop still had soldiers drop blades";

        Mods.Install(new[] { always }, log: false);
        var mark = new EnemySoldier(world.Player.Position + new Vector2(0f, 12f), 20f, leader: false, slot: 1);
        world.Soldiers.Add(mark);
        world.HurtSoldierForTest(mark, 50f);
        if (mark.Alive) return "the soldier survived";
        if (world.GroundWeapons.Count != 1) return "a soldier certain to drop a blade dropped none";
        GroundWeapon g = world.GroundWeapons[0];
        if (g.Landed) return "a blade off a soldier twenty metres up appeared on the grid already";

        StepFor(world, 3f);
        if (!g.Landed) return $"the blade was still {g.Height:0.0} m up three seconds later";
        return null;
    }

    private static string? SwordGoesDownWithTheLife()
    {
        var world = SwordWorld();
        PlayerTank p = world.Player;
        p.HeldWeapon = 0;
        StepWithoutInput(world);
        SpendALife(p);
        StepWithoutInput(world);
        if (p.HeldWeapon >= 0) return "the sword stayed in the hand of a craft that just lost a life";
        if (world.GroundWeapons.Count != 1) return "the sword out of a dying hand did not land anywhere";
        return null;
    }

    // --- The wire ------------------------------------------------------------------------------

    private static string? SwordsCrossTheWire()
    {
        InstallSwords();
        var host = new World.World(null, new MatchSettings { MaxPlayers = 4 }) { DynamicSpawning = false };
        host.Enemies.Clear();
        host.GroundWeapons.Clear();
        PlayerTank? mate = host.AddPlayer(new Loadout { Class = PlayerClass.Soldier });
        if (mate is null) return "could not seat a soldier on the host";
        mate.HeldWeapon = 1;
        mate.SwingSeq = 3;
        host.Players[0].HeldWeapon = 0;
        host.Players[0].SwingSeq = 2;
        Vector2 at = Torus.Wrap(host.Players[0].Position + new Vector2(5f, 0f));
        host.GroundWeapons.Add(new GroundWeapon(at, 0, 6.5f));

        var client = new World.World(null, new MatchSettings { MaxPlayers = 4 })
        { DynamicSpawning = false, Authoritative = false };
        client.LocalIndex = 0;
        client.GroundWeapons.Clear();
        client.Players[0].SwingSeq = 7;

        var buf = new byte[Snapshot.MaxSize];
        int n = Snapshot.WritePlayers(host, 1u, buf);
        Snapshot.ApplyPlayers(client, buf.AsSpan(0, n));
        if (client.Players.Count < 2) return "the soldier's seat never reached the client";
        if (client.Players[1].HeldWeapon != 1) return $"the team-mate's greatsword arrived as {client.Players[1].HeldWeapon}";
        if (client.Players[1].SwingSeq != 3) return "the team-mate's swings did not cross";
        if (client.Players[0].HeldWeapon != 0) return "the host's word on our own hand was not taken";
        if (client.Players[0].SwingSeq != 7) return "the host's swing count overwrote the one this machine predicts";

        n = Snapshot.WriteField(host, forSeat: 0, tick: 1u, buf);
        if (n > Snapshot.MaxSize) return "the field packet ran past its own buffer";
        Snapshot.ApplyField(client, buf.AsSpan(0, n));
        if (client.GroundWeapons.Count != 1) return $"{client.GroundWeapons.Count} swords arrived, wanted 1";
        GroundWeapon g = client.GroundWeapons[0];
        if (g.Weapon != 0) return "the sword on the grid arrived as the wrong weapon";
        if (Torus.Distance(g.Position, at) > 0.05f) return "the sword on the grid arrived in the wrong place";
        if (MathF.Abs(g.Height - 6.5f) > 0.3f) return $"a falling sword arrived at {g.Height:0.0} m, not 6.5";

        // Taken on the host, gone on the client.
        host.GroundWeapons.Clear();
        n = Snapshot.WriteField(host, forSeat: 0, tick: 2u, buf);
        Snapshot.ApplyField(client, buf.AsSpan(0, n));
        if (client.GroundWeapons.Count != 0) return "a sword taken on the host still lay on the client";
        return null;
    }

    private static string? MismatchedModsAreRefused()
    {
        InstallSwords();

        // A guest whose folder differs.
        {
            var net = new LoopbackNet(2, LinkQuality.Perfect, seed: 41);
            var hw = new World.World(null, new MatchSettings { MaxPlayers = 4 }) { DynamicSpawning = false };
            var cw = new World.World(null, new MatchSettings { MaxPlayers = 4 })
            { DynamicSpawning = false, Authoritative = false };
            var host = new Session(net[0], host: true);
            var client = new Session(net[1], host: false) { ModPrint = Mods.Fingerprint ^ 0x5A5A5A5Au };
            host.HostMatch(hw);
            client.JoinMatch(cw);
            client.SendHello(PlayerClass.Soldier);
            for (int i = 0; i < 40; i++) { net.Advance(); host.PumpLobby(); client.PumpLobby(); }

            if (client.LocalSeat >= 0) return "a guest with different mods was seated";
            if (client.Rejected is not { } why || !why.StartsWith("MODS DIFFER"))
                return $"the guest was told \"{client.Rejected}\" rather than that the mods differ";
            if (!why.Contains("SWORDS")) return $"the refusal \"{why}\" does not say what the host has";
            if (hw.Players.Count != 1) return "the refused guest still cost the host a seat";
        }

        // And one whose folder matches walks straight in.
        {
            var net = new LoopbackNet(2, LinkQuality.Perfect, seed: 42);
            var hw = new World.World(null, new MatchSettings { MaxPlayers = 4 }) { DynamicSpawning = false };
            var cw = new World.World(null, new MatchSettings { MaxPlayers = 4 })
            { DynamicSpawning = false, Authoritative = false };
            var host = new Session(net[0], host: true);
            var client = new Session(net[1], host: false);
            host.HostMatch(hw);
            client.JoinMatch(cw);
            client.SendHello(PlayerClass.Soldier);
            for (int i = 0; i < 40; i++) { net.Advance(); host.PumpLobby(); client.PumpLobby(); }
            if (client.LocalSeat != 1) return $"a guest with the same mods was not seated ({client.Rejected})";
        }
        return null;
    }
}
