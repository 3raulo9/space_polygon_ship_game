using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.UI;

namespace VoidTanks.Rendering;

/// <summary>
/// Draws the hangar flat over the low-res target, after the 3D pass has already put
/// the player's chassis turning in the middle of the frame. Three panels — the roster
/// down the left, the point budget down the right, the build's own description across
/// the bottom — with the model showing through the gap between them, and the paint bay
/// replacing the lot when it's open.
///
/// Everything is measured in the internal 320×240 space and set in the panel's crisp
/// 5×7 bitmap font, which is the only thing legible at this density; the heading keeps
/// the default font so it matches the other sub-screens' flicker.
/// </summary>
internal static class ClassSelectRenderer
{
    private const int W = Config.InternalWidth;   // 320
    private const int H = Config.InternalHeight;  // 240

    // The two side panels. The band between them is left clear for the turntable.
    private const int LeftX = 5, LeftW = 72;
    private const int RightX = 214, RightW = 101;
    private const int PanelY = 30, PanelH = 104;

    public static void Draw(ClassSelectScreen screen, float elapsed)
    {
        if (screen.Customising) { DrawPaintBay(screen, elapsed); return; }

        DrawHeading("HANGAR", elapsed);
        DrawRoster(screen);
        DrawBudget(screen);
        DrawBriefing(screen);
        DrawActions(screen);
        DrawFooter(screen.Focus switch
        {
            ClassSelectScreen.Pane.Classes => "UP DN PICK - TAB PANE - ENTER GO - ESC BACK",
            ClassSelectScreen.Pane.Stats => "UP DN TRACK - LEFT RIGHT SPEND - TAB PANE",
            _ => "LEFT RIGHT CHOOSE - ENTER CONFIRM - TAB PANE",
        });
    }

    // --- Left: the chassis roster ---------------------------------------------

    private const int RowStep = 12;
    private const int RosterTop = 48;

    private static void DrawRoster(ClassSelectScreen screen)
    {
        bool focused = screen.Focus == ClassSelectScreen.Pane.Classes;
        Panel(LeftX, PanelY, LeftW, PanelH, focused);
        PixelFont.Draw("CHASSIS", LeftX + 5, PanelY + 5, 1, Scale(Palette.GridNear, 0.9f));

        for (int i = 0; i < ClassCatalog.All.Count; i++)
        {
            var arch = ClassCatalog.All[i];
            bool selected = screen.ClassIndex == i;
            int y = RosterTop + i * RowStep;

            // An offline chassis is nearly swallowed by the dark, exactly as the menu's
            // dead MULTIPLAYER entry is — it can be looked at and read about, not driven.
            Color c = arch.Available
                ? (selected ? Palette.HudChrome : Scale(Palette.HudChrome, 0.5f))
                : (selected ? Scale(Palette.Warning, 1.6f) : Scale(Palette.HudChrome, 0.22f));

            PixelFont.Draw(arch.Name, LeftX + 11, y, 1, c);
            if (selected)
                PixelFont.Draw(">", LeftX + 4, y, 1,
                    focused ? Palette.HudChrome : Scale(Palette.HudChrome, 0.45f));
        }
    }

    // --- Right: the point budget ----------------------------------------------

    private const int StatTop = 46;
    private const int StatStep = 22;
    private const int PipW = 8, PipH = 4, PipGap = 1;

    private static void DrawBudget(ClassSelectScreen screen)
    {
        bool focused = screen.Focus == ClassSelectScreen.Pane.Stats;
        Panel(RightX, PanelY, RightW, PanelH, focused);
        PixelFont.Draw("BUILD", RightX + 5, PanelY + 5, 1, Scale(Palette.GridNear, 0.9f));

        Loadout lo = screen.Loadout;
        DrawStat(screen, focused, Loadout.Stat.Shield, "SHIELD", StatTop, Palette.HudChrome);
        DrawStat(screen, focused, Loadout.Stat.Speed, "SPEED", StatTop + StatStep, Palette.GridNear);
        DrawStat(screen, focused, Loadout.Stat.Ammo, "AMMO", StatTop + StatStep * 2, Palette.Flag);

        // What's left on the table. The whole rule of the budget is visible here: max
        // one track and this number tells you, immediately, that the other two are
        // going to be poor.
        int left = lo.Remaining;
        Color c = left > 0 ? Palette.HudChrome : Scale(Palette.HudChrome, 0.4f);
        PixelFont.Draw("POINTS", RightX + 5, PanelY + PanelH - 12, 1, Scale(Palette.HudChrome, 0.55f));
        string n = left.ToString();
        PixelFont.Draw(n, RightX + RightW - 6 - PixelFont.Measure(n, 1), PanelY + PanelH - 12, 1, c);
    }

    private static void DrawStat(ClassSelectScreen screen, bool paneFocused,
        Loadout.Stat stat, string label, int y, Color fill)
    {
        bool selected = screen.StatRow == stat && paneFocused;
        int value = screen.Loadout[stat];

        PixelFont.Draw(label, RightX + 11, y, 1,
            selected ? Palette.HudChrome : Scale(Palette.HudChrome, 0.55f));
        if (selected) PixelFont.Draw(">", RightX + 4, y, 1, Palette.HudChrome);

        string n = value.ToString();
        PixelFont.Draw(n, RightX + RightW - 6 - PixelFont.Measure(n, 1), y, 1,
            selected ? Palette.HudChrome : Scale(Palette.HudChrome, 0.7f));

        // Ten pips, filled to the track's value. Pips past what the remaining budget
        // could reach are drawn darker still, so the ceiling this build has already
        // spent itself into is visible without doing arithmetic.
        int reach = Math.Min(Loadout.StatMax, value + screen.Loadout.Remaining);
        int py = y + 9;
        for (int i = 0; i < Loadout.StatMax; i++)
        {
            int px = RightX + 5 + i * (PipW + PipGap);
            Color c = i < value ? fill
                    : i < reach ? Scale(Palette.HudChrome, 0.22f)
                    : Scale(Palette.HudChrome, 0.08f);
            Raylib.DrawRectangle(px, py, PipW, PipH, c);
        }
    }

    // --- Bottom: what this chassis actually is --------------------------------

    private const int BriefTop = 142;

    private static void DrawBriefing(ClassSelectScreen screen)
    {
        var arch = screen.Current;
        // Full width and near-opaque. The grid behind this band is half lit squares and
        // half dark ones, and at any lighter setting the same line of text sits on two
        // different backgrounds and reads as two different brightnesses.
        Raylib.DrawRectangle(0, BriefTop - 5, W, 49, new Color(5, 7, 10, 225));

        PixelFont.Draw(arch.Tagline, LeftX + 5, BriefTop, 1,
            arch.Available ? Scale(Palette.GridNear, 0.9f) : Scale(Palette.Warning, 1.5f));

        for (int i = 0; i < arch.Lines.Length; i++)
            PixelFont.Draw(arch.Lines[i], LeftX + 5, BriefTop + 11 + i * 8, 1,
                Scale(Palette.HudChrome, 0.68f));
    }

    // --- The action row --------------------------------------------------------

    private const int ActionY = 196;

    private static void DrawActions(ClassSelectScreen screen)
    {
        bool focused = screen.Focus == ClassSelectScreen.Pane.Actions;
        // A dark band under the row and the hint below it. Without this the buttons sit
        // straight on the lit grid squares, and the same grey reads bright over a dark
        // cell and nearly invisible over a lit one — the label's legibility ends up
        // depending on where the turntable's floor happens to be this frame.
        Raylib.DrawRectangle(0, ActionY - 8, W, H - ActionY + 8, new Color(5, 7, 10, 215));
        DrawAction(screen, focused, ClassSelectScreen.Act.Customise, "CUSTOMISE",
            W / 2 - 56, screen.CanCustomise);
        DrawAction(screen, focused, ClassSelectScreen.Act.Launch, "LAUNCH",
            W / 2 + 52, screen.CanLaunch);
    }

    private static void DrawAction(ClassSelectScreen screen, bool paneFocused,
        ClassSelectScreen.Act act, string label, int cx, bool enabled)
    {
        bool selected = screen.ActionRow == act && paneFocused;
        int w = PixelFont.Measure(label, 1);
        int x = cx - w / 2;

        Color c = enabled
            ? (selected ? Palette.HudChrome : Scale(Palette.HudChrome, 0.5f))
            : Scale(Palette.HudChrome, 0.2f);

        if (selected)
        {
            Raylib.DrawRectangle(x - 7, ActionY - 4, w + 14, 15,
                new Color(10, 20, 24, 220));
            Raylib.DrawRectangleLines(x - 7, ActionY - 4, w + 14, 15,
                Scale(enabled ? Palette.HudChrome : Palette.Warning, 0.8f));
        }
        PixelFont.Draw(label, x, ActionY, 1, c);
    }

    // --- The paint bay ---------------------------------------------------------
    // Its own screen, per the brief: the model keeps turning (the 3D pass behind this
    // repaints it live as swatches are cycled) and every part of it is listed with the
    // colour it currently wears, so a change is visible on the model the same frame it
    // is visible in the list.

    private const int PaintX = 186, PaintW = 129;
    private const int PaintTop = 44, PaintStep = 20;

    private static void DrawPaintBay(ClassSelectScreen screen, float elapsed)
    {
        DrawHeading("PAINT", elapsed);

        var arch = screen.Current;
        // Sized to its contents rather than to a fixed box: chassis have different part
        // counts, and a panel with a hand's depth of empty space under BACK reads as a
        // list that failed to load rather than as a short list.
        int panelH = 26 + arch.PartNames.Length * PaintStep + 32;
        Raylib.DrawRectangle(PaintX, PaintTop - 10, PaintW, panelH, new Color(5, 7, 10, 210));
        Raylib.DrawRectangleLines(PaintX, PaintTop - 10, PaintW, panelH, Scale(Palette.HudChrome, 0.9f));
        PixelFont.Draw(arch.Name, PaintX + 6, PaintTop - 6, 1, Scale(Palette.GridNear, 0.9f));

        for (int i = 0; i < arch.PartNames.Length; i++)
        {
            bool selected = screen.PaintRow == i;
            int y = PaintTop + 10 + i * PaintStep;
            int swatch = screen.Loadout.SwatchIndex(arch.Kind, i);

            PixelFont.Draw(arch.PartNames[i], PaintX + 12, y, 1,
                selected ? Palette.HudChrome : Scale(Palette.HudChrome, 0.5f));
            if (selected) PixelFont.Draw(">", PaintX + 5, y, 1, Palette.HudChrome);

            // The chip itself, framed, with the colour's name beside it. Arrow brackets
            // only on the focused row — the same "this one is live" grammar the settings
            // screen uses for a cyclable value.
            const int chipW = 26, chipH = 7;
            int chipX = PaintX + PaintW - 6 - chipW;
            Raylib.DrawRectangle(chipX, y - 1, chipW, chipH, ClassCatalog.SwatchColor(swatch));
            Raylib.DrawRectangleLines(chipX - 1, y - 2, chipW + 2, chipH + 2,
                Scale(Palette.HudChrome, selected ? 0.85f : 0.35f));

            string name = ClassCatalog.SwatchName(swatch);
            string shown = selected ? "<" + name + ">" : name;
            PixelFont.Draw(shown, PaintX + 12, y + 9, 1,
                selected ? Scale(Palette.HudChrome, 0.85f) : Scale(Palette.HudChrome, 0.4f));
        }

        DrawPaintButton(screen, screen.ResetRow, "RESET", PaintTop + 16 + arch.PartNames.Length * PaintStep);
        DrawPaintButton(screen, screen.BackRow, "BACK", PaintTop + 32 + arch.PartNames.Length * PaintStep);

        DrawFooter("UP DN PART - LEFT RIGHT COLOUR - ESC BACK");
    }

    private static void DrawPaintButton(ClassSelectScreen screen, int row, string label, int y)
    {
        bool selected = screen.PaintRow == row;
        PixelFont.Draw(label, PaintX + 12, y, 1,
            selected ? Palette.HudChrome : Scale(Palette.HudChrome, 0.5f));
        if (selected) PixelFont.Draw(">", PaintX + 5, y, 1, Palette.HudChrome);
    }

    // --- shared chrome ---------------------------------------------------------

    /// <summary>A dark backing box for a panel, brightened at the border when the
    /// arrows are currently driving it — the only cue that says which pane has focus.</summary>
    private static void Panel(int x, int y, int w, int h, bool focused)
    {
        Raylib.DrawRectangle(x, y, w, h, new Color(5, 7, 10, 200));
        // Both borders are the same chrome, only the brightness differs. Using a
        // different *hue* for the unfocused state (the grid's teal was the obvious
        // choice) backfires: teal on this backdrop reads as more alive than dimmed
        // chrome does, so the pane nobody is driving ends up looking like the one that
        // is. Focus has to be told with brightness alone.
        Raylib.DrawRectangleLines(x, y, w, h,
            Scale(Palette.HudChrome, focused ? 0.95f : 0.3f));
    }

    /// <summary>The same slow, uneven breathe the other sub-screen headings carry.</summary>
    private static void DrawHeading(string text, float elapsed)
    {
        Font font = Raylib.GetFontDefault();
        const int size = 20;
        Vector2 m = Raylib.MeasureTextEx(font, text, size, 1);
        float x = (W - m.X) * 0.5f;
        const int y = 6;

        float breathe = 0.78f + 0.22f * MathF.Abs(MathF.Sin(elapsed * 0.9f));
        Raylib.DrawTextEx(font, text, new Vector2(x, y), size, 1, Scale(Palette.HudChrome, breathe));

        int ruleW = (int)m.X;
        Color rule = Scale(Palette.GridFar, 0.5f + 0.2f * MathF.Sin(elapsed * 1.7f));
        Raylib.DrawRectangle((W - ruleW) / 2, y + size + 3, ruleW, 1, rule);
    }

    private static void DrawFooter(string hint)
    {
        int w = PixelFont.Measure(hint, 1);
        PixelFont.Draw(hint, (W - w) / 2, H - 14, 1, Scale(Palette.HudChrome, 0.4f));
    }

    private static Color Scale(Color c, float t)
    {
        t = Math.Clamp(t, 0f, 2f);
        return new Color(
            (int)Math.Min(255f, c.R * t),
            (int)Math.Min(255f, c.G * t),
            (int)Math.Min(255f, c.B * t), c.A);
    }
}
