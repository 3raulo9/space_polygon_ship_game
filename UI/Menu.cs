using Unrendered.Core;
using Unrendered.Input;

namespace Unrendered.UI;

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
        /// <summary>The endless one: the world running for as long as you can stand in it.</summary>
        StartSandbox,
        /// <summary>The one with somewhere to go: the same field, but landed on deliberately
        /// and — eventually — finished with.</summary>
        StartDescent,
        StartMultiplayer,
        OpenSettings,
        OpenTestScreen, // the secret hatch — wired, but goes nowhere yet
        /// <summary>F4 three times: the creature and vehicle creator. A developer's bench,
        /// so it is knocked for rather than listed.</summary>
        OpenCreator,
        Quit,
    }

    /// <summary>
    /// The entries, in the order they are listed <em>and</em> the order the cursor walks them
    /// — one list for both, so the two can never disagree again (they did: the screen said
    /// MULTIPLAYER then SETTINGS while the cursor went to SETTINGS first).
    /// </summary>
    public enum Item
    {
        /// <summary>Listed first because it is the game with an intent behind it. SANDBOX sits
        /// under it as the thing you drop to when you would rather just drive.</summary>
        Descent,
        Sandbox,
        Multiplayer, // host or join a match; a separate mode, not a flavour of the solo run
        Settings,
    }

    /// <summary>What each entry says on the screen.</summary>
    public static string Label(Item item) => item switch
    {
        Item.Descent => "DESCENT",
        Item.Sandbox => "SANDBOX",
        Item.Multiplayer => "MULTIPLAYER",
        _ => "SETTINGS",
    };

    /// <summary>How long Escape has to be held on this screen before the game closes. A
    /// press does nothing but start the count: one stray Escape too many on the way back
    /// out of a match used to drop the player to the desktop.</summary>
    public const float QuitHoldSeconds = 2.5f;

    /// <summary>How far through that hold the player is, 0..1.</summary>
    public float QuitProgress => Math.Clamp(_quitHeld / QuitHoldSeconds, 0f, 1f);

    /// <summary>How much of the QUITTING notice is showing, 0..1: fades in as the hold
    /// begins and back out when it is let go of, rather than blinking off.</summary>
    public float QuitShown { get; private set; }

    private float _quitHeld;
    private bool _quitArmed;
    private double _lastUpdate = double.NegativeInfinity;

    /// <summary>Capture harness: UNRENDERED_CAPTURE_QUIT=&lt;seconds&gt; holds the screen that far
    /// into a held Escape, so the notice can be photographed without a hand on the key.</summary>
    private readonly float? _stagedHold =
        float.TryParse(Environment.GetEnvironmentVariable("UNRENDERED_CAPTURE_QUIT"),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float held) ? held : null;

    public Menu() => Stage();

    private bool Stage()
    {
        if (_stagedHold is not { } held) return false;
        _quitHeld = held;
        QuitShown = 1f;
        return true;
    }

    /// <summary>
    /// One frame of the hold. Counts only a hold that <em>began</em> on this screen — an
    /// Escape still down from backing out of the hangar or the settings is not a request to
    /// leave the game — and starts again from nothing the moment the key comes up. Returns
    /// true on the frame the hold reaches <see cref="QuitHoldSeconds"/>.
    /// </summary>
    internal bool StepQuitHold(bool pressed, bool held, float dt)
    {
        if (pressed) _quitArmed = true;
        if (_quitArmed && held)
        {
            _quitHeld += dt;
            QuitShown = MathF.Min(1f, QuitShown + dt / QuitFadeIn);
        }
        else
        {
            _quitArmed = false;
            _quitHeld = 0f;
            QuitShown = MathF.Max(0f, QuitShown - dt / QuitFadeOut);
        }

        if (_quitHeld < QuitHoldSeconds) return false;
        _quitArmed = false;
        _quitHeld = 0f;
        QuitShown = 0f;
        return true;
    }

    private const float QuitFadeIn = 0.6f;
    private const float QuitFadeOut = 0.25f;

    /// <summary>Every entry is live now that multiplayer has somewhere to go.</summary>
    public static bool IsSelectable(Item item) => true;

    public Item Selected { get; private set; } = Item.Descent;

    private double _f4First = double.NegativeInfinity;
    private int _f4Count;

    /// <summary>
    /// Reads input and returns the action the loop should take. Movement skips
    /// the disabled Multiplayer entry so the cursor never rests on a dead option.
    /// </summary>
    public Action Update()
    {
        // A frame or more away from this screen and the hold is void: whatever Escape was
        // doing while the menu was not being read, it was not holding it.
        if (Stage()) return Action.None;

        double clock = Raylib_cs.Raylib.GetTime();
        if (clock - _lastUpdate > 0.25) { _quitArmed = false; _quitHeld = 0f; QuitShown = 0f; }
        _lastUpdate = clock;

        if (StepQuitHold(InputMap.QuitPressed, InputMap.QuitHeld, Raylib_cs.Raylib.GetFrameTime()))
            return Action.Quit;

        // Secret keybind ('L' position) — undocumented test screen.
        if (InputMap.SecretTestPressed) return Action.OpenTestScreen;

        // Three F4s inside a second and a half. Each press has to land within the window of
        // the first, so three slow taps spread over a minute do not add up to a knock.
        if (Raylib_cs.Raylib.IsKeyPressed(Raylib_cs.KeyboardKey.F4))
        {
            double now = Raylib_cs.Raylib.GetTime();
            if (now - _f4First > 1.5) { _f4First = now; _f4Count = 0; }
            if (++_f4Count >= 3)
            {
                _f4Count = 0;
                _f4First = double.NegativeInfinity;
                return Action.OpenCreator;
            }
        }

        if (InputMap.MenuUp) MoveSelection(-1);
        if (InputMap.MenuDown) MoveSelection(+1);

        if (InputMap.MenuConfirm)
        {
            switch (Selected)
            {
                case Item.Descent: return Action.StartDescent;
                case Item.Sandbox: return Action.StartSandbox;
                case Item.Settings: return Action.OpenSettings;
                case Item.Multiplayer: return Action.StartMultiplayer;
            }
        }

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
            if (IsSelectable((Item)i))
            {
                Selected = (Item)i;
                Audio.PlayBlip(); // only when the cursor truly lands on a new option
                return;
            }
        }
    }
}
