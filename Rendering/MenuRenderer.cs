using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Input;
using Unrendered.UI;

namespace Unrendered.Rendering;

/// <summary>
/// Draws the menu as flat 2D over the low-res target. Everything is measured in
/// the internal 320x240 space so it upscales with the world and stays chunky.
/// The title admits the frame never finished (Doc 01): it flickers and drops
/// characters as if the machine can't hold the image. No warmth, no polish —
/// cold chrome text on the void.
/// </summary>
internal static class MenuRenderer
{
    private const int W = Config.InternalWidth;   // 320
    private const int H = Config.InternalHeight;  // 240

    private const int TitleSize = 30;
    private const int ItemSize = 14;
    private const int Spacing = 1; // raylib default text spacing (per-size scaled)

    private const string Title = "UNRENDERED";

    public static void Draw(Menu menu, float elapsed)
    {
        DrawTitle(elapsed);
        DrawItems(menu);
        DrawFooter(elapsed);
    }

    /// <summary>Draws whichever settings page is up. <paramref name="alpha"/> fades the
    /// whole thing, so the pause panel can bring it in over a live match.</summary>
    public static void DrawSettings(SettingsScreen screen, float elapsed, byte alpha = 255)
    {
        switch (screen.At)
        {
            case SettingsScreen.Page.Controls: DrawControls(screen, elapsed, alpha); break;
            case SettingsScreen.Page.Audio: DrawAudio(screen, elapsed, alpha); break;
            default: DrawSettingsRoot(screen, elapsed, alpha); break;
        }
    }

    // --- Title: flickering, occasionally-dropped glyphs ---
    private static void DrawTitle(float elapsed)
    {
        Font font = Raylib.GetFontDefault();
        Vector2 full = Raylib.MeasureTextEx(font, Title, TitleSize, Spacing);
        float glyphAdvance = full.X / Title.Length;
        float x = (W - full.X) * 0.5f;
        const int y = 44;

        // The whole title dims and lifts on a slow, uneven pulse — the render
        // straining to stay lit rather than a clean fade.
        float breathe = 0.72f + 0.28f * MathF.Abs(MathF.Sin(elapsed * 0.9f));

        for (int i = 0; i < Title.Length; i++)
        {
            // Per-glyph deterministic flicker: a character occasionally drops out
            // or jitters, as if the scanline couldn't resolve it this frame. Kept
            // rare so the word stays legible — a flicker, not a permanent gap.
            float n = Hash(i * 12.9898f + MathF.Floor(elapsed * 10f) * 7.13f);
            bool dropped = n > 0.975f;             // rare full dropout
            if (dropped) continue;

            bool unstable = n > 0.9f;
            float jitter = unstable ? (Hash(i + elapsed) - 0.5f) * 2f : 0f;
            float bright = breathe * (unstable ? 0.65f : 1f);

            Color c = Scale(Palette.HudChrome, bright);
            // A faint cold ghost behind the glyph — the phosphor not quite letting go.
            Color ghost = Scale(Palette.GridFar, bright * 0.5f);
            Raylib.DrawTextEx(font, Title[i].ToString(),
                new Vector2(x + i * glyphAdvance + 1f, y + jitter + 1f), TitleSize, Spacing, ghost);
            Raylib.DrawTextEx(font, Title[i].ToString(),
                new Vector2(x + i * glyphAdvance, y + jitter), TitleSize, Spacing, c);
        }

        // A thin chrome rule under the title, itself faintly unstable.
        int ruleW = (int)full.X;
        int rx = (W - ruleW) / 2;
        Color rule = Scale(Palette.GridFar, 0.5f + 0.2f * MathF.Sin(elapsed * 1.7f));
        Raylib.DrawRectangle(rx, y + TitleSize + 6, ruleW, 1, rule);
    }

    // --- Menu items ---
    private static void DrawItems(Menu menu)
    {
        Font font = Raylib.GetFontDefault();
        int y = 116;
        const int step = 24;

        DrawItem(font, "DESCENT", Menu.Item.Descent, menu, y, null);
        DrawItem(font, "SANDBOX", Menu.Item.Sandbox, menu, y + step, null);
        DrawItem(font, "MULTIPLAYER", Menu.Item.Multiplayer, menu, y + step * 2, null);
        DrawItem(font, "SETTINGS", Menu.Item.Settings, menu, y + step * 3, null);
    }

    private static void DrawItem(Font font, string label, Menu.Item item, Menu menu, int y, string? tag)
    {
        bool selectable = Menu.IsSelectable(item);
        bool selected = menu.Selected == item;

        Vector2 size = Raylib.MeasureTextEx(font, label, ItemSize, Spacing);
        float x = (W - size.X) * 0.5f;

        Color color = selectable
            ? (selected ? Palette.HudChrome : Scale(Palette.HudChrome, 0.55f))
            : Scale(Palette.HudChrome, 0.22f); // multiplayer: nearly swallowed by the dark

        Raylib.DrawTextEx(font, label, new Vector2(x, y), ItemSize, Spacing, color);

        // Bracket cursor flanks the current choice — a targeting reticle, not a highlight.
        if (selected && selectable)
        {
            Raylib.DrawTextEx(font, "[", new Vector2(x - 14, y), ItemSize, Spacing, Palette.HudChrome);
            Raylib.DrawTextEx(font, "]", new Vector2(x + size.X + 6, y), ItemSize, Spacing, Palette.HudChrome);
        }

        // Unavailable tag, small and dim, sitting under the dead option.
        if (tag != null)
        {
            const int tagSize = 8;
            Vector2 ts = Raylib.MeasureTextEx(font, tag, tagSize, Spacing);
            Raylib.DrawTextEx(font, tag, new Vector2((W - ts.X) * 0.5f, y + ItemSize + 1),
                tagSize, Spacing, Scale(Palette.Warning, 0.9f));
        }
    }

    // --- Footer hint (never mentions the secret keybind) ---
    private static void DrawFooter(float elapsed)
        => DrawFooterHint("ARROWS / W S · ENTER SELECT");

    private static void DrawFooterHint(string hint) => DrawFooterHint(hint, 255);

    /// <summary>
    /// <paramref name="size"/> defaults to the 8px the title screen has always used, and the
    /// settings pages ask for 10 instead.
    ///
    /// <para>Raylib's default font is a 10-pixel bitmap, so a requested 8 is a 0.8 downscale
    /// of a bitmap with nearest-neighbour filtering — whole rows of pixels are dropped and the
    /// glyphs come apart. On the title menu that reads as the degraded-terminal look the whole
    /// game is built on. On a page whose hint is the only place the rebinding keys are written
    /// down, it reads as a bug.</para>
    /// </summary>
    private static void DrawFooterHint(string hint, byte alpha, Color? tint = null, int size = 8)
    {
        Font font = Raylib.GetFontDefault();
        Vector2 s = Raylib.MeasureTextEx(font, hint, size, Spacing);
        // Barely there — a prompt left glowing at the bottom of a dead terminal. Except when
        // it is carrying a warning, which is allowed to be legible.
        Color c = Fade(tint ?? Scale(Palette.HudChrome, 0.35f), alpha);
        Raylib.DrawTextEx(font, hint, new Vector2((W - s.X) * 0.5f, H - 18), size, Spacing, c);
    }

    // --- Pause panel ---

    /// <summary>
    /// Draws the pause panel over the dimmed world. <paramref name="t"/> (0..1) is how far
    /// the panel has come in, which is also how far the world behind it has been dimmed; the
    /// text arrives a little after the dim starts so the two do not fight.
    ///
    /// <para>Identical in single player and multiplayer, down to the last row's wording. The
    /// world behind it is frozen in one and still moving in the other, and that is the only
    /// difference the player is meant to notice.</para>
    /// </summary>
    public static void DrawPause(UI.PauseMenu menu, float elapsed, float t)
    {
        float a = Math.Clamp((t - 0.2f) / 0.5f, 0f, 1f);
        if (a <= 0f) return;
        byte alpha = (byte)(a * 255);

        Font font = Raylib.GetFontDefault();

        // Heading — the same slow breathe as the other sub-screen titles.
        const string head = "PAUSED";
        const int hs = 28;
        Vector2 hm = Raylib.MeasureTextEx(font, head, hs, Spacing);
        float breathe = 0.8f + 0.2f * MathF.Abs(MathF.Sin(elapsed * 0.9f));
        Raylib.DrawTextEx(font, head, new Vector2((W - hm.X) * 0.5f, 66), hs, Spacing,
            Fade(Scale(Palette.HudChrome, breathe), alpha));

        int ruleW = (int)hm.X;
        Raylib.DrawRectangle((W - ruleW) / 2, 66 + hs + 6, ruleW, 1,
            Fade(Scale(Palette.GridFar, 0.6f), alpha));

        int y = 122;
        foreach (var item in System.Enum.GetValues<UI.PauseMenu.Item>())
        {
            DrawCentredRow(font, UI.PauseMenu.Label(item), menu.Selected == item, y, alpha);
            y += 22;
        }

        // Ten rather than the title screen's eight. The degraded, half-resolved look belongs
        // to UNRENDERED itself; a panel a player opens mid-fight to find a control is a tool,
        // and its one line of instructions has to be readable. See DrawFooterHint.
        DrawFooterHint("ESC / ENTER RESUME", alpha, null, 10);
    }

    // --- Settings: the front page ---

    private static void DrawSettingsRoot(SettingsScreen screen, float elapsed, byte alpha)
    {
        DrawHeading("SETTINGS", elapsed, alpha);

        Font font = Raylib.GetFontDefault();
        int y = 116;
        const int step = 24;

        foreach (var row in System.Enum.GetValues<SettingsScreen.RootRow>())
        {
            DrawCentredRow(font, SettingsScreen.RootLabel(row), screen.Root == row, y, alpha);
            y += step;
        }

        DrawFooterHint("UP DN MOVE · ENTER SELECT · ESC BACK", alpha);
    }

    // --- Settings: the mixer ---

    private static void DrawAudio(SettingsScreen screen, float elapsed, byte alpha)
    {
        DrawPanel(alpha);
        DrawHeading("SOUND", elapsed, alpha);

        Font font = Raylib.GetFontDefault();
        const int y = 74;
        const int step = 15;

        // Walked rather than listed, so adding a fader to the screen adds it here too.
        int at = 0;
        foreach (var row in System.Enum.GetValues<SettingsScreen.AudioRow>())
        {
            if (row == SettingsScreen.AudioRow.Back) continue;
            DrawValueRow(font, SettingsScreen.AudioLabel(row), screen.AudioValue(row),
                screen.Audio == row, y + step * at, alpha);
            at++;
        }
        DrawCentredRow(font, "BACK", screen.Audio == SettingsScreen.AudioRow.Back,
            y + step * at + 4, alpha);

        DrawFooterHint("< > CHANGE · UP DN MOVE · ESC BACK", alpha, null, 10);
    }

    // --- Settings: the bindings ---

    /// <summary>
    /// The controls list. Long enough that it scrolls, so the window is walked from the
    /// screen's own scroll offset and section headings are drawn as they are passed rather
    /// than laid out up front — a heading only earns a line when the first row under it is
    /// actually visible.
    /// </summary>
    private static void DrawControls(SettingsScreen screen, float elapsed, byte alpha)
    {
        DrawPanel(alpha);
        DrawHeading("CONTROLS", elapsed, alpha);

        Font font = Raylib.GetFontDefault();
        const int top = SettingsScreen.ListTop;
        const int step = SettingsScreen.RowHeight;
        const int headStep = SettingsScreen.HeadingHeight;

        var all = InputActions.All;
        int y = top;
        InputSection? drawn = null;

        // How many rows fit is the screen's answer, not this method's — the cursor has to
        // stay inside the same window the draw uses, so only one of them may decide it.
        int last = Math.Min(all.Length,
            screen.ControlScroll + SettingsScreen.RowsFrom(screen.ControlScroll));
        for (int i = screen.ControlScroll; i < last; i++)
        {
            var action = all[i];
            var section = InputActions.SectionOf(action);
            if (drawn != section)
            {
                drawn = section;
                DrawSectionHead(font, section, y, alpha);
                y += headStep;
            }

            DrawBindRow(font, screen, action, i == screen.ControlRow, y, alpha);
            y += step;
        }

        // A hint that the list continues, at whichever end it does. Without it the page
        // reads as if it holds eight bindings. Parked at fixed heights rather than against
        // the last row drawn, which moves with the sections and would otherwise put the
        // bottom tick into the RESET row on some scroll positions and not others.
        if (screen.ControlScroll > 0) DrawTick(font, "^", top - 11, alpha);
        if (last < all.Length) DrawTick(font, "v", SettingsScreen.ListBottom - 6, alpha);

        // RESET and BACK live under the window rather than in it: they are not bindings, and
        // scrolling past the end of the list to reach the way out is a bad way to leave.
        DrawCentredRow(font, "RESET TO DEFAULTS",
            screen.ControlRow == screen.ControlResetRow, 196, alpha, small: true);
        DrawCentredRow(font, "BACK", screen.ControlRow == screen.ControlBackRow, 208, alpha,
            small: true);

        if (screen.Capturing) DrawCapturePrompt(font, screen, alpha);
        else if (screen.LastClash is { } clash)
            DrawFooterHint("ALSO BOUND TO " + InputActions.Label(clash), alpha,
                Palette.Warning, 10);
        else
            DrawFooterHint("ENTER BIND · < > SLOT · DEL CLEAR · ESC BACK", alpha, null, 10);
    }

    private static void DrawSectionHead(Font font, InputSection section, int y, byte alpha)
    {
        // Ten, not eight — the font's own size. See DrawFooterHint.
        const int size = 10;
        string label = InputActions.SectionLabel(section);
        string? note = InputActions.SectionNote(section);

        Raylib.DrawTextEx(font, label, new Vector2(ColLeft, y), size, Spacing,
            Fade(Palette.GridNear, alpha));

        if (note != null)
        {
            Vector2 m = Raylib.MeasureTextEx(font, note, size, Spacing);
            Raylib.DrawTextEx(font, note, new Vector2(ColRight - m.X, y), size, Spacing,
                Fade(Scale(Palette.HudChrome, 0.36f), alpha));
        }

        Raylib.DrawRectangle(ColLeft, y + size + 2, ColRight - ColLeft, 1,
            Fade(Scale(Palette.GridFar, 0.5f), alpha));
    }

    /// <summary>
    /// One binding: its name on the left, its two slots on the right. The focused slot wears
    /// the brackets, so the cursor says which of the two an ENTER is about to overwrite.
    /// Both slots go warning-red when the action is sharing a button with a neighbour in its
    /// own section.
    /// </summary>
    private static void DrawBindRow(Font font, SettingsScreen screen, InputAction action,
        bool selected, int y, byte alpha)
    {
        const int size = 10;
        var bind = screen.Settings.Bindings[action];
        bool clash = screen.IsClashing(action);

        Color label = selected ? Palette.HudChrome : Scale(Palette.HudChrome, 0.5f);
        Raylib.DrawTextEx(font, InputActions.Label(action), new Vector2(ColLeft, y), size,
            Spacing, Fade(label, alpha));

        // Two fixed columns, so the slots line up down the page and an empty one reads as a
        // gap in a column rather than as a shorter row.
        DrawSlot(font, bind.Primary.Label, selected && screen.ControlSlot == 0, clash,
            SlotOneX, y, size, alpha);
        DrawSlot(font, bind.Secondary.Label, selected && screen.ControlSlot == 1, clash,
            SlotTwoX, y, size, alpha);
    }

    private static void DrawSlot(Font font, string text, bool focused, bool clash, int x,
        int y, int size, byte alpha)
    {
        Color c = clash
            ? (focused ? Palette.Warning : Scale(Palette.Warning, 0.65f))
            : (focused ? Palette.HudChrome : Scale(Palette.HudChrome, 0.42f));

        Raylib.DrawTextEx(font, text, new Vector2(x, y), size, Spacing, Fade(c, alpha));

        if (!focused) return;
        Vector2 m = Raylib.MeasureTextEx(font, text, size, Spacing);
        Color bracket = Fade(clash ? Palette.Warning : Palette.HudChrome, alpha);
        Raylib.DrawTextEx(font, "[", new Vector2(x - 8, y), size, Spacing, bracket);
        Raylib.DrawTextEx(font, "]", new Vector2(x + m.X + 3, y), size, Spacing, bracket);
    }

    /// <summary>The "press something" prompt. A band across the middle rather than a dialog:
    /// the list stays visible behind it, which is the only way to see what you are answering
    /// for.</summary>
    private static void DrawCapturePrompt(Font font, SettingsScreen screen, byte alpha)
    {
        const int bandY = 100;
        const int bandH = 34;
        Raylib.DrawRectangle(0, bandY, W, bandH, Fade(new Color(5, 7, 10, 225), alpha));
        Raylib.DrawRectangle(0, bandY, W, 1, Fade(Scale(Palette.GridNear, 0.8f), alpha));
        Raylib.DrawRectangle(0, bandY + bandH - 1, W, 1, Fade(Scale(Palette.GridNear, 0.8f), alpha));

        string what = screen.FocusedAction is { } a ? InputActions.Label(a) : "";
        string head = "PRESS A KEY FOR " + what;
        const int hs = 10;
        Vector2 hm = Raylib.MeasureTextEx(font, head, hs, Spacing);
        Raylib.DrawTextEx(font, head, new Vector2((W - hm.X) * 0.5f, bandY + 7), hs, Spacing,
            Fade(Palette.HudChrome, alpha));

        const string sub = "ESC TO CANCEL";
        const int ss = 8;
        Vector2 sm = Raylib.MeasureTextEx(font, sub, ss, Spacing);
        Raylib.DrawTextEx(font, sub, new Vector2((W - sm.X) * 0.5f, bandY + 21), ss, Spacing,
            Fade(Scale(Palette.HudChrome, 0.45f), alpha));
    }

    private static void DrawTick(Font font, string glyph, int y, byte alpha)
    {
        const int size = 8;
        Vector2 m = Raylib.MeasureTextEx(font, glyph, size, Spacing);
        Raylib.DrawTextEx(font, glyph, new Vector2((W - m.X) * 0.5f, y), size, Spacing,
            Fade(Scale(Palette.HudChrome, 0.4f), alpha));
    }

    // --- Settings screen pieces ---

    // The panel's columns, in internal pixels. Shared by the mixer and the bindings so the
    // two pages line up with each other and read as one screen seen twice.
    private const int ColLeft = 34;    // label left edge
    private const int ColRight = 286;  // value right edge
    private const int SlotOneX = 176;  // primary binding
    private const int SlotTwoX = 240;  // secondary

    /// <summary>
    /// The dark slab the two dense settings pages sit on.
    ///
    /// <para>The screens behind them — the drifting grid off the title menu, a live firefight
    /// off the pause panel — are both a chequerboard floor under a magenta sky, and a column
    /// of eight-pixel key names laid straight onto that is unreadable. The sparse screens (the
    /// title, the settings front page, the pause panel) do not need it and do not get it: half
    /// a dozen words in fourteen-pixel chrome survive anything, and the backdrop showing
    /// through is most of what those screens are.</para>
    /// </summary>
    private static void DrawPanel(byte alpha)
    {
        const int top = 30;
        const int bottom = 234;   // low enough to take the footer hint in with it
        Raylib.DrawRectangle(0, top, W, bottom - top, Fade(new Color(5, 7, 10, 232), alpha));
        Raylib.DrawRectangle(0, top, W, 1, Fade(Scale(Palette.GridFar, 0.55f), alpha));
        Raylib.DrawRectangle(0, bottom - 1, W, 1, Fade(Scale(Palette.GridFar, 0.55f), alpha));
    }

    /// <summary>A smaller, steadier version of the title flicker for sub-screen headings.</summary>
    private static void DrawHeading(string text, float elapsed, byte alpha = 255)
    {
        Font font = Raylib.GetFontDefault();
        const int size = 22;
        Vector2 m = Raylib.MeasureTextEx(font, text, size, Spacing);
        float x = (W - m.X) * 0.5f;
        const int y = 40;

        float breathe = 0.78f + 0.22f * MathF.Abs(MathF.Sin(elapsed * 0.9f));
        Raylib.DrawTextEx(font, text, new Vector2(x, y), size, Spacing,
            Fade(Scale(Palette.HudChrome, breathe), alpha));

        int ruleW = (int)m.X;
        Color rule = Scale(Palette.GridFar, 0.5f + 0.2f * MathF.Sin(elapsed * 1.7f));
        Raylib.DrawRectangle((W - ruleW) / 2, y + size + 5, ruleW, 1, Fade(rule, alpha));
    }

    /// <summary>
    /// A label on the left, its cyclable value (in arrow brackets when focused) on
    /// the right, laid out in a fixed centred column so the rows read as a panel.
    /// </summary>
    private static void DrawValueRow(Font font, string label, string value, bool selected,
        int y, byte alpha)
    {
        const int size = ItemSize;

        Color labelColor = selected ? Palette.HudChrome : Scale(Palette.HudChrome, 0.55f);
        Raylib.DrawTextEx(font, label, new Vector2(ColLeft, y), size, Spacing,
            Fade(labelColor, alpha));

        string shown = selected ? "< " + value + " >" : value;
        Vector2 vs = Raylib.MeasureTextEx(font, shown, size, Spacing);
        Color valColor = selected ? Palette.HudChrome : Scale(Palette.HudChrome, 0.45f);
        Raylib.DrawTextEx(font, shown, new Vector2(ColRight - vs.X, y), size, Spacing,
            Fade(valColor, alpha));
    }

    /// <summary>A centred, bracket-flanked choice — BACK, RESET, the front page's two doors,
    /// the pause panel's rows. The one row shape the whole UI shares.</summary>
    private static void DrawCentredRow(Font font, string label, bool selected, int y,
        byte alpha, bool small = false)
    {
        int size = small ? 10 : ItemSize;
        Vector2 s = Raylib.MeasureTextEx(font, label, size, Spacing);
        float x = (W - s.X) * 0.5f;

        Color c = Fade(selected ? Palette.HudChrome : Scale(Palette.HudChrome, 0.55f), alpha);
        Raylib.DrawTextEx(font, label, new Vector2(x, y), size, Spacing, c);

        if (!selected) return;
        Color bracket = Fade(Palette.HudChrome, alpha);
        int gap = small ? 10 : 14;
        Raylib.DrawTextEx(font, "[", new Vector2(x - gap, y), size, Spacing, bracket);
        Raylib.DrawTextEx(font, "]", new Vector2(x + s.X + 6, y), size, Spacing, bracket);
    }

    // --- helpers ---

    /// <summary>Multiplies an RGB colour toward black by <paramref name="t"/> (0..1), keeping alpha.</summary>
    private static Color Scale(Color c, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color((int)(c.R * t), (int)(c.G * t), (int)(c.B * t), c.A);
    }

    /// <summary>Scales a colour's alpha by <paramref name="alpha"/> (0..255), for fades.</summary>
    private static Color Fade(Color c, byte alpha)
        => new Color((int)c.R, (int)c.G, (int)c.B, (int)(c.A * alpha / 255));

    /// <summary>Cheap deterministic 0..1 hash — the classic fract(sin) trick, for flicker.</summary>
    private static float Hash(float x)
    {
        float s = MathF.Sin(x) * 43758.5453f;
        return s - MathF.Floor(s);
    }
}
