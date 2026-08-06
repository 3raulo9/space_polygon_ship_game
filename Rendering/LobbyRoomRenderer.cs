using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Entities;
using Unrendered.Net;
using Unrendered.World;

namespace Unrendered.Rendering;

/// <summary>
/// Draws the walkable multiplayer lobby: a glass dome hanging over a planet, with the people
/// about to play milling around inside it as their chosen craft. The 3D scene and the flat
/// panels over it both live here, so the <see cref="LobbyRoom"/> stays pure state and never
/// touches raylib.
///
/// Avatars are drawn with the same <see cref="EntityRenderer.DrawCraft"/> the match uses, so a
/// player picking a chassis at the pod literally turns into the thing they will fight as. Until
/// they pick they are the suited figure — the SOLDIER model standing in for a person on foot.
/// </summary>
internal sealed class LobbyRoomRenderer
{
    private const int W = Config.InternalWidth;
    private const int H = Config.InternalHeight;

    /// <summary>Per-seat craft used to draw each avatar, rebuilt only when a seat's chassis
    /// changes — building a PlayerTank a frame per figure would churn its rig every frame.</summary>
    private readonly Dictionary<int, (PlayerClass Cls, PlayerTank Tank)> _craft = new();

    // A fixed starfield, rolled once, so the sky beyond the dome holds still frame to frame.
    private readonly (int X, int Y, byte B)[] _stars;

    /// <summary>The starfield, drawn flat behind the 3D pass by the owning renderer.</summary>
    public IReadOnlyList<(int X, int Y, byte B)> Stars => _stars;

    public LobbyRoomRenderer()
    {
        var rng = new Random(0x5EED);
        _stars = new (int, int, byte)[130];
        for (int i = 0; i < _stars.Length; i++)
            _stars[i] = (rng.Next(W), rng.Next(H * 3 / 4), (byte)rng.Next(60, 210));
    }

    // --- The 3D pass ------------------------------------------------------------------

    public void Draw3D(LobbyRoom room, EntityRenderer entities, Vector3 eye, float elapsed)
    {
        DrawPlanet(elapsed);
        DrawFloor(elapsed);
        DrawDome(elapsed);
        DrawStations(room, elapsed);

        foreach (var a in room.Avatars.Values)
        {
            if (a.Seat == room.LocalSeat) continue;   // first-person: our own figure is the camera
            PlayerTank tank = CraftFor(a);
            tank.Position = a.Position;
            tank.Heading = a.Heading;
            tank.Height = a.Height;
            entities.DrawCraft(tank, a.Position, eye, elapsed);
        }
    }

    /// <summary>The world the dome hangs over — recognisably the PLANET the match drops into:
    /// a dark violet body wrapped in the game's own magenta sky-glow at the limb, its surface
    /// veined with the teal grid and lit by the cold cyan of the city, with the odd neon core
    /// burning among the lights. Drawn before the translucent floor so it shows up through the
    /// glass — an observation deck in orbit over the world.</summary>
    private static void DrawPlanet(float elapsed)
    {
        var centre = new Vector3(0f, -72f, 6f);
        const float R = 60f;

        // The body: near-void violet, so it reads as a mass, not a lit ball.
        Raylib.DrawSphere(centre, R, Mix(Palette.StructureDeep, Palette.Void, 0.45f));

        // The atmosphere: the game's magenta horizon glow, as a faint shell a touch larger than
        // the body — the single most recognisable thing about the PLANET's sky.
        Raylib.DrawSphereWires(centre, R + 1.6f, 7, 14, Scale(Palette.SkyHorizon, 0.35f));
        Raylib.DrawSphereWires(centre, R + 3.2f, 5, 10, Scale(Palette.SkyHorizon, 0.18f));

        // The grid, veining the surface in the same sick teal the floor of the world is drawn in.
        Raylib.DrawSphereWires(centre, R + 0.3f, 12, 20, Scale(Palette.GridFar, 0.3f));

        // The city: cyan lights scattered over the cap facing us, with the odd neon core
        // (magenta / red) burning among them, all twinkling out of phase.
        for (int i = 0; i < CityLights.Length; i++)
        {
            var (u, v) = CityLights[i];
            float lat = 0.5f + v * 0.75f;                 // the upper cap, toward the deck
            float lon = u * MathF.Tau + elapsed * 0.04f;
            var p = centre + new Vector3(
                MathF.Cos(lat) * MathF.Cos(lon) * (R + 0.2f),
                MathF.Sin(lat) * (R + 0.2f),
                MathF.Cos(lat) * MathF.Sin(lon) * (R + 0.2f));
            float tw = 0.45f + 0.55f * MathF.Abs(MathF.Sin(elapsed * 1.6f + i));
            Color c = i % 11 == 0 ? Palette.NeonRed
                    : i % 7 == 0 ? Palette.NeonMagenta
                    : Palette.StructureGlow;
            Raylib.DrawSphere(p, i % 7 == 0 ? 0.5f : 0.35f, Scale(c, tw));
        }
    }

    // Fixed light placements on the planet, rolled once.
    private static readonly (float U, float V)[] CityLights = BuildLights();
    private static (float, float)[] BuildLights()
    {
        var rng = new Random(0x0C17);
        var a = new (float, float)[70];
        for (int i = 0; i < a.Length; i++) a[i] = ((float)rng.NextDouble(), (float)rng.NextDouble());
        return a;
    }

    private static Color Mix(Color a, Color b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t), (byte)255);

    /// <summary>The platform: a translucent glass deck with concentric teal rings and radial
    /// ribs, so the planet glows up through it. Drawn after the planet, alpha-blended over it.</summary>
    private static void DrawFloor(float elapsed)
    {
        float r = LobbyRoom.Radius + 2.5f;
        // A faint tinted glass, dark enough to stand on, clear enough to see the planet through.
        Raylib.DrawCylinder(new Vector3(0f, -0.15f, 0f), r, r, 0.15f, 56,
            new Color(20, 40, 58, 70));
        Raylib.DrawCylinderWires(new Vector3(0f, -0.15f, 0f), r, r, 0.15f, 56, Scale(Palette.GridNear, 0.55f));
        for (int i = 1; i <= 5; i++)
            Raylib.DrawCircle3D(new Vector3(0f, 0.02f, 0f), r * i / 5f,
                new Vector3(1f, 0f, 0f), 90f, Scale(Palette.GridNear, 0.3f + 0.05f * i));
        // Radial ribs, so the deck reads as panelled glass rather than a flat pond.
        for (int m = 0; m < 12; m++)
        {
            float az = m / 12f * MathF.Tau;
            var edge = new Vector3(MathF.Cos(az) * r, 0.02f, MathF.Sin(az) * r);
            Raylib.DrawLine3D(new Vector3(0f, 0.02f, 0f), edge, Scale(Palette.GridNear, 0.22f));
        }
    }

    /// <summary>The glass dome: a cage of latitude rings and meridian ribs, faintly alive.</summary>
    private static void DrawDome(float elapsed)
    {
        const float R = LobbyRoom.Radius + 3f;
        const int Rings = 5, Ribs = 24;
        float pulse = 0.55f + 0.15f * MathF.Sin(elapsed * 0.6f);
        Color glass = Scale(Palette.GridNear, 0.35f * pulse);

        for (int i = 1; i <= Rings; i++)
        {
            float a = (i / (float)(Rings + 1)) * (MathF.PI / 2f);
            Raylib.DrawCircle3D(new Vector3(0f, MathF.Sin(a) * R, 0f),
                MathF.Cos(a) * R, new Vector3(1f, 0f, 0f), 90f, glass);
        }
        for (int m = 0; m < Ribs; m++)
        {
            float az = m / (float)Ribs * MathF.Tau;
            Vector3 prev = new(R, 0f, 0f);
            const int Steps = 10;
            for (int s = 0; s <= Steps; s++)
            {
                float a = s / (float)Steps * (MathF.PI / 2f);
                var p = new Vector3(MathF.Cos(a) * R * MathF.Cos(az),
                                    MathF.Sin(a) * R,
                                    MathF.Cos(a) * R * MathF.Sin(az));
                if (s > 0) Raylib.DrawLine3D(prev, p, Scale(glass, 0.8f));
                prev = p;
            }
        }
    }

    private static void DrawStations(LobbyRoom room, float elapsed)
    {
        if (room.Stage == LobbyRoom.Phase.Antechamber)
        {
            DrawPillar(LobbyRoom.HostPillar, Palette.GridNear, elapsed, room.NearHost);
            DrawPillar(LobbyRoom.JoinPillar, Palette.Flag, elapsed, room.NearJoin);
        }
        else
        {
            DrawPod(LobbyRoom.Pod, elapsed, room.NearPod);
            DrawChart(room, elapsed);
            if (room.IsHost) DrawConsole(LobbyRoom.ConsoleStation, elapsed, room.NearConsole);
        }
    }

    /// <summary>
    /// The holo chart: a low plinth in the middle of the floor with the five worlds hanging
    /// in a ring above it, turning. Each is painted in its own horizon colour, so the room can
    /// see at a glance that they are five different skies — and the one the match is currently
    /// bound for is ringed and lifted clear of the others.
    ///
    /// A world with a vote behind it wears one bright pip per ballot, stacked over it. From
    /// across the deck that is the only part of the vote you can read, and it is enough:
    /// somebody watching from the pod can see which way the room is leaning without walking over.
    /// </summary>
    private static void DrawChart(LobbyRoom room, float elapsed)
    {
        var at = LobbyRoom.ChartStation;
        bool near = room.NearChart;

        // The plinth. Waist-high and no wider than a table you could put your hands on: the
        // point of the station is that people gather round it, and a two-metre drum in the
        // middle of the deck reads as an obstacle instead.
        Raylib.DrawCylinder(new Vector3(at.X, 0f, at.Y), 1.5f, 1.8f, 1.0f, 8,
            Scale(Palette.StructureShell, 0.8f));
        Raylib.DrawCylinderWires(new Vector3(at.X, 0f, at.Y), 1.5f, 1.8f, 1.0f, 8,
            near ? Palette.StructureGlow : Scale(Palette.StructureGlow, 0.5f));

        // The projection cone: a faint wire funnel from the plinth up to the ring, so the
        // worlds read as being *thrown* up there rather than simply floating.
        Raylib.DrawCylinderWires(new Vector3(at.X, 1.0f, at.Y), 1.3f, 2.6f, 1.3f, 8,
            Scale(Palette.StructureGlow, 0.16f));

        // Deliberately small — a chart on a table, at about the size of a thing you would lean
        // over. An earlier pass had the worlds a metre across at head height, which read as
        // five moons in the room rather than as a map of anywhere.
        const float Ring = 2.1f;
        const float Base = 2.3f;

        int n = Planet.All.Count;
        float ring = elapsed * 0.35f;
        for (int i = 0; i < n; i++)
        {
            Planet p = Planet.All[i];
            bool chosen = room.Match.Destination == p.Id;
            bool cursor = near && room.Where == LobbyRoom.Focus.Chart && room.Chart.Cursor == p.Id;

            float a = ring + i * MathF.Tau / n;
            float lift = Base + (chosen ? 0.45f : 0f) + 0.08f * MathF.Sin(elapsed * 1.7f + i);
            var pos = new Vector3(at.X + MathF.Cos(a) * Ring, lift, at.Y + MathF.Sin(a) * Ring);

            float size = chosen ? 0.42f : 0.3f;
            Raylib.DrawSphere(pos, size, Scale(p.SkyHorizon, chosen ? 1f : 0.7f));
            if (chosen || cursor)
                Raylib.DrawSphereWires(pos, size + 0.2f, 5, 8,
                    cursor ? Palette.Flag : Scale(Palette.Flag, 0.5f));

            // SOLUNE's moon, so the world with the cycle is identifiable from the floor.
            if (p.HasCycle)
            {
                float m = elapsed * 1.1f + i;
                Raylib.DrawSphere(pos + new Vector3(MathF.Cos(m) * 0.7f, 0.12f, MathF.Sin(m) * 0.7f),
                    0.1f, Palette.HudChrome);
            }

            // The ballot, stacked above the world.
            int votes = room.Chart.Tally(p.Id);
            for (int v = 0; v < votes && v < 8; v++)
                Raylib.DrawCube(pos + new Vector3(0f, size + 0.28f + v * 0.2f, 0f),
                    0.14f, 0.11f, 0.14f, Palette.GridNear);
        }
    }

    private static void DrawPillar(Vector2 at, Color c, float elapsed, bool near)
    {
        float lift = near ? 0.4f * MathF.Abs(MathF.Sin(elapsed * 4f)) : 0f;
        Raylib.DrawCube(new Vector3(at.X, 2.5f, at.Y), 1.2f, 5f, 1.2f, Scale(Palette.StructureShell, 0.9f));
        Raylib.DrawCubeWires(new Vector3(at.X, 2.5f, at.Y), 1.2f, 5f, 1.2f, near ? c : Scale(c, 0.6f));
        Raylib.DrawCube(new Vector3(at.X, 5.2f + lift, at.Y), 1.6f, 1.6f, 1.6f, Scale(c, near ? 1f : 0.6f));
    }

    private static void DrawPod(Vector2 at, float elapsed, bool near)
    {
        Raylib.DrawCylinder(new Vector3(at.X, 0f, at.Y), 2.2f, 2.6f, 1.2f, 20, Scale(Palette.CrabChassis, 0.7f));
        Raylib.DrawCylinderWires(new Vector3(at.X, 0f, at.Y), 2.2f, 2.6f, 1.2f, 20,
            near ? Palette.NeonMagenta : Scale(Palette.NeonMagenta, 0.5f));
        float spin = elapsed * 1.4f;
        var core = new Vector3(at.X, 2.6f + 0.2f * MathF.Sin(elapsed * 2f), at.Y);
        Raylib.DrawCubeWires(core, 1.4f, 1.4f, 1.4f, Palette.NeonMagenta);
        Raylib.DrawSphere(core, 0.3f, Palette.NeonMagenta);
    }

    private static void DrawConsole(Vector2 at, float elapsed, bool near)
    {
        Raylib.DrawCube(new Vector3(at.X, 1.1f, at.Y), 3.2f, 2.2f, 1.4f, Scale(Palette.StructureShell, 0.9f));
        Raylib.DrawCubeWires(new Vector3(at.X, 1.1f, at.Y), 3.2f, 2.2f, 1.4f,
            near ? Palette.GridNear : Scale(Palette.GridNear, 0.55f));
        // A slanted screen face on top, glowing.
        float g = 0.5f + 0.3f * MathF.Sin(elapsed * 3f);
        Raylib.DrawCube(new Vector3(at.X, 2.5f, at.Y), 2.8f, 0.15f, 1.1f, Scale(Palette.StructureGlow, g));
    }

    private PlayerTank CraftFor(LobbyRoom.Avatar a)
    {
        PlayerClass cls = a.Chassis ?? PlayerClass.Soldier;   // suited figure until they pick
        if (!_craft.TryGetValue(a.Seat, out var e) || e.Cls != cls)
        {
            var tank = new PlayerTank(a.Position, a.Heading, new Loadout { Class = cls });
            _craft[a.Seat] = e = (cls, tank);
        }
        return e.Tank;
    }

    // --- The flat panels over the scene -----------------------------------------------

    public void Draw2D(LobbyRoom room, Camera3D camera, float elapsed)
    {
        // Names, floating over every figure but our own.
        foreach (var a in room.Avatars.Values)
        {
            if (a.Seat == room.LocalSeat) continue;
            var head = new Vector3(a.Position.X, 5.2f, a.Position.Y);
            if (!InFront(camera, head)) continue;
            Vector2 s = Raylib.GetWorldToScreenEx(head, camera, W, H);
            string tag = a.Name + (a.Ready ? "" : " ...");
            Color c = a.Ready ? Palette.HudChrome : Scale(Palette.HudChrome, 0.6f);
            PixelFont.DrawCentered(tag, (int)s.X, (int)s.Y, 1, c);
        }

        switch (room.Stage)
        {
            case LobbyRoom.Phase.Antechamber: Draw2DAntechamber(room, elapsed); break;
            case LobbyRoom.Phase.Connecting: DrawBanner("CONNECTING", elapsed); break;
            default: Draw2DRoom(room, elapsed); break;
        }

        if (room.Where == LobbyRoom.Focus.Naming) DrawNameBox(room, elapsed);

        if (room.Trouble is { } bad)
            PixelFont.DrawCentered(bad, W / 2, H - 20, 1, Palette.Warning);
    }

    private static void Draw2DAntechamber(LobbyRoom room, float elapsed)
    {
        PixelFont.DrawCentered("MULTIPLAYER", W / 2, 10, 2, Palette.HudChrome);
        PixelFont.DrawCentered(SteamNet.Available ? "STEAM CONNECTED" : SteamNet.Trouble ?? "STEAM OFFLINE",
            W / 2, 26, 1, SteamNet.Available ? Palette.GridNear : Palette.Warning);

        if (room.Where == LobbyRoom.Focus.Code) { DrawCodeBox(room, elapsed); return; }

        if (room.NearHost) Prompt("E — HOST A MATCH");
        else if (room.NearJoin) Prompt("E — JOIN A MATCH");
        else Prompt("WALK TO A PILLAR — HOST OR JOIN");
    }

    private static void Draw2DRoom(LobbyRoom room, float elapsed)
    {
        // Your identity + the code to read out, top-left.
        PixelFont.Draw(room.MyName, 5, 5, 1, Palette.Flag);
        if (room.IsHost)
            PixelFont.Draw("CODE " + Space(SteamNet.LocalCode), 5, 15, 1, Palette.GridNear);

        // The roster, top-right.
        int y = 5;
        PixelFont.Draw($"{room.Seated}/{room.Match.MaxPlayers}", W - 40, y, 1, Palette.HudChrome);
        y += 10;
        foreach (var a in room.Avatars.Values.OrderBy(a => a.Seat))
        {
            string line = a.Name + (a.Ready ? "" : " ?");
            PixelFont.Draw(line, W - 5 - PixelFont.Measure(line, 1), y, 1,
                a.Ready ? Palette.GridNear : Scale(Palette.HudChrome, 0.6f));
            y += 9;
        }

        // The pod is not drawn here at all: standing in it hands the whole frame to the
        // hangar (see Renderer.DrawLobbyRoom), which is the same screen single player uses
        // and needs the room's 3D pass out of its way to do it.
        if (room.Where == LobbyRoom.Focus.Pod) return;
        if (room.Where == LobbyRoom.Focus.Chart)
        {
            // The full chart, the same picture a solo player gets — with the tally and the
            // clock switched on, because here there are other people to disagree with.
            StarMapRenderer.Draw(room.Chart, room.Match.Mode, elapsed, voting: true);
            if (!room.IsHost && !room.Chart.VoteOpen)
                PixelFont.DrawCentered("YOU CAN LOOK, BUT NOT CHOOSE", W / 2, H - 26, 1,
                    Scale(Palette.HudChrome, 0.5f));
            return;
        }
        if (room.Where == LobbyRoom.Focus.Console) { DrawConsolePanel(room, elapsed); return; }

        // A running vote is shouted from anywhere in the room, not only at the table — nobody
        // should miss twenty seconds because they were standing at the pod.
        if (room.Chart.VoteOpen)
        {
            bool urgent = room.Chart.SecondsLeft <= 5f;
            float beat = 0.55f + 0.45f * MathF.Abs(MathF.Sin(elapsed * (urgent ? 8f : 3f)));
            PixelFont.DrawCentered($"DESTINATION VOTE  {room.Chart.SecondsLeft:0}", W / 2, 34, 1,
                Scale(urgent ? Palette.Warning : Palette.Flag, beat));
        }

        if (room.NearPod) Prompt("E — HANGAR: CHOOSE AND BUILD YOUR CRAFT");
        else if (room.NearChart) Prompt(room.Chart.VoteOpen ? "E — CAST YOUR VOTE" : "E — STAR CHART");
        else if (room.NearConsole) Prompt("E — HOST CONSOLE");
        else Prompt("ENTER — RENAME     ESC — LEAVE");
    }

    private static void DrawConsolePanel(LobbyRoom room, float elapsed)
    {
        Panel(64, 54, W - 128, 178);
        PixelFont.DrawCentered("HOST CONSOLE", W / 2, 60, 1, Palette.HudChrome);

        int y = 76;
        Row(room, UI.LobbyScreen.Row.Mode, "MODE",
            room.Match.Mode == GameMode.Descent ? "DESCENT" : "SANDBOX", y);
        // Not a row you nudge: it is settled at the chart, and shown here so a host does not
        // have to walk across the deck to remember where they are taking everyone.
        PixelFont.Draw("DESTINATION", 84, y + 14, 1, Scale(Palette.HudChrome, 0.45f));
        PixelFont.Draw(room.Match.World.Name, 200, y + 14, 1, Scale(Palette.Flag, 0.8f));
        Row(room, UI.LobbyScreen.Row.Vote, "VOTE",
            room.Chart.VoteOpen ? $"{room.Chart.SecondsLeft:0}S LEFT" : "PUT IT TO THE ROOM", y + 28);
        Row(room, UI.LobbyScreen.Row.Seats, "SEATS", room.Match.MaxPlayers.ToString(), y + 42);
        Row(room, UI.LobbyScreen.Row.FriendlyFire, "FRIENDLY FIRE", room.Match.FriendlyFire ? "ON" : "OFF", y + 56);
        Row(room, UI.LobbyScreen.Row.Revives, "REVIVES", room.Match.Revives.ToString(), y + 70);
        Row(room, UI.LobbyScreen.Row.Enemies, "ENEMIES", room.Match.SpawnEnemies ? "ON" : "OFF", y + 84);

        bool go = room.ConsoleRow == UI.LobbyScreen.Row.Launch;
        // A vote in progress holds the launch: the room has not finished saying where it wants
        // to go, and dropping them mid-argument would make the whole exercise a decoration.
        bool ready = room.AllReady && !room.Chart.VoteOpen;
        float beat = ready ? 0.7f + 0.3f * MathF.Abs(MathF.Sin(elapsed * 3f)) : 0.4f;
        string gate = room.Chart.VoteOpen ? "VOTE IN PROGRESS"
                    : room.AllReady ? "LAUNCH" : "WAITING FOR ALL READY";
        PixelFont.DrawCentered(gate, W / 2, y + 104,
            go && ready ? 2 : 1, go ? Scale(Palette.Flag, beat) : Scale(Palette.HudChrome, 0.6f));

        PixelFont.DrawCentered("UP/DN ROW   A/D CHANGE   ENTER   ESC BACK", W / 2, y + 122, 1,
            Scale(Palette.HudChrome, 0.6f));
    }

    private static void Row(LobbyRoom room, UI.LobbyScreen.Row row, string label, string val, int y)
    {
        bool on = room.ConsoleRow == row;
        Color c = on ? Palette.Flag : Palette.HudChrome;
        PixelFont.Draw(label, 84, y, 1, on ? c : Scale(c, 0.6f));
        PixelFont.Draw(on ? $"< {val} >" : val, 200, y, 1, c);
    }

    private static void DrawCodeBox(LobbyRoom room, float elapsed)
    {
        Panel(80, 150, W - 160, 60);
        PixelFont.DrawCentered("ENTER THE HOST'S CODE", W / 2, 156, 1, Palette.HudChrome);
        string shown = room.TypedCode.PadRight(7, '.');
        bool caret = room.TypedCode.Length < 7 && MathF.Sin(elapsed * 6f) > 0f;
        PixelFont.DrawCentered(Space(shown), W / 2, 172, 2, caret ? Palette.Flag : Palette.HudChrome);
        PixelFont.DrawCentered(room.TypedCode.Length == 7 ? "ENTER TO CONNECT" : "TYPE SEVEN",
            W / 2, 194, 1, Scale(Palette.HudChrome, 0.6f));
    }

    private static void DrawNameBox(LobbyRoom room, float elapsed)
    {
        Panel(80, 150, W - 160, 54);
        PixelFont.DrawCentered("YOUR NAME", W / 2, 156, 1, Palette.HudChrome);
        string shown = room.NameBuffer.Length == 0 ? "_" : room.NameBuffer;
        bool caret = MathF.Sin(elapsed * 6f) > 0f;
        PixelFont.DrawCentered(shown + (caret ? "_" : " "), W / 2, 172, 2, Palette.Flag);
        PixelFont.DrawCentered("ENTER OK   ESC CANCEL", W / 2, 192, 1, Scale(Palette.HudChrome, 0.6f));
    }

    // --- Small helpers ----------------------------------------------------------------

    private static void Prompt(string text)
        => PixelFont.DrawCentered(text, W / 2, H - 20, 1, Scale(Palette.HudChrome, 0.8f));

    private static void DrawBanner(string text, float elapsed)
    {
        int dots = (int)(elapsed * 2f) % 4;
        PixelFont.DrawCentered(text + new string('.', dots), W / 2, H / 2, 2, Palette.Flag);
    }

    private static void Panel(int x, int y, int w, int h)
    {
        Raylib.DrawRectangle(x, y, w, h, new Color(5, 7, 10, 220));
        Raylib.DrawRectangleLines(x, y, w, h, Scale(Palette.HudChrome, 0.5f));
    }

    private static bool InFront(Camera3D cam, Vector3 p)
        => Vector3.Dot(Vector3.Normalize(cam.Target - cam.Position), p - cam.Position) > 0.2f;

    private static string Space(string s) => string.Join(' ', s.ToCharArray());

    private static Color Scale(Color c, float k)
        => new((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k), c.A);
}
