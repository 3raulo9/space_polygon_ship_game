using VoidTanks.Acoustics;
using VoidTanks.Input;

namespace VoidTanks.Core;

/// <summary>
/// Everything the player can change about how the game reads them and how it sounds,
/// persisted to a small text file next to the executable so choices survive a relaunch.
///
/// <para>This used to be three curated schemes — a turn swap, a WASD/arrows choice and a
/// three-way fire key — chosen over a rebinding UI because "a couple of curated schemes keep
/// the cold terminal feel and stay unbreakable". They are gone. The game grew five chassis
/// with thirty bindings between them, and by then the presets covered a tenth of the keys and
/// answered none of the questions people actually had. What replaced them is
/// <see cref="Bindings"/>: every one of the three is expressible there, and so is everything
/// they could not say.</para>
/// </summary>
public sealed class Settings
{
    /// <summary>The control layout. Mutated in place by the settings screen, which is why
    /// <see cref="InputMap.Active"/> can hold a reference and see changes at once.</summary>
    public Bindings Bindings { get; } = new();

    /// <summary>The multiplayer nickname the player last set in the lobby room. Empty means
    /// "use the Steam persona name" — the room falls back to that so a first-time player still
    /// has a name over their head without having to type one.</summary>
    public string Nickname { get; set; } = "";

    // --- Audio --------------------------------------------------------------------
    // One fader per bus the mixer actually has. Split this way — rather than one SFX knob —
    // because the four things below are what people ask for separately: the soundtrack under
    // the guns, the guns under the monsters, and the whole loud end down at two in the
    // morning. See VoidTanks.Acoustics.Bus for which cue lands where.

    public float MasterVolume { get; set; } = 1f;
    public float MusicVolume { get; set; } = 1.4f;
    public float ShootingVolume { get; set; } = 1f;
    public float ExplosionsVolume { get; set; } = 1f;
    public float EnemiesVolume { get; set; } = 1f;
    public float PlayerVolume { get; set; } = 1f;

    /// <summary>Folds the two channels together. For anyone playing on one speaker, or
    /// deaf in one ear — without it, half of a game that now puts sounds hard left and
    /// hard right is simply lost.</summary>
    public bool MonoAudio { get; set; }

    /// <summary>Squashes the loud end of the mix toward the quiet end. The engine has real
    /// dynamics now — a boss dying is genuinely far louder than a footstep — and that is not
    /// something everyone wants at three in the morning.</summary>
    public bool SoftenLoudSounds { get; set; }

    /// <summary>Reads a fader by the bus it drives, so the settings screen can walk the
    /// mixer rather than restating it. Buses with no fader of their own answer 1 — they are
    /// governed by the master and nothing else.</summary>
    public float VolumeOf(Bus bus) => bus switch
    {
        Bus.Music => MusicVolume,
        Bus.Shooting => ShootingVolume,
        Bus.Explosions => ExplosionsVolume,
        Bus.Enemies => EnemiesVolume,
        // A menu blip, a pickup chime and the low-shield alarm are sounds your own machine
        // makes at you, so they ride the player's fader rather than having one of their own.
        Bus.Player or Bus.Ui => PlayerVolume,
        _ => 1f,
    };

    public void SetVolume(Bus bus, float v)
    {
        v = Math.Clamp(v, 0f, 1f);
        switch (bus)
        {
            case Bus.Music: MusicVolume = v; break;
            case Bus.Shooting: ShootingVolume = v; break;
            case Bus.Explosions: ExplosionsVolume = v; break;
            case Bus.Enemies: EnemiesVolume = v; break;
            case Bus.Player or Bus.Ui: PlayerVolume = v; break;
        }
    }

    /// <summary>Steps a fader by one notch and keeps it in range. Tenths: fine enough to
    /// find a level, coarse enough to reach either end without holding a key.</summary>
    public static float StepVolume(float v, int dir)
        => MathF.Round(Math.Clamp(v + dir * 0.1f, 0f, 1f) * 10f) / 10f;

    /// <summary>A fader as the settings screen shows it — a bar, not a number, because a
    /// number tells you nothing about how loud it will be.</summary>
    public static string VolumeLabel(float v)
    {
        int filled = (int)MathF.Round(Math.Clamp(v, 0f, 1f) * 10f);
        return filled == 0 ? "OFF" : new string('|', filled).PadRight(10, '.');
    }

    // Config file lives beside the executable so it's found regardless of CWD.
    private static string FilePath =>
        Path.Combine(AppContext.BaseDirectory, "controls.cfg");

    /// <summary>Loads settings from disk, falling back to launch defaults on any problem.</summary>
    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            if (!File.Exists(FilePath)) return s;

            foreach (string raw in File.ReadAllLines(FilePath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq].Trim();
                string val = line[(eq + 1)..].Trim();

                // Bindings own their own key space and get first refusal.
                if (s.Bindings.Load(key, val)) continue;

                switch (key.ToLowerInvariant())
                {
                    case "nickname":
                        s.Nickname = val;
                        break;
                    case "master":
                        if (float.TryParse(val, out float mv)) s.MasterVolume = Math.Clamp(mv, 0f, 1f);
                        break;
                    case "music":
                        if (float.TryParse(val, out float muv)) s.MusicVolume = Math.Clamp(muv, 0f, 1f);
                        break;
                    case "shooting":
                        if (float.TryParse(val, out float shv)) s.ShootingVolume = Math.Clamp(shv, 0f, 1f);
                        break;
                    case "explosions":
                        if (float.TryParse(val, out float exv)) s.ExplosionsVolume = Math.Clamp(exv, 0f, 1f);
                        break;
                    case "enemies":
                        if (float.TryParse(val, out float env)) s.EnemiesVolume = Math.Clamp(env, 0f, 1f);
                        break;
                    case "player":
                        if (float.TryParse(val, out float plv)) s.PlayerVolume = Math.Clamp(plv, 0f, 1f);
                        break;
                    case "mono":
                        s.MonoAudio = val is "1" or "true";
                        break;
                    case "softenloud":
                        s.SoftenLoudSounds = val is "1" or "true";
                        break;

                    // Retired: the three curated schemes and the single SFX fader they sat
                    // beside. Skipped rather than mapped forward — a swap-turn flag cannot be
                    // honoured against a table the player may since have rebound by hand, and
                    // guessing would silently move somebody's controls.
                    case "swapturn":
                    case "movement":
                    case "fire":
                    case "sfx":
                        break;
                }
            }
        }
        catch
        {
            // A corrupt or unreadable file must never block launch — use defaults.
            return new Settings();
        }
        return s;
    }

    /// <summary>Writes settings to disk. Failures are swallowed — saving is best-effort.</summary>
    public void Save()
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("# VOID TANKS controls\n");
            sb.Append($"nickname={Nickname}\n");
            sb.Append($"master={MasterVolume:0.0}\n");
            sb.Append($"music={MusicVolume:0.0}\n");
            sb.Append($"shooting={ShootingVolume:0.0}\n");
            sb.Append($"explosions={ExplosionsVolume:0.0}\n");
            sb.Append($"enemies={EnemiesVolume:0.0}\n");
            sb.Append($"player={PlayerVolume:0.0}\n");
            sb.Append($"mono={(MonoAudio ? 1 : 0)}\n");
            sb.Append($"softenLoud={(SoftenLoudSounds ? 1 : 0)}\n");
            sb.Append("\n# controls — primary , secondary\n");
            foreach (string line in Bindings.Save()) sb.Append(line).Append('\n');

            File.WriteAllText(FilePath, sb.ToString());
        }
        catch
        {
            // Read-only disk / permissions — the session's settings still apply.
        }
    }
}
