using Raylib_cs;

namespace Unrendered.Core;

/// <summary>
/// <c>--musiccheck</c>: proves the soundtrack out without a window and without the
/// honest schedule, which is minutes long and would make this untestable.
///
/// It answers the three questions that matter after dropping a file into
/// Assets/Audio/Music: is my file in the rotation, does anything ever overlap, and can
/// the same piece come round twice running. The first is a listing; the second and
/// third are watched across a run of rolls on <see cref="MusicBox.Rush"/>'s collapsed
/// clock, with each piece skipped to its end after a moment so a full rotation takes
/// seconds instead of a quarter of an hour.
///
/// It makes real sound — briefly, once per roll. That is the point: a check that
/// silently ticked boxes would not catch a file that loads and decodes to nothing.
/// </summary>
public static class MusicCheck
{
    /// <summary>How many pieces to roll before calling it. Enough to catch a repeat
    /// with a two-track folder several times over.</summary>
    private const int Rolls = 8;

    /// <summary>Seconds of each piece to actually hear before skipping to its end.</summary>
    private const double Listen = 1.2;

    private const float Dt = 1f / 60f;

    public static int Run()
    {
        Raylib.InitAudioDevice();

        // Open the mixer's own stream first, exactly as a real launch does.
        //
        // This check used to talk straight to MusicBox with no engine behind it, and that is
        // precisely why it never saw the worst thing the soundtrack has done. raylib's stream
        // buffer size is a global read by the next stream that loads, the mixer sets it to
        // twelve milliseconds for its own callback-fed stream, and a Music stream refilled
        // once per rendered frame cannot live on twelve milliseconds — it tore continuously.
        // A harness that runs a quieter version of the game than the game is not a harness.
        var engine = new Acoustics.AudioEngine(CueBank.BuildTable());
        engine.Open();

        MusicBox.Init();

        var tracks = MusicBox.Tracks;
        Console.WriteLine($"MUSIC FOLDER — {tracks.Count} track(s):");
        foreach (string t in tracks) Console.WriteLine($"  {Path.GetFileName(t)}");
        if (tracks.Count == 0)
        {
            Console.WriteLine("NOTHING TO PLAY — drop a .wav/.ogg/.mp3/.flac into Assets/Audio/Music"
                + " and rebuild, or straight into the built game's copy of that folder.");
            MusicBox.Shutdown();
            engine.Close();
            Raylib.CloseAudioDevice();
            return 1;
        }

        MusicBox.Rush();

        var heard = new List<string>();
        string current = "";
        double playing = 0;      // how long the current piece has been sounding
        bool skipped = false;    // ...and whether its playhead has already been moved on
        int overlaps = 0;        // pieces that began while another was still open

        // A generous ceiling on frames, so a track that somehow never reports itself
        // finished ends the check rather than hanging it.
        for (int f = 0; f < 60 * 60 && heard.Count < Rolls; f++)
        {
            MusicBox.Service(Dt, wanted: true);
            string now = MusicBox.NowPlaying;

            if (now != current)
            {
                // A change straight from one piece to another — with no silence between
                // them — is the overlap this whole design exists to prevent. It cannot
                // happen (there is one stream field, and a roll is unreachable while it
                // is full), so it is checked rather than assumed.
                if (current.Length > 0 && now.Length > 0) overlaps++;

                current = now;
                playing = 0;
                skipped = false;
                if (now.Length > 0)
                {
                    heard.Add(now);
                    Console.WriteLine($"  [{heard.Count}] {now}");
                }
            }
            else if (now.Length > 0)
            {
                playing += Dt;
                // Once only. Seeking every frame would keep dragging the playhead back
                // to just before the end, and the piece would never actually get there.
                if (playing > Listen && !skipped) skipped = MusicBox.SkipToEnd();
            }

            Thread.Sleep(4);   // let the mixer's own thread actually fill the device
        }

        // No piece may follow itself. With one track in the folder there is nothing to
        // alternate with, so the rule doesn't apply and isn't checked.
        int repeats = 0;
        if (tracks.Count > 1)
            for (int i = 1; i < heard.Count; i++)
                if (heard[i] == heard[i - 1]) repeats++;

        MusicBox.Shutdown();
        engine.Close();
        Raylib.CloseAudioDevice();

        Console.WriteLine();
        Console.WriteLine($"rolled     {heard.Count}/{Rolls}");
        Console.WriteLine($"overlaps   {overlaps}");
        Console.WriteLine($"repeats    {repeats}" + (tracks.Count > 1 ? "" : "  (n/a — one track)"));

        bool ok = heard.Count == Rolls && overlaps == 0 && repeats == 0;
        Console.WriteLine(ok ? "MUSIC OK" : "MUSIC FAILED");
        return ok ? 0 : 1;
    }
}
