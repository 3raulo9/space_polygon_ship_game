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

    /// <summary>
    /// For an animated specimen (the Crab-Core boss), which protocol phase is
    /// looping on the turntable. Ignored for the static tank/ship silhouettes.
    /// </summary>
    public Entities.CrabCore.State CrabPhase { get; private set; }

    /// <summary>Whether the shown specimen is the animated boss rig.</summary>
    public bool ShowingBoss => Current.Kind == EnemyKind.CrabCoreBoss;

    public TestScreen()
    {
        // Capture harness: VOIDTANKS_TEST_INDEX picks which specimen to open on,
        // so each roster entry can be screenshotted without keypresses.
        if (int.TryParse(Environment.GetEnvironmentVariable("VOIDTANKS_TEST_INDEX"), out int i)
            && i >= 0 && i < EnemyCatalog.All.Count)
            Selected = i;

        // Capture harness: VOIDTANKS_TEST_PHASE opens the boss on a chosen phase
        // (0..5 — the four protocol phases plus the lance's charge and burn) so each
        // of its animations can be screenshotted without keypresses.
        if (int.TryParse(Environment.GetEnvironmentVariable("VOIDTANKS_TEST_PHASE"), out int ph)
            && ph >= 0 && ph <= (int)Entities.CrabCore.State.Firing)
            CrabPhase = (Entities.CrabCore.State)ph;
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

        // On the animated boss, number keys 1..6 scrub between its phases: the four
        // protocol states, then the lance charging and the lance firing.
        if (ShowingBoss)
        {
            int digit = InputMap.MenuDigitPressed();
            if (digit != 0 && digit - 1 <= (int)Entities.CrabCore.State.Firing)
            {
                CrabPhase = (Entities.CrabCore.State)(digit - 1);
                Audio.PlayBlip();
            }
        }

        return Action.None;
    }

    private void Step(int dir)
    {
        int n = EnemyCatalog.All.Count;
        Selected = ((Selected + dir) % n + n) % n; // wrap — a roster reads as a ring
        Audio.PlayBlip();
    }
}
