using Raylib_cs;

namespace VoidTanks.Core;

/// <summary>
/// The whole game lives in a small, dark, slightly-sick palette (Doc 02).
/// Nothing warm, nothing pretty. Every surface should pull one of these.
/// </summary>
public static class Palette
{
    // #05070A — near-black with a faint cold-blue cast. Never pure black.
    public static readonly Color Void = new(5, 7, 10, 255);

    // #0A1418 — the colour everything dissolves *into* at distance.
    public static readonly Color Fog = new(10, 20, 24, 255);

    // Grid: sickly teal-green. The grid is the only "floor".
    public static readonly Color GridNear = new(18, 160, 140, 255); // #12A08C
    public static readonly Color GridFar = new(14, 122, 107, 255);  // #0E7A6B

    // #8AA0A8 — desaturated cold grey, like old plastic. HUD chrome.
    public static readonly Color HudChrome = new(138, 160, 168, 255);

    // #B83A2E — dull, dried-blood red. Enemy tanks. Not a hero red.
    public static readonly Color EnemyFill = new(184, 58, 46, 255);

    // #C87A22 — bruised orange. Elite cones (from level 6).
    public static readonly Color EliteFill = new(200, 122, 34, 255);

    // #C9B23A — dim, jaundiced yellow. Flags.
    public static readonly Color Flag = new(201, 178, 58, 255);

    // #7A1C1C — deep, throbbing red for HUD alerts / low health.
    public static readonly Color Warning = new(122, 28, 28, 255);

    /// <summary>Player craft body — a cold, desaturated survivor colour.</summary>
    public static readonly Color PlayerFill = new(96, 120, 128, 255);
}
