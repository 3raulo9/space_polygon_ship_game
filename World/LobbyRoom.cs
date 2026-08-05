using System.Numerics;
using Raylib_cs;
using Unrendered.Core;

namespace Unrendered.World;

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
    public enum Focus { Walking, Pod, Console, Code, Naming, Chart }

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

    /// <summary>The holo chart of the five worlds, stood in the middle of the floor between the
    /// pod and the console rather than off at a wall — it is the one station the whole room has
    /// business at, and a vote should look like people gathering round a table.</summary>
    public static readonly Vector2 ChartStation = new(0f, 14f);

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

    /// <summary>The chart of the five worlds, and the room's vote about which one to drop
    /// onto. Every player has one; the host's is the one that counts.</summary>
    public readonly UI.StarMap Chart = new();

    /// <summary>Set on the frame the host opens a vote, for the loop to broadcast and clear.
    /// The tally itself is pushed continuously while a vote runs, so this only marks the
    /// <em>start</em> — the one edge a client cannot infer from a countdown it has not seen.</summary>
    public bool VoteDirty { get; private set; }

    /// <summary>The chassis highlighted in the pod, which Enter commits.</summary>
    public int PodIndex => Hangar.ClassIndex;

    /// <summary>
    /// The pod IS the hangar — the same screen single player picks a craft on, opened where
    /// the player is standing. Roster, turntable, the BUILD budget and the paint bay, all of
    /// it, driven by the same <see cref="UI.ClassSelectScreen"/> that has always driven it;
    /// only its LAUNCH is read as READY here, because in a room the host decides when anyone
    /// goes anywhere.
    ///
    /// <para>Before this the pod was a bare left/right carousel of chassis names. A player in
    /// a match could not spend a single build point or choose a single colour — the whole
    /// bench existed, and multiplayer simply did not open the door to it.</para>
    /// </summary>
    public UI.ClassSelectScreen Hangar { get; }

    /// <summary>The build the local player settled on at the pod: chassis, points and paint.
    /// This is what goes on the wire and what their craft is made from at launch.</summary>
    public Loadout MyBuild => Hangar.Loadout;

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

    /// <param name="build">The loop's own long-lived build, so a player who set one up in
    /// single player walks into the room still wearing it and the hangar's edits persist back
    /// out. Tests pass nothing and get a fresh one.</param>
    public LobbyRoom(Loadout? build = null)
    {
        Hangar = new UI.ClassSelectScreen(build ?? new Loadout());
        // Everyone starts in the antechamber, back from the two pillars, facing them.
        Position = new Vector2(0f, -16f);
        Heading = 0f; // faces +Z, toward the pillars/stations
    }

    /// <summary>Called once the session knows this machine's seat and name — seats the local
    /// avatar and drops the room out of the antechamber into the room proper.</summary>
    public void Seat(int seat, string name)
    {
        LocalSeat = seat;
        // The name the session was opened with, but never over one the player typed in the
        // antechamber while the dial was out — being seated used to quietly rename them back
        // to their Steam persona a second after they had chosen something else.
        if (!_named && name.Length > 0) MyName = name;
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

    public void ClearDirty()
    {
        PickDirty = NameDirty = RulesDirty = VoteDirty = false;
        Chart.ClearDirty();
    }

    /// <summary>Test hook: choose a chassis without going through the pod's key handling — the
    /// same effect as confirming a pick at the pod. Goes through the hangar's own loadout so
    /// the build that leaves on the wire names the chassis the test asked for.</summary>
    public void PickForTest(PlayerClass chassis)
    {
        Hangar.Loadout.Class = chassis;
        MyChassis = chassis;
        PickDirty = true;
    }

    /// <summary>Test hook: put the destination to the room without going through the console's
    /// key handling — the same effect as the host confirming the VOTE row.</summary>
    public void OpenVoteForTest() { Chart.OpenVote(); VoteDirty = true; }

    /// <summary>Test hook: settle the rules without going through the console's key handling —
    /// the same effect as the host nudging every row, including the flag that puts the change
    /// on the wire (which <see cref="AdoptRules"/> deliberately does not, since that is the
    /// path a change arrives <em>from</em> the wire by).</summary>
    public void SetRulesForTest(MatchSettings m) { Match = m.Clamped(); RulesDirty = true; }

    /// <summary>The host applies a live rules edit made at its own console. A client takes the
    /// same call off the wire — which is also how a settled destination reaches its chart, so
    /// the hologram in front of a client always shows the world they are actually going to.</summary>
    public void AdoptRules(MatchSettings m)
    {
        Match = m.Clamped();
        if (!IsHost) Chart.PointAt(Match.Destination);
    }

    /// <summary>Host-side: run the vote's clock. Returns the world the room settled on the
    /// frame the countdown expires, and null every other frame.</summary>
    public PlanetId? TickVote(float dt)
    {
        if (!IsHost || !Chart.Tick(dt)) return null;
        PlanetId won = Chart.Resolve(Match.Destination);
        SetDestination(won);
        return won;
    }

    /// <summary>Host-side: settle the destination, from a vote or from the host's own hand.</summary>
    public void SetDestination(PlanetId id)
    {
        var m = Clone(Match);
        m.Destination = id;
        Match = m.Clamped();
        Chart.PointAt(id);
        RulesDirty = true;
    }

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
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) && Where == Focus.Naming) { CommitName(); return Action.None; }

        Action act = Where switch
        {
            Focus.Naming => UpdateNaming(input),
            Focus.Code => UpdateCode(input),
            Focus.Pod => UpdatePod(input),
            Focus.Chart => UpdateChart(input),
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

        // Naming is reachable from anywhere with Enter. A literal key, like the Escape and
        // the arrows around it: the room's own chrome is not part of the control layout.
        if (Raylib.IsKeyPressed(KeyboardKey.Enter)) { BeginNaming(); return Action.None; }

        // Interact with whatever station is in reach.
        if (input.InteractPressed)
        {
            if (Stage == Phase.Antechamber)
            {
                if (Near(HostPillar)) { Choice = 0; return Action.StartHost; }
                if (Near(JoinPillar)) { Choice = 1; TypedCode = ""; Where = Focus.Code; }
            }
            else if (Stage == Phase.InRoom)
            {
                // The hangar remembers where it was left — walking back to the pod reopens it
                // on the craft you were last looking at, with the points you had already spent.
                if (Near(Pod)) Where = Focus.Pod;
                else if (Near(ChartStation)) { Where = Focus.Chart; }
                else if (IsHost && Near(ConsoleStation)) { Where = Focus.Console; }
            }
        }
        return Action.None;
    }

    public bool NearPod => Stage == Phase.InRoom && Near(Pod);
    public bool NearChart => Stage == Phase.InRoom && Near(ChartStation);
    public bool NearConsole => Stage == Phase.InRoom && IsHost && Near(ConsoleStation);
    public bool NearHost => Stage == Phase.Antechamber && Near(HostPillar);
    public bool NearJoin => Stage == Phase.Antechamber && Near(JoinPillar);

    private bool Near(Vector2 station) => Vector2.Distance(Position, station) <= Reach;

    // --- The chassis pod --------------------------------------------------------------

    /// <summary>
    /// Standing in the pod, which is the hangar. The screen owns every key while it is up —
    /// including Escape, which backs out of the paint bay before it backs out of the pod —
    /// so this hands the frame straight over and only acts on the two things it answers with.
    /// </summary>
    private Action UpdatePod(in InputFrame input)
    {
        switch (Hangar.Update())
        {
            case UI.ClassSelectScreen.Action.Launch:
                // READY, not launch. The build is settled and goes on the wire; the host is
                // the only one who says when the room leaves.
                MyChassis = Hangar.Loadout.Class;
                PickDirty = true;
                Where = Focus.Walking;   // step back out of the pod; you are now READY
                break;

            case UI.ClassSelectScreen.Action.Back:
                // Walked away without confirming. Whatever was being browsed stays on the
                // bench for next time, but it is not a pick until they press READY — a player
                // who wandered off mid-decision is not ready and the launch gate must know it.
                Where = Focus.Walking;
                break;
        }
        return Action.None;
    }

    /// <summary>Test/capture hook: step into the pod without walking there and pressing E.</summary>
    public void EnterPodForTest() => Where = Focus.Pod;

    /// <summary>Test hook: drive the pod's screen without a keyboard — commits whatever build
    /// the hangar currently holds, exactly as pressing READY does.</summary>
    public void ConfirmPodForTest()
    {
        MyChassis = Hangar.Loadout.Class;
        PickDirty = true;
        Where = Focus.Walking;
    }

    // --- The holo chart ---------------------------------------------------------------

    /// <summary>
    /// Standing at the table. Everyone can walk up and turn the chart — reading the conditions
    /// on a world you are about to be dropped onto is not a privilege — but what confirming
    /// does depends on who you are and whether a vote is running: the host settles it outright,
    /// anyone else casts a ballot, and outside a vote a client's Enter does nothing at all
    /// rather than pretending to.
    /// </summary>
    private Action UpdateChart(in InputFrame input)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { Where = Focus.Walking; return Action.None; }

        // Edges, not held, for the same reason the pod uses them: one world per press.
        if (input.Hit(Btn.TurnLeft)) Chart.Move(-1);
        if (input.Hit(Btn.TurnRight)) Chart.Move(+1);

        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || input.InteractPressed)
        {
            if (Chart.VoteOpen) Chart.CastLocal(LocalSeat);
            else if (IsHost) SetDestination(Chart.Cursor);
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
                case UI.LobbyScreen.Row.Mode:
                    m.Mode = m.Mode == GameMode.Sandbox ? GameMode.Descent : GameMode.Sandbox;
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
                case UI.LobbyScreen.Row.Enemies:
                    m.SpawnEnemies = !m.SpawnEnemies;
                    break;
            }
            Match = m.Clamped();
            RulesDirty = true;     // the loop mirrors the new rules to every client
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Enter))
        {
            // Put the destination to the room. Refused while one is already running, so a host
            // leaning on Enter cannot keep resetting the clock out from under people.
            if (ConsoleRow == UI.LobbyScreen.Row.Vote && !Chart.VoteOpen)
            {
                Chart.OpenVote();
                VoteDirty = true;
            }
            // LAUNCH only once everyone in the room has picked a craft — and never in the
            // middle of a vote, which would land the room somewhere it had not finished
            // arguing about.
            else if (ConsoleRow == UI.LobbyScreen.Row.Launch && AllReady && !Chart.VoteOpen)
                return Action.Launch;
        }

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
        if (name.Length > 0) { MyName = name; NameDirty = true; _named = true; }
        Where = Focus.Walking;
    }

    /// <summary>True once the player has typed a name of their own, so nothing later puts the
    /// machine's default back over it. See <see cref="Seat"/>.</summary>
    private bool _named;

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

    /// <summary>A seat has left — drop their figure, and their ballot with it.</summary>
    public void Remove(int seat)
    {
        Avatars.Remove(seat);
        Chart.Withdraw(seat);
    }

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
        Destination = m.Destination,
        Mode = m.Mode,
        SpawnEnemies = m.SpawnEnemies,
    };
}
