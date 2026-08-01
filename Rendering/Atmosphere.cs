using System.Numerics;
using Raylib_cs;
using Unrendered.Core;

namespace Unrendered.Rendering;

/// <summary>
/// The air this frame is being drawn through: how far you can see, what colour things
/// dissolve into, what the sky does above the horizon and which way the light comes from.
///
/// Deliberately static, and the one place in this codebase where that is the right shape.
/// Fog and light are read from the deepest, hottest loops in the renderer — every facet of
/// every mesh, every cell of the floor — and threading a parameter down to them would touch
/// several hundred call sites to say the same thing every time. There is exactly one screen
/// and exactly one world being drawn into it, so there is exactly one atmosphere.
///
/// <see cref="Adopt"/> is called once a frame, before anything is drawn, from whatever owns
/// the view: the world pushes its planet and its hour, the menus push the default. Nothing
/// in the simulation reads this — gravity, spawn rates and how close a hunter dares to come
/// live on the <see cref="World.World"/> and its entities, so two worlds in a headless test
/// never fight over them.
/// </summary>
public static class Atmosphere
{
    /// <summary>The look in force. Starts as <see cref="Planet.TitleSky"/> — the magenta the
    /// game has always worn — which is what every screen with no world behind it is drawn
    /// under: the title, the hangar, the chart.</summary>
    public static SkyLook Look { get; private set; } = Planet.TitleSky;

    /// <summary>
    /// Where geometry begins and finishes dissolving. Fixed for the whole game, on every world
    /// and at every hour — a world you cannot see across says so by the far field going dark
    /// (<see cref="FogColor"/>), never by the horizon moving in. Per-world draw distances were
    /// built and then taken out again: the horizon visibly lurched as SOLUNE's light changed,
    /// which reads as the renderer failing rather than as night falling.
    /// </summary>
    public static float FogStart => Config.FogStart;
    public static float FogEnd => Config.FogEnd;

    /// <summary>What the far edge dissolves into. This is where a world's visibility actually
    /// lives — VERENE's is a pale haze you can still pick a tower out of, ABYSSE's is very
    /// nearly the void.</summary>
    public static Color FogColor => Look.Fog;

    public static Vector3 LightDir => Look.LightDir;

    /// <summary>Install the conditions over a world at an hour of its day.</summary>
    public static void Adopt(Planet planet, float phase) => Look = planet.Look(phase);

    public static void Adopt(in SkyLook look) => Look = look;

    /// <summary>Back to the title sky — for the screens that are not standing on a planet at
    /// all, and for a test that has just finished pretending to.</summary>
    public static void Reset() => Look = Planet.TitleSky;
}
