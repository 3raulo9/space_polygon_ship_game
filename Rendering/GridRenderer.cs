using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;

namespace VoidTanks.Rendering;

/// <summary>
/// The floor: a flat plane of teal lines receding into fog (Doc 02). It is the
/// player's only spatial reference, so line spacing must make speed read as the
/// lines rush past. Above the horizon: nothing. We only ever draw grid lines
/// out to the fog distance, so the world dissolves into the void colour and the
/// short draw distance is hidden for free.
/// </summary>
public static class GridRenderer
{
    private const float Spacing = 8f;        // world units between lines
    private const float DrawRadius = 168f;   // a touch past FogEnd

    public static void Draw(Vector2 center)
    {
        // Snap the grid origin to the player so lines always fill the view and
        // the pattern scrolls smoothly beneath the craft.
        float cx = MathF.Floor(center.X / Spacing) * Spacing;
        float cz = MathF.Floor(center.Y / Spacing) * Spacing;

        int lines = (int)(DrawRadius / Spacing);

        for (int i = -lines; i <= lines; i++)
        {
            float x = cx + i * Spacing;
            float z = cz + i * Spacing;

            // Lines parallel to Z (varying X).
            DrawFadedLine(
                new Vector3(x, 0f, cz - DrawRadius),
                new Vector3(x, 0f, cz + DrawRadius),
                center);

            // Lines parallel to X (varying Z).
            DrawFadedLine(
                new Vector3(cx - DrawRadius, 0f, z),
                new Vector3(cx + DrawRadius, 0f, z),
                center);
        }
    }

    /// <summary>
    /// Draws a grid line whose colour lerps toward the fog colour by the
    /// distance of its midpoint from the player, so distant lines melt away.
    /// </summary>
    private static void DrawFadedLine(Vector3 a, Vector3 b, Vector2 center)
    {
        var mid = new Vector2((a.X + b.X) * 0.5f, (a.Z + b.Z) * 0.5f);
        float dist = Vector2.Distance(mid, center);

        float fade = FogFactor(dist);
        // Cull lines that have all but dissolved into fog. This stops the far
        // lines from piling into a bright band at the horizon and lets the grid
        // genuinely melt into the fog colour instead of glowing.
        if (fade >= 0.96f) return;
        Color c = LerpColor(Palette.GridNear, Palette.Fog, fade);
        Raylib.DrawLine3D(a, b, c);
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
