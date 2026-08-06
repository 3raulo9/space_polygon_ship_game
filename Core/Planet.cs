using System.Numerics;
using Raylib_cs;

namespace Unrendered.Core;

/// <summary>
/// The five worlds a match can be played on. They share a layout — the same grid, the same
/// city, the same monsters — because what makes one different from another here is the air
/// over it and the pull under it, not the floor plan. A destination is a set of conditions
/// you agree to before you drop, and the star map states them plainly (see
/// <see cref="Planet.Hostiles"/> and friends) so a vote is a decision rather than a colour.
/// </summary>
public enum PlanetId : byte
{
    Solune = 0,
    Verene = 1,
    Thalos = 2,
    Kirene = 3,
    Abysse = 4,
}

/// <summary>
/// Which of the two games a match is. SANDBOX is the world running forever with nothing to
/// finish; DESCENT is the same field with a landing and, eventually, a way to be done with a
/// planet. They deliberately share every mechanic today — the split exists so the clear
/// condition has somewhere to land, and so a player who wants to mess about can say so.
/// </summary>
public enum GameMode : byte
{
    Sandbox = 0,
    Descent = 1,
}

/// <summary>
/// Everything the sky and the physics need to know about a world, resolved for one instant.
/// SOLUNE's turns through the day (see <see cref="Planet.Look"/>); the other four hand back
/// the same values every time they are asked.
/// </summary>
public readonly record struct SkyLook(
    Color Top,
    Color Mid,
    Color Horizon,
    // What geometry dissolves into at the far edge. The *distance* it dissolves over never
    // changes — see Planet.Murk — so this colour is the whole of the difference between a
    // world you can see across and one you cannot.
    Color Fog,
    // 0 = the far field keeps its own haze colour, 1 = it goes to the void. See Planet.Murk.
    float Murk,
    Vector3 LightDir,
    // Height of the sun above the horizon, -1 (under the world) .. 1 (overhead). Below zero
    // it is not drawn. The azimuth is its bearing in radians, so the disc and the shading
    // never disagree about where the light is coming from.
    float SunAltitude,
    float SunAzimuth,
    float MoonAltitude,
    float MoonAzimuth,
    // 0 in full daylight, 1 at the dead of night. Everything that gets worse after dark reads
    // this rather than the clock, so a world without a cycle simply sits at 0 forever.
    float Night);

/// <summary>
/// One destination. Immutable, built once into <see cref="All"/>; a match carries only the
/// <see cref="PlanetId"/> and looks the rest up, so nothing about a world has to cross the
/// wire and two machines can never disagree about how hard a place is.
/// </summary>
public sealed record Planet(
    PlanetId Id,
    string Name,
    string Blurb,
    Color SkyTop,
    Color SkyMid,
    Color SkyHorizon,
    // The colour of this world's haze — the hue distant things take on before they go.
    Color FogColor,
    // How dark the far field goes, 0 (clearest) .. 1 (the far edge is the void). This is the
    // ONLY thing that changes about visibility between worlds: the draw distance is a fixed
    // 24→130 everywhere (Config.FogStart/FogEnd) and always will be. Pulling the distance in
    // per-world was tried and rejected — it makes the horizon lurch when the hour changes, and
    // it makes a busy world cheaper to render than a quiet one, which is exactly backwards.
    // A murky world draws every bit as much geometry; you simply cannot make much of it out.
    float Murk,
    // Multiplier on the craft's fall. Under one the jump-dodge floats and hangs; over one it
    // is a shorter, meaner hop.
    float Gravity,
    // Multiplier on how much wants to kill you — both the population ceiling and how fast the
    // director tops it up.
    float Hostiles,
    // Whether the sun and moon actually move here. True for exactly one world.
    bool HasCycle)
{
    /// <summary>How long a full day takes, in seconds. Six minutes, so a run of any length
    /// sees a couple of nights and neither half outstays its welcome.</summary>
    public const float DayLength = 360f;

    /// <summary>How far a hunter holds off in daylight, and how far it dares to close after
    /// dark. The night figure is inside the range at which a tank reads as a silhouette
    /// rather than a shape, which is the whole point of it.</summary>
    public const float DayStandoff = 40f;
    public const float NightStandoff = 18f;

    /// <summary>What the darkness does to the population on a world that has one.</summary>
    public const float NightHostileBoost = 1.7f;

    /// <summary>
    /// The conditions over this world at <paramref name="phase"/> — a fraction of a day,
    /// 0 at dawn, 0.25 at noon, 0.5 at dusk, 0.75 at midnight. Ignored entirely by the four
    /// worlds whose sky does not move.
    /// </summary>
    public SkyLook Look(float phase)
    {
        if (!HasCycle)
            return new SkyLook(SkyTop, SkyMid, SkyHorizon, Haze(FogColor, Murk), Murk,
                FixedLight, SunAltitude: -1f, SunAzimuth: 0f,
                MoonAltitude: -1f, MoonAzimuth: 0f, Night: 0f);

        return Solune(phase);
    }

    /// <summary>
    /// The colour distant geometry actually fades into: this world's haze, pulled toward the
    /// void by how murky it is. Everything reads this rather than <see cref="FogColor"/>, so
    /// "you cannot see far here" is expressed as the far field going dark rather than as the
    /// far field being cut off — the horizon stays where it is and simply stops giving anything
    /// up. Never all the way to pure black: the void is #05070A and not zero for the same
    /// reason (Doc 02).
    /// </summary>
    private static Color Haze(Color baseFog, float murk) => Lerp(baseFog, Void, Math.Clamp(murk, 0f, 1f));

    /// <summary>#05070A — the same near-black <c>Palette.Void</c> holds, kept here so this file
    /// does not have to reach into the renderer's palette for one colour.</summary>
    private static readonly Color Void = new(5, 7, 10, 255);

    /// <summary>The light everywhere that has no sun to move: down and to one side, the fixed
    /// direction every facet in this game has been lit by since the first milestone.</summary>
    public static readonly Vector3 FixedLight = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.3f));

    // --- SOLUNE's day -----------------------------------------------------------------

    // Three skies, crossfaded by where the sun is. Dusk is deliberately the magenta-purple
    // this game has had over it since the beginning: the signature look is not being retired,
    // it is being explained — that sky was always a sunset, and now the sun sets.
    private static readonly Color DayTop = new(11, 32, 54, 255);
    private static readonly Color DayMid = new(42, 90, 120, 255);
    private static readonly Color DayHorizon = new(158, 194, 206, 255);

    private static readonly Color DuskTop = new(0, 0, 0, 255);
    private static readonly Color DuskMid = new(74, 24, 66, 255);      // Palette.SkyMid
    private static readonly Color DuskHorizon = new(150, 50, 128, 255); // Palette.SkyHorizon

    private static readonly Color NightTop = new(0, 0, 0, 255);
    private static readonly Color NightMid = new(10, 16, 36, 255);
    private static readonly Color NightHorizon = new(30, 42, 80, 255);

    private static readonly Color DayFog = new(10, 20, 24, 255);   // Palette.Fog
    private static readonly Color NightFog = new(5, 7, 14, 255);

    // Visibility. The horizon does NOT move when the sun goes down — the city is still drawn
    // all the way out to 130 units at midnight, it has simply gone almost black out there, so
    // a tower you could read at dusk is a suggestion of a tower an hour later. Pulling the draw
    // distance in instead made the whole world visibly lurch inward as the light changed, which
    // read as a rendering fault rather than as nightfall.
    private const float DayMurk = 0.30f;
    private const float NightMurk = 0.88f;

    private static SkyLook Solune(float phase)
    {
        phase = Frac(phase);

        // The sun rides a full circle; the moon rides the same circle half a day behind, so
        // exactly one of them is up at any time and the handover happens on the horizon.
        float sunAngle = phase * MathF.Tau;
        float sunAlt = MathF.Sin(sunAngle);
        float moonAlt = MathF.Sin(sunAngle + MathF.PI);

        // Night runs on where the sun is, not on the clock, so the transitions land exactly
        // when the disc touches the horizon. A little slack either side of zero keeps dusk
        // from snapping.
        float night = 1f - Math.Clamp((sunAlt + 0.18f) / 0.36f, 0f, 1f);

        // Dusk is a wide band around the horizon crossings, not a moment: the magenta is the
        // look this game is known for and it has to actually sit in the sky for a while rather
        // than flash past between afternoon and dark. Peaks with the sun level and takes about
        // a sixth of the day to fade out either side, at dawn and at dusk alike.
        float dusk = Math.Clamp(1f - MathF.Abs(sunAlt) / 0.55f, 0f, 1f);

        // Day -> night first, then the sunset laid over the top of whichever it is nearer.
        Color top = Lerp(DayTop, NightTop, night);
        Color mid = Lerp(DayMid, NightMid, night);
        Color hor = Lerp(DayHorizon, NightHorizon, night);
        top = Lerp(top, DuskTop, dusk);
        mid = Lerp(mid, DuskMid, dusk);
        hor = Lerp(hor, DuskHorizon, dusk);

        // The light follows whichever body is up. It rakes in nearly level at the horizon and
        // comes down hard at noon, so geometry genuinely lights from the side at dawn.
        float lightAlt = MathF.Max(sunAlt, moonAlt);
        float azimuth = sunAlt >= moonAlt ? sunAngle : sunAngle + MathF.PI;
        var light = Vector3.Normalize(new Vector3(
            -MathF.Sin(azimuth) * 0.9f,
            -(0.25f + 0.95f * MathF.Max(lightAlt, 0f)),
            -MathF.Cos(azimuth) * 0.9f));

        float murk = DayMurk + (NightMurk - DayMurk) * night;

        return new SkyLook(
            top, mid, hor,
            Haze(Lerp(DayFog, NightFog, night), murk),
            murk,
            light,
            sunAlt, sunAngle,
            moonAlt, sunAngle + MathF.PI,
            night);
    }

    /// <summary>
    /// The sky over every screen that is not standing on a world — the title, the hangar, the
    /// chart. Deliberately SOLUNE's <em>dusk</em> laid over the game's original fog: that
    /// magenta band is the look this game has had since its first frame, and the menu is where
    /// it has to keep living. Taking the daylight ramp instead would leave the title screen a
    /// pale blue afternoon, which is nothing like the game behind it.
    ///
    /// The haze is the game's original one at its clearest, because the menu drifts through the
    /// city and a murky far field would swallow the skyline the title is drawn against.
    /// </summary>
    public static SkyLook TitleSky => new(
        DuskTop, DuskMid, DuskHorizon, DayFog, Murk: 0f, FixedLight,
        SunAltitude: -1f, SunAzimuth: 0f, MoonAltitude: -1f, MoonAzimuth: 0f, Night: 1f);

    private static float Frac(float v) { float f = v % 1f; return f < 0f ? f + 1f : f; }

    private static Color Lerp(Color a, Color b, float t) => new(
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t),
        (int)(a.A + (b.A - a.A) * t));

    // --- The catalogue ----------------------------------------------------------------

    /// <summary>The five, in map order. Index is the <see cref="PlanetId"/>.</summary>
    public static readonly IReadOnlyList<Planet> All = new Planet[]
    {
        new(PlanetId.Solune, "SOLUNE", "A SUN AND A MOON. IT MATTERS WHEN YOU LAND.",
            SkyTop: DayTop, SkyMid: DayMid, SkyHorizon: DayHorizon, FogColor: DayFog,
            Murk: DayMurk,
            Gravity: 0.9f, Hostiles: 1.0f, HasCycle: true),

        new(PlanetId.Verene, "VERENE", "THIN PALE AIR. YOU CAN SEE THEM COMING.",
            SkyTop: new Color(0, 0, 0, 255),
            SkyMid: new Color(42, 58, 70, 255),
            SkyHorizon: new Color(126, 154, 166, 255),
            FogColor: new Color(16, 30, 36, 255),
            Murk: 0.08f,
            Gravity: 1.0f, Hostiles: 0.6f, HasCycle: false),

        new(PlanetId.Thalos, "THALOS", "GREEN MURK, AND TOO MUCH LIVING IN IT.",
            SkyTop: new Color(0, 8, 20, 255),
            SkyMid: new Color(18, 50, 34, 255),
            SkyHorizon: new Color(46, 110, 68, 255),
            FogColor: new Color(12, 34, 22, 255),
            Murk: 0.62f,
            Gravity: 1.15f, Hostiles: 1.5f, HasCycle: false),

        new(PlanetId.Kirene, "KIRENE", "HIGH COLD DUST. NOTHING WEIGHS WHAT IT SHOULD.",
            SkyTop: new Color(0, 0, 0, 255),
            SkyMid: new Color(58, 48, 72, 255),
            SkyHorizon: new Color(168, 144, 158, 255),
            FogColor: new Color(26, 24, 36, 255),
            Murk: 0.20f,
            Gravity: 0.7f, Hostiles: 1.0f, HasCycle: false),

        new(PlanetId.Abysse, "ABYSSE", "BARELY A SKY AT ALL. DO NOT STOP MOVING.",
            SkyTop: new Color(0, 0, 0, 255),
            SkyMid: new Color(8, 6, 12, 255),
            SkyHorizon: new Color(42, 16, 48, 255),
            FogColor: new Color(18, 10, 22, 255),
            Murk: 0.80f,
            Gravity: 1.3f, Hostiles: 1.9f, HasCycle: false),
    };

    /// <summary>Looks a destination up, falling back to SOLUNE for a byte off the wire that
    /// names a world this build does not have.</summary>
    public static Planet Get(PlanetId id)
    {
        int i = (int)id;
        return i >= 0 && i < All.Count ? All[i] : All[0];
    }

    /// <summary>The default landing, and what a solo run drops onto unless the map says
    /// otherwise.</summary>
    public static Planet Default => All[0];
}
