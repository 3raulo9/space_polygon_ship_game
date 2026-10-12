using System.Numerics;
using Unrendered.Core;
using Unrendered.Rendering;

namespace Unrendered.Creator;

/// <summary>
/// The shapes bar: everything that can be dragged off the palette onto a body, the way the
/// Spore creator's part palette works. Each item is a small ready-jointed assembly — a lone
/// block, or a whole leg with its knee and foot already hinged and given the roles the walk
/// cycle reads — so a creature that has had legs dropped on it walks.
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

    public static readonly string[] Categories = { "BLOCKS", "LIMBS", "MACHINE", "GAME" };

    private static List<Item>? _all;
    public static IReadOnlyList<Item> All => _all ??= Build();

    public static List<Item> In(string category) => All.Where(i => i.Category == category).ToList();

    /// <summary>
    /// Puts a copy of <paramref name="item"/> into <paramref name="d"/> and returns its root.
    /// The root is not attached to anything yet; the caller places it.
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
            d.Parts.Add(p);
        }
        root.Stick = item.Stick;
        return root;
    }

    // =========================================================================

    private static Block Box(float x, float y, float z, float sx, float sy, float sz, int swatch, float taper = 1f, BlockShape shape = BlockShape.Box)
        => new() { Center = new(x, y, z), Size = new(sx, sy, sz), Swatch = swatch, Taper = taper, Shape = shape };

    private static Part P(int id, string name, PartRole role, int parent, Vector3 pivot, params Block[] blocks)
        => new() { Id = id, Name = name, Role = role, Parent = parent, Pivot = pivot, Blocks = blocks.ToList() };

    private static Item Single(string label, string cat, PartRole role, Block b, bool stick = true)
        => new(label, cat, () => new List<Part> { P(0, label, role, -1, Vector3.Zero, b) }, stick);

    private static Item FromMesh(string label, PolyMesh mesh)
        => new(label, "GAME", () => new List<Part> { P(0, label, PartRole.Decor, -1, Vector3.Zero, Block.FromTriangles(mesh.Triangles)) });

    private static List<Item> Build()
    {
        var items = new List<Item>
        {
            // ---- BLOCKS: they sit on the surface, base down --------------------------
            Single("CUBE", "BLOCKS", PartRole.Decor, Box(0, 0.2f, 0, 0.4f, 0.4f, 0.4f, 1)),
            Single("SLAB", "BLOCKS", PartRole.Decor, Box(0, 0.06f, 0, 0.7f, 0.12f, 0.7f, 10)),
            Single("BEAM", "BLOCKS", PartRole.Decor, Box(0, 0.07f, 0, 0.14f, 0.14f, 0.9f, 10)),
            Single("PILLAR", "BLOCKS", PartRole.Decor, Box(0, 0.4f, 0, 0.16f, 0.8f, 0.16f, 1)),
            Single("WEDGE", "BLOCKS", PartRole.Decor, Box(0, 0.15f, 0, 0.4f, 0.3f, 0.5f, 1, shape: BlockShape.Wedge)),
            Single("SPIKE", "BLOCKS", PartRole.Decor, Box(0, 0.3f, 0, 0.24f, 0.6f, 0.24f, 9, taper: 0f)),
            Single("STEP", "BLOCKS", PartRole.Decor, Box(0, 0.2f, 0, 0.5f, 0.4f, 0.5f, 1, taper: 0.55f)),
            Single("GEM", "BLOCKS", PartRole.Core, Box(0, 0.2f, 0, 0.4f, 0.4f, 0.4f, 8, shape: BlockShape.Octa)),
            Single("PLATE", "BLOCKS", PartRole.Decor, Box(0, 0.03f, 0, 0.5f, 0.06f, 0.3f, 0)),

            // ---- LIMBS: jointed, with the roles the built-in walk reads ---------------
            new("LEG", "LIMBS", () => new List<Part>
            {
                P(0, "THIGH", PartRole.Leg, -1, Vector3.Zero, Box(0, -0.22f, 0, 0.22f, 0.44f, 0.22f, 6)),
                P(1, "SHIN", PartRole.Leg, 0, new(0, -0.44f, 0), Box(0, -0.2f, 0, 0.2f, 0.4f, 0.2f, 6)),
                P(2, "FOOT", PartRole.Foot, 1, new(0, -0.4f, 0), Box(0, -0.05f, 0.06f, 0.24f, 0.1f, 0.34f, 10)),
            }, Stick: false, ReachGround: true),
            new("CLAW LEG", "LIMBS", () => new List<Part>
            {
                new() { Id = 0, Name = "CLAW LEG", Role = PartRole.Leg, Parent = -1, Rest = new(0, 0, 0.45f),
                        Blocks = { Box(0.4f, 0, 0, 0.8f, 0.14f, 0.14f, 10) } },
                new() { Id = 1, Name = "CLAW SHIN", Role = PartRole.Leg, Parent = 0, Pivot = new(0.8f, 0, 0), Rest = new(0, 0, -0.45f),
                        Blocks = { Box(0, -0.6f, 0, 0.12f, 1.2f, 0.12f, 10, taper: 0.6f) } },
            }, Stick: false, Sideways: true, ReachGround: true),
            new("ARM", "LIMBS", () => new List<Part>
            {
                P(0, "ARM", PartRole.Arm, -1, Vector3.Zero, Box(0, -0.18f, 0, 0.16f, 0.36f, 0.16f, 6)),
                P(1, "FOREARM", PartRole.Arm, 0, new(0, -0.36f, 0), Box(0, -0.17f, 0, 0.15f, 0.34f, 0.15f, 6)),
                P(2, "HAND", PartRole.Hand, 1, new(0, -0.34f, 0), Box(0, -0.07f, 0, 0.18f, 0.14f, 0.18f, 10)),
            }, Stick: false),
            new("TAIL", "LIMBS", () => new List<Part>
            {
                P(0, "TAIL", PartRole.Tail, -1, Vector3.Zero, Box(0, 0, -0.25f, 0.3f, 0.3f, 0.5f, 11)),
                P(1, "TAIL 2", PartRole.Tail, 0, new(0, 0, -0.5f), Box(0, 0, -0.2f, 0.22f, 0.22f, 0.4f, 11)),
                P(2, "TAIL TIP", PartRole.Tail, 1, new(0, 0, -0.4f), Box(0, 0, -0.15f, 0.14f, 0.14f, 0.3f, 11, taper: 0.4f)),
            }, Stick: false),
            new("HEAD", "LIMBS", () => new List<Part>
            {
                P(0, "HEAD", PartRole.Head, -1, Vector3.Zero,
                    Box(0, 0.25f, 0.05f, 0.5f, 0.5f, 0.5f, 10),
                    Box(-0.13f, 0.32f, 0.3f, 0.1f, 0.1f, 0.04f, 8),
                    Box(0.13f, 0.32f, 0.3f, 0.1f, 0.1f, 0.04f, 8)),
            }),
            new("NECK", "LIMBS", () => new List<Part>
            {
                P(0, "NECK", PartRole.Neck, -1, Vector3.Zero, Box(0, 0.25f, 0, 0.22f, 0.5f, 0.22f, 6)),
            }),
            Single("EYE", "LIMBS", PartRole.Decor, Box(0, 0.06f, 0, 0.14f, 0.12f, 0.14f, 8)),
            Single("HORN", "LIMBS", PartRole.Decor, Box(0, 0.25f, 0, 0.14f, 0.5f, 0.14f, 9, taper: 0f)),
            new("FIN", "LIMBS", () => new List<Part>
            {
                P(0, "FIN", PartRole.Fin, -1, Vector3.Zero, Box(0.25f, 0, 0, 0.5f, 0.05f, 0.35f, 6)),
            }, Stick: false, Sideways: true),
            new("WING", "LIMBS", () => new List<Part>
            {
                P(0, "WING", PartRole.Wing, -1, Vector3.Zero, Box(0.45f, 0, 0, 0.9f, 0.05f, 0.45f, 6)),
                P(1, "WING TIP", PartRole.Wing, 0, new(0.9f, 0, 0), Box(0.3f, 0, -0.05f, 0.6f, 0.04f, 0.3f, 6, taper: 1f)),
            }, Stick: false, Sideways: true),
            Single("PETAL", "LIMBS", PartRole.Petal, Box(0, 0.25f, 0, 0.24f, 0.5f, 0.06f, 4, taper: 0.5f)),
            new("STALK", "LIMBS", () => new List<Part>
            {
                P(0, "STALK", PartRole.Stalk, -1, Vector3.Zero, Box(0, 0.4f, 0, 0.12f, 0.8f, 0.12f, 5)),
                P(1, "STALK 2", PartRole.Stalk, 0, new(0, 0.8f, 0), Box(0, 0.4f, 0, 0.1f, 0.8f, 0.1f, 5)),
            }),

            // ---- MACHINE: what vehicles are made of ------------------------------------
            Single("CHASSIS", "MACHINE", PartRole.Decor, Box(0, 0.2f, 0, 1.2f, 0.4f, 1.6f, 1)),
            new("WHEEL", "MACHINE", () => new List<Part>
            {
                P(0, "WHEEL", PartRole.Wheel, -1, Vector3.Zero, Box(0.09f, 0, 0, 0.18f, 0.55f, 0.55f, 10), Box(0.19f, 0, 0, 0.04f, 0.2f, 0.2f, 0)),
            }, Stick: false, Sideways: true),
            new("TREAD", "MACHINE", () => new List<Part>
            {
                P(0, "TREAD", PartRole.Tread, -1, Vector3.Zero, Box(0.2f, 0, 0, 0.4f, 0.5f, 2.2f, 10)),
                P(1, "WHEEL 1", PartRole.Wheel, 0, new(0.22f, 0, -0.7f), Box(0, 0, 0, 0.42f, 0.4f, 0.4f, 0)),
                P(2, "WHEEL 2", PartRole.Wheel, 0, new(0.22f, 0, 0), Box(0, 0, 0, 0.42f, 0.4f, 0.4f, 0)),
                P(3, "WHEEL 3", PartRole.Wheel, 0, new(0.22f, 0, 0.7f), Box(0, 0, 0, 0.42f, 0.4f, 0.4f, 0)),
            }, Stick: false, Sideways: true),
            new("TURRET", "MACHINE", () => new List<Part>
            {
                P(0, "TURRET", PartRole.Turret, -1, Vector3.Zero, Box(0, 0.2f, 0, 0.9f, 0.4f, 0.9f, 1, taper: 0.7f)),
                P(1, "BARREL", PartRole.Barrel, 0, new(0, 0.2f, 0.35f), Box(0, 0, 0.6f, 0.18f, 0.18f, 1.2f, 0)),
            }, Stick: false),
            new("BARREL", "MACHINE", () => new List<Part>
            {
                P(0, "BARREL", PartRole.Barrel, -1, Vector3.Zero, Box(0, 0, 0.6f, 0.18f, 0.18f, 1.2f, 0)),
            }, Stick: false),
            Single("CORE", "MACHINE", PartRole.Core, Box(0, 0.25f, 0, 0.5f, 0.5f, 0.5f, 8, shape: BlockShape.Octa)),
            Single("ANTENNA", "MACHINE", PartRole.Decor, Box(0, 0.5f, 0, 0.05f, 1f, 0.05f, 0)),
            Single("VENT", "MACHINE", PartRole.Decor, Box(0, 0.08f, 0, 0.5f, 0.16f, 0.3f, 10, shape: BlockShape.Wedge)),

            // ---- GAME: the game's own meshes, triangle for triangle ---------------------
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
        return items;
    }
}
