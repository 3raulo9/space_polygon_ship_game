using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.World;

namespace Unrendered.Rendering;

/// <summary>
/// Everything a gate looks like: the panel on its leg, the five recesses strung along its span,
/// a fragment riding the rail between them, the column of light that says which arcs have power
/// in them, and finally the thing that opens underneath.
///
/// <para>Nothing here draws the arc itself — that is <see cref="StructureMeshes.Arch"/> and it
/// has been standing on the grid since the drop. This draws only the difference between an arc
/// and a way off the planet, which for most of a run is one small dead box on a leg.</para>
/// </summary>
public sealed class ArchRenderer
{
    // The two keys, built once. Same call the pack icons and the world pickups use, so a
    // fragment seated in a socket is recognisably the object the player carried there.
    private readonly PolyMesh _sun = Meshes.Fragment(Palette.SunStone, Palette.SunBreak, hollow: false);
    private readonly PolyMesh _moon = Meshes.Fragment(Palette.MoonStone, Palette.MoonBreak, hollow: true);

    /// <summary>How high the column reaches. Past the fog's far plane on purpose: the whole job
    /// of it is to be visible from anywhere on a 400-unit torus, and a column that ends inside
    /// the murk is a column you can only see once you have already found the arch.</summary>
    private const float ColumnHeight = 260f;

    /// <summary>How wide at the base, and how much it narrows going up. A shaft that tapers
    /// reads as light; a straight tube reads as a wall.</summary>
    private const float ColumnRadius = 3.4f;

    public void Draw(World.World world, Vector3 cameraPos, Vector2 eyeXZ, float time)
    {
        if (world.Gates.Count == 0) return;

        foreach (var gate in world.Gates)
        {
            // The torus. Everything below is placed off the gate's own stored position, so it
            // has to be shifted onto whichever image of the world the camera is looking at —
            // otherwise a gate near the seam draws its column four hundred units away.
            Vector2 shift = Torus.NearestImage(gate.Position, eyeXZ) - gate.Position;

            DrawPanel(gate, shift, time);
            DrawSockets(gate, cameraPos, shift, time);
            DrawColumn(gate, shift, time);
            DrawMouth(gate, shift, time);
        }
    }

    // --- The panel ---------------------------------------------------------------------

    /// <summary>
    /// The box on the right-hand leg. This is the whole interface: dark for five waves, lit
    /// when the Colossus falls, and the one on the claimed gate stays lit while the others go
    /// out. Small — it is a panel on a building, and the building is the landmark.
    /// </summary>
    private static void DrawPanel(Arch gate, Vector2 shift, float time)
    {
        Vector3 p = gate.PanelPoint();
        p.X += shift.X; p.Z += shift.Y;

        float s = gate.Scale;
        var size = new Vector3(1.9f * s, 2.6f * s, 1.0f * s);

        // The housing, always there. Dead metal, the arch's own dark stone — for most of a run
        // this is all there is to see, and it is meant to look like a fixture nobody has
        // touched in a very long time rather than like a prize.
        Raylib.DrawCubeV(p, size, Palette.ArchDead);
        Raylib.DrawCubeWiresV(p, size, Palette.ArchLive);

        if (gate.State == Arch.Phase.Dark) return;

        // The face, once there is power behind it. It breathes rather than holding steady: at
        // three hundred units a static light is a pixel and a pulsing one is a signal.
        float beat = 0.62f + 0.38f * MathF.Sin(time * 2.6f);
        Color face = gate.State == Arch.Phase.Lit
            ? Tint(Palette.BatteryCore, beat)
            // A claimed gate stops flashing for attention — it has been found. It holds a
            // steady light instead, which is the visual difference between "come here" and
            // "this is the one".
            : Palette.BatteryCore;

        // Stood a hair proud of the housing on the outward face so it is not z-fighting the
        // box it sits on.
        Vector2 out_ = gate.Along;
        var facePos = new Vector3(p.X + out_.X * size.X * 0.55f, p.Y,
                                  p.Z + out_.Y * size.X * 0.55f);
        Raylib.DrawCubeV(facePos, new Vector3(0.3f * s, 1.7f * s, 0.7f * s), face);
    }

    // --- The span ----------------------------------------------------------------------

    /// <summary>
    /// The five recesses and whatever is sitting in them, plus the fragment currently riding
    /// the rail. Drawn along the same half-ellipse the arc's beams are built from, so a seated
    /// fragment sits on the stonework rather than beside it.
    /// </summary>
    private void DrawSockets(Arch gate, Vector3 cameraPos, Vector2 shift, float time)
    {
        if (gate.State == Arch.Phase.Dark) return;

        for (int i = 0; i < Arch.SocketCount; i++)
        {
            Vector3 at = gate.SocketPoint(i);
            at.X += shift.X; at.Z += shift.Y;
            Fragment seated = gate.SocketAt(i);

            if (seated == Fragment.None)
            {
                // An empty recess. Deliberately drawn even when nothing is in it and even
                // before the chart has closed: five obviously empty holes is how a player who
                // has never seen an arch open works out what it wants without being told.
                //
                // Which only works if they can be seen. The first build drew them as a dark
                // cube with a dimmed wire edge and, fifteen units up a dark span at 320×240,
                // they were invisible — a player looking at a lit arch saw one lit socket and
                // four patches of stonework. So: a bigger box, a bright pulsing edge, and a
                // small light sitting in the recess so the empty ones read as sockets waiting
                // rather than as nothing at all.
                float dim = 0.55f + 0.45f * MathF.Sin(time * 2.2f + i * 1.3f);
                var size = new Vector3(2.4f * gate.Scale);
                Raylib.DrawCubeV(at, size, Palette.ArchDead);
                Raylib.DrawCubeWiresV(at, size, Tint(Palette.ArchSocket, dim));
                Raylib.DrawSphereEx(at, 0.5f * gate.Scale, 6, 6,
                    Fade(Palette.ArchSocket, (int)(150 * dim)));
                continue;
            }

            // Seated. The key itself, turning slowly in its housing — the same mesh that was
            // lying on the grid an hour ago.
            PolyMesh mesh = seated == Fragment.Sun ? _sun : _moon;
            mesh.Draw(new Vector2(at.X, at.Z), time * 0.35f + i, at.Y, cameraPos, 2.2f * gate.Scale);

            // And the contact glow under it, in its own temperature, so the mix along the span
            // is readable from the ground before the portal ever colours itself.
            Raylib.DrawSphereEx(at, 1.05f * gate.Scale, 8, 8,
                Fade(seated == Fragment.Sun ? Palette.SunBreak : Palette.MoonBreak, 90));
        }

        // The haul in flight, riding the curve from the panel out to its recess.
        if (gate.CarryPoint() is not { } carry) return;
        carry.X += shift.X; carry.Z += shift.Y;

        PolyMesh flying = gate.Carrying == Fragment.Sun ? _sun : _moon;
        flying.Draw(new Vector2(carry.X, carry.Z), time * 3.2f, carry.Y, cameraPos,
            2.2f * gate.Scale);
        // A bright envelope around it while it moves, so the eye can follow one small object
        // across a fifty-unit span at speed.
        Raylib.DrawSphereEx(carry, 1.5f * gate.Scale, 8, 8,
            Fade(gate.Carrying == Fragment.Sun ? Palette.SunBreak : Palette.MoonBreak, 70));
    }

    // --- The column --------------------------------------------------------------------

    /// <summary>
    /// The shaft of light that answers "where". Drawn only for a gate with power in it, and
    /// only until one is claimed — after that exactly one column stands on the whole planet,
    /// which is the entire point of claiming.
    ///
    /// <para>This is how a room finds an arch. The radar is a fifty-two-pixel square with a
    /// ninety-unit reach on a four-hundred-unit world, so for most of the map it can only say
    /// "that way, further than this" — and a screen-edge chevron would be the first piece of
    /// modern-shooter furniture in the game. A column is diegetic, readable from any bearing,
    /// occluded by the buildings between you and it, and reads at a glance as something that
    /// switched on.</para>
    /// </summary>
    private static void DrawColumn(Arch gate, Vector2 shift, float time)
    {
        if (gate.State == Arch.Phase.Dark) return;

        Vector2 at = gate.MouthPoint() + shift;
        var bottom = new Vector3(at.X, 0.2f, at.Y);
        var top = new Vector3(at.X, ColumnHeight, at.Y);

        // Warmer or colder with what has been fed into it — so from across the city you can
        // see which way the mix is going before you get there.
        Color hue = Lerp(Palette.PortalMoon, Palette.PortalSun, gate.Warmth);

        // Two shells: a soft wide one and a hard narrow core. One cylinder at one alpha reads
        // as a plastic tube; the pair reads as light with a bright centre.
        float breathe = 0.85f + 0.15f * MathF.Sin(time * 1.7f);
        float r = ColumnRadius * gate.Scale * breathe;

        Raylib.DrawCylinderEx(bottom, top, r, r * 0.25f, 10, Fade(hue, 40));
        Raylib.DrawCylinderEx(bottom, top, r * 0.42f, r * 0.1f, 8, Fade(hue, 95));
    }

    // --- The opening -------------------------------------------------------------------

    /// <summary>
    /// What happens under the span. Nothing at all until all five are in, then eleven seconds
    /// of something winding itself up, then a hole you walk into.
    /// </summary>
    private static void DrawMouth(Arch gate, Vector2 shift, float time)
    {
        if (gate.State is not (Arch.Phase.Charging or Arch.Phase.Open)) return;

        Vector2 at = gate.MouthPoint() + shift;
        float charge = gate.State == Arch.Phase.Open ? 1f : gate.Charge;

        // The colour the five fragments bought. This is the only place in the game the coin
        // flip has ever had a consequence, so it is not subtle: a five-sun gate opens hot
        // orange and a five-moon one opens cold blue, and any mix lands honestly between.
        Color hue = Lerp(Palette.PortalMoon, Palette.PortalSun, gate.Warmth);

        // While charging it is a knot of light gathering at the ground, shaking harder as it
        // goes. Once open it is a standing disc filling the arc.
        float r = gate.MouthRadius * (0.12f + 0.88f * charge * charge);
        float wobble = gate.State == Arch.Phase.Charging
            ? (1f - charge) * 0.35f * MathF.Sin(time * 26f)
            : 0f;

        var centre = new Vector3(at.X, r * 0.92f + wobble, at.Y);

        // Three nested shells rather than one: the outer haze, the body, and a white-hot core.
        // At 320×240 a single translucent sphere is a flat blob — the layering is what makes it
        // read as something with depth that is not simply painted on the air.
        //
        // The alphas are low and the outer shell is barely wider than the body, both learned
        // the hard way: the first build's haze reached a third again past the mouth at a third
        // opacity, and from any normal standing distance that was a brown fog over the entire
        // screen. What has to be legible through this is the arch it hangs under and the ground
        // you are about to drive across.
        Raylib.DrawSphereEx(centre, r * 1.16f, 12, 12, Fade(hue, (int)(26 * charge)));
        Raylib.DrawSphereEx(centre, r, 14, 14, Fade(hue, (int)(96 * charge)));
        Raylib.DrawSphereEx(centre, r * 0.42f, 10, 10,
            Fade(Color.White, (int)(150 * charge * charge)));

        // A ring on the grid under it, which is what tells a player on foot where the edge of
        // the thing is — the sphere alone gives no read of where you have to stand.
        if (gate.State != Arch.Phase.Open) return;
        float ring = gate.MouthRadius * (0.9f + 0.1f * MathF.Sin(time * 3.4f));
        Raylib.DrawCylinderWires(new Vector3(at.X, 0.15f, at.Y), ring, ring, 0.1f, 20,
            Fade(hue, 150));
    }

    // --- helpers -----------------------------------------------------------------------

    private static Color Tint(Color c, float t) => new(
        (int)Math.Clamp(c.R * t, 0f, 255f),
        (int)Math.Clamp(c.G * t, 0f, 255f),
        (int)Math.Clamp(c.B * t, 0f, 255f),
        (int)c.A);

    private static Color Fade(Color c, int alpha)
        => new(c.R, c.G, c.B, (byte)Math.Clamp(alpha, 0, 255));

    private static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t),
            255);
    }
}
