using Unrendered.Core;
using Unrendered.Input;

namespace Unrendered.UI;

/// <summary>
/// The panel that drops over a run when Escape is pressed in-world. Spare, like the title
/// menu: slip back into the fight, open the settings, or abandon it to the terminal. Escape
/// resumes, so the same key both opens and closes it.
///
/// <para><b>The same panel in both modes, and deliberately so.</b> Single-player freezes the
/// world behind it and multiplayer does not — a room of twenty cannot be held still because
/// one person went to find the volume — but that difference belongs to the loop, and none of
/// it is visible here. The panel does not know which kind of match it is over, has no row
/// that appears in one and not the other, and does not word itself differently. A player who
/// learns where RESUME sits has learned it for both.</para>
///
/// <para>Owns only selection state; the dim and the drawing live in the Renderer.</para>
/// </summary>
public sealed class PauseMenu
{
    /// <summary>What the pause panel is asking the loop to do this frame.</summary>
    public enum Action { None, Resume, OpenSettings, BackToMenu }

    public enum Item { Resume, Settings, BackToMenu }

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
                Item.Settings => Action.OpenSettings,
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

    public static string Label(Item item) => item switch
    {
        Item.Resume => "RESUME",
        Item.Settings => "SETTINGS",
        _ => "BACK TO MENU",
    };
}
