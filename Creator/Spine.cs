using System.Numerics;

namespace Unrendered.Creator;

/// <summary>
/// The torso as Spore builds it: a run of vertebrae down the centre line that the mouse bends
/// and stretches. Here each vertebra is a chunky block rather than a blob — the game's hard
/// edges — but it behaves the same way: pull an end vertebra out and the spine grows new
/// ones, push it back in and they are absorbed; drag one in the middle and the spine bends
/// there; roll the wheel over one and that stretch of the body fattens or thins.
///
/// <para>It bends the way a back bends, only more so. Each joint turns at most
/// <see cref="MaxBend"/> from the one behind it and no stretch of the spine tips past
/// <see cref="MaxPitch"/> from level, so it can arch, sag, rear up and curl its tail, but it
/// can never fold back over itself or tie itself in a loop. Dragging a middle vertebra pulls
/// its neighbours along with it, less the further away they are, so a pull makes a curve
/// rather than a kink.</para>
///
/// <para>Vertebrae are ordinary parts (role <see cref="PartRole.Spine"/>), all hung from one
/// invisible root at the origin, so limbs attach to them, the body bob moves them all, and
/// nothing about posing or saving needs to know a spine exists.</para>
/// </summary>
public static class Spine
{
    /// <summary>How far apart vertebrae sit. Pulling an end out past
    /// <see cref="GrowAt"/> of this adds one; pushing it within <see cref="ShrinkAt"/> of
    /// its neighbour removes one.</summary>
    public const float Spacing = 0.42f;
    private const float GrowAt = 1.6f;
    private const float ShrinkAt = 0.45f;
    private const int MaxVertebrae = 24;

    /// <summary>The most one joint turns from the one behind it, radians (~38°). A real
    /// back manages a fraction of this per vertebra; these vertebrae are long and the
    /// creatures are cartoons, so it is pushed until a curl reads from across the room.</summary>
    public const float MaxBend = 0.66f;
    /// <summary>The steepest any stretch of spine may stand (~72° from level). Short of
    /// upright, so the spine always runs tail to head and can never turn back on itself.</summary>
    public const float MaxPitch = 1.25f;
    /// <summary>How far a middle drag carries to the neighbours, in vertebrae.</summary>
    private const float Reach = 1.15f;
    /// <summary>How much a middle drag may squash or stretch the gaps between vertebrae.</summary>
    private const float MinGap = 0.72f, MaxGap = 1.35f;
    /// <summary>The lowest a vertebra may sit — the body never sinks into the floor.</summary>
    public const float Floor = 0.15f;

    public static List<Part> Vertebrae(Design d)
        => d.Parts.Where(p => p.Role == PartRole.Spine).OrderBy(p => p.Vertebra).ToList();

    public static bool Has(Design d) => d.Parts.Any(p => p.Role == PartRole.Spine);

    /// <summary>A fresh spine: four vertebrae, level, at hip height, under an empty root.</summary>
    public static Design NewCreature()
    {
        var d = new Design { Name = "NEW CREATURE", Kind = DesignKind.Creature, BaseClass = Core.PlayerClass.Soldier };
        d.Parts.Add(new Part { Id = 0, Name = "BODY", Role = PartRole.Body });
        // Fatter in the middle, the way Spore's starting blob is.
        float[] girth = { 0.62f, 0.8f, 0.86f, 0.66f };
        for (int i = 0; i < girth.Length; i++)
            d.Parts.Add(new Part
            {
                Id = i + 1, Name = $"SPINE {i + 1}", Role = PartRole.Spine, Parent = 0, Vertebra = i,
                Pivot = new Vector3(0, 1.1f + (i == girth.Length - 1 ? 0.08f : 0f), (i - 1.5f) * Spacing),
                Blocks = { new Block { Size = new Vector3(girth[i], girth[i] * 0.85f, Spacing), Taper = 0.85f, Swatch = 1 } },
            });
        Layout(d);
        return d;
    }

    /// <summary>
    /// Re-aims every vertebra along the curve through its neighbours and sizes its block to
    /// reach halfway to each, so the torso stays one closed run of blocks however it is bent.
    /// Called after every spine edit.
    /// </summary>
    public static void Layout(Design d)
    {
        var v = Vertebrae(d);
        for (int i = 0; i < v.Count; i++)
        {
            v[i].Vertebra = i;
            v[i].Pivot = v[i].Pivot with { X = 0f };
            v[i].Name = $"SPINE {i + 1}";
            v[i].Side = 0;
            v[i].Twin = -1;
        }
        if (v.Count == 0) return;
        for (int i = 0; i < v.Count; i++)
        {
            Vector3 back = v[Math.Max(0, i - 1)].Pivot, front = v[Math.Min(v.Count - 1, i + 1)].Pivot;
            Vector3 dir = front - back;
            if (dir.LengthSquared() < 1e-6f) dir = Vector3.UnitZ;
            dir = Vector3.Normalize(dir);
            // Pitch so the block's length runs along the spine (+Z forward; a positive pitch
            // noses down), turned right round if an old file has a spine bent back on itself.
            v[i].Rest = new Vector3(-MathF.Asin(Math.Clamp(dir.Y, -1f, 1f)), dir.Z < 0 ? MathF.PI : 0f, 0f);

            float toBack = i > 0 ? Vector3.Distance(v[i].Pivot, v[i - 1].Pivot) : Spacing;
            float toFront = i < v.Count - 1 ? Vector3.Distance(v[i].Pivot, v[i + 1].Pivot) : Spacing;
            // A bent joint opens a wedge on the outside of the curve; the block grows a little
            // with the bend so the body stays closed round it.
            float bend = i > 0 && i < v.Count - 1 ? Angle(v[i].Pivot - v[i - 1].Pivot, v[i + 1].Pivot - v[i].Pivot) : 0f;
            foreach (var b in v[i].Blocks)
            {
                // A hair short of touching on the straight, so each vertebra reads as its own
                // block — the segments are what the mouse grabs — while the body still reads
                // as one.
                b.Size = b.Size with { Z = (toBack + toFront) * 0.5f * (0.92f + 0.55f * MathF.Sin(bend * 0.5f)) };
                b.Center = new Vector3(0, 0, (toFront - toBack) * 0.25f);
            }
            v[i].Mount = Vector3.UnitY;
            v[i].Stretch = Vector3.One;
        }
    }

    /// <summary>
    /// Drags a vertebra toward <paramref name="target"/> (in the spine root's frame),
    /// returning the vertebra now being held — which changes when pulling an end grows a new
    /// one. It goes as near the cursor as the back allows.
    /// </summary>
    public static Part Drag(Design d, Part held, Vector3 target)
    {
        target.X = 0f;
        target.Y = MathF.Max(Floor, target.Y);
        var v = Vertebrae(d);
        int i = v.IndexOf(held);
        if (i < 0) return held;
        bool isEnd = i == 0 || i == v.Count - 1;

        if (isEnd && v.Count >= 2)
        {
            // An end reaches out from its neighbour: any length (that is how it grows), but
            // only in a direction the back can bend to.
            bool head = i == v.Count - 1;
            int n = head ? i - 1 : 1;
            var neighbour = v[n];
            Vector3? beyond = head
                ? n > 0 ? v[n].Pivot - v[n - 1].Pivot : null
                : n < v.Count - 1 ? v[n + 1].Pivot - v[n].Pivot : null;
            Vector3 seg = head ? target - neighbour.Pivot : neighbour.Pivot - target;
            seg = Bend(seg, beyond, 0.02f, Spacing * 3f);
            held.Pivot = head ? neighbour.Pivot + seg : neighbour.Pivot - seg;
            float gap = Vector3.Distance(held.Pivot, neighbour.Pivot);

            // Pulled out: drop a new vertebra halfway, so the spine grows from the end that is
            // being pulled and the cursor keeps holding the tip.
            if (gap > Spacing * GrowAt && v.Count < MaxVertebrae)
            {
                var fresh = held.Clone();
                fresh.Id = d.NextPartId();
                fresh.Pivot = (held.Pivot + neighbour.Pivot) * 0.5f;
                fresh.Blocks = held.Blocks.Select(b => b.Clone()).ToList();
                d.Parts.Add(fresh);
                fresh.Vertebra = i == 0 ? 0 : held.Vertebra;
                held.Vertebra = i == 0 ? -1 : held.Vertebra + 1;
                Renumber(d, held, fresh, i == 0);
            }
            // Pushed in: the neighbour is absorbed, and whatever hung off it moves to the
            // vertebra in hand rather than being lost.
            else if (gap < Spacing * ShrinkAt && v.Count > 2)
            {
                foreach (var child in d.Parts.Where(p => p.Parent == neighbour.Id))
                {
                    child.Pivot += neighbour.Pivot - held.Pivot;
                    child.Parent = held.Id;
                }
                d.DeleteSubtree(neighbour.Id);
            }
        }
        else
        {
            // The middle: the held vertebra goes to the cursor and its neighbours follow on a
            // falloff, then the whole run is walked outward from it and every joint brought
            // back inside what a back can do.
            Vector3 delta = target - held.Pivot;
            var start = v.Select(p => p.Pivot).ToArray();
            for (int j = 0; j < v.Count; j++)
            {
                float k = j - i;
                float w = MathF.Exp(-k * k / (2f * Reach * Reach));
                v[j].Pivot = start[j] + delta * w;
            }
            Solve(v, i);
        }
        Lift(d);
        Layout(d);
        return held;
    }

    /// <summary>Walks the spine out from vertebra <paramref name="anchor"/> in both
    /// directions, holding each joint to the bend and pitch limits and each gap to a sane
    /// length. The anchor stays where it is; everything else gives.</summary>
    private static void Solve(List<Part> v, int anchor)
    {
        float lo = Spacing * MinGap, hi = Spacing * MaxGap;
        // Toward the head: each stretch measured against the one behind it.
        for (int j = anchor + 1; j < v.Count; j++)
        {
            Vector3? behind = j >= 2 ? v[j - 1].Pivot - v[j - 2].Pivot : null;
            var seg = Bend(v[j].Pivot - v[j - 1].Pivot, behind, lo, hi);
            v[j].Pivot = v[j - 1].Pivot + seg;
        }
        // Toward the tail: each stretch measured against the one ahead of it, which is
        // already settled — so the joint at the anchor itself is limited here too.
        for (int j = anchor - 1; j >= 0; j--)
        {
            Vector3? ahead = j + 2 < v.Count ? v[j + 2].Pivot - v[j + 1].Pivot : null;
            var seg = Bend(v[j + 1].Pivot - v[j].Pivot, ahead, lo, hi);
            v[j].Pivot = v[j + 1].Pivot - seg;
        }
    }

    /// <summary>A spine pushed into the floor is lifted out of it whole. Pushing single
    /// vertebrae back up would put a kink in the curve the limits have just smoothed; raising
    /// the lot keeps the shape, the way a creature rears rather than folds when it meets the
    /// ground.</summary>
    private static void Lift(Design d)
    {
        var v = Vertebrae(d);
        if (v.Count == 0) return;
        float low = v.Min(p => p.Pivot.Y);
        if (low >= Floor) return;
        foreach (var p in v) p.Pivot = p.Pivot with { Y = p.Pivot.Y + Floor - low };
    }

    /// <summary>
    /// One stretch of spine, tail-to-head, brought within the limits: no steeper than
    /// <see cref="MaxPitch"/>, turned no more than <see cref="MaxBend"/> from
    /// <paramref name="reference"/> (the stretch next to it, if there is one), and between
    /// <paramref name="minLen"/> and <paramref name="maxLen"/> long. Works in the centre
    /// plane — the spine never leaves it.
    /// </summary>
    public static Vector3 Bend(Vector3 seg, Vector3? reference, float minLen, float maxLen)
    {
        float len = Math.Clamp(new Vector2(seg.Y, seg.Z).Length(), minLen, maxLen);
        // A stretch pointing backward is read as the nearest forward one, not flipped round.
        float a = MathF.Atan2(seg.Y, seg.Z);
        a = Math.Clamp(a, -MaxPitch, MaxPitch);
        if (reference is { } r && new Vector2(r.Y, r.Z).LengthSquared() > 1e-8f)
        {
            float ar = Math.Clamp(MathF.Atan2(r.Y, r.Z), -MaxPitch, MaxPitch);
            a = Math.Clamp(a, ar - MaxBend, ar + MaxBend);
            a = Math.Clamp(a, -MaxPitch, MaxPitch);
        }
        return new Vector3(0f, MathF.Sin(a) * len, MathF.Cos(a) * len);
    }

    /// <summary>The turn between two stretches, radians.</summary>
    public static float Angle(Vector3 a, Vector3 b)
    {
        if (a.LengthSquared() < 1e-10f || b.LengthSquared() < 1e-10f) return 0f;
        return MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f));
    }

    /// <summary>Puts the vertebra indices back in order after a new one went in next to the
    /// held end.</summary>
    private static void Renumber(Design d, Part held, Part fresh, bool atTail)
    {
        var rest = Vertebrae(d).Where(p => p != held && p != fresh).OrderBy(p => p.Vertebra).ToList();
        var order = new List<Part>();
        if (atTail) { order.Add(held); order.Add(fresh); order.AddRange(rest); }
        else { order.AddRange(rest); order.Add(fresh); order.Add(held); }
        for (int k = 0; k < order.Count; k++) order[k].Vertebra = k;
    }

    /// <summary>A spine this long or longer spreads a fattening two vertebrae each way;
    /// a shorter one spreads it one.</summary>
    public const int LongSpine = 7;

    /// <summary>
    /// The wheel over a vertebra: that stretch of body fattens or thins. With
    /// <paramref name="spread"/> the change eases out into the neighbours — half as much one
    /// vertebra away, a quarter two away on a long spine — so the body swells like flesh
    /// rather than growing a single ring. Without it, only the one vertebra changes.
    /// </summary>
    public static void Girth(Design d, Part vertebra, float factor, bool spread = false)
    {
        var v = Vertebrae(d);
        int at = v.IndexOf(vertebra);
        int reach = !spread || at < 0 ? 0 : v.Count >= LongSpine ? 2 : 1;
        for (int k = -reach; k <= reach; k++)
        {
            int j = at + k;
            if (at >= 0 ? j < 0 || j >= v.Count : k != 0) continue;
            var target = at >= 0 ? v[j] : vertebra;
            // The share in the exponent, so a step and its undo cancel exactly.
            float f = MathF.Pow(factor, MathF.Pow(0.5f, Math.Abs(k)));
            foreach (var b in target.Blocks)
            {
                float x = Math.Clamp(b.Size.X * f, 0.12f, 4f);
                float y = Math.Clamp(b.Size.Y * f, 0.12f, 4f);
                b.Size = b.Size with { X = x, Y = y };
            }
        }
        Layout(d);
    }
}
