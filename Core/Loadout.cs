using Raylib_cs;

namespace Unrendered.Core;

/// <summary>
/// Everything the player decides before the world is built: which chassis they climb
/// into, how its three reserves are apportioned, and what colour each of its parts is
/// painted. Held by <see cref="Game"/> across runs so a trip back to the menu doesn't
/// wipe the build, and handed to <see cref="World.World"/> at spawn, which is the only
/// place it is read into live stats.
///
/// The point budget is the whole design of the loadout: 20 points across four tracks,
/// each track between 1 and 10. Maxing one thing to 10 leaves 10, which has to cover
/// three other tracks — so a specialist is genuinely crippled everywhere else.
/// Spreading them 5/5/5/5 spends the lot exactly. There is no build that is good at
/// everything, which is the only rule that matters here.
/// </summary>
public sealed class Loadout
{
    /// <summary>
    /// The four reserves the points are spent on.
    ///
    /// <para><b>Append only.</b> These are wire vocabulary — a build crosses to every other
    /// machine in this order (<c>Write</c>/<c>Read</c> below), so reordering them repaints
    /// and rebalances every craft in a match built against the old order.</para>
    /// </summary>
    public enum Stat { Shield, Speed, Ammo, Health }

    /// <summary>How many tracks the budget is spread across.</summary>
    public const int StatCount = 4;

    public const int Budget = 20;
    public const int StatMin = 1;
    public const int StatMax = 10;

    public PlayerClass Class { get; set; } = PlayerClass.Tank;

    private readonly int[] _stats = { 5, 5, 5, 5 };

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
    public int Health => _stats[(int)Stat.Health];

    /// <summary>Points already committed across the four tracks.</summary>
    public int Spent
    {
        get
        {
            int total = 0;
            foreach (int v in _stats) total += v;
            return total;
        }
    }

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
    // A straight 5/5/5/5 reproduces exactly the numbers the game has always used: 100
    // points of punishment before a life is spent, 26 units/sec, a 50-round magazine.
    // The 100 is now split down the middle — 50 of shield in front of 50 of hull — so
    // adding the fourth track did not make anybody tougher, it gave the same
    // durability two different textures. Every other build trades away from that.

    /// <summary>
    /// What one shield charge soaks before it breaks. The SHIELD track buys a *count* of
    /// these, not a bar: five points is five charges, and the HUD counts them off 5/5,
    /// 4/5, 3/5 as they pop. Ten is small enough that a rocket takes several — which is
    /// what makes losing them read as an event rather than as a bar sliding.
    /// </summary>
    public const float ChargeStrength = 10f;

    /// <summary>How many shield charges this build carries. One per point, exactly.</summary>
    public int ShieldCharges => Shield;

    /// <summary>Total damage the shield stack absorbs when full. 5 → 50.</summary>
    public float MaxShield => ChargeStrength * ShieldCharges;

    /// <summary>Multiplier on the craft's top speed and acceleration. 5 → 1.0.</summary>
    public float SpeedScale => 0.6f + 0.08f * Speed;

    /// <summary>Magazine size. 5 → the historical 50-round cap.</summary>
    public int MaxAmmo => 10 * Ammo;

    /// <summary>
    /// Hull, which is what actually kills you: damage only reaches it once every shield
    /// charge is spent, and nothing but a repair kit brings it back. Floored well above
    /// zero (1 → 26) because a track at its minimum should be a weakness, not a craft
    /// that dies to the first stray round it meets with its shields down.
    /// </summary>
    public float MaxHealth => 20f + 6f * Health;

    // --- The wire -----------------------------------------------------------
    // A build is not private to the machine that made it. Everyone in a match sees
    // everyone else's craft, and a craft is its chassis, its points and its paint — so
    // the whole thing crosses, once, on the reliable channel when a player commits at
    // the pod. It used to be a single chassis byte, which is why every other player's
    // craft arrived in the machine's default colours wearing the machine's default
    // build, however long its owner had spent at the bench.

    /// <summary>How many bytes <see cref="Write"/> lays down. Fixed: both ends walk the
    /// same catalog, so the length is a property of the build, not of the message.</summary>
    public static int Bytes
    {
        get
        {
            int n = 1 + StatCount;
            foreach (var arch in ClassCatalog.All) n += arch.PartNames.Length;
            return n;
        }
    }

    /// <summary>Lays the build down: chassis, the four tracks, then every chassis's paint
    /// in catalog order. The paint of the ones you did not pick rides along because it
    /// costs a dozen bytes on a message sent once, and it means a player who changes their
    /// mind mid-match arrives in the colours they had already chosen for that craft.</summary>
    public void Write(Span<byte> dst)
    {
        int at = 0;
        dst[at++] = (byte)Class;
        for (int i = 0; i < StatCount; i++) dst[at++] = (byte)_stats[i];
        foreach (var arch in ClassCatalog.All)
        {
            int[] sw = _swatches[arch.Kind];
            for (int i = 0; i < arch.PartNames.Length; i++) dst[at++] = (byte)sw[i];
        }
    }

    /// <summary>Rebuilds a written build. Every field is clamped rather than trusted: this
    /// is a packet, and a malformed one must produce a legal craft, not an exception in the
    /// middle of the host's tick.</summary>
    public static Loadout Read(ReadOnlySpan<byte> src)
    {
        var lo = new Loadout();
        if (src.Length < Bytes) return lo;
        int at = 0;
        var kind = (PlayerClass)src[at++];
        lo.Class = Enum.IsDefined(kind) ? kind : PlayerClass.Tank;
        for (int i = 0; i < StatCount; i++)
            lo._stats[i] = Math.Clamp((int)src[at++], StatMin, StatMax);
        // A build whose tracks total more than the budget allows is not one the hangar
        // could have produced. Rather than reject it (and leave that player craftless),
        // walk it back down: the cheat costs the cheater their highest track first.
        while (lo.Spent > Budget)
        {
            int worst = 0;
            for (int i = 1; i < StatCount; i++) if (lo._stats[i] > lo._stats[worst]) worst = i;
            if (lo._stats[worst] <= StatMin) break;
            lo._stats[worst]--;
        }
        int nSwatch = ClassCatalog.Swatches.Length;
        foreach (var arch in ClassCatalog.All)
        {
            int[] sw = lo._swatches[arch.Kind];
            for (int i = 0; i < arch.PartNames.Length; i++)
                sw[i] = ((src[at++] % nSwatch) + nSwatch) % nSwatch;
        }
        return lo;
    }

    /// <summary>Whether two builds describe the same craft — used to decide whether a
    /// seat's craft actually needs rebuilding when one arrives off the wire.</summary>
    public bool SameAs(Loadout other)
    {
        if (Class != other.Class) return false;
        for (int i = 0; i < StatCount; i++) if (_stats[i] != other._stats[i]) return false;
        int[] mine = _swatches[Class], theirs = other._swatches[Class];
        for (int i = 0; i < mine.Length; i++) if (mine[i] != theirs[i]) return false;
        return true;
    }

    /// <summary>
    /// An independent copy — the build as it stands, detached from whoever goes on editing
    /// this one.
    ///
    /// A craft holds its build for the life of the craft and the renderer reads the chassis
    /// and the paint straight off it, so a craft must never share the object the hangar (and
    /// the multiplayer launch path) keeps mutating. It used to: <see cref="World.World"/>
    /// handed the loop's single loadout to seat 0 by reference, so the moment the local player
    /// settled on a chassis, <em>the craft in seat 0 was drawn as it</em> — which on a client
    /// is the host, and is exactly how everyone ended up looking at their own craft wearing
    /// somebody else's name.
    /// </summary>
    public Loadout Clone()
    {
        var copy = new Loadout { Class = Class };
        Array.Copy(_stats, copy._stats, _stats.Length);
        foreach (var kv in _swatches) copy._swatches[kv.Key] = (int[])kv.Value.Clone();
        return copy;
    }
}
