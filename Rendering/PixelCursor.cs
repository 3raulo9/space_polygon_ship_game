using System.Numerics;
using Raylib_cs;

namespace Unrendered.Rendering;

/// <summary>
/// The pointer. The operating system's cursor is hidden for the whole game, everywhere, and
/// this is what replaces it on the screens the mouse actually drives — the inventory and the
/// creator. Drawn into the 320×240 target like everything else, so it is blown up by the same
/// nearest-neighbour scale as the world and comes out as fat hard pixels instead of a crisp
/// modern arrow sitting on top of a retro picture.
///
/// <para>'F' is the dark border, 'B' the light body: an arrow needs both or it disappears
/// against the pale slot wells and the grid it spends its time over.</para>
/// </summary>
internal static class PixelCursor
{
    private static readonly string[] Glyph =
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

    private static readonly Color Border = new(4, 6, 9, 235);

    /// <summary>Draws the arrow with its tip on <paramref name="at"/>, in internal pixels.
    /// Must be called last, inside the target's texture mode, so it sits over everything.</summary>
    public static void Draw(Vector2 at)
    {
        int x0 = (int)MathF.Round(at.X);
        int y0 = (int)MathF.Round(at.Y);

        for (int row = 0; row < Glyph.Length; row++)
        {
            string line = Glyph[row];
            for (int col = 0; col < line.Length; col++)
            {
                if (line[col] == '.') continue;
                Raylib.DrawRectangle(x0 + col, y0 + row, 1, 1,
                    line[col] == 'F' ? Border : Color.White);
            }
        }
    }
}
