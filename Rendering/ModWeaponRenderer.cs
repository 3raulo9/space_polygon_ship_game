using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Entities;
using Unrendered.Modding;

namespace Unrendered.Rendering;

/// <summary>
/// Turns a modded weapon's boxes into something on screen. Two meshes per weapon, built
/// the first time each is asked for: the weapon in its own frame (+Y out along the blade),
/// for the grid and the viewmodel, which both place it with a full transform; and the same
/// boxes turned end for end for the figure's hand, whose limb frame hangs <em>down</em> the
/// arm — so a blade held the way the squads hold theirs runs on out past the fist along the
/// forearm's line.
///
/// <para>Hard-faceted boxes and nothing else, which is the rule every model in this game
/// keeps; a mod gets the same flat shading, banding and fog as the hunters.</para>
/// </summary>
internal static class ModWeaponRenderer
{
    private static readonly Dictionary<ModWeapon, PolyMesh> _own = new();
    private static readonly Dictionary<ModWeapon, PolyMesh> _held = new();

    /// <summary>How much bigger a weapon is drawn lying on the grid than the box sizes say. A
    /// little past the size it is in the hand of a figure drawn at
    /// <see cref="EnemySoldier.Scale"/>: a blade is the thinnest thing on the grid, and at its
    /// true size it vanished into the floor's own lines from any range worth walking.</summary>
    public const float GroundScale = 2.5f;

    /// <summary>How deep the point sinks into the grid, in model metres.</summary>
    private const float Sink = 0.06f;

    public static PolyMesh Mesh(ModWeapon w)
    {
        if (_own.TryGetValue(w, out var m)) return m;
        m = new PolyMesh();
        foreach (var p in w.Model)
            m.AddBoxSpan(p.Color, p.From.X, p.To.X, p.From.Z, p.To.Z, p.From.Y, p.To.Y);
        return _own[w] = m;
    }

    /// <summary>The same weapon for a figure's hand: turned half a revolution about X, so +Y
    /// becomes the limb's −Y (down the arm, past the fist) and the box stays a box.</summary>
    public static PolyMesh HeldMesh(ModWeapon w)
    {
        if (_held.TryGetValue(w, out var m)) return m;
        m = new PolyMesh();
        foreach (var p in w.Model)
            m.AddBoxSpan(p.Color, p.From.X, p.To.X, -p.To.Z, -p.From.Z, -p.To.Y, -p.From.Y);
        return _held[w] = m;
    }

    /// <summary>
    /// Every weapon nobody is holding. Stood on its point with a lean when it has landed;
    /// tumbling end over end about its middle while it is still coming down.
    /// </summary>
    public static void DrawGround(World.World world, Vector3 cameraPos)
    {
        if (world.GroundWeapons.Count == 0) return;
        var eyeXZ = new Vector2(cameraPos.X, cameraPos.Z);

        foreach (var g in world.GroundWeapons)
        {
            if (Mods.Weapon(g.Weapon) is not { } w) continue;
            Vector2 at = Torus.NearestImage(g.Position, eyeXZ);
            float s = GroundScale;
            float half = w.Length * 0.5f;

            Matrix4x4 world_;
            if (g.Landed)
            {
                // Point down: half a turn about X sends the blade under the fist. Then the
                // lean, then which way it leans, then up so the point just bites the grid.
                float lift = (w.Length - Sink) * s * MathF.Cos(g.Lean);
                world_ = Matrix4x4.CreateScale(s)
                       * Matrix4x4.CreateRotationX(MathF.PI)
                       * Matrix4x4.CreateRotationZ(g.Lean)
                       * Matrix4x4.CreateRotationY(g.LeanHeading)
                       * Matrix4x4.CreateTranslation(at.X, lift, at.Y);
            }
            else
            {
                // Falling: spun about its own middle, so it reads as dropped rather than
                // as a sword sliding down an invisible pole.
                world_ = Matrix4x4.CreateTranslation(0f, -half, 0f)
                       * Matrix4x4.CreateScale(s)
                       * Matrix4x4.CreateRotationX(g.Age * 9f)
                       * Matrix4x4.CreateRotationY(g.LeanHeading)
                       * Matrix4x4.CreateTranslation(at.X, g.Height + half * s, at.Y);
            }

            Mesh(w).Draw(world_, cameraPos);

            // A glint up off the hilt every couple of seconds — something catching the light
            // out in the fog, which is the only way a thin blade on a grid of thin lines is
            // ever noticed from further than it can be read.
            if (g.Landed) DrawGlint(at, w, g.Age, cameraPos);
        }
    }

    private static void DrawGlint(Vector2 at, ModWeapon w, float age, Vector3 cameraPos)
    {
        float phase = (age * 0.55f) % 1f;
        if (phase > 0.18f) return;
        float k = MathF.Sin(phase / 0.18f * MathF.PI);

        float dist = Vector2.Distance(at, new Vector2(cameraPos.X, cameraPos.Z));
        float fog = GridRenderer.FogFactor(dist);
        if (fog >= 0.99f) return;

        float top = w.Length * GroundScale + 0.25f;
        Color c = GridRenderer.LerpColor(w.Spark, Color.White, 0.5f);
        c = GridRenderer.LerpColor(c, Atmosphere.FogColor, fog);
        c.A = (byte)(255 * k);
        var foot = new Vector3(at.X, top, at.Y);
        Raylib.DrawLine3D(foot, foot + new Vector3(0f, 1.2f * k, 0f), c);
        Raylib.DrawLine3D(foot + new Vector3(-0.35f * k, 0.5f * k, 0f),
                          foot + new Vector3(0.35f * k, 0.5f * k, 0f), c);
    }
}
