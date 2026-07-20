using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.UI;

namespace VoidTanks.Rendering;

/// <summary>
/// Draws the inventory / crafting panel flat over the frozen world, at the internal
/// 320×240 resolution so it shares the chunky pixels. Reads the world's
/// <see cref="Inventory"/> and the <see cref="InventoryScreen"/>'s drag state; never
/// mutates anything. Geometry comes straight from <see cref="InventoryLayout"/>, the
/// same source the click hit-testing uses, so every box drawn is a box you can click.
/// </summary>
internal static class InventoryRenderer
{
    private const int W = Config.InternalWidth;
    private const int H = Config.InternalHeight;
    private const int S = InventoryLayout.Slot;

    private static readonly Color Panel = new(6, 10, 14, 205);
    private static readonly Color SlotWell = new(12, 22, 26, 230);
    private static readonly Color SlotEdge = new(70, 92, 100, 255);
    private static readonly Color Ink = new(200, 218, 224, 255);

    // The live 3D icon source for this frame, handed in by DrawInventory.
    private static ItemIconRenderer? _icons;

    public static void Draw(World.World world, InventoryScreen screen, float elapsed,
        ItemIconRenderer icons)
    {
        _icons = icons;
        var inv = world.Inventory;

        // A full-screen wash so the panel reads as a surface over the suspended run.
        Raylib.DrawRectangle(0, 0, W, H, Panel);

        PixelFont.DrawCentered("INVENTORY", W / 2, 5, 2, Ink);

        // --- Equip slots (R T Y U) ---
        string letters = "RTYU";
        for (int i = 0; i < Inventory.WeaponCount; i++)
        {
            Rectangle b = InventoryLayout.Weapon(i);
            DrawSlot(b, inv.Weapons[i]);
            PixelFont.DrawCentered(letters[i].ToString(), (int)(b.X + b.Width / 2),
                (int)(b.Y + b.Height + 2), 1, Scale(Ink, 0.85f));
        }

        // --- Crafting triangle ---
        Vector2 a = InventoryLayout.CraftCorner(0);
        Vector2 bl = InventoryLayout.CraftCorner(1);
        Vector2 br = InventoryLayout.CraftCorner(2);
        Color edge = Scale(Palette.NeonRed, inv.CanCraft() ? 1f : 0.5f);
        Raylib.DrawLineV(a, bl, edge);
        Raylib.DrawLineV(bl, br, edge);
        Raylib.DrawLineV(br, a, edge);

        for (int i = 0; i < Inventory.CraftCount; i++)
            DrawSlot(InventoryLayout.Craft(i), inv.Craft[i]);

        // The output box: a live CRAB CORE when the recipe is met, else a dim empty well.
        Rectangle outBox = InventoryLayout.Output;
        ItemStack output = inv.CraftOutput();
        DrawSlot(outBox, ItemStack.Empty);
        if (!output.IsEmpty)
        {
            // A gentle pulse so the craftable core reads as "ready", then its icon.
            float pulse = 0.6f + 0.4f * MathF.Sin(elapsed * 5f);
            Raylib.DrawRectangleLinesEx(Grow(outBox, 1), 1f, Scale(Palette.NeonMagenta, pulse));
            DrawIcon(outBox, ItemKind.CrabCore);
        }

        // --- The 20-slot grid ---
        for (int i = 0; i < Inventory.SlotCount; i++)
            DrawSlot(InventoryLayout.GridSlot(i), inv.Slots[i]);

        // --- Hints along the bottom ---
        PixelFont.DrawCentered("LCLICK-MOVE STACK    RCLICK-USE STACK",
            W / 2, H - 15, 1, Scale(Ink, 0.6f));
        PixelFont.DrawCentered("HOLD LCLICK THEN RCLICK-DROP ONE    E-CLOSE",
            W / 2, H - 7, 1, Scale(Ink, 0.6f));

        // --- The dragged stack rides the cursor last, over everything ---
        if (!screen.Held.IsEmpty)
        {
            var c = screen.Cursor;
            var box = new Rectangle(c.X - S / 2f, c.Y - S / 2f, S, S);
            DrawIcon(box, screen.Held.Kind);
            DrawCount(box, screen.Held.Count);
        }

        // A small square cursor so the (hidden-in-world) pointer reads on the panel.
        Raylib.DrawRectangleLines((int)screen.Cursor.X - 1, (int)screen.Cursor.Y - 1, 3, 3, Ink);
    }

    // --- Slot + item drawing --------------------------------------------------

    private static void DrawSlot(Rectangle box, ItemStack stack)
    {
        Raylib.DrawRectangleRec(box, SlotWell);
        Raylib.DrawRectangleLinesEx(box, 1f, Scale(SlotEdge, 0.8f));
        if (stack.IsEmpty) return;
        DrawIcon(box, stack.Kind);
        DrawCount(box, stack.Count);
    }

    /// <summary>Blits a kind's rotating 3D model (rendered this frame by the
    /// <see cref="ItemIconRenderer"/>) inset into a slot, so the salvage turns slowly on
    /// the spot instead of sitting there as a flat square. The source is flipped
    /// vertically because render textures are stored bottom-up.</summary>
    private static void DrawIcon(Rectangle box, ItemKind kind)
    {
        if (_icons is null) return;
        var inner = Grow(box, -1);
        var src = new Rectangle(0, 0, ItemIconRenderer.Size, -ItemIconRenderer.Size);
        Raylib.DrawTexturePro(_icons.Texture(kind), src, inner, Vector2.Zero, 0f, Color.White);
    }

    private static void DrawCount(Rectangle box, int count)
    {
        if (count <= 1) return;
        string s = count.ToString();
        int w = PixelFont.Measure(s, 1);
        // Bottom-right, with a dark plate behind so it reads over any icon colour.
        int tx = (int)(box.X + box.Width) - w - 1;
        int ty = (int)(box.Y + box.Height) - PixelFont.GlyphH - 1;
        Raylib.DrawRectangle(tx - 1, ty - 1, w + 1, PixelFont.GlyphH + 2, new Color(0, 0, 0, 190));
        PixelFont.Draw(s, tx, ty, 1, Color.White);
    }

    // --- helpers --------------------------------------------------------------

    /// <summary>Insets (negative) or expands (positive) a rectangle on all sides.</summary>
    private static Rectangle Grow(Rectangle r, float by) =>
        new(r.X - by, r.Y - by, r.Width + by * 2, r.Height + by * 2);

    private static Color Scale(Color col, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color((int)(col.R * t), (int)(col.G * t), (int)(col.B * t), col.A);
    }
}
