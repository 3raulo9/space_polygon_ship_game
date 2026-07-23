using Raylib_cs;

namespace VoidTanks.Core;

/// <summary>
/// Everything the player decides before the world is built: which chassis they climb
/// into, how its three reserves are apportioned, and what colour each of its parts is
/// painted. Held by <see cref="Game"/> across runs so a trip back to the menu doesn't
/// wipe the build, and handed to <see cref="World.World"/> at spawn, which is the only
/// place it is read into live stats.
///
/// The point budget is the whole design of the loadout: 16 points across three tracks,
/// each track between 1 and 10. Maxing one thing to 10 leaves 6, which buys a 5 and a
/// 1 and nothing else — so a specialist is genuinely crippled somewhere. Spreading
/// them 5/5/5 costs 15 and leaves a point spare. There is no build that is good at
/// everything, which is the only rule that matters here.
/// </summary>
public sealed class Loadout
{
    /// <summary>The three reserves the points are spent on.</summary>
    public enum Stat { Shield, Speed, Ammo }

    public const int Budget = 16;
    public const int StatMin = 1;
    public const int StatMax = 10;

    public PlayerClass Class { get; set; } = PlayerClass.Tank;

    private readonly int[] _stats = { 5, 5, 5 };

    // Paint jobs are kept per chassis, not per player: switching to the spider and back
    // must not repaint the tank you already dressed.
    private readonly Dictionary<PlayerClass, int[]> _swatches = new();

    public Loadout()
    {
        foreach (var arch in ClassCatalog.All)
            _swatches[arch.Kind] = (int[])arch.DefaultSwatches.Clone();
    }

    public int this[Stat s] => _stats[(int)s];

    public int Shield => _stats[(int)Stat.Shield];
    public int Speed => _stats[(int)Stat.Speed];
    public int Ammo => _stats[(int)Stat.Ammo];

    /// <summary>Points already committed across the three tracks.</summary>
    public int Spent => _stats[0] + _stats[1] + _stats[2];

    /// <summary>Points still on the table. Never negative — see <see cref="Adjust"/>.</summary>
    public int Remaining => Budget - Spent;

    /// <summary>
    /// Nudges one track by <paramref name="delta"/>, refusing any move that would push
    /// it outside 1..10 or overspend the budget. Returns true when the value actually
    /// changed, so the screen can decide whether to blip.
    /// </summary>
    public bool Adjust(Stat s, int delta)
    {
        int i = (int)s;
        int next = _stats[i] + delta;
        if (next < StatMin || next > StatMax) return false;
        if (Spent - _stats[i] + next > Budget) return false;
        _stats[i] = next;
        return true;
    }

    // --- Paint --------------------------------------------------------------

    /// <summary>Swatch index for one part of a given chassis.</summary>
    public int SwatchIndex(PlayerClass kind, int part) => _swatches[kind][part];

    public Color PartColor(PlayerClass kind, int part)
        => ClassCatalog.SwatchColor(_swatches[kind][part]);

    /// <summary>Cycles one part's colour through the swatch table (wraps both ways).</summary>
    public void CycleSwatch(PlayerClass kind, int part, int dir)
    {
        int n = ClassCatalog.Swatches.Length;
        _swatches[kind][part] = (((_swatches[kind][part] + dir) % n) + n) % n;
    }

    /// <summary>Repaints a chassis back to how the machine shipped it.</summary>
    public void ResetPaint(PlayerClass kind)
        => _swatches[kind] = (int[])ClassCatalog.Get(kind).DefaultSwatches.Clone();

    // --- Derived combat stats -----------------------------------------------
    // Each track is centred so that a straight 5 reproduces exactly the numbers the
    // game has always used: 100 shield, 26 units/sec, a 50-round magazine. A 5/5/5
    // build is therefore the old craft, and every other build is a deliberate trade
    // away from it in both directions.

    /// <summary>Shield capacity for the current build. 5 → the historical 100.</summary>
    public float MaxShield => 40f + 12f * Shield;

    /// <summary>Multiplier on the craft's top speed and acceleration. 5 → 1.0.</summary>
    public float SpeedScale => 0.6f + 0.08f * Speed;

    /// <summary>Magazine size. 5 → the historical 50-round cap.</summary>
    public int MaxAmmo => 10 * Ammo;
}
