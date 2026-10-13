using System.Numerics;

namespace Unrendered.Creator;

/// <summary>
/// Writes a design's built-in animations from the shape of its body, the way Spore gives a
/// creature a walk the moment it has legs. Everything it makes is ordinary clip data — waves
/// and keys on parts — so a built-in is a starting point the user can retune, not a black
/// box: change an amplitude and it is your walk.
///
/// <para>The four clip names are the same for every design (IDLE, MOVE, ATTACK, JUMP for
/// a creature; IDLE, MOVE, AIM, ATTACK for a vehicle), so whatever later drives a creation
/// in play can ask for them by name.</para>
/// </summary>
public static class AutoAnim
{
    public static readonly string[] CreatureClips = { "IDLE", "MOVE", "ATTACK", "JUMP" };
    public static readonly string[] VehicleClips = { "IDLE", "MOVE", "AIM", "ATTACK" };

    /// <summary>
    /// Replaces every built-in clip with a fresh one fitted to the body as it is now. Clips
    /// the user made themselves (not <see cref="Clip.BuiltIn"/>) are left untouched; a
    /// built-in the user has edited is replaced, which is what "rebuild" means.
    /// </summary>
    public static void Rebuild(Design d)
    {
        var fresh = d.Kind == DesignKind.Creature
            ? new[] { CreatureIdle(d), Move(d), CreatureAttack(d), CreatureJump(d) }
            : new[] { VehicleIdle(d), Move(d), VehicleAim(d), VehicleAttack(d) };

        // Keep the user's own clips where they were; put the built-ins first, in order. A
        // clip of the user's (or one copied off the game) that already has a built-in's name
        // takes that built-in's place.
        var mine = d.Clips.Where(c => !c.BuiltIn || c.Edited).ToList();
        var names = mine.Select(c => c.Name).ToHashSet();
        var ordered = new List<Clip>();
        foreach (var f in fresh)
            ordered.Add(names.Contains(f.Name) ? mine.First(c => c.Name == f.Name) : f);
        d.Clips = ordered.Concat(mine.Where(c => !ordered.Contains(c))).ToList();
    }

    /// <summary>Just one built-in, rebuilt — or null if <paramref name="name"/> is not one.</summary>
    public static Clip? Build(Design d, string name) => (d.Kind, name) switch
    {
        (_, "MOVE") => Move(d),
        (DesignKind.Creature, "IDLE") => CreatureIdle(d),
        (DesignKind.Creature, "ATTACK") => CreatureAttack(d),
        (DesignKind.Creature, "JUMP") => CreatureJump(d),
        (DesignKind.Vehicle, "IDLE") => VehicleIdle(d),
        (DesignKind.Vehicle, "AIM") => VehicleAim(d),
        (DesignKind.Vehicle, "ATTACK") => VehicleAttack(d),
        _ => null,
    };

    // =========================================================================
    //  Reading the body
    // =========================================================================

    private static bool ParentIs(Design d, Part p, params PartRole[] roles)
        => d.Find(p.Parent) is { } up && roles.Contains(up.Role);

    private static IEnumerable<Part> Roots(Design d)
        => d.Parts.Where(p => p.Parent < 0 && p.Role == PartRole.Body);

    /// <summary>Legs that join the body — not knees, which are legs hung from legs.</summary>
    private static List<Part> Hips(Design d)
        => d.Parts.Where(p => p.Role == PartRole.Leg && !ParentIs(d, p, PartRole.Leg)).ToList();

    private static List<Part> Shoulders(Design d)
        => d.Parts.Where(p => p.Role == PartRole.Arm && !ParentIs(d, p, PartRole.Arm)).ToList();

    /// <summary>Which way a limb points at rest, in the body's frame: from its joint to the
    /// farthest block anywhere below it.</summary>
    private static Vector3 Reach(Design d, Part limb)
    {
        var world = DesignPose.Evaluate(d, null, 0f, Matrix4x4.Identity);
        Vector3 joint = world[limb.Id].Translation;
        Vector3 far = joint;
        float best = 0f;
        foreach (var p in d.Subtree(limb.Id))
            foreach (var b in p.Blocks)
            {
                Vector3 at = Vector3.Transform(b.Center, world[p.Id]);
                float dist = Vector3.DistanceSquared(at, joint);
                if (dist > best) { best = dist; far = at; }
            }
        return far - joint;
    }

    /// <summary>
    /// When each leg steps, as a fraction of the stride. Legs are walked front to back down
    /// each side, alternating, and the two sides start half a stride apart — which gives a
    /// biped its alternating walk, a quadruped its diagonal trot and a hexapod its tripod,
    /// all from one rule.
    /// </summary>
    public static Dictionary<int, float> GaitPhases(Design d)
    {
        var phases = new Dictionary<int, float>();
        foreach (var side in Hips(d).GroupBy(p => p.Side))
        {
            int i = 0;
            foreach (var leg in side.OrderByDescending(p => d.RestPosition(p).Z))
            {
                float ph = i * 0.5f + (side.Key > 0 ? 0.5f : 0f);
                phases[leg.Id] = ph % 1f;
                i++;
            }
        }
        return phases;
    }

    /// <summary>How deep a part sits in a chain of its own role — the second tail segment
    /// is 1, the tip 2. Waves are delayed down a chain so a tail ripples rather than
    /// swinging as one stick.</summary>
    private static int ChainDepth(Design d, Part p)
    {
        int n = 0;
        for (var cur = p; d.Find(cur.Parent) is { } up && up.Role == p.Role && n < 32; cur = up) n++;
        return n;
    }

    // =========================================================================
    //  Track helpers
    // =========================================================================

    private static Track Wave(Part p, Channel ch, float amp, float phase = 0f, int cycles = 1, float offset = 0f)
        => new() { Part = p.Id, Channel = ch, Mode = TrackMode.Wave, Amp = amp, Phase = Wrap(phase), Cycles = cycles, Offset = offset };

    private static Track Spin(Part p, Channel ch, float turns, int cycles = 1)
        => new() { Part = p.Id, Channel = ch, Mode = TrackMode.Spin, Amp = turns, Cycles = cycles };

    private static Track Keys(Part p, Channel ch, params (float T, float V)[] keys)
        => new()
        {
            Part = p.Id, Channel = ch, Mode = TrackMode.Keys,
            Keys = keys.Select(k => new Key { T = k.T, V = k.V }).ToList(),
        };

    private static float Wrap(float phase) => ((phase % 1f) + 1f) % 1f;

    // =========================================================================
    //  Shared: moving
    // =========================================================================

    /// <summary>
    /// The one clip both kinds share. Legs get a gait whatever the design is — a vehicle
    /// with legs is a walker — wheels roll, tails and fins swim, and the body rides on
    /// top of all of it.
    /// </summary>
    private static Clip Move(Design d)
    {
        var clip = new Clip { Name = "MOVE", Duration = d.Kind == DesignKind.Vehicle ? 1f : 0.9f, BuiltIn = true };
        var t = clip.Tracks;
        var phases = GaitPhases(d);
        var hips = Hips(d);
        bool swims = hips.Count == 0 && d.Parts.Any(p => p.Role is PartRole.Tail or PartRole.Fin);

        foreach (var hip in hips)
        {
            float ph = phases[hip.Id];
            Vector3 reach = Reach(d, hip);
            if (MathF.Abs(reach.X) > MathF.Abs(reach.Y))
            {
                // A leg that reaches out sideways — a spider's — strides by swinging round
                // its hip and lifts by rolling up, rather than pitching like a person's.
                t.Add(Wave(hip, Channel.Yaw, hip.Side * 0.35f, ph));
                t.Add(Wave(hip, Channel.Roll, hip.Side * 0.22f, ph + 0.75f, offset: hip.Side * 0.1f));
            }
            else
            {
                // A leg that hangs: swing at the hip, and fold the knee hardest mid-swing so
                // the foot clears the ground on the way forward.
                t.Add(Wave(hip, Channel.Pitch, 0.45f, ph));
                foreach (var knee in d.Parts.Where(p => p.Parent == hip.Id && p.Role == PartRole.Leg))
                {
                    t.Add(Wave(knee, Channel.Pitch, 0.35f, ph + 0.75f, offset: 0.35f));
                    foreach (var foot in d.Parts.Where(p => p.Parent == knee.Id && p.Role == PartRole.Foot))
                        t.Add(Wave(foot, Channel.Pitch, 0.25f, ph + 0.25f, offset: -0.1f));
                }
            }
        }

        // Arms counter-swing the leg on their own side, which is most of what makes a
        // walk read as a walk rather than as legs being operated.
        foreach (var arm in Shoulders(d))
        {
            var sameSide = hips.Where(h => h.Side == arm.Side).OrderByDescending(h => d.RestPosition(h).Z).FirstOrDefault();
            float ph = sameSide != null ? phases[sameSide.Id] + 0.5f : (arm.Side > 0 ? 0.5f : 0f);
            t.Add(Wave(arm, Channel.Pitch, 0.4f, ph));
            foreach (var fore in d.Parts.Where(p => p.Parent == arm.Id && p.Role is PartRole.Arm or PartRole.Hand))
                t.Add(Wave(fore, Channel.Pitch, 0.15f, ph, offset: -0.35f));
        }

        int stepsPerCycle = Math.Max(1, Math.Min(2, hips.Count));
        foreach (var root in Roots(d))
        {
            if (hips.Count > 0)
            {
                // Rise on each footfall; roll onto whichever leg is planted. More legs means
                // a flatter ride.
                float calm = hips.Count <= 2 ? 1f : 0.4f;
                t.Add(Wave(root, Channel.MoveY, 0.035f * calm, 0.25f, stepsPerCycle));
                t.Add(Wave(root, Channel.Roll, 0.05f * calm, 0f));
                t.Add(Wave(root, Channel.Yaw, 0.04f * calm, 0.25f));
            }
            else if (swims)
            {
                t.Add(Wave(root, Channel.Yaw, 0.15f, 0f));
                t.Add(Wave(root, Channel.MoveY, 0.05f, 0.25f));
            }
            else if (d.Kind == DesignKind.Vehicle)
            {
                // The suspension working over the grid.
                t.Add(Wave(root, Channel.MoveY, 0.02f, 0f, 4));
                t.Add(Wave(root, Channel.Pitch, 0.02f, 0.25f, 2));
            }
            else if (!d.Parts.Any(p => p.Role == PartRole.Stalk))
            {
                // No legs, no tail, not rooted: it drifts — a slow bob and a lean into the
                // direction of travel, the way the mote hangs in the air.
                t.Add(Wave(root, Channel.MoveY, 0.08f, 0f));
                t.Add(Wave(root, Channel.Roll, 0.08f, 0.25f));
                t.Add(Keys(root, Channel.Pitch, (0f, 0.15f), (1f, 0.15f)));
            }
        }

        foreach (var p in d.Parts)
        {
            int depth = ChainDepth(d, p);
            switch (p.Role)
            {
                case PartRole.Head:
                    // Steadies itself against the bob: the eyes stay level.
                    t.Add(Wave(p, Channel.Pitch, 0.05f, 0.75f, stepsPerCycle));
                    break;
                case PartRole.Tail:
                    t.Add(Wave(p, Channel.Yaw, swims ? 0.45f : 0.2f, 0.25f + 0.15f * (depth + 1)));
                    break;
                case PartRole.Fin:
                    if (p.Side != 0) t.Add(Wave(p, Channel.Roll, p.Side * 0.3f, 0.25f));
                    else t.Add(Wave(p, Channel.Yaw, 0.12f, 0.35f));
                    break;
                case PartRole.Wing:
                    t.Add(Wave(p, Channel.Roll, p.Side * 0.5f, 0f, 2));
                    break;
                case PartRole.Wheel:
                    t.Add(Spin(p, Channel.Pitch, 1f, 2));
                    break;
                case PartRole.Stalk:
                    t.Add(Wave(p, Channel.Pitch, 0.12f, 0.12f * depth));
                    break;
                case PartRole.Petal:
                    t.Add(Wave(p, Channel.Pitch, 0.12f, 0.1f * p.Id));
                    break;
                case PartRole.Turret:
                    t.Add(Wave(p, Channel.Yaw, 0.05f, 0.5f));
                    break;
                case PartRole.Core:
                    t.Add(Spin(p, Channel.Yaw, 1f, 2));
                    break;
                case PartRole.Spine:
                    // The torso snakes a little, the wave running from head to tail.
                    t.Add(Wave(p, Channel.Yaw, swims ? 0.12f : 0.04f, -0.12f * p.Vertebra));
                    break;
                case PartRole.Decor when d.Kind == DesignKind.Creature:
                    // Loose bits trail and writhe, each a little out of step with the rest.
                    t.Add(Wave(p, Channel.Pitch, 0.18f, 0.17f * p.Id, 2));
                    break;
            }
        }
        return clip;
    }

    // =========================================================================
    //  Creature
    // =========================================================================

    private static Clip CreatureIdle(Design d)
    {
        var clip = new Clip { Name = "IDLE", Duration = 2.4f, BuiltIn = true };
        var t = clip.Tracks;
        foreach (var root in Roots(d)) t.Add(Wave(root, Channel.MoveY, 0.015f, 0f, 2));
        foreach (var p in d.Parts)
        {
            int depth = ChainDepth(d, p);
            switch (p.Role)
            {
                case PartRole.Head:
                    t.Add(Wave(p, Channel.Yaw, 0.25f, 0f));
                    t.Add(Wave(p, Channel.Pitch, 0.05f, 0.5f, 2));
                    break;
                case PartRole.Neck:
                    t.Add(Wave(p, Channel.Pitch, 0.04f, 0.25f, 2));
                    break;
                case PartRole.Arm when !ParentIs(d, p, PartRole.Arm):
                    t.Add(Wave(p, Channel.Pitch, 0.05f, p.Side > 0 ? 0.5f : 0f, 2));
                    break;
                case PartRole.Tail:
                    t.Add(Wave(p, Channel.Yaw, 0.15f, 0.15f * depth));
                    break;
                case PartRole.Fin or PartRole.Wing:
                    t.Add(Wave(p, Channel.Roll, (p.Side == 0 ? 1 : p.Side) * 0.08f, 0f, 2));
                    break;
                case PartRole.Stalk:
                    t.Add(Wave(p, Channel.Roll, 0.05f, 0.15f * depth));
                    break;
                case PartRole.Petal:
                    t.Add(Wave(p, Channel.Pitch, 0.06f, 0.125f * p.Id, 2));
                    break;
                case PartRole.Core:
                    t.Add(Spin(p, Channel.Yaw, 1f));
                    break;
                case PartRole.Decor:
                    t.Add(Wave(p, Channel.Pitch, 0.08f, 0.17f * p.Id, 2));
                    break;
                case PartRole.Eye:
                    // A glance about, each eye a little out of step so a cluster looks alive.
                    t.Add(Wave(p, Channel.Yaw, 0.12f, 0.13f * p.Id));
                    break;
                case PartRole.Mouth when p.Fixed:
                    // A jaw breathing.
                    t.Add(Wave(p, Channel.Pitch, 0.05f, 0.1f, 2));
                    break;
            }
        }
        return clip;
    }

    /// <summary>Wind up, lunge, recover. Written as keys rather than waves because a strike
    /// is a single event with a shape, not a rhythm.</summary>
    private static Clip CreatureAttack(Design d)
    {
        var clip = new Clip { Name = "ATTACK", Duration = 0.7f, Loop = false, BuiltIn = true };
        var t = clip.Tracks;
        foreach (var root in Roots(d))
        {
            t.Add(Keys(root, Channel.Pitch, (0f, 0f), (0.3f, -0.15f), (0.5f, 0.25f), (1f, 0f)));
            t.Add(Keys(root, Channel.MoveZ, (0f, 0f), (0.3f, -0.06f), (0.5f, 0.18f), (1f, 0f)));
        }
        foreach (var p in d.Parts)
        {
            if (p.Role == PartRole.Head)
                t.Add(Keys(p, Channel.Pitch, (0f, 0f), (0.3f, -0.2f), (0.5f, 0.25f), (1f, 0f)));
            else if (p.Role == PartRole.Arm && !ParentIs(d, p, PartRole.Arm))
                t.Add(Keys(p, Channel.Pitch, (0f, 0f), (0.3f, 0.6f), (0.5f, -1.4f), (1f, 0f)));
            else if (p.Role == PartRole.Tail && !ParentIs(d, p, PartRole.Tail))
                t.Add(Keys(p, Channel.Pitch, (0f, 0f), (0.3f, 0.2f), (0.5f, -0.25f), (1f, 0f)));
            else if (p.Role == PartRole.Stalk && !ParentIs(d, p, PartRole.Stalk))
                t.Add(Keys(p, Channel.Pitch, (0f, 0f), (0.3f, -0.25f), (0.5f, 0.35f), (1f, 0f)));
            else if (p.Role == PartRole.Petal)
                t.Add(Keys(p, Channel.Pitch, (0f, 0f), (0.3f, -0.3f), (0.5f, 0.4f), (1f, 0f)));
            else if (p.Role == PartRole.Mouth && p.Fixed)
            {
                // Gape on the wind-up, snap shut on the lunge. A jaw off to one side — a
                // mandible — closes sideways, toward the middle.
                if (MathF.Abs(p.Pivot.X) > 0.03f)
                {
                    float s = MathF.Sign(p.Pivot.X);
                    t.Add(Keys(p, Channel.Yaw, (0f, 0f), (0.3f, s * 0.45f), (0.5f, -s * 0.3f), (1f, 0f)));
                }
                else t.Add(Keys(p, Channel.Pitch, (0f, 0f), (0.3f, 0.6f), (0.5f, -0.25f), (1f, 0f)));
            }
        }
        return clip;
    }

    /// <summary>Crouch, spring, land.</summary>
    private static Clip CreatureJump(Design d)
    {
        var clip = new Clip { Name = "JUMP", Duration = 0.9f, Loop = false, BuiltIn = true };
        var t = clip.Tracks;
        foreach (var root in Roots(d))
            t.Add(Keys(root, Channel.MoveY, (0f, 0f), (0.25f, -0.12f), (0.55f, 0.7f), (0.85f, -0.05f), (1f, 0f)));
        foreach (var hip in Hips(d))
        {
            bool sideways = MathF.Abs(Reach(d, hip).X) > MathF.Abs(Reach(d, hip).Y);
            if (sideways)
                t.Add(Keys(hip, Channel.Roll, (0f, 0f), (0.25f, hip.Side * 0.3f), (0.55f, -hip.Side * 0.25f), (1f, 0f)));
            else
            {
                t.Add(Keys(hip, Channel.Pitch, (0f, 0f), (0.25f, -0.45f), (0.55f, 0.25f), (0.85f, -0.3f), (1f, 0f)));
                foreach (var knee in d.Parts.Where(p => p.Parent == hip.Id && p.Role == PartRole.Leg))
                    t.Add(Keys(knee, Channel.Pitch, (0f, 0f), (0.25f, 0.85f), (0.55f, 0.1f), (0.85f, 0.6f), (1f, 0f)));
            }
        }
        foreach (var arm in Shoulders(d))
            t.Add(Keys(arm, Channel.Pitch, (0f, 0f), (0.25f, 0.5f), (0.55f, -1.2f), (1f, 0f)));
        foreach (var p in d.Parts.Where(p => p.Role is PartRole.Tail or PartRole.Fin or PartRole.Wing))
            t.Add(Keys(p, Channel.Pitch, (0f, 0f), (0.25f, -0.2f), (0.55f, 0.3f), (1f, 0f)));
        return clip;
    }

    // =========================================================================
    //  Vehicle
    // =========================================================================

    private static Clip VehicleIdle(Design d)
    {
        var clip = new Clip { Name = "IDLE", Duration = 1.5f, BuiltIn = true };
        var t = clip.Tracks;
        // An engine ticking over: a fast, tiny shake.
        foreach (var root in Roots(d)) t.Add(Wave(root, Channel.MoveY, 0.008f, 0f, 8));
        foreach (var p in d.Parts)
        {
            if (p.Role == PartRole.Turret) t.Add(Wave(p, Channel.Yaw, 0.12f, 0f));
            else if (p.Role == PartRole.Core) t.Add(Spin(p, Channel.Yaw, 1f));
        }
        return clip;
    }

    private static Clip VehicleAim(Design d)
    {
        var clip = new Clip { Name = "AIM", Duration = 2.4f, BuiltIn = true };
        var t = clip.Tracks;
        foreach (var p in d.Parts)
        {
            if (p.Role == PartRole.Turret) t.Add(Wave(p, Channel.Yaw, 0.6f, 0f));
            else if (p.Role == PartRole.Barrel) t.Add(Wave(p, Channel.Pitch, 0.12f, 0.25f, 2, -0.08f));
        }
        return clip;
    }

    /// <summary>Fire: the barrel kicks back into the turret and the hull rocks on its
    /// springs.</summary>
    private static Clip VehicleAttack(Design d)
    {
        var clip = new Clip { Name = "ATTACK", Duration = 0.5f, Loop = false, BuiltIn = true };
        var t = clip.Tracks;
        foreach (var root in Roots(d))
            t.Add(Keys(root, Channel.Pitch, (0f, 0f), (0.1f, -0.06f), (0.45f, 0.02f), (1f, 0f)));
        foreach (var p in d.Parts)
        {
            if (p.Role == PartRole.Barrel)
                t.Add(Keys(p, Channel.MoveZ, (0f, 0f), (0.08f, -0.35f), (1f, 0f)));
            else if (p.Role == PartRole.Turret)
                t.Add(Keys(p, Channel.MoveZ, (0f, 0f), (0.1f, -0.08f), (1f, 0f)));
        }
        return clip;
    }
}
