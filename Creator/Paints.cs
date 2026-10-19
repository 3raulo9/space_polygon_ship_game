using System.Numerics;
using Raylib_cs;
using Unrendered.Core;

namespace Unrendered.Creator;

/// <summary>
/// The creator's paint box. The first twelve colours are the game's own swatches, in the same
/// order, so every design saved before the box grew still means the same colours; the rest
/// are pulled from the palette the world is already drawn in — cloth, webbing, copper, bone,
/// the arch's brass, the portal's two lights — so nothing painted here can look like it came
/// from another game.
/// </summary>
public static class Paints
{
    public static readonly (string Name, Color Color)[] All = Build();

    private static (string, Color)[] Build()
    {
        var list = ClassCatalog.Swatches.ToList();
        list.AddRange(new (string, Color)[]
        {
            ("SOOT",     new Color(24, 26, 31, 255)),
            ("CLOTH",    Palette.SoldierCloth),
            ("DUSK",     Palette.PowderDark),
            ("STRUCT",   Palette.StructureShell),
            ("NIGHT",    Palette.FloorLight),
            ("ALLOY",    Palette.AlloyIngot),
            ("LEAD",     Palette.LeadGrey),
            ("STEEL",    Palette.SoldierSteel),
            ("ZINC",     Palette.ZincPale),
            ("MOON",     Palette.MoonStone),
            ("BLADE",    Palette.SoldierBlade),
            ("CHALK",    Palette.RepairMark),
            ("WEBBING",  Palette.SoldierWebbing),
            ("COPPER",   Palette.CopperWire),
            ("BRASS",    Palette.ArchSocket),
            ("SUN",      Palette.SunStone),
            ("EMBER",    Palette.PortalSun),
            ("YELLOW",   Palette.NeonYellow),
            ("MARK",     Palette.SoldierMark),
            ("ROSE",     Palette.LithiumRose),
            ("DUST",     Palette.PowderGrain),
            ("SKY",      Palette.SkyHorizon),
            ("ICE",      Palette.PortalMoon),
            ("BILE",     Palette.MawLaser),
            ("GLOW",     Palette.BatteryCore),
            ("MOSS",     Palette.RepairShell),
            ("CYAN",     Palette.StructureGlow),
            ("PINE",     new Color(38, 74, 58, 255)),
        });
        return list.ToArray();
    }

    public static int Count => All.Length;

    public static Color Color(int index) => All[((index % All.Length) + All.Length) % All.Length].Color;
    public static string Name(int index) => All[((index % All.Length) + All.Length) % All.Length].Name;

    public static readonly PaintPattern[] Patterns = Enum.GetValues<PaintPattern>();

    /// <summary>
    /// Whether a facet takes the mark colour. <paramref name="u"/> is where the facet's middle
    /// sits in the block, 0..1 along each of its axes; <paramref name="n"/> is which way the
    /// facet faces, in the block's frame; <paramref name="size"/> is the block's own size, so
    /// stripes run across a limb's length whichever way the limb was built.
    /// </summary>
    public static bool Marked(PaintPattern pattern, Vector3 u, Vector3 n, Vector3 size) => pattern switch
    {
        PaintPattern.Belly => n.Y < -0.4f || u.Y < 0.32f,
        PaintPattern.Stripes => (int)MathF.Floor(Along(u, Length(size, sideways: 2.5f)) * 6f) % 2 == 1,
        PaintPattern.Rings => (int)MathF.Floor(u.Y * 5f) % 2 == 1,
        PaintPattern.Spots => Hash((int)MathF.Floor(u.X * 4f), (int)MathF.Floor(u.Y * 4f), (int)MathF.Floor(u.Z * 4f)) < 0.3f,
        PaintPattern.Tips => MathF.Abs(Along(u, Length(size, sideways: 1f)) - 0.5f) > 0.3f,
        PaintPattern.Dorsal => MathF.Abs(u.X - 0.5f) < 0.17f && (n.Y > 0.2f || u.Y > 0.55f),
        PaintPattern.Checker => ((int)MathF.Floor(u.X * 4f) + (int)MathF.Floor(u.Y * 4f) + (int)MathF.Floor(u.Z * 4f)) % 2 == 1,
        _ => false,
    };

    /// <summary>Which axis a block is long along: Y or Z, whichever is longer, and X only when
    /// it outreaches both by <paramref name="sideways"/> — a body segment is wide without
    /// being long, a spider's leg is long.</summary>
    private static int Length(Vector3 size, float sideways)
    {
        if (size.X > sideways * MathF.Max(size.Y, size.Z)) return 0;
        return size.Y > size.Z ? 1 : 2;
    }

    private static float Along(Vector3 u, int axis) => axis == 0 ? u.X : axis == 1 ? u.Y : u.Z;

    /// <summary>A stable 0..1 roll per cell, so spots stay where they are frame to frame and
    /// across a save.</summary>
    private static float Hash(int x, int y, int z)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h & 0xFFFF) / 65535f;
    }
}
