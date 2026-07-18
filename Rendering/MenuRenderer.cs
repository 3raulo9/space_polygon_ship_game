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
        int y = 128;
        const int step = 26;

        DrawItem(font, "SINGLE PLAYER", Menu.Item.SinglePlayer, menu, y, null);
        DrawItem(font, "MULTIPLAYER", Menu.Item.Multiplayer, menu, y + step, "UNAVAILABLE");
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
    {
        Font font = Raylib.GetFontDefault();
        const string hint = "ARROWS / W S · ENTER SELECT";
        const int size = 8;
        Vector2 s = Raylib.MeasureTextEx(font, hint, size, Spacing);
        // Barely there — a prompt left glowing at the bottom of a dead terminal.
        Color c = Scale(Palette.HudChrome, 0.35f);
        Raylib.DrawTextEx(font, hint, new Vector2((W - s.X) * 0.5f, H - 18), size, Spacing, c);
    }

    // --- helpers ---

    /// <summary>Multiplies an RGB colour toward black by <paramref name="t"/> (0..1), keeping alpha.</summary>
    private static Color Scale(Color c, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color((int)(c.R * t), (int)(c.G * t), (int)(c.B * t), c.A);
    }

    /// <summary>Cheap deterministic 0..1 hash — the classic fract(sin) trick, for flicker.</summary>
    private static float Hash(float x)
    {
        float s = MathF.Sin(x) * 43758.5453f;
        return s - MathF.Floor(s);
    }
}
