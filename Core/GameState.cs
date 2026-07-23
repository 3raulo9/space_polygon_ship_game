namespace VoidTanks.Core;

/// <summary>
/// Top-level state machine (Doc 05). Milestone 1 only needs Playing, but the
/// full set is declared so transitions can stay quiet fades, not flashy wipes.
/// </summary>
public enum GameState
{
    Menu,
    ClassSelect, // the hangar: pick a chassis, spend the points, paint it, then launch
    Settings,
    Test,        // hidden bestiary reached by the secret 'L' hatch
    LevelIntro,
    Playing,     // the inventory/crafting panel (E) is a live overlay on this state, not its own
    Paused,      // world frozen behind a pixel-blur while the pause panel is up
    Dead,
    LevelClear,
}
