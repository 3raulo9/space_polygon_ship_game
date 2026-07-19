using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.UI;

namespace VoidTanks.Rendering;

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

    /// <summary>Draws the controls screen (rows of cyclable values) over the target.</summary>
    public static void DrawSettings(SettingsScreen screen, float elapsed)
    {
        DrawHeading("SETTINGS", elapsed);

        Font font = Raylib.GetFontDefault();
        int y = 108;
        const int step = 24;

        DrawValueRow(font, screen, SettingsScreen.Row.SwapTurn, y);
        DrawValueRow(font, screen, SettingsScreen.Row.Movement, y + step);
        DrawValueRow(font, screen, SettingsScreen.Row.Fire, y + step * 2);
        DrawBackRow(font, screen, y + step * 3 + 8);

        DrawFooterHint("< > CHANGE · UP DN MOVE · ESC BACK");
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

        DrawItem(font, "SINGLE PLAYER", Menu.Item.SinglePlayer, menu, y, null);
        DrawItem(font, "SETTINGS", Menu.Item.Settings, menu, y + step, null);
        DrawItem(font, "MULTIPLAYER", Menu.Item.Multiplayer, menu, y + step * 2, "UNAVAILABLE");
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

    private static void DrawFooterHint(string hint, byte alpha)
    {
        Font font = Raylib.GetFontDefault();
        const int size = 8;
        Vector2 s = Raylib.MeasureTextEx(font, hint, size, Spacing);
        // Barely there — a prompt left glowing at the bottom of a dead terminal.
        Color c = Fade(Scale(Palette.HudChrome, 0.35f), alpha);
        Raylib.DrawTextEx(font, hint, new Vector2((W - s.X) * 0.5f, H - 18), size, Spacing, c);
    }

    // --- Pause panel ---

    /// <summary>
    /// Draws the pause panel over the blurred frozen frame. It fades up only once
    /// the pixel-blur has mostly closed (<paramref name="t"/> past ~0.35), so the
    /// text arrives after the world has dissolved, not on top of a sharp scene.
    /// </summary>
    public static void DrawPause(UI.PauseMenu menu, float elapsed, float t)
    {
        float a = Math.Clamp((t - 0.35f) / 0.45f, 0f, 1f);
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

        DrawPauseItem(font, "RESUME", UI.PauseMenu.Item.Resume, menu, 128, alpha);
        DrawPauseItem(font, "BACK TO MENU", UI.PauseMenu.Item.BackToMenu, menu, 152, alpha);

        DrawFooterHint("ESC / ENTER RESUME", alpha);
    }

    private static void DrawPauseItem(Font font, string label, UI.PauseMenu.Item item,
        UI.PauseMenu menu, int y, byte alpha)
    {
        bool selected = menu.Selected == item;
        Vector2 size = Raylib.MeasureTextEx(font, label, ItemSize, Spacing);
        float x = (W - size.X) * 0.5f;

        Color color = Fade(selected ? Palette.HudChrome : Scale(Palette.HudChrome, 0.55f), alpha);
        Raylib.DrawTextEx(font, label, new Vector2(x, y), ItemSize, Spacing, color);

        if (selected)
        {
            Color bracket = Fade(Palette.HudChrome, alpha);
            Raylib.DrawTextEx(font, "[", new Vector2(x - 14, y), ItemSize, Spacing, bracket);
            Raylib.DrawTextEx(font, "]", new Vector2(x + size.X + 6, y), ItemSize, Spacing, bracket);
        }
    }

    // --- Settings screen pieces ---

    /// <summary>A smaller, steadier version of the title flicker for sub-screen headings.</summary>
    private static void DrawHeading(string text, float elapsed)
    {
        Font font = Raylib.GetFontDefault();
        const int size = 22;
        Vector2 m = Raylib.MeasureTextEx(font, text, size, Spacing);
        float x = (W - m.X) * 0.5f;
        const int y = 40;

        float breathe = 0.78f + 0.22f * MathF.Abs(MathF.Sin(elapsed * 0.9f));
        Raylib.DrawTextEx(font, text, new Vector2(x, y), size, Spacing, Scale(Palette.HudChrome, breathe));

        int ruleW = (int)m.X;
        Color rule = Scale(Palette.GridFar, 0.5f + 0.2f * MathF.Sin(elapsed * 1.7f));
        Raylib.DrawRectangle((W - ruleW) / 2, y + size + 5, ruleW, 1, rule);
    }

    /// <summary>
    /// A label on the left, its cyclable value (in arrow brackets when focused) on
    /// the right, laid out in a fixed centred column so the rows read as a panel.
    /// </summary>
    private static void DrawValueRow(Font font, SettingsScreen screen, SettingsScreen.Row row, int y)
    {
        bool selected = screen.Selected == row;
        const int size = ItemSize;
        const int colLeft = 60;    // label left edge (internal px)
        const int colRight = 260;  // value right edge

        Color labelColor = selected ? Palette.HudChrome : Scale(Palette.HudChrome, 0.55f);
        Raylib.DrawTextEx(font, SettingsScreen.Label(row), new Vector2(colLeft, y), size, Spacing, labelColor);

        string value = screen.Value(row);
        string shown = selected ? "< " + value + " >" : value;
        Vector2 vs = Raylib.MeasureTextEx(font, shown, size, Spacing);
        Color valColor = selected ? Palette.HudChrome : Scale(Palette.HudChrome, 0.45f);
        Raylib.DrawTextEx(font, shown, new Vector2(colRight - vs.X, y), size, Spacing, valColor);
    }

    private static void DrawBackRow(Font font, SettingsScreen screen, int y)
    {
        bool selected = screen.Selected == SettingsScreen.Row.Back;
        const int size = ItemSize;
        string label = "BACK";
        Vector2 s = Raylib.MeasureTextEx(font, label, size, Spacing);
        float x = (W - s.X) * 0.5f;
        Color c = selected ? Palette.HudChrome : Scale(Palette.HudChrome, 0.55f);
        Raylib.DrawTextEx(font, label, new Vector2(x, y), size, Spacing, c);
        if (selected)
        {
            Raylib.DrawTextEx(font, "[", new Vector2(x - 14, y), size, Spacing, Palette.HudChrome);
            Raylib.DrawTextEx(font, "]", new Vector2(x + s.X + 6, y), size, Spacing, Palette.HudChrome);
        }
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
