using VoidTanks.Core;
using VoidTanks.Entities;
using VoidTanks.Input;

namespace VoidTanks.UI;

/// <summary>
/// The hidden bestiary reached by the secret 'L' hatch on the menu. A turntable:
/// one enemy at a time turns slowly in the void while its placeholder name and
/// stats sit alongside. Left/right steps through the roster — the two real
/// hunters and the unbuilt polygon spaceships — so their silhouettes can be
/// eyeballed side by side. Owns only selection state; drawing lives in the
/// Renderer, same as the menu and settings screens.
/// </summary>
public sealed class TestScreen
{
    public enum Action { None, Back }

    /// <summary>Index into <see cref="EnemyCatalog.All"/> of the specimen on show.</summary>
    public int Selected { get; private set; }

    public TestScreen()
    {
        // Capture harness: VOIDTANKS_TEST_INDEX picks which specimen to open on,
        // so each roster entry can be screenshotted without keypresses.
        if (int.TryParse(Environment.GetEnvironmentVariable("VOIDTANKS_TEST_INDEX"), out int i)
            && i >= 0 && i < EnemyCatalog.All.Count)
            Selected = i;
    }

    public EnemyArchetype Current => EnemyCatalog.All[Selected];

    /// <summary>Reads input; returns Back when the tester leaves (Escape).</summary>
    public Action Update()
    {
        if (InputMap.QuitPressed) return Action.Back;

        if (InputMap.MenuLeft) Step(-1);
        if (InputMap.MenuRight) Step(+1);
        // Up/down cycle too, so it's reachable however the tester reaches for it.
        if (InputMap.MenuUp) Step(-1);
        if (InputMap.MenuDown) Step(+1);

        return Action.None;
    }

    private void Step(int dir)
    {
        int n = EnemyCatalog.All.Count;
        Selected = ((Selected + dir) % n + n) % n; // wrap — a roster reads as a ring
        Audio.PlayBlip();
    }
}
