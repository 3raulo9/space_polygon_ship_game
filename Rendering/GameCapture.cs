using System.Numerics;
using Unrendered.Core;
using Unrendered.Creator;
using Unrendered.Entities;

namespace Unrendered.Rendering;

/// <summary>
/// Copies the game's own characters into the creator by recording them being drawn. The
/// hangar's showcase draw runs with a <see cref="MeshRecorder"/> catching every mesh, so each
/// part comes out with the game's real triangles, colours and placement — not a remembered
/// approximation that drifts the moment somebody touches a model.
///
/// <para>The draw is then repeated across a few seconds of the idle, and how each mesh moves
/// is written down as keys on an IDLE clip, so the copy also moves the way the original
/// does. Joints are inferred: each mesh hangs from the larger one nearest its origin, and two
/// meshes that are mirror images of each other across the centre line are linked as twins
/// so symmetric editing works on them as it does on anything built in the creator.</para>
/// </summary>
public static class GameCapture
{
    /// <summary>The renderer's entity drawer, handed over when the renderer starts. Capturing
    /// needs it because the models live on it.</summary>
    public static EntityRenderer? Entities { get; set; }

    public sealed record Entry(string Id, string Label, DesignKind Kind, PlayerClass Class,
        Action<EntityRenderer, float> Draw, Walk? Move = null);

    /// <summary>
    /// A character's walk, for the ones whose walk the game draws as a pure function of
    /// where in the stride it is. <see cref="Draw"/> takes the fraction of the way round the
    /// loop, 0..1, and <see cref="Seconds"/> is how long one loop lasts in play. Copied the
    /// same way the idle is, into the MOVE clip, so the copy walks as the game does instead
    /// of with a gait the creator made up for a body it doesn't understand.
    /// </summary>
    public sealed record Walk(float Seconds, Action<EntityRenderer, float> Draw);

    /// <summary>Everything in the game that can be opened in the creator.</summary>
    public static readonly IReadOnlyList<Entry> All = new Entry[]
    {
        Player("tank", "TANK", DesignKind.Vehicle, PlayerClass.Tank),
        new("grinder", "GRINDER TANK", DesignKind.Vehicle, PlayerClass.Tank,
            (er, t) => er.DrawShowcase(EnemyKind.StandardTank, Vector2.Zero, 0f, Eye, t)),
        new("revenant", "REVENANT TANK", DesignKind.Vehicle, PlayerClass.Tank,
            (er, t) => er.DrawShowcase(EnemyKind.EliteTank, Vector2.Zero, 0f, Eye, t)),
        Player("spider", "SPIDER", DesignKind.Creature, PlayerClass.Spider) with { Move = SpiderWalk() },
        Player("virus", "VIRUS", DesignKind.Creature, PlayerClass.Virus),
        Player("fish", "FISH", DesignKind.Creature, PlayerClass.Fish),
        Player("soldier", "SOLDIER", DesignKind.Creature, PlayerClass.Soldier),
        Player("flower", "FLOWER", DesignKind.Creature, PlayerClass.Flower),
    };

    private static readonly Vector3 Eye = new(0f, 3f, 8f);

    /// <summary>How many strides one copied walk loop holds. Three, because the gem turns
    /// once a loop and at the Crab-Core's hunting cadence three strides is the turn closest
    /// to its own agitated spin.</summary>
    private const int WalkStrides = 3;

    /// <summary>The SPIDER at full stride, hunting pace: exactly the Crab-Core's pursuit,
    /// since it is the same rig.</summary>
    private static Walk SpiderWalk()
    {
        float seconds = WalkStrides * MathF.Tau / CrabCore.PursuitLeg;
        var loadout = new Loadout { Class = PlayerClass.Spider };
        return new Walk(seconds, (er, u) =>
            er.DrawSpider(loadout, Vector2.Zero, 0f, Eye,
                coreSpin: u * MathF.Tau, legPhase: u * WalkStrides * MathF.Tau, stride: 1f));
    }

    /// <summary>How many times a walk loop is sampled. Denser than the idle: a stride is a
    /// quick, sharp motion, and the swing's lift has to survive being drawn through a curve.</summary>
    public const int WalkSamples = 48;

    /// <summary>The walk's draws, one per sample of the loop.</summary>
    public static List<List<MeshRecorder.Shot>> RunWalk(Walk walk, EntityRenderer er)
    {
        var samples = new List<List<MeshRecorder.Shot>>();
        for (int k = 0; k < WalkSamples; k++)
        {
            float u = (float)k / WalkSamples;
            samples.Add(MeshRecorder.Record(() => walk.Draw(er, u)));
        }
        return samples;
    }

    private static Entry Player(string id, string label, DesignKind kind, PlayerClass cls)
        => new(id, label, kind, cls, (er, t) =>
        {
            var loadout = new Loadout { Class = cls };
            er.DrawLoadoutShowcase(loadout, Vector2.Zero, 0f, Eye, t);
        });

    /// <summary>How long of the idle is copied, and how many times a second it is sampled.
    /// Dense enough that the curve through the samples is the motion.</summary>
    private const float IdleSeconds = 4f;
    private const int Samples = 32;

    public static Design Capture(Entry e)
    {
        var er = Entities ?? new EntityRenderer();
        var samples = Run(e, er);
        var shots = samples[0];
        var d = new Design { Name = e.Label, Kind = e.Kind, BaseClass = e.Class, Template = e.Id };
        if (shots.Count == 0) return d;
        int n = shots.Count;

        // ---- one part per mesh, rigid, with its scale baked into its triangles ----------
        var rigid = new Matrix4x4[n];
        var scale0 = new Vector3[n];
        var extent0 = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var (scale, rot) = Split(shots[i].World);
            rigid[i] = rot;
            scale0[i] = scale;
            extent0[i] = Extent(shots[i].Mesh);
            var tint = shots[i].Tint;
            var tris = shots[i].Mesh.Triangles.Select(t => (t.A * scale, t.B * scale, t.C * scale, tint ?? t.Color));
            d.Parts.Add(new Part { Id = i, Name = $"PIECE {i + 1}", Role = PartRole.Decor, Blocks = { Block.FromTriangles(tris) } });
        }

        // ---- the joints -----------------------------------------------------------------
        var size = d.Parts.Select(Volume).ToArray();
        var order = Enumerable.Range(0, n).OrderByDescending(i => size[i]).ToList();
        int root = order[0];
        var parent = new int[n];
        parent[root] = -1;
        for (int k = 1; k < order.Count; k++)
        {
            int i = order[k];
            Vector3 origin = rigid[i].Translation;
            int best = root;
            float bestDist = float.MaxValue;
            for (int j = 0; j < k; j++)
            {
                int q = order[j];
                float dist = DistanceToBounds(origin, d.Parts[q], rigid[q]);
                if (dist < bestDist - 1e-4f) { bestDist = dist; best = q; }
            }
            parent[i] = best;
        }
        for (int i = 0; i < n; i++)
        {
            var p = d.Parts[i];
            p.Parent = parent[i];
            var local = Relative(rigid[i], parent[i] >= 0 ? rigid[parent[i]] : Matrix4x4.Identity);
            p.Pivot = local.Translation;
            p.Rest = DesignPose.ToEuler(local);
            p.Side = MathF.Abs(rigid[i].Translation.X) < 0.02f ? 0 : MathF.Sign(rigid[i].Translation.X);
        }
        d.Parts[root].Role = PartRole.Body;
        d.Parts[root].Name = "BODY";
        // The game's model is one piece of anatomy. Every mesh in it can be bent at its joint,
        // sized, stretched and painted, but none of it comes off — a forearm does not leave
        // the soldier's elbow because it was dragged.
        foreach (var p in d.Parts) p.Fixed = p.Parent >= 0;

        // ---- twins: mirror images across the centre line ----------------------------------
        for (int i = 0; i < n; i++)
        {
            if (d.Parts[i].Twin >= 0 || d.Parts[i].Side == 0) continue;
            Vector3 o = rigid[i].Translation;
            for (int j = i + 1; j < n; j++)
            {
                if (d.Parts[j].Twin >= 0 || d.Parts[j].Side != -d.Parts[i].Side) continue;
                if (d.Parts[j].Blocks[0].Tris!.Length != d.Parts[i].Blocks[0].Tris!.Length) continue;
                Vector3 m = rigid[j].Translation;
                if (Vector3.Distance(o with { X = -o.X }, m) > 0.03f) continue;
                d.Parts[i].Twin = j;
                d.Parts[j].Twin = i;
                break;
            }
        }
        foreach (var p in d.Parts.Where(p => p.Side != 0))
            p.Name = $"PIECE {p.Id + 1} {(p.Side < 0 ? "L" : "R")}";

        // ---- the idle, from the samples -----------------------------------------------------
        d.Clips.Add(Record("IDLE", IdleSeconds, d, samples, shots, parent, scale0, extent0, wholeTurns: false));

        // The walk, measured against the same rest pose. The gem's turn is a whole number of
        // revolutions a loop, so it is written as a spin rather than keys: keys would wind it
        // back round the long way at the seam.
        if (e.Move is { } walk)
            d.Clips.Add(Record("MOVE", walk.Seconds, d, RunWalk(walk, er), shots, parent, scale0, extent0, wholeTurns: true));

        // The creator's own built-ins (MOVE, ATTACK...) join it; the game's idle keeps its name
        // and wins over the generated one.
        AutoAnim.Rebuild(d);
        d.Sanitize();
        return d;
    }

    /// <summary>
    /// Runs the showcase the way the game does — one draw every sixtieth of a second, from a
    /// fresh figure, through a second of settling — and keeps the draws that fall on the
    /// sample instants. Anything stateful in a model (the soldier's idle eases; it does not
    /// jump) then comes out as the game shows it.
    /// </summary>
    public static List<List<MeshRecorder.Shot>> Run(Entry e, EntityRenderer er)
    {
        var samples = new List<List<MeshRecorder.Shot>>();
        er.ResetShowcaseFigure();
        er.ShowcaseStep = Step;
        try
        {
            int frame = 0;
            for (int k = 0; k < Samples; k++)
            {
                int target = SampleFrame(k);
                for (; frame < target; frame++) MeshRecorder.Record(() => e.Draw(er, FrameTime(frame)));
                samples.Add(MeshRecorder.Record(() => e.Draw(er, FrameTime(frame))));
                frame++;
            }
        }
        finally { er.ShowcaseStep = null; }
        return samples;
    }

    private const float Step = 1f / 60f;
    /// <summary>A second of settling before the first sample.</summary>
    private const int Settle = 60;
    private static int SampleFrame(int k) => Settle + k * (int)(IdleSeconds * 60f) / Samples;
    private static float FrameTime(int frame) => frame * Step;

    /// <summary>The showcase-clock time of sample <paramref name="k"/>.</summary>
    public static float SampleTime(int k) => FrameTime(SampleFrame(k));
    public const int SampleCount = Samples;

    /// <summary>
    /// Writes a run of samples down as a looping clip: each part's motion relative to where
    /// it sits in <paramref name="rest"/>, the draw the design's rest pose was taken from.
    /// </summary>
    private static Clip Record(string name, float seconds, Design d, List<List<MeshRecorder.Shot>> samples,
        List<MeshRecorder.Shot> rest, int[] parent, Vector3[] scale0, Vector3[] extent0, bool wholeTurns)
    {
        var clip = new Clip { Name = name, Duration = seconds, Loop = true };
        int n = d.Parts.Count;
        int count = samples.Count;
        var restSplit = rest.Select(s => Split(s.World)).ToArray();
        var rot0 = new Vector3[n];
        var move0 = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var local = Relative(restSplit[i].Rot, parent[i] >= 0 ? restSplit[parent[i]].Rot : Matrix4x4.Identity);
            rot0[i] = DesignPose.ToEuler(local);
            move0[i] = local.Translation;
        }
        var rot = new Vector3[n][];
        var move = new Vector3[n][];
        var stretch = new Vector3[n][];
        for (int i = 0; i < n; i++) { rot[i] = new Vector3[count]; move[i] = new Vector3[count]; stretch[i] = new Vector3[count]; }

        for (int k = 0; k < count; k++)
        {
            var shots = samples[k];
            // A draw with a different number of meshes (something blinked in for a moment)
            // cannot be matched piece for piece; it holds the previous sample instead.
            if (shots.Count != n)
            {
                for (int i = 0; i < n; i++)
                {
                    rot[i][k] = k > 0 ? rot[i][k - 1] : rot0[i];
                    move[i][k] = k > 0 ? move[i][k - 1] : move0[i];
                    stretch[i][k] = k > 0 ? stretch[i][k - 1] : Vector3.One;
                }
                continue;
            }
            var split = shots.Select(s => Split(s.World)).ToArray();
            for (int i = 0; i < n; i++)
            {
                var local = Relative(split[i].Rot, parent[i] >= 0 ? split[parent[i]].Rot : Matrix4x4.Identity);
                var euler = DesignPose.ToEuler(local);
                // Unwrap, so a part spinning through half a turn reads as turning on rather
                // than jumping back round.
                euler = Unwrap(euler, k > 0 ? rot[i][k - 1] : rot0[i]);
                rot[i][k] = euler;
                move[i][k] = local.Translation;
                // How much bigger the piece is than in the first sample, along each of its own
                // axes: the draw's own scale, and the mesh itself if it was rebuilt longer.
                stretch[i][k] = Ratio(split[i].Scale * Extent(shots[i].Mesh), scale0[i] * extent0[i]);
            }
        }

        for (int i = 0; i < n; i++)
        {
            var p = d.Parts[i];
            Vector3 r0 = rot0[i], m0 = move0[i];
            AddTurn(clip, p.Id, Channel.Pitch, rot[i].Select(v => v.X - r0.X).ToArray(), wholeTurns);
            AddTurn(clip, p.Id, Channel.Yaw, rot[i].Select(v => v.Y - r0.Y).ToArray(), wholeTurns);
            AddTurn(clip, p.Id, Channel.Roll, rot[i].Select(v => v.Z - r0.Z).ToArray(), wholeTurns);
            AddChannel(clip, p.Id, Channel.MoveX, move[i].Select(v => v.X - m0.X).ToArray(), 0.002f);
            AddChannel(clip, p.Id, Channel.MoveY, move[i].Select(v => v.Y - m0.Y).ToArray(), 0.002f);
            AddChannel(clip, p.Id, Channel.MoveZ, move[i].Select(v => v.Z - m0.Z).ToArray(), 0.002f);
            AddChannel(clip, p.Id, Channel.StretchX, stretch[i].Select(v => v.X - 1f).ToArray(), 0.004f);
            AddChannel(clip, p.Id, Channel.StretchY, stretch[i].Select(v => v.Y - 1f).ToArray(), 0.004f);
            AddChannel(clip, p.Id, Channel.StretchZ, stretch[i].Select(v => v.Z - 1f).ToArray(), 0.004f);
        }
        return clip;
    }

    private static Vector3 Extent(PolyMesh mesh)
    {
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var (a, b, c, _) in mesh.Triangles)
        {
            lo = Vector3.Min(lo, Vector3.Min(a, Vector3.Min(b, c)));
            hi = Vector3.Max(hi, Vector3.Max(a, Vector3.Max(b, c)));
        }
        return lo.X > hi.X ? Vector3.One : hi - lo;
    }

    private static Vector3 Ratio(Vector3 a, Vector3 b)
    {
        static float R(float x, float y) => MathF.Abs(y) < 1e-5f ? 1f : MathF.Abs(x / y);
        return new Vector3(R(a.X, b.X), R(a.Y, b.Y), R(a.Z, b.Z));
    }

    /// <summary>
    /// A rotation channel. With <paramref name="wholeTurns"/>, one that comes all the way
    /// round over the loop (a part spinning a whole number of turns) is written as a spin,
    /// which carries on round at the seam instead of unwinding back to where it started.
    /// </summary>
    private static void AddTurn(Clip clip, int part, Channel ch, float[] values, bool wholeTurns)
    {
        if (wholeTurns && values.Length > 1)
        {
            // The drift over the whole loop: first to last sample, plus the one more step
            // that closes it.
            float drift = values[^1] - values[0] + (values[^1] - values[^2]);
            float turns = MathF.Round(drift / MathF.Tau);
            if (turns != 0f && MathF.Abs(drift - turns * MathF.Tau) < 0.35f)
            {
                clip.Tracks.Add(new Track
                {
                    Part = part, Channel = ch, Mode = TrackMode.Spin, Amp = turns, Cycles = 1,
                    Offset = values[0],
                });
                return;
            }
        }
        AddChannel(clip, part, ch, values, 0.004f);
    }

    private static void AddChannel(Clip clip, int part, Channel ch, float[] values, float still)
    {
        if (values.Max() - values.Min() < still) return;
        var tr = new Track { Part = part, Channel = ch, Mode = TrackMode.Keys };
        for (int k = 0; k < values.Length; k++)
            tr.Keys.Add(new Key { T = (float)k / values.Length, V = MathF.Round(values[k] * 10000f) / 10000f });
        clip.Tracks.Add(tr);
    }

    private static Vector3 Unwrap(Vector3 v, Vector3 prev)
    {
        static float U(float a, float p)
        {
            while (a - p > MathF.PI) a -= MathF.Tau;
            while (a - p < -MathF.PI) a += MathF.Tau;
            return a;
        }
        return new Vector3(U(v.X, prev.X), U(v.Y, prev.Y), U(v.Z, prev.Z));
    }

    /// <summary>Splits a draw transform into its per-axis scale and the rigid rest.</summary>
    private static (Vector3 Scale, Matrix4x4 Rot) Split(Matrix4x4 m)
    {
        var x = new Vector3(m.M11, m.M12, m.M13);
        var y = new Vector3(m.M21, m.M22, m.M23);
        var z = new Vector3(m.M31, m.M32, m.M33);
        var s = new Vector3(x.Length(), y.Length(), z.Length());
        if (s.X < 1e-6f || s.Y < 1e-6f || s.Z < 1e-6f) return (Vector3.One, m);
        x /= s.X; y /= s.Y; z /= s.Z;
        // A mirrored draw (a negative scale) leaves a reflection in the rotation, which no set
        // of angles can express. The mirror moves into the scale, which is baked into the
        // triangles, and the rotation stays a rotation.
        if (Vector3.Dot(Vector3.Cross(x, y), z) < 0f) { x = -x; s.X = -s.X; }
        var r = new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, m.M41, m.M42, m.M43, 1);
        return (s, r);
    }

    private static Matrix4x4 Relative(Matrix4x4 child, Matrix4x4 parent)
        => Matrix4x4.Invert(parent, out var inv) ? child * inv : child;

    private static float Volume(Part p)
    {
        var (lo, hi) = DesignPose.PartBounds(p);
        var s = Vector3.Max(hi - lo, new Vector3(0.05f));
        return s.X * s.Y * s.Z;
    }

    private static float DistanceToBounds(Vector3 point, Part q, Matrix4x4 qWorld)
    {
        if (!Matrix4x4.Invert(qWorld, out var inv)) return float.MaxValue;
        var local = Vector3.Transform(point, inv);
        var (lo, hi) = DesignPose.PartBounds(q);
        var clamped = Vector3.Clamp(local, lo, hi);
        return Vector3.Distance(local, clamped);
    }
}
