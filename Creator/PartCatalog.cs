using System.Numerics;
using Unrendered.Core;
using Unrendered.Rendering;

namespace Unrendered.Creator;

/// <summary>
/// The shapes bar: everything that can be dragged off the palette onto a body, the way the
/// Spore creator's part palette works. Each item is a small ready-jointed assembly — a lone
/// block, or a whole leg with its knee and foot already hinged and given the roles the walk
/// cycle reads — so a creature that has had legs dropped on it walks.
///
/// <para>Everything below an assembly's root is a <see cref="Part.Fixed"/> segment: it bends,
/// sizes and paints, but the leg comes off whole or not at all.</para>
///
/// <para>The conventions every item is built to, so it lands the right way up: limbs that
/// hang (legs, arms) run down −Y from their joint; things that reach sideways (wings,
/// spider legs) run out along +X and are mirrored for the left; heads, mouths and snouts
/// point forward along +Z and stay upright wherever they are dropped; tails run back along
/// −Z; and anything that grows out of a surface (eyes, horns, plates) stands along +Y and is
/// turned to face out of whatever it is dropped on.</para>
/// </summary>
public static class PartCatalog
{
    public sealed record Item(
        string Label,
        string Category,
        Func<List<Part>> Build,
        /// <summary>Stands out of whatever surface it is dropped on (true), or keeps hanging
        /// straight down whatever the surface faces (false) — a spike against a leg.</summary>
        bool Stick = true,
        /// <summary>Built reaching out to the right; mirrored when placed on the left.</summary>
        bool Sideways = false,
        /// <summary>A leg: scaled on placement so its foot meets the ground.</summary>
        bool ReachGround = false);

    public static readonly string[] Categories =
        { "SHAPES", "LEGS", "ARMS", "HEADS", "MOUTHS", "EYES", "TAILS", "WINGS", "DETAILS", "PLANT", "MACHINE", "GAME" };

    private static List<Item>? _all;
    public static IReadOnlyList<Item> All => _all ??= Build();

    public static List<Item> In(string category) => All.Where(i => i.Category == category).ToList();

    /// <summary>
    /// Puts a copy of <paramref name="item"/> into <paramref name="d"/> and returns its root.
    /// The root is not attached to anything yet; the caller places it. Every part under the
    /// root is a fixed segment of it.
    /// </summary>
    public static Part Instantiate(Design d, Item item)
    {
        var parts = item.Build();
        var root = parts[0];
        var map = new Dictionary<int, int>();
        int next = d.NextPartId();
        foreach (var p in parts) map[p.Id] = next++;
        foreach (var p in parts)
        {
            p.Id = map[p.Id];
            p.Parent = map.TryGetValue(p.Parent, out int np) ? np : -1;
            p.Twin = -1;
            p.Fixed = p != root;
            d.Parts.Add(p);
        }
        root.Stick = item.Stick;
        return root;
    }

    // =========================================================================
    //  Building blocks
    // =========================================================================

    // Swatches the defaults are painted in (indices into Paints).
    private const int Chrome = 0, Slate = 1, Rust = 2, Bruise = 3, Jaundice = 4, Teal = 5, Deep = 6, Magenta = 7,
        Red = 8, Bone = 9, Gunmetal = 10, Rot = 11, Soot = 12, Cloth = 13, Dusk = 14, Steel = 19, Chalk = 23,
        Webbing = 24, Copper = 25, Brass = 26, Ember = 28, Mark = 30, Ice = 34, Bile = 35, Glow = 36, Moss = 37, Pine = 39;

    private static Block Box(float x, float y, float z, float sx, float sy, float sz, int swatch, float taper = 1f, BlockShape shape = BlockShape.Box)
        => new() { Center = new(x, y, z), Size = new(sx, sy, sz), Swatch = swatch, Taper = taper, Shape = shape };

    private static Block Gem(float x, float y, float z, float sx, float sy, float sz, int swatch)
        => Box(x, y, z, sx, sy, sz, swatch, shape: BlockShape.Octa);

    private static Block Ramp(float x, float y, float z, float sx, float sy, float sz, int swatch)
        => Box(x, y, z, sx, sy, sz, swatch, shape: BlockShape.Wedge);

    private static Part P(int id, string name, PartRole role, int parent, Vector3 pivot, params Block[] blocks)
        => new() { Id = id, Name = name, Role = role, Parent = parent, Pivot = pivot, Blocks = blocks.ToList() };

    /// <summary>A part turned at its joint: <paramref name="rest"/> is pitch, yaw, roll.</summary>
    private static Part R(int id, string name, PartRole role, int parent, Vector3 pivot, Vector3 rest, params Block[] blocks)
        => new() { Id = id, Name = name, Role = role, Parent = parent, Pivot = pivot, Rest = rest, Blocks = blocks.ToList() };

    private static Item Single(string label, string cat, PartRole role, Block b, bool stick = true)
        => new(label, cat, () => new List<Part> { P(0, label, role, -1, Vector3.Zero, b.Clone()) }, stick);

    private static Item Many(string label, string cat, PartRole role, params Block[] blocks)
        => new(label, cat, () => new List<Part> { P(0, label, role, -1, Vector3.Zero, blocks.Select(b => b.Clone()).ToArray()) });

    private static Item FromMesh(string label, PolyMesh mesh)
        => new(label, "GAME", () => new List<Part> { P(0, label, PartRole.Decor, -1, Vector3.Zero, Block.FromTriangles(mesh.Triangles)) });

    /// <summary>A chain of tapering segments, each hung off the end of the one before and
    /// turned a little further — tentacles, whips, vines. <paramref name="along"/> is the
    /// direction a segment runs in its own frame.</summary>
    private static List<Part> Chain(string name, PartRole role, int count, Vector3 along, float len, float thick, float thin,
        Vector3 curl, int swatch, PartRole tipRole = (PartRole)(-1), Block? tipExtra = null)
    {
        var parts = new List<Part>();
        for (int i = 0; i < count; i++)
        {
            float k = count == 1 ? 0f : (float)i / (count - 1);
            float w = thick + (thin - thick) * k;
            float l = len * (1f - 0.12f * k);
            float prevL = i == 0 ? 0f : len * (1f - 0.12f * ((float)(i - 1) / (count - 1)));
            var size = new Vector3(along.X != 0 ? l : w, along.Y != 0 ? l : w, along.Z != 0 ? l : w);
            var block = Box(along.X * l * 0.5f, along.Y * l * 0.5f, along.Z * l * 0.5f, size.X, size.Y, size.Z, swatch);
            bool tip = i == count - 1;
            var p = R(i, i == 0 ? name : tip ? name + " TIP" : $"{name} {i + 1}",
                tip && (int)tipRole >= 0 ? tipRole : role, i - 1, i == 0 ? Vector3.Zero : along * prevL, i == 0 ? Vector3.Zero : curl, block);
            if (tip && tipExtra != null) p.Blocks.Add(tipExtra);
            parts.Add(p);
        }
        return parts;
    }

    // =========================================================================

    private static List<Item> Build()
    {
        var items = new List<Item>();
        items.AddRange(Shapes());
        items.AddRange(Legs());
        items.AddRange(Arms());
        items.AddRange(Heads());
        items.AddRange(Mouths());
        items.AddRange(Eyes());
        items.AddRange(Tails());
        items.AddRange(Wings());
        items.AddRange(Details());
        items.AddRange(Plant());
        items.AddRange(Machine());
        items.AddRange(Game());
        return items;
    }

    // ---- SHAPES: they sit on the surface, base down ---------------------------------
    private static IEnumerable<Item> Shapes() => new[]
    {
        Single("CUBE", "SHAPES", PartRole.Decor, Box(0, 0.2f, 0, 0.4f, 0.4f, 0.4f, Slate)),
        Single("SLAB", "SHAPES", PartRole.Detail, Box(0, 0.06f, 0, 0.7f, 0.12f, 0.7f, Gunmetal)),
        Single("BEAM", "SHAPES", PartRole.Detail, Box(0, 0.07f, 0, 0.14f, 0.14f, 0.9f, Gunmetal)),
        Single("PILLAR", "SHAPES", PartRole.Detail, Box(0, 0.4f, 0, 0.16f, 0.8f, 0.16f, Slate)),
        Single("WEDGE", "SHAPES", PartRole.Detail, Ramp(0, 0.15f, 0, 0.4f, 0.3f, 0.5f, Slate)),
        Single("STEP", "SHAPES", PartRole.Detail, Box(0, 0.2f, 0, 0.5f, 0.4f, 0.5f, Slate, taper: 0.55f)),
        Single("GEM", "SHAPES", PartRole.Core, Gem(0, 0.2f, 0, 0.4f, 0.4f, 0.4f, Red)),
        Single("PLATE", "SHAPES", PartRole.Detail, Box(0, 0.03f, 0, 0.5f, 0.06f, 0.3f, Chrome)),
        Single("PYRAMID", "SHAPES", PartRole.Detail, Box(0, 0.2f, 0, 0.5f, 0.4f, 0.5f, Slate, taper: 0f)),
        Many("DOME", "SHAPES", PartRole.Detail,
            Box(0, 0.08f, 0, 0.7f, 0.16f, 0.7f, Slate, 0.85f),
            Box(0, 0.22f, 0, 0.56f, 0.14f, 0.56f, Slate, 0.75f),
            Box(0, 0.33f, 0, 0.38f, 0.1f, 0.38f, Slate, 0.6f)),
        Many("HUMP", "SHAPES", PartRole.Detail,
            Box(0, 0.12f, 0, 0.5f, 0.24f, 0.8f, Slate, 0.8f),
            Box(0, 0.3f, -0.05f, 0.34f, 0.16f, 0.5f, Slate, 0.6f)),
        Single("ORB", "SHAPES", PartRole.Detail, Gem(0, 0.25f, 0, 0.5f, 0.5f, 0.5f, Teal)),
    };

    // ---- LEGS: hang down from the hip; the walk drives every one of them -----------------
    private static IEnumerable<Item> Legs() => new[]
    {
        new Item("LEG", "LEGS", () => new List<Part>
        {
            P(0, "THIGH", PartRole.Leg, -1, Vector3.Zero, Box(0, -0.22f, 0, 0.22f, 0.44f, 0.22f, Deep)),
            P(1, "SHIN", PartRole.Leg, 0, new(0, -0.44f, 0), Box(0, -0.2f, 0, 0.2f, 0.4f, 0.2f, Deep)),
            P(2, "FOOT", PartRole.Foot, 1, new(0, -0.4f, 0), Box(0, -0.05f, 0.06f, 0.24f, 0.1f, 0.34f, Gunmetal)),
        }, Stick: false, ReachGround: true),
        // Digitigrade: the knee forward, the ankle high and back, standing on its toes.
        new Item("BIRD LEG", "LEGS", () => new List<Part>
        {
            R(0, "THIGH", PartRole.Leg, -1, Vector3.Zero, new(-0.55f, 0, 0), Box(0, -0.2f, 0, 0.26f, 0.42f, 0.26f, Deep, 0.8f)),
            R(1, "SHIN", PartRole.Leg, 0, new(0, -0.42f, 0), new(1.15f, 0, 0), Box(0, -0.24f, 0, 0.14f, 0.48f, 0.14f, Deep)),
            R(2, "ANKLE", PartRole.Leg, 1, new(0, -0.48f, 0), new(-0.6f, 0, 0), Box(0, -0.18f, 0, 0.1f, 0.36f, 0.1f, Bone)),
            P(3, "TOES", PartRole.Foot, 2, new(0, -0.36f, 0),
                Box(0, -0.03f, 0.12f, 0.06f, 0.06f, 0.28f, Bone, 0.5f),
                Box(-0.09f, -0.03f, 0.08f, 0.05f, 0.05f, 0.2f, Bone), Box(0.09f, -0.03f, 0.08f, 0.05f, 0.05f, 0.2f, Bone),
                Box(0, -0.03f, -0.07f, 0.05f, 0.05f, 0.12f, Bone)),
        }, Stick: false, ReachGround: true),
        new Item("HOOF LEG", "LEGS", () => new List<Part>
        {
            P(0, "THIGH", PartRole.Leg, -1, Vector3.Zero, Box(0, -0.22f, 0, 0.3f, 0.44f, 0.32f, Slate, 0.85f)),
            R(1, "SHIN", PartRole.Leg, 0, new(0, -0.44f, 0), new(0.15f, 0, 0), Box(0, -0.22f, 0, 0.16f, 0.44f, 0.16f, Slate)),
            R(2, "HOOF", PartRole.Foot, 1, new(0, -0.44f, 0), new(-0.15f, 0, 0), Box(0, -0.07f, 0.02f, 0.2f, 0.14f, 0.22f, Soot, 1.3f)),
        }, Stick: false, ReachGround: true),
        new Item("STOMPER", "LEGS", () => new List<Part>
        {
            P(0, "THIGH", PartRole.Leg, -1, Vector3.Zero, Box(0, -0.2f, 0, 0.42f, 0.4f, 0.42f, Slate, 0.9f)),
            P(1, "SHIN", PartRole.Leg, 0, new(0, -0.4f, 0), Box(0, -0.18f, 0, 0.38f, 0.36f, 0.38f, Slate, 1.05f)),
            P(2, "PAD", PartRole.Foot, 1, new(0, -0.36f, 0),
                Box(0, -0.05f, 0.02f, 0.46f, 0.1f, 0.46f, Gunmetal),
                Box(-0.13f, -0.04f, 0.24f, 0.09f, 0.08f, 0.08f, Bone), Box(0, -0.04f, 0.25f, 0.09f, 0.08f, 0.08f, Bone),
                Box(0.13f, -0.04f, 0.24f, 0.09f, 0.08f, 0.08f, Bone)),
        }, Stick: false, ReachGround: true),
        new Item("STILT", "LEGS", () => new List<Part>
        {
            P(0, "STILT", PartRole.Leg, -1, Vector3.Zero, Box(0, -0.35f, 0, 0.1f, 0.7f, 0.1f, Chrome)),
            R(1, "STILT 2", PartRole.Leg, 0, new(0, -0.7f, 0), new(0.1f, 0, 0), Box(0, -0.35f, 0, 0.07f, 0.7f, 0.07f, Chrome)),
            P(2, "POINT", PartRole.Foot, 1, new(0, -0.7f, 0), Box(0, -0.06f, 0, 0.12f, 0.12f, 0.12f, Red, 0f)),
        }, Stick: false, ReachGround: true),
        // Folded up under itself, ready to spring.
        new Item("FROG LEG", "LEGS", () => new List<Part>
        {
            R(0, "THIGH", PartRole.Leg, -1, Vector3.Zero, new(-1.0f, 0, 0), Box(0, -0.25f, 0, 0.3f, 0.5f, 0.26f, Moss, 0.8f)),
            R(1, "SHIN", PartRole.Leg, 0, new(0, -0.5f, 0), new(2.1f, 0, 0), Box(0, -0.24f, 0, 0.18f, 0.48f, 0.18f, Moss)),
            R(2, "FOOT", PartRole.Foot, 1, new(0, -0.48f, 0), new(-1.1f, 0, 0),
                Box(0, -0.03f, 0.18f, 0.28f, 0.06f, 0.42f, Moss, 1.4f)),
        }, Stick: false, ReachGround: true),
        new Item("TENTACLE", "LEGS", () => Chain("TENTACLE", PartRole.Leg, 4, -Vector3.UnitY, 0.3f, 0.2f, 0.08f, new(0.18f, 0, 0), Rot),
            Stick: false, ReachGround: true),
        new Item("CLAW LEG", "LEGS", () => new List<Part>
        {
            R(0, "CLAW LEG", PartRole.Leg, -1, Vector3.Zero, new(0, 0, 0.45f), Box(0.4f, 0, 0, 0.8f, 0.14f, 0.14f, Gunmetal)),
            R(1, "CLAW SHIN", PartRole.Leg, 0, new(0.8f, 0, 0), new(0, 0, -0.45f), Box(0, -0.6f, 0, 0.12f, 1.2f, 0.12f, Gunmetal, 0.6f)),
        }, Stick: false, Sideways: true, ReachGround: true),
        new Item("INSECT LEG", "LEGS", () => new List<Part>
        {
            R(0, "COXA", PartRole.Leg, -1, Vector3.Zero, new(0, 0, 0.75f), Box(0.3f, 0, 0, 0.6f, 0.12f, 0.12f, Soot)),
            R(1, "FEMUR", PartRole.Leg, 0, new(0.6f, 0, 0), new(0, 0, -1.55f), Box(0.35f, 0, 0, 0.7f, 0.09f, 0.09f, Soot)),
            R(2, "TARSUS", PartRole.Foot, 1, new(0.7f, 0, 0), new(0, 0, -0.55f), Box(0.25f, 0, 0, 0.5f, 0.06f, 0.06f, Jaundice)),
        }, Stick: false, Sideways: true, ReachGround: true),
    };

    // ---- ARMS: hang from the shoulder; the walk swings them against the legs -------------
    private static IEnumerable<Item> Arms() => new[]
    {
        new Item("ARM", "ARMS", () => new List<Part>
        {
            P(0, "ARM", PartRole.Arm, -1, Vector3.Zero, Box(0, -0.18f, 0, 0.16f, 0.36f, 0.16f, Deep)),
            P(1, "FOREARM", PartRole.Arm, 0, new(0, -0.36f, 0), Box(0, -0.17f, 0, 0.15f, 0.34f, 0.15f, Deep)),
            P(2, "HAND", PartRole.Hand, 1, new(0, -0.34f, 0), Box(0, -0.07f, 0, 0.18f, 0.14f, 0.18f, Gunmetal)),
        }, Stick: false),
        new Item("BRUTE ARM", "ARMS", () => new List<Part>
        {
            R(0, "ARM", PartRole.Arm, -1, Vector3.Zero, new(-0.15f, 0, 0), Box(0, -0.2f, 0, 0.3f, 0.4f, 0.3f, Slate, 0.8f)),
            R(1, "FOREARM", PartRole.Arm, 0, new(0, -0.4f, 0), new(-0.35f, 0, 0), Box(0, -0.22f, 0, 0.32f, 0.44f, 0.32f, Slate, 1.2f)),
            P(2, "FIST", PartRole.Hand, 1, new(0, -0.44f, 0), Box(0, -0.14f, 0, 0.34f, 0.28f, 0.34f, Gunmetal),
                Box(0, -0.06f, 0.18f, 0.28f, 0.08f, 0.04f, Bone)),
        }, Stick: false),
        new Item("PINCER", "ARMS", () => new List<Part>
        {
            R(0, "ARM", PartRole.Arm, -1, Vector3.Zero, new(-0.3f, 0, 0), Box(0, -0.18f, 0, 0.15f, 0.36f, 0.15f, Rust)),
            R(1, "FOREARM", PartRole.Arm, 0, new(0, -0.36f, 0), new(-0.9f, 0, 0), Box(0, -0.18f, 0, 0.18f, 0.36f, 0.2f, Rust)),
            P(2, "CLAW", PartRole.Hand, 1, new(0, -0.36f, 0), Box(0, -0.1f, 0, 0.26f, 0.2f, 0.24f, Rust),
                Box(0, -0.32f, 0.05f, 0.1f, 0.26f, 0.08f, Bone, 0.3f)),
            R(3, "CLAW JAW", PartRole.Hand, 2, new(0, -0.18f, -0.06f), new(0.45f, 0, 0), Box(0, -0.13f, 0, 0.09f, 0.26f, 0.07f, Bone, 0.3f)),
        }, Stick: false),
        // A mantis's: the blade folded back up along the forearm, ready to strike.
        new Item("SCYTHE", "ARMS", () => new List<Part>
        {
            R(0, "ARM", PartRole.Arm, -1, Vector3.Zero, new(-0.5f, 0, 0), Box(0, -0.2f, 0, 0.12f, 0.4f, 0.12f, Moss)),
            R(1, "FOREARM", PartRole.Arm, 0, new(0, -0.4f, 0), new(-1.2f, 0, 0), Box(0, -0.2f, 0, 0.1f, 0.4f, 0.12f, Moss)),
            R(2, "BLADE", PartRole.Hand, 1, new(0, -0.4f, 0), new(2.6f, 0, 0), Box(0, -0.32f, 0.02f, 0.04f, 0.64f, 0.14f, Chrome, 0.15f)),
        }, Stick: false),
        new Item("TENTACLE ARM", "ARMS", () => Chain("TENTACLE ARM", PartRole.Arm, 5, -Vector3.UnitY, 0.22f, 0.14f, 0.05f, new(-0.2f, 0, 0), Bruise, PartRole.Hand),
            Stick: false),
        new Item("GUN ARM", "ARMS", () => new List<Part>
        {
            P(0, "ARM", PartRole.Arm, -1, Vector3.Zero, Box(0, -0.18f, 0, 0.18f, 0.36f, 0.18f, Gunmetal)),
            R(1, "CANNON", PartRole.Arm, 0, new(0, -0.36f, 0), new(-1.45f, 0, 0),
                Box(0, -0.16f, 0, 0.22f, 0.32f, 0.22f, Gunmetal), Box(0, -0.48f, 0, 0.12f, 0.36f, 0.12f, Chrome)),
            P(2, "MUZZLE", PartRole.Hand, 1, new(0, -0.66f, 0), Box(0, -0.04f, 0, 0.16f, 0.08f, 0.16f, Red)),
        }, Stick: false),
        new Item("WING ARM", "ARMS", () => new List<Part>
        {
            R(0, "ARM", PartRole.Arm, -1, Vector3.Zero, new(-0.2f, 0, 0), Box(0, -0.18f, 0, 0.12f, 0.36f, 0.12f, Dusk)),
            R(1, "FOREARM", PartRole.Arm, 0, new(0, -0.36f, 0), new(-0.5f, 0, 0), Box(0, -0.2f, 0, 0.1f, 0.4f, 0.1f, Dusk)),
            P(2, "TALONS", PartRole.Hand, 1, new(0, -0.4f, 0),
                Box(-0.05f, -0.1f, 0.04f, 0.04f, 0.2f, 0.04f, Bone, 0.2f), Box(0.05f, -0.1f, 0.04f, 0.04f, 0.2f, 0.04f, Bone, 0.2f),
                Box(0, -0.08f, -0.04f, 0.04f, 0.16f, 0.04f, Bone, 0.2f)),
        }, Stick: false),
    };

    // ---- HEADS: point forward and stay upright wherever they are dropped ---------------
    private static IEnumerable<Item> Heads() => new[]
    {
        new Item("HEAD", "HEADS", () => new List<Part>
        {
            P(0, "HEAD", PartRole.Head, -1, Vector3.Zero,
                Box(0, 0.22f, 0.22f, 0.5f, 0.5f, 0.5f, Gunmetal),
                Box(-0.13f, 0.3f, 0.48f, 0.1f, 0.1f, 0.04f, Red),
                Box(0.13f, 0.3f, 0.48f, 0.1f, 0.1f, 0.04f, Red)),
        }, Stick: false),
        new Item("SNOUT", "HEADS", () => new List<Part>
        {
            P(0, "SNOUT", PartRole.Head, -1, Vector3.Zero,
                Box(0, 0.2f, 0.18f, 0.46f, 0.36f, 0.4f, Moss),
                Box(0, 0.22f, 0.58f, 0.3f, 0.18f, 0.44f, Moss),
                Box(-0.17f, 0.36f, 0.26f, 0.08f, 0.08f, 0.1f, Jaundice), Box(0.17f, 0.36f, 0.26f, 0.08f, 0.08f, 0.1f, Jaundice),
                Box(-0.1f, 0.1f, 0.6f, 0.04f, 0.06f, 0.36f, Bone, 0.4f), Box(0.1f, 0.1f, 0.6f, 0.04f, 0.06f, 0.36f, Bone, 0.4f)),
            R(1, "JAW", PartRole.Mouth, 0, new(0, 0.06f, 0.0f), new(0.25f, 0, 0),
                Box(0, -0.05f, 0.4f, 0.28f, 0.1f, 0.78f, Moss)),
        }, Stick: false),
        new Item("BEAK HEAD", "HEADS", () => new List<Part>
        {
            P(0, "BEAK HEAD", PartRole.Head, -1, Vector3.Zero,
                Box(0, 0.22f, 0.15f, 0.4f, 0.42f, 0.4f, Chalk, 0.85f),
                Box(-0.2f, 0.28f, 0.22f, 0.04f, 0.1f, 0.1f, Soot), Box(0.2f, 0.28f, 0.22f, 0.04f, 0.1f, 0.1f, Soot),
                Box(0, 0.17f, 0.52f, 0.2f, 0.18f, 0.36f, Ember, 0.3f)),
            R(1, "LOWER BEAK", PartRole.Mouth, 0, new(0, 0.08f, 0.32f), new(0.15f, 0, 0),
                Box(0, -0.02f, 0.14f, 0.14f, 0.06f, 0.28f, Ember)),
        }, Stick: false),
        new Item("BUG HEAD", "HEADS", () => new List<Part>
        {
            P(0, "BUG HEAD", PartRole.Head, -1, Vector3.Zero,
                Box(0, 0.18f, 0.18f, 0.44f, 0.32f, 0.38f, Soot, 0.8f),
                Gem(-0.2f, 0.26f, 0.3f, 0.18f, 0.2f, 0.2f, Bile), Gem(0.2f, 0.26f, 0.3f, 0.18f, 0.2f, 0.2f, Bile)),
            R(1, "MANDIBLE L", PartRole.Mouth, 0, new(-0.1f, 0.08f, 0.36f), new(0, -0.35f, 0), Box(0, 0, 0.14f, 0.06f, 0.06f, 0.3f, Jaundice, 0.4f)),
            R(2, "MANDIBLE R", PartRole.Mouth, 0, new(0.1f, 0.08f, 0.36f), new(0, 0.35f, 0), Box(0, 0, 0.14f, 0.06f, 0.06f, 0.3f, Jaundice, 0.4f)),
        }, Stick: false),
        new Item("SKULL", "HEADS", () => new List<Part>
        {
            P(0, "SKULL", PartRole.Head, -1, Vector3.Zero,
                Box(0, 0.26f, 0.2f, 0.46f, 0.42f, 0.46f, Bone),
                Box(-0.11f, 0.3f, 0.43f, 0.12f, 0.1f, 0.02f, Soot), Box(0.11f, 0.3f, 0.43f, 0.12f, 0.1f, 0.02f, Soot),
                Box(0, 0.12f, 0.42f, 0.3f, 0.06f, 0.04f, Bone)),
            R(1, "JAW", PartRole.Mouth, 0, new(0, 0.08f, 0.05f), new(0.12f, 0, 0),
                Box(0, -0.05f, 0.2f, 0.36f, 0.1f, 0.38f, Bone),
                Box(0, 0.02f, 0.36f, 0.3f, 0.05f, 0.04f, Chalk)),
        }, Stick: false),
        new Item("HELM", "HEADS", () => new List<Part>
        {
            P(0, "HELM", PartRole.Head, -1, Vector3.Zero,
                Box(0, 0.24f, 0.16f, 0.42f, 0.48f, 0.44f, Cloth, 0.85f),
                Box(0, 0.3f, 0.39f, 0.34f, 0.08f, 0.04f, Glow),
                Box(0, 0.5f, 0.12f, 0.06f, 0.08f, 0.4f, Steel)),
        }, Stick: false),
        new Item("HAMMERHEAD", "HEADS", () => new List<Part>
        {
            P(0, "HAMMERHEAD", PartRole.Head, -1, Vector3.Zero,
                Box(0, 0.16f, 0.18f, 0.3f, 0.28f, 0.4f, Slate),
                Box(0, 0.2f, 0.38f, 1.0f, 0.14f, 0.18f, Slate),
                Box(-0.5f, 0.2f, 0.38f, 0.08f, 0.1f, 0.1f, Red), Box(0.5f, 0.2f, 0.38f, 0.08f, 0.1f, 0.1f, Red)),
        }, Stick: false),
        new Item("NECK", "HEADS", () => new List<Part>
        {
            P(0, "NECK", PartRole.Neck, -1, Vector3.Zero, Box(0, 0.25f, 0, 0.22f, 0.5f, 0.22f, Deep)),
        }),
        new Item("LONG NECK", "HEADS", () => Chain("NECK", PartRole.Neck, 3, Vector3.UnitY, 0.32f, 0.24f, 0.18f, new(0.3f, 0, 0), Deep),
            Stick: false),
    };

    // ---- MOUTHS: forward and upright; the jaw gapes when it attacks ----------------------
    private static IEnumerable<Item> Mouths() => new[]
    {
        new Item("JAW", "MOUTHS", () => new List<Part>
        {
            P(0, "UPPER JAW", PartRole.Mouth, -1, Vector3.Zero,
                Box(0, 0.06f, 0.16f, 0.3f, 0.12f, 0.32f, Rust),
                Box(-0.1f, -0.02f, 0.26f, 0.04f, 0.06f, 0.04f, Bone, 0f), Box(0.1f, -0.02f, 0.26f, 0.04f, 0.06f, 0.04f, Bone, 0f)),
            R(1, "LOWER JAW", PartRole.Mouth, 0, new(0, 0, 0.02f), new(0.3f, 0, 0), Box(0, -0.04f, 0.14f, 0.26f, 0.08f, 0.28f, Rust)),
        }, Stick: false),
        new Item("BEAK", "MOUTHS", () => new List<Part>
        {
            P(0, "BEAK", PartRole.Mouth, -1, Vector3.Zero, Box(0, 0.06f, 0.18f, 0.18f, 0.12f, 0.36f, Ember, 0.4f)),
            R(1, "UNDERBEAK", PartRole.Mouth, 0, new(0, 0, 0.02f), new(0.2f, 0, 0), Box(0, -0.03f, 0.13f, 0.14f, 0.06f, 0.26f, Ember, 0.5f)),
        }, Stick: false),
        new Item("MANDIBLES", "MOUTHS", () => new List<Part>
        {
            P(0, "MOUTHPARTS", PartRole.Mouth, -1, Vector3.Zero, Box(0, 0, 0.04f, 0.2f, 0.12f, 0.08f, Soot)),
            R(1, "MANDIBLE L", PartRole.Mouth, 0, new(-0.08f, 0, 0.06f), new(0, -0.4f, 0), Box(0, 0, 0.16f, 0.06f, 0.07f, 0.32f, Jaundice, 0.3f)),
            R(2, "MANDIBLE R", PartRole.Mouth, 0, new(0.08f, 0, 0.06f), new(0, 0.4f, 0), Box(0, 0, 0.16f, 0.06f, 0.07f, 0.32f, Jaundice, 0.3f)),
        }, Stick: false),
        new Item("TUSKS", "MOUTHS", () => new List<Part>
        {
            P(0, "TUSK BASE", PartRole.Mouth, -1, Vector3.Zero, Box(0, 0, 0.06f, 0.3f, 0.12f, 0.12f, Rot)),
            R(1, "TUSK L", PartRole.Mouth, 0, new(-0.12f, 0, 0.08f), new(-0.6f, -0.2f, 0), Box(0, 0.16f, 0, 0.07f, 0.32f, 0.07f, Bone, 0f)),
            R(2, "TUSK R", PartRole.Mouth, 0, new(0.12f, 0, 0.08f), new(-0.6f, 0.2f, 0), Box(0, 0.16f, 0, 0.07f, 0.32f, 0.07f, Bone, 0f)),
        }, Stick: false),
        new Item("FANGS", "MOUTHS", () => new List<Part>
        {
            P(0, "FANGS", PartRole.Mouth, -1, Vector3.Zero,
                Box(0, 0.04f, 0.08f, 0.26f, 0.08f, 0.16f, Bruise),
                Box(-0.08f, -0.1f, 0.13f, 0.05f, 0.2f, 0.05f, Bone, 0f), Box(0.08f, -0.1f, 0.13f, 0.05f, 0.2f, 0.05f, Bone, 0f)),
        }, Stick: false),
        new Item("PROBOSCIS", "MOUTHS", () => Chain("PROBOSCIS", PartRole.Mouth, 3, Vector3.UnitZ, 0.22f, 0.1f, 0.05f, new(0.25f, 0, 0), Rose),
            Stick: false),
        new Item("MAW", "MOUTHS", () => new List<Part>
        {
            P(0, "MAW", PartRole.Mouth, -1, Vector3.Zero,
                Box(0, 0.08f, 0.12f, 0.42f, 0.16f, 0.24f, Rot),
                Box(-0.14f, -0.02f, 0.22f, 0.05f, 0.08f, 0.05f, Bone, 0f), Box(0, -0.02f, 0.23f, 0.05f, 0.08f, 0.05f, Bone, 0f),
                Box(0.14f, -0.02f, 0.22f, 0.05f, 0.08f, 0.05f, Bone, 0f)),
            R(1, "LOWER MAW", PartRole.Mouth, 0, new(0, 0, 0.02f), new(0.45f, 0, 0),
                Box(0, -0.06f, 0.11f, 0.4f, 0.12f, 0.22f, Rot),
                Box(-0.1f, 0.02f, 0.2f, 0.05f, 0.08f, 0.05f, Bone, 0f), Box(0.1f, 0.02f, 0.2f, 0.05f, 0.08f, 0.05f, Bone, 0f)),
        }, Stick: false),
    };

    private const int Rose = 31;

    // ---- EYES: grow out of whatever they are dropped on ---------------------------------
    private static IEnumerable<Item> Eyes() => new[]
    {
        Single("EYE", "EYES", PartRole.Eye, Box(0, 0.06f, 0, 0.14f, 0.12f, 0.14f, Red)),
        Many("BIG EYE", "EYES", PartRole.Eye,
            Box(0, 0.05f, 0, 0.3f, 0.1f, 0.3f, Soot),
            Box(0, 0.11f, 0, 0.22f, 0.04f, 0.22f, Chalk),
            Box(0, 0.14f, 0, 0.1f, 0.03f, 0.1f, Soot)),
        Single("BUG EYE", "EYES", PartRole.Eye, Gem(0, 0.06f, 0, 0.24f, 0.2f, 0.24f, Bile)),
        Many("VISOR", "EYES", PartRole.Eye,
            Box(0, 0.04f, 0, 0.5f, 0.08f, 0.14f, Soot),
            Box(0, 0.09f, 0, 0.44f, 0.03f, 0.08f, Glow)),
        Many("EYE CLUSTER", "EYES", PartRole.Eye,
            Gem(0, 0.06f, 0, 0.12f, 0.12f, 0.12f, Red),
            Gem(-0.12f, 0.04f, 0.06f, 0.08f, 0.08f, 0.08f, Red), Gem(0.12f, 0.04f, 0.06f, 0.08f, 0.08f, 0.08f, Red),
            Gem(-0.07f, 0.03f, -0.1f, 0.07f, 0.07f, 0.07f, Red), Gem(0.07f, 0.03f, -0.1f, 0.07f, 0.07f, 0.07f, Red)),
        new Item("STALK EYE", "EYES", () => new List<Part>
        {
            P(0, "EYESTALK", PartRole.Stalk, -1, Vector3.Zero, Box(0, 0.22f, 0, 0.06f, 0.44f, 0.06f, Moss)),
            R(1, "EYE", PartRole.Eye, 0, new(0, 0.44f, 0), new(0.4f, 0, 0),
                Box(0, 0.06f, 0, 0.16f, 0.14f, 0.16f, Chalk), Box(0, 0.06f, 0.08f, 0.08f, 0.08f, 0.02f, Soot)),
        }),
        new Item("GLOW EYE", "EYES", () => new List<Part>
        {
            P(0, "GLOW EYE", PartRole.Eye, -1, Vector3.Zero, Box(0, 0.04f, 0, 0.2f, 0.08f, 0.2f, Gunmetal, 0.8f), Gem(0, 0.1f, 0, 0.12f, 0.12f, 0.12f, Glow)),
        }),
    };

    // ---- TAILS: run back from where they are dropped; the walk ripples them ---------------
    private static IEnumerable<Item> Tails() => new[]
    {
        new Item("TAIL", "TAILS", () => new List<Part>
        {
            P(0, "TAIL", PartRole.Tail, -1, Vector3.Zero, Box(0, 0, -0.25f, 0.3f, 0.3f, 0.5f, Rot)),
            P(1, "TAIL 2", PartRole.Tail, 0, new(0, 0, -0.5f), Box(0, 0, -0.2f, 0.22f, 0.22f, 0.4f, Rot)),
            P(2, "TAIL TIP", PartRole.Tail, 1, new(0, 0, -0.4f), Box(0, 0, -0.15f, 0.14f, 0.14f, 0.3f, Rot, taper: 0.4f)),
        }, Stick: false),
        new Item("WHIP", "TAILS", () => Chain("WHIP", PartRole.Tail, 6, -Vector3.UnitZ, 0.3f, 0.14f, 0.04f, new(0.08f, 0, 0), Bruise),
            Stick: false),
        new Item("CLUB TAIL", "TAILS", () => new List<Part>
        {
            P(0, "TAIL", PartRole.Tail, -1, Vector3.Zero, Box(0, 0, -0.25f, 0.26f, 0.26f, 0.5f, Slate)),
            P(1, "TAIL 2", PartRole.Tail, 0, new(0, 0, -0.5f), Box(0, 0, -0.22f, 0.18f, 0.18f, 0.44f, Slate)),
            P(2, "CLUB", PartRole.Tail, 1, new(0, 0, -0.44f),
                Box(0, 0, -0.18f, 0.44f, 0.3f, 0.36f, Gunmetal),
                Box(-0.26f, 0, -0.18f, 0.1f, 0.1f, 0.1f, Bone, 0f), Box(0.26f, 0, -0.18f, 0.1f, 0.1f, 0.1f, Bone, 0f)),
        }, Stick: false),
        new Item("SPIKE TAIL", "TAILS", () => new List<Part>
        {
            P(0, "TAIL", PartRole.Tail, -1, Vector3.Zero, Box(0, 0, -0.27f, 0.26f, 0.26f, 0.54f, Moss)),
            R(1, "TAIL TIP", PartRole.Tail, 0, new(0, 0, -0.54f), new(0.1f, 0, 0),
                Box(0, 0, -0.25f, 0.16f, 0.16f, 0.5f, Moss, 0.6f),
                Box(-0.12f, 0.16f, -0.12f, 0.06f, 0.34f, 0.06f, Bone, 0f), Box(0.12f, 0.16f, -0.12f, 0.06f, 0.34f, 0.06f, Bone, 0f),
                Box(-0.1f, 0.14f, -0.36f, 0.05f, 0.28f, 0.05f, Bone, 0f), Box(0.1f, 0.14f, -0.36f, 0.05f, 0.28f, 0.05f, Bone, 0f)),
        }, Stick: false),
        new Item("FAN TAIL", "TAILS", () => new List<Part>
        {
            P(0, "TAIL", PartRole.Tail, -1, Vector3.Zero, Box(0, 0, -0.18f, 0.18f, 0.22f, 0.36f, Teal)),
            P(1, "FAN", PartRole.Tail, 0, new(0, 0, -0.36f),
                Box(0, 0, -0.2f, 0.04f, 0.6f, 0.4f, Teal, 1f),
                Box(0, 0.28f, -0.32f, 0.03f, 0.16f, 0.2f, Ice), Box(0, -0.28f, -0.32f, 0.03f, 0.16f, 0.2f, Ice)),
        }, Stick: false),
        new Item("FLUKE", "TAILS", () => new List<Part>
        {
            P(0, "TAIL", PartRole.Tail, -1, Vector3.Zero, Box(0, 0, -0.25f, 0.26f, 0.22f, 0.5f, Night)),
            P(1, "TAIL 2", PartRole.Tail, 0, new(0, 0, -0.5f), Box(0, 0, -0.2f, 0.16f, 0.14f, 0.4f, Night)),
            P(2, "FLUKES", PartRole.Tail, 1, new(0, 0, -0.4f), Box(0, 0, -0.14f, 0.8f, 0.04f, 0.28f, Night, 1f)),
        }, Stick: false),
        // Curled up over the back, the barb pointing forward.
        new Item("STINGER", "TAILS", () =>
        {
            var parts = Chain("STINGER", PartRole.Tail, 5, -Vector3.UnitZ, 0.28f, 0.2f, 0.11f, new(0.55f, 0, 0), Jaundice);
            parts[^1].Blocks.Add(Box(0, 0.04f, -0.36f, 0.07f, 0.07f, 0.24f, Red, 0f));
            return parts;
        }, Stick: false),
    };

    private const int Night = 16;

    // ---- WINGS AND FINS: reach out sideways, mirrored on the left -----------------------
    private static IEnumerable<Item> Wings() => new[]
    {
        new Item("WING", "WINGS", () => new List<Part>
        {
            P(0, "WING", PartRole.Wing, -1, Vector3.Zero, Box(0.45f, 0, 0, 0.9f, 0.05f, 0.45f, Deep)),
            P(1, "WING TIP", PartRole.Wing, 0, new(0.9f, 0, 0), Box(0.3f, 0, -0.05f, 0.6f, 0.04f, 0.3f, Deep, taper: 1f)),
        }, Stick: false, Sideways: true),
        new Item("BAT WING", "WINGS", () => new List<Part>
        {
            R(0, "WING ARM", PartRole.Wing, -1, Vector3.Zero, new(0, 0, 0.3f),
                Box(0.3f, 0, 0, 0.6f, 0.07f, 0.07f, Dusk), Box(0.3f, 0, -0.22f, 0.6f, 0.02f, 0.4f, Bruise)),
            R(1, "WING HAND", PartRole.Wing, 0, new(0.6f, 0, 0), new(0, 0, -0.25f),
                Box(0.35f, 0, 0, 0.7f, 0.05f, 0.05f, Dusk), Box(0.3f, 0, -0.26f, 0.6f, 0.02f, 0.46f, Bruise),
                Box(0.62f, 0, -0.42f, 0.04f, 0.04f, 0.5f, Dusk)),
        }, Stick: false, Sideways: true),
        new Item("FEATHER WING", "WINGS", () => new List<Part>
        {
            P(0, "WING", PartRole.Wing, -1, Vector3.Zero,
                Box(0.35f, 0, 0, 0.7f, 0.08f, 0.2f, Chalk),
                Box(0.35f, -0.02f, -0.2f, 0.66f, 0.03f, 0.26f, Chalk), Box(0.4f, -0.04f, -0.42f, 0.6f, 0.03f, 0.24f, Webbing)),
            R(1, "WING TIP", PartRole.Wing, 0, new(0.7f, 0, 0), new(0, -0.2f, 0),
                Box(0.3f, 0, -0.06f, 0.6f, 0.05f, 0.18f, Chalk),
                Box(0.4f, -0.02f, -0.26f, 0.7f, 0.03f, 0.24f, Webbing), Box(0.5f, -0.04f, -0.46f, 0.66f, 0.03f, 0.2f, Soot)),
        }, Stick: false, Sideways: true),
        new Item("BUG WING", "WINGS", () => new List<Part>
        {
            R(0, "FOREWING", PartRole.Wing, -1, Vector3.Zero, new(0, 0.25f, 0.15f), Box(0.5f, 0, 0, 1.0f, 0.02f, 0.26f, Ice)),
            R(1, "HINDWING", PartRole.Wing, 0, new(0.02f, -0.02f, -0.06f), new(0, 0.45f, 0), Box(0.42f, 0, 0, 0.84f, 0.02f, 0.22f, Ice)),
        }, Stick: false, Sideways: true),
        new Item("FIN", "WINGS", () => new List<Part>
        {
            P(0, "FIN", PartRole.Fin, -1, Vector3.Zero, Box(0.25f, 0, 0, 0.5f, 0.05f, 0.35f, Teal)),
        }, Stick: false, Sideways: true),
        new Item("FLIPPER", "WINGS", () => new List<Part>
        {
            R(0, "FLIPPER", PartRole.Fin, -1, Vector3.Zero, new(0, 0, -0.35f), Box(0.2f, 0, 0, 0.4f, 0.1f, 0.24f, Night)),
            R(1, "PADDLE", PartRole.Fin, 0, new(0.4f, 0, 0), new(0, -0.3f, 0), Box(0.25f, 0, 0, 0.5f, 0.05f, 0.32f, Night, 1f)),
        }, Stick: false, Sideways: true),
        Single("DORSAL FIN", "WINGS", PartRole.Fin, Box(0, 0.25f, -0.05f, 0.05f, 0.5f, 0.6f, Teal, 0.15f)),
    };

    // ---- DETAILS: rigid things that grow out of the surface ---------------------------
    private static IEnumerable<Item> Details() => new[]
    {
        Single("SPIKE", "DETAILS", PartRole.Detail, Box(0, 0.3f, 0, 0.24f, 0.6f, 0.24f, Bone, taper: 0f)),
        Single("HORN", "DETAILS", PartRole.Detail, Box(0, 0.25f, 0, 0.14f, 0.5f, 0.14f, Bone, taper: 0f)),
        new Item("CURVED HORN", "DETAILS", () =>
        {
            var parts = Chain("HORN", PartRole.Detail, 3, Vector3.UnitY, 0.22f, 0.16f, 0.07f, new(-0.45f, 0, 0), Bone);
            parts[^1].Blocks[0].Taper = 0f;
            return parts;
        }),
        new Item("ANTLER", "DETAILS", () => new List<Part>
        {
            R(0, "ANTLER", PartRole.Detail, -1, Vector3.Zero, new(-0.2f, 0, 0), Box(0, 0.25f, 0, 0.08f, 0.5f, 0.08f, Webbing)),
            R(1, "TINE", PartRole.Detail, 0, new(0, 0.2f, 0), new(-0.9f, 0, 0), Box(0, 0.13f, 0, 0.06f, 0.26f, 0.06f, Webbing, 0.3f)),
            R(2, "BRANCH", PartRole.Detail, 0, new(0, 0.5f, 0), new(0.5f, 0, 0.4f), Box(0, 0.18f, 0, 0.06f, 0.36f, 0.06f, Webbing, 0.4f)),
            R(3, "TINE 2", PartRole.Detail, 0, new(0, 0.5f, 0), new(-0.6f, 0, -0.3f), Box(0, 0.15f, 0, 0.05f, 0.3f, 0.05f, Webbing, 0.3f)),
        }),
        Single("BACK PLATE", "DETAILS", PartRole.Detail, Box(0, 0.25f, 0, 0.05f, 0.5f, 0.5f, Brass, 0.25f)),
        Many("SPINES", "DETAILS", PartRole.Detail,
            Box(0, 0.18f, 0.18f, 0.08f, 0.36f, 0.08f, Bone, 0f), Box(0, 0.24f, 0, 0.1f, 0.48f, 0.1f, Bone, 0f),
            Box(0, 0.16f, -0.18f, 0.08f, 0.32f, 0.08f, Bone, 0f)),
        new Item("ANTENNA", "DETAILS", () => Chain("FEELER", PartRole.Stalk, 3, Vector3.UnitY, 0.24f, 0.04f, 0.025f, new(0.35f, 0, 0), Soot,
            tipExtra: Gem(0, 0.24f, 0, 0.08f, 0.08f, 0.08f, Glow))),
        Single("EAR", "DETAILS", PartRole.Detail, Box(0, 0.18f, 0, 0.18f, 0.36f, 0.04f, Rose, 0.3f)),
        Many("CREST", "DETAILS", PartRole.Detail,
            Box(0, 0.22f, 0, 0.03f, 0.44f, 0.14f, Ember, 0.4f), Box(0, 0.18f, -0.14f, 0.03f, 0.36f, 0.12f, Red, 0.4f),
            Box(0, 0.13f, -0.26f, 0.03f, 0.26f, 0.1f, Mark, 0.4f)),
        Many("FRILL", "DETAILS", PartRole.Detail,
            Box(0, 0.22f, 0, 0.7f, 0.44f, 0.04f, Mark, 0.7f), Box(0, 0.36f, -0.01f, 0.5f, 0.12f, 0.03f, Jaundice, 0.6f)),
        Many("SHELL", "DETAILS", PartRole.Detail,
            Box(0, 0.06f, 0, 0.8f, 0.12f, 0.9f, Copper, 0.9f),
            Box(0, 0.17f, 0, 0.66f, 0.12f, 0.74f, Webbing, 0.85f),
            Box(0, 0.27f, 0, 0.46f, 0.1f, 0.52f, Copper, 0.7f)),
        Single("KNOB", "DETAILS", PartRole.Detail, Gem(0, 0.06f, 0, 0.16f, 0.16f, 0.16f, Bone)),
        Many("STUDS", "DETAILS", PartRole.Detail,
            Box(-0.12f, 0.03f, -0.12f, 0.08f, 0.06f, 0.08f, Steel, 0.5f), Box(0.12f, 0.03f, -0.12f, 0.08f, 0.06f, 0.08f, Steel, 0.5f),
            Box(-0.12f, 0.03f, 0.12f, 0.08f, 0.06f, 0.08f, Steel, 0.5f), Box(0.12f, 0.03f, 0.12f, 0.08f, 0.06f, 0.08f, Steel, 0.5f)),
    };

    // ---- PLANT ------------------------------------------------------------------------
    private static IEnumerable<Item> Plant() => new[]
    {
        Single("PETAL", "PLANT", PartRole.Petal, Box(0, 0.25f, 0, 0.24f, 0.5f, 0.06f, Jaundice, taper: 0.5f)),
        new Item("STALK", "PLANT", () => new List<Part>
        {
            P(0, "STALK", PartRole.Stalk, -1, Vector3.Zero, Box(0, 0.4f, 0, 0.12f, 0.8f, 0.12f, Teal)),
            P(1, "STALK 2", PartRole.Stalk, 0, new(0, 0.8f, 0), Box(0, 0.4f, 0, 0.1f, 0.8f, 0.1f, Teal)),
        }),
        Single("LEAF", "PLANT", PartRole.Petal, Box(0, 0.3f, 0, 0.3f, 0.6f, 0.03f, Pine, taper: 0.2f)),
        Single("BULB", "PLANT", PartRole.Core, Gem(0, 0.22f, 0, 0.36f, 0.44f, 0.36f, Magenta)),
        Single("THORN", "PLANT", PartRole.Detail, Box(0, 0.12f, 0, 0.1f, 0.24f, 0.1f, Pine, taper: 0f)),
        new Item("VINE", "PLANT", () => Chain("VINE", PartRole.Stalk, 5, Vector3.UnitY, 0.26f, 0.08f, 0.04f, new(0.3f, 0.4f, 0), Pine,
            tipExtra: Box(0, 0.26f, 0, 0.16f, 0.2f, 0.03f, Moss, 0.2f))),
        new Item("BLOOM", "PLANT", () => new List<Part>
        {
            P(0, "BLOOM", PartRole.Core, -1, Vector3.Zero, Gem(0, 0.08f, 0, 0.16f, 0.16f, 0.16f, Jaundice)),
            R(1, "PETAL 1", PartRole.Petal, 0, Vector3.Zero, new(0.9f, 0, 0), Box(0, 0.16f, 0, 0.14f, 0.32f, 0.03f, Magenta, 0.5f)),
            R(2, "PETAL 2", PartRole.Petal, 0, Vector3.Zero, new(-0.9f, 0, 0), Box(0, 0.16f, 0, 0.14f, 0.32f, 0.03f, Magenta, 0.5f)),
            R(3, "PETAL 3", PartRole.Petal, 0, Vector3.Zero, new(0, 0, 0.9f), Box(0, 0.16f, 0, 0.03f, 0.32f, 0.14f, Magenta, 0.5f)),
            R(4, "PETAL 4", PartRole.Petal, 0, Vector3.Zero, new(0, 0, -0.9f), Box(0, 0.16f, 0, 0.03f, 0.32f, 0.14f, Magenta, 0.5f)),
        }),
    };

    // ---- MACHINE: what vehicles are made of -------------------------------------------
    private static IEnumerable<Item> Machine() => new[]
    {
        Single("CHASSIS", "MACHINE", PartRole.Decor, Box(0, 0.2f, 0, 1.2f, 0.4f, 1.6f, Slate)),
        new Item("WHEEL", "MACHINE", () => new List<Part>
        {
            P(0, "WHEEL", PartRole.Wheel, -1, Vector3.Zero, Box(0.09f, 0, 0, 0.18f, 0.55f, 0.55f, Gunmetal), Box(0.19f, 0, 0, 0.04f, 0.2f, 0.2f, Chrome)),
        }, Stick: false, Sideways: true),
        new Item("TREAD", "MACHINE", () => new List<Part>
        {
            P(0, "TREAD", PartRole.Tread, -1, Vector3.Zero, Box(0.2f, 0, 0, 0.4f, 0.5f, 2.2f, Gunmetal)),
            P(1, "WHEEL 1", PartRole.Wheel, 0, new(0.22f, 0, -0.7f), Box(0, 0, 0, 0.42f, 0.4f, 0.4f, Chrome)),
            P(2, "WHEEL 2", PartRole.Wheel, 0, new(0.22f, 0, 0), Box(0, 0, 0, 0.42f, 0.4f, 0.4f, Chrome)),
            P(3, "WHEEL 3", PartRole.Wheel, 0, new(0.22f, 0, 0.7f), Box(0, 0, 0, 0.42f, 0.4f, 0.4f, Chrome)),
        }, Stick: false, Sideways: true),
        new Item("TURRET", "MACHINE", () => new List<Part>
        {
            P(0, "TURRET", PartRole.Turret, -1, Vector3.Zero, Box(0, 0.2f, 0, 0.9f, 0.4f, 0.9f, Slate, taper: 0.7f)),
            P(1, "BARREL", PartRole.Barrel, 0, new(0, 0.2f, 0.35f), Box(0, 0, 0.6f, 0.18f, 0.18f, 1.2f, Chrome)),
        }, Stick: false),
        new Item("BARREL", "MACHINE", () => new List<Part>
        {
            P(0, "BARREL", PartRole.Barrel, -1, Vector3.Zero, Box(0, 0, 0.6f, 0.18f, 0.18f, 1.2f, Chrome)),
        }, Stick: false),
        new Item("TWIN GUNS", "MACHINE", () => new List<Part>
        {
            P(0, "GUN MOUNT", PartRole.Turret, -1, Vector3.Zero, Box(0, 0.12f, 0, 0.5f, 0.24f, 0.4f, Gunmetal, 0.8f)),
            P(1, "BARRELS", PartRole.Barrel, 0, new(0, 0.14f, 0.2f),
                Box(-0.12f, 0, 0.4f, 0.1f, 0.1f, 0.8f, Chrome), Box(0.12f, 0, 0.4f, 0.1f, 0.1f, 0.8f, Chrome)),
        }, Stick: false),
        Single("CORE", "MACHINE", PartRole.Core, Gem(0, 0.25f, 0, 0.5f, 0.5f, 0.5f, Red)),
        Single("ANTENNA ROD", "MACHINE", PartRole.Decor, Box(0, 0.5f, 0, 0.05f, 1f, 0.05f, Chrome)),
        Single("VENT", "MACHINE", PartRole.Detail, Ramp(0, 0.08f, 0, 0.5f, 0.16f, 0.3f, Gunmetal)),
        new Item("THRUSTER", "MACHINE", () => new List<Part>
        {
            P(0, "THRUSTER", PartRole.Detail, -1, Vector3.Zero,
                Box(0, 0, -0.2f, 0.34f, 0.34f, 0.4f, Gunmetal), Box(0, 0, -0.44f, 0.26f, 0.26f, 0.08f, Ember)),
        }, Stick: false),
        Many("DISH", "MACHINE", PartRole.Detail,
            Box(0, 0.12f, 0, 0.06f, 0.24f, 0.06f, Chrome), Box(0, 0.28f, 0, 0.5f, 0.08f, 0.5f, Chrome, 1.6f),
            Gem(0, 0.36f, 0, 0.08f, 0.12f, 0.08f, Red)),
        Many("ARMOUR", "MACHINE", PartRole.Detail,
            Box(0, 0.05f, 0, 0.7f, 0.1f, 0.6f, Gunmetal, 0.9f), Box(0, 0.12f, 0.2f, 0.5f, 0.06f, 0.1f, Mark)),
        new Item("PISTON LEG", "MACHINE", () => new List<Part>
        {
            P(0, "PISTON", PartRole.Leg, -1, Vector3.Zero, Box(0, -0.25f, 0, 0.26f, 0.5f, 0.26f, Gunmetal)),
            P(1, "ROD", PartRole.Leg, 0, new(0, -0.5f, 0), Box(0, -0.25f, 0, 0.1f, 0.5f, 0.1f, Chrome)),
            P(2, "FOOTPLATE", PartRole.Foot, 1, new(0, -0.5f, 0), Box(0, -0.04f, 0.04f, 0.36f, 0.08f, 0.44f, Gunmetal)),
        }, Stick: false, ReachGround: true),
    };

    // ---- GAME: the game's own meshes, triangle for triangle -----------------------------
    private static IEnumerable<Item> Game() => new[]
    {
        FromMesh("TANK HULL", Meshes.TankHull(Palette.PlayerFill)),
        FromMesh("TANK CAP", Meshes.TankCap(Palette.PlayerFill)),
        FromMesh("TANK GUN", Meshes.TankBarrel(Palette.HudChrome)),
        FromMesh("ELITE CONE", Meshes.EliteCone(Palette.EliteFill)),
        FromMesh("CRAB SHELL", Meshes.CrabBodyUpper(Palette.CrabChassis)),
        FromMesh("CRAB BASE", Meshes.CrabBodyLower(Palette.CrabChassis)),
        FromMesh("CRAB GEM", Meshes.CrabCoreGem(Palette.NeonRed)),
        FromMesh("CRAB LEG", Meshes.CrabLeg(Palette.CrabChassis)),
        FromMesh("MAW JAW", Meshes.MawJaw(Palette.MawShell)),
        FromMesh("MAW TOOTH", Meshes.MawTooth(Palette.MawTooth)),
        FromMesh("WRAITH", Meshes.ShipInterceptor(Palette.EnemyFill)),
        FromMesh("PALLBEARER", Meshes.ShipGunship(Palette.EnemyFill)),
        FromMesh("NULL-9", Meshes.ShipScout(Palette.EnemyFill)),
        FromMesh("SHARD", Meshes.Shard(Palette.NeonRed)),
        FromMesh("CRYSTAL", Meshes.Crystal(Palette.ZincPale)),
        FromMesh("BATTERY", Meshes.Battery(Palette.BatteryFill, Palette.BatteryCore)),
        FromMesh("ROD", Meshes.Rod(Palette.HudChrome, Palette.LithiumRose)),
        FromMesh("SCRAP", Meshes.ScrapPlate(Palette.ScrapSteel)),
        FromMesh("COIL", Meshes.WireCoil(Palette.CopperWire)),
        FromMesh("INGOT", Meshes.Ingot(Palette.AlloyIngot, Palette.HudChrome)),
        FromMesh("MOONSTONE", Meshes.MoonShard(Palette.MoonStone, Palette.MoonBreak)),
        FromMesh("REPAIR KIT", Meshes.RepairKit(Palette.RepairShell, Palette.RepairMark)),
    };
}
