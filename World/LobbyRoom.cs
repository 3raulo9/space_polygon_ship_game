using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;

namespace VoidTanks.World;

/// <summary>
/// The multiplayer front door, as a place rather than a menu: a domed room hanging over a
/// planet, where the people who are about to play walk around as their chosen craft, set a
/// name, and — for the host — settle the rules and press LAUNCH.
///
/// Deliberately <em>not</em> the combat <see cref="World"/>. There is no simulation here: no
/// enemies, no torus wrap, no collision. Just avatars walking a bounded floor, driven by the
/// same <see cref="InputFrame"/> as the world so the loop can feed it the keyboard, a test can
/// feed it a script, and a peer over the wire can feed it their transform. Everything about
/// what a player has chosen (chassis, name, ready) is plain data so it survives a round trip
/// through the netcode and can be asserted headlessly.
///
/// The room is client-authoritative over each player's own avatar: with no combat there is
/// nothing to cheat, so a client owns exactly where its own figure stands and the host merely
/// relays that to everyone else. See <see cref="Net.Session"/> for the wire side.
/// </summary>
public sealed class LobbyRoom
{
    /// <summary>Where in the flow the room is. The antechamber is the same space before a
    /// session exists — two pillars, HOST and JOIN; the room proper is after one does.</summary>
    public enum Phase { Antechamber, Connecting, InRoom }

    /// <summary>What the local player is currently leaning on. While focused on a station the
    /// walker is frozen and the station owns Left/Right/Up/Down, so nothing a nav key does can
    /// also nudge the avatar across the floor.</summary>
    public enum Focus { Walking, Pod, Console, Code, Naming }

    /// <summary>What the room is asking the loop to do this frame. Everything else — a pick, a
    /// name, a rules change — the room records on itself and the loop reads through the dirty
    /// flags below, so the only things that need a return value are the four that open or close
    /// a socket.</summary>
    public enum Action { None, Back, StartHost, StartJoin, Launch }

    // --- Geometry ---------------------------------------------------------------------

    /// <summary>How far from the centre a walker may roam before the dome wall stops them.</summary>
    public const float Radius = 34f;

    /// <summary>The walker's eye off the floor — a person's height, well under a tank's, so the
    /// dome reads as something you are standing inside.</summary>
    public const float EyeHeight = 2.6f;

    /// <summary>How close counts as "at" a station.</summary>
    public const float Reach = 5.5f;

    // Antechamber pillars.
    public static readonly Vector2 HostPillar = new(-9f, 6f);
    public static readonly Vector2 JoinPillar = new(9f, 6f);

    // Room stations.
    public static readonly Vector2 Pod = new(-11f, 4f);
    public static readonly Vector2 ConsoleStation = new(11f, 4f);

    // --- One player in the room -------------------------------------------------------

    /// <summary>A single figure on the floor. Pure data: the renderer turns it into a craft.</summary>
    public sealed class Avatar
    {
        public int Seat;
        public string Name = "PLAYER";
        /// <summary>Null until they have stood in the pod and chosen — drawn as the neutral
        /// suited figure until then, and their own craft after.</summary>
        public PlayerClass? Chassis;
        public bool Ready;
        public Vector2 Position;
        public float Heading;
        public float Pitch;
        public float Height;
        public bool IsLocal;
    }

    /// <summary>Everyone in the room, keyed by seat. Includes the local player once seated.</summary>
    public readonly Dictionary<int, Avatar> Avatars = new();

    /// <summary>This machine's seat, or -1 before the host has seated it. The local walker's
    /// transform lives on <see cref="Position"/>/<see cref="Heading"/>/<see cref="Pitch"/> and
    /// is mirrored into <c>Avatars[LocalSeat]</c> each frame once that is known.</summary>
    public int LocalSeat = -1;

    /// <summary>Decided at a pillar in the antechamber — true once the player has walked to
    /// HOST and opened a socket, false once they have walked to JOIN.</summary>
    public bool IsHost { get; set; }

    // --- Local walker -----------------------------------------------------------------

    public Vector2 Position;
    public float Heading;
    public float Pitch;
    /// <summary>Height off the deck during a hop — the one movement the room allows.</summary>
    public float Height;
    private float _vVel;

    public Focus Where { get; private set; } = Focus.Walking;
    public Phase Stage { get; private set; } = Phase.Antechamber;

    /// <summary>The rules the host is settling. Shared out to clients whenever it changes.</summary>
    public MatchSettings Match { get; private set; } = new()
    {
        MaxPlayers = MatchSettings.DefaultMaxPlayers,
        Revives = MatchSettings.DefaultRevives,
    };

    /// <summary>The console row the host is on. Mirrors the old text lobby's rows.</summary>
    public UI.LobbyScreen.Row ConsoleRow { get; private set; } = UI.LobbyScreen.Row.Seats;

    /// <summary>The chassis highlighted in the pod, which Enter commits.</summary>
    public int PodIndex { get; private set; }

    /// <summary>The local player's confirmed chassis (null until they pick).</summary>
    public PlayerClass? MyChassis { get; private set; }

    /// <summary>The local player's name, and the buffer being typed while naming.</summary>
    public string MyName { get; private set; } = "PLAYER";
    public string NameBuffer { get; private set; } = "";

    public int Choice { get; private set; }             // antechamber: 0 HOST, 1 JOIN
    public string TypedCode { get; private set; } = "";  // antechamber: the code being entered
    public string? Trouble { get; set; }                 // a failure to show instead of a station

    // Set true on the frame the local player changes one of these, for the loop to push to the
    // wire and then clear. Keeps the room from having to know the session exists.
    public bool PickDirty { get; private set; }
    public bool NameDirty { get; private set; }
    public bool RulesDirty { get; private set; }

    // --- Feel -------------------------------------------------------------------------

    private const float WalkSpeed = 13f;
    private const float MouseSens = 0.0026f;
    private const float JumpVel = 9f;
    private const float Gravity = 26f;

    public LobbyRoom()
    {
        // Everyone starts in the antechamber, back from the two pillars, facing them.
        Position = new Vector2(0f, -16f);
        Heading = 0f; // faces +Z, toward the pillars/stations
    }

    /// <summary>Called once the session knows this machine's seat and name — seats the local
    /// avatar and drops the room out of the antechamber into the room proper.</summary>
    public void Seat(int seat, string name)
    {
        LocalSeat = seat;
        MyName = name;
        Stage = Phase.InRoom;
        Trouble = null;
        Sync();
    }

    /// <summary>The dial is out; waiting on the host to answer.</summary>
    public void Connecting() => Stage = Phase.Connecting;

    /// <summary>A dial failed or the host went away — back to the antechamber with a reason.</summary>
    public void Fail(string why)
    {
        Trouble = why;
        Stage = Phase.Antechamber;
        Where = Focus.Walking;
        TypedCode = "";
    }

    public void ClearDirty() { PickDirty = NameDirty = RulesDirty = false; }

    /// <summary>The host applies a live rules edit made at its own console.</summary>
    public void AdoptRules(MatchSettings m) => Match = m.Clamped();

    /// <summary>How many are seated, for the console readout and the launch gate.</summary>
    public int Seated => Avatars.Count == 0 ? 1 : Avatars.Count;

    /// <summary>Everyone who is going to play has picked a craft. The launch gate.</summary>
    public bool AllReady => Avatars.Count > 1 && Avatars.Values.All(a => a.Ready);

    /// <summary>
    /// One frame of the room. <paramref name="input"/> is the live keyboard (edges and mouse
    /// included); <paramref name="dt"/> is wall-clock seconds. Returns the one thing that needs
    /// the loop's hand, if any.
    /// </summary>
    public Action Update(in InputFrame input, float dt)
    {
        // Escape always steps back out of wherever the player is: a station, then the room,
        // then the whole screen.
        if (input.Hit(Btn.Enter) && Where == Focus.Naming) { CommitName(); return Action.None; }

        Action act = Where switch
        {
            Focus.Naming => UpdateNaming(input),
            Focus.Code => UpdateCode(input),
            Focus.Pod => UpdatePod(input),
            Focus.Console => UpdateConsole(input),
            _ => UpdateWalking(input, dt),
        };

        Sync();
        return act;
    }

    // --- Walking ----------------------------------------------------------------------

    private Action UpdateWalking(in InputFrame input, float dt)
    {
        // Escape leaves the whole screen (the loop tears the session down).
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) return Action.Back;

        // Look: the mouse alone turns the head, using the game's own sign (Heading += -dx) so
        // the room aims exactly like the world does. A and D no longer touch the head.
        Heading += -input.LookDelta.X * MouseSens;
        Pitch = Math.Clamp(Pitch - input.LookDelta.Y * MouseSens, -1.2f, 1.2f);

        // Move: forward/back along the heading, A/D strafe along the right axis — the same
        // right = (-fwd.Y, fwd.X) a craft strafes along, so D steps screen-right like the game.
        var fwd = new Vector2(MathF.Sin(Heading), MathF.Cos(Heading));
        var right = new Vector2(-fwd.Y, fwd.X);
        float drive = (input.Forward ? 1f : 0f) - (input.Back ? 1f : 0f);
        float strafe = (input.TurnRight ? 1f : 0f) - (input.TurnLeft ? 1f : 0f);
        Vector2 move = fwd * drive + right * strafe;
        if (move != Vector2.Zero)
        {
            Position += move * WalkSpeed * dt;
            float r = Position.Length();
            if (r > Radius) Position *= Radius / r;   // the dome wall
        }

        // A hop — the one movement the room allows. Fire/abilities stay locked.
        if (input.JumpPressed && Height <= 0f) _vVel = JumpVel;
        _vVel -= Gravity * dt;
        Height += _vVel * dt;
        if (Height <= 0f) { Height = 0f; _vVel = 0f; }

        // Naming is reachable from anywhere with Enter.
        if (input.Hit(Btn.Enter)) { BeginNaming(); return Action.None; }

        // Interact (E) with whatever station is in reach.
        if (input.Hit(Btn.E))
        {
            if (Stage == Phase.Antechamber)
            {
                if (Near(HostPillar)) { Choice = 0; return Action.StartHost; }
                if (Near(JoinPillar)) { Choice = 1; TypedCode = ""; Where = Focus.Code; }
            }
            else if (Stage == Phase.InRoom)
            {
                if (Near(Pod)) { Where = Focus.Pod; PodIndex = MyChassis.HasValue ? (int)MyChassis.Value : 0; }
                else if (IsHost && Near(ConsoleStation)) { Where = Focus.Console; }
            }
        }
        return Action.None;
    }

    public bool NearPod => Stage == Phase.InRoom && Near(Pod);
    public bool NearConsole => Stage == Phase.InRoom && IsHost && Near(ConsoleStation);
    public bool NearHost => Stage == Phase.Antechamber && Near(HostPillar);
    public bool NearJoin => Stage == Phase.Antechamber && Near(JoinPillar);

    private bool Near(Vector2 station) => Vector2.Distance(Position, station) <= Reach;

    // --- The chassis pod --------------------------------------------------------------

    private Action UpdatePod(in InputFrame input)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { Where = Focus.Walking; return Action.None; }

        int n = ClassCatalog.All.Count;
        // Edges, not held: a held key must step one craft, not flick through the whole roster.
        if (input.Hit(Btn.TurnLeft)) PodIndex = (PodIndex - 1 + n) % n;
        if (input.Hit(Btn.TurnRight)) PodIndex = (PodIndex + 1) % n;

        if (input.Hit(Btn.Enter) || input.Hit(Btn.E))
        {
            var chassis = ClassCatalog.All[PodIndex].Kind;
            MyChassis = chassis;
            PickDirty = true;      // the loop pushes this to the wire / the host's own avatar
            Where = Focus.Walking; // step back out; you are now READY
        }
        return Action.None;
    }

    // --- The host console -------------------------------------------------------------

    private Action UpdateConsole(in InputFrame input)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { Where = Focus.Walking; return Action.None; }

        int rows = Enum.GetValues<UI.LobbyScreen.Row>().Length;
        if (Raylib.IsKeyPressed(KeyboardKey.Down)) ConsoleRow = (UI.LobbyScreen.Row)(((int)ConsoleRow + 1) % rows);
        if (Raylib.IsKeyPressed(KeyboardKey.Up)) ConsoleRow = (UI.LobbyScreen.Row)(((int)ConsoleRow - 1 + rows) % rows);

        int nudge = (input.Hit(Btn.TurnRight) ? 1 : 0) - (input.Hit(Btn.TurnLeft) ? 1 : 0);
        if (nudge != 0)
        {
            var m = Clone(Match);
            switch (ConsoleRow)
            {
                case UI.LobbyScreen.Row.Map:
                    m.Map = m.Map == GameMap.Planet ? GameMap.Flat : GameMap.Planet;
                    break;
                case UI.LobbyScreen.Row.Seats:
                    m.MaxPlayers = Math.Clamp(m.MaxPlayers + nudge, Math.Max(2, Seated), MatchSettings.MaxSeats);
                    break;
                case UI.LobbyScreen.Row.FriendlyFire:
                    m.FriendlyFire = !m.FriendlyFire;
                    break;
                case UI.LobbyScreen.Row.Revives:
                    m.Revives = Math.Clamp(m.Revives + nudge, 0, MatchSettings.MaxRevives);
                    break;
            }
            Match = m.Clamped();
            RulesDirty = true;     // the loop mirrors the new rules to every client
        }

        // LAUNCH only once everyone in the room has picked a craft.
        if (input.Hit(Btn.Enter) && ConsoleRow == UI.LobbyScreen.Row.Launch && AllReady)
            return Action.Launch;

        return Action.None;
    }

    // --- Typing: the code, and the name -----------------------------------------------

    private Action UpdateCode(in InputFrame input)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { Where = Focus.Walking; TypedCode = ""; return Action.None; }

        for (int c = Raylib.GetCharPressed(); c != 0; c = Raylib.GetCharPressed())
        {
            if (TypedCode.Length >= 7) break;
            char ch = char.ToUpperInvariant((char)c);
            if (char.IsLetterOrDigit(ch)) TypedCode += ch;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && TypedCode.Length > 0)
            TypedCode = TypedCode[..^1];

        if (Raylib.IsKeyPressed(KeyboardKey.Enter) && TypedCode.Length == 7)
        {
            Where = Focus.Walking;
            return Action.StartJoin;
        }
        return Action.None;
    }

    private void BeginNaming()
    {
        Where = Focus.Naming;
        NameBuffer = MyName == "PLAYER" ? "" : MyName;
    }

    private Action UpdateNaming(in InputFrame input)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { Where = Focus.Walking; return Action.None; }

        for (int c = Raylib.GetCharPressed(); c != 0; c = Raylib.GetCharPressed())
        {
            if (NameBuffer.Length >= 16) break;
            char ch = (char)c;
            if (ch >= ' ' && ch < 127) NameBuffer += char.ToUpperInvariant(ch);
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && NameBuffer.Length > 0)
            NameBuffer = NameBuffer[..^1];

        return Action.None;
    }

    private void CommitName()
    {
        string name = NameBuffer.Trim();
        if (name.Length > 0) { MyName = name; NameDirty = true; }
        Where = Focus.Walking;
    }

    // --- Applying the wire's account --------------------------------------------------

    /// <summary>Host-side or client-side: install/refresh a remote avatar from a RoomState
    /// entry. The local seat is skipped — this machine owns its own transform.</summary>
    public void ApplyAvatar(int seat, string name, PlayerClass? chassis, bool ready,
        Vector2 pos, float heading, float pitch, float height)
    {
        if (seat == LocalSeat) { EnsureLocal(); return; }
        if (!Avatars.TryGetValue(seat, out var a))
            Avatars[seat] = a = new Avatar { Seat = seat };
        a.Name = name;
        a.Chassis = chassis;
        a.Ready = ready;
        a.Position = pos;
        a.Heading = heading;
        a.Pitch = pitch;
        a.Height = height;
    }

    /// <summary>Host-side: a client's transform update (RoomMove carries only where they stand;
    /// their chassis and ready come from a Pick, so those are left untouched here).</summary>
    public void ApplyTransform(int seat, Vector2 pos, float heading, float pitch, float height, string name)
    {
        if (seat == LocalSeat) return;
        if (!Avatars.TryGetValue(seat, out var a))
            Avatars[seat] = a = new Avatar { Seat = seat, Name = name };
        a.Position = pos;
        a.Heading = heading;
        a.Pitch = pitch;
        a.Height = height;
    }

    /// <summary>Host-side: a seat has chosen a craft at the pod.</summary>
    public void ApplyPick(int seat, PlayerClass chassis, bool ready)
    {
        if (seat == LocalSeat) { MyChassis = chassis; return; }
        if (!Avatars.TryGetValue(seat, out var a))
            Avatars[seat] = a = new Avatar { Seat = seat };
        a.Chassis = chassis;
        a.Ready = ready;
    }

    /// <summary>Host-side: a seat has set its nickname.</summary>
    public void ApplyName(int seat, string name)
    {
        if (seat == LocalSeat) return;
        if (!Avatars.TryGetValue(seat, out var a))
            Avatars[seat] = a = new Avatar { Seat = seat };
        a.Name = name;
    }

    /// <summary>A seat has left — drop their figure.</summary>
    public void Remove(int seat) => Avatars.Remove(seat);

    /// <summary>Mirror the local walker into its own avatar entry so the roster and the launch
    /// gate see it too.</summary>
    private void Sync() { if (LocalSeat >= 0) EnsureLocal(); }

    private void EnsureLocal()
    {
        if (!Avatars.TryGetValue(LocalSeat, out var a))
            Avatars[LocalSeat] = a = new Avatar { Seat = LocalSeat, IsLocal = true };
        a.IsLocal = true;
        a.Name = MyName;
        a.Chassis = MyChassis;
        a.Ready = MyChassis.HasValue;
        a.Position = Position;
        a.Heading = Heading;
        a.Pitch = Pitch;
        a.Height = Height;
    }

    private static MatchSettings Clone(MatchSettings m) => new()
    {
        MaxPlayers = m.MaxPlayers,
        FriendlyFire = m.FriendlyFire,
        Revives = m.Revives,
        Map = m.Map,
    };
}
