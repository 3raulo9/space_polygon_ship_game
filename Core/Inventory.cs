namespace Unrendered.Core;

/// <summary>What a carried item is. Salvage the craft scoops off the grid, plus the
/// CRAB CORE fragment harvested from a slain boss and the weapon crafted from three
/// of them.</summary>
public enum ItemKind
{
    Battery,       // repairs shield + hyper when spent
    Bullet,        // reloads the cannon when spent
    CrabFragment,  // dropped by a killed Crab-Core; three craft a CRAB CORE
    CrabCore,      // the throwable radial-beam bomb
}

/// <summary>
/// Which cluster of slots an address names. Lives here rather than beside the panel that
/// draws it because it is now part of the <em>wire</em>: a client's inventory moves are sent
/// to the host as intents addressed in these terms, and the host replays them against the
/// authoritative pack with the same rules (see <see cref="Inventory.Move"/>).
/// <see cref="None"/> is empty panel; <see cref="Output"/> is the crafting triangle's centre,
/// which holds nothing and is only ever a source.
/// </summary>
public enum InvRegion : byte { None, Slots, Craft, Weapons, Output }

/// <summary>What a client is asking the host to do to its pack.</summary>
public enum InvOp : byte
{
    /// <summary>Move <c>Count</c> of the source slot onto the target — the completed
    /// drag. Covers merge, swap and the single-unit peel into a craft corner.</summary>
    Move,
    /// <summary>Spend the triangle's three fragments and land the crafted core on the
    /// target slot.</summary>
    CraftInto,
    /// <summary>Burn the named grid slot into the craft — a battery's charge, a stack of
    /// rounds into the magazine.</summary>
    Charge,
}

/// <summary>
/// One inventory action a client wants performed, as six bytes. Clients never own their
/// pack: they mutate a local mirror for an instant response and send one of these, and the
/// host's reliable echo of the real inventory is what finally decides.
/// </summary>
public readonly record struct InvIntent(
    InvOp Op, InvRegion From, byte FromIndex, InvRegion To, byte ToIndex, byte Count)
{
    public const int Size = 6;

    public void Write(Span<byte> dst)
    {
        dst[0] = (byte)Op;
        dst[1] = (byte)From;
        dst[2] = FromIndex;
        dst[3] = (byte)To;
        dst[4] = ToIndex;
        dst[5] = Count;
    }

    public static InvIntent Read(ReadOnlySpan<byte> src)
        => new((InvOp)src[0], (InvRegion)src[1], src[2], (InvRegion)src[3], src[4], src[5]);
}

/// <summary>
/// One occupied (or empty) slot: a kind and how many of it. A struct so the slot
/// arrays hold values directly and clearing a slot is a plain assignment. Count 0
/// means empty regardless of kind — see <see cref="IsEmpty"/>.
/// </summary>
public struct ItemStack
{
    public ItemKind Kind;
    public int Count;

    public ItemStack(ItemKind kind, int count) { Kind = kind; Count = count; }

    public readonly bool IsEmpty => Count <= 0;
    public static ItemStack Empty => new(ItemKind.Battery, 0);
}

/// <summary>
/// One player's carried goods: a 20-slot grid, the crafting triangle's three corner
/// inputs, and the four equip slots wired to R/T/Y/U. Pure data + placement rules;
/// the world reads it to spend items and the UI drives drag/drop and crafting against
/// it. Lives on <see cref="Unrendered.World.World"/> so it survives a paused/opened
/// inventory the same way the rest of the run does.
///
/// <para>There is one of these <em>per seat</em>, not one per world: salvage is personal, so
/// the craft that drove over a cell is the craft that keeps it. The host owns every seat's
/// pack and mirrors each client its own; a client's copy is a mirror it may scribble on for
/// responsiveness, never the truth (see <see cref="InvIntent"/>).</para>
/// </summary>
public sealed class Inventory
{
    public const int SlotCount = 20;
    public const int CraftCount = 3;     // the triangle's three corners
    public const int WeaponCount = 4;    // R T Y U

    /// <summary>How many fragments one CRAB CORE costs — one from each triangle corner.</summary>
    public const int CraftCost = CraftCount;

    public readonly ItemStack[] Slots = new ItemStack[SlotCount];
    public readonly ItemStack[] Craft = new ItemStack[CraftCount];
    public readonly ItemStack[] Weapons = new ItemStack[WeaponCount];

    /// <summary>The stacking ceiling for a kind: batteries 4, bullets 20, fragments a
    /// small pile, the crafted core one at a time.</summary>
    public static int MaxStack(ItemKind kind) => kind switch
    {
        ItemKind.Battery      => 4,
        ItemKind.Bullet       => 20,
        ItemKind.CrabFragment => 9,
        ItemKind.CrabCore     => 1,
        _                     => 1,
    };

    /// <summary>
    /// Folds <paramref name="count"/> of a kind into the grid: tops up matching stacks
    /// first, then fills empty slots, each capped at <see cref="MaxStack"/>. Returns
    /// whatever wouldn't fit (0 when it all landed), so the caller can decide what to do
    /// with the overflow.
    /// </summary>
    public int Add(ItemKind kind, int count)
    {
        int max = MaxStack(kind);

        // Pass 1: pour into partial stacks of the same kind.
        for (int i = 0; i < Slots.Length && count > 0; i++)
        {
            if (Slots[i].IsEmpty || Slots[i].Kind != kind) continue;
            int room = max - Slots[i].Count;
            if (room <= 0) continue;
            int moved = Math.Min(room, count);
            Slots[i].Count += moved;
            count -= moved;
        }

        // Pass 2: open fresh slots for the remainder.
        for (int i = 0; i < Slots.Length && count > 0; i++)
        {
            if (!Slots[i].IsEmpty) continue;
            int moved = Math.Min(max, count);
            Slots[i] = new ItemStack(kind, moved);
            count -= moved;
        }

        return count; // leftover that didn't fit
    }

    /// <summary>The recipe is satisfied when all three corners hold a fragment.</summary>
    public bool CanCraft()
    {
        foreach (var c in Craft)
            if (c.IsEmpty || c.Kind != ItemKind.CrabFragment) return false;
        return true;
    }

    /// <summary>The CRAB CORE preview shown in the triangle's centre while craftable,
    /// or an empty stack. Reading it never consumes anything.</summary>
    public ItemStack CraftOutput() =>
        CanCraft() ? new ItemStack(ItemKind.CrabCore, 1) : ItemStack.Empty;

    /// <summary>
    /// Claims the crafted CRAB CORE: spends one fragment from each corner and hands
    /// back the core. Returns an empty stack (spending nothing) if the recipe isn't met.
    /// </summary>
    public ItemStack TakeCraftOutput()
    {
        if (!CanCraft()) return ItemStack.Empty;
        for (int i = 0; i < Craft.Length; i++)
        {
            Craft[i].Count--;
            if (Craft[i].Count <= 0) Craft[i] = ItemStack.Empty;
        }
        return new ItemStack(ItemKind.CrabCore, 1);
    }

    // --- Addressing, and the placement rules both ends of the wire run ---------------
    // These used to live in the panel, which was fine while the panel was the only thing
    // that ever touched a pack. It is not any more: the host has to replay a client's move
    // against the authoritative inventory, and it must land in exactly the same place the
    // client's optimistic mirror put it. So the rules live on the data.

    /// <summary>Whether a region will hold a given kind: equip slots take only the crafted
    /// core, the crafting corners only fragments, the grid anything.</summary>
    public static bool Accepts(InvRegion region, ItemKind kind) => region switch
    {
        InvRegion.Slots   => true,
        InvRegion.Weapons => kind == ItemKind.CrabCore,
        InvRegion.Craft   => kind == ItemKind.CrabFragment,
        _                 => false,
    };

    /// <summary>How many slots a region has, or 0 for the ones that store nothing.</summary>
    public static int Length(InvRegion region) => region switch
    {
        InvRegion.Slots   => SlotCount,
        InvRegion.Craft   => CraftCount,
        InvRegion.Weapons => WeaponCount,
        _                 => 0,
    };

    /// <summary>True if this region/index pair names a real slot.</summary>
    public static bool Addressable(InvRegion region, int index)
        => (uint)index < (uint)Length(region);

    /// <summary>The slot a region/index pair names, by reference so callers can write it.
    /// Regions with no storage hand back a scratch cell that is never read as inventory —
    /// so a caller can always take a ref without a null check.</summary>
    public ref ItemStack SlotRef(InvRegion region, int index)
    {
        if (!Addressable(region, index)) return ref _sink;
        switch (region)
        {
            case InvRegion.Slots:   return ref Slots[index];
            case InvRegion.Craft:   return ref Craft[index];
            case InvRegion.Weapons: return ref Weapons[index];
            default:                return ref _sink;
        }
    }

    private static ItemStack _sink;

    /// <summary>
    /// The completed drag, as one operation: takes <paramref name="count"/> off the source
    /// slot and lands it on the target, merging with a same-kind occupant up to the stack
    /// ceiling and swapping with a different-kind one when the source will take it back.
    /// Anything that will not fit stays where it was. Returns true if the pack changed.
    /// </summary>
    public bool Move(InvRegion from, int fromIndex, InvRegion to, int toIndex, int count)
    {
        if (!Addressable(from, fromIndex) || !Addressable(to, toIndex)) return false;
        if (from == to && fromIndex == toIndex) return false;
        if (count <= 0) return false;

        ref ItemStack src = ref SlotRef(from, fromIndex);
        if (src.IsEmpty) return false;
        if (!Accepts(to, src.Kind)) return false;

        count = Math.Min(count, src.Count);
        ItemKind kind = src.Kind;
        ref ItemStack dst = ref SlotRef(to, toIndex);

        if (dst.IsEmpty)
        {
            int moved = Math.Min(count, MaxStack(kind));
            dst = new ItemStack(kind, moved);
            Spend(ref src, moved);
            return true;
        }

        if (dst.Kind == kind)
        {
            int room = MaxStack(kind) - dst.Count;
            int moved = Math.Min(room, count);
            if (moved <= 0) return false;
            dst.Count += moved;
            Spend(ref src, moved);
            return true;
        }

        // Different kinds. Only a whole-stack drag swaps, and only if the source will hold
        // what comes back — a fragment cannot be parked in an equip slot on the way past.
        if (count < src.Count) return false;
        if (!Accepts(from, dst.Kind)) return false;
        ItemStack swapped = dst;
        dst = src;
        src = swapped;
        return true;
    }

    private static void Spend(ref ItemStack slot, int count)
    {
        slot.Count -= count;
        if (slot.Count <= 0) slot = ItemStack.Empty;
    }

    /// <summary>
    /// Spends the triangle's three fragments and lands the crafted CRAB CORE on the target
    /// slot. Refuses (and spends nothing) unless the recipe is met and the target is a free
    /// slot that will take a core. Returns true if a core was made.
    /// </summary>
    public bool CraftInto(InvRegion to, int toIndex)
    {
        if (!Addressable(to, toIndex)) return false;
        if (!Accepts(to, ItemKind.CrabCore)) return false;
        if (!SlotRef(to, toIndex).IsEmpty) return false;
        if (!CanCraft()) return false;

        ItemStack core = TakeCraftOutput();
        if (core.IsEmpty) return false;
        SlotRef(to, toIndex) = core;
        return true;
    }

    // --- The wire -------------------------------------------------------------------

    /// <summary>Every slot in the pack, in one fixed order: grid, then the triangle's
    /// corners, then the equip row. The order is the wire format, so it never changes.</summary>
    public const int WireSlots = SlotCount + CraftCount + WeaponCount;

    /// <summary>Bytes one whole pack takes on the wire — a kind and a count per slot.</summary>
    public const int WireSize = WireSlots * 2;

    private ref ItemStack Flat(int i)
    {
        if (i < SlotCount) return ref Slots[i];
        if (i < SlotCount + CraftCount) return ref Craft[i - SlotCount];
        return ref Weapons[i - SlotCount - CraftCount];
    }

    public void Write(Span<byte> dst)
    {
        for (int i = 0; i < WireSlots; i++)
        {
            ref ItemStack s = ref Flat(i);
            dst[i * 2] = (byte)s.Kind;
            dst[i * 2 + 1] = (byte)Math.Clamp(s.Count, 0, 255);
        }
    }

    public void Read(ReadOnlySpan<byte> src)
    {
        for (int i = 0; i < WireSlots; i++)
        {
            var kind = (ItemKind)src[i * 2];
            int count = src[i * 2 + 1];
            // A malformed byte becomes an empty slot rather than a bogus item.
            if (!Enum.IsDefined(kind) || count <= 0) { Flat(i) = ItemStack.Empty; continue; }
            Flat(i) = new ItemStack(kind, count);
        }
    }

    /// <summary>
    /// A cheap digest of the whole pack, so the host can tell whether a seat's inventory
    /// actually changed since the last time it mirrored it. Comparing this beats keeping a
    /// version counter, because the panel writes slots directly and would forget to bump one.
    /// </summary>
    public int Fingerprint()
    {
        int h = 17;
        for (int i = 0; i < WireSlots; i++)
        {
            ref ItemStack s = ref Flat(i);
            h = h * 31 + (s.IsEmpty ? 0 : ((int)s.Kind + 1) * 1000 + s.Count);
        }
        return h;
    }
}
