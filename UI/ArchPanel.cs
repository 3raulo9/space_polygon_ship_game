using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.World;

namespace Unrendered.UI;

/// <summary>
/// The panel on a gate's leg: two screens, in order. First where the room is going, then what
/// it is going to cost.
///
/// <para>Pure state, like every other screen here — it owns a page, a cursor and a set of
/// intents, and knows nothing about mice, pixels or the wire. The game loop drives it and
/// carries out what it asks for; <c>ArchPanelRenderer</c> draws it.</para>
///
/// <para><b>Why the chart comes first.</b> The obvious order is the other way round — feed the
/// arch, then decide where it points — and it is worse: the fragments are the ceremony and the
/// argument is the admin, so putting the admin last would end a planet on a menu. This way the
/// last thing that happens before a portal opens is five people physically handing over what
/// they spent three hours carrying.</para>
/// </summary>
public sealed class ArchPanel
{
    public enum Page : byte
    {
        /// <summary>The chart. Where are we going.</summary>
        Chart,
        /// <summary>The board. Five recesses and whatever this player is carrying.</summary>
        Sockets,
        /// <summary>Nowhere left to go — the campaign has been everywhere. The panel says so
        /// and offers the way out of the run instead.</summary>
        Exhausted,
    }

    /// <summary>What the panel wants the game loop to do this frame, if anything.</summary>
    public enum Action : byte
    {
        None,
        /// <summary>Close it and go back to driving.</summary>
        Close,
        /// <summary>Cast the local player's ballot for whatever the cursor is on.</summary>
        Cast,
        /// <summary>Feed the fragment in <see cref="FeedSlot"/> into the arch.</summary>
        Feed,
        /// <summary>The campaign is over: hand the player the ending screen.</summary>
        EndRun,
    }

    /// <summary>Which gate this panel belongs to. Null when it is shut.</summary>
    public Arch? Gate { get; private set; }

    public Page Where { get; private set; } = Page.Chart;

    /// <summary>Whether the panel is up at all.</summary>
    public bool Open => Gate is not null;

    /// <summary>The chart, shared with the arch rather than copied: the vote's tally and clock
    /// live here and the host's TALLY packets land straight in it.</summary>
    public readonly StarMap Chart = new();

    /// <summary>Which pack slot the cursor is over on the socket page, or -1. Only ever a slot
    /// holding a fragment — the board does not show the rest of a pack, because the rest of a
    /// pack is not a thing an arch has any opinion about.</summary>
    public int HoverSlot { get; private set; } = -1;

    /// <summary>The slot the last <see cref="Action.Feed"/> named.</summary>
    public int FeedSlot { get; private set; } = -1;

    /// <summary>Which world the cursor is over on the chart page, or null when it is over
    /// nothing. Set by the renderer's hit test, since the mouse addresses the chart in pixels
    /// and this class has none.</summary>
    public PlanetId? HoverWorld { get; set; }

    /// <summary>
    /// Opens the panel on a gate. <paramref name="here"/> and <paramref name="visited"/> are
    /// what the chart may not offer: where the room is standing, and everywhere it has already
    /// been this session.
    /// </summary>
    public void OpenOn(Arch gate, PlanetId here, int visited)
    {
        Gate = gate;
        HoverSlot = -1;
        FeedSlot = -1;
        HoverWorld = null;

        Chart.ClearLocks();
        Chart.Lock(here);
        for (int i = 0; i < Planet.All.Count; i++)
            if ((visited & (1 << i)) != 0) Chart.Lock((PlanetId)i);

        // A gate that has already been aimed goes straight to the board — the argument is over
        // and the second person to walk up to it should not have to sit through it again.
        Where = gate.Destination is not null ? Page.Sockets
              : Chart.AnyOpen ? Page.Chart
              : Page.Exhausted;

        if (Where == Page.Chart) Chart.PointAtFirstOpen();
    }

    public void Close()
    {
        Gate = null;
        HoverWorld = null;
        HoverSlot = -1;
    }

    /// <summary>Called by the game loop once the destination has actually been settled, so the
    /// panel moves on from the chart without having to poll the gate.</summary>
    public void NoteDestinationSet()
    {
        if (Open && Where == Page.Chart) Where = Page.Sockets;
    }

    /// <summary>Puts the cursor over a pack slot, or -1 for none. Fed by the renderer's hit
    /// test.</summary>
    public void HoverPackSlot(int slot) => HoverSlot = slot;

    /// <summary>
    /// One frame of the panel: where the pointer is, and what it just clicked.
    ///
    /// <para>Reads the mouse directly, the same way <see cref="InventoryScreen"/> does — a
    /// screen in this game owns its own pointer. Everything it decides comes back as an
    /// <see cref="Action"/> for the game loop to carry out, because half of them (a ballot, a
    /// fragment leaving a pack) have to go over the wire and this class has never known the
    /// wire exists.</para>
    /// </summary>
    public Action Update(World.World world)
    {
        if (Gate is not { } gate) return Action.None;

        Vector2 cursor = Rendering.Renderer.ScreenToInternal(Raylib.GetMousePosition());
        bool click = Raylib.IsMouseButtonPressed(MouseButton.Left);

        // A gate that got claimed by somebody else while this panel was open on it. The player
        // is looking at a screen that is no longer theirs to use, so it shuts rather than
        // silently doing nothing to every click.
        if (gate.State == World.Arch.Phase.Dark) return Action.Close;

        // The destination landed while this was up — either because the vote resolved or
        // because somebody else at another panel settled it. Move on rather than making them
        // re-open it.
        if (Where == Page.Chart && gate.Destination is not null) Where = Page.Sockets;

        switch (Where)
        {
            case Page.Chart:
            {
                HoverWorld = Rendering.StarMapRenderer.WorldAt(cursor);
                if (!click || HoverWorld is not { } id || Chart.IsLocked(id)) break;
                Chart.PointAt(id);
                return Action.Cast;
            }

            case Page.Sockets:
            {
                HoverSlot = FragmentSlotAt(world, cursor);
                if (!click || HoverSlot < 0 || !gate.CanAccept) break;
                FeedSlot = HoverSlot;
                return Action.Feed;
            }

            default:
                if (click && Raylib.CheckCollisionPointRec(
                        cursor, Rendering.ArchPanelRenderer.EndRow()))
                    return Action.EndRun;
                break;
        }

        return Action.None;
    }

    /// <summary>
    /// Which pack slot the pointer is over in the tray, or -1.
    ///
    /// <para>The tray shows only the fragments out of a twenty-slot pack, packed left to right,
    /// so the n'th box on screen is not the n'th slot in the inventory — this walks the pack in
    /// the same order the renderer draws it and hands back the real slot index, which is what
    /// the feed has to name.</para>
    /// </summary>
    private static int FragmentSlotAt(World.World world, Vector2 cursor)
    {
        Inventory inv = world.Inventory;
        int shown = 0;
        for (int slot = 0; slot < Inventory.SlotCount; slot++)
        {
            if (inv.Slots[slot].IsEmpty || !World.World.IsFragment(inv.Slots[slot].Kind)) continue;
            if (Raylib.CheckCollisionPointRec(
                    cursor, Rendering.ArchPanelRenderer.TraySlotBox(shown)))
                return slot;
            shown++;
        }
        return -1;
    }

    /// <summary>What the heading says. One place, because the panel's three faces are three
    /// different jobs and the title is the only thing telling the player which one they are
    /// looking at.</summary>
    public string Title => Where switch
    {
        Page.Chart => "WHERE",
        Page.Sockets => "WHAT IT COSTS",
        _ => "NOWHERE LEFT",
    };
}
