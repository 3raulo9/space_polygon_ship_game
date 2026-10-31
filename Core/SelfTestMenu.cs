using Unrendered.UI;

namespace Unrendered.Core;

/// <summary>
/// The title menu: the order its entries are walked in, and the long hold on Escape that
/// stands between a player and the desktop.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The cursor walks the entries in the order the screen lists them, and that
    /// order is DESCENT, SANDBOX, MULTIPLAYER, SETTINGS.</summary>
    private static string? TheTitleMenuWalksInTheOrderItShows()
    {
        Menu.Item[] want = { Menu.Item.Descent, Menu.Item.Sandbox, Menu.Item.Multiplayer, Menu.Item.Settings };
        Menu.Item[] got = Enum.GetValues<Menu.Item>();
        if (!got.SequenceEqual(want))
            return "the entries run " + string.Join(", ", got.Select(Menu.Label));
        if (new Menu().Selected != Menu.Item.Descent) return "the cursor does not start on DESCENT";
        return null;
    }

    /// <summary>
    /// Escape on the title menu closes the game only after a long hold —
    /// <see cref="Menu.QuitHoldSeconds"/>, two and a half seconds. Anything shorter is
    /// nothing, and a let-go starts the count again from zero rather than letting two short
    /// holds add up to one long one. A hold that began somewhere else (Escape still down from
    /// backing out of a screen) does not count at all.
    /// </summary>
    private static string? QuittingTakesALongHold()
    {
        const float dt = 1f / 60f;
        const float hold = Menu.QuitHoldSeconds;
        int Frames(float seconds) => (int)MathF.Round(seconds / dt);

        // Pressed, held for most of the time, let go: no quit, and the notice fading out.
        var menu = new Menu();
        for (int i = 0; i < Frames(hold - 0.2f); i++)
            if (menu.StepQuitHold(pressed: i == 0, held: true, dt)) return "a hold short of the full time quit the game";
        if (menu.QuitShown < 0.99f) return "the QUITTING notice never came all the way in";
        if (menu.QuitProgress < 0.9f) return "the hold's progress did not follow the time held";
        menu.StepQuitHold(pressed: false, held: false, dt);
        if (menu.QuitProgress != 0f) return "letting go did not reset the count";
        for (int i = 0; i < Frames(0.5f); i++) menu.StepQuitHold(false, false, dt);
        if (menu.QuitShown > 0f) return "the notice stayed up after Escape was let go";

        // Two short holds do not add up.
        for (int i = 0; i < Frames(hold * 0.6f); i++) menu.StepQuitHold(i == 0, true, dt);
        menu.StepQuitHold(false, false, dt);
        for (int i = 0; i < Frames(hold * 0.6f); i++)
            if (menu.StepQuitHold(i == 0, true, dt)) return "two short holds added up to a quit";
        menu.StepQuitHold(false, false, dt);

        // A key already down when the menu arrived is not a request to leave.
        for (int i = 0; i < Frames(hold * 2f); i++)
            if (menu.StepQuitHold(pressed: false, held: true, dt)) return "an Escape held from another screen quit the game";
        menu.StepQuitHold(false, false, dt);

        // And the full hold does quit, once.
        int quits = 0, at = -1;
        for (int i = 0; i < Frames(hold * 2f); i++)
            if (menu.StepQuitHold(i == 0, true, dt)) { quits++; if (at < 0) at = i; }
        if (quits != 1) return $"a hold twice the length quit {quits} times, wanted once";
        if (MathF.Abs(at * dt - Menu.QuitHoldSeconds) > 0.05f)
            return $"the game quit after {at * dt:0.00} s of holding, not {Menu.QuitHoldSeconds}";
        return null;
    }
}
