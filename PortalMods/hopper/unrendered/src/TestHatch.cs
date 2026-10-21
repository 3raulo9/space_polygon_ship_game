using Raylib_cs;
using Unrendered.Core;
using GameWorld = Unrendered.World.World;

namespace DimensionHopper;

/// <summary>
/// HOPPER_TEST=1: the gun plays itself once, for the end-to-end run. It brings up the dial,
/// looks at another game's realm if one is on it, sets itself to the next of UNRENDERED's
/// worlds, fires, walks through, and says where it came out. Pictures are taken by the
/// glowstone rift's test hatch when it is installed (found by name; nothing depends on it).
/// </summary>
internal static class TestHatch
{
    private static readonly bool On = Environment.GetEnvironmentVariable("HOPPER_TEST") is "1" or "true";
    private static double _t0 = -1;
    private static int _step;

    internal static void Frame(GameWorld w)
    {
        if (!On || _step >= 9) return;
        double now = Raylib.GetTime();
        if (_t0 < 0) _t0 = now;
        double t = now - _t0;
        switch (_step)
        {
            case 0 when t > 3:
                Dial.Show(w);
                Hopper.Log("test: the dial is up with " + string.Join(", ", Realms.All(w).Select(e => $"{e.Name} ({e.Game}, {(e.Clear ? "clear" : e.Status)})")));
                _step++;
                break;
            case 1 when t > 4.2:
                Shot("hopper-dial");
                _step++;
                break;
            case 2 when t > 5:
            {
                int other = Realms.All(w).FindIndex(e => e.Game != "UNRENDERED");
                if (other >= 0) { Dial.Select(other); Hopper.Log("test: looking at " + Dial.Selected?.Name + ": " + Dial.Selected?.Status); }
                _step++;
                break;
            }
            case 3 when t > 6.2:
                if (Dial.Selected is { Game: not "UNRENDERED" }) Shot("hopper-establish");
                _step++;
                break;
            case 4 when t > 7.5:
            {
                var all = Realms.All(w);
                int next = all.FindIndex(e => e.Planet != null && !e.Here);
                Dial.Select(next);
                Dial.Choose(w);
                Hopper.Log("test: set to " + Hopper.Destination);
                _step++;
                break;
            }
            case 5 when t > 8.5:
                Hopper.Fire(w);
                _step++;
                break;
            case 6 when t > 9.8:
                Shot("hopper-door");
                _step++;
                break;
            case 7 when t > 10.6:
            {
                // Into it.
                var d = Hopper.Doors.FirstOrDefault(x => !x.Arrival);
                if (d != null) w.Player.Position = d.At;
                Hopper.Log("test: walking into the door" + (d == null ? " (there is none)" : ""));
                _step++;
                break;
            }
            case 8 when t > 12.4:
                Shot("hopper-arrived");
                Hopper.Log($"test: standing on {Planet.Get(w.Match.Destination).Name}");
                _step++;
                break;
        }
    }

    private static void Shot(string label)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            if (asm.GetType("UnrenderedPortal.TestHatch")?.GetMethod("Want") is { } want)
            {
                want.Invoke(null, new object[] { label });
                return;
            }
    }
}
