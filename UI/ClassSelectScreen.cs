using VoidTanks.Core;
using VoidTanks.Input;

namespace VoidTanks.UI;

/// <summary>
/// The hangar: the screen that stands between choosing Single Player and actually
/// being out there. Three panes side by side — the chassis roster on the left, the
/// build's own 3D model turning in the middle, the point budget on the right — with a
/// paint bay folded in behind it. Tab walks the panes, arrows work inside whichever
/// one has focus, Escape backs out to the menu.
///
/// Owns only selection state and mutates the shared <see cref="Loadout"/> in place;
/// drawing lives in the Renderer, same as every other screen here.
/// </summary>
public sealed class ClassSelectScreen
{
    public enum Action { None, Launch, Back }

    /// <summary>Which column the arrows are currently driving.</summary>
    public enum Pane { Classes, Stats, Actions }

    /// <summary>The two things the action row can do.</summary>
    public enum Act { Customise, Launch }

    private readonly Loadout _loadout;

    public Loadout Loadout => _loadout;
    public Pane Focus { get; private set; } = Pane.Classes;
    public int ClassIndex { get; private set; }
    public Loadout.Stat StatRow { get; private set; } = Loadout.Stat.Shield;
    public Act ActionRow { get; private set; } = Act.Launch;

    /// <summary>True while the paint bay is up — it takes the whole screen and owns
    /// every key, so the renderer draws it instead of the three panes.</summary>
    public bool Customising { get; private set; }

    /// <summary>Which row of the paint bay is focused: 0..PartCount-1 are the parts,
    /// then RESET, then BACK.</summary>
    public int PaintRow { get; private set; }

    public ClassSelectScreen(Loadout loadout)
    {
        _loadout = loadout;
        ClassIndex = (int)loadout.Class;

        // Capture harness: VOIDTANKS_CLASS_INDEX / _PANE / _PAINT open the screen in a
        // chosen state so each pane and the paint bay can be screenshotted headlessly.
        if (int.TryParse(Environment.GetEnvironmentVariable("VOIDTANKS_CLASS_INDEX"), out int ci)
            && ci >= 0 && ci < ClassCatalog.All.Count)
            SelectClass(ci, quiet: true);
        if (int.TryParse(Environment.GetEnvironmentVariable("VOIDTANKS_CLASS_PANE"), out int cp)
            && cp >= 0 && cp <= (int)Pane.Actions)
            Focus = (Pane)cp;
        if (Environment.GetEnvironmentVariable("VOIDTANKS_CLASS_PAINT") == "1" && CanCustomise)
            Customising = true;
    }

    public ClassArchetype Current => ClassCatalog.All[ClassIndex];

    /// <summary>Parts of the shown chassis that take their own colour.</summary>
    public int PartCount => Current.PartNames.Length;

    /// <summary>Rows in the paint bay: every part, then RESET, then BACK.</summary>
    public int PaintRowCount => PartCount + 2;
    public int ResetRow => PartCount;
    public int BackRow => PartCount + 1;

    /// <summary>An offline chassis has no model and nothing to paint.</summary>
    public bool CanCustomise => Current.Available && PartCount > 0;

    /// <summary>Whether LAUNCH will actually do anything — a locked chassis can be
    /// looked at but not driven.</summary>
    public bool CanLaunch => Current.Available;

    public Action Update()
    {
        if (Customising) return UpdatePaint();

        if (InputMap.QuitPressed) return Back();

        if (InputMap.MenuTab) CyclePane(InputMap.MenuTabBack ? -1 : +1);

        switch (Focus)
        {
            case Pane.Classes: return UpdateClasses();
            case Pane.Stats: UpdateStats(); return Action.None;
            default: return UpdateActions();
        }
    }

    /// <summary>
    /// Leaves for the menu, first making sure the build isn't left pointing at a chassis
    /// the machine can't make. Browsing the roster writes whatever is highlighted into
    /// the loadout — that is what keeps the turntable showing the thing you are reading
    /// about — so backing out while parked on an offline chassis would otherwise persist
    /// an unbuildable class as the player's current craft.
    /// </summary>
    private Action Back()
    {
        if (!Current.Available)
        {
            _loadout.Class = PlayerClass.Tank;
            ClassIndex = (int)PlayerClass.Tank;
        }
        return Action.Back;
    }

    // --- The three panes -----------------------------------------------------

    private Action UpdateClasses()
    {
        if (InputMap.MenuUp) Step(-1);
        if (InputMap.MenuDown) Step(+1);
        // Right off the roster falls into the budget, so the screen can be driven
        // with the arrows alone if the player never finds Tab.
        if (InputMap.MenuRight) CyclePane(+1);
        // Confirming a buildable chassis jumps straight to LAUNCH — the common path
        // through this screen is "pick a thing, go", and it should cost two keys.
        if (InputMap.MenuConfirm && CanLaunch)
        {
            Focus = Pane.Actions;
            ActionRow = Act.Launch;
            Audio.PlayBlip();
        }
        return Action.None;
    }

    private void UpdateStats()
    {
        int count = 3;
        if (InputMap.MenuUp) { StatRow = (Loadout.Stat)(((int)StatRow - 1 + count) % count); Audio.PlayBlip(); }
        if (InputMap.MenuDown) { StatRow = (Loadout.Stat)(((int)StatRow + 1) % count); Audio.PlayBlip(); }

        // A refused nudge (the budget is spent, or the track is already at a stop)
        // stays silent, so the blip means "that worked" and nothing else.
        if (InputMap.MenuLeft && _loadout.Adjust(StatRow, -1)) Audio.PlayBlip();
        if (InputMap.MenuRight && _loadout.Adjust(StatRow, +1)) Audio.PlayBlip();
    }

    private Action UpdateActions()
    {
        if (InputMap.MenuLeft) SetAction(Act.Customise);
        if (InputMap.MenuRight) SetAction(Act.Launch);
        // Up/down out of the action row lands back on the roster, so no pane is a
        // dead end for a player who never presses Tab.
        if (InputMap.MenuUp || InputMap.MenuDown) { Focus = Pane.Classes; Audio.PlayBlip(); }

        if (!InputMap.MenuConfirm) return Action.None;

        if (ActionRow == Act.Launch)
            return CanLaunch ? Action.Launch : Action.None;

        if (CanCustomise)
        {
            Customising = true;
            PaintRow = 0;
            Audio.PlayBlip();
        }
        return Action.None;
    }

    private void SetAction(Act act)
    {
        if (ActionRow == act) return;
        ActionRow = act;
        Audio.PlayBlip();
    }

    private void CyclePane(int dir)
    {
        const int count = 3;
        Focus = (Pane)((((int)Focus + dir) % count + count) % count);
        Audio.PlayBlip();
    }

    private void Step(int dir)
    {
        int n = ClassCatalog.All.Count;
        // Wraps: the roster reads as a ring, the same as the bestiary's does. Locked
        // chassis are still landed on — they have descriptions worth reading, and the
        // machine admitting it can't build them is the point of listing them.
        SelectClass(((ClassIndex + dir) % n + n) % n);
    }

    private void SelectClass(int index, bool quiet = false)
    {
        ClassIndex = index;
        // The loadout tracks whatever is on show; launching a locked chassis is
        // blocked at the action row, not here, so the preview can still draw it.
        _loadout.Class = Current.Kind;
        PaintRow = 0;
        if (!quiet) Audio.PlayBlip();
    }

    // --- The paint bay -------------------------------------------------------

    private Action UpdatePaint()
    {
        if (InputMap.QuitPressed)
        {
            Customising = false;
            Audio.PlayBlip();
            return Action.None;
        }

        int n = PaintRowCount;
        if (InputMap.MenuUp) { PaintRow = (PaintRow - 1 + n) % n; Audio.PlayBlip(); }
        if (InputMap.MenuDown) { PaintRow = (PaintRow + 1) % n; Audio.PlayBlip(); }

        if (PaintRow < PartCount)
        {
            if (InputMap.MenuLeft) { _loadout.CycleSwatch(Current.Kind, PaintRow, -1); Audio.PlayBlip(); }
            if (InputMap.MenuRight) { _loadout.CycleSwatch(Current.Kind, PaintRow, +1); Audio.PlayBlip(); }
            // Enter nudges forward too, so the bay is usable without arrow keys.
            if (InputMap.MenuConfirm) { _loadout.CycleSwatch(Current.Kind, PaintRow, +1); Audio.PlayBlip(); }
            return Action.None;
        }

        if (!InputMap.MenuConfirm) return Action.None;

        if (PaintRow == ResetRow) _loadout.ResetPaint(Current.Kind);
        else Customising = false;
        Audio.PlayBlip();
        return Action.None;
    }
}
