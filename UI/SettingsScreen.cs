using VoidTanks.Core;
using VoidTanks.Input;

namespace VoidTanks.UI;

/// <summary>
/// The controls screen reached from the menu. Spare on purpose: a short list of
/// rows, each a value you cycle with left/right; up/down moves between rows; Back
/// (or Escape) returns to the menu and saves. Mutates the live <see cref="Settings"/>
/// in place so a swap takes effect the instant you drive out of the menu. Owns
/// only selection state — drawing lives in the Renderer.
/// </summary>
public sealed class SettingsScreen
{
    public enum Row
    {
        SwapTurn,
        Movement,
        Fire,
        // The mixer's faders. Separate buses, so the soundtrack and the guns can be set
        // against each other rather than everything moving together.
        Master,
        Sfx,
        Music,
        Mono,
        SoftenLoud,
        Back,
    }

    public enum Action { None, Back }

    private readonly Settings _settings;
    public Row Selected { get; private set; } = Row.SwapTurn;

    public SettingsScreen(Settings settings) => _settings = settings;

    /// <summary>Reads input, mutating settings on a value change. Returns Back when leaving.</summary>
    public Action Update()
    {
        if (InputMap.QuitPressed) return Action.Back;

        int count = System.Enum.GetValues<Row>().Length;
        if (InputMap.MenuUp) Selected = (Row)(((int)Selected - 1 + count) % count);
        if (InputMap.MenuDown) Selected = (Row)(((int)Selected + 1) % count);

        // Left/right cycle the focused row's value. Confirm on Back leaves.
        if (Selected == Row.Back)
        {
            if (InputMap.MenuConfirm) return Action.Back;
            return Action.None;
        }

        if (InputMap.MenuLeft) Cycle(-1);
        if (InputMap.MenuRight) Cycle(+1);
        // Enter on a value row also nudges it forward, so it's usable without arrows.
        if (InputMap.MenuConfirm) Cycle(+1);

        return Action.None;
    }

    private void Cycle(int dir)
    {
        switch (Selected)
        {
            case Row.SwapTurn:
                _settings.SwapTurn = !_settings.SwapTurn;
                break;
            case Row.Movement:
                _settings.Movement = CycleEnum(_settings.Movement, dir);
                break;
            case Row.Fire:
                _settings.Fire = CycleEnum(_settings.Fire, dir);
                break;

            case Row.Master:
                _settings.MasterVolume = Settings.StepVolume(_settings.MasterVolume, dir);
                break;
            case Row.Sfx:
                _settings.SfxVolume = Settings.StepVolume(_settings.SfxVolume, dir);
                break;
            case Row.Music:
                _settings.MusicVolume = Settings.StepVolume(_settings.MusicVolume, dir);
                break;
            case Row.Mono:
                _settings.MonoAudio = !_settings.MonoAudio;
                break;
            case Row.SoftenLoud:
                _settings.SoftenLoudSounds = !_settings.SoftenLoudSounds;
                break;
        }

        // Straight into the mixer, so a fader is heard as it moves rather than on the way
        // out of the screen. The menu blip that follows this frame's keypress is itself the
        // audition: nudge the master down and the next one is quieter.
        if (IsAudio(Selected)) Audio.ApplySettings(_settings);
    }

    /// <summary>Whether a row belongs to the mixer, and so should be applied live.</summary>
    public static bool IsAudio(Row row)
        => row is Row.Master or Row.Sfx or Row.Music or Row.Mono or Row.SoftenLoud;

    private static T CycleEnum<T>(T value, int dir) where T : struct, System.Enum
    {
        var vals = System.Enum.GetValues<T>();
        int i = System.Array.IndexOf(vals, value);
        int n = vals.Length;
        return vals[((i + dir) % n + n) % n];
    }

    // --- Row labels/values for the renderer ---
    public Settings Settings => _settings;

    public static string Label(Row row) => row switch
    {
        Row.SwapTurn => "SWAP TURN L / R",
        Row.Movement => "MOVEMENT",
        Row.Fire => "FIRE KEY",
        Row.Master => "MASTER",
        Row.Sfx => "EFFECTS",
        Row.Music => "MUSIC",
        Row.Mono => "MONO",
        Row.SoftenLoud => "SOFTEN LOUD",
        Row.Back => "BACK",
        _ => "",
    };

    public string Value(Row row) => row switch
    {
        Row.SwapTurn => _settings.SwapLabel,
        Row.Movement => _settings.MovementLabel,
        Row.Fire => _settings.FireLabel,
        Row.Master => Settings.VolumeLabel(_settings.MasterVolume),
        Row.Sfx => Settings.VolumeLabel(_settings.SfxVolume),
        Row.Music => Settings.VolumeLabel(_settings.MusicVolume),
        Row.Mono => _settings.MonoAudio ? "ON" : "OFF",
        Row.SoftenLoud => _settings.SoftenLoudSounds ? "ON" : "OFF",
        _ => "",
    };
}
