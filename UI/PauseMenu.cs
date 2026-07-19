using VoidTanks.Core;
using VoidTanks.Input;

namespace VoidTanks.UI;

/// <summary>
/// The panel that drops over a frozen run when Escape is pressed in-world. Spare,
/// like the title menu: two choices — slip back into the fight, or abandon it to
/// the terminal. Escape resumes (so the same key both pauses and unpauses). Owns
/// only selection state; the blur transition and drawing live in the Renderer, so
/// this stays pure state and never touches Raylib.
/// </summary>
public sealed class PauseMenu
{
    /// <summary>What the pause panel is asking the loop to do this frame.</summary>
    public enum Action { None, Resume, BackToMenu }

    public enum Item { Resume, BackToMenu }

    public Item Selected { get; private set; } = Item.Resume;

    /// <summary>Cursor always reopens on Resume — the least-committal option.</summary>
    public void Reset() => Selected = Item.Resume;

    /// <summary>Reads input and returns the action the loop should take.</summary>
    public Action Update()
    {
        // Escape both opens and closes the panel: here it means "resume".
        if (InputMap.QuitPressed) return Action.Resume;

        if (InputMap.MenuUp) Move(-1);
        if (InputMap.MenuDown) Move(+1);

        if (InputMap.MenuConfirm)
        {
            return Selected switch
            {
                Item.Resume => Action.Resume,
                Item.BackToMenu => Action.BackToMenu,
                _ => Action.None,
            };
        }

        return Action.None;
    }

    private void Move(int step)
    {
        int count = System.Enum.GetValues<Item>().Length;
        int next = (int)Selected + step;
        if (next < 0 || next >= count) return; // hard edges, no wrap
        Selected = (Item)next;
        Audio.PlayBlip();
    }
}
