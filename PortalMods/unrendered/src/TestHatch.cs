namespace UnrenderedPortal;

/// <summary>
/// Scripted runs for checking the rift without hands on the keys — the same idea as the
/// game's own UNRENDERED_CAPTURE harness, which it runs alongside. RIFT_TEST holds any of:
/// <c>portal</c> (open the portal on the first frame in a world), <c>link</c> (connect to
/// <c>connect=</c> from portal.cfg, or RIFT_CONNECT), <c>enter</c> (step through as soon as
/// Minecraft has answered). Unset, it does nothing at all.
/// </summary>
public static class TestHatch
{
    private static readonly string Plan = Environment.GetEnvironmentVariable("RIFT_TEST") ?? "";

    // --- Pictures and an ending, for runs nobody is watching (the end-to-end test) ----------

    /// <summary>RIFT_SHOTS: a folder; the finished frame is saved there every few seconds.</summary>
    private static readonly string? Shots = Environment.GetEnvironmentVariable("RIFT_SHOTS");
    private static readonly double QuitAfter =
        double.TryParse(Environment.GetEnvironmentVariable("RIFT_QUIT_AFTER"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double q) ? q : 0;
    private static double _lastShot = -100, _started = -1;
    private static readonly double Delay =
        double.TryParse(Environment.GetEnvironmentVariable("RIFT_TEST_DELAY"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : 0;

    /// <summary>A picture on the next presented frame under this name — for another mod's test
    /// (found by name, so no mod depends on this one).</summary>
    public static void Want(string label) { if (Shots != null) _pending = label; }
    private static int _shotN;

    public static bool ShotDue => Shots != null && (_pending != null || Raylib_cs.Raylib.GetTime() - _lastShot > 4.0);

    /// <summary>A picture wanted on the very next frame, under this name.</summary>
    private static string? _pending;

    /// <summary>Saves the frame being presented (call before the buffers swap).</summary>
    public static void Shoot(string label)
    {
        _lastShot = Raylib_cs.Raylib.GetTime();
        label = _pending ?? label;
        _pending = null;
        try
        {
            Directory.CreateDirectory(Shots!);
            var img = Raylib_cs.Raylib.LoadImageFromScreen();
            string file = Path.Combine(Shots!, $"{_shotN++:000}-{label}.png");
            Raylib_cs.Raylib.ExportImage(img, file);
            Raylib_cs.Raylib.UnloadImage(img);
            Rift.Log($"shot {file} (realm {Realm.Active}, terrain columns {Realm.Land.Columns}, mobs {Realm.Ents.Count}, " +
                     $"rift things {Overlays.RiftEnts.Count}, visitors {Visitors.All.Count}, built {Built.Any})");
        }
        catch (Exception e) { Rift.Log("shot: " + e.Message); }
    }
    private static bool _opened, _linked, _entered;
    private static double _packOpened = -1, _homeSince = -1;
    private static bool _packDone, _packShot;

    /// <summary>Every presented frame, panel or not.</summary>
    internal static void Presenting()
    {
        if (_packOpened < 0) return;
        double open = Raylib_cs.Raylib.GetTime() - _packOpened;
        if (open > 1.0 && !_packShot) { _pending = "pack"; _packShot = true; }
        if (open > 3.0)
        {
            Panel(false);
            _packOpened = -1;
            _packDone = true;
        }
    }

    /// <summary>The game's own inventory panel, opened or shut as the F key would.</summary>
    private static bool Panel(bool open)
    {
        var m = Rift.Game?.GetType().GetMethod("SetInventory",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (m == null) { Rift.Log("test hatch: no inventory panel to open"); return false; }
        m.Invoke(Rift.Game, new object[] { open });
        return true;
    }

    public static void Frame()
    {
        if (_started < 0) _started = Raylib_cs.Raylib.GetTime();
        if (QuitAfter > 0 && Raylib_cs.Raylib.GetTime() - _started > QuitAfter)
        {
            Rift.Log("test hatch: time is up");
            Environment.Exit(0);
        }
        if (Plan.Length == 0 || Rift.World is not { } w) return;

        // An unattended run is about the rift, not about surviving: the test pilot cannot die.
        // Whatever would have hurt it is logged instead, so a picture of a game-over screen is
        // never what a run spends its last minute on.
        var me = w.Player;
        float lost = (me.MaxHealth - me.Health) + (me.MaxShield - me.Shield);
        if (lost > 0.5f)
        {
            Rift.Log($"test hatch: the pilot would have taken {lost:0.0} ({(Realm.Active ? "in Minecraft" : "in UNRENDERED")}, " +
                     $"{w.Enemies.Count} hunters, {w.Soldiers.Count} soldiers near)");
            me.Health = me.MaxHealth;
            me.Shield = me.MaxShield;
        }
        // RIFT_TEST_DELAY: seconds to hold the plan back, so another mod's own test can run first.
        if (Raylib_cs.Raylib.GetTime() - _started < Delay) return;

        if (Plan.Contains("portal") && !_opened)
        {
            _opened = true;
            Portal.OpenAhead();
        }
        if (Plan.Contains("link") && !_linked)
        {
            _linked = true;
            string target = Environment.GetEnvironmentVariable("RIFT_CONNECT") ?? "127.0.0.1:25570";
            int colon = target.LastIndexOf(':');
            Rift.Link = Link.Connect(target[..colon], int.Parse(target[(colon + 1)..]), target);
        }
        if (Plan.Contains("enter") && !_entered && Rift.Live != null && Portal.Linked != null)
        {
            _entered = true;
            Realm.Enter(w);
        }

        // Walk the nearest hunter through UNRENDERED's own portal, one side then the other.
        if (Plan.Contains("cross") && Portal.Open && Portal.Linked != null && _crossStep < 3 && w.Enemies.Count > 0)
        {
            var n = new System.Numerics.Vector2(MathF.Sin(Portal.Facing), MathF.Cos(Portal.Facing));
            var e = w.Enemies[0];
            if (_crossStep == 0 && Raylib_cs.Raylib.GetTime() > 4) { e.Position = Portal.Position + n * 2f; _crossStep = 1; }
            else if (_crossStep == 1) { _crossStep = 2; }
            else if (_crossStep == 2) { e.Position = Portal.Position - n * 2f; _crossStep = 3; }
        }

        // Turn to face what Minecraft built.
        if (Plan.Contains("lookbuilt") && Built.First is { } b)
        {
            var to = new System.Numerics.Vector2(b.X, b.Z) - w.Player.Position;
            w.Player.Heading = MathF.Atan2(to.X, to.Y);
        }

        // Back home with something out of Minecraft in the pack: open the pack and look at it.
        // The panel stops the mods' frame, so Presenting() takes the picture and shuts it.
        if (Plan.Contains("pack") && !Realm.Active && _packOpened < 0 && !_packDone && _homeSince > 0
            && Raylib_cs.Raylib.GetTime() - _homeSince > 3.0 && Pocket.FirstSlot(w) is var slot and >= 0)
        {
            Rift.Log($"test hatch: the pack holds {Unrendered.Core.ItemNames.Of(w.Inventory.Slots[slot].Kind)} x{w.Inventory.Slots[slot].Count} from Minecraft; opening it");
            if (Panel(true)) _packOpened = Raylib_cs.Raylib.GetTime();
            else _packDone = true;
        }
        if (Realm.Active) _homeSince = -1;
        else if (_homeSince < 0) _homeSince = Raylib_cs.Raylib.GetTime();

        // Back home: turn to face the Minecraft player standing in UNRENDERED.
        if (Plan.Contains("lookvisitor") && !Realm.Active && Visitors.FirstIn(w) is { } vp)
        {
            var to = Unrendered.Core.Torus.NearestImage(vp, w.Player.Position) - w.Player.Position;
            if (to.LengthSquared() > 0.01f) w.Player.Heading = MathF.Atan2(to.X, to.Y);
        }

        if (!Realm.Active) { _since = -1; return; }
        double now = Raylib_cs.Raylib.GetTime();
        if (_since < 0) _since = now;
        double inside = now - _since;
        var craft = w.Player;

        // Roll over the first dropped item Minecraft is showing.
        if (Plan.Contains("pickup") && !_picked && inside > 1.0
            && Realm.Ents.FirstOrDefault(e => e.IsItem) is { } item)
        {
            _picked = true;
            craft.Position = new System.Numerics.Vector2((float)(item.Pos.X - Realm.Ox), (float)(item.Pos.Z - Realm.Oz));
        }
        // Take what came back in hand.
        if (Plan.Contains("use") && !_used && inside > 2.0 && Pocket.FirstSlot(w) is var held and >= 0)
        {
            _used = true;
            Rift.Log($"test hatch: right-click on {Unrendered.Core.ItemNames.Of(w.Inventory.Slots[held].Kind)} in the pack: "
                + (w.ChargeFromSlot(craft, w.Inventory, held) ? "spent" : "kept"));
        }
        // Turn on the nearest mob and fire at it twice a second.
        if (Plan.Contains("shoot") && inside > 2.0 && inside < 5.0 && now - _shot > 0.5
            && Realm.Ents.FirstOrDefault(e => !e.IsItem) is { } mob)
        {
            _shot = now;
            var to = new System.Numerics.Vector2((float)(mob.Pos.X - Realm.Ox), (float)(mob.Pos.Z - Realm.Oz)) - craft.Position;
            craft.Heading = MathF.Atan2(to.X, to.Y);
            w.FirePlayerShot();
        }
        // A walk through Minecraft: forward for twenty seconds (turning off anything it walks
        // into), then at the nearest mob, then the nearest dropped item, then home.
        if (Plan.Contains("tour"))
        {
            float dt = Raylib_cs.Raylib.GetFrameTime();
            if (inside > 2 && inside < 22)
            {
                var before = craft.Position;
                craft.Position += craft.Forward * 7f * dt;
                if (inside - _lastTurn > 1.0 && Vector2Distance(before, _tourMark) < 0.5f) { craft.Heading += 1.6f; _lastTurn = inside; }
                if (inside - _lastTurn > 1.0) _tourMark = before;
            }
            else if (inside >= 22 && inside < 30 && Realm.Ents.FirstOrDefault(e => !e.IsItem) is { } quarry && now - _shot > 0.4)
            {
                _shot = now;
                var to = new System.Numerics.Vector2((float)(quarry.Pos.X - Realm.Ox), (float)(quarry.Pos.Z - Realm.Oz)) - craft.Position;
                craft.Heading = MathF.Atan2(to.X, to.Y);
                if (to.Length() > 12f) craft.Position += System.Numerics.Vector2.Normalize(to) * 8f * dt;
                w.FirePlayerShot();
            }
            else if (inside >= 30 && inside < 34 && Realm.Ents.FirstOrDefault(e => e.IsItem) is { } loot)
                Realm.PlaceAt(loot.Pos.X, loot.Pos.Z);
            else if (inside >= 40 && !_left)
            {
                // Into the water: the portal's first water cell.
                _left = true;
                var portal = Portal.Linked!;
                Realm.PlaceAt(portal["x"]!.GetValue<int>() + 0.5, portal["z"]!.GetValue<int>() + 0.5,
                    portal["y"]!.GetValue<int>() + 1.5f);   // in the water, not on the frame's top
                Rift.Log("test hatch: walking into the portal");
            }
            else if (inside >= 44 && _left)
            {
                // Still here: the portal is not where Minecraft said, or the craft was pushed off
                // its frame. Say so, and go home anyway so the rest of the run is not lost.
                Rift.Log($"test hatch: the portal did not take the pilot (portal {Portal.Linked?.ToJsonString()}, " +
                         $"feet {Realm.Mc(craft.Position, craft.Height)})");
                Realm.Leave();
            }
        }

        // Walk back into Minecraft's portal.
        if (Plan.Contains("leave") && !_left && inside > 5.0)
        {
            _left = true;
            var portal = Portal.Linked!;
            craft.Position = new System.Numerics.Vector2(
                (float)(portal["x"]!.GetValue<int>() + 0.5 - Realm.Ox), (float)(portal["z"]!.GetValue<int>() + 0.5 - Realm.Oz));
        }
    }

    private static double _since = -1, _shot;
    private static int _crossStep;
    private static double _lastTurn;
    private static System.Numerics.Vector2 _tourMark;

    private static float Vector2Distance(System.Numerics.Vector2 a, System.Numerics.Vector2 b) => (a - b).Length();
    private static bool _picked, _used, _left;
}
