using System.Globalization;

namespace VoidTanks.Acoustics;

/// <summary>
/// Where a voice is routed before it reaches the master. Buses exist so the player can turn
/// the music down without turning the guns down, and so one category can be ducked under
/// another without touching individual voices.
///
/// <para>The engine attaches no meaning to any of these beyond "sum here, then apply this
/// gain". Which cue belongs on which bus is the <em>game's</em> statement, made once in
/// <see cref="VoidTanks.Core.CueBank.BuildTable"/>; the only bus the mixer treats specially
/// is <see cref="Ui"/>, and that is a signal-path decision rather than a taxonomic one.</para>
/// </summary>
public enum Bus : byte
{
    /// <summary>Anything the world makes that no category below has claimed. Governed by the
    /// master fader alone. Every cue the game ships is routed explicitly, so in practice this
    /// carries only cues added since the last time somebody looked at the table — audible and
    /// slightly ungoverned, which is the right way for an oversight to present itself.</summary>
    Sfx = 0,
    /// <summary>Menus, the inventory, the cursor. Never spatial, never occluded, never
    /// ducked — a click that goes quiet because a boss is roaring feels broken. Follows the
    /// player fader: a menu blip is a sound your own machine makes at you.</summary>
    Ui = 1,
    /// <summary>The soundtrack. Not mixed here (it streams through raylib), but it owns a
    /// bus gain so the same fader and the same ducking reach it.</summary>
    Music = 2,
    /// <summary>Reserved for player voice. Nothing routes here yet — the bus and the
    /// spatialisation path exist so proximity chat drops in without a rewrite.</summary>
    Voice = 3,

    // --- The four the settings screen's sliders name ---------------------------------
    // Split by what a player would point at and say "that, less of that", which is not the
    // same axis as who made the noise. Gunfire is one texture whoever is holding the gun;
    // a monster is a presence whether or not it is shooting.

    /// <summary>Every gun in the game leaving its barrel, on any side. One texture, one
    /// fader — and it has to be, since the cue for a cannon firing does not know whether the
    /// hull under it is a player's.</summary>
    Shooting = 4,
    /// <summary>Ordnance arriving, craft coming apart, the city failing. The loud end of the
    /// mix, and the one people most often want less of.</summary>
    Explosions = 5,
    /// <summary>What the monsters are and do, short of firing and dying: footsteps, calls,
    /// jaws, the beds they hum on. Turn this down and the field goes quiet without the
    /// firefight losing its shape.</summary>
    Enemies = 6,
    /// <summary>Your own craft and your own panel — the wind past it, the cables, the hits it
    /// takes, the chime when you collect something. Everything whose reference point is the
    /// player rather than a place.</summary>
    Player = 7,
}

/// <summary>
/// Everything the engine needs to know about one <em>kind</em> of sound: how far it
/// carries, how sharply it fades, how many of it may sound at once, and which of the
/// spatial treatments apply to it.
///
/// This is the whole reason the sim no longer computes volumes. A cue used to arrive as
/// "play this at 0.3" — a number some call site invented — and forty of the fifty cues
/// never arrived with a number at all, which is why the game sounded like a stack of mp3s.
/// Now a cue arrives as "this happened, there", and the row below decides the rest.
/// </summary>
public readonly record struct SoundSpec(
    /// <summary>Where it is routed.</summary>
    Bus Bus,
    /// <summary>Level at the source, before any distance work. This is the one place a
    /// cue's loudness relative to its neighbours is decided.</summary>
    float Gain,
    /// <summary>Inside this radius the cue plays at full <see cref="Gain"/>. Keeps a sound
    /// that happens on your own hull from being modulated by sub-metre wobble.</summary>
    float MinDistance,
    /// <summary>Beyond this the cue is silent and is never even given a voice. Also the
    /// radius the pan, absorption and reverb curves are measured against.</summary>
    float MaxDistance,
    /// <summary>The exponent on the fade. 1 is a straight line; 2 falls off fast and near
    /// (a rifle); 0.5 stays present most of the way out (a boss, a collapse).</summary>
    float Rolloff,
    /// <summary>Who survives when the pool is full. A boss death outranks a footstep.</summary>
    byte Priority,
    /// <summary>How many of this cue may sound at once. Twenty players firing rifles is a
    /// wall of noise unless somebody says four.</summary>
    byte MaxInstances,
    /// <summary>False for anything that belongs in the player's head rather than in the
    /// world: menu clicks, the low-shield alarm, a pickup chime. Centred, unattenuated,
    /// unoccluded.</summary>
    bool Spatial,
    /// <summary>Whether the city can get in the way of it.</summary>
    bool Occludes,
    /// <summary>How much of it is fed to the room. Percussive, transient things want a lot
    /// (that tail is what tells you the size of the space); sustained beds want almost
    /// none or the mix turns to soup.</summary>
    float ReverbSend,
    /// <summary>How hard distance dulls it, 0..1. Broadband cracks lose their top quickly;
    /// something already low and rolling barely changes.</summary>
    float AirAbsorb,
    /// <summary>Random pitch spread, ±fraction. Stops repeated cues sounding mechanical.</summary>
    float PitchJitter,
    /// <summary>Whether the sound takes time to arrive. On for the big, distant-by-nature
    /// events — a detonation across the map should flash before it booms.</summary>
    bool TravelDelay
)
{
    /// <summary>A sensible world sound, for anything the table has not been taught about
    /// yet. Deliberately unremarkable rather than silent: a new cue should be audible and
    /// slightly wrong, not missing.</summary>
    public static readonly SoundSpec Default = new(
        Bus.Sfx, Gain: 0.8f, MinDistance: 6f, MaxDistance: 170f, Rolloff: 1.4f,
        Priority: 100, MaxInstances: 6, Spatial: true, Occludes: true,
        ReverbSend: 0.3f, AirAbsorb: 0.6f, PitchJitter: 0.03f, TravelDelay: false);

    /// <summary>A sound with no position — menus, alarms, anything that happens to
    /// <em>you</em> rather than somewhere.</summary>
    public static readonly SoundSpec Flat = new(
        Bus.Ui, Gain: 0.9f, MinDistance: 0f, MaxDistance: 0f, Rolloff: 1f,
        Priority: 200, MaxInstances: 4, Spatial: false, Occludes: false,
        ReverbSend: 0f, AirAbsorb: 0f, PitchJitter: 0f, TravelDelay: false);
}

/// <summary>
/// The cue table: one <see cref="SoundSpec"/> per cue id, plus the names those ids answer
/// to so an override file can address them.
///
/// The engine never learns what a "Crab-Core" is. The game hands it a table at boot and
/// from then on speaks in integers — which is what makes this whole folder droppable into
/// another project.
/// </summary>
public sealed class SpecTable
{
    private readonly SoundSpec[] _rows;
    private readonly string[] _names;

    public SpecTable(int count)
    {
        _rows = new SoundSpec[count];
        _names = new string[count];
        Array.Fill(_rows, SoundSpec.Default);
        for (int i = 0; i < count; i++) _names[i] = "";
    }

    public int Count => _rows.Length;

    public SoundSpec this[int id]
        => (uint)id < (uint)_rows.Length ? _rows[id] : SoundSpec.Default;

    public string NameOf(int id) => (uint)id < (uint)_names.Length ? _names[id] : "";

    public void Set(int id, string name, SoundSpec spec)
    {
        if ((uint)id >= (uint)_rows.Length) return;
        _rows[id] = spec;
        _names[id] = name;
    }

    /// <summary>The furthest any cue in the table carries. The engine uses it to size the
    /// travel-delay line and to answer "is this worth a voice at all" cheaply.</summary>
    public float LongestReach
    {
        get
        {
            float far = 0f;
            foreach (var r in _rows) if (r.MaxDistance > far) far = r.MaxDistance;
            return far;
        }
    }

    /// <summary>
    /// Folds an override file over the table. The format is one field per line —
    /// <c>CueName.field = value</c> — because the point of it is to change one number and
    /// hear the difference, not to restate the row. Unknown names and unknown fields are
    /// skipped in silence: the file is a tuning aid, and a typo in it must never be able to
    /// take the game's audio down.
    /// </summary>
    /// <returns>How many fields were actually applied.</returns>
    public int ApplyOverrides(IEnumerable<string> lines)
    {
        int applied = 0;
        foreach (string raw in lines)
        {
            string line = raw;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            line = line.Trim();
            if (line.Length == 0) continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim();
            string val = line[(eq + 1)..].Trim();

            int dot = key.LastIndexOf('.');
            if (dot <= 0) continue;
            string cue = key[..dot].Trim();
            string field = key[(dot + 1)..].Trim();

            int id = -1;
            for (int i = 0; i < _names.Length; i++)
                if (string.Equals(_names[i], cue, StringComparison.OrdinalIgnoreCase)) { id = i; break; }
            if (id < 0) continue;

            if (TryApply(id, field, val)) applied++;
        }
        return applied;
    }

    private bool TryApply(int id, string field, string val)
    {
        var r = _rows[id];
        bool ok = true;

        bool Num(out float f)
            => float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f);
        bool Flag() => val is "1" or "true" or "TRUE" or "on" or "yes";

        switch (field.ToLowerInvariant())
        {
            case "gain": if (Num(out var g)) r = r with { Gain = g }; else ok = false; break;
            case "min": case "mindistance":
                if (Num(out var mn)) r = r with { MinDistance = mn }; else ok = false; break;
            case "max": case "maxdistance":
                if (Num(out var mx)) r = r with { MaxDistance = mx }; else ok = false; break;
            case "rolloff": if (Num(out var ro)) r = r with { Rolloff = ro }; else ok = false; break;
            case "priority":
                if (Num(out var pr)) r = r with { Priority = (byte)Math.Clamp(pr, 0, 255) }; else ok = false; break;
            case "max_instances": case "maxinstances": case "cap":
                if (Num(out var ci)) r = r with { MaxInstances = (byte)Math.Clamp(ci, 1, 64) }; else ok = false; break;
            case "reverb": case "reverbsend":
                if (Num(out var rv)) r = r with { ReverbSend = Math.Clamp(rv, 0f, 1f) }; else ok = false; break;
            case "air": case "airabsorb":
                if (Num(out var ab)) r = r with { AirAbsorb = Math.Clamp(ab, 0f, 1f) }; else ok = false; break;
            case "jitter": case "pitchjitter":
                if (Num(out var pj)) r = r with { PitchJitter = Math.Clamp(pj, 0f, 1f) }; else ok = false; break;
            case "spatial": r = r with { Spatial = Flag() }; break;
            case "occludes": r = r with { Occludes = Flag() }; break;
            case "delay": case "traveldelay": r = r with { TravelDelay = Flag() }; break;
            default: ok = false; break;
        }

        if (ok) _rows[id] = r;
        return ok;
    }
}
