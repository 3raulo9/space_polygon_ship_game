using System.Numerics;
using Raylib_cs;

namespace VoidTanks.Rendering;

/// <summary>
/// A tiny hand-drawn 5×7 bitmap font, rendered by stamping solid rectangles per lit
/// pixel. Built for the inventory panel, where Raylib's default font — drawn below its
/// native 10px size to fit the small slots — turns to unreadable mush. Every glyph here
/// is authored as a crisp 5-wide, 7-tall block so it stays hard-edged at the internal
/// resolution and scales up cleanly with the rest of the chunky-pixel look.
///
/// Uppercase letters, digits and a handful of punctuation only — the panel never needs
/// more. Unknown characters advance as a blank.
/// </summary>
internal static class PixelFont
{
    public const int GlyphW = 5;
    public const int GlyphH = 7;

    /// <summary>Draws <paramref name="text"/> with its top-left at (x, y). Each font
    /// pixel becomes a <paramref name="px"/>×<paramref name="px"/> block; glyphs are
    /// separated by one font pixel. Returns the advance width in screen pixels.</summary>
    public static int Draw(string text, int x, int y, int px, Color color)
    {
        int cx = x;
        foreach (char ch in text.ToUpperInvariant())
        {
            if (ch != ' ' && Glyphs.TryGetValue(ch, out string[]? rows))
            {
                for (int r = 0; r < GlyphH; r++)
                {
                    string row = rows[r];
                    for (int c = 0; c < GlyphW; c++)
                        if (c < row.Length && row[c] == '#')
                            Raylib.DrawRectangle(cx + c * px, y + r * px, px, px, color);
                }
            }
            cx += (GlyphW + 1) * px;   // one blank column between glyphs
        }
        return cx - x;
    }

    /// <summary>Advance width of a string at the given pixel scale, matching Draw.</summary>
    public static int Measure(string text, int px) => text.Length * (GlyphW + 1) * px;

    /// <summary>Draws text centred horizontally on <paramref name="cx"/>.</summary>
    public static void DrawCentered(string text, int cx, int y, int px, Color color)
    {
        int w = Measure(text, px);
        Draw(text, cx - w / 2, y, px, color);
    }

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
        ['C'] = new[] { ".####", "#....", "#....", "#....", "#....", "#....", ".####" },
        ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
        ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
        ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
        ['G'] = new[] { ".####", "#....", "#....", "#..##", "#...#", "#...#", ".####" },
        ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['I'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####" },
        ['J'] = new[] { "..###", "...#.", "...#.", "...#.", "#..#.", "#..#.", ".##.." },
        ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
        ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
        ['M'] = new[] { "#...#", "##.##", "#.#.#", "#...#", "#...#", "#...#", "#...#" },
        ['N'] = new[] { "#...#", "##..#", "#.#.#", "#.#.#", "#..##", "#...#", "#...#" },
        ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
        ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
        ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
        ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
        ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
        ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
        ['W'] = new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#" },
        ['X'] = new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" },
        ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
        ['Z'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" },
        ['0'] = new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
        ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
        ['2'] = new[] { ".###.", "#...#", "....#", "..##.", ".#...", "#....", "#####" },
        ['3'] = new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." },
        ['4'] = new[] { "#...#", "#...#", "#...#", "#####", "....#", "....#", "....#" },
        ['5'] = new[] { "#####", "#....", "#....", "####.", "....#", "....#", "####." },
        ['6'] = new[] { ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###." },
        ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
        ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
        ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###." },
        ['-'] = new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." },
        ['/'] = new[] { "....#", "....#", "...#.", "..#..", ".#...", "#....", "#...." },
        [':'] = new[] { ".....", "..#..", "..#..", ".....", "..#..", "..#..", "....." },
        ['+'] = new[] { ".....", "..#..", "..#..", "#####", "..#..", "..#..", "....." },
        ['.'] = new[] { ".....", ".....", ".....", ".....", ".....", ".....", "..#.." },
    };
}
