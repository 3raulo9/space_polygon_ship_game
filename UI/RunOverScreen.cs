using Unrendered.Core;
using Unrendered.Input;

namespace Unrendered.UI;

/// <summary>
/// The end of a solo run, either way it went.
///
/// <para>Deliberately not a "game over" box. This game can end two ways — a craft erased, or a
/// world cleared — and both used to end in exactly the same place: nowhere. A dead player sat
/// inside their own wreck for ever, and a player who beat all five waves of a DESCENT got a
/// two-word line in the top strip and then also sat there. Escape into the pause panel was the
/// only way out of either. One screen answers both, because they are the same moment: the run
/// is finished and the player has to decide what to do with that.</para>
///
/// <para>The three ways on are the three things a player actually wants after a run, and they
/// are different enough to be worth separating: go again at the same world in the same craft
/// (the run was close and they want it back), rebuild first (the run said the build was
/// wrong — which is the interesting one, since a lost run is usually a lesson about points),
/// or leave. A single "TRY AGAIN" would have quietly picked one of those for them.</para>
///
/// <para>Pure state, like every other screen in <c>UI/</c>: it owns the rows and what the
/// panel should say, and <c>Rendering/RunOverRenderer</c> does the drawing.</para>
/// </summary>
public sealed class RunOverScreen
{
    public enum Action { None, Retry, Hangar, Menu }

    /// <summary>The three rows, in the order they are drawn.</summary>
    public enum Row { Retry, Hangar, Menu }

    /// <summary>How the run ended. A cleared world is not a failure and must not be dressed
    /// as one — same screen, different face.</summary>
    public bool Won { get; private set; }

    /// <summary>The heading: the name of what happened.</summary>
    public string Title { get; private set; } = "UNRENDERED";

    /// <summary>One line under it saying what that means, in the game's own voice.</summary>
    public string Epitaph { get; private set; } = "";

    /// <summary>The run's account of itself: label/value pairs, drawn as a short column. Built
    /// once when the run ends, so nothing behind this screen can change what it says.</summary>
    public IReadOnlyList<(string Label, string Value)> Lines => _lines;
    private readonly List<(string, string)> _lines = new();

    public Row Selected { get; private set; } = Row.Retry;

    public static int RowCount => System.Enum.GetValues<Row>().Length;

    /// <summary>
    /// What the first row is called. "TRY AGAIN" is the wrong thing to say to somebody who
    /// just won — they are not retrying anything, they are going back for another one.
    /// </summary>
    public string RetryLabel => Won ? "GO AGAIN" : "TRY AGAIN";

    public string LabelOf(Row row) => row switch
    {
        Row.Retry => RetryLabel,
        Row.Hangar => "THE HANGAR",
        _ => "BACK TO MENU",
    };

    /// <summary>One line of help under the focused row — what that choice actually keeps.</summary>
    public string HintOf(Row row) => row switch
    {
        Row.Retry => "THE SAME WORLD, THE SAME CRAFT",
        Row.Hangar => "REBUILD, THEN CHOOSE WHERE",
        _ => "LEAVE THE FIELD",
    };

    /// <summary>
    /// Whether a run has ended in the way that earns this screen.
    ///
    /// <para>Two conditions, either of which is enough: the craft is spent (no revives left,
    /// hull gone), or a DESCENT reached its own end — which covers <em>winning</em>, and is why
    /// this is not simply a death check.</para>
    ///
    /// <para>And one that is not negotiable: <paramref name="networked"/> forbids it outright.
    /// A match has nineteen other people in it, a spectator camera to watch them with, and a
    /// host who decides when it is over; a panel offering one of them "TRY AGAIN" while the
    /// others are still fighting would be nonsense, and Escape already gets them out.</para>
    ///
    /// <para><b>Clearing a planet no longer counts.</b> It used to: a Colossus going down was
    /// the end of DESCENT and this screen was what you got. It is the middle now — the arches
    /// take power, the room picks somewhere it has not been, and the crossing carries on. So
    /// only a <em>lost</em> run opens this automatically, and the winning face is reached the
    /// one way that is genuinely an ending: the last arch of a crossing, whose panel reads
    /// IN PROGRESS and whose one row asks for it (see <c>Game.EnterRunOver</c>).</para>
    /// </summary>
    public static bool ShouldOpen(World.World world, bool networked)
    {
        if (networked) return false;
        return world.Player.Spectating || world.Run is { Phase: DescentPhase.Lost };
    }

    /// <summary>
    /// Reads the finished run and settles everything this screen will say. Called once, on the
    /// frame the run ends — after that the world may go on smoking behind the panel without the
    /// numbers on it drifting.
    /// </summary>
    public void Open(World.World world)
    {
        Selected = Row.Retry;
        _lines.Clear();

        Descent? run = world.Run;
        Won = run is { Phase: DescentPhase.Cleared };

        if (Won && run is not null)
        {
            Title = Planet.Get(run.Destination).Name + " IS CLEAR";
            Epitaph = "NOTHING DOWN THERE IS STILL STANDING";
        }
        else
        {
            // The title of the game is also its word for what happens to you. Using it here
            // costs nothing and is the only place in the whole thing where the name is a verb.
            Title = "UNRENDERED";
            Epitaph = run is not null
                ? "THE DESCENT ENDS HERE"
                : "THE GRID KEEPS WHAT IT TAKES";
        }

        _lines.Add(("CRAFT", ClassCatalog.Get(world.Player.Build.Class).Name));
        _lines.Add(("WORLD", Planet.Get(world.Match.Destination).Name));

        if (run is not null)
        {
            // How far down they got. A cleared run says so rather than reading "WAVE 5 OF 5",
            // which is the same fact stated as though it had stopped short.
            _lines.Add(("REACHED", Won ? "ALL FIVE WAVES"
                                       : $"WAVE {run.Wave} OF {Descent.WaveCount}"));
            // What they were still holding when it ended, out of their pack — not what they
            // ever took. A player who carried three across a planet and fed them all to an
            // arch shows none here, and that is right: the arch has them, and this line is
            // about what was in your hands at the end.
            int suns = world.FragmentsOf(world.LocalIndex, Fragment.Sun);
            int moons = world.FragmentsOf(world.LocalIndex, Fragment.Moon);
            if (suns + moons > 0) _lines.Add(("STILL CARRYING", $"{suns} SUN  {moons} MOON"));
        }

        _lines.Add(("DESTROYED", world.KillsOf(world.LocalIndex).ToString()));
        _lines.Add(("TIME ON THE WORLD", Clock(world.RunTime)));
    }

    /// <summary>Seconds as m:ss — a run is minutes long, and a bare float of seconds is not a
    /// thing anybody reads as a length of time.</summary>
    public static string Clock(float seconds)
    {
        int whole = (int)MathF.Max(0f, seconds);
        return $"{whole / 60}:{whole % 60:00}";
    }

    public Action Update()
    {
        if (InputMap.MenuUp) Step(-1);
        if (InputMap.MenuDown) Step(+1);

        // Escape means the same thing here as everywhere else in this game: out, one level.
        // From the end of a run there is only one level left.
        if (InputMap.QuitPressed) { Audio.PlayBlip(); return Action.Menu; }

        if (!InputMap.MenuConfirm) return Action.None;
        Audio.PlayBlip();
        return Selected switch
        {
            Row.Retry => Action.Retry,
            Row.Hangar => Action.Hangar,
            _ => Action.Menu,
        };
    }

    /// <summary>Test hook: move the cursor without a keyboard.</summary>
    public void SelectForTest(Row row) => Selected = row;

    private void Step(int dir)
    {
        int n = RowCount;
        Selected = (Row)((((int)Selected + dir) % n + n) % n);
        Audio.PlayBlip();
    }
}
