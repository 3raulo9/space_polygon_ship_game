using Unrendered.Acoustics;
using Unrendered.Core;
using Unrendered.Input;

namespace Unrendered.UI;

/// <summary>
/// Settings. One screen, three pages: a two-item front door, a scrolling list of every
/// binding in the game, and the mixer.
///
/// <para>The same instance is opened from the title menu and from the pause panel, which is
/// the whole reason it is a page stack rather than two screens. A player who learns this
/// list once should not have to learn a second, differently-shaped one because they happened
/// to reach it mid-match — and there is nothing on it that cannot be changed while a run is
/// going, since every value it touches is read fresh on the next frame.</para>
///
/// <para>Owns only selection state and the rebinding prompt. Drawing lives in the Renderer,
/// as it does for every other screen here.</para>
/// </summary>
public sealed class SettingsScreen
{
    public enum Page { Root, Controls, Audio }
    public enum Action { None, Back }

    /// <summary>The two doors on the front page, plus the way out.</summary>
    public enum RootRow { Controls, Audio, Back }

    /// <summary>A row on the mixer page. Walked as an enum so the renderer and the input
    /// handler cannot disagree about what is where.</summary>
    public enum AudioRow
    {
        Master,
        Music,
        Shooting,
        Explosions,
        Enemies,
        Player,
        Mono,
        SoftenLoud,
        Back,
    }

    private readonly Settings _settings;

    public SettingsScreen(Settings settings) => _settings = settings;

    public Settings Settings => _settings;

    public Page At { get; private set; } = Page.Root;
    public RootRow Root { get; private set; } = RootRow.Controls;
    public AudioRow Audio { get; private set; } = AudioRow.Master;

    // --- The controls page's cursor ---------------------------------------------------
    // Two axes: which action, and which of its two slots. The slot is part of the cursor
    // rather than a mode you enter, so binding a fallback is left-right-enter and not a
    // second decision about what kind of thing you are about to do.

    /// <summary>Index into <see cref="InputActions.All"/>, or one past the end for the
    /// RESET row and two past it for BACK.</summary>
    public int ControlRow { get; private set; }
    public int ControlSlot { get; private set; }

    /// <summary>First visible row of the controls list. The page holds far more rows than a
    /// 240-line target can, so the view follows the cursor.</summary>
    public int ControlScroll { get; private set; }

    // The list's geometry, in internal pixels. It lives here rather than in the renderer
    // because how many rows fit is not a drawing detail — it is what the cursor has to stay
    // inside, and the two cannot be allowed to disagree.
    public const int RowHeight = 13;
    public const int HeadingHeight = 16;   // the label, its rule, and air under both
    public const int ListTop = 76;
    public const int ListBottom = 190;     // RESET sits at 196

    /// <summary>
    /// How many binding rows fit when the window starts at <paramref name="from"/>.
    ///
    /// <para>Not a constant, because the section headings are drawn inline: a window that
    /// opens in the middle of GENERAL fits eight rows and one that crosses TANK, SPIDER and
    /// SOLDIER fits five, since three headings have eaten thirty-three pixels of it. A fixed
    /// count would either waste half the page or run the list off the bottom and through the
    /// RESET row, depending on where the player had scrolled to.</para>
    /// </summary>
    public static int RowsFrom(int from)
    {
        const int budget = ListBottom - ListTop;
        int used = 0, fit = 0;
        InputSection? last = null;

        for (int i = from; i < InputActions.All.Length; i++)
        {
            var section = InputActions.SectionOf(InputActions.All[i]);
            int cost = RowHeight + (section != last ? HeadingHeight : 0);
            if (used + cost > budget) break;
            used += cost;
            last = section;
            fit++;
        }

        // At least one, always: a window that fits nothing cannot be scrolled out of.
        return Math.Max(1, fit);
    }

    /// <summary>Rows after the last action: RESET, then BACK.</summary>
    public const int ControlExtraRows = 2;
    public int ControlResetRow => InputActions.All.Length;
    public int ControlBackRow => InputActions.All.Length + 1;

    /// <summary>The action the cursor is on, or null when it is on RESET or BACK.</summary>
    public InputAction? FocusedAction
        => ControlRow < InputActions.All.Length ? InputActions.All[ControlRow] : null;

    /// <summary>Set while the screen is waiting for the player to press something. The whole
    /// page keeps drawing behind the prompt so they can see which row they are answering
    /// for.</summary>
    public bool Capturing { get; private set; }

    /// <summary>What the last capture collided with, for one frame's worth of feedback. Not
    /// a refusal — the binding took — just an answer to "did I just break something".</summary>
    public InputAction? LastClash { get; private set; }

    /// <summary>Reopens on the front page. Called whenever the screen is entered, so
    /// backing out of the mixer and coming back does not drop you into the mixer.</summary>
    public void Reset()
    {
        At = Page.Root;
        Root = RootRow.Controls;
        Audio = AudioRow.Master;
        ControlRow = 0;
        ControlSlot = 0;
        ControlScroll = 0;
        Capturing = false;
        LastClash = null;
    }

    /// <summary>
    /// Capture harness only: opens a named page with the cursor parked on a given row, so the
    /// screenshot hatch can photograph the controls list scrolled down to the SOLDIER or the
    /// VIRUS without a human walking there. Moves the cursor the way a player does, so what
    /// gets photographed is a state the screen can actually be in.
    /// </summary>
    public void OpenForCapture(string page, int row)
    {
        At = page switch
        {
            "controls" => Page.Controls,
            "sound" => Page.Audio,
            _ => Page.Root,
        };
        if (At != Page.Controls) return;

        ControlRow = 0;
        ControlScroll = 0;
        int want = Math.Clamp(row, 0, InputActions.All.Length - 1);
        while (ControlRow < want) StepRow(+1, InputActions.All.Length + ControlExtraRows);
    }

    /// <summary>Reads input and returns Back when the player has left the screen entirely.
    /// Every value is written straight into the live <see cref="Settings"/>, so a change is
    /// in force before the frame it was made on is drawn.</summary>
    public Action Update()
    {
        if (Capturing) { UpdateCapture(); return Action.None; }

        return At switch
        {
            Page.Controls => UpdateControls(),
            Page.Audio => UpdateAudio(),
            _ => UpdateRoot(),
        };
    }

    // --- The front page ---------------------------------------------------------------

    private Action UpdateRoot()
    {
        if (InputMap.QuitPressed) return Action.Back;

        int count = System.Enum.GetValues<RootRow>().Length;
        if (InputMap.MenuUp) { Root = (RootRow)(((int)Root - 1 + count) % count); Core.Audio.PlayBlip(); }
        if (InputMap.MenuDown) { Root = (RootRow)(((int)Root + 1) % count); Core.Audio.PlayBlip(); }

        if (!InputMap.MenuConfirm) return Action.None;

        switch (Root)
        {
            case RootRow.Controls:
                At = Page.Controls;
                ControlRow = 0;
                ControlSlot = 0;
                ControlScroll = 0;
                LastClash = null;
                Core.Audio.PlayBlip();
                break;
            case RootRow.Audio:
                At = Page.Audio;
                Audio = AudioRow.Master;
                Core.Audio.PlayBlip();
                break;
            default:
                return Action.Back;
        }
        return Action.None;
    }

    // --- The mixer --------------------------------------------------------------------

    private Action UpdateAudio()
    {
        if (InputMap.QuitPressed) { At = Page.Root; return Action.None; }

        int count = System.Enum.GetValues<AudioRow>().Length;
        if (InputMap.MenuUp) { Audio = (AudioRow)(((int)Audio - 1 + count) % count); Core.Audio.PlayBlip(); }
        if (InputMap.MenuDown) { Audio = (AudioRow)(((int)Audio + 1) % count); Core.Audio.PlayBlip(); }

        if (Audio == AudioRow.Back)
        {
            if (InputMap.MenuConfirm) At = Page.Root;
            return Action.None;
        }

        if (InputMap.MenuLeft) Nudge(-1);
        if (InputMap.MenuRight) Nudge(+1);
        // Enter on a value row also steps it forward, so the page is usable without arrows.
        if (InputMap.MenuConfirm) Nudge(+1);

        return Action.None;
    }

    private void Nudge(int dir)
    {
        switch (Audio)
        {
            case AudioRow.Mono:
                _settings.MonoAudio = !_settings.MonoAudio;
                break;
            case AudioRow.SoftenLoud:
                _settings.SoftenLoudSounds = !_settings.SoftenLoudSounds;
                break;
            case AudioRow.Master:
                _settings.MasterVolume = Settings.StepVolume(_settings.MasterVolume, dir);
                break;
            default:
                Bus bus = BusOf(Audio);
                _settings.SetVolume(bus, Settings.StepVolume(_settings.VolumeOf(bus), dir));
                break;
        }

        // Straight into the mixer, so a fader is heard as it moves rather than on the way out
        // of the screen. The menu blip that follows this frame's keypress is itself the
        // audition: nudge a slider down and the next one is quieter.
        Core.Audio.ApplySettings(_settings);
    }

    /// <summary>Which bus a slider drives. The two toggles and the master have no bus of
    /// their own and answer <see cref="Bus.Sfx"/>, which nothing asks about.</summary>
    public static Bus BusOf(AudioRow row) => row switch
    {
        AudioRow.Music => Bus.Music,
        AudioRow.Shooting => Bus.Shooting,
        AudioRow.Explosions => Bus.Explosions,
        AudioRow.Enemies => Bus.Enemies,
        AudioRow.Player => Bus.Player,
        _ => Bus.Sfx,
    };

    // --- The controls list ------------------------------------------------------------

    private Action UpdateControls()
    {
        if (InputMap.QuitPressed) { At = Page.Root; return Action.None; }

        int rows = InputActions.All.Length + ControlExtraRows;

        if (InputMap.MenuUp) StepRow(-1, rows);
        if (InputMap.MenuDown) StepRow(+1, rows);

        bool onAction = ControlRow < InputActions.All.Length;

        // Left/right pick which of the two slots the cursor is answering for. Only
        // meaningful on a binding row — RESET and BACK have nothing to hold.
        if (onAction)
        {
            if (InputMap.MenuLeft && ControlSlot != 0) { ControlSlot = 0; Core.Audio.PlayBlip(); }
            if (InputMap.MenuRight && ControlSlot != 1) { ControlSlot = 1; Core.Audio.PlayBlip(); }

            // Delete on a slot clears it. An action with no binding is a legitimate choice —
            // somebody who never uses the smoke dischargers is entitled to have the key back.
            if (InputMap.MenuDelete)
            {
                var a = InputActions.All[ControlRow];
                _settings.Bindings[a] = _settings.Bindings[a].With(ControlSlot, InputCode.None);
                LastClash = null;
                Core.Audio.PlayBlip();
                return Action.None;
            }
        }

        if (!InputMap.MenuConfirm) return Action.None;

        if (ControlRow == ControlBackRow) { At = Page.Root; return Action.None; }

        if (ControlRow == ControlResetRow)
        {
            _settings.Bindings.Reset();
            LastClash = null;
            Core.Audio.PlayBlip();
            return Action.None;
        }

        Capturing = true;
        LastClash = null;
        Core.Audio.PlayBlip();
        return Action.None;
    }

    private void StepRow(int dir, int rows)
    {
        ControlRow = (ControlRow + dir + rows) % rows;
        Core.Audio.PlayBlip();

        // RESET and BACK are drawn under the list rather than in it, so landing on one of
        // them leaves the view exactly where it was — which is what you want when you scroll
        // to the bottom to reset and then come back up.
        if (ControlRow >= InputActions.All.Length) return;

        if (ControlRow < ControlScroll) ControlScroll = ControlRow;

        // Walked forward rather than solved, because how many rows a window holds depends on
        // where it starts — see RowsFrom. Bounded by the list length, so it terminates even
        // if that ever returned something absurd.
        while (ControlRow >= ControlScroll + RowsFrom(ControlScroll)
               && ControlScroll < InputActions.All.Length - 1)
            ControlScroll++;
    }

    /// <summary>
    /// Waiting for a button. Escape backs out with the row untouched — which is why Escape
    /// is the one thing <see cref="Bindings.CapturePressed"/> refuses to hand back, and why
    /// it is not rebindable anywhere in the game.
    /// </summary>
    private void UpdateCapture()
    {
        if (InputMap.QuitPressed) { Capturing = false; return; }

        if (Bindings.CapturePressed() is not { } code) return;

        var action = InputActions.All[ControlRow];
        _settings.Bindings[action] = _settings.Bindings[action].With(ControlSlot, code);

        // Reported, never refused. Two actions on one button is sometimes exactly what a
        // player means — and in a game where Q is three different things depending on the
        // chassis, a screen that argued about it would be wrong more often than right.
        LastClash = _settings.Bindings.Clashes(action);

        Capturing = false;
        Core.Audio.PlayBlip();
    }

    // --- Labels for the renderer ------------------------------------------------------

    public static string RootLabel(RootRow row) => row switch
    {
        RootRow.Controls => "CONTROLS",
        RootRow.Audio => "SOUND",
        _ => "BACK",
    };

    public static string AudioLabel(AudioRow row) => row switch
    {
        AudioRow.Master => "MASTER",
        AudioRow.Music => "MUSIC",
        AudioRow.Shooting => "SHOOTING",
        AudioRow.Explosions => "EXPLOSIONS",
        AudioRow.Enemies => "ENEMIES",
        AudioRow.Player => "PLAYER",
        AudioRow.Mono => "MONO",
        AudioRow.SoftenLoud => "SOFTEN LOUD",
        _ => "BACK",
    };

    public string AudioValue(AudioRow row) => row switch
    {
        AudioRow.Master => Settings.VolumeLabel(_settings.MasterVolume),
        AudioRow.Mono => _settings.MonoAudio ? "ON" : "OFF",
        AudioRow.SoftenLoud => _settings.SoftenLoudSounds ? "ON" : "OFF",
        AudioRow.Back => "",
        _ => Settings.VolumeLabel(_settings.VolumeOf(BusOf(row))),
    };

    /// <summary>Whether a binding row is currently sharing a button with a neighbour in its
    /// own section. The renderer paints both ends of that in warning red.</summary>
    public bool IsClashing(InputAction a) => _settings.Bindings.Clashes(a) != null;
}
