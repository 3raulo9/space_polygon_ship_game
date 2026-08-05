namespace Unrendered.Core;

/// <summary>
/// The workshop's two tables, as data: what three parts <em>assemble</em> into, and what one
/// finished thing <em>comes apart</em> into. Both live here rather than on
/// <see cref="Inventory"/> because both ends of the wire have to run exactly the same tables —
/// a client's optimistic craft and the host's replay of it must agree about what came out —
/// and because "what a battery is made of" is world knowledge, not a property of one pack.
///
/// <para>Adding to either table is the whole cost of adding a recipe: nothing in the panel,
/// the wire or the world is written per-item. The panel counts the arrows it draws off
/// <see cref="PartCount"/>, and the assembly bench matches whatever the three corners hold
/// against every recipe here.</para>
/// </summary>
public static class Crafting
{
    /// <summary>
    /// One assembly recipe: three corner ingredients in, one stack out. Each corner is a
    /// <em>set</em> of accepted kinds, so a recipe can say "and any one of these three metals"
    /// without needing three near-identical entries — which is exactly what a battery needs,
    /// because a battery taken apart gives back only one of them.
    /// </summary>
    public sealed class Recipe
    {
        public readonly string Name;
        public readonly ItemKind[][] Inputs;
        public readonly ItemKind Output;
        public readonly int Count;

        public Recipe(string name, ItemKind[][] inputs, ItemKind output, int count)
        {
            Name = name;
            Inputs = inputs;
            Output = output;
            Count = count;
        }
    }

    private static ItemKind[] One(ItemKind k) => new[] { k };

    /// <summary>
    /// Every recipe the bench knows. Order matters only in that the first match wins, and no
    /// two of these can be satisfied by the same three parts.
    /// </summary>
    public static readonly Recipe[] Recipes =
    {
        // The original: three shards of a slain Crab-Core become one throwable core.
        new("CRAB CORE",
            new[] { One(ItemKind.CrabFragment), One(ItemKind.CrabFragment), One(ItemKind.CrabFragment) },
            ItemKind.CrabCore, 1),

        // Rounds. Powder for the push, dense alloy for the slug, scrap drawn into a casing —
        // which is precisely what a round gives back when it is pulled apart, so the two
        // tables read as one operation run in either direction.
        new("ROUNDS",
            new[] { One(ItemKind.SpaceGunpowder), One(ItemKind.DenseAlloy), One(ItemKind.ScrapMetal) },
            ItemKind.Bullet, 5),

        // A cell. Wire, a housing, and whatever metal you happened to pull out of the last
        // one you broke — lead, zinc or lithium all work, and the cell does not care which.
        new("CELL",
            new[]
            {
                One(ItemKind.CopperWire),
                One(ItemKind.ScrapMetal),
                new[] { ItemKind.Lead, ItemKind.Zinc, ItemKind.Lithium },
            },
            ItemKind.Battery, 1),

        // A repair kit: plate to weld over the hole, alloy to back it, and wire to tie it
        // into whatever it interrupted. No powder and no anode metal, which is what keeps it
        // clear of the other two — the bench never has to guess which of them you meant.
        new("REPAIR KIT",
            new[] { One(ItemKind.ScrapMetal), One(ItemKind.DenseAlloy), One(ItemKind.CopperWire) },
            ItemKind.RepairKit, 1),
    };

    /// <summary>The six orders three corners can be read in. The bench does not care which
    /// corner a part was dropped into, so a recipe is satisfied if <em>any</em> pairing of
    /// corners to ingredients works.</summary>
    private static readonly int[][] Orders =
    {
        new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
        new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 },
    };

    /// <summary>The recipe these three corners satisfy, or null. Reading it consumes
    /// nothing — the panel calls it every frame to decide whether the triangle is lit.</summary>
    public static Recipe? Match(ItemStack[] corners)
    {
        if (corners.Length != 3) return null;
        foreach (var r in Recipes)
            if (Fits(r, corners)) return r;
        return null;
    }

    private static bool Fits(Recipe r, ItemStack[] corners)
    {
        foreach (int[] order in Orders)
        {
            bool ok = true;
            for (int i = 0; i < 3 && ok; i++)
            {
                ItemStack c = corners[order[i]];
                ok = !c.IsEmpty && Array.IndexOf(r.Inputs[i], c.Kind) >= 0;
            }
            if (ok) return true;
        }
        return false;
    }

    /// <summary>Whether a kind is named by any recipe — which is exactly the set the crafting
    /// corners will hold. A battery is not one: batteries are made, not spent, at the bench.</summary>
    public static bool IsIngredient(ItemKind kind)
    {
        foreach (var r in Recipes)
            foreach (var slot in r.Inputs)
                if (Array.IndexOf(slot, kind) >= 0) return true;
        return false;
    }

    // --- Taking apart ----------------------------------------------------------------

    /// <summary>
    /// What one finished item comes apart into: the parts that always fall out, and — for the
    /// things that were never made to be opened — one part out of a set that you get about
    /// half the time. The uncertain one is drawn as a "?" under its own arrow, so a player can
    /// see there is a gamble there before spending anything.
    /// </summary>
    public sealed class Teardown
    {
        public readonly ItemKind[] Certain;
        public readonly ItemKind[] OneOf;

        public Teardown(ItemKind[] certain, ItemKind[] oneOf)
        {
            Certain = certain;
            OneOf = oneOf;
        }
    }

    /// <summary>How often the uncertain part actually comes off. A coin toss: half the
    /// batteries you open give up their metal, half are scrap and wire and nothing else.</summary>
    public const float MaybeChance = 0.5f;

    private static readonly Dictionary<ItemKind, Teardown> Table = new()
    {
        // A cell: the winding and the housing every time, and the anode metal — whichever it
        // happened to be built around — on a coin toss.
        [ItemKind.Battery] = new Teardown(
            new[] { ItemKind.CopperWire, ItemKind.ScrapMetal },
            new[] { ItemKind.Lead, ItemKind.Zinc, ItemKind.Lithium }),

        // A round: powder, slug, casing. Nothing in a bullet is a surprise.
        [ItemKind.Bullet] = new Teardown(
            new[] { ItemKind.SpaceGunpowder, ItemKind.DenseAlloy, ItemKind.ScrapMetal },
            Array.Empty<ItemKind>()),

        // A kit: the plate and the alloy back out every time, the wire about half — some of
        // it went into the hull and is not coming out again.
        [ItemKind.RepairKit] = new Teardown(
            new[] { ItemKind.ScrapMetal, ItemKind.DenseAlloy },
            new[] { ItemKind.CopperWire }),

        // And the crafted core comes back apart into the three shards it was pressed from,
        // so a player who built one and then wanted the fragments back is not simply stuck.
        [ItemKind.CrabCore] = new Teardown(
            new[] { ItemKind.CrabFragment, ItemKind.CrabFragment, ItemKind.CrabFragment },
            Array.Empty<ItemKind>()),
    };

    /// <summary>Whether the bench can open this kind at all. Raw salvage cannot be broken
    /// down further — a scrap of metal is already the bottom of the pile.</summary>
    public static bool CanBreak(ItemKind kind) => Table.ContainsKey(kind);

    /// <summary>How this kind comes apart, or null if it doesn't.</summary>
    public static Teardown? Of(ItemKind kind) => Table.GetValueOrDefault(kind);

    /// <summary>
    /// How many arrows the take-apart bench draws for a kind: one per certain part, plus one
    /// more for the uncertain one. Zero for anything unbreakable, which is what leaves the
    /// bench showing no arrows at all until something is put on it.
    /// </summary>
    public static int PartCount(ItemKind kind)
    {
        Teardown? t = Of(kind);
        if (t is null) return 0;
        return t.Certain.Length + (t.OneOf.Length > 0 ? 1 : 0);
    }

    /// <summary>What the arrow at <paramref name="index"/> shows before anything is spent:
    /// the part it will yield, or an empty stack for the uncertain arrow (which the panel
    /// draws as a "?" — the whole point being that it cannot be promised).</summary>
    public static ItemStack Preview(ItemKind kind, int index)
    {
        Teardown? t = Of(kind);
        if (t is null || index < 0 || index >= PartCount(kind)) return ItemStack.Empty;
        return index < t.Certain.Length ? new ItemStack(t.Certain[index], 1) : ItemStack.Empty;
    }

    /// <summary>True if this arrow is the gamble rather than a promise.</summary>
    public static bool IsUncertain(ItemKind kind, int index)
    {
        Teardown? t = Of(kind);
        return t is not null && t.OneOf.Length > 0 && index == t.Certain.Length;
    }

    /// <summary>
    /// Rolls one teardown, filling <paramref name="parts"/> arrow by arrow so the results land
    /// under the arrows that promised them. An arrow that yielded nothing this time (the coin
    /// toss lost) is left empty.
    ///
    /// <para>This is the one genuinely random thing in the pack, so it is the one thing a
    /// client cannot know it got right: the client rolls its own to answer the click instantly
    /// and the host's echo of the real pack is what it ends up with. Which is the same bargain
    /// every other inventory action already makes — see <see cref="InvIntent"/>.</para>
    /// </summary>
    public static void Roll(ItemKind kind, Span<ItemStack> parts, Random? rng = null)
    {
        rng ??= Random.Shared;
        for (int i = 0; i < parts.Length; i++) parts[i] = ItemStack.Empty;

        Teardown? t = Of(kind);
        if (t is null) return;

        int n = Math.Min(t.Certain.Length, parts.Length);
        for (int i = 0; i < n; i++) parts[i] = new ItemStack(t.Certain[i], 1);

        if (t.OneOf.Length == 0 || n >= parts.Length) return;
        if (rng.NextSingle() >= MaybeChance) return;
        parts[n] = new ItemStack(t.OneOf[rng.Next(t.OneOf.Length)], 1);
    }
}
