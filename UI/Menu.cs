using VoidTanks.Input;

namespace VoidTanks.UI;

/// <summary>
/// The first thing the machine shows you. Deliberately spare: a title that
/// admits the world never finished loading, two choices (one of which isn't
/// really there), and quiet cold chrome. No splash, no music sting, no animation
/// that reads as "welcome" — the menu should feel like a terminal left running
/// in an empty room (Doc 01). Owns only selection state; drawing lives in the
/// Renderer so it shares the low-res, chunky-pixel target.
/// </summary>
public sealed class Menu
{
    /// <summary>What the menu is asking the loop to do this frame.</summary>
    public enum Action
    {
        None,
        StartSinglePlayer,
        OpenTestScreen, // the secret hatch — wired, but goes nowhere yet
        Quit,
    }

    public enum Item
    {
        SinglePlayer,
        Multiplayer, // present but unavailable — a door that won't open
    }

    /// <summary>Multiplayer is shown greyed-out; it can be looked at, not chosen.</summary>
    public static bool IsSelectable(Item item) => item != Item.Multiplayer;

    public Item Selected { get; private set; } = Item.SinglePlayer;

    /// <summary>
    /// Reads input and returns the action the loop should take. Movement skips
    /// the disabled Multiplayer entry so the cursor never rests on a dead option.
    /// </summary>
    public Action Update()
    {
        if (InputMap.QuitPressed) return Action.Quit;

        // Secret keybind ('L' position) — undocumented test screen.
        if (InputMap.SecretTestPressed) return Action.OpenTestScreen;

        if (InputMap.MenuUp) MoveSelection(-1);
        if (InputMap.MenuDown) MoveSelection(+1);

        if (InputMap.MenuConfirm && Selected == Item.SinglePlayer)
            return Action.StartSinglePlayer;

        return Action.None;
    }

    private void MoveSelection(int step)
    {
        int count = System.Enum.GetValues<Item>().Length;
        int i = (int)Selected;
        // Walk in the requested direction until we land on a selectable entry,
        // clamping at the ends so the list doesn't wrap.
        for (int guard = 0; guard < count; guard++)
        {
            int next = i + step;
            if (next < 0 || next >= count) break; // no wrap — a hard edge feels right here
            i = next;
            if (IsSelectable((Item)i)) { Selected = (Item)i; return; }
        }
    }
}
