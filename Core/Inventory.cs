namespace VoidTanks.Core;

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
/// The player's carried goods: a 20-slot grid, the crafting triangle's three corner
/// inputs, and the four equip slots wired to R/T/Y/U. Pure data + placement rules;
/// the world reads it to spend items and the UI drives drag/drop and crafting against
/// it. Lives on <see cref="VoidTanks.World.World"/> so it survives a paused/opened
/// inventory the same way the rest of the run does.
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
}
