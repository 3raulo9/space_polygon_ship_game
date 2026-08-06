using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.UI;

namespace Unrendered.Rendering;

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

        // --- The two benches, mirrored about the middle of the screen ---
        DrawAssembly(inv, elapsed);
        DrawTeardown(inv, elapsed);

        // --- The 20-slot grid ---
        for (int i = 0; i < Inventory.SlotCount; i++)
            DrawSlot(InventoryLayout.GridSlot(i), inv.Slots[i]);

        // --- Hints along the bottom ---
        // Three lines of 7px glyphs, the topmost sitting one pixel under the grid's last row.
        // "PLACE ONE" rather than the old "DROP ONE": there is a real drop now, and the two
        // must not read as the same gesture.
        PixelFont.DrawCentered("LCLICK-MOVE STACK    RCLICK-USE OR BREAK ONE",
            W / 2, H - 23, 1, Scale(Ink, 0.6f));
        PixelFont.DrawCentered("HOLD LCLICK THEN RCLICK-PLACE ONE",
            W / 2, H - 15, 1, Scale(Ink, 0.6f));
        PixelFont.DrawCentered("SHIFT+RCLICK-THROW ONE OUT    F-CLOSE",
            W / 2, H - 7, 1, Scale(Ink, 0.6f));

        // --- The hover label, then the dragged stack, then the pointer ---
        DrawHover(inv, screen);

        if (!screen.Held.IsEmpty)
        {
            var c = screen.Cursor;
            var box = new Rectangle(c.X - S / 2f, c.Y - S / 2f, S, S);
            DrawIcon(box, screen.Held.Kind);
            DrawCount(box, screen.Held.Count);
        }

        DrawPixelCursor(screen.Cursor);
    }

    // --- The assembly bench ---------------------------------------------------
    // Three corners feeding a centre. Unchanged in how it works; it has simply moved off the
    // middle of the screen to sit opposite the bench that undoes what it does.

    private static void DrawAssembly(Inventory inv, float elapsed)
    {
        PixelFont.DrawCentered("ASSEMBLE", InventoryLayout.BenchLeft,
            InventoryLayout.BenchLabelY, 1, Scale(Ink, 0.75f));

        Vector2 a = InventoryLayout.CraftCorner(0);
        Vector2 bl = InventoryLayout.CraftCorner(1);
        Vector2 br = InventoryLayout.CraftCorner(2);
        Color edge = Scale(Palette.NeonRed, inv.CanCraft() ? 1f : 0.5f);
        Raylib.DrawLineV(a, bl, edge);
        Raylib.DrawLineV(bl, br, edge);
        Raylib.DrawLineV(br, a, edge);

        for (int i = 0; i < Inventory.CraftCount; i++)
            DrawSlot(InventoryLayout.Craft(i), inv.Craft[i]);

        // The output box: whatever the corners make, live, else a dim empty well.
        Rectangle outBox = InventoryLayout.Output;
        ItemStack output = inv.CraftOutput();
        DrawSlot(outBox, ItemStack.Empty);
        if (!output.IsEmpty)
        {
            // A gentle pulse so a finished recipe reads as "ready", then its icon and count.
            float pulse = 0.6f + 0.4f * MathF.Sin(elapsed * 5f);
            Raylib.DrawRectangleLinesEx(Grow(outBox, 1), 1f, Scale(Palette.NeonMagenta, pulse));
            DrawIcon(outBox, output.Kind);
            DrawCount(outBox, output.Count);
        }
    }

    // --- The take-apart bench -------------------------------------------------
    // One thing in at the top, and however many parts it is made of coming down out of it.
    // The arrows are the whole point: they appear the moment something breakable lands on the
    // bench, one per part, so a player can see what a battery is worth before spending it —
    // and the last of them is a "?" when the part under it is a gamble rather than a promise.

    private static void DrawTeardown(Inventory inv, float elapsed)
    {
        PixelFont.DrawCentered("TAKE APART", InventoryLayout.BenchRight,
            InventoryLayout.BenchLabelY, 1, Scale(Ink, 0.75f));

        ItemStack bench = inv.Break[0];
        int arrows = bench.IsEmpty ? 0 : Crafting.PartCount(bench.Kind);

        Rectangle inBox = InventoryLayout.BreakSlot;
        DrawSlot(inBox, bench);
        if (arrows > 0)
        {
            // The same "ready" pulse the assembly output wears, so both benches say they are
            // waiting on the player in the same language.
            float pulse = 0.6f + 0.4f * MathF.Sin(elapsed * 5f);
            Raylib.DrawRectangleLinesEx(Grow(inBox, 1), 1f, Scale(Palette.NeonMagenta, pulse));
        }

        var from = new Vector2(inBox.X + inBox.Width / 2f, inBox.Y + inBox.Height);
        for (int i = 0; i < Inventory.PartCount; i++)
        {
            bool live = i < arrows;
            ItemStack held = inv.Parts[i];
            // A slot is drawn while its arrow exists, and stays drawn while it still holds
            // something — so parts left behind by the last teardown are never hidden by
            // clearing the bench.
            if (!live && held.IsEmpty) continue;

            Rectangle box = InventoryLayout.Part(i);
            if (live) DrawArrow(from, new Vector2(box.X + box.Width / 2f, box.Y),
                                Scale(Palette.BatteryCore, held.IsEmpty ? 0.55f : 0.9f));
            DrawSlot(box, held);

            // Nothing there yet: show what this arrow is going to give, ghosted — or a "?"
            // for the one that might give nothing at all.
            if (!live || !held.IsEmpty) continue;
            if (Crafting.IsUncertain(bench.Kind, i))
            {
                PixelFont.DrawCentered("?", (int)(box.X + box.Width / 2f),
                    (int)(box.Y + (box.Height - PixelFont.GlyphH) / 2f), 1, Scale(Ink, 0.7f));
                continue;
            }
            ItemStack promise = Crafting.Preview(bench.Kind, i);
            if (!promise.IsEmpty) DrawIcon(box, promise.Kind, Ghost);
        }
    }

    /// <summary>What the uncertain arrow might give, spelled out — "LEAD / ZINC / LITHIUM?".
    /// Read off the teardown table rather than typed, so a table that grows a fourth metal
    /// says so here without anybody remembering to come and edit a string.</summary>
    private static string MaybeNames(ItemKind kind)
    {
        Crafting.Teardown? t = Crafting.Of(kind);
        if (t is null || t.OneOf.Length == 0) return "?";
        var names = new string[t.OneOf.Length];
        for (int i = 0; i < names.Length; i++) names[i] = ItemNames.Of(t.OneOf[i]);
        return string.Join(" / ", names) + "?";
    }

    /// <summary>A thin line with a two-pixel head at the far end — the "this becomes that"
    /// mark, drawn at the internal resolution so it stays as chunky as everything else.</summary>
    private static void DrawArrow(Vector2 from, Vector2 to, Color col)
    {
        Raylib.DrawLineV(from, to, col);
        Vector2 dir = to - from;
        float len = dir.Length();
        if (len < 1f) return;
        dir /= len;
        var side = new Vector2(-dir.Y, dir.X);
        Vector2 back = to - dir * 3f;
        Raylib.DrawLineV(to, back + side * 2f, col);
        Raylib.DrawLineV(to, back - side * 2f, col);
    }

    // --- The pointer ----------------------------------------------------------
    // The operating system's cursor is hidden for the whole game, everywhere, and this
    // is what replaces it — but only here, because this panel is the only screen the
    // mouse actually drives. Drawn into the 320×240 target like everything else, so it
    // is blown up by the same nearest-neighbour scale as the world and comes out as fat
    // hard pixels instead of a crisp modern arrow sitting on top of a retro picture.
    //
    // 'F' is the dark border, 'B' the light body: an arrow needs both or it disappears
    // against the pale slot wells it spends all its time over.

    private static readonly string[] CursorGlyph =
    {
        "F.......",
        "FF......",
        "FBF.....",
        "FBBF....",
        "FBBBF...",
        "FBBBBF..",
        "FBBBBBF.",
        "FBBBBBBF",
        "FBBBFFFF",
        "FBFBF...",
        "FF.FBF..",
        "....FF..",
    };

    private static void DrawPixelCursor(Vector2 at)
    {
        int x0 = (int)MathF.Round(at.X);
        int y0 = (int)MathF.Round(at.Y);
        var border = new Color(4, 6, 9, 235);

        for (int row = 0; row < CursorGlyph.Length; row++)
        {
            string line = CursorGlyph[row];
            for (int col = 0; col < line.Length; col++)
            {
                if (line[col] == '.') continue;
                Raylib.DrawRectangle(x0 + col, y0 + row, 1, 1,
                    line[col] == 'F' ? border : Color.White);
            }
        }
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
    private static void DrawIcon(Rectangle box, ItemKind kind, Color? tint = null)
    {
        if (_icons is null) return;
        var inner = Grow(box, -1);
        var src = new Rectangle(0, 0, ItemIconRenderer.Size, -ItemIconRenderer.Size);
        Raylib.DrawTexturePro(_icons.Texture(kind), src, inner, Vector2.Zero, 0f,
            tint ?? Color.White);
    }

    /// <summary>The wash a promised-but-not-yet-taken part is drawn under: present enough to
    /// name the thing, faint enough that nobody mistakes it for something they own.</summary>
    private static readonly Color Ghost = new(255, 255, 255, 105);

    private static void DrawCount(Rectangle box, int count)
    {
        if (count <= 1) return;
        string s = count.ToString();
        int w = PixelFont.MeasureSmall(s) - 1;   // no trailing gap after the last digit
        // Bottom-right, in the compact digits, with a dark plate behind so it reads over any
        // icon colour. Small on purpose: a slot is 18 pixels and the item in it is the thing
        // worth looking at — the count is a footnote, and at the full face it was covering a
        // quarter of what it was counting.
        int tx = (int)(box.X + box.Width) - w - 2;
        int ty = (int)(box.Y + box.Height) - PixelFont.SmallH - 2;
        Raylib.DrawRectangle(tx - 1, ty - 1, w + 2, PixelFont.SmallH + 2, new Color(0, 0, 0, 190));
        PixelFont.DrawSmall(s, tx, ty, Color.White);
    }

    // --- The hover label ------------------------------------------------------
    // Everything in the pack is a small turning polygon and several of them are grey lumps.
    // The silhouettes are doing as much as silhouettes can at this size, and the honest fix
    // for the rest is to say the name: point at a thing and it tells you what it is and how
    // many of it you have.

    private static void DrawHover(Inventory inv, InventoryScreen screen)
    {
        // Nothing while a stack is riding the cursor — the cursor is already carrying the
        // answer, and a label under a dragged item just obscures where it is going.
        if (!screen.Held.IsEmpty) return;

        var (region, index) = InventoryLayout.Locate(screen.Cursor);
        ItemStack under = region switch
        {
            InvRegion.Output => inv.CraftOutput(),
            InvRegion.None   => ItemStack.Empty,
            _                => Inventory.Addressable(region, index)
                                    ? inv.SlotRef(region, index) : ItemStack.Empty,
        };

        // An empty slot under one of the bench's arrows names what that arrow is going to
        // give, so the promise can be read rather than guessed at from a 16-pixel ghost.
        string name;
        if (under.IsEmpty && region == InvRegion.Parts && !inv.Break[0].IsEmpty)
        {
            ItemKind on = inv.Break[0].Kind;
            if (index >= Crafting.PartCount(on)) return;
            name = Crafting.IsUncertain(on, index)
                ? MaybeNames(on)
                : ItemNames.Of(Crafting.Preview(on, index).Kind);
        }
        else if (under.IsEmpty) return;
        else name = ItemNames.Of(under.Kind);

        int nameW = PixelFont.Measure(name, 1) - 1;
        string count = under.IsEmpty ? "" : under.Count.ToString();
        int countW = count.Length == 0 ? 0 : PixelFont.MeasureSmall(count) + 3;
        int w = nameW + countW + 6;
        const int h = PixelFont.GlyphH + 4;

        // Up and to the right of the pointer, then folded back inside the panel so a label
        // on the last column or the bottom row is never half off the screen.
        int x = (int)screen.Cursor.X + 6;
        int y = (int)screen.Cursor.Y - h - 2;
        x = Math.Clamp(x, 1, W - w - 1);
        y = Math.Clamp(y, 1, H - h - 1);

        Raylib.DrawRectangle(x, y, w, h, new Color(4, 8, 12, 230));
        Raylib.DrawRectangleLines(x, y, w, h, Scale(SlotEdge, 0.9f));
        PixelFont.Draw(name, x + 3, y + 2, 1, Ink);
        if (count.Length > 0)
            PixelFont.DrawSmall(count, x + 3 + nameW + 3, y + 2 + (PixelFont.GlyphH - PixelFont.SmallH),
                Scale(Ink, 0.75f));
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
