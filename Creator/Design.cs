using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Unrendered.Core;

namespace Unrendered.Creator;

/// <summary>Whether a design walks or rolls. Decides which built-in animations it is given
/// and how they move it: a creature gets a gait, a vehicle gets wheels and a turret.</summary>
public enum DesignKind { Creature, Vehicle }

/// <summary>
/// What a part is <em>for</em>. The role is how the built-in animator knows a leg from a
/// tail without being told: legs get a gait, arms counter-swing them, heads steady
/// themselves, wheels spin, turrets sweep. Changing a part's role and rebuilding the
/// built-ins is how a body that has been reshaped gets a walk that fits it.
/// </summary>
public enum PartRole
{
    Body, Head, Neck, Leg, Foot, Arm, Hand, Tail, Wing, Fin,
    Wheel, Tread, Turret, Barrel, Stalk, Petal, Core, Decor,
    /// <summary>One vertebra of a Spore-style spine: dragged to bend and lengthen the torso,
    /// wheeled to fatten it. See <see cref="Spine"/>.</summary>
    Spine,
    /// <summary>Looks about while idle; holds still while walking.</summary>
    Eye,
    /// <summary>A jaw, beak or mandible: gapes on the attack and works a little at rest.</summary>
    Mouth,
    /// <summary>A rigid detail — a horn, a plate, a spike — that moves only with what it is
    /// stuck to. Unlike <see cref="Decor"/>, it does not writhe.</summary>
    Detail,
}

/// <summary>
/// How a block's two colours are laid over it, the way Spore lays a coat and a detail over a
/// base. Worked out per facet: a patterned block is cut into smaller triangles and each one
/// takes the base or the mark, so a pattern is a few more hard-edged flats rather than a
/// texture.
/// </summary>
public enum PaintPattern
{
    Solid,
    /// <summary>The underside in the mark colour: countershading.</summary>
    Belly,
    /// <summary>Bands across the length.</summary>
    Stripes,
    /// <summary>Bands round the height.</summary>
    Rings,
    Spots,
    /// <summary>The ends and edges in the mark colour.</summary>
    Tips,
    /// <summary>A stripe down the middle of the back.</summary>
    Dorsal,
    Checker,
}

/// <summary>The shape a block is cut to. Everything stays a hard-edged solid — these are
/// the few departures from a plain box the art direction allows.</summary>
public enum BlockShape
{
    /// <summary>A box, optionally tapering toward its top by <see cref="Block.Taper"/>.</summary>
    Box,
    /// <summary>A ramp: full height at the back, sloping to nothing at the front (+Z).</summary>
    Wedge,
    /// <summary>A double pyramid — gems, cores, cones.</summary>
    Octa,
    /// <summary>Triangles copied from one of the game's own meshes, carried in the file so
    /// the design does not depend on the game's code staying the same.</summary>
    Mesh,
}

/// <summary>One solid in a part, in that part's own frame (the part's pivot is the origin).
/// Axis-aligned inside the part: a block turns because its part does.</summary>
public sealed class Block
{
    public Vector3 Center;
    /// <summary>Full size along each axis, in metres. For a mesh block, the size of its
    /// triangles' bounds — change it and the mesh stretches to match.</summary>
    public Vector3 Size = new(0.4f, 0.4f, 0.4f);
    /// <summary>How big the top face is relative to the bottom: 1 is a box, 0 is a pyramid.</summary>
    public float Taper = 1f;
    public BlockShape Shape;
    /// <summary>Index into <see cref="ClassCatalog.Swatches"/>, the game's closed palette. -1
    /// keeps a mesh block's own colours.</summary>
    public int Swatch;
    /// <summary>The second colour, which <see cref="Pattern"/> lays over the first.</summary>
    public int Swatch2 = 9;
    public PaintPattern Pattern;

    /// <summary>Mesh blocks only: nine floats per triangle, in the block's own frame.</summary>
    public float[]? Tris;
    /// <summary>Mesh blocks only: one packed RGBA colour per triangle.</summary>
    public uint[]? Colors;

    /// <summary>The bounds of <see cref="Tris"/> — what <see cref="Size"/> is measured
    /// against to stretch the mesh.</summary>
    [JsonIgnore]
    public (Vector3 Min, Vector3 Max) MeshBounds
    {
        get
        {
            if (Tris == null || Tris.Length < 9) return (-Size * 0.5f, Size * 0.5f);
            var lo = new Vector3(float.MaxValue);
            var hi = new Vector3(float.MinValue);
            for (int i = 0; i + 2 < Tris.Length; i += 3)
            {
                var v = new Vector3(Tris[i], Tris[i + 1], Tris[i + 2]);
                lo = Vector3.Min(lo, v);
                hi = Vector3.Max(hi, v);
            }
            return (lo, hi);
        }
    }

    /// <summary>How much a mesh block is stretched from its natural size, per axis. A flat
    /// mesh has no natural size along its thin axis; that axis is left alone.</summary>
    [JsonIgnore]
    public Vector3 MeshScale
    {
        get
        {
            var (lo, hi) = MeshBounds;
            Vector3 nat = hi - lo;
            return new(nat.X > 1e-4f ? Size.X / nat.X : 1f, nat.Y > 1e-4f ? Size.Y / nat.Y : 1f, nat.Z > 1e-4f ? Size.Z / nat.Z : 1f);
        }
    }

    /// <summary>Packs a mesh's triangles into a block whose size is the mesh's natural one.</summary>
    public static Block FromTriangles(IEnumerable<(Vector3 A, Vector3 B, Vector3 C, Raylib_cs.Color Color)> tris, int swatch = -1)
    {
        var f = new List<float>();
        var c = new List<uint>();
        foreach (var (a, b, cc, col) in tris)
        {
            f.AddRange(new[] { a.X, a.Y, a.Z, b.X, b.Y, b.Z, cc.X, cc.Y, cc.Z });
            c.Add((uint)(col.R << 24 | col.G << 16 | col.B << 8 | col.A));
        }
        var block = new Block { Shape = BlockShape.Mesh, Tris = f.ToArray(), Colors = c.ToArray(), Swatch = swatch };
        var (lo, hi) = block.MeshBounds;
        block.Size = hi - lo;
        return block;
    }

    public static Raylib_cs.Color Unpack(uint c) => new((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);

    public Block Clone() => (Block)MemberwiseClone(); // the triangle arrays are never edited in place
}

/// <summary>
/// One jointed piece of the body. Parts form a tree through <see cref="Parent"/>; each one
/// turns about its own <see cref="Pivot"/>, which is placed in its parent's frame, so a
/// shin hung off a thigh swings with the thigh and bends at the knee on its own.
/// </summary>
public sealed class Part
{
    public int Id;
    public string Name = "PART";
    /// <summary>The part this one hangs from, or -1 for a root.</summary>
    public int Parent = -1;
    public PartRole Role;
    /// <summary>-1 left, 0 centre line, +1 right. Gait phases and mirroring read it.</summary>
    public int Side;
    /// <summary>The mirror image of this part on the other side, or -1. Kept in step with
    /// it while symmetry is on.</summary>
    public int Twin = -1;
    /// <summary>The joint, in the parent's frame.</summary>
    public Vector3 Pivot;
    /// <summary>The resting angle at the joint, radians: X pitch, Y yaw, Z roll — turned
    /// about the mounting frame, so on a part stuck to a surface yaw spins it about the
    /// surface's normal.</summary>
    public Vector3 Rest;
    /// <summary>Which way the part's own up points, in the parent's frame. A horn dropped on
    /// the side of a head stands out of the side of the head; a leg keeps hanging down.</summary>
    public Vector3 Mount = Vector3.UnitY;
    /// <summary>Whether dragging the part across a surface re-aims <see cref="Mount"/> at the
    /// surface's normal (spikes, eyes, plates) or leaves it upright (legs, wheels, arms).</summary>
    public bool Stick;
    /// <summary>Uniform size, set with the mouse wheel. Carries down to everything attached,
    /// so a scaled leg keeps its foot.</summary>
    public float Scale = 1f;
    /// <summary>Per-axis stretch from the drag handles. Stretches only this part's blocks; the
    /// joints of parts hung on it ride the stretch so they stay on its surface.</summary>
    public Vector3 Stretch = Vector3.One;
    /// <summary>For a spine vertebra, its place along the spine from the tail (0) forward;
    /// -1 for anything else.</summary>
    public int Vertebra = -1;
    /// <summary>A segment of something bigger — a shin, a forearm, a piece of a game model.
    /// It bends at its joint, sizes and paints like anything else, but it never comes off:
    /// dragging it swings it, and only the limb it belongs to can be moved or thrown away.</summary>
    public bool Fixed;
    public List<Block> Blocks = new();

    public Part Clone()
    {
        var p = (Part)MemberwiseClone();
        p.Blocks = Blocks.Select(b => b.Clone()).ToList();
        return p;
    }
}

/// <summary>Which degree of freedom a track drives. Rotations are radians added to the
/// part's rest angle; moves are metres added to its pivot; stretches are added to 1 and
/// size the part's own blocks along that axis — the virus's veins reaching and pulling back.</summary>
public enum Channel { Pitch, Yaw, Roll, MoveX, MoveY, MoveZ, StretchX, StretchY, StretchZ }

/// <summary>
/// How a track makes its value. Waves are what the built-in gaits are written in, because
/// a walk is a set of oscillators with phase offsets and editing one is a matter of turning
/// four knobs. Keys are there for the things that are not periodic — a lunge, a recoil.
/// </summary>
public enum TrackMode
{
    /// <summary>Offset + Amp · sin(2π(Cycles·t + Phase)).</summary>
    Wave,
    /// <summary>A continuous turn: Amp full revolutions per cycle, Cycles cycles per clip.
    /// Loops seamlessly, which is what a wheel needs.</summary>
    Spin,
    /// <summary>A smooth curve through <see cref="Track.Keys"/>.</summary>
    Keys,
}

public sealed class Key
{
    /// <summary>Normalised time in the clip, 0..1.</summary>
    public float T;
    public float V;
    public Key Clone() => (Key)MemberwiseClone();
}

public sealed class Track
{
    public int Part;
    public Channel Channel;
    public TrackMode Mode;
    public float Amp;
    public int Cycles = 1;
    /// <summary>Fraction of a cycle, 0..1.</summary>
    public float Phase;
    public float Offset;
    public List<Key> Keys = new();

    public Track Clone()
    {
        var t = (Track)MemberwiseClone();
        t.Keys = Keys.Select(k => k.Clone()).ToList();
        return t;
    }

    /// <summary>The track's value at normalised clip time <paramref name="t"/> (0..1).</summary>
    public float Sample(float t, bool loop)
    {
        switch (Mode)
        {
            case TrackMode.Wave:
                return Offset + Amp * MathF.Sin(MathF.Tau * (Cycles * t + Phase));
            case TrackMode.Spin:
                return Offset + Amp * MathF.Tau * Cycles * t;
            default:
                return SampleKeys(t, loop);
        }
    }

    private float SampleKeys(float t, bool loop)
    {
        if (Keys.Count == 0) return Offset;
        if (Keys.Count == 1) return Keys[0].V + Offset;

        // Keys are kept sorted by the editor, but a hand-edited file may not be.
        var keys = Keys.OrderBy(k => k.T).ToList();
        int n = keys.Count;

        // A key at time t, extended past either end of the clip: round the loop when it
        // loops, held flat when it does not.
        (float T, float V) At(int i)
        {
            if (loop)
            {
                int wraps = (int)MathF.Floor((float)i / n);
                var k = keys[((i % n) + n) % n];
                return (k.T + wraps, k.V);
            }
            var c = keys[Math.Clamp(i, 0, n - 1)];
            return (c.T + (i < 0 ? i : i >= n ? i - n + 1 : 0) * 1e-3f, c.V);
        }

        if (!loop && t <= keys[0].T) return keys[0].V + Offset;
        if (!loop && t >= keys[^1].T) return keys[^1].V + Offset;

        // The segment holding t.
        int seg = -1;
        for (int i = 0; i < n; i++) if (keys[i].T <= t) seg = i;
        if (!loop) seg = Math.Max(0, seg);

        var p0 = At(seg - 1);
        var p1 = At(seg);
        var p2 = At(seg + 1);
        var p3 = At(seg + 2);
        float span = p2.T - p1.T;
        float u = span <= 1e-5f ? 1f : Math.Clamp((t - p1.T) / span, 0f, 1f);

        // Catmull-Rom through the keys, with tangents scaled for uneven spacing. Passes
        // through every key exactly and keeps the velocity across it, so a curve made of
        // many keys — one sampled off the game — plays as motion rather than as steps.
        float m1 = (p2.V - p0.V) / MathF.Max(1e-4f, p2.T - p0.T) * span;
        float m2 = (p3.V - p1.V) / MathF.Max(1e-4f, p3.T - p1.T) * span;
        float u2 = u * u, u3 = u2 * u;
        float v = (2 * u3 - 3 * u2 + 1) * p1.V + (u3 - 2 * u2 + u) * m1 + (-2 * u3 + 3 * u2) * p2.V + (u3 - u2) * m2;
        return v + Offset;
    }
}

public sealed class Clip
{
    public string Name = "CLIP";
    /// <summary>Seconds for one pass.</summary>
    public float Duration = 1f;
    public bool Loop = true;
    /// <summary>True for the animations the creator writes itself. Rebuilding the built-ins
    /// replaces these and leaves any clip the user made alone.</summary>
    public bool BuiltIn;
    /// <summary>Set once the user has changed a built-in by hand. The creator rebuilds
    /// built-ins on its own as the body changes, but never one of these.</summary>
    public bool Edited;
    public List<Track> Tracks = new();

    public Clip Clone()
    {
        var c = (Clip)MemberwiseClone();
        c.Tracks = Tracks.Select(t => t.Clone()).ToList();
        return c;
    }
}

/// <summary>
/// A whole creation: the body as a tree of block-built parts, and every animation it owns.
/// Plain data, saved as JSON, so a design can be read, diffed and fixed by hand.
/// </summary>
public sealed class Design
{
    public const int CurrentVersion = 1;

    public int Version = CurrentVersion;
    public string Name = "UNTITLED";
    public DesignKind Kind;
    /// <summary>The chassis whose controls, weapons and hitbox this body will borrow once
    /// creations are playable.</summary>
    public PlayerClass BaseClass;
    /// <summary>The built-in character this was started from, if any. Informational — a
    /// template is never written back to.</summary>
    public string? Template;
    public List<Part> Parts = new();
    public List<Clip> Clips = new();

    public Design Clone()
    {
        var d = (Design)MemberwiseClone();
        d.Parts = Parts.Select(p => p.Clone()).ToList();
        d.Clips = Clips.Select(c => c.Clone()).ToList();
        return d;
    }

    public Part? Find(int id) => Parts.FirstOrDefault(p => p.Id == id);

    public int NextPartId() => Parts.Count == 0 ? 0 : Parts.Max(p => p.Id) + 1;

    /// <summary>The part and everything hanging from it, parent before child.</summary>
    public List<Part> Subtree(int id)
    {
        var result = new List<Part>();
        var root = Find(id);
        if (root == null) return result;
        var stack = new Stack<Part>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            result.Add(p);
            foreach (var c in Parts.Where(c => c.Parent == p.Id).Reverse()) stack.Push(c);
        }
        return result;
    }

    /// <summary>Depth of a part in the tree, for indenting the part list.</summary>
    public int Depth(Part p)
    {
        int d = 0;
        for (var cur = p; cur.Parent >= 0 && d < 64; d++)
        {
            var up = Find(cur.Parent);
            if (up == null) break;
            cur = up;
        }
        return d;
    }

    /// <summary>Parts in tree order — parents first, children indented under them.</summary>
    public List<Part> TreeOrder()
    {
        var result = new List<Part>();
        foreach (var root in Parts.Where(p => p.Parent < 0 || Find(p.Parent) == null))
            result.AddRange(Subtree(root.Id));
        return result;
    }

    /// <summary>The pivot's position with no animation and no rotation applied — good
    /// enough to tell a front leg from a back one.</summary>
    public Vector3 RestPosition(Part p)
    {
        Vector3 at = p.Pivot;
        var cur = p;
        for (int guard = 0; cur.Parent >= 0 && guard < 64; guard++)
        {
            var up = Find(cur.Parent);
            if (up == null) break;
            at += up.Pivot;
            cur = up;
        }
        return at;
    }

    /// <summary>
    /// Copies a part and everything under it onto the other side of the body, reflected
    /// through the centre line, and links each copy to its original as twins. Returns the
    /// new root's id. A centre-line part mirrors onto itself's position — the copy is still
    /// made, so this doubles as "duplicate".
    /// </summary>
    public int MirrorSubtree(int id)
    {
        var src = Subtree(id);
        if (src.Count == 0) return -1;
        var map = new Dictionary<int, int>();
        int next = NextPartId();
        foreach (var p in src) map[p.Id] = next++;

        foreach (var p in src)
        {
            var c = p.Clone();
            c.Id = map[p.Id];
            Reflect(p, c);
            c.Side = -p.Side;
            c.Name = SwapSide(p.Name);
            if (map.TryGetValue(p.Parent, out int np)) c.Parent = np;
            else
            {
                // The root of the copy hangs from the parent's twin when the parent has one,
                // so a mirrored hand goes on the other arm rather than the same one.
                var parent = Find(p.Parent);
                c.Parent = parent != null && parent.Twin >= 0 ? parent.Twin : p.Parent;
            }
            if (p.Side != 0 || c.Pivot.X != p.Pivot.X)
            {
                c.Twin = p.Id;
                p.Twin = c.Id;
            }
            else c.Twin = -1;
            Parts.Add(c);
        }

        // Animations follow: every track on an original is copied onto its mirror, half a
        // cycle out for waves so the new leg steps opposite its twin.
        foreach (var clip in Clips)
        {
            var added = new List<Track>();
            foreach (var t in clip.Tracks.Where(t => map.ContainsKey(t.Part)))
            {
                var nt = t.Clone();
                nt.Part = map[t.Part];
                if (nt.Mode == TrackMode.Wave) nt.Phase = (nt.Phase + 0.5f) % 1f;
                added.Add(nt);
            }
            clip.Tracks.AddRange(added);
        }
        return map[id];
    }

    /// <summary>Makes <paramref name="p"/>'s twin match it, reflected. Shape only: the
    /// twin keeps its own id, parent, name and animations.</summary>
    public void SyncTwin(Part p)
    {
        var t = p.Twin >= 0 ? Find(p.Twin) : null;
        if (t == null) return;
        t.Role = p.Role;
        Reflect(p, t);
    }

    /// <summary>Writes <paramref name="src"/>'s geometry into <paramref name="dst"/>
    /// reflected through X = 0.</summary>
    private static void Reflect(Part src, Part dst)
    {
        dst.Pivot = src.Pivot with { X = -src.Pivot.X };
        dst.Rest = new Vector3(src.Rest.X, -src.Rest.Y, -src.Rest.Z);
        dst.Mount = src.Mount with { X = -src.Mount.X };
        dst.Scale = src.Scale;
        dst.Stretch = src.Stretch;
        dst.Stick = src.Stick;
        dst.Blocks = src.Blocks.Select(b =>
        {
            var c = b.Clone();
            c.Center = b.Center with { X = -b.Center.X };
            if (b.Tris != null)
            {
                // A copied game mesh may not be symmetric itself, so its triangles mirror too.
                var t = (float[])b.Tris.Clone();
                for (int i = 0; i < t.Length; i += 3) t[i] = -t[i];
                c.Tris = t;
            }
            return c;
        }).ToList();
    }

    /// <summary>A plain copy of a part and everything under it, hung from the same parent.
    /// Returns the new root's id. Twins inside the copy stay twinned to each other.</summary>
    public int DuplicateSubtree(int id)
    {
        var src = Subtree(id);
        if (src.Count == 0) return -1;
        var map = new Dictionary<int, int>();
        int next = NextPartId();
        foreach (var p in src) map[p.Id] = next++;
        foreach (var p in src)
        {
            var c = p.Clone();
            c.Id = map[p.Id];
            c.Parent = map.TryGetValue(p.Parent, out int np) ? np : p.Parent;
            c.Twin = map.TryGetValue(p.Twin, out int nt) ? nt : -1;
            c.Vertebra = -1;
            Parts.Add(c);
        }
        foreach (var clip in Clips)
            clip.Tracks.AddRange(clip.Tracks.Where(t => map.ContainsKey(t.Part)).Select(t =>
            {
                var nt = t.Clone();
                nt.Part = map[t.Part];
                return nt;
            }).ToList());
        return map[id];
    }

    /// <summary>Removes a part, everything under it, and their animation tracks.</summary>
    public void DeleteSubtree(int id)
    {
        var doomed = Subtree(id).Select(p => p.Id).ToHashSet();
        Parts.RemoveAll(p => doomed.Contains(p.Id));
        foreach (var p in Parts.Where(p => doomed.Contains(p.Twin))) p.Twin = -1;
        foreach (var c in Clips) c.Tracks.RemoveAll(t => doomed.Contains(t.Part));
    }

    /// <summary>Whether a part of role <paramref name="child"/> hung from one of role
    /// <paramref name="parent"/> is a further segment of the same limb.</summary>
    public static bool IsSegmentOf(PartRole child, PartRole parent) => (parent, child) switch
    {
        (PartRole.Leg, PartRole.Leg or PartRole.Foot) => true,
        (PartRole.Arm, PartRole.Arm or PartRole.Hand) => true,
        (PartRole.Tail or PartRole.Wing or PartRole.Stalk or PartRole.Neck, _) => child == parent,
        _ => false,
    };

    /// <summary>The top of the limb a part belongs to: walks up through
    /// <see cref="Part.Fixed"/> segments to the first part that is free to move.</summary>
    public Part LimbRoot(Part p)
    {
        var cur = p;
        for (int guard = 0; cur.Fixed && guard < 64 && Find(cur.Parent) is { } up; guard++) cur = up;
        return cur;
    }

    /// <summary>"ARM L" becomes "ARM R", and "WHEEL L2" becomes "WHEEL R2": the side letter
    /// is the last word, optionally numbered.</summary>
    public static string SwapSide(string name)
        => System.Text.RegularExpressions.Regex.Replace(name, @"(?<=^|\s)([LR])(\d*)$",
            m => (m.Groups[1].Value == "L" ? "R" : "L") + m.Groups[2].Value);

    /// <summary>
    /// Repairs what a hand-edited or older file might carry: dangling parents, cycles,
    /// duplicate ids, tracks aimed at parts that are gone. Always safe to call.
    /// </summary>
    public void Sanitize()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "UNTITLED" : Name.Trim();
        Parts ??= new();
        Clips ??= new();

        var seen = new HashSet<int>();
        foreach (var p in Parts)
        {
            p.Blocks ??= new();
            p.Name = string.IsNullOrWhiteSpace(p.Name) ? "PART" : p.Name;
            if (!seen.Add(p.Id)) { p.Id = NextPartId(); seen.Add(p.Id); }
        }
        foreach (var p in Parts)
        {
            if (p.Parent == p.Id || (p.Parent >= 0 && Find(p.Parent) == null)) p.Parent = -1;
            // Files from before limbs held together: a knee under a thigh, a hand under an
            // arm, is a segment whatever the file says.
            if (Find(p.Parent) is { } up && IsSegmentOf(p.Role, up.Role)) p.Fixed = true;
            if (p.Twin >= 0 && Find(p.Twin) == null) p.Twin = -1;
            if (p.Mount.LengthSquared() < 1e-6f || !float.IsFinite(p.Mount.X)) p.Mount = Vector3.UnitY;
            p.Mount = Vector3.Normalize(p.Mount);
            p.Scale = float.IsFinite(p.Scale) ? Math.Clamp(p.Scale, 0.05f, 20f) : 1f;
            p.Stretch = Vector3.Clamp(p.Stretch, new Vector3(0.05f), new Vector3(20f));
            foreach (var b in p.Blocks)
            {
                b.Size = Vector3.Max(b.Size, new Vector3(0.02f));
                b.Taper = Math.Clamp(b.Taper, 0f, 2f);
                if (!Enum.IsDefined(b.Pattern)) b.Pattern = PaintPattern.Solid;
                if (b.Shape == BlockShape.Mesh && (b.Tris == null || b.Tris.Length % 9 != 0
                    || b.Colors == null || b.Colors.Length != b.Tris.Length / 9))
                {
                    // A mesh that cannot be drawn becomes a plain box of the same size.
                    b.Shape = BlockShape.Box;
                    b.Tris = null;
                    b.Colors = null;
                }
            }
        }
        // Break any cycle by cutting the first link found inside it.
        foreach (var p in Parts)
        {
            var visited = new HashSet<int> { p.Id };
            for (var cur = p; cur.Parent >= 0;)
            {
                if (!visited.Add(cur.Parent)) { cur.Parent = -1; break; }
                var up = Find(cur.Parent);
                if (up == null) break;
                cur = up;
            }
        }
        foreach (var c in Clips)
        {
            c.Tracks ??= new();
            c.Tracks.RemoveAll(t => Find(t.Part) == null);
            c.Duration = Math.Clamp(c.Duration, 0.05f, 60f);
            foreach (var t in c.Tracks)
            {
                t.Keys ??= new();
                t.Cycles = Math.Clamp(t.Cycles, 1, 16);
            }
        }
    }
}

/// <summary>
/// Reading and writing designs. Saved creations live in <c>Creations/</c> beside the
/// executable. Built-in characters are templates in code and are never written: a design
/// started from one always saves as a new file, so the originals cannot be overwritten.
/// </summary>
public static class DesignStore
{
    /// <summary>Settable so the self-test can save into a scratch folder instead.</summary>
    public static string Folder { get; set; } = Path.Combine(AppContext.BaseDirectory, "Creations");

    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(Design d) => JsonSerializer.Serialize(d, Options);

    public static Design FromJson(string json)
    {
        var d = JsonSerializer.Deserialize<Design>(json, Options)
            ?? throw new InvalidDataException("empty design");
        d.Sanitize();
        return d;
    }

    /// <summary>Every saved creation, newest first. Files that will not parse are listed
    /// with their error rather than hidden, so a broken hand-edit is visible.</summary>
    public static List<(string Path, Design? Design, string? Error)> List()
    {
        var result = new List<(string, Design?, string?)>();
        if (!Directory.Exists(Folder)) return result;
        foreach (var path in Directory.GetFiles(Folder, "*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try { result.Add((path, FromJson(File.ReadAllText(path)), null)); }
            catch (Exception e) { result.Add((path, null, e.Message)); }
        }
        return result;
    }

    /// <summary>
    /// Writes <paramref name="d"/>. With <paramref name="overwrite"/> set it replaces that
    /// file; otherwise it picks a fresh name from the design's own and never clobbers
    /// anything. Returns the path written.
    /// </summary>
    public static string Save(Design d, string? overwrite)
    {
        Directory.CreateDirectory(Folder);
        string path = overwrite ?? FreshPath(d.Name);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson(d));
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    public static string FreshPath(string name)
    {
        string stem = Slug(name);
        string path = Path.Combine(Folder, stem + ".json");
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(Folder, $"{stem}-{n}.json");
        return path;
    }

    public static string Slug(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        string s = new string(chars).Trim('-');
        while (s.Contains("--")) s = s.Replace("--", "-");
        return s.Length == 0 ? "creation" : s;
    }
}
