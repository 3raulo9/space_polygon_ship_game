using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.UI;

namespace Unrendered.Rendering;

/// <summary>
/// The gate's panel, drawn flat over the running world at the internal 320×240 — the same
/// surface treatment as the inventory, because it is the same kind of object: a screen you are
/// standing in front of while the planet carries on behind you.
///
/// <para><b>The world is never frozen under this.</b> A room can be attacked while somebody is
/// arguing with a chart, and on a cleared planet with the last wave swept that is mostly
/// theatre — but it is the honest behaviour, it matches the inventory, and it means the panel
/// can be left open by somebody who has walked away without stopping anyone else.</para>
/// </summary>
internal static class ArchPanelRenderer
{
    private const int W = Config.InternalWidth;
    private const int H = Config.InternalHeight;

    private static readonly Color Panel = new(6, 10, 14, 214);
    private static readonly Color Ink = new(200, 218, 224, 255);

    /// <summary>The board's five recesses, laid out as an arc across the screen so the picture
    /// on the panel is the shape of the thing outside it.</summary>
    private const int BoardY = 96;
    private const int BoardRise = 26;
    private const int SocketBox = 22;

    /// <summary>Where this player's fragments are offered, along the bottom.</summary>
    private const int TrayY = 168;
    private const int TraySlot = 26;

    public static void Draw(World.World world, ArchPanel panel, float elapsed,
        ItemIconRenderer icons)
    {
        if (panel.Gate is not { } gate) return;

        Raylib.DrawRectangle(0, 0, W, H, Panel);
        PixelFont.DrawCentered(panel.Title, W / 2, 5, 2, Ink);

        switch (panel.Where)
        {
            case ArchPanel.Page.Chart: DrawChart(world, panel, elapsed); break;
            case ArchPanel.Page.Sockets: DrawBoard(world, panel, gate, elapsed, icons); break;
            default: DrawExhausted(world, elapsed); break;
        }
    }

    // --- Where ---------------------------------------------------------------------------

    private static void DrawChart(World.World world, ArchPanel panel, float elapsed)
    {
        // The room's own chart, reused whole — the same row of octagons, the same tally pips,
        // the same clock. Reusing it rather than drawing a second chart is not only less code:
        // a player has already learned to read this screen at the lobby's holo table, and
        // teaching them a second one for the same decision would be gratuitous.
        StarMapRenderer.Draw(panel.Chart, GameMode.Descent, elapsed,
            voting: world.Networked && panel.Chart.VoteOpen);

        // Over the top of it, the one thing the lobby's version cannot say.
        PixelFont.DrawCentered("THE ARCH WILL ONLY OPEN ONCE", W / 2, 30, 1,
            Scale(Palette.Flag, 0.75f));

        PixelFont.DrawCentered(world.Networked
                ? "CLICK A WORLD TO VOTE    ESC BACK"
                : "CLICK A WORLD TO SET THE COURSE    ESC BACK",
            W / 2, H - 6, 1, Scale(Ink, 0.6f));
    }

    // --- What it costs -------------------------------------------------------------------

    private static void DrawBoard(World.World world, ArchPanel panel, World.Arch gate,
        float elapsed, ItemIconRenderer icons)
    {
        Planet dest = Planet.Get(gate.Destination ?? world.Match.Destination);
        PixelFont.DrawCentered($"COURSE SET   {dest.Name}", W / 2, 24, 1,
            Scale(Palette.BatteryCore, 0.9f));

        DrawSockets(gate, elapsed, icons);
        DrawTray(world, panel, gate, icons);

        // What is actually happening, in one line under the arc. The count is the number the
        // whole room is watching, so it is the largest thing on the page after the arc itself.
        string state = gate.State switch
        {
            World.Arch.Phase.Charging => "THE ARCH IS WINDING UP",
            World.Arch.Phase.Open => "IT IS OPEN   DRIVE INTO IT",
            _ when gate.Carrying != Fragment.None => "STAND CLEAR",
            _ => $"{gate.Filled} OF {World.Arch.SocketCount} SEATED",
        };
        PixelFont.DrawCentered(state, W / 2, BoardY + BoardRise + 22, 1,
            gate.State == World.Arch.Phase.Open ? Palette.BatteryCore : Ink);

        PixelFont.DrawCentered("CLICK A FRAGMENT TO FEED IT    ESC BACK", W / 2, H - 6, 1,
            Scale(Ink, 0.6f));
    }

    /// <summary>
    /// The five recesses, drawn as an arc rather than a row. This is the one piece of the panel
    /// that had to be a picture: the player is looking at a diagram of the thing standing over
    /// their head, and when the fifth one lands they should see the span close on the screen
    /// and hear it close outside at the same moment.
    /// </summary>
    private static void DrawSockets(World.Arch gate, float elapsed, ItemIconRenderer icons)
    {
        for (int i = 0; i < World.Arch.SocketCount; i++)
        {
            // Same parameterisation as the real span, so socket 2 is the crown here exactly as
            // it is out there.
            float along = World.Arch.SocketAlong[i];
            float t = MathF.PI * (1f - along);
            int x = W / 2 + (int)(MathF.Cos(t) * 78f);
            int y = BoardY + BoardRise - (int)(MathF.Sin(t) * BoardRise);

            var box = new Rectangle(x - SocketBox / 2, y - SocketBox / 2, SocketBox, SocketBox);
            Fragment seated = gate.SocketAt(i);

            Raylib.DrawRectangleRec(box, new Color(12, 22, 26, 230));
            Raylib.DrawRectangleLinesEx(box, 1f, seated == Fragment.None
                ? Scale(Palette.ArchSocket, 0.5f + 0.3f * MathF.Sin(elapsed * 2f + i))
                : Palette.BatteryCore);

            if (seated == Fragment.None)
            {
                // Which one is next, so a player carrying one knows where it is about to go
                // before they commit to it.
                if (gate.NextSocket() == i && gate.CanAccept)
                    PixelFont.DrawCentered("NEXT", x, y + SocketBox / 2 + 2, 1,
                        Scale(Palette.Flag, 0.8f));
                continue;
            }

            Icon(icons, seated == Fragment.Sun ? ItemKind.SunFragment : ItemKind.MoonFragment,
                new Rectangle(box.X + 2, box.Y + 2, SocketBox - 4, SocketBox - 4));
        }

        // The haul in flight, sliding along the same arc. Drawn from the gate's own CarryT so
        // the pip on the panel and the rock on the stonework are never out of step.
        if (gate.Carrying == Fragment.None || gate.CarryingTo < 0) return;

        float e = gate.CarryT * gate.CarryT * (3f - 2f * gate.CarryT);
        float a = 1f + (World.Arch.SocketAlong[gate.CarryingTo] - 1f) * e;
        float ta = MathF.PI * (1f - a);
        int cx = W / 2 + (int)(MathF.Cos(ta) * 78f);
        int cy = BoardY + BoardRise - (int)(MathF.Sin(ta) * BoardRise);

        Icon(icons, gate.Carrying == Fragment.Sun ? ItemKind.SunFragment : ItemKind.MoonFragment,
            new Rectangle(cx - 8, cy - 8, 16, 16));
    }

    /// <summary>One item icon into a box. The icons are rendered to textures upside down (they
    /// come off a render target), which is what the negative source height is for — the same
    /// trick the inventory's own draw uses.</summary>
    private static void Icon(ItemIconRenderer icons, ItemKind kind, Rectangle into)
    {
        var src = new Rectangle(0, 0, ItemIconRenderer.Size, -ItemIconRenderer.Size);
        Raylib.DrawTexturePro(icons.Texture(kind), src, into, Vector2.Zero, 0f, Color.White);
    }

    /// <summary>
    /// What this player is carrying, offered along the bottom. Only fragments — the rest of a
    /// pack is not something an arch has any opinion about, and showing twenty slots here would
    /// turn a ceremony into inventory management.
    /// </summary>
    private static void DrawTray(World.World world, ArchPanel panel, World.Arch gate,
        ItemIconRenderer icons)
    {
        Inventory inv = world.Inventory;
        int shown = 0;

        for (int slot = 0; slot < Inventory.SlotCount; slot++)
        {
            ref ItemStack s = ref inv.Slots[slot];
            if (s.IsEmpty || !World.World.IsFragment(s.Kind)) continue;

            Rectangle box = TraySlotBox(shown);
            bool hot = panel.HoverSlot == slot && gate.CanAccept;

            Raylib.DrawRectangleRec(box, new Color(12, 22, 26, 235));
            Raylib.DrawRectangleLinesEx(box, 1f,
                hot ? Palette.Flag : Scale(Palette.HudChrome, gate.CanAccept ? 0.7f : 0.3f));
            Icon(icons, s.Kind, new Rectangle(box.X + 3, box.Y + 3, TraySlot - 6, TraySlot - 6));
            shown++;
            if (shown >= TrayCapacity) break;
        }

        if (shown == 0)
        {
            // Which is the ordinary case for most players most of the time: five fragments and
            // twenty people. Said plainly, so somebody carrying nothing does not stand there
            // wondering what they are supposed to click.
            PixelFont.DrawCentered("YOU ARE CARRYING NONE", W / 2, TrayY + 8, 1,
                Scale(Ink, 0.45f));
            return;
        }

        if (!gate.CanAccept)
            PixelFont.DrawCentered(gate.Carrying != Fragment.None
                    ? "THE RAIL IS BUSY"
                    : "THE ARCH IS FULL",
                W / 2, TrayY - 10, 1, Scale(Ink, 0.5f));
    }

    /// <summary>How many fragments the tray can show at once. Five is every one on a planet, so
    /// it can never truncate.</summary>
    private const int TrayCapacity = World.Arch.SocketCount;

    /// <summary>Where tray entry <paramref name="i"/> sits. Shared with the hit test.</summary>
    public static Rectangle TraySlotBox(int i)
    {
        int total = TrayCapacity * (TraySlot + 4) - 4;
        int x0 = (W - total) / 2;
        return new Rectangle(x0 + i * (TraySlot + 4), TrayY, TraySlot, TraySlot);
    }

    // --- Nowhere left ---------------------------------------------------------------------

    private static void DrawExhausted(World.World world, float elapsed)
    {
        PixelFont.DrawCentered("IN PROGRESS", W / 2, 70, 3, Palette.Flag);
        PixelFont.DrawCentered("THE ARCH HAS NOWHERE LEFT TO SEND YOU", W / 2, 100, 1,
            Scale(Ink, 0.8f));
        PixelFont.DrawCentered("THIS IS AS FAR AS THE ROAD IS BUILT", W / 2, 112, 1,
            Scale(Ink, 0.5f));

        // Every world the session actually crossed, listed. After three hours this is the run's
        // receipt, and it is the only place it is ever printed.
        int y = 136;
        PixelFont.DrawCentered("WORLDS CROSSED", W / 2, y, 1, Scale(Palette.HudChrome, 0.6f));
        y += 12;
        foreach (var p in Planet.All)
        {
            if (!world.HasVisited(p.Id)) continue;
            PixelFont.DrawCentered(p.Name, W / 2, y, 1, Palette.BatteryCore);
            y += 10;
        }

        var row = EndRow();
        Raylib.DrawRectangleLinesEx(row, 1f,
            Scale(Palette.HudChrome, 0.6f + 0.4f * MathF.Sin(elapsed * 3f)));
        PixelFont.DrawCentered("END THE RUN", W / 2, (int)row.Y + 4, 1, Ink);
    }

    /// <summary>The exhausted page's one clickable row. Shared with the hit test.</summary>
    public static Rectangle EndRow() => new(W / 2 - 50, H - 26, 100, 14);

    private static Color Scale(Color c, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color((int)(c.R * t), (int)(c.G * t), (int)(c.B * t), c.A);
    }
}
