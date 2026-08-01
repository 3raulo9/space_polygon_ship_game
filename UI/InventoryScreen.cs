using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Rendering;

namespace Unrendered.UI;

// The regions a point can land in — None, Slots, Craft, Weapons, Output — used to be
// declared here. They now live beside the data in Core.Inventory, because they are part of
// the wire: a client addresses its inventory intents in exactly these terms and the host
// replays them against the authoritative pack.

/// <summary>
/// The fixed geometry of the inventory panel at the internal 320×240 resolution, shared
/// by the screen (hit-testing) and the renderer (drawing) so the boxes drawn are exactly
/// the boxes clicked. Lower section: a 5×4 grid of 20 slots. Upper section: a crafting
/// triangle with a box at each corner and an output box at its centre. Along the top: the
/// four R/T/Y/U equip slots.
/// </summary>
public static class InventoryLayout
{
    public const int Slot = 18;   // box side, internal px

    // --- Equip slots (R T Y U), a centred row along the top ---
    private const int WeaponGap = 8;
    public const int WeaponY = 20;
    public static Rectangle Weapon(int i)
    {
        int total = Inventory.WeaponCount * Slot + (Inventory.WeaponCount - 1) * WeaponGap;
        int x0 = (Config.InternalWidth - total) / 2;
        return new Rectangle(x0 + i * (Slot + WeaponGap), WeaponY, Slot, Slot);
    }

    // --- Crafting triangle: three corners + a centre output ---
    // Points chosen so the triangle reads clearly under the equip row and above the grid.
    private static readonly Vector2[] _corners =
    {
        new(160, 62),    // apex
        new(128, 116),   // bottom-left
        new(192, 116),   // bottom-right
    };
    public static Vector2 CraftCorner(int i) => _corners[i];
    public static Vector2 CraftCentroid =>
        (_corners[0] + _corners[1] + _corners[2]) / 3f;

    public static Rectangle Craft(int i) => Boxed(_corners[i]);
    public static Rectangle Output => Boxed(CraftCentroid);

    // --- The 20-slot grid, lower section ---
    public const int GridCols = 5;
    public const int GridRows = 4;
    private const int GridGap = 4;
    public static int GridTop => Config.InternalHeight -
        (GridRows * Slot + (GridRows - 1) * GridGap) - 24;
    public static int GridLeft
    {
        get
        {
            int total = GridCols * Slot + (GridCols - 1) * GridGap;
            return (Config.InternalWidth - total) / 2;
        }
    }
    public static Rectangle GridSlot(int i)
    {
        int col = i % GridCols, row = i / GridCols;
        return new Rectangle(GridLeft + col * (Slot + GridGap),
                             GridTop + row * (Slot + GridGap), Slot, Slot);
    }

    private static Rectangle Boxed(Vector2 centre) =>
        new(centre.X - Slot / 2f, centre.Y - Slot / 2f, Slot, Slot);

    /// <summary>Finds which slot a point lands in. Returns the region and its index
    /// (index 0 for the single output box).</summary>
    public static (InvRegion region, int index) Locate(Vector2 p)
    {
        for (int i = 0; i < Inventory.WeaponCount; i++)
            if (Hit(Weapon(i), p)) return (InvRegion.Weapons, i);
        for (int i = 0; i < Inventory.CraftCount; i++)
            if (Hit(Craft(i), p)) return (InvRegion.Craft, i);
        if (Hit(Output, p)) return (InvRegion.Output, 0);
        for (int i = 0; i < Inventory.SlotCount; i++)
            if (Hit(GridSlot(i), p)) return (InvRegion.Slots, i);
        return (InvRegion.None, -1);
    }

    private static bool Hit(Rectangle r, Vector2 p) =>
        p.X >= r.X && p.X < r.X + r.Width && p.Y >= r.Y && p.Y < r.Y + r.Height;
}

/// <summary>
/// The inventory / crafting panel's interaction state: mouse-driven drag-and-drop of
/// stacks between the grid, the crafting triangle and the equip slots, plus right-click
/// to spend a battery or bullet stack straight into the craft's shield / hyper / ammo.
/// Pure logic over the world's <see cref="Inventory"/> and <see cref="PlayerTank"/> — the
/// renderer reads <see cref="Held"/> / <see cref="Cursor"/> to draw the dragged stack.
///
/// <para><b>In a match the pack is not this machine's to change.</b> The panel still drags
/// against the local mirror, because a drag that waited a round trip to show anything would
/// be unusable — but every completed action is also filed as an <see cref="InvIntent"/> for
/// the host, which replays it against the real pack with the same rules
/// (<see cref="Inventory.Move"/>) and mirrors the result back. On a host or a solo run the
/// mirror <em>is</em> the pack and the intents are dropped.</para>
///
/// <para>The drag is deliberately expressed to the host as one completed operation rather
/// than as a pickup and a drop: while a stack is riding the cursor here it is still sitting
/// in its source slot as far as the host is concerned, so a link that dies mid-drag loses
/// nothing at all.</para>
/// </summary>
public sealed class InventoryScreen
{
    /// <summary>The stack currently riding the cursor, or empty when nothing is dragged.</summary>
    public ItemStack Held { get; private set; } = ItemStack.Empty;

    /// <summary>The cursor in internal 320×240 space — the renderer draws the held stack
    /// here.</summary>
    public Vector2 Cursor { get; private set; }

    // Where the held stack came from, so an invalid drop can put it back untouched. When
    // the source is the crafting output the pickup is only *provisional* — the fragments
    // are not spent until the core actually lands somewhere valid.
    private InvRegion _sourceRegion = InvRegion.None;
    private int _sourceIndex = -1;

    /// <summary>Clears any drag state when the panel opens.</summary>
    public void Reset()
    {
        Held = ItemStack.Empty;
        _sourceRegion = InvRegion.None;
        _sourceIndex = -1;
    }

    /// <summary>Closing the panel: drop any half-held stack back where it came from so
    /// nothing is stranded on the cursor.</summary>
    public void Cancel(World.World world)
    {
        if (!Held.IsEmpty) ReturnToSource(world.Inventory);
        Reset();
    }

    public void Update(World.World world)
    {
        Cursor = Renderer.ScreenToInternal(Raylib.GetMousePosition());
        var inv = world.Inventory;
        var (region, index) = InventoryLayout.Locate(Cursor);

        // Left-click grabs a whole stack onto the cursor; releasing drops it.
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            Pickup(inv, region, index);
        else if (Raylib.IsMouseButtonReleased(MouseButton.Left))
            Drop(world, inv, region, index);

        // Right-click, while carrying a stack (left still held), peels a single unit off
        // into the slot under the cursor — the way one fragment at a time goes into each
        // craft corner. With empty hands instead, right-click spends a battery/bullet
        // stack straight into the craft.
        if (Raylib.IsMouseButtonPressed(MouseButton.Right))
        {
            if (!Held.IsEmpty) PlaceOne(world, inv, region, index);
            else RightClickCharge(world, region, index);
        }
    }

    /// <summary>
    /// Drops one unit of the held stack into the slot under the cursor — the "hold
    /// left-click, click right" split used to feed single fragments into the crafting
    /// triangle. The target must accept the kind and be empty or a same-kind stack with
    /// room; otherwise the click is ignored. Emptying the held pile clears the drag.
    /// </summary>
    private void PlaceOne(World.World world, Inventory inv, InvRegion region, int index)
    {
        if (region is InvRegion.None or InvRegion.Output) return;
        if (!Inventory.Accepts(region, Held.Kind)) return;

        ref ItemStack target = ref inv.SlotRef(region, index);
        if (target.IsEmpty)
        {
            target = new ItemStack(Held.Kind, 1);
        }
        else if (target.Kind == Held.Kind && target.Count < Inventory.MaxStack(target.Kind))
        {
            target.Count++;
        }
        else
        {
            return; // full, or a different kind sitting there — nothing placed
        }

        // One unit off the source slot, which as far as the host is concerned still holds
        // the whole pile riding this cursor.
        File(world, InvOp.Move, region, index, 1);

        Held = new ItemStack(Held.Kind, Held.Count - 1);
        if (Held.IsEmpty) ClearHeld();
    }

    // --- Drag pickup ----------------------------------------------------------

    private void Pickup(Inventory inv, InvRegion region, int index)
    {
        if (!Held.IsEmpty) return;

        // The output box hands out a fresh CRAB CORE preview; the fragments are only
        // spent once it's dropped somewhere valid (see PlaceCraftedCore). Nothing has
        // happened yet, so the host is told nothing either.
        if (region == InvRegion.Output)
        {
            if (!inv.CanCraft()) return;
            Held = new ItemStack(ItemKind.CrabCore, 1);
            _sourceRegion = InvRegion.Output;
            _sourceIndex = 0;
            return;
        }

        ref ItemStack slot = ref inv.SlotRef(region, index);
        if (Unsafe(region) || slot.IsEmpty) return;

        // Lifted off the local mirror only. As far as the host is concerned this stack is
        // still in its slot and stays there until the drop names where it went — so a link
        // that dies mid-drag strands nothing.
        Held = slot;
        slot = ItemStack.Empty;
        _sourceRegion = region;
        _sourceIndex = index;
    }

    // --- Drag drop ------------------------------------------------------------

    private void Drop(World.World world, Inventory inv, InvRegion region, int index)
    {
        if (Held.IsEmpty) return;

        // Nowhere valid under the cursor, or the output box (never a drop target): put
        // it back where it started. Nothing to tell the host — it never moved.
        if (region is InvRegion.None or InvRegion.Output || !Inventory.Accepts(region, Held.Kind))
        {
            ReturnToSource(inv);
            return;
        }

        // A freshly crafted core: only lands on an empty (or same-kind) slot, and only
        // then are the fragments consumed.
        if (_sourceRegion == InvRegion.Output)
        {
            PlaceCraftedCore(world, inv, region, index);
            return;
        }

        ref ItemStack target = ref inv.SlotRef(region, index);

        if (target.IsEmpty)
        {
            File(world, InvOp.Move, region, index, Held.Count);
            target = Held;
            ClearHeld();
            return;
        }

        // Same kind: merge up to the stack ceiling, any overflow flows back to source.
        if (target.Kind == Held.Kind)
        {
            int max = Inventory.MaxStack(target.Kind);
            int room = max - target.Count;
            int moved = Math.Min(room, Held.Count);
            if (moved > 0) File(world, InvOp.Move, region, index, moved);
            target.Count += moved;
            Held = new ItemStack(Held.Kind, Held.Count - moved);
            if (Held.IsEmpty) ClearHeld();
            else ReturnToSource(inv);
            return;
        }

        // Different kinds: swap, but only if the source will accept what comes back.
        if (Inventory.Accepts(_sourceRegion, target.Kind))
        {
            File(world, InvOp.Move, region, index, Held.Count);
            ItemStack swapped = target;
            target = Held;
            PutBack(_sourceRegion, _sourceIndex, inv, swapped);
            ClearHeld();
        }
        else
        {
            ReturnToSource(inv);
        }
    }

    /// <summary>Lands a crafted core on the target slot and only now spends the three
    /// fragments. If the target isn't free the craft is abandoned with nothing lost.</summary>
    private void PlaceCraftedCore(World.World world, Inventory inv, InvRegion region, int index)
    {
        ref ItemStack target = ref inv.SlotRef(region, index);
        bool free = target.IsEmpty;
        if (!free) { ClearHeld(); return; }   // preview discarded, no fragments spent

        ItemStack core = inv.TakeCraftOutput();   // spends one fragment per corner
        if (core.IsEmpty) { ClearHeld(); return; } // recipe slipped away — shouldn't happen
        target = core;
        world.FileInvIntent(new InvIntent(InvOp.CraftInto, InvRegion.Output, 0,
                                          region, (byte)index, 1));
        ClearHeld();
    }

    // --- Right-click: spend a stack straight into the craft --------------------

    private void RightClickCharge(World.World world, InvRegion region, int index)
    {
        // Only the main grid charges — the craft slots and the crafting corners are for
        // equipping and building, not for burning fuel.
        if (region != InvRegion.Slots) return;

        // The spend itself lives on the world now, so the host can run exactly this for a
        // client that asked for it. All the panel does is name the slot.
        if (!world.ChargeFromSlot(world.Player, world.Inventory, index)) return;
        world.FileInvIntent(new InvIntent(InvOp.Charge, InvRegion.Slots, (byte)index,
                                          InvRegion.None, 0, 1));
    }

    // --- Slot addressing + rules ----------------------------------------------
    // Which region takes which kind, and what a move does to the slots it touches, now live
    // on Core.Inventory — the host has to be able to replay a client's drag and land it in
    // the same place, and two copies of those rules would drift. What is left here is the
    // drag *state*, which is a fact about this mouse and belongs to no one else.

    /// <summary>Regions a stack can never be *picked up* from (there are none today, but
    /// keeps the pickup path honest).</summary>
    private static bool Unsafe(InvRegion region) => region is InvRegion.None or InvRegion.Output;

    /// <summary>Files the completed operation for the host — from wherever the drag began to
    /// wherever it landed. A no-op on a machine that owns its own pack.</summary>
    private void File(World.World world, InvOp op, InvRegion to, int toIndex, int count)
    {
        if (!Inventory.Addressable(_sourceRegion, _sourceIndex)) return;
        world.FileInvIntent(new InvIntent(op, _sourceRegion, (byte)_sourceIndex,
                                          to, (byte)toIndex,
                                          (byte)Math.Clamp(count, 0, 255)));
    }

    /// <summary>
    /// Puts a stack into a slot, merging with a same-kind occupant rather than clobbering
    /// it (so returning a split unit re-joins the pile it came from). Anything that won't
    /// fit — a full slot, or a different-kind occupant — flows into the grid, and only if
    /// even that overflows is it dropped.
    /// </summary>
    private void PutBack(InvRegion region, int index, Inventory inv, ItemStack stack)
    {
        if (region is InvRegion.None or InvRegion.Output || stack.IsEmpty) return;
        ref ItemStack slot = ref inv.SlotRef(region, index);

        if (slot.IsEmpty)
        {
            slot = stack;
            return;
        }
        if (slot.Kind == stack.Kind)
        {
            int room = Inventory.MaxStack(slot.Kind) - slot.Count;
            int moved = Math.Min(room, stack.Count);
            slot.Count += moved;
            int leftover = stack.Count - moved;
            if (leftover > 0) inv.Add(stack.Kind, leftover);
            return;
        }
        inv.Add(stack.Kind, stack.Count);   // different kind sits there — spill to the grid
    }

    /// <summary>Puts the held stack back where it was picked up from and clears the drag.
    /// A provisional craft preview (source = Output) is simply discarded, so no fragments
    /// are spent.</summary>
    private void ReturnToSource(Inventory inv)
    {
        if (_sourceRegion is not (InvRegion.None or InvRegion.Output))
            PutBack(_sourceRegion, _sourceIndex, inv, Held);
        ClearHeld();
    }

    private void ClearHeld()
    {
        Held = ItemStack.Empty;
        _sourceRegion = InvRegion.None;
        _sourceIndex = -1;
    }
}
