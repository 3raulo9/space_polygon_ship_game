using System.Numerics;
using Raylib_cs;
using Unrendered.Acoustics;
using Unrendered.Input;

namespace Unrendered.Core;

/// <summary>
/// The controls layout and the mixer's category faders.
///
/// <para>Both arrived in the same pass and for the same reason: the game had thirty things a
/// player might want to move and a settings page that offered three, and sixty cues on one
/// SFX fader. Neither is testable by looking at a screen, and both are exactly the kind of
/// wiring that quietly comes apart — a row added to an enum and not to a table, a cue whose
/// bus nobody set. What is checked here is the wiring, not the taste.</para>
/// </summary>
public static partial class SelfTest
{
    // --- Bindings ---------------------------------------------------------------------

    /// <summary>
    /// A rebinding screen is a promise that nothing changed for anyone who does not open it.
    /// The controls this game shipped with for its whole life were hardcoded, and the table
    /// that replaced them has to reproduce them exactly — so this is a transcription of the
    /// old <c>Settings</c> and <c>InputMap</c>, checked against the new defaults.
    /// </summary>
    private static string? DefaultBindingsMatchTheOldHardcodedKeys()
    {
        var b = new Bindings();

        string? Want(InputAction a, KeyboardKey? primary, MouseButton? mouse, KeyboardKey? secondary)
        {
            var bind = b[a];
            InputCode expectPrimary = mouse.HasValue
                ? InputCode.Mouse(mouse.Value)
                : primary.HasValue ? InputCode.Key(primary.Value) : InputCode.None;
            InputCode expectSecondary = secondary.HasValue
                ? InputCode.Key(secondary.Value) : InputCode.None;

            if (bind.Primary != expectPrimary)
                return $"{a}'s main button is {bind.Primary.Label}, was {expectPrimary.Label}";
            if (bind.Secondary != expectSecondary)
                return $"{a}'s fallback is {bind.Secondary.Label}, was {expectSecondary.Label}";
            return null;
        }

        // The drive keys, with the arrows mirroring them — Settings.ForwardDown and friends.
        if (Want(InputAction.Forward, KeyboardKey.W, null, KeyboardKey.Up) is { } e1) return e1;
        if (Want(InputAction.Back, KeyboardKey.S, null, KeyboardKey.Down) is { } e2) return e2;
        if (Want(InputAction.TurnLeft, KeyboardKey.A, null, KeyboardKey.Left) is { } e3) return e3;
        if (Want(InputAction.TurnRight, KeyboardKey.D, null, KeyboardKey.Right) is { } e4) return e4;

        // Fire was "either control, or the left mouse button"; the grenade was "G or right".
        if (Want(InputAction.Fire, null, MouseButton.Left, KeyboardKey.LeftControl) is { } e5) return e5;
        if (Want(InputAction.Secondary, null, MouseButton.Right, KeyboardKey.G) is { } e6) return e6;

        if (Want(InputAction.Jump, KeyboardKey.Space, null, null) is { } e7) return e7;
        if (Want(InputAction.Hyperspace, KeyboardKey.X, null, null) is { } e8) return e8;
        if (Want(InputAction.Inventory, KeyboardKey.F, null, null) is { } e9) return e9;
        if (Want(InputAction.Scoreboard, KeyboardKey.Tab, null, null) is { } e10) return e10;

        // The kits: the tank's Q/E/R, the spider's legs, the soldier's two hooks, the
        // virus's R/T/Y/U row.
        if (Want(InputAction.TankLurch, KeyboardKey.Q, null, null) is { } e11) return e11;
        if (Want(InputAction.TankSmoke, KeyboardKey.E, null, null) is { } e12) return e12;
        if (Want(InputAction.TankSlug, KeyboardKey.R, null, null) is { } e13) return e13;
        if (Want(InputAction.SpiderPounce, KeyboardKey.Q, null, null) is { } e14) return e14;
        if (Want(InputAction.LeftHook, KeyboardKey.Q, null, null) is { } e15) return e15;
        if (Want(InputAction.RightHook, KeyboardKey.E, null, null) is { } e16) return e16;
        if (Want(InputAction.HighJump, KeyboardKey.Enter, null, KeyboardKey.Space) is { } e17) return e17;
        if (Want(InputAction.Beat, KeyboardKey.W, null, KeyboardKey.Space) is { } e18) return e18;
        if (Want(InputAction.Brake, KeyboardKey.S, null, null) is { } e19) return e19;
        if (Want(InputAction.Slot1, KeyboardKey.R, null, null) is { } e20) return e20;
        if (Want(InputAction.Slot4, KeyboardKey.U, null, null) is { } e21) return e21;

        // Nothing may ship unbound. A row with two empty slots is an action the player has
        // no way of discovering exists.
        foreach (var a in InputActions.All)
            if (b[a].Primary.IsNone)
                return $"{a} ships with no button on it at all";

        return null;
    }

    /// <summary>
    /// Controls have to survive a relaunch, and the file has to survive the next version of
    /// the game. Keyed by the action's name rather than its index, so this also checks that a
    /// config written before a row was inserted still lands on the right rows.
    /// </summary>
    private static string? BindingsRoundTripThroughTheConfig()
    {
        var from = new Bindings();
        from[InputAction.Fire] = new InputBind(InputCode.Key(KeyboardKey.Z),
                                               InputCode.Mouse(MouseButton.Side));
        from[InputAction.TankSmoke] = new InputBind(InputCode.Key(KeyboardKey.KpEnter));
        from[InputAction.Slot3] = InputBind.Unbound;   // deliberately cleared

        var to = new Bindings();
        int taken = 0;
        foreach (string line in from.Save())
        {
            int eq = line.IndexOf('=');
            if (eq <= 0) return $"a saved line has no '=' in it: {line}";
            if (to.Load(line[..eq].Trim(), line[(eq + 1)..].Trim())) taken++;
        }

        if (taken != InputActions.All.Length)
            return $"only {taken} of {InputActions.All.Length} rows were read back";

        foreach (var a in InputActions.All)
            if (to[a] != from[a])
                return $"{a} came back as {to[a].Primary.Label}/{to[a].Secondary.Label}, "
                     + $"not {from[a].Primary.Label}/{from[a].Secondary.Label}";

        // A key with a mouse button in the second slot is the case most likely to be lost in
        // a text format, so it is worth naming rather than trusting to the sweep above.
        if (!to[InputAction.Fire].Secondary.IsMouse)
            return "a mouse button in the fallback slot came back as a key";

        // A line from a build that has since retired a row must be ignored, not thrown.
        if (to.Load("bind.SomeActionWeDeleted", "key:Q"))
            return "an action this build has never heard of was accepted";
        if (to.Load("master", "0.5"))
            return "the bindings claimed a line that belongs to the volumes";

        return null;
    }

    /// <summary>
    /// The clash rule, which is the one piece of judgement on the controls page.
    ///
    /// <para>Q is the tank's lurch, the spider's pounce <em>and</em> the soldier's left hook
    /// on a fresh install, because only one chassis is ever being read. If those counted as
    /// clashes the shipped layout would open painted red and the colour would stop meaning
    /// anything. What must be caught is two things on one button inside a single section —
    /// FIRE and SECONDARY together, say — because that is a control the player has genuinely
    /// lost.</para>
    /// </summary>
    private static string? ClashesAreReportedWithinASectionOnly()
    {
        var b = new Bindings();

        foreach (var a in InputActions.All)
            if (b.Clashes(a) is { } other)
                return $"the shipped layout reports {a} as fighting with {other}";

        // Across sections: the tank's lurch and the soldier's left hook are already both Q
        // out of the box, which is the case the rule exists to permit.
        if (b[InputAction.TankLurch].Primary != b[InputAction.LeftHook].Primary)
            return "this check assumes the tank's lurch and the soldier's left hook share a key";

        // Within one section: put SECONDARY on FIRE's button and both ends must light up.
        b[InputAction.Secondary] = new InputBind(InputCode.Mouse(MouseButton.Left));
        if (b.Clashes(InputAction.Secondary) != InputAction.Fire)
            return "two triggers on the left mouse button were not reported";
        if (b.Clashes(InputAction.Fire) != InputAction.Secondary)
            return "the clash was reported on one row but not on the other";

        // A cleared slot is not a collision with every other cleared slot.
        b.Reset();
        b[InputAction.TankSmoke] = InputBind.Unbound;
        b[InputAction.TankSlug] = InputBind.Unbound;
        if (b.Clashes(InputAction.TankSmoke) != null)
            return "two unbound rows were reported as fighting over nothing";

        return null;
    }

    /// <summary>
    /// The controls list is longer than the screen and its section headings are drawn inline,
    /// so how many rows a window holds depends on where it starts. Two things have to hold for
    /// every possible scroll position: the window must fit in the space above the RESET row,
    /// and it must never be empty — a window that fits nothing is one the cursor can never
    /// scroll out of.
    /// </summary>
    private static string? TheControlsListAlwaysFitsOnScreen()
    {
        var all = InputActions.All;
        const int budget = UI.SettingsScreen.ListBottom - UI.SettingsScreen.ListTop;

        for (int from = 0; from < all.Length; from++)
        {
            int rows = UI.SettingsScreen.RowsFrom(from);
            if (rows < 1) return $"a window starting at row {from} holds nothing";

            // Re-measure what the renderer will actually lay out from this offset.
            int used = 0;
            InputSection? last = null;
            for (int i = from; i < from + rows; i++)
            {
                var section = InputActions.SectionOf(all[i]);
                if (section != last) { used += UI.SettingsScreen.HeadingHeight; last = section; }
                used += UI.SettingsScreen.RowHeight;
            }

            if (used > budget)
                return $"a window starting at row {from} draws {used}px into a {budget}px space";
        }

        // ...and the last row of the list has to be reachable, or the bindings at the bottom
        // of the page could never be changed.
        int lastRow = all.Length - 1;
        int scroll = 0;
        while (lastRow >= scroll + UI.SettingsScreen.RowsFrom(scroll) && scroll < all.Length - 1)
            scroll++;
        if (lastRow < scroll || lastRow >= scroll + UI.SettingsScreen.RowsFrom(scroll))
            return "the last binding on the page cannot be scrolled to";

        return null;
    }

    // --- The mixer's categories --------------------------------------------------------

    /// <summary>
    /// Every cue has to sit on a bus the settings page offers a slider for. The failure this
    /// prevents is silent and permanent: a cue added later defaults to
    /// <see cref="Bus.Sfx"/>, which no fader touches, so the player turns everything down and
    /// that one sound stays exactly as loud as it was.
    /// </summary>
    private static string? EveryCueIsOnACategoryBus()
    {
        var table = CueBank.BuildTable();
        var settings = new Settings();

        foreach (var cue in Enum.GetValues<Cue>())
        {
            if (cue == Cue.None) continue;
            var spec = table[(int)cue];

            if (spec.Bus == Bus.Sfx)
                return $"{cue} is on the ungoverned bus — no slider on the sound page reaches it";

            // ...and the bus it is on has to be one the settings actually drive. A cue routed
            // to Voice would be just as unreachable as one left on Sfx.
            if (spec.Bus == Bus.Voice)
                return $"{cue} is routed to the reserved voice-chat bus";
        }

        // And the sliders have to cover the buses in the other direction: a category with no
        // way to read its own level is a category the screen cannot draw.
        foreach (var bus in new[] { Bus.Music, Bus.Shooting, Bus.Explosions, Bus.Enemies, Bus.Player, Bus.Ui })
        {
            settings.SetVolume(bus, 0.3f);
            if (Math.Abs(settings.VolumeOf(bus) - 0.3f) > 0.001f)
                return $"the {bus} fader does not read back what was written to it";
        }

        return null;
    }

    /// <summary>
    /// The point of splitting the faders: pulling one down must not pull the others with it.
    /// Rendered through the real mixer rather than asserted against the settings object,
    /// because what is being checked is that the bus routing in the cue table and the gains in
    /// <see cref="Audio.ApplySettings"/> are talking about the same thing.
    /// </summary>
    private static string? CategoryFadersAreIndependent()
    {
        var (engine, clip) = Bench();
        engine.Update(EarAt(Vector2.Zero), 1f / 60f);

        // A gunshot and a monster's footfall, both a few metres away and both audible.
        static float Peak(AudioEngine e)
        {
            var buf = new float[512 * Mixer.Channels];
            e.RenderOffline(buf, 512);
            float p = 0f;
            foreach (float s in buf) p = MathF.Max(p, MathF.Abs(s));
            return p;
        }

        float Render(Cue cue, float shooting, float explosions, float enemies, float player)
        {
            engine.StopAll();
            engine.SetBusGain(Bus.Shooting, shooting);
            engine.SetBusGain(Bus.Explosions, explosions);
            engine.SetBusGain(Bus.Enemies, enemies);
            engine.SetBusGain(Bus.Player, player);
            if (engine.Play((int)cue, clip, new Vector2(0f, 10f), 1.6f) == 0)
                return -1f;
            // The mixer ramps a voice's gain across its first block, so the level being
            // measured is the second one.
            Peak(engine);
            return Peak(engine);
        }

        float shotWideOpen = Render(Cue.Detonation, 1f, 1f, 1f, 1f);
        if (shotWideOpen <= 0f) return "a gunshot ten units away was inaudible with everything up";

        float shotWithGunsDown = Render(Cue.Detonation, 0f, 1f, 1f, 1f);
        if (shotWithGunsDown > shotWideOpen * 0.02f)
            return $"the shooting fader did not silence a gunshot ({shotWithGunsDown:0.0000})";

        float shotWithMonstersDown = Render(Cue.Detonation, 1f, 1f, 0f, 1f);
        if (MathF.Abs(shotWithMonstersDown - shotWideOpen) > shotWideOpen * 0.02f)
            return "turning the monsters down also turned the guns down";

        float stepWideOpen = Render(Cue.Footstep, 1f, 1f, 1f, 1f);
        if (stepWideOpen <= 0f) return "a footfall ten units away was inaudible";

        float stepWithMonstersDown = Render(Cue.Footstep, 1f, 1f, 0f, 1f);
        if (stepWithMonstersDown > stepWideOpen * 0.02f)
            return $"the enemies fader did not silence a footfall ({stepWithMonstersDown:0.0000})";

        float stepWithGunsDown = Render(Cue.Footstep, 0f, 1f, 1f, 1f);
        if (MathF.Abs(stepWithGunsDown - stepWideOpen) > stepWideOpen * 0.02f)
            return "turning the guns down also turned the monsters down";

        return null;
    }
}
