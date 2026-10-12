using System.Numerics;

namespace Unrendered.Creator;

/// <summary>
/// The torso as Spore builds it: a run of vertebrae down the centre line that the mouse bends
/// and stretches. Here each vertebra is a chunky block rather than a blob — the game's hard
/// edges — but it behaves the same way: pull an end vertebra out and the spine grows new
/// ones, push it back in and they are absorbed; drag one in the middle and the spine bends
/// there; roll the wheel over one and that stretch of the body fattens or thins.
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

    public static List<Part> Vertebrae(Design d)
        => d.Parts.Where(p => p.Role == PartRole.Spine).OrderBy(p => p.Vertebra).ToList();

    public static bool Has(Design d) => d.Parts.Any(p => p.Role == PartRole.Spine);

    /// <summary>A fresh spine: three vertebrae, level, at hip height, under an empty root.</summary>
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
            // noses down), turned right round if the spine has been bent back on itself.
            v[i].Rest = new Vector3(-MathF.Asin(Math.Clamp(dir.Y, -1f, 1f)), dir.Z < 0 ? MathF.PI : 0f, 0f);

            float toBack = i > 0 ? Vector3.Distance(v[i].Pivot, v[i - 1].Pivot) : Spacing;
            float toFront = i < v.Count - 1 ? Vector3.Distance(v[i].Pivot, v[i + 1].Pivot) : Spacing;
            foreach (var b in v[i].Blocks)
            {
                // A hair short of touching, so each vertebra reads as its own block — the
                // segments are what the mouse grabs — while the body still reads as one.
                b.Size = b.Size with { Z = (toBack + toFront) * 0.5f * 0.92f };
                b.Center = new Vector3(0, 0, (toFront - toBack) * 0.25f);
            }
            v[i].Mount = Vector3.UnitY;
            v[i].Stretch = Vector3.One;
        }
    }

    /// <summary>
    /// Drags a vertebra to <paramref name="target"/> (in the spine root's frame), returning
    /// the vertebra now being held — which changes when pulling an end grows a new one.
    /// The neighbours do not follow; the spine bends at the one being held.
    /// </summary>
    public static Part Drag(Design d, Part held, Vector3 target)
    {
        target.X = 0f;
        held.Pivot = target;
        var v = Vertebrae(d);
        int i = v.IndexOf(held);
        bool isEnd = i == 0 || i == v.Count - 1;

        if (isEnd && v.Count >= 2)
        {
            var neighbour = v[i == 0 ? 1 : v.Count - 2];
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
        Layout(d);
        return held;
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

    /// <summary>The wheel over a vertebra: that stretch of body fattens or thins.</summary>
    public static void Girth(Design d, Part vertebra, float factor)
    {
        foreach (var b in vertebra.Blocks)
        {
            float x = Math.Clamp(b.Size.X * factor, 0.12f, 4f);
            float y = Math.Clamp(b.Size.Y * factor, 0.12f, 4f);
            b.Size = b.Size with { X = x, Y = y };
        }
        Layout(d);
    }
}
