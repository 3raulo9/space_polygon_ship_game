using System.Numerics;
using Unrendered.Creator;
using Unrendered.Rendering;

namespace Unrendered.Core;

/// <summary>
/// The creator: the parts of it a screenshot cannot vouch for. That a game character opened
/// in it is the game's character to the triangle, and moves as the game moves it; that a
/// saved design comes back exactly, edited animations and all; that saving never lands on a
/// file it was not asked to replace; that the spine grows and shrinks as it is pulled; that
/// legs dropped on a body walk out of step; and that mirroring reflects rather than copies.
/// </summary>
public static partial class SelfTest
{
    private static int RunCreatorChecks()
    {
        int failures = 0;
        failures += Check("every game character copies into the creator exactly", CreatorCopiesGameModelsExactly);
        failures += Check("a copied character idles exactly as the game's does", CreatorCopiesGameIdle);
        failures += Check("the copied spider walks exactly as the Crab-Core does", CreatorCopiesSpiderWalk);
        failures += Check("a design survives saving with its edited animations", CreatorRoundTripsEditedAnimation);
        failures += Check("saving a copy never overwrites another file", CreatorSaveNeverClobbers);
        failures += Check("the spine grows when pulled and shrinks when pushed", CreatorSpineGrowsAndShrinks);
        failures += Check("legs dropped on a spine walk out of step", CreatorGaitsAreOutOfStep);
        failures += Check("built-in loops join up at the seam", CreatorLoopsAreSeamless);
        failures += Check("mirroring reflects a limb and links the twins", CreatorMirrorReflects);
        failures += Check("turns survive being taken apart and put back", CreatorEulerRoundTrips);
        failures += Check("a keyed curve passes through every key", CreatorKeysAreExact);
        failures += Check("a broken file is repaired, not trusted", CreatorSanitizesBadFiles);
        return failures;
    }

    /// <summary>Every triangle the game drew for a character in one recorded frame, in the world.</summary>
    private static List<Vector3> GameTriangles(List<MeshRecorder.Shot> shots)
    {
        var pts = new List<Vector3>();
        foreach (var shot in shots)
            foreach (var (a, b, c, _) in shot.Mesh.Triangles)
            {
                pts.Add(Vector3.Transform(a, shot.World));
                pts.Add(Vector3.Transform(b, shot.World));
                pts.Add(Vector3.Transform(c, shot.World));
            }
        return pts;
    }

    /// <summary>The same, from the creator's copy at a moment in a clip.</summary>
    private static List<Vector3> DesignTriangles(Design d, Clip? clip, float t)
    {
        var pose = DesignPose.Evaluate(d, clip, t, Matrix4x4.Identity);
        var pts = new List<Vector3>();
        foreach (var p in d.Parts.OrderBy(p => p.Id))
            foreach (var b in p.Blocks)
            {
                var m = DesignPose.BlockMatrix(p, b, pose[p.Id]);
                foreach (var (a, bb, c, _) in BlockGeometry.Triangles(b))
                {
                    pts.Add(Vector3.Transform(a, m));
                    pts.Add(Vector3.Transform(bb, m));
                    pts.Add(Vector3.Transform(c, m));
                }
            }
        return pts;
    }

    private static float WorstGap(List<Vector3> a, List<Vector3> b)
    {
        if (a.Count != b.Count) return float.MaxValue;
        float worst = 0f;
        for (int i = 0; i < a.Count; i++) worst = MathF.Max(worst, Vector3.Distance(a[i], b[i]));
        return worst;
    }

    private static readonly EntityRenderer CaptureSource = new();
    private static readonly Dictionary<string, Design> Copies = new();

    private static Design CopyOf(GameCapture.Entry e)
    {
        GameCapture.Entities = CaptureSource;
        if (!Copies.TryGetValue(e.Id, out var d)) Copies[e.Id] = d = GameCapture.Capture(e);
        return d.Clone();
    }

    private static string? CreatorCopiesGameModelsExactly()
    {
        foreach (var e in GameCapture.All)
        {
            var d = CopyOf(e);
            if (d.Parts.Count == 0) return $"{e.Label} came out empty";
            // The rest pose is the idle's first sample. The game is run again from scratch,
            // the way it runs in the hangar, and must land where the copy says.
            var game = GameTriangles(GameCapture.Run(e, CaptureSource)[0]);
            var mine = DesignTriangles(d, null, 0f);
            float gap = WorstGap(game, mine);
            if (gap > 2e-3f)
                return gap == float.MaxValue
                    ? $"{e.Label}: the game draws {game.Count / 3} triangles, the copy {mine.Count / 3}"
                    : $"{e.Label}: a corner is {gap:F4} away from where the game puts it";
            if (d.Parts.Count(p => p.Parent < 0) != 1) return $"{e.Label} has more than one root";
        }
        return null;
    }

    private static string? CreatorCopiesGameIdle()
    {
        foreach (var e in GameCapture.All)
        {
            var d = CopyOf(e);
            var idle = d.Clips.FirstOrDefault(c => c.Name == "IDLE" && !c.BuiltIn);
            if (idle == null) return $"{e.Label} lost its idle";
            // The curve passes through each sample, so at a sample instant the copy must be
            // exactly where the game was.
            var run = GameCapture.Run(e, CaptureSource);
            foreach (int k in new[] { 5, 13, 27 })
            {
                float t = (float)k / GameCapture.SampleCount;
                var game = GameTriangles(run[k]);
                var mine = DesignTriangles(d, idle, t);
                if (game.Count != mine.Count) continue; // a frame where something blinked in
                float gap = WorstGap(game, mine);
                if (gap > 0.01f) return $"{e.Label} at {t:F2} of its idle is {gap:F3} off the game";
            }
        }
        return null;
    }

    private static string? CreatorCopiesSpiderWalk()
    {
        var e = GameCapture.All.First(x => x.Id == "spider");
        if (e.Move is not { } walk) return "the spider has no walk to copy";
        var d = CopyOf(e);
        var move = d.Clips.FirstOrDefault(c => c.Name == "MOVE" && !c.BuiltIn);
        if (move == null) return "the copy walks with a made-up gait instead of the game's";

        // At each sample instant the copy must be where the game drew it, and that includes
        // the seam: the last sample has to run on into the first, gem and all.
        var run = GameCapture.RunWalk(walk, CaptureSource);
        foreach (int k in new[] { 0, 7, 19, 30, GameCapture.WalkSamples - 1 })
        {
            float t = (float)k / GameCapture.WalkSamples;
            var game = GameTriangles(run[k]);
            var mine = DesignTriangles(d, move, t);
            float gap = WorstGap(game, mine);
            if (gap > 0.01f) return $"at {t:F2} of its walk the copy is {gap:F3} off the game";
        }

        // And it really walks: a foot at the top of its step stands clear of where it plants.
        float lift = 0f;
        var rest = DesignTriangles(d, null, 0f);
        foreach (var shots in run)
        {
            var at = GameTriangles(shots);
            for (int i = 0; i < at.Count && i < rest.Count; i++) lift = MathF.Max(lift, at[i].Y - rest[i].Y);
        }
        return lift < 0.1f ? $"the walking legs only rise {lift:F2}" : null;
    }

    private static string? CreatorRoundTripsEditedAnimation()
    {
        var d = CopyOf(GameCapture.All.First(e => e.Id == "soldier"));
        // The edits a user would make: retune the game's idle, key a new clip by hand.
        var idle = d.Clips.First(c => c.Name == "IDLE");
        idle.Tracks[0].Keys[3].V += 0.5f;
        idle.Duration = 3.3f;
        idle.Edited = true;
        var wave = new Clip { Name = "WAVE HELLO", Duration = 2f, Loop = false };
        int arm = d.Parts.First(p => p.Parent >= 0).Id;
        wave.Tracks.Add(new Track
        {
            Part = arm, Channel = Channel.Roll, Mode = TrackMode.Keys,
            Keys = { new Key { T = 0f, V = 0f }, new Key { T = 0.4f, V = 2.5f }, new Key { T = 1f, V = 0f } },
        });
        d.Clips.Add(wave);
        d.Parts.First(p => p.Parent >= 0).Scale = 1.7f;
        d.Name = "MY SOLDIER";

        string before = DesignStore.ToJson(d);
        var back = DesignStore.FromJson(before);
        if (DesignStore.ToJson(back) != before) return "the file changed on a round trip";

        var i2 = back.Clips.First(c => c.Name == "IDLE");
        if (i2.Duration != 3.3f || MathF.Abs(i2.Tracks[0].Keys[3].V - idle.Tracks[0].Keys[3].V) > 1e-6f)
            return "the retuned idle came back as the original";
        var w = back.Clips.FirstOrDefault(c => c.Name == "WAVE HELLO");
        if (w == null || w.Loop || w.Tracks.Count != 1 || w.Tracks[0].Keys.Count != 3) return "the hand-keyed clip was lost";
        if (DesignTriangles(back, null, 0f).Count != DesignTriangles(d, null, 0f).Count) return "the game's meshes did not survive the file";
        return null;
    }

    private static string? CreatorSaveNeverClobbers()
    {
        string old = DesignStore.Folder;
        string dir = Path.Combine(Path.GetTempPath(), "unrendered-creator-" + Guid.NewGuid().ToString("N"));
        DesignStore.Folder = dir;
        try
        {
            var d = DesignTemplates.Blank(DesignKind.Vehicle);
            string a = DesignStore.Save(d, null);
            string b = DesignStore.Save(d, null);
            if (a == b) return "two saves of a new design wrote the same file";
            if (!File.Exists(a) || !File.Exists(b)) return "a save did not reach the disk";

            d.Name = "RENAMED";
            string c = DesignStore.Save(d, a);
            if (c != a) return "an overwrite went somewhere else";
            if (DesignStore.FromJson(File.ReadAllText(b)).Name != "NEW VEHICLE") return "the other copy was touched";
            if (DesignStore.List().Count != 2) return $"listed {DesignStore.List().Count} files, expected 2";
            return null;
        }
        finally
        {
            DesignStore.Folder = old;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static string? CreatorSpineGrowsAndShrinks()
    {
        var d = Spine.NewCreature();
        int start = Spine.Vertebrae(d).Count;
        var head = Spine.Vertebrae(d)[^1];
        // Pull the front end well forward, in steps, the way a drag arrives.
        for (int i = 1; i <= 10; i++) Spine.Drag(d, head, head.Pivot with { Z = head.Pivot.Z + 0.15f });
        int grown = Spine.Vertebrae(d).Count;
        if (grown <= start) return $"pulling the head out left {grown} vertebrae (was {start})";
        if (Spine.Vertebrae(d)[^1] != head) return "the vertebra in hand stopped being the end";

        // Neighbours stay close enough to read as one body.
        var v = Spine.Vertebrae(d);
        for (int i = 1; i < v.Count; i++)
            if (Vector3.Distance(v[i].Pivot, v[i - 1].Pivot) > Spine.Spacing * 1.7f) return "the spine opened a gap";

        // Push it back in.
        for (int i = 0; i < 20; i++)
        {
            var prev = Spine.Vertebrae(d)[^2];
            Spine.Drag(d, head, Vector3.Lerp(head.Pivot, prev.Pivot, 0.7f));
        }
        if (Spine.Vertebrae(d).Count >= grown) return "pushing the head in did not shorten the spine";
        if (Spine.Vertebrae(d).Count < 2) return "the spine collapsed below two";

        // Fattening one stretch.
        var mid = Spine.Vertebrae(d)[0];
        float before = mid.Blocks[0].Size.X;
        Spine.Girth(d, mid, 1.5f);
        if (!(mid.Blocks[0].Size.X > before)) return "the wheel did not fatten the vertebra";
        return null;
    }

    /// <summary>Two legs, as the shapes bar would put them: one placed, its twin mirrored.</summary>
    private static Design CreatureWithLegs(int pairs)
    {
        var d = Spine.NewCreature();
        var leg = PartCatalog.All.First(i => i.Label == "LEG");
        var v = Spine.Vertebrae(d);
        for (int i = 0; i < pairs; i++)
        {
            var root = PartCatalog.Instantiate(d, leg);
            var on = v[i * (v.Count - 1) / Math.Max(1, pairs - 1)];
            root.Parent = on.Id;
            root.Pivot = new Vector3(-0.35f, -0.2f, 0);
            root.Side = -1;
            d.MirrorSubtree(root.Id);
        }
        AutoAnim.Rebuild(d);
        return d;
    }

    private static string? CreatorGaitsAreOutOfStep()
    {
        var biped = CreatureWithLegs(1);
        var ph = AutoAnim.GaitPhases(biped);
        if (ph.Count != 2) return $"found {ph.Count} hips on a biped";
        var vals = ph.Values.ToArray();
        if (MathF.Abs(MathF.Abs(vals[0] - vals[1]) - 0.5f) > 1e-4f) return $"the two legs step at {vals[0]} and {vals[1]}";

        var quad = CreatureWithLegs(2);
        var qp = AutoAnim.GaitPhases(quad);
        if (qp.Count != 4) return $"found {qp.Count} hips on a quadruped";
        // A trot: diagonal pairs together.
        var hips = quad.Parts.Where(p => qp.ContainsKey(p.Id)).ToList();
        foreach (var h in hips)
        {
            var diagonal = hips.Single(o => o.Side == -h.Side && o.Parent != h.Parent && o.Parent != quad.Find(h.Parent)?.Twin);
            if (MathF.Abs(qp[h.Id] - qp[diagonal.Id]) > 1e-4f) return "a quadruped's diagonal legs are out of step";
        }
        if (quad.Clips.First(c => c.Name == "MOVE").Tracks.Count(t => qp.ContainsKey(t.Part)) < 4) return "not every leg is driven";
        return null;
    }

    private static string? CreatorLoopsAreSeamless()
    {
        var designs = new[] { CreatureWithLegs(2), DesignTemplates.Blank(DesignKind.Vehicle) };
        foreach (var d in designs)
            foreach (var c in d.Clips.Where(c => c.Loop && c.BuiltIn))
            {
                var a = DesignPose.Evaluate(d, c, 0f, Matrix4x4.Identity);
                var b = DesignPose.Evaluate(d, c, 0.99999f, Matrix4x4.Identity);
                foreach (var p in d.Parts)
                {
                    var probe = new Vector3(0.5f, 0.5f, 0.5f);
                    float gap = Vector3.Distance(Vector3.Transform(probe, a[p.Id]), Vector3.Transform(probe, b[p.Id]));
                    if (gap > 0.01f) return $"{d.Name}'s {c.Name} jumps {gap:F3} at {p.Name}";
                }
            }
        return null;
    }

    private static string? CreatorMirrorReflects()
    {
        var d = DesignTemplates.Blank(DesignKind.Creature);
        int body = Spine.Vertebrae(d)[1].Id;
        var arm = new Part
        {
            Id = d.NextPartId(), Name = "ARM L", Parent = body, Role = PartRole.Arm, Side = -1,
            Pivot = new(-0.6f, 0.2f, 0.1f), Rest = new(0.1f, 0.2f, 0.3f), Mount = Vector3.Normalize(new(-1, 1, 0)),
            Blocks = { new Block { Center = new(-0.2f, -0.3f, 0f), Size = new(0.2f, 0.6f, 0.2f) } },
        };
        d.Parts.Add(arm);
        int copyId = d.MirrorSubtree(arm.Id);
        var copy = d.Find(copyId)!;
        if (copy.Name != "ARM R" || copy.Side != 1) return $"the copy is {copy.Name} on side {copy.Side}";
        if (copy.Pivot != new Vector3(0.6f, 0.2f, 0.1f)) return $"the copy's joint is at {copy.Pivot}";
        if (copy.Rest != new Vector3(0.1f, -0.2f, -0.3f)) return $"the copy's rest is {copy.Rest}";
        if (MathF.Abs(copy.Mount.X - -arm.Mount.X) > 1e-6f) return "the copy stands on the wrong side of its surface";
        if (arm.Twin != copy.Id || copy.Twin != arm.Id) return "the twins are not linked";

        // The world shape really is a mirror image: every corner of one, reflected, is a
        // corner of the other.
        var pose = DesignPose.Evaluate(d, null, 0f, Matrix4x4.Identity);
        var ca = Vector3.Transform(arm.Blocks[0].Center, DesignPose.BlockMatrix(arm, arm.Blocks[0], pose[arm.Id]) * Matrix4x4.Identity);
        var cb = Vector3.Transform(copy.Blocks[0].Center, DesignPose.BlockMatrix(copy, copy.Blocks[0], pose[copy.Id]));
        if (Vector3.Distance(ca with { X = -ca.X }, cb) > 1e-4f) return "the mirrored arm is not where the reflection says";

        arm.Scale = 1.6f;
        arm.Pivot = arm.Pivot with { Y = 0.4f };
        d.SyncTwin(arm);
        if (copy.Scale != 1.6f || copy.Pivot.Y != 0.4f) return "an edit did not follow onto the twin";

        foreach (var (from, to) in new[] { ("WHEEL L2", "WHEEL R2"), ("SHIN R", "SHIN L"), ("LEG", "LEG"), ("ROLL", "ROLL") })
            if (Creator.Design.SwapSide(from) != to) return $"{from} mirrors to {Creator.Design.SwapSide(from)}";
        return null;
    }

    private static string? CreatorEulerRoundTrips()
    {
        var rng = new Random(7);
        for (int i = 0; i < 200; i++)
        {
            float y = (float)(rng.NextDouble() * 6 - 3), p = (float)(rng.NextDouble() * 3 - 1.5), r = (float)(rng.NextDouble() * 6 - 3);
            var m = Matrix4x4.CreateFromYawPitchRoll(y, p, r);
            var e = DesignPose.ToEuler(m);
            var back = Matrix4x4.CreateFromYawPitchRoll(e.Y, e.X, e.Z);
            var probe = new Vector3(0.3f, -0.7f, 1.1f);
            if (Vector3.Distance(Vector3.Transform(probe, m), Vector3.Transform(probe, back)) > 1e-3f)
                return $"yaw {y:F2} pitch {p:F2} roll {r:F2} came back different";
        }
        return null;
    }

    private static string? CreatorKeysAreExact()
    {
        foreach (bool loop in new[] { true, false })
        {
            var t = new Track { Mode = TrackMode.Keys };
            float[] ts = { 0f, 0.15f, 0.4f, 0.75f };
            float[] vs = { 0.2f, -1f, 2.5f, 0.3f };
            for (int i = 0; i < ts.Length; i++) t.Keys.Add(new Key { T = ts[i], V = vs[i] });
            for (int i = 0; i < ts.Length; i++)
                if (MathF.Abs(t.Sample(ts[i], loop) - vs[i]) > 1e-4f) return $"key {i} plays as {t.Sample(ts[i], loop):F3} (loop {loop})";
            if (loop && MathF.Abs(t.Sample(0f, true) - t.Sample(0.99999f, true)) > 0.01f) return "a looping curve does not join up";
        }
        return null;
    }

    private static string? CreatorSanitizesBadFiles()
    {
        // A cycle, a dangling parent, a track on a missing part, a negative size and a mesh
        // whose triangles do not add up.
        const string json = """
        {
          "Name": "",
          "Parts": [
            { "Id": 1, "Parent": 2, "Blocks": [ { "Size": { "X": -1, "Y": 1, "Z": 1 } } ] },
            { "Id": 2, "Parent": 1, "Blocks": [ { "Shape": "Mesh", "Tris": [1, 2, 3], "Colors": [] } ] },
            { "Id": 3, "Parent": 99, "Mount": { "X": 0, "Y": 0, "Z": 0 }, "Blocks": [] }
          ],
          "Clips": [ { "Name": "X", "Duration": 0, "Tracks": [ { "Part": 42 } ] } ]
        }
        """;
        var d = DesignStore.FromJson(json);
        if (d.Name != "UNTITLED") return "a blank name was kept";
        if (d.Find(3)!.Parent != -1) return "a parent that does not exist was kept";
        if (d.TreeOrder().Count != 3) return $"the tree walks {d.TreeOrder().Count} of 3 parts - a cycle survived";
        if (d.Clips[0].Tracks.Count != 0) return "a track on a missing part was kept";
        if (d.Clips[0].Duration <= 0f) return "a zero-length clip was kept";
        if (d.Find(1)!.Blocks[0].Size.X <= 0f) return "a negative size was kept";
        if (d.Find(2)!.Blocks[0].Shape != BlockShape.Box) return "a broken mesh was kept";
        if (d.Find(3)!.Mount != Vector3.UnitY) return "a zero mounting was kept";
        return null;
    }
}
