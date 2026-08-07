namespace Unrendered.Core;

/// <summary>What a carried item is. Salvage the craft scoops off the grid, the CRAB CORE
/// fragment harvested from a slain boss and the weapon crafted from three of them, and the
/// raw materials a kill leaves behind or a finished item gives up when it is taken apart.
///
/// <para><b>Append only.</b> The value is the byte on the wire, both in a pack
/// (<see cref="Inventory.Write"/>) and on a floating pickup, so reordering these renames
/// every item in every pack in a match built against the old order.</para>
/// </summary>
public enum ItemKind
{
    Battery,       // repairs shield + hyper when spent
    Bullet,        // reloads the cannon when spent
    CrabFragment,  // dropped by a killed Crab-Core; three craft a CRAB CORE
    CrabCore,      // the throwable radial-beam bomb

    // --- Materials: what things are made of, and what they come back apart into ---
    ScrapMetal,      // the common one — nearly every kill leaves some
    CopperWire,      // drawn winding; the scarce one off a kill
    SpaceGunpowder,  // the push behind a round
    DenseAlloy,      // the mass in front of it
    Lead,            // \
    Zinc,            //  > a cell's anode metal: one of the three, never known in advance
    Lithium,         // /

    /// <summary>Mends the hull, and is the only thing that does. A battery puts shield
    /// charges back on the stack; nothing puts hull back but this.</summary>
    RepairKit,

    /// <summary>
    /// A piece of the moon. It falls, on the one world that has a moon to fall off, and it is
    /// the rarest thing anybody can be holding.
    ///
    /// <para>Spent, it does all three things at once — every shield charge, the hull whole, the
    /// reserve full. Nothing else in the game does more than one, and that is the point of it:
    /// a cell is a decision about the next thirty seconds and this is a decision about the run.
    /// It cannot be crafted and it cannot be taken apart. You do not open a piece of the
    /// moon.</para>
    ///
    /// <para><b>Called MOON FRAGMENT until the arch was built.</b> It had to give the name up:
    /// a boss now leaves a FRAGMENT OF THE MOON on the ground, and two collectible moon rocks
    /// with the same name and opposite purposes is a player throwing away the run's progress
    /// because they thought it was a heal. This one is named for the colour it is drawn in
    /// (<see cref="Palette.MoonStone"/>), which is lifted off the moon disc in the sky.</para>
    /// </summary>
    Moonstone,

    // --- The arch's keys ---------------------------------------------------------------
    // What a DESCENT boss leaves behind. Five of them open the way off a planet, and that is
    // the whole of what they do: they mend nothing, they are made of nothing, and they cannot
    // be taken apart. Appended here rather than filed beside the moonstone deliberately — the
    // enum is the wire, and these are the newest thing in it.

    /// <summary>Half of what a boss can give up. Kept, carried, and fed to an arch.</summary>
    SunFragment,

    /// <summary>The other half. The coin is fair and has no memory, so a run really can end
    /// with five of one and none of the other.</summary>
    MoonFragment,
}

/// <summary>What each item is called when the panel has room to say so — the hover label,
/// and nothing else. Kept beside the enum so a new kind is named in the same edit that
/// creates it, rather than falling through to a placeholder nobody notices.</summary>
public static class ItemNames
{
    public static string Of(ItemKind kind) => kind switch
    {
        ItemKind.Battery        => "BATTERY CELL",
        ItemKind.Bullet         => "ROUNDS",
        ItemKind.CrabFragment   => "CRAB FRAGMENT",
        ItemKind.CrabCore       => "CRAB CORE",
        ItemKind.ScrapMetal     => "SCRAP METAL",
        ItemKind.CopperWire     => "COPPER WIRE",
        ItemKind.SpaceGunpowder => "SPACE GUNPOWDER",
        ItemKind.DenseAlloy     => "DENSE ALLOY",
        ItemKind.Lead           => "LEAD",
        ItemKind.Zinc           => "ZINC",
        ItemKind.Lithium        => "LITHIUM",
        ItemKind.RepairKit      => "REPAIR KIT",
        ItemKind.Moonstone      => "MOONSTONE",
        ItemKind.SunFragment    => "FRAGMENT OF THE SUN",
        ItemKind.MoonFragment   => "FRAGMENT OF THE MOON",
        _                       => "SALVAGE",
    };
}

/// <summary>
/// Which cluster of slots an address names. Lives here rather than beside the panel that
/// draws it because it is now part of the <em>wire</em>: a client's inventory moves are sent
/// to the host as intents addressed in these terms, and the host replays them against the
/// authoritative pack with the same rules (see <see cref="Inventory.Move"/>).
/// <see cref="None"/> is empty panel; <see cref="Output"/> is the crafting triangle's centre,
/// which holds nothing and is only ever a source. <see cref="Break"/> is the take-apart
/// bench's single input and <see cref="Parts"/> the row of slots its arrows come down into —
/// which is real storage, but out-only: the bench fills it, the player empties it.
/// </summary>
public enum InvRegion : byte { None, Slots, Craft, Weapons, Output, Break, Parts }

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
    /// <summary>Pull one item off the take-apart bench into its parts. Addresses nothing:
    /// the bench has one input slot and the table decides the rest.</summary>
    Break,
    /// <summary>Throw one unit off the named slot out onto the grid, where it becomes a
    /// piece of salvage anybody can drive over. <c>To</c> addresses nothing — where it lands
    /// is the thrower's own position, which only the host knows for certain.</summary>
    Throw,
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
/// One player's carried goods: a 20-slot grid, the two workbenches — the assembly triangle's
/// three corners and the take-apart bench's input plus the row of parts that come off it —
/// and the four equip slots wired to R/T/Y/U. Pure data + placement rules; the world reads it
/// to spend items and the UI drives drag/drop, crafting and teardown against it. Lives on
/// <see cref="Unrendered.World.World"/> so it survives a paused/opened inventory the same way
/// the rest of the run does.
///
/// <para>There is one of these <em>per seat</em>, not one per world: salvage is personal, so
/// the craft that drove over a cell is the craft that keeps it. The host owns every seat's
/// pack and mirrors each client its own; a client's copy is a mirror it may scribble on for
/// responsiveness, never the truth (see <see cref="InvIntent"/>).</para>
/// </summary>
public sealed class Inventory
{
    public const int SlotCount = 20;
    public const int CraftCount = 3;     // the assembly triangle's three corners
    public const int WeaponCount = 4;    // R T Y U
    public const int BreakCount = 1;     // the take-apart bench's single input
    public const int PartCount = 3;      // the most arrows any teardown can come down

    /// <summary>How many parts one assembly costs — one from each triangle corner.</summary>
    public const int CraftCost = CraftCount;

    public readonly ItemStack[] Slots = new ItemStack[SlotCount];
    public readonly ItemStack[] Craft = new ItemStack[CraftCount];
    public readonly ItemStack[] Weapons = new ItemStack[WeaponCount];

    /// <summary>What is sitting on the take-apart bench waiting to be opened.</summary>
    public readonly ItemStack[] Break = new ItemStack[BreakCount];

    /// <summary>What has come off it, one slot per arrow. Filled by <see cref="BreakOne"/>
    /// and emptied by hand — the player drags the parts down into the grid.</summary>
    public readonly ItemStack[] Parts = new ItemStack[PartCount];

    /// <summary>The stacking ceiling for a kind: batteries 4, bullets 20, fragments a
    /// small pile, raw materials by the handful, the crafted core one at a time. Repair
    /// kits stack low — hull is the layer you cannot get back cheaply, and a pocket of
    /// twenty of them would make it one you never had to think about.</summary>
    public static int MaxStack(ItemKind kind) => kind switch
    {
        ItemKind.Battery      => 4,
        ItemKind.RepairKit    => 2,
        // One. Not because two would be unbalanced — it would, but that is what rarity is for —
        // because a slot holding "MOONSTONE x3" makes it a supply, and the whole of what
        // this item is worth is that having one is an event.
        ItemKind.Moonstone => 1,
        // Also one, for a different reason: five fragments must cost five of the twenty slots.
        // A stack would make carrying the run's progress free, and the weight is the point —
        // a player holding all five is carrying a quarter of a pack in rocks that do nothing,
        // which is exactly the pressure that makes a room split the load.
        ItemKind.SunFragment or ItemKind.MoonFragment => 1,
        ItemKind.Bullet       => 20,
        ItemKind.CrabFragment => 9,
        ItemKind.CrabCore     => 1,
        ItemKind.ScrapMetal or ItemKind.CopperWire or ItemKind.SpaceGunpowder
            or ItemKind.DenseAlloy or ItemKind.Lead or ItemKind.Zinc
            or ItemKind.Lithium   => 20,
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

    /// <summary>The recipe the three corners currently satisfy, or null. The bench does not
    /// care which corner holds what — see <see cref="Crafting.Match"/>.</summary>
    public Crafting.Recipe? Recipe() => Crafting.Match(Craft);

    /// <summary>Whether the three corners make anything at all.</summary>
    public bool CanCraft() => Recipe() != null;

    /// <summary>The preview shown in the triangle's centre while something is craftable,
    /// or an empty stack. Reading it never consumes anything.</summary>
    public ItemStack CraftOutput()
    {
        Crafting.Recipe? r = Recipe();
        return r is null ? ItemStack.Empty : new ItemStack(r.Output, r.Count);
    }

    /// <summary>
    /// Claims what the bench made: spends one part from each corner and hands back the
    /// output stack. Returns an empty stack (spending nothing) if no recipe is met.
    /// </summary>
    public ItemStack TakeCraftOutput()
    {
        Crafting.Recipe? r = Recipe();
        if (r is null) return ItemStack.Empty;
        for (int i = 0; i < Craft.Length; i++)
        {
            Craft[i].Count--;
            if (Craft[i].Count <= 0) Craft[i] = ItemStack.Empty;
        }
        return new ItemStack(r.Output, r.Count);
    }

    // --- The take-apart bench ---------------------------------------------------------

    /// <summary>
    /// Opens one item off the bench, landing each part under the arrow that promised it. A
    /// part whose slot is busy with something else — or already piled to the ceiling — spills
    /// into the grid rather than being lost, and only if the grid is full too does it go.
    ///
    /// <para>One item per call, not the whole stack: a teardown with a coin toss in it is
    /// rolled per item, and a player watching four batteries open one at a time can see which
    /// of them gave up its metal.</para>
    ///
    /// <para>Runs identically on both ends of the wire (the host replays a client's
    /// <see cref="InvOp.Break"/> through this same method) — except for the roll, which no two
    /// machines can agree on. The host's echo settles that, as it settles everything else.</para>
    /// </summary>
    public bool BreakOne(Random? rng = null)
    {
        ref ItemStack src = ref Break[0];
        if (src.IsEmpty || !Crafting.CanBreak(src.Kind)) return false;

        Span<ItemStack> parts = stackalloc ItemStack[PartCount];
        Crafting.Roll(src.Kind, parts, rng);
        Spend(ref src, 1);

        for (int i = 0; i < PartCount; i++)
        {
            if (parts[i].IsEmpty) continue;
            ref ItemStack dst = ref Parts[i];
            if (dst.IsEmpty)
                dst = parts[i];
            else if (dst.Kind == parts[i].Kind && dst.Count + parts[i].Count <= MaxStack(dst.Kind))
                dst.Count += parts[i].Count;
            else
                Add(parts[i].Kind, parts[i].Count);   // arrow's slot busy — down to the grid
        }
        return true;
    }

    // --- Addressing, and the placement rules both ends of the wire run ---------------
    // These used to live in the panel, which was fine while the panel was the only thing
    // that ever touched a pack. It is not any more: the host has to replay a client's move
    // against the authoritative inventory, and it must land in exactly the same place the
    // client's optimistic mirror put it. So the rules live on the data.

    /// <summary>
    /// Whether a region will hold a given kind: equip slots take only the crafted core, the
    /// crafting corners only things some recipe is built out of, the bench only things that
    /// can be opened, and the grid anything.
    ///
    /// <para><see cref="InvRegion.Parts"/> accepts nothing, which is what makes the take-apart
    /// bench's output row one-way: the bench puts parts there and the player takes them out,
    /// but nothing can be dropped in — a slot the player could fill is a slot the next
    /// teardown has to spill around.</para>
    /// </summary>
    public static bool Accepts(InvRegion region, ItemKind kind) => region switch
    {
        InvRegion.Slots   => true,
        InvRegion.Weapons => kind == ItemKind.CrabCore,
        InvRegion.Craft   => Crafting.IsIngredient(kind),
        InvRegion.Break   => Crafting.CanBreak(kind),
        _                 => false,
    };

    /// <summary>How many slots a region has, or 0 for the ones that store nothing.</summary>
    public static int Length(InvRegion region) => region switch
    {
        InvRegion.Slots   => SlotCount,
        InvRegion.Craft   => CraftCount,
        InvRegion.Weapons => WeaponCount,
        InvRegion.Break   => BreakCount,
        InvRegion.Parts   => PartCount,
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
            case InvRegion.Break:   return ref Break[index];
            case InvRegion.Parts:   return ref Parts[index];
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
    /// Peels one unit off the named slot and hands back what it was, for a caller that is
    /// taking it out of the pack entirely — the throw. Any slot the pack can address will
    /// give one up, including the benches: a corner of the triangle or a part off a teardown
    /// is as much yours to be rid of as anything on the grid.
    ///
    /// <para>The half a throw that is only about slots, so both ends of the wire run exactly
    /// this and their packs agree whatever the host then does with the item. Returns false and
    /// changes nothing if the slot is not there or is empty.</para>
    /// </summary>
    public bool TakeOne(InvRegion region, int index, out ItemKind kind)
    {
        kind = default;
        if (!Addressable(region, index)) return false;
        ref ItemStack slot = ref SlotRef(region, index);
        if (slot.IsEmpty) return false;
        kind = slot.Kind;
        Spend(ref slot, 1);
        return true;
    }

    /// <summary>
    /// Spends the triangle's three parts and lands what they made on the target slot. Refuses
    /// (and spends nothing) unless a recipe is met and the target is a free slot that will
    /// take the output. Returns true if something was made.
    /// </summary>
    public bool CraftInto(InvRegion to, int toIndex)
    {
        if (!Addressable(to, toIndex)) return false;
        ItemStack made = CraftOutput();
        if (made.IsEmpty) return false;
        if (!Accepts(to, made.Kind)) return false;
        if (!SlotRef(to, toIndex).IsEmpty) return false;

        made = TakeCraftOutput();
        if (made.IsEmpty) return false;
        SlotRef(to, toIndex) = made;
        return true;
    }

    // --- The wire -------------------------------------------------------------------

    /// <summary>Every slot in the pack, in one fixed order: grid, the triangle's corners, the
    /// equip row, the take-apart bench, then the row its parts come down into. Append only —
    /// the order is the wire format, so an existing region never moves.</summary>
    public const int WireSlots = SlotCount + CraftCount + WeaponCount + BreakCount + PartCount;

    /// <summary>Bytes one whole pack takes on the wire — a kind and a count per slot.</summary>
    public const int WireSize = WireSlots * 2;

    private ref ItemStack Flat(int i)
    {
        if (i < SlotCount) return ref Slots[i];
        i -= SlotCount;
        if (i < CraftCount) return ref Craft[i];
        i -= CraftCount;
        if (i < WeaponCount) return ref Weapons[i];
        i -= WeaponCount;
        if (i < BreakCount) return ref Break[i];
        return ref Parts[i - BreakCount];
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
