using System.Numerics;
using Unrendered.Rendering;

namespace Unrendered.Core;

/// <summary>
/// The figure: the layered animator that decides where every joint of a soldier is.
///
/// <para>None of this can be checked by looking at a screen. The whole system is built out
/// of things that are true over <em>time</em> — a limb that arrives late, a knee that
/// compresses and springs back, a boot that stays where it was put while the body travels
/// over it — and a screenshot of any one frame of that is a pose, which tells you nothing
/// about whether the pose was arrived at physically. So the checks here step the animator
/// with a real clock and assert about the shape of what comes out.</para>
///
/// <para>What is checked is the mechanism, not the taste: that a body in the air folds, not
/// whether it folds by the right amount; that a hit throws the head away from what caused
/// it, not whether the flinch looks good. The numbers are somebody's judgement and belong in
/// the animator where they can be retuned. The wiring is not, and is exactly the kind of
/// thing that quietly comes apart.</para>
/// </summary>
public static partial class SelfTest
{
    /// <summary>A body doing nothing, standing on the grid. The zero every check below
    /// departs from.</summary>
    private static SoldierAnimator.Signals Still(float time = 0f) => new(
        Position: Vector2.Zero, Height: 0f, Heading: 0f, Velocity: Vector3.Zero,
        Grounded: true, Perched: false, Anchored: false, Reeling: false,
        LeftTension: 0f, RightTension: 0f, LeftOut: false, RightOut: false,
        LeftHook: Vector3.Zero, RightHook: Vector3.Zero,
        Bank: 0f, Stagger: 0f, Blades: false,
        LookYaw: 0f, LookPitch: 0f, GroundY: 0f, Scale: 1f, Time: time);

    /// <summary>Steps an animator for a stretch of time at a fixed rate, so a check can ask
    /// what a body settles into rather than what it looks like on the frame it was told.</summary>
    private static void Settle(SoldierAnimator a, Func<float, SoldierAnimator.Signals> at,
        float seconds, float dt = 1f / 60f)
    {
        for (float t = 0f; t < seconds; t += dt) a.Step(at(t), dt);
    }

    /// <summary>
    /// The single most important thing the air layer does: a body travelling fast on a taut
    /// line pitches down its own line of travel, so how fast a soldier is going is legible in
    /// the shape of them rather than only in how quickly they cross the frame.
    ///
    /// Checked as a comparison rather than against an absolute, because the exact angle is a
    /// tuning decision and the <em>relationship</em> is not: fast has to fold harder than
    /// slow, and both have to fold further than standing still.
    /// </summary>
    private static string? FlightFoldsWithSpeed()
    {
        float Fold(float speed)
        {
            var a = new SoldierAnimator();
            Settle(a, t => Still(t) with
            {
                Height = 20f,
                Velocity = new Vector3(0f, -1f, speed),
                Grounded = false, Anchored = true,
                LeftTension = 0.9f, LeftOut = true,
                LeftHook = new Vector3(0f, 34f, 12f),
            }, 3f);
            var (pitch, _, _) = SoldierSkeleton.ChestAngles(a.Pose);
            return pitch;
        }

        float standing = StandingFold();
        float slow = Fold(6f);
        float fast = Fold(26f);

        if (fast <= slow)
            return $"a fast swing folds no harder than a slow one ({fast:0.00} vs {slow:0.00} rad)";
        if (slow <= standing)
            return $"a swing folds no harder than standing still ({slow:0.00} vs {standing:0.00} rad)";
        if (fast < 0.8f)
            return $"a body at full speed is barely folded at all ({fast:0.00} rad)";
        return null;

        static float StandingFold()
        {
            var a = new SoldierAnimator();
            Settle(a, Still, 3f);
            var (pitch, _, _) = SoldierSkeleton.ChestAngles(a.Pose);
            return pitch;
        }
    }

    /// <summary>
    /// An arrival has to be taken by the knees and then given back. Compression alone is a
    /// crouch; compression that springs back up through level and settles is a landing, and
    /// the difference is the only reason the squash is a spring rather than a curve.
    /// </summary>
    private static string? LandingCompressesThenRecovers()
    {
        var a = new SoldierAnimator();

        // Falling hard, then the floor.
        Settle(a, t => Still(t) with
        {
            Height = 8f, Grounded = false, Velocity = new Vector3(0f, -18f, 0f),
        }, 0.5f);

        float deepest = 0f;
        for (float t = 0f; t < 0.25f; t += 1f / 60f)
        {
            a.Step(Still(t) with { Velocity = Vector3.Zero }, 1f / 60f);
            deepest = MathF.Min(deepest, a.Squash);
        }
        if (deepest > -0.05f)
            return $"a twenty-metre arrival barely bent the knees ({deepest:0.000} m)";

        // And back up. Not merely toward zero — through it, which is what a spring does and
        // an ease does not.
        bool overshot = false;
        for (float t = 0f; t < 2f; t += 1f / 60f)
        {
            a.Step(Still(2f + t), 1f / 60f);
            if (a.Squash > 0.002f) overshot = true;
        }
        if (MathF.Abs(a.Squash) > 0.01f)
            return $"the knees never came back up ({a.Squash:0.000} m still compressed)";
        if (!overshot)
            return "the body rose to a stop instead of springing past level and settling";
        return null;
    }

    /// <summary>
    /// Momentum, which is the whole difference between this and the pose function it
    /// replaced. Handed a body whose pose target changes in one frame, the drawn limb must
    /// <em>not</em> be there yet — and must get there shortly after.
    /// </summary>
    private static string? LimbsLagTheBodyTheyHangFrom()
    {
        var a = new SoldierAnimator();
        Settle(a, Still, 2f);
        float rest = a.Pose.LeftArm.UpperPitch;

        // Straight into a hard swing: the target arm angle is now a long way from rest.
        SoldierAnimator.Signals Flying(float t) => Still(t) with
        {
            Height = 20f, Grounded = false, Anchored = true,
            Velocity = new Vector3(0f, -1f, 26f),
            LeftTension = 0.9f, LeftOut = true, LeftHook = new Vector3(0f, 34f, 12f),
        };

        a.Step(Flying(0f), 1f / 60f);
        float afterOneFrame = a.Pose.LeftArm.UpperPitch;

        Settle(a, Flying, 2f);
        float settled = a.Pose.LeftArm.UpperPitch;

        if (MathF.Abs(settled - rest) < 0.2f)
            return "the arm never moved into the swing at all";

        // One frame in, it should have covered only a sliver of the distance.
        float covered = MathF.Abs(afterOneFrame - rest) / MathF.Abs(settled - rest);
        if (covered > 0.35f)
            return $"the arm snapped to the new pose in one frame ({covered:0%} of the way)";
        return null;
    }

    /// <summary>
    /// A boot that is carrying weight stays where it was put while the body walks over it.
    /// This is the difference between striding and skating, and it is the one thing a pose
    /// function of the clock can never do however well it is authored.
    /// </summary>
    private static string? PlantedBootsDoNotSkate()
    {
        var a = new SoldierAnimator();
        var walked = Vector2.Zero;

        // Walking forward at a steady pace.
        SoldierAnimator.Signals Walking(float t)
        {
            walked = new Vector2(0f, 4f * t);
            return Still(t) with { Position = walked, Velocity = new Vector3(0f, 0f, 4f) };
        }

        Settle(a, Walking, 1f);

        // Follow one ankle through a stretch where the same boot is carrying: in the world's
        // frame it must barely move, even though the body has travelled.
        Vector3? held = null;
        float worst = 0f;
        Vector2 from = walked;
        for (float t = 1f; t < 1.25f; t += 1f / 60f)
        {
            a.Step(Walking(t), 1f / 60f);
            if (a.LeftFootLoad < 0.9f) { held = null; continue; }

            Vector3 ankle = Ankle(a.Pose, left: true);
            // Into the world: the body has moved, so the same planted boot must move back
            // through the body's own frame by exactly as much.
            var world = new Vector3(walked.X + ankle.X, ankle.Y, walked.Y + ankle.Z);
            if (held is { } was) worst = MathF.Max(worst, (world - was).Length());
            else held = world;
        }

        float travelled = Vector2.Distance(from, walked);
        if (travelled < 0.5f) return "the test body never actually walked anywhere";
        if (worst > 0.12f)
            return $"a planted boot slid {worst:0.000} m while the body walked {travelled:0.00} m";
        return null;
    }

    /// <summary>
    /// Each boot stays on its own side of the body.
    ///
    /// This is here because it went wrong. The solver plants a boot by working out where on
    /// the ground that foot should go, which means converting "out to the figure's left" from
    /// the body's frame into the world's — and the two differ by a sign that is easy to get
    /// backwards and impossible to notice in a still frame. Backwards, every soldier in the
    /// game walks with its legs crossed, and the only symptom is that they look wrong.
    /// </summary>
    private static string? BootsStayOnTheirOwnSides()
    {
        var a = new SoldierAnimator();
        float worst = float.MaxValue;

        // Walked in four directions, because a sign error in the world-to-body turn is
        // invisible at one heading and obvious across several.
        foreach (float heading in new[] { 0f, 1.2f, MathF.PI, -2.4f })
        {
            var walk = new Vector2(MathF.Sin(heading), MathF.Cos(heading)) * 4f;
            for (float t = 0f; t < 2.5f; t += 1f / 60f)
            {
                a.Step(Still(t) with
                {
                    Heading = heading,
                    Position = walk * t,
                    Velocity = new Vector3(walk.X, 0f, walk.Y),
                }, 1f / 60f);

                if (t < 1f) continue;

                // In the figure's own frame, +X is its left. So the left ankle must be the
                // one further along +X, always, by more than a rounding error.
                float gap = Ankle(a.Pose, left: true).X - Ankle(a.Pose, left: false).X;
                worst = MathF.Min(worst, gap);
            }
        }

        if (worst < 0.02f)
            return $"the boots crossed over each other while walking ({worst:0.000} m apart, "
                 + "left minus right — negative means the legs are intertwined)";
        return null;
    }

    /// <summary>Where one ankle is, in the body's own frame — the far end of the two-bone
    /// chain the solver writes.</summary>
    private static Vector3 Ankle(in SoldierPose p, bool left)
    {
        LimbPose leg = left ? p.LeftLeg : p.RightLeg;
        Vector3 hip = SoldierSkeleton.Hip(p, left);
        Vector3 knee = hip + SoldierSkeleton.Segment(SoldierSkeleton.SegLen,
            leg.UpperPitch, leg.UpperRoll);
        return knee + SoldierSkeleton.Segment(SoldierSkeleton.SegLen,
            leg.LowerPitch, leg.LowerRoll);
    }

    /// <summary>
    /// The flinch, which is worth nothing without a direction. A hit from the left has to
    /// throw the head to the right; a symmetric recoil is the animation of being hit rather
    /// than a body reacting to something in particular.
    ///
    /// And it has to be <em>additive</em>: the body must still be doing whatever it was doing
    /// underneath, or a soldier hit mid-swing drops out of the arc they are in.
    /// </summary>
    private static string? FlinchThrowsTheHeadAwayFromTheHit()
    {
        SoldierAnimator.Signals Flying(float t) => Still(t) with
        {
            Height = 20f, Grounded = false, Anchored = true,
            Velocity = new Vector3(0f, -1f, 26f),
            LeftTension = 0.9f, LeftOut = true, LeftHook = new Vector3(0f, 34f, 12f),
        };

        var a = new SoldierAnimator();
        Settle(a, Flying, 3f);
        var (foldBefore, _, _) = SoldierSkeleton.ChestAngles(a.Pose);
        float headBefore = a.Pose.HeadYaw;

        // Hit from the body's left. Heading is 0 (facing +Z), so +X is the figure's left and
        // the angle to it is +π/2.
        a.Flinch(MathF.PI * 0.5f, 1f);
        Settle(a, Flying, 0.1f);

        float headAfter = a.Pose.HeadYaw;
        var (foldAfter, _, _) = SoldierSkeleton.ChestAngles(a.Pose);

        if (headAfter >= headBefore - 0.1f)
            return $"a hit from the left did not throw the head right ({headBefore:0.00} → {headAfter:0.00})";

        // A mirrored hit has to mirror the answer.
        var b = new SoldierAnimator();
        Settle(b, Flying, 3f);
        b.Flinch(-MathF.PI * 0.5f, 1f);
        Settle(b, Flying, 0.1f);
        if (b.Pose.HeadYaw <= headBefore + 0.1f)
            return "a hit from the right did not throw the head left";

        // Still swinging underneath it.
        if (MathF.Abs(foldAfter - foldBefore) > 0.8f)
            return $"the flinch replaced the swing instead of laying over it "
                 + $"({foldBefore:0.00} → {foldAfter:0.00} rad)";
        return null;
    }

    /// <summary>
    /// The hands go where the load is. With a cable out on one hip only, that arm has to be
    /// the one taking the weight and the other has to be free — which is the readout that
    /// tells a player at a glance which of a body's two lines is carrying it.
    /// </summary>
    private static string? AHeldCablePutsTheHandOnItsLauncher()
    {
        var a = new SoldierAnimator();
        Settle(a, t => Still(t) with
        {
            Height = 20f, Grounded = false, Anchored = true,
            Velocity = new Vector3(0f, -1f, 20f),
            RightTension = 1f, RightOut = true,
            RightHook = new Vector3(0f, 40f, 10f),
        }, 3f);

        // The wrist on the loaded side should have arrived near its own launcher.
        Vector3 grip = SoldierSkeleton.Launcher(a.Pose, left: false);
        Vector3 wrist = Wrist(a.Pose, left: false);
        float reach = (wrist - grip).Length();
        if (reach > 0.22f)
            return $"the loaded hand never reached its launcher ({reach:0.000} m away)";

        // And the free one should not have.
        Vector3 freeGrip = SoldierSkeleton.Launcher(a.Pose, left: true);
        Vector3 freeWrist = Wrist(a.Pose, left: true);
        if ((freeWrist - freeGrip).Length() < 0.05f)
            return "the free hand was solved onto a launcher it is not holding";
        return null;
    }

    private static Vector3 Wrist(in SoldierPose p, bool left)
    {
        LimbPose arm = left ? p.LeftArm : p.RightArm;
        Vector3 shoulder = SoldierSkeleton.Shoulder(p, left);
        Vector3 elbow = shoulder + SoldierSkeleton.Segment(SoldierSkeleton.SegLen,
            arm.UpperPitch, arm.UpperRoll);
        return elbow + SoldierSkeleton.Segment(SoldierSkeleton.SegLen,
            arm.LowerPitch, arm.LowerRoll);
    }

    /// <summary>
    /// The chest turns against the hips through a stride. Contrapposto is a small angle and
    /// it is most of what separates a walking person from a walking chair, so it is checked
    /// for existence and for <em>sign</em>: they have to disagree, not merely both move.
    /// </summary>
    private static string? HipsAndShouldersTurnAgainstEachOther()
    {
        var a = new SoldierAnimator();
        float worstAgreement = 0f;
        bool everTwisted = false;

        for (float t = 0f; t < 4f; t += 1f / 60f)
        {
            a.Step(Still(t) with
            {
                Position = new Vector2(0f, 5.5f * t),
                Velocity = new Vector3(0f, 0f, 5.5f),
            }, 1f / 60f);

            if (t < 1f) continue;   // let the stride get going
            float hips = a.Pose.PelvisYaw;
            float chest = a.Pose.ChestYaw;
            if (MathF.Abs(hips) < 0.05f) continue;

            everTwisted = true;
            // Same sign means they turned together — the whole body swinging as one piece,
            // which is the thing this is here to prevent.
            if (hips * chest > 0f) worstAgreement = MathF.Max(worstAgreement, MathF.Abs(chest));
        }

        if (!everTwisted) return "the hips never turned through a whole stride";
        if (worstAgreement > 0.08f)
            return $"the shoulders turned with the hips rather than against them "
                 + $"({worstAgreement:0.00} rad the wrong way)";
        return null;
    }

    /// <summary>
    /// Nothing the simulation can hand this thing may produce a joint angle that is not a
    /// number. An animator is downstream of a constraint solver that writes velocities four
    /// times a step, and a single NaN in a spring poisons every frame after it — the figure
    /// does not glitch, it disappears, permanently, and only for the machines drawing it.
    /// </summary>
    private static string? NothingProducesANonNumber()
    {
        var a = new SoldierAnimator();
        var rng = new Random(20260801);

        for (int i = 0; i < 4000; i++)
        {
            float dt = (float)(rng.NextDouble() * 0.2);
            var s = Still(i * 0.01f) with
            {
                Position = new Vector2(Wild(), Wild()),
                Height = Wild(),
                Heading = Wild(),
                Velocity = new Vector3(Wild(), Wild(), Wild()),
                Grounded = rng.Next(2) == 0,
                Perched = rng.Next(4) == 0,
                Anchored = rng.Next(2) == 0,
                Reeling = rng.Next(2) == 0,
                LeftTension = (float)rng.NextDouble(),
                RightTension = (float)rng.NextDouble(),
                LeftOut = rng.Next(2) == 0,
                RightOut = rng.Next(2) == 0,
                LeftHook = new Vector3(Wild(), Wild(), Wild()),
                RightHook = new Vector3(Wild(), Wild(), Wild()),
                Bank = Wild() * 0.1f,
                Stagger = (float)rng.NextDouble(),
                Blades = rng.Next(2) == 0,
                LookYaw = Wild() * 0.1f,
                LookPitch = Wild() * 0.1f,
                Scale = 0.5f + (float)rng.NextDouble() * 3f,
            };
            if (rng.Next(20) == 0) a.Flinch(Wild(), (float)rng.NextDouble());
            if (rng.Next(20) == 0) a.Fire(rng.Next(2) == 0);
            if (rng.Next(30) == 0) a.Slash();
            a.Step(s, dt);

            if (Bad(a.Pose) is { } where) return $"{where} after {i} steps of nonsense";
        }
        return null;

        float Wild() => (float)(rng.NextDouble() * 80.0 - 40.0);

        static string? Bad(in SoldierPose p)
        {
            if (!Finite(p.Rise) || !Finite(p.Sway) || !Finite(p.Surge)) return "the root went bad";
            if (!Finite(p.PelvisPitch) || !Finite(p.PelvisRoll) || !Finite(p.PelvisYaw))
                return "the pelvis went bad";
            if (!Finite(p.ChestPitch) || !Finite(p.ChestRoll) || !Finite(p.ChestYaw))
                return "the chest went bad";
            if (!Finite(p.HeadYaw) || !Finite(p.HeadNod) || !Finite(p.HeadTilt))
                return "the head went bad";
            return BadLimb(p.LeftArm) ?? BadLimb(p.RightArm)
                ?? BadLimb(p.LeftLeg) ?? BadLimb(p.RightLeg);
        }

        static string? BadLimb(in LimbPose l)
            => Finite(l.UpperPitch) && Finite(l.UpperRoll)
            && Finite(l.LowerPitch) && Finite(l.LowerRoll) ? null : "a limb went bad";

        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f) && MathF.Abs(f) < 1e4f;
    }
}
