using System.Diagnostics;
using Raylib_cs;

namespace VoidTanks.Core;

/// <summary>
/// The soundtrack: whatever is sitting in Assets/Audio/Music, played one piece at a
/// time, at long irregular intervals, while the player is out in the world.
///
/// Three things shape the whole design.
///
/// <b>Nothing is registered anywhere.</b> The folder is the track list. Drop a .wav
/// into Assets/Audio/Music and it is in the rotation on the next launch — the .csproj
/// already sweeps <c>Assets\**\*</c> into the output, and this scans the folder at
/// runtime rather than naming clips the way <see cref="Audio"/>'s SFX bank has to.
/// It also re-reads the folder whenever its timestamp changes, so a file dropped
/// straight into the running build's output is picked up without even a restart.
///
/// <b>One piece at a time, always.</b> There is exactly one stream field, and a new
/// track can only be rolled while it is empty. Two pieces cannot overlap because
/// there is nowhere to put the second one — the guarantee is structural rather than a
/// timer someone has to keep in step with the longest file in the folder.
///
/// <b>Every machine hears its own.</b> The clock and the rolls are local — the sim
/// never sees this, nothing about it crosses the wire, and it uses its own
/// <see cref="Random"/> rather than anything the world steps with. In a four-player
/// match, four people are each on their own private schedule: one hears a piece open
/// as they spawn and another hears the same piece twenty minutes later, which is the
/// point. Music that started on everyone at once would read as a scripted event, and
/// the wrong one, since the world it is scoring is not in the same place for any two
/// of them.
/// </summary>
public static class MusicBox
{
    /// <summary>Where the tracks live, relative to the working directory — the same
    /// convention <see cref="Audio"/>'s SFX bank uses.</summary>
    private const string MusicDir = "Assets/Audio/Music";

    /// <summary>Everything raylib's mixer can decode. A file in the folder with any
    /// other suffix (a readme, a project file, a stray .flp) is simply not a track.</summary>
    private static readonly string[] PlayableExtensions =
        [".wav", ".ogg", ".mp3", ".flac", ".qoa", ".xm", ".mod"];

    /// <summary>Level a track settles at. Well under the one-shot cues: this plays for
    /// minutes at a stretch underneath a fight, so it is scenery, not a layer of it.</summary>
    private const float MaxVolume = 0.4f;

    /// <summary>How fast a track swells in and sinks out, per second. Slow enough that
    /// walking out of a match mid-piece is a piece ending rather than a tape stopping.</summary>
    private const float FadeRate = 0.55f;

    /// <summary>Bounds on the silence between pieces, in seconds. Wide on purpose —
    /// the gap is re-rolled every time, so the soundtrack never falls into a cadence
    /// the player can feel coming, and two machines drift apart within one match.</summary>
    private const double MinGap = 60.0;
    private const double MaxGap = 330.0;

    /// <summary>The wait before the <em>first</em> piece of a session, which is drawn
    /// from a shorter window. The full gap can be five and a half minutes, and opening
    /// every run with that much silence would leave most short sessions never hearing
    /// the soundtrack at all.</summary>
    private const double FirstMinGap = 25.0;
    private const double FirstMaxGap = 140.0;

    /// <summary>How long to sit on our hands after a file refuses to load, before
    /// trying whatever else is in the folder.</summary>
    private const double BadTrackRetry = 8.0;

    /// <summary>Seconds after a start during which the "has it finished?" tests are
    /// ignored — see where they are made for why.</summary>
    private const double StartGrace = 0.5;

    /// <summary>This machine's own dice. Never the world's — see the class remarks.</summary>
    private static readonly Random _rng = new();

    /// <summary>...and its own clock, rather than <see cref="Raylib.GetTime"/>, which
    /// the rest of the bank uses. That one is started by InitWindow and reads as a
    /// stopped clock without one, so anything scheduled against it never comes due in a
    /// run that opened the audio device but no window — which is exactly what
    /// <c>--musiccheck</c> does. Nothing about a soundtrack needs the display's clock.</summary>
    private static readonly Stopwatch _clock = Stopwatch.StartNew();

    private static double Now => _clock.Elapsed.TotalSeconds;

    private static string _dir = MusicDir;
    private static string[] _tracks = [];
    private static DateTime _scanned;       // folder timestamp the list was built from

    // The one and only voice. A Music stream rather than a Sound: a Sound is decoded
    // whole into memory up front, and a two-minute stereo .wav is tens of megabytes of
    // it. This decodes off the file as it plays.
    private static Music _music;
    private static bool _loaded;            // a stream is open (playing or fading out)
    private static string _lastPath = "";   // what we heard last, so it can't come round twice

    private static float _volume;
    private static double _began;           // wall clock the current piece started
    private static double _nextRoll;        // wall clock the next piece may begin

    private static bool _ready;

    /// <summary>
    /// Collapses every wait to almost nothing. Set by VOIDTANKS_MUSIC_RUSH=1 and by
    /// <c>--musiccheck</c>: the honest schedule is minutes long, which makes "did the
    /// track I just dropped in get picked up?" an unanswerable question otherwise.
    /// </summary>
    private static bool _rush;

    /// <summary>
    /// Finds the tracks and arms the first roll. Called from <see cref="Audio.Init"/>
    /// once the device is up; like the rest of the bank it stays inert if that never
    /// happened, so the headless self-test touches none of it.
    /// </summary>
    public static void Init()
    {
        // The SFX bank resolves off the working directory, which is the output folder
        // in every normal launch. Fall back to the executable's own folder for the
        // cases where it isn't — a shortcut with a different "start in", mostly.
        if (!Directory.Exists(_dir))
        {
            string beside = Path.Combine(AppContext.BaseDirectory, MusicDir);
            if (Directory.Exists(beside)) _dir = beside;
        }

        Rescan();
        _rush |= Environment.GetEnvironmentVariable("VOIDTANKS_MUSIC_RUSH") == "1";
        _nextRoll = _rush
            ? Now + RushGap
            : Now + FirstMinGap + _rng.NextDouble() * (FirstMaxGap - FirstMinGap);
        _ready = true;
    }

    /// <summary>Every track the folder currently offers, in the order the roll sees
    /// them. Rescans, so it is always what the next pick will actually draw from.</summary>
    public static IReadOnlyList<string> Tracks { get { Rescan(); return _tracks; } }

    /// <summary>The piece sounding right now, or an empty string during a silence.</summary>
    public static string NowPlaying => _loaded ? Path.GetFileName(_lastPath) : "";

    /// <summary>Diagnostics only: turns on <see cref="_rush"/> and brings the next roll
    /// forward, so a check can watch the rotation without sitting through the real gaps.</summary>
    public static void Rush()
    {
        _rush = true;
        _nextRoll = Now + RushGap;
    }

    /// <summary>Diagnostics only: drops the playhead just short of the end of whatever
    /// is sounding, so the real end-of-track path runs without playing the whole piece.
    /// Returns false if nothing is open.</summary>
    public static bool SkipToEnd()
    {
        if (!_loaded) return false;
        float len = Raylib.GetMusicTimeLength(_music);
        Raylib.SeekMusicStream(_music, MathF.Max(0f, len - 0.3f));
        return true;
    }

    /// <summary>
    /// One frame of the soundtrack. <paramref name="wanted"/> is whether the player is
    /// actually out in the world — a menu, the hangar or the bestiary all pass false,
    /// which fades whatever is sounding out and holds the next roll until they are back
    /// in a match. A pause does <em>not</em> pass false: the world freezing is not a
    /// reason for the music to stop, and cutting it there would make the panel feel like
    /// leaving the game rather than stepping back from it.
    /// </summary>
    public static void Service(float dt, bool wanted)
    {
        if (!_ready) return;

        if (_loaded)
        {
            // Ease toward this frame's level and keep the stream's buffers fed. The
            // refill has to happen every frame a stream is open, fading or not.
            _volume = Approach(_volume, wanted ? MaxVolume : 0f, FadeRate * dt);
            Raylib.SetMusicVolume(_music, _volume);
            Raylib.UpdateMusicStream(_music);

            // Done when the piece runs out, or when it has finished fading away
            // because the player left the world. The two end tests are belt and braces:
            // a non-looping stream stops itself when it runs dry, but the played/length
            // comparison catches a decoder that reports the tail differently. Both are
            // held off for a moment after the start so a stream that hasn't reported
            // itself as playing yet on its first serviced frame isn't read as finished.
            bool ended = Now - _began > StartGrace
                && (!Raylib.IsMusicStreamPlaying(_music)
                    || Raylib.GetMusicTimePlayed(_music) >= Raylib.GetMusicTimeLength(_music) - 0.05f);
            if (ended || (!wanted && _volume <= 0.001f))
            {
                Release();
                ScheduleNext();
            }
            return;
        }

        // Silence. Nothing may start until the clock comes round and the player is
        // somewhere that wants music — and, crucially, this branch is unreachable while
        // a piece is open, which is what makes an overlap impossible.
        if (!wanted || Now < _nextRoll) return;
        Begin();
    }

    /// <summary>Picks a piece and starts it, or backs off if the folder has nothing
    /// playable in it (or has stopped having it — a track can be deleted between one
    /// roll and the next).</summary>
    private static void Begin()
    {
        Rescan();
        string? path = Choose();
        if (path == null) { ScheduleNext(); return; }

        Music m;
        try { m = Raylib.LoadMusicStream(path); }
        catch { m = default; }

        if (!Raylib.IsMusicValid(m))
        {
            // Something in the folder isn't a track after all — a truncated file, a
            // codec raylib was built without. Forget it so it can't be rolled again
            // this session, and come back to the rest shortly.
            _tracks = [.. _tracks.Where(t => t != path)];
            _nextRoll = Now + BadTrackRetry;
            return;
        }

        _music = m;
        _music.Looping = false;   // a piece, not a bed: it has to end so the next can be rolled
        _loaded = true;
        _lastPath = path;
        _volume = 0f;             // swell in from nothing rather than arriving at level
        _began = Now;

        Raylib.SetMusicVolume(_music, 0f);
        Raylib.PlayMusicStream(_music);
    }

    /// <summary>
    /// Rolls a track that isn't the one we just heard. With a single file in the folder
    /// there is nothing to exclude and it simply comes round again — the alternative
    /// would be a soundtrack that plays once and then never again.
    /// </summary>
    private static string? Choose()
    {
        int n = _tracks.Length;
        if (n == 0) return null;
        if (n == 1) return _tracks[0];

        // Draw from the n-1 tracks that aren't the last one, then step over the gap
        // where it sat. Cheaper and better-distributed than re-rolling until it differs.
        int skip = Array.IndexOf(_tracks, _lastPath);
        if (skip < 0) return _tracks[_rng.Next(n)];

        int pick = _rng.Next(n - 1);
        if (pick >= skip) pick++;
        return _tracks[pick];
    }

    /// <summary>
    /// Rebuilds the track list, but only when the folder has actually changed since the
    /// last look — this runs before every roll, and re-globbing a directory that nobody
    /// has touched is pure waste.
    /// </summary>
    private static void Rescan()
    {
        try
        {
            if (!Directory.Exists(_dir)) { _tracks = []; return; }

            DateTime stamp = Directory.GetLastWriteTimeUtc(_dir);
            if (_tracks.Length > 0 && stamp == _scanned) return;
            _scanned = stamp;

            // Sorted, so the list is the same on every machine and every launch. The
            // order isn't audible — the choice is random — but a stable list keeps the
            // "not the last one" exclusion pointing at the same file it did a moment ago.
            _tracks = [.. Directory.EnumerateFiles(_dir)
                .Where(f => PlayableExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)];
        }
        catch
        {
            // An unreadable folder is a silent game, not a crashed one.
            _tracks = [];
        }
    }

    /// <summary>Sets the wall clock for the next piece, re-rolled every time.</summary>
    private static void ScheduleNext()
        => _nextRoll = Now + (_rush
            ? RushGap
            : MinGap + _rng.NextDouble() * (MaxGap - MinGap));

    /// <summary>The gap <see cref="_rush"/> substitutes for the real one. Not zero:
    /// pieces should still be separated by a beat of silence, or a rushed rotation
    /// stops telling you anything about how the real one sounds.</summary>
    private const double RushGap = 1.5;

    /// <summary>Stops and frees the current stream, leaving the slot empty.</summary>
    private static void Release()
    {
        if (!_loaded) return;
        Raylib.StopMusicStream(_music);
        Raylib.UnloadMusicStream(_music);
        _loaded = false;
        _volume = 0f;
    }

    /// <summary>Frees whatever is open. Mirrors <see cref="Init"/>, and must run before
    /// the audio device closes.</summary>
    public static void Shutdown()
    {
        Release();
        _ready = false;
    }

    private static float Approach(float v, float target, float step)
        => v < target ? MathF.Min(target, v + step) : MathF.Max(target, v - step);
}
