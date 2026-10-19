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
    private static bool _opened, _linked, _entered;

    public static void Frame()
    {
        if (Plan.Length == 0 || Rift.World is not { } w) return;
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
        if (Plan.Contains("use") && !_used && Pocket.Selected != null && inside > 2.0)
        {
            _used = true;
            Pocket.UseSelected();
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
    private static bool _picked, _used, _left;
}
