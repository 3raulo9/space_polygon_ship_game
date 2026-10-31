using Unrendered.Core;
using Unrendered.Entities;

namespace Unrendered.Modding;

/// <summary>
/// A kind of carried item a code mod added at run time: something that is not salvage of
/// this game, but that a pack can hold all the same — it stacks in the grid, turns up in the
/// panel with its own picture and name, and does what its mod says when it is spent or
/// thrown.
///
/// <para>It lives in the pack as an ordinary <see cref="ItemKind"/>, numbered from
/// <see cref="ModItems.First"/> up, so every rule the pack already keeps (stacking, moving,
/// the wire) holds for it unchanged. The game's own kinds never reach that range. Without a
/// code mod that registers one, none exist and nothing about the pack is different.</para>
/// </summary>
public sealed class ModItem
{
    /// <summary>The code mod it belongs to.</summary>
    public required string ModId { get; init; }

    /// <summary>Its id inside that mod. Registering the same id twice hands back the first.</summary>
    public required string Id { get; init; }

    /// <summary>The panel's hover label.</summary>
    public required string Name { get; init; }

    public int MaxStack { get; init; } = 1;

    /// <summary>
    /// Draws its picture into a <c>size</c>×<c>size</c> render texture that is already bound
    /// and cleared, once a frame; the second argument is the panel clock, for an icon that
    /// turns. Whatever it draws is what every slot holding it shows.
    /// </summary>
    public Action<int, float>? DrawIcon { get; init; }

    /// <summary>The panel's right-click on a grid slot holding it: the world, the craft and
    /// the slot's index. Returns true when one was spent (the pack loses it), false to keep
    /// it.</summary>
    public Func<World.World, PlayerTank, int, bool>? Use { get; init; }

    /// <summary>One was thrown out of a pack by this craft, at this point on the field. The
    /// game has no salvage to put down for it, so the mod decides where it goes.</summary>
    public Action<World.World, PlayerTank, System.Numerics.Vector2>? Thrown { get; init; }

    /// <summary>Anything the mod wants to keep beside it.</summary>
    public object? Tag { get; init; }

    public ItemKind Kind { get; internal set; }

    public string Key => ModId + ":" + Id;
}

/// <summary>The kinds code mods have added, by <see cref="ItemKind"/>.</summary>
public static class ModItems
{
    /// <summary>The first kind a code mod gets. Far past anything the game will ever append,
    /// and the pack's wire is a byte, so there is room for <see cref="Capacity"/>.</summary>
    public const int First = 128;
    public const int Capacity = 256 - First;

    private static readonly List<ModItem> _items = new();
    private static readonly Dictionary<string, ModItem> _byKey = new();

    public static IReadOnlyList<ModItem> All => _items;

    public static bool IsMod(ItemKind kind) => (int)kind >= First;

    public static ModItem? Get(ItemKind kind)
    {
        int i = (int)kind - First;
        return (uint)i < (uint)_items.Count ? _items[i] : null;
    }

    public static ModItem? Find(string modId, string id) => _byKey.GetValueOrDefault(modId + ":" + id);

    /// <summary>Adds a kind, or hands back the one already added under the same key. Null
    /// when every kind is taken.</summary>
    public static ModItem? Register(ModItem item)
    {
        if (_byKey.TryGetValue(item.Key, out var had)) return had;
        if (_items.Count >= Capacity) return null;
        item.Kind = (ItemKind)(First + _items.Count);
        _items.Add(item);
        _byKey[item.Key] = item;
        return item;
    }

    /// <summary>Forgets every kind. For the self-test, which must leave the game as it found it.</summary>
    internal static void Reset()
    {
        _items.Clear();
        _byKey.Clear();
    }

    /// <summary>Whether a byte off the wire or out of a pack names something real.</summary>
    public static bool Known(ItemKind kind) => IsMod(kind) ? Get(kind) != null : Enum.IsDefined(kind);
}
