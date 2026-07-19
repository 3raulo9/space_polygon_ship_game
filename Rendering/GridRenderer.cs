using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;

namespace VoidTanks.Rendering;

/// <summary>
/// The floor: a solid chess-board of filled cells receding into fog (Doc 02).
/// It is the player's only spatial reference, so the checker's scroll makes
/// speed read as the squares rush past. No outlines — just filled blocks, one
/// tone pure black, the other a faint dark teal. Cells only reach out to the fog
/// distance, so the world dissolves into the void and the short draw distance is
/// hidden for free.
/// </summary>
public static class GridRenderer
{
    private const float Spacing = 8f;        // world units per cell
    private const float DrawRadius = 168f;   // a touch past FogEnd

    // The floor sits flat at ground level.
    private const float TileY = 0f;

    public static void Draw(Vector2 center)
    {
        // Snap the origin to the player so the checker always fills the view and
        // scrolls smoothly beneath the craft.
        float cx = MathF.Floor(center.X / Spacing) * Spacing;
        float cz = MathF.Floor(center.Y / Spacing) * Spacing;

        int lines = (int)(DrawRadius / Spacing);

        // Filled chess-board, keyed off the tile's world index so the pattern
        // stays fixed in the world and scrolls under the craft instead of
        // flickering as the origin snaps.
        int baseX = (int)MathF.Floor(cx / Spacing);
        int baseZ = (int)MathF.Floor(cz / Spacing);
        for (int i = -lines; i < lines; i++)
        {
            for (int j = -lines; j < lines; j++)
            {
                float x0 = cx + i * Spacing;
                float z0 = cz + j * Spacing;
                var mid = new Vector2(x0 + Spacing * 0.5f, z0 + Spacing * 0.5f);
                float fade = FogFactor(Vector2.Distance(mid, center));
                if (fade >= 0.98f) continue;

                bool light = ((baseX + i + baseZ + j) & 1) == 0;
                Color baseCol = light ? Palette.FloorLight : Palette.FloorDark;
                // Darken the checker with distance: far cells sink toward black so
                // the floor dims away from the player instead of glowing at range.
                // The sqrt bends the falloff so tiles go dark quickly, not just at
                // the very edge of the fog.
                Color c = LerpColor(baseCol, Palette.FloorDark, MathF.Sqrt(fade));
                DrawTile(x0, z0, x0 + Spacing, z0 + Spacing, c);
            }
        }
    }

    /// <summary>
    /// Fills one floor cell with a flat quad (two triangles) at <see cref="TileY"/>.
    /// Backface culling is off game-wide, so winding here doesn't matter.
    /// </summary>
    private static void DrawTile(float x0, float z0, float x1, float z1, Color c)
    {
        var a = new Vector3(x0, TileY, z0);
        var b = new Vector3(x1, TileY, z0);
        var d = new Vector3(x1, TileY, z1);
        var e = new Vector3(x0, TileY, z1);
        Raylib.DrawTriangle3D(a, b, d, c);
        Raylib.DrawTriangle3D(a, d, e, c);
    }

    /// <summary>0 at the camera, 1 once fully dissolved into fog.</summary>
    public static float FogFactor(float dist)
    {
        float t = (dist - Config.FogStart) / (Config.FogEnd - Config.FogStart);
        return Math.Clamp(t, 0f, 1f);
    }

    public static Color LerpColor(Color a, Color b, float t)
    {
        return new Color(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t),
            (int)(a.A + (b.A - a.A) * t));
    }
}
