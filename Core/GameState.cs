namespace VoidTanks.Core;

/// <summary>
/// Top-level state machine (Doc 05). Milestone 1 only needs Playing, but the
/// full set is declared so transitions can stay quiet fades, not flashy wipes.
/// </summary>
public enum GameState
{
    Menu,
    Settings,
    LevelIntro,
    Playing,
    Dead,
    LevelClear,
}
