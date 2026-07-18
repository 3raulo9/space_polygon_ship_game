using Raylib_cs;

namespace VoidTanks.Core;

/// <summary>
/// Central sound-effects bank. Loads the game's clips once and plays them by
/// name from anywhere in the sim. Every call is a no-op until <see cref="Init"/>
/// has run against a live audio device, so the headless self-test (which never
/// opens a window or an audio device) can drive the same simulation code without
/// touching Raylib's audio at all.
/// </summary>
public static class Audio
{
    // Guards every play/load call. The self-test leaves this false, so audio is
    // silently skipped; normal boot flips it on after InitAudioDevice succeeds.
    private static bool _enabled;

    private static Sound _blip;       // menu cursor moving between options
    private static Sound _detonation; // a barrel firing — player or enemy shot
    private static Sound _explosion;  // a tank being destroyed (player or enemy)
    private static Sound _hit;        // the player's craft taking a hit
    private static Sound _warning;    // shield crosses the low-health line

    // Assets are copied next to the executable by the .csproj, so a relative
    // path off the working directory resolves at runtime.
    private const string SfxDir = "Assets/Audio/SFX/";

    /// <summary>
    /// Opens the audio device and loads every clip. Call once at startup, after
    /// the window exists. Safe to skip entirely (the sim just stays silent).
    /// </summary>
    public static void Init()
    {
        Raylib.InitAudioDevice();
        _blip = Load("blip.wav");
        _detonation = Load("detonation.wav");
        _explosion = Load("explosion.wav");
        _hit = Load("hit.wav");
        _warning = Load("warning.wav");
        _enabled = true;
    }

    /// <summary>Menu cursor stepping to a new option. Mapped to blip.wav.</summary>
    public static void PlayBlip()
    {
        if (_enabled) Raylib.PlaySound(_blip);
    }

    /// <summary>A shot leaving a barrel — player or enemy. Mapped to detonation.wav.</summary>
    public static void PlayDetonation()
    {
        if (_enabled) Raylib.PlaySound(_detonation);
    }

    /// <summary>A tank being destroyed — player or enemy. Mapped to explosion.wav.</summary>
    public static void PlayExplosion()
    {
        if (_enabled) Raylib.PlaySound(_explosion);
    }

    /// <summary>The player's craft absorbing a hit.</summary>
    public static void PlayHit()
    {
        if (_enabled) Raylib.PlaySound(_hit);
    }

    /// <summary>Low-shield alarm — fired once when crossing the threshold, not per frame.</summary>
    public static void PlayWarning()
    {
        if (_enabled) Raylib.PlaySound(_warning);
    }

    /// <summary>Unloads the clips and closes the device. Mirrors <see cref="Init"/>.</summary>
    public static void Shutdown()
    {
        if (!_enabled) return;
        Raylib.UnloadSound(_blip);
        Raylib.UnloadSound(_detonation);
        Raylib.UnloadSound(_explosion);
        Raylib.UnloadSound(_hit);
        Raylib.UnloadSound(_warning);
        Raylib.CloseAudioDevice();
        _enabled = false;
    }

    private static Sound Load(string file) => Raylib.LoadSound(SfxDir + file);
}
