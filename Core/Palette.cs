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

    // Chess-board floor fill: solid filled cells, no outline. One tone is pure
    // black, the other a dim cold blue so the lit squares read without glowing.
    public static readonly Color FloorDark = new(0, 0, 0, 255);      // pure black
    public static readonly Color FloorLight = new(44, 82, 118, 255); // #2C5276 dim blue

    // Sky: a purple-magenta horizon glow dithered up into pure black overhead — a
    // chunky 8-bit stepped gradient, not a smooth blend. A deep magenta band at the
    // ground line spreading into dark purple, then falling off to black up top.
    public static readonly Color SkyHorizon = new(150, 50, 128, 255); // #96327F magenta-purple
    public static readonly Color SkyMid = new(74, 24, 66, 255);       // #4A1842 dark purple
    public static readonly Color SkyTop = new(0, 0, 0, 255);          // pure black

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

    // --- Crab-Core boss ------------------------------------------------------
    // The Stalker: a dead gunmetal chassis wrapped around a neon core that has no
    // business glowing that bright in this dim world. The chassis stays grim; the
    // core is the wrongness — a hot magenta pilot light that flips to a flashing
    // red the instant it decides to kill you.

    // #737A86 — cold light gunmetal-grey, like old moulded plastic. The boss's
    // carapace, legs and claws read as a pale silhouette against the dark, the way
    // its neon core reads as the one wrong bright thing.
    public static readonly Color CrabChassis = new(115, 122, 134, 255);

    // #E63CC8 — hot neon magenta. The spinning pyramid core at rest.
    public static readonly Color NeonMagenta = new(230, 60, 200, 255);

    // #FF2828 — flashing neon red. The core once the threat display begins.
    public static readonly Color NeonRed = new(255, 40, 40, 255);

    // --- Floating pickups ----------------------------------------------------
    // Salvage that drifts on the grid: a battery cell that recharges shield + hyper,
    // and a stray round that restocks ammo. Both read as "good" — a cool charged
    // green for the cell, the same jaundiced flag-yellow the ammo gauge already uses
    // for the bullet — so they stand apart from the dried-blood enemies.

    // #28786E — dark charged teal, the battery's casing.
    public static readonly Color BatteryFill = new(40, 120, 110, 255);

    // #5AE6C8 — bright charge-band / terminal glow on the battery cell.
    public static readonly Color BatteryCore = new(90, 230, 200, 255);
}
