using System.Numerics;
using Raylib_cs;
using Unrendered.Rendering;

namespace Unrendered.Modding;

/// <summary>
/// The game's own lettering and pointer, for a code mod drawing into the 320×240 target. The
/// hand-drawn 5×7 face the inventory uses rather than Raylib's default font, which turns to
/// mush below its native size — a mod's HUD should look like it belongs on this screen.
/// Capitals, digits and a little punctuation; anything else advances as a blank.
/// </summary>
public static class ModText
{
    /// <summary>Glyph height at a scale of 1, in internal pixels.</summary>
    public const int Height = PixelFont.GlyphH;

    /// <summary>Writes <paramref name="text"/> with its top-left at (x, y), each font pixel a
    /// <paramref name="scale"/>-sided block. Returns how far it advanced.</summary>
    public static int Draw(string text, int x, int y, Color color, int scale = 1)
        => PixelFont.Draw(text, x, y, scale, color);

    /// <summary>Writes a line centred on <paramref name="cx"/>.</summary>
    public static void DrawCentered(string text, int cx, int y, Color color, int scale = 1)
        => PixelFont.DrawCentered(text, cx, y, scale, color);

    /// <summary>How wide a line is when drawn at <paramref name="scale"/>.</summary>
    public static int Measure(string text, int scale = 1) => PixelFont.Measure(text, scale);

    /// <summary>The game's pixel arrow, tip at <paramref name="at"/> (internal pixels).</summary>
    public static void Pointer(Vector2 at) => PixelCursor.Draw(at);

    /// <summary>Where the mouse is, in internal pixels.</summary>
    public static Vector2 Mouse => Renderer.ScreenToInternal(Raylib.GetMousePosition());
}
