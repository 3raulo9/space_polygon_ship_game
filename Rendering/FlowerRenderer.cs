using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Entities;

namespace Unrendered.Rendering;

/// <summary>
/// Everything the FLOWER puts in the world that is not the plant itself: the petals crossing
/// the field, the rosettes they open, the reticle a player picking ground is looking at, and
/// the two screen effects the chassis owns.
///
/// <para>The petals are the important half. This class's whole read — for the player throwing
/// them and, much more importantly, for everybody being thrown at — is a small bright thing
/// travelling in an obviously curved line. A straight tracer says "a shot went past"; an arc
/// says "that is coming back", and the difference between those two sentences is the entire
/// counterplay to the weapon. So a petal in flight is drawn with a <em>trail</em> that records
/// where it has actually been, which is the only way a curve is legible at 320 pixels across
/// on something moving sixty metres a second.</para>
/// </summary>
public sealed class FlowerRenderer
{
    private readonly FlowerModel _model;

    /// <summary>Takes the same model the plant is drawn from, so a petal in the air is
    /// literally the geometry missing from the ring rather than a lookalike.</summary>
    public FlowerRenderer(FlowerModel model) => _model = model;

    // How far behind a petal the trail reaches, and how many segments it is drawn in. Short:
    // a long trail reads as a beam, and the point of it is to show curvature over about a
    // third of a second, which is as much of a turn as the eye can take in at once.
    private const int TrailSteps = 7;
    private const float TrailStep = 0.045f;

    /// <summary>
    /// The world half. Called from inside the 3D pass with every seat's plant available, since
    /// a team-mate's petals are exactly as much a fact about the field as one's own — and, with
    /// friendly fire on, considerably more urgent.
    /// </summary>
    public void Draw(World.World world, Vector3 cameraPos, float elapsed)
    {
        foreach (var craft in world.Players)
        {
            if (craft.Away || craft.Flower is not { } stalk) continue;
            Color petal = craft.Build.PartColor(PlayerClass.Flower, 0);
            Color seed = craft.Build.PartColor(PlayerClass.Flower, 3);

            foreach (var p in stalk.Petals)
            {
                if (!p.InAir) continue;
                DrawTrail(p, petal, cameraPos);
                DrawPetalInFlight(p, petal, seed, cameraPos);
            }
        }

        DrawBlooms(world, cameraPos);
        DrawReplantMarker(world, elapsed);
    }

    /// <summary>
    /// The petal itself: the ring's own geometry, tumbling about its long axis and yawed onto
    /// its line of travel.
    ///
    /// <para>Grown well past the size it is on the head, because a petal that is honestly
    /// scaled is four pixels of yellow at any useful range — and this object has to be readable
    /// from across a street by somebody it is about to arrive at. It is the one deliberate lie
    /// in the class's geometry and it is the right one.</para>
    /// </summary>
    private void DrawPetalInFlight(FlowerRig.Petal p, Color tint, Color core, Vector3 cameraPos)
    {
        float heading = MathF.Atan2(p.Direction.X, p.Direction.Y);
        // Nosed along the climb, so a petal chasing a soldier across the sky is visibly aimed
        // upward rather than sliding along level and happening to gain height.
        float pitch = MathF.Atan2(p.Climb, FlowerRig.Speed);

        _model.PetalMesh.Draw(p.Position, heading, p.Height, cameraPos,
            FlightScale, tint, pitch - MathF.PI * 0.5f, p.Spin);

        // A pip of the plant's own core colour riding in the petal's root. It is a tiny thing
        // and it does a lot of work: at fog range the yellow blade fades into the haze with
        // everything else, and the neon does not — so a petal you cannot yet make out is still
        // a bright dot travelling on a curve, which is all the warning anybody needs.
        _model.PetalMesh.Draw(p.Position, heading, p.Height, cameraPos,
            FlightScale * 0.22f, core, pitch - MathF.PI * 0.5f, p.Spin);
    }

    /// <summary>How much bigger a thrown petal is than a seated one. See the note above: this
    /// is a legibility decision, not a physical one.</summary>
    private const float FlightScale = 2.6f;

    /// <summary>
    /// The wake: a line of fading pips back along where the petal came from, reconstructed by
    /// walking its direction backwards. That reconstruction is an approximation — it draws a
    /// straight tail on a turning object — and it is deliberately kept short enough that the
    /// approximation never shows, while the <em>sequence</em> of tails across successive frames
    /// is what draws the curve.
    /// </summary>
    private static void DrawTrail(FlowerRig.Petal p, Color tint, Vector3 cameraPos)
    {
        var back = new Vector2(-p.Direction.X, -p.Direction.Y);
        for (int i = 1; i <= TrailSteps; i++)
        {
            float t = i / (float)TrailSteps;
            Vector2 at = Torus.Wrap(p.Position + back * (FlowerRig.Speed * TrailStep * i));
            float h = MathF.Max(0.2f, p.Height - p.Climb * TrailStep * i);

            var c = new Color(tint.R, tint.G, tint.B, (int)(190 * (1f - t) * (1f - t)));
            float size = 0.26f * (1f - t * 0.7f);

            Raylib.DrawCubeV(new Vector3(at.X, h, at.Y), new Vector3(size, size, size), c);
        }
    }

    /// <summary>
    /// A rosette opening where a petal went off: six flat lobes pushed out of the point of
    /// impact, spinning slowly and thinning as they go.
    ///
    /// <para>Drawn as flat triangles on the ground plane rather than as a sphere of debris,
    /// because the debris is already there — <c>StageBloom</c> throws it — and what this adds
    /// is the one thing debris cannot say, which is <em>that was a flower</em>. It is the same
    /// argument the class's audio makes: the weapon should be identifiable from its aftermath by
    /// somebody who did not see it thrown.</para>
    /// </summary>
    private static void DrawBlooms(World.World world, Vector3 cameraPos)
    {
        foreach (var b in world.Blooms)
        {
            float t = b.Phase;
            // Opens hard over the first quarter, then holds and thins — a flower doing anything.
            float open = t < 0.25f ? t / 0.25f : 1f;
            float fade = t < 0.25f ? 1f : 1f - (t - 0.25f) / 0.75f;
            float radius = b.Radius * open;
            float spin = t * 2.2f;
            float y = MathF.Max(0.15f, b.Height * (1f - t * 0.55f));

            var petal = new Color(Palette.Flag.R, Palette.Flag.G, Palette.Flag.B,
                                  (int)(210 * fade * fade));
            var heart = new Color(Palette.NeonRed.R, Palette.NeonRed.G, Palette.NeonRed.B,
                                  (int)(230 * fade));

            for (int i = 0; i < 6; i++)
            {
                float a = MathF.Tau * i / 6f + spin;
                float wide = a + 0.34f, narrow = a - 0.34f;

                var centre = new Vector3(b.Position.X, y, b.Position.Y);
                var tip = centre + new Vector3(MathF.Sin(a) * radius, 0f, MathF.Cos(a) * radius);
                var l = centre + new Vector3(MathF.Sin(wide) * radius * 0.45f, 0f,
                                             MathF.Cos(wide) * radius * 0.45f);
                var r = centre + new Vector3(MathF.Sin(narrow) * radius * 0.45f, 0f,
                                             MathF.Cos(narrow) * radius * 0.45f);

                // Both windings, because Raylib culls one of them in 2D and this game has been
                // bitten by exactly that before — see the HUD's player marker.
                Raylib.DrawTriangle3D(l, tip, r, petal);
                Raylib.DrawTriangle3D(r, tip, l, petal);
            }

            float pip = 0.5f * fade;
            Raylib.DrawCubeV(new Vector3(b.Position.X, y, b.Position.Y),
                new Vector3(pip, pip, pip), heart);
        }
    }

    /// <summary>
    /// The reticle a flower picking ground is looking at: a ring on the plate at the point the
    /// look meets it, with a stalk of light standing in the middle of it.
    ///
    /// <para>Only ever drawn for the craft this machine is flying. A replant is a decision, and
    /// broadcasting everybody's half-made decisions would turn the class into a chassis that
    /// telegraphs its one escape to the entire room. What the room gets is the commit — the
    /// wilt, the tear, and the hole — which is plenty.</para>
    /// </summary>
    private static void DrawReplantMarker(World.World world, float elapsed)
    {
        PlayerTank me = world.Eye;
        if (me.Flower is not { } stalk || !world.IsChoosingGround(me)) return;
        if (stalk.Replanting) return;

        Vector2 at = world.FlowerAimPoint(me);

        // Always teal, because the ground is always takeable: the replant costs nothing now, so
        // there is no state in which the marker means "no". It used to turn red on an empty
        // reserve, which was the correct readout for a rule that no longer exists.
        Color ink = Palette.GridNear;
        float pulse = 0.85f + 0.15f * MathF.Sin(elapsed * 6f);

        const int segments = 18;
        float radius = 1.5f * pulse;
        for (int i = 0; i < segments; i++)
        {
            float a0 = MathF.Tau * i / segments;
            float a1 = MathF.Tau * (i + 1) / segments;
            var p0 = new Vector3(at.X + MathF.Sin(a0) * radius, 0.08f, at.Y + MathF.Cos(a0) * radius);
            var p1 = new Vector3(at.X + MathF.Sin(a1) * radius, 0.08f, at.Y + MathF.Cos(a1) * radius);
            Raylib.DrawLine3D(p0, p1, ink);
        }

        // The shoot: a vertical line the height the plant will stand at, so the player is
        // choosing where their *head* ends up rather than where their feet do — which, on a
        // chassis whose only cover is the geometry it stands behind, is the question they are
        // actually asking.
        Raylib.DrawLine3D(new Vector3(at.X, 0.08f, at.Y),
                          new Vector3(at.X, FlowerRig.EyeHeight * pulse, at.Y), ink);
    }

    // --- Screen effects ---------------------------------------------------------------

    /// <summary>
    /// The two things the chassis does to the frame itself, both of them about the one state no
    /// other class has: being underground.
    ///
    /// Returns immediately without a flower, exactly as the fish's and the virus's passes do,
    /// so it costs nothing to be called on every run.
    /// </summary>
    public static void DrawScreenEffects(World.World world, float elapsed)
    {
        if (world.Eye.Flower is not { } stalk) return;

        const int w = Config.InternalWidth;
        const int h = Config.InternalHeight;

        if (stalk.Replanting) DrawSoilWash(stalk, w, h);
        if (stalk.Ripe) DrawRipeGlow(w, h, elapsed);
    }

    /// <summary>
    /// The plate closing over the view and opening again. It goes almost fully dark at the
    /// bottom of the transit and it is <em>supposed</em> to: the whole price of the replant is
    /// three quarters of a second of not knowing what happened to the fight you left, and a
    /// player who can still read the field through it has not paid for anything.
    /// </summary>
    private static void DrawSoilWash(FlowerRig stalk, int w, int h)
    {
        // Deepest at the seed and easing off through the wilt and the sprout — the same curve
        // the model's own growth runs on, so the picture and the plant agree about where in the
        // transit the player is.
        float depth = stalk.State switch
        {
            FlowerRig.Stance.Wilting => stalk.StancePhase * 0.75f,
            FlowerRig.Stance.Seeded => 0.92f,
            _ => (1f - stalk.StancePhase) * 0.85f,
        };
        if (depth <= 0.01f) return;

        Raylib.DrawRectangle(0, 0, w, h,
            new Color(10, 14, 10, (int)(235 * Math.Clamp(depth, 0f, 1f))));

        // A ragged aperture rather than a clean fade: soil is not a lens. Bands of the void
        // eaten in from the top and bottom at slightly different rates, which at this
        // resolution reads as earth rather than as a dip to black.
        int bite = (int)(h * 0.5f * depth);
        for (int i = 0; i < bite; i += 2)
        {
            int jag = (i * 37) % 5;
            Raylib.DrawRectangle(0, i, w, 1, new Color(6, 9, 7, 200 + jag * 8));
            Raylib.DrawRectangle(0, h - 1 - i, w, 1, new Color(6, 9, 7, 200 + jag * 8));
        }
    }

    /// <summary>
    /// A ripe head, told at the edges of the frame rather than in a number. The HUD has the bar
    /// and the bar is precise; this is the thing that makes a player who is busy notice that
    /// they have a harvest waiting without having to look away from what is shooting at them.
    ///
    /// Deliberately very faint and very slow. It is a nudge, not an alert — nothing is going
    /// wrong, there is simply something free on the table.
    /// </summary>
    private static void DrawRipeGlow(int w, int h, float elapsed)
    {
        float pulse = 0.5f + 0.5f * MathF.Sin(elapsed * 1.4f);
        int alpha = (int)(26 + 18 * pulse);

        for (int i = 0; i < 12; i++)
        {
            int a = alpha - i * 2;
            if (a <= 0) break;
            var tint = new Color(Palette.Flag.R, Palette.Flag.G, Palette.Flag.B, a);
            Raylib.DrawRectangle(i, 0, 1, h, tint);
            Raylib.DrawRectangle(w - 1 - i, 0, 1, h, tint);
            Raylib.DrawRectangle(0, h - 1 - i, w, 1, tint);
        }
    }
}
