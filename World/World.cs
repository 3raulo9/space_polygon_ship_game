using System.Numerics;
using Raylib_cs;
using VoidTanks.Acoustics;
using VoidTanks.Core;
using VoidTanks.Entities;
using VoidTanks.Input;

namespace VoidTanks.World;

/// <summary>
/// Holds the live entities and runs the combat simulation: player + enemies +
/// a pooled projectile list, with collision and damage. Kept separate from the
/// render/loop plumbing so the rules read in one place. Milestone 2 seeds a
/// single enemy at range so it materializes out of the fog and hunts.
/// </summary>
public sealed class World : IAnchorField
{
    /// <summary>
    /// Everyone in the seat, host first. In a solo run this is one craft and every read of
    /// <see cref="Player"/> below finds it; in a match it is up to
    /// <see cref="MatchSettings.MaxSeats"/> of them, and the host's copy of this list is
    /// the only one that counts — clients are told what is in it, they do not decide.
    ///
    /// The order is the peer order the session assigned, so index 0 is the host on every
    /// machine and a snapshot can address a craft by one byte.
    /// </summary>
    public readonly List<PlayerTank> Players = new();

    /// <summary>
    /// Which seat this machine is driving. Zero on the host, and on every solo run, which
    /// is why <see cref="Player"/> can stay the plain unqualified read it has always been.
    /// </summary>
    public int LocalIndex
    {
        get => _localIndex;
        // The camera follows the seat by default. Kept in step here so a client that is told
        // its seat has a sane view from the very first frame, before any step has run to
        // settle it (see PickSpectatorSeat).
        internal set { _localIndex = value; ViewSeat = value; }
    }
    private int _localIndex;

    /// <summary>
    /// The craft this machine is driving — the one whose HUD is drawn, whose camera the
    /// scene is rendered from, and whose keys the sampler is reading.
    ///
    /// This used to be the only player there was, and the several hundred places that read
    /// it still say exactly what they said before. That is deliberate: the ones that meant
    /// "the local craft" (the camera, the HUD, the crosshair) are already correct and must
    /// never change, and the ones that meant "any craft" (damage sweeps, enemy targeting)
    /// are converted deliberately, one system at a time, rather than in one rename nobody
    /// could review. Until a system has been converted it simply behaves as it always has.
    /// </summary>
    public PlayerTank Player => Players[LocalIndex];

    /// <summary>The rules of this match — seats, friendly fire, revives. The host's copy;
    /// clients are handed it once on joining and never edit it.</summary>
    public MatchSettings Match { get; internal set; } = MatchSettings.SinglePlayer;

    /// <summary>The hangar build this stage was spun up for — the chassis, its point
    /// spend and its paint job. Read by the renderer to colour the craft's own parts,
    /// and by nothing in the sim: the stats were baked into the player at construction.</summary>
    public readonly Loadout Loadout;

    public readonly List<EnemyTank> Enemies = new();

    /// <summary>
    /// The soldier squads working the field. Each owns its four members and does nothing
    /// but hand them a bearing and a turn; the flying is theirs. Squads that have been
    /// wiped out are swept at the end of the step, exactly as dead hunters are.
    /// </summary>
    public readonly List<SoldierSquad> Squads = new();

    /// <summary>
    /// Every live soldier on the field, flat. The same objects the squads hold — this is
    /// the list everything that does not care about squads (the renderer, the radar, every
    /// damage sweep in the game) walks, so none of them has to iterate a nesting they have
    /// no use for.
    /// </summary>
    public readonly List<EnemySoldier> Soldiers = new();

    public readonly DebrisSystem Debris = new();

    /// <summary>Floating salvage scattered on the grid — batteries and stray rounds.</summary>
    public readonly List<Pickup> Pickups = new();

    /// <summary>
    /// One pack per seat — filled by driving over salvage, spent from the inventory panel (E).
    /// Salvage no longer charges the craft on contact; it is stowed and applied by hand.
    ///
    /// Personal, not shared: the craft that drove over a cell is the craft that keeps it, which
    /// is the whole of progression in a match. Grown lazily so a seat that appears mid-snapshot
    /// (a late joiner the host names before this machine has built them) always has one.
    /// </summary>
    private readonly List<Inventory> _inventories = new();

    /// <summary>The pack belonging to one seat. Never null: an unheard-of seat is simply given
    /// an empty pack, because a snapshot naming a seat is the normal way one first appears.</summary>
    public Inventory InventoryOf(int seat)
    {
        if (seat < 0) seat = 0;
        while (_inventories.Count <= seat) _inventories.Add(new Inventory());
        return _inventories[seat];
    }

    /// <summary>This machine's own pack — what the panel drags against and the HUD's equip row
    /// reads. Every one of the several dozen existing readers meant "mine", so they are all
    /// correct unchanged; only the sim's collection and spending had to learn about seats.</summary>
    public Inventory Inventory => InventoryOf(LocalIndex);

    /// <summary>
    /// Client-side outbox: the inventory actions this player has taken that the host has not
    /// been told about yet. A client scribbles on its own mirror for an instant response and
    /// files the intent here; the session drains it, and the host's reliable echo of the real
    /// pack is what finally decides. Empty on a host, which owns the packs outright.
    /// </summary>
    public readonly List<InvIntent> InvIntents = new();

    /// <summary>Files an inventory action for the host. A no-op on the machine that owns the
    /// simulation, where the panel has already done the real thing.</summary>
    public void FileInvIntent(in InvIntent intent)
    {
        if (Authoritative) return;
        // A player hammering a panel while the link is stalled must not build a queue that
        // replays minutes later; past a sane depth the oldest intent is simply lost, and the
        // host's next echo puts the pack back to the truth.
        if (InvIntents.Count >= MaxPendingIntents) InvIntents.RemoveAt(0);
        InvIntents.Add(intent);
    }

    private const int MaxPendingIntents = 32;

    /// <summary>
    /// Host-side: replays one client's inventory action against the authoritative pack, using
    /// the same placement rules the client's mirror ran (<see cref="Inventory.Move"/>), so the
    /// two land in the same place and the echo confirms rather than corrects. An action that
    /// no longer makes sense — the slot emptied, the recipe slipped — is dropped, and the echo
    /// is what tells the client so.
    /// </summary>
    public void ApplyInvIntent(int seat, in InvIntent it)
    {
        if ((uint)seat >= (uint)Players.Count) return;
        Inventory inv = InventoryOf(seat);
        switch (it.Op)
        {
            case InvOp.Move:
                inv.Move(it.From, it.FromIndex, it.To, it.ToIndex, it.Count);
                break;
            case InvOp.CraftInto:
                inv.CraftInto(it.To, it.ToIndex);
                break;
            case InvOp.Charge:
                ChargeFromSlot(Players[seat], inv, it.FromIndex);
                break;
        }
    }

    /// <summary>Live CRAB CORE detonations — each a brief ring of lances raking the
    /// grid. Rare and short-lived, so a plain list rather than a pool.</summary>
    public readonly List<CrabCoreBlast> Blasts = new();

    /// <summary>The lone Crab-Core boss seeded into the stage, or null. Runs its
    /// own Stalker Protocol against the player independent of the tank combat.</summary>
    public CrabCore? Boss { get; private set; }

    /// <summary>
    /// The boss's execution cinematic while it has hold of the player, or null the
    /// rest of the time. While one exists it owns the player's transform outright and
    /// the renderer reads its shake, roll and glow — see <see cref="CrabSeizure"/>.
    /// </summary>
    public CrabSeizure? Seizure { get; private set; }

    /// <summary>The lone Maw-Core hanging over the stage, or null. Hovers at the top
    /// of the player's jump and runs its own hunt — see <see cref="MawCore"/>.</summary>
    public MawCore? Maw { get; private set; }

    /// <summary>
    /// The Maw-Core's digestion, while it has the player in its throat. Unlike the
    /// crab's seizure this one has no timer: it runs until the player shoots their way
    /// out of it or is eaten.
    /// </summary>
    public MawDigestion? Digestion { get; private set; }

    /// <summary>
    /// Whichever set piece currently owns the camera, or null. Only ever one at a
    /// time: both monsters refuse to start one on a player who is already
    /// <see cref="PlayerTank.Captured"/>, so the two can never overlap.
    ///
    /// Only when it is happening to the craft the camera is <em>riding</em>. A monster can now
    /// corner any seat, and the shake, roll and stain of being in a claw belong to the person
    /// in it — throwing the whole room's view about because somebody across the map got
    /// grabbed would be nonsense. Measured against <see cref="Eye"/> rather than
    /// <see cref="Player"/> so a spectator watching a team-mate get taken rides it with them.
    /// </summary>
    public ICinematicView? Cinematic
        => Seizure is { } s && ReferenceEquals(s.Victim, Eye) ? s
         : Digestion is { } d && ReferenceEquals(d.Victim, Eye) ? d
         : null;

    /// <summary>True when <paramref name="who"/> is the craft in the crab's claw right now —
    /// the question every trigger has to ask before refusing to fire. It used to ask whether
    /// <em>anybody</em> was held, which once a remote seat could be seized would have frozen
    /// all twenty players' weapons because one of them was grabbed.</summary>
    private bool HeldInClaw(PlayerTank who)
        => Seizure is { Held: true } s && ReferenceEquals(s.Victim, who);

    /// <summary>The digestion that has <paramref name="who"/> in its throat, or null. The
    /// escape (three shots from inside) has to be credited to the craft actually inside the
    /// mouth, not to whoever happens to pull a trigger while a digestion is running.</summary>
    private MawDigestion? SwallowedIn(PlayerTank who)
        => Digestion is { } d && ReferenceEquals(d.Victim, who) ? d : null;

    private readonly Projectile[] _projectiles;

    private const int MaxProjectiles = 64;
    private const float PlayerShotDamage = 1f;   // vs shield "points"
    private const float GrenadeDamage = 4f;      // heavy round, dealt to all in the blast
    private const float EnemyShotDamage = 12f;   // vs player's 100-point shield

    /// <summary>
    /// How much harder a hit lands on a naked VIRUS mote than on a body. This is the price
    /// of the whole class stated as one number: unhosted, the payload is fragile enough that
    /// a single hunter's round is most of a life, which is what makes reaching the next body
    /// a matter of survival rather than convenience. Ignored while a host is worn — there the
    /// husk soaks the blow instead (see <see cref="Entities.VirusRig.AbsorbDamage"/>).
    /// </summary>
    public const float MoteVulnerability = 2.2f;

    /// <summary>Half-height of the band an enemy shot has to arrive in to bite the player,
    /// measured off the craft's body centre (<see cref="EnemyTank.AimHeight"/>). Wide
    /// enough to cover the hull and forgive a per-tick step of the bolt, tight enough that
    /// climbing or dropping a couple of metres through the shot's flight slips it.</summary>
    private const float EnemyHitVertical = 1.6f;

    // Shield fraction at which the low-health alarm sounds. Crossing *down*
    // through this line fires warning.wav once — not once per frame below it.
    private const float LowShieldWarning = 0.45f;

    // --- The TANK's siege kit (world side) --------------------------------------
    // The craft-side state lives on PlayerTank; these are the numbers only the world can own,
    // because they are about the tank meeting the rest of the field — what a ram costs a hunter,
    // how far it shoves them, what an AP slug does to a line, and the screening smoke it lays.

    /// <summary>Base ram damage, scaled up by how hard the hull was moving. A cruising bump
    /// chips a hunter; a lurch-speed slam erases one. See <see cref="UpdateRam"/>.</summary>
    private const float RamDamage = 2.2f;

    /// <summary>How far a rammed hunter is thrown clear along the contact line. Enough to break
    /// contact in one tick, which is what keeps a single slam from billing every frame.</summary>
    private const float RamShove = 4.5f;

    /// <summary>What the AP slug deals per body it punches through — one-shots a standard hunter
    /// and badly hurts an elite, the price of five rounds and a long reload for a whole line.</summary>
    private const float SlugDamage = 3f;

    /// <summary>
    /// What a mortar burst deals to a boss's weak point when it lands on or arcs over one.
    /// Deliberately well short of the rocket's one-shot: the mortar is ammo-cheap and comes
    /// back every few seconds, so a boss should cost two or three good lobs (the crab's core is
    /// 4, the maw's crystal 5), not a single lucky one. This is the tank's indirect answer to a
    /// monster it can no longer leap up to hit — the same bargain the thrown CRAB CORE strikes,
    /// where a heavy detonation close enough simply reaches the core through its inert armour.
    /// </summary>
    private const float MortarBossDamage = 2f;

    /// <summary>The lone screening smoke a TANK lays with its dischargers. A short list, capped,
    /// aged every tick and swept when spent. Public so the renderer can draw the murk.</summary>
    public readonly List<SmokeCloud> Smoke = new();
    private const int MaxSmoke = 8;
    private const float SmokeLife = 6f;
    private const float SmokeRadius = 7.5f;

    // What one battery is worth when spent from the pack: 30% of the shield *and* 30%
    // of the Hyper reserve. Public so the inventory panel's right-click charge reads
    // the same figure the salvage used to apply on contact.
    public const float BatteryChargeFraction = 0.30f;

    // --- Dynamic horizon spawning -------------------------------------------------
    // Nothing is pinned to a fixed spot. Hunters, salvage and the rare Crab-Core all
    // fade in out of the fog ring around the craft as it roams: each is rolled on its
    // own timer and dropped at a random bearing on the far horizon, so the field is
    // built by where the player goes rather than pre-placed. Turned off for the
    // capture harness and the headless self-test, which want a fixed, known scene.
    public bool DynamicSpawning = true;

    /// <summary>
    /// True on the machine that owns the simulation — the host, and every solo run. False on a
    /// client, where the enemies, bosses, squads and everyone else's rounds are a picture the
    /// host paints through snapshots, not a thing this machine reasons about. A client still
    /// runs its <em>own</em> craft's physics and its own rounds for an instant, responsive feel
    /// (see <see cref="StepForTest"/>); it just never simulates anything it does not drive, so
    /// there are no phantom enemies firing phantom shots and no doubled sounds.
    /// </summary>
    public bool Authoritative { get; set; } = true;

    // --- Reconciliation of this machine's own craft (client only) -----------------
    // A client predicts its own craft every frame for instant control, but the host is the
    // authority on where it truly ended up — after a collision, a knockback, a seizure, or a
    // hyperspace the host rolled its own dice for. These hold the last authoritative transform
    // the host sent for the local seat; SmoothLocalCraft eases the predicted craft onto it so
    // it stays honest without snapping the camera in the player's hands.
    private Vector2 _netPos;
    private float _netHeight, _netHeading, _netPitch;
    private bool _hasNet;
    private bool _netFollow;   // capture/away: the host owns the transform outright — follow it, no deadzone

    // Below this much positional disagreement the prediction is simply trusted: the standing
    // gap between a predicted craft and the host's confirmation of it is about a round-trip of
    // travel, and correcting that every snapshot would drag the craft backward the whole time
    // it drove. Past the snap distance the two have genuinely parted company — a teleport, a bad
    // desync — and easing across the map would look worse than a cut.
    //
    // Every one of these is a guess until it has been driven on two machines with real
    // latency between them, so they are read from the environment rather than compiled in:
    // set VOIDTANKS_NET_DEADZONE / _SNAP / _RATE / _HEADDEAD / _HEADSNAP / _REMOTERATE to
    // retune a running build without a rebuild, which is the only way a two-machine session
    // can converge on numbers in one sitting. The defaults are the shipped values.
    private static readonly float ReconcileDeadzone = Tune("VOIDTANKS_NET_DEADZONE", 4f);
    private static readonly float ReconcileSnap = Tune("VOIDTANKS_NET_SNAP", 22f);
    private static readonly float ReconcileRate = Tune("VOIDTANKS_NET_RATE", 12f);
    private static readonly float ReconcileHeadingDead = Tune("VOIDTANKS_NET_HEADDEAD", 0.06f);
    private static readonly float ReconcileHeadingSnap = Tune("VOIDTANKS_NET_HEADSNAP", 0.7f);

    /// <summary>Reads one network feel constant from the environment, or hands back the
    /// shipped default. Bad text is ignored rather than crashing a session.</summary>
    private static float Tune(string name, float fallback)
        => float.TryParse(Environment.GetEnvironmentVariable(name),
               System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out float v) && v > 0f
           ? v : fallback;

    /// <summary>
    /// Client-side: records the host's authoritative transform for this machine's own craft,
    /// to be eased in by <see cref="SmoothLocalCraft"/>. <paramref name="follow"/> is set when
    /// the host has the craft in a hold (a seizure) or frozen (away) — cases where this machine
    /// is not predicting anything, so the transform is followed rather than blended against a
    /// prediction that is not happening.
    /// </summary>
    public void ReconcileLocal(Vector2 pos, float height, float heading, float pitch, bool follow)
    {
        _netPos = pos;
        _netHeight = height;
        _netHeading = heading;
        _netPitch = pitch;
        _netFollow = follow;
        _hasNet = true;
    }

    /// <summary>
    /// Eases the local craft toward the last authoritative transform the host sent. Called once
    /// a frame on a client, after the craft has predicted its own step. Small errors are left
    /// alone (the prediction is trusted), real ones are blended out over a few frames, and a
    /// craft the host has grabbed or frozen simply follows.
    /// </summary>
    private void SmoothLocalCraft(float dt)
    {
        if (!_hasNet) return;
        PlayerTank me = Players[LocalIndex];
        float k = 1f - MathF.Exp(-ReconcileRate * dt);

        if (_netFollow)
        {
            // The host owns the transform (a hold, or an away freeze). Glide onto it every
            // frame — no deadzone — so a yank into the air reads as a pull, not a jump.
            float kf = 1f - MathF.Exp(-ReconcileRate * 2f * dt);
            me.Position = Torus.Wrap(me.Position + Torus.Delta(me.Position, _netPos) * kf);
            me.Height += (_netHeight - me.Height) * kf;
            me.Heading += MathF.IEEERemainder(_netHeading - me.Heading, MathF.Tau) * kf;
            me.Pitch += (_netPitch - me.Pitch) * kf;
            return;
        }

        // Position: trust small errors, ease real ones, cut on a genuine parting.
        Vector2 err = Torus.Delta(me.Position, _netPos);
        float dist = err.Length();
        if (dist > ReconcileSnap) me.Position = _netPos;
        else if (dist > ReconcileDeadzone) me.Position = Torus.Wrap(me.Position + err * k);

        // Heading: the same deadzone-then-blend, so mouse aim stays the player's but never
        // drifts from what the host is telling everyone else the craft points at.
        float dh = MathF.IEEERemainder(_netHeading - me.Heading, MathF.Tau);
        float ah = MathF.Abs(dh);
        if (ah > ReconcileHeadingSnap) me.Heading = _netHeading;
        else if (ah > ReconcileHeadingDead) me.Heading += dh * k;

        // Height eases in without a deadzone — a jump the host resolved differently should
        // settle rather than hang a few units off the deck.
        if (MathF.Abs(_netHeight - me.Height) > 0.05f) me.Height += (_netHeight - me.Height) * k;
    }

    /// <summary>How fast a remote puppet is eased onto the host's latest report of it, per
    /// second. Fast enough to keep up with real motion, soft enough to turn 20 Hz steps into a
    /// glide rather than a series of catches.</summary>
    private static readonly float RemoteSmoothRate = Tune("VOIDTANKS_NET_REMOTERATE", 18f);

    /// <summary>
    /// Client-side: eases every remote puppet — team-mates, hunters, the squad — one frame
    /// toward the transform the host last reported for it, which is what turns the 20 Hz
    /// snapshot into smooth motion. The bosses ease themselves in their own <c>Animate</c>; the
    /// local craft is reconciled separately by <see cref="SmoothLocalCraft"/> because it
    /// predicts. Called once a frame from the client's step.
    /// </summary>
    private void InterpolateRemotes(float dt)
    {
        float k = 1f - MathF.Exp(-RemoteSmoothRate * dt);
        for (int i = 0; i < Players.Count; i++)
            if (i != LocalIndex) Players[i].EaseToNet(k);
        foreach (var e in Enemies) e.EaseToNet(k);
        foreach (var s in Soldiers) s.EaseToNet(k);
    }

    // --- Lag compensation ---------------------------------------------------------
    //
    // The host decides what a round hits, against where everything is *now*. A client saw
    // the field as it was a round trip ago, so at any real ping it leads its shots against
    // stale positions and the host scores them against fresh ones — which is the whole of
    // "I clearly hit them" missing. The fix is the standard one: the host keeps a short
    // history of where everything was, and when it resolves a shot fired by seat S it tests
    // it against the world as S actually saw it.
    //
    // Only the *test* is rewound. Damage, death and debris all land on the entity where it
    // truly is, so nothing else in the sim has to know this happened.

    /// <summary>
    /// How many ticks of history the host keeps. Forty at 60 Hz is two thirds of a second,
    /// which covers a 600 ms round trip — far past anything playable, and past that the
    /// rewind is simply clamped rather than reaching for a frame that has rolled off.
    /// </summary>
    private const int RewindFrames = 40;

    /// <summary>Where one body was on one tick. A flat list per frame, scanned linearly —
    /// there are at most a couple of dozen live bodies and a scan of that beats a dictionary
    /// allocated sixty times a second.</summary>
    private readonly (int Id, Vector2 Pos, float Height)[][] _rewindMarks =
        new (int, Vector2, float)[RewindFrames][];
    private readonly int[] _rewindCount = new int[RewindFrames];
    private readonly uint[] _rewindTick = new uint[RewindFrames];
    private int _rewindAt = -1;

    /// <summary>The host's own step counter, the clock the history is stamped against.</summary>
    private uint _simTick;

    /// <summary>How far behind the host each seat's view of the world runs, in ticks — one
    /// full round trip, measured by the session from the snapshot tick each client says it
    /// last applied. Zero for the host's own seat, which is never behind itself.</summary>
    private readonly int[] _lagTicks = new int[MatchSettings.MaxSeats];

    /// <summary>Host-side: records how stale <paramref name="seat"/>'s view is, in ticks.
    /// Called by the session as each client's input arrives with its acknowledgement.</summary>
    public void SetSeatLag(int seat, int ticks)
    {
        if ((uint)seat >= (uint)_lagTicks.Length) return;
        _lagTicks[seat] = Math.Clamp(ticks, 0, RewindFrames - 1);
    }

    // --- The scoreboard ------------------------------------------------------------
    // Kept here rather than on PlayerTank because a seat outlives the craft in it: a player
    // who has been through three chassis and two revives is still the same player, and their
    // tally has to survive every ReplacePlayer the match puts them through.

    private readonly int[] _kills = new int[MatchSettings.MaxSeats];
    private readonly int[] _deaths = new int[MatchSettings.MaxSeats];

    /// <summary>What this seat has destroyed.</summary>
    public int KillsOf(int seat) => (uint)seat < (uint)_kills.Length ? _kills[seat] : 0;

    /// <summary>How many times this seat has been destroyed.</summary>
    public int DeathsOf(int seat) => (uint)seat < (uint)_deaths.Length ? _deaths[seat] : 0;

    /// <summary>
    /// This seat's round trip, in milliseconds. Derived from the lag the session already
    /// measures for the rewind buffer rather than pinged separately — the number is the same
    /// number, and a second timing channel could only ever disagree with the first.
    /// </summary>
    public int PingOf(int seat)
        => (uint)seat < (uint)_lagTicks.Length ? (int)(_lagTicks[seat] * (1000f / 60f)) : 0;

    /// <summary>Host-side: writes a seat's tally straight from the wire on a client.</summary>
    public void SetScore(int seat, int kills, int deaths, int pingMs)
    {
        if ((uint)seat >= (uint)_kills.Length) return;
        _kills[seat] = kills;
        _deaths[seat] = deaths;
        _lagTicks[seat] = Math.Clamp((int)(pingMs * 60f / 1000f), 0, RewindFrames - 1);
    }

    /// <summary>
    /// A line for the combat feed — who killed what, who went down. Filled by the host and
    /// mirrored to every client by the session, which is why it is a plain string handed out
    /// rather than a structure: the wire already carries formatted notices, and a kill feed is
    /// a notice about a kill.
    /// </summary>
    public Action<string>? Announce;

    private void Credit(int seat)
    {
        if ((uint)seat < (uint)_kills.Length) _kills[seat]++;
    }

    // --- World markers -------------------------------------------------------------

    /// <summary>
    /// A place somebody pointed at. Twenty players sharing a world with no voice chat need
    /// <em>some</em> way to say "there" — this is it: a mark dropped on the world at the
    /// crosshair, seen by everybody, with a chirp from where it landed so it is heard as well
    /// as seen, even by a player facing the other way.
    /// </summary>
    public sealed class Marker
    {
        public required Vector2 Position { get; init; }
        public required int Seat { get; init; }
        public float Remaining { get; set; }
    }

    /// <summary>How long a mark stands. Long enough to drive to, short enough that a field
    /// full of old marks never becomes the thing you are reading instead of the world.</summary>
    public const float MarkerLife = 12f;

    /// <summary>One mark per player at a time: a second one replaces the first, so nobody
    /// can litter the arena and everybody's most recent intent is what is standing.</summary>
    private readonly List<Marker> _markers = new();

    public IReadOnlyList<Marker> Markers => _markers;

    /// <summary>Plants a mark for a seat, replacing whatever that seat had standing, and
    /// chirps from where it landed. Called on every machine — the one that pressed the key
    /// and, through the wire, everyone else.</summary>
    public void PlaceMarker(int seat, Vector2 at)
    {
        _markers.RemoveAll(m => m.Seat == seat);
        _markers.Add(new Marker { Position = Torus.Wrap(at), Seat = seat, Remaining = MarkerLife });
        Emit(Cue.Marker, at, owner: Projectile.NoOwner);
    }

    private void AgeMarkers(float dt)
    {
        for (int i = _markers.Count - 1; i >= 0; i--)
        {
            _markers[i].Remaining -= dt;
            if (_markers[i].Remaining <= 0f) _markers.RemoveAt(i);
        }
    }

    /// <summary>
    /// Where the camera is pointing, on the world. The point a mark is dropped at: walk the
    /// view direction until it meets the grid, or the first building it runs into, whichever
    /// comes first. Aiming at the sky lands the mark out at the end of the useful range
    /// rather than nowhere, so a press never silently does nothing.
    /// </summary>
    public Vector2 CrosshairTarget()
    {
        PlayerTank eye = Eye;
        float pitch = eye.Pitch;
        Vector2 flat = new(MathF.Sin(eye.Heading), MathF.Cos(eye.Heading));
        float eyeY = eye.Height + eye.EyeHeight;

        // Where the line meets the grid, if it is going down at all.
        float range = MarkerMaxRange;
        if (pitch < -0.02f) range = MathF.Min(range, eyeY / MathF.Tan(-pitch));

        // ...and the first wall in the way, which is nearly always the thing being pointed at.
        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        foreach (var s in Structures)
        {
            int n = s.Blockers(blockers);
            for (int i = 0; i < n; i++)
            {
                Vector2 rel = Torus.Delta(eye.Position, blockers[i].At);
                float along = Vector2.Dot(rel, flat);
                if (along <= 1f || along >= range) continue;
                if ((rel - flat * along).Length() >= blockers[i].Radius) continue;
                if (eyeY + MathF.Tan(pitch) * along > s.BlockHeight) continue;   // over the roof
                range = along;
            }
        }

        return Torus.Wrap(eye.Position + flat * range);
    }

    /// <summary>How far out a mark can be dropped. Past the fog there is nothing to point
    /// at, and a mark on the far side of the world is a mark about nothing.</summary>
    private const float MarkerMaxRange = 140f;

    /// <summary>Stable per-body ids for the history, handed out lazily. Separate from the
    /// snapshot's <c>NetId</c>, which is a byte and is reused freely — a rewind key must not
    /// be.</summary>
    private int _hitIdSeq;

    /// <summary>The id a player's seat goes by in the history. Negative, so it can never
    /// collide with a hunter's or a soldier's.</summary>
    private static int SeatHitId(int seat) => -1 - seat;

    /// <summary>
    /// Files where everything stands this tick. Called once per authoritative step, after
    /// everything has moved and before any round is resolved against it.
    /// </summary>
    private void RecordRewind()
    {
        _simTick++;
        _rewindAt = (_rewindAt + 1) % RewindFrames;
        _rewindTick[_rewindAt] = _simTick;

        int need = Players.Count + Enemies.Count + Soldiers.Count;
        var frame = _rewindMarks[_rewindAt];
        if (frame == null || frame.Length < need)
            _rewindMarks[_rewindAt] = frame = new (int, Vector2, float)[Math.Max(need, 32)];

        int n = 0;
        for (int i = 0; i < Players.Count; i++)
            frame[n++] = (SeatHitId(i), Players[i].Position, Players[i].Height);
        foreach (var e in Enemies)
        {
            if (e.HitId == 0) e.HitId = ++_hitIdSeq;
            frame[n++] = (e.HitId, e.Position, 0f);
        }
        foreach (var s in Soldiers)
        {
            if (s.HitId == 0) s.HitId = ++_hitIdSeq;
            frame[n++] = (s.HitId, s.Position, s.Height);
        }
        _rewindCount[_rewindAt] = n;
    }

    /// <summary>
    /// Where a body was when <paramref name="shooterSeat"/> saw it — the position their round
    /// should be tested against. Hands back <paramref name="now"/> unchanged for the host's own
    /// shots, for a seat with no measured lag, and whenever the history has nothing to say,
    /// so every path that is not a laggy client's shot behaves exactly as it always did.
    /// </summary>
    public Vector2 Rewound(int hitId, int shooterSeat, Vector2 now)
    {
        if (!Authoritative || _rewindAt < 0) return now;
        if ((uint)shooterSeat >= (uint)_lagTicks.Length) return now;
        int back = _lagTicks[shooterSeat];
        if (back <= 0 || hitId == 0) return now;

        int slot = ((_rewindAt - back) % RewindFrames + RewindFrames) % RewindFrames;
        // A frame that has rolled off (or never been written) is not history, it is a
        // guess — and a guess about where somebody was is worse than the truth about where
        // they are.
        if (_rewindTick[slot] != _simTick - (uint)back) return now;

        var frame = _rewindMarks[slot];
        if (frame == null) return now;
        int count = _rewindCount[slot];
        for (int i = 0; i < count; i++)
            if (frame[i].Id == hitId) return frame[i].Pos;
        return now;
    }

    /// <summary>As <see cref="Rewound(int, int, Vector2)"/>, for a seat's craft.</summary>
    public Vector2 RewoundSeat(int seat, int shooterSeat, Vector2 now)
        => Rewound(SeatHitId(seat), shooterSeat, now);

    /// <summary>Host-side: hands out a stable id for a hunter so a client can match the same one
    /// across field packets and interpolate it. Assigned lazily the first time a hunter is
    /// written to the wire (see <c>Snapshot.WriteField</c>); a byte on the wire, which is safe
    /// because only a handful are ever alive and in range at once.</summary>
    private int _netIdSeq;
    public int NextNetId() => ++_netIdSeq;

    // The fog band new arrivals drop into — past the near fog so they resolve as
    // blips on the skyline, yet close enough to eventually drift into play.
    private const float SpawnMinRange = 72f;
    private const float SpawnMaxRange = 120f;

    // Hunters: keep at most this many on the field. Rolls come at a brisk cadence, and
    // when the field is already full the farthest hunter (drifted off into the fog) is
    // let go so a fresh one can always resolve on the horizon — spawning never stalls.
    // The field runs denser than it used to across the board: partly for the fight, and
    // partly because one chassis now eats bodies to live — a VIRUS in a starved arena is
    // a corpse with a timer.
    private const int MaxEnemies = 6;
    private const float EnemySpawnInterval = 6f;
    private const float EnemySpawnChance = 0.7f;
    private const float EliteChance = 0.22f;
    private float _enemyTimer;

    // Floating salvage: a slow, endless drip. At the cap the farthest piece drifts out
    // of play and a new one fades in, so batteries and rounds never stop appearing.
    private const int MaxPickups = 7;
    private const float PickupSpawnInterval = 7f;
    private const float PickupSpawnChance = 0.6f;
    private const float BatteryShare = 0.6f;   // this fraction of new salvage is batteries
    private float _pickupTimer;

    // The Crab-Core is no longer a rare boss — it is a regular inhabitant of the field:
    // while none stalks it, roll often for one to rise out of the fog at a random
    // bearing. Still only ever one at a time, because everything that reads a crab (the
    // beam, the seizure, the renderer) reads *the* crab — the demotion is in how often
    // it shows up, not in what it is.
    private const float BossSpawnInterval = 10f;
    private const float BossSpawnChance = 0.4f;
    private float _bossTimer;

    // The Maw-Core rolls on its own clock, independent of the crab's — the two are
    // different threats occupying different space (one on the grid, one in the air
    // over it) and meeting both at once is now simply an ordinary evening. Like the
    // crab it has stopped being an event and started being a neighbour.
    private const float MawSpawnInterval = 11f;
    private const float MawSpawnChance = 0.35f;
    private float _mawTimer;

    // Soldier squads. Rarer and slower than everything else on this list, because a squad
    // is four bodies rather than one and because they do not drift in and mill about —
    // they arrive on a tower, watch, and then the fight is on until one side of it is
    // dead. Two squads at once is already eight people in the air; three would be weather.
    private const int MaxSquads = 2;
    private const float SquadSpawnInterval = 16f;
    private const float SquadSpawnChance = 0.5f;
    private float _squadTimer;

    /// <summary>What one of their rifle rounds costs. Well under a hunter's cannon shell —
    /// a rifle is a rifle — but they fire far faster and there are four of them, so a squad
    /// that has settled into its ring is doing more damage a second than any tank on the
    /// field.</summary>
    private const float SoldierShotDamage = 7f;

    /// <summary>And what the blades cost on a pass that lands. Nearly twice a shell,
    /// because a run is a whole committed arc with a wind-up you can watch coming, a
    /// direction you can move out of and a soldier hanging helpless on a cable behind it.
    /// If they connect anyway, they have earned it.</summary>
    private const float SoldierBladeDamage = 20f;

    /// <summary>
    /// Builds a stage for the given hangar build. A null loadout is the standard TANK
    /// on a straight 5/5/5 — the craft this game had before the hangar existed — which
    /// is what the headless self-test and the capture harness both want.
    /// </summary>
    public World(Loadout? loadout = null, MatchSettings? match = null)
    {
        Loadout = loadout ?? new Loadout();
        Match = (match ?? MatchSettings.SinglePlayer).Clamped();

        // The wire's directory of the skyline, taken while the field is still whole and every
        // building's Index is its place in it. Held for the life of the stage, so a razed lot
        // can still be named and recognised long after it left the live list.
        _byIndex = Structures.ToArray();

        // Seat zero. In a solo run it is the only one; in a match the host fills the rest
        // as people join, and this one is still the host's own craft.
        Players.Add(new PlayerTank(Vector2.Zero, 0f, Loadout) { Lives = Match.Revives + 1 });

        // A soldier does not start in the clearing. Every other chassis opens at the
        // origin, which the skyline is deliberately kept out of (see
        // StructureField.ClearRadius) so nothing stands in the player's nose on the first
        // frame — but this one has nothing to hang from out there, and a class whose
        // whole loop is anchors would open on a minute of walking. So it starts within
        // one cable's throw of the nearest tower, looking straight at it.
        if (Player.Soldier != null) StandTheSoldierInTheCity(Player);
        // And a fish does not start on the seabed. Opening beached would put the player's
        // first ten seconds in the one state the entire chassis is designed around
        // escaping — so it opens already swimming, up level with the middle of the towers,
        // with speed on the clock and the city laid out beneath it.
        if (Player.Fish != null) SwimTheFishOffTheDeck(Player);

        _projectiles = new Projectile[MaxProjectiles];
        for (int i = 0; i < _projectiles.Length; i++)
            _projectiles[i] = new Projectile();

        // Capture overrides freeze a controlled scene for the verification harness:
        // exact point-blank placements and no drifting-in spawns.
        string? nearPickup = Environment.GetEnvironmentVariable("VOIDTANKS_PICKUP_NEAR");
        string? nearEnemy = Environment.GetEnvironmentVariable("VOIDTANKS_ENEMY_NEAR");
        string? nearBoss = Environment.GetEnvironmentVariable("VOIDTANKS_BOSS_NEAR");
        string? nearMaw = Environment.GetEnvironmentVariable("VOIDTANKS_MAW_NEAR");
        bool capture = nearPickup == "1" || nearEnemy is "1" or "elite"
                    || nearBoss is "1" or "seize" || nearMaw is "1" or "swallow";
        if (capture) DynamicSpawning = false;

        // FLAT is a sandbox: the city still stands (Structures is built regardless, above),
        // but nothing hostile is seeded and nothing ever spawns. Turn the director off and
        // skip every seeding block below — no salvage, no opening hunter, no bosses.
        if (Match.Map == GameMap.Flat)
        {
            DynamicSpawning = false;
            return;
        }

        // Salvage. Capture seeds one battery and one round dead ahead; play seeds a
        // small starter field at random fog bearings — no fixed spots — so there's
        // salvage on the horizon from the first frame, then the director tops it up.
        if (nearPickup == "1")
        {
            Pickups.Add(new Pickup(new Vector2(-2.5f, 14f), PickupKind.Battery));
            Pickups.Add(new Pickup(new Vector2(2.5f, 14f), PickupKind.Ammo));
        }
        else
        {
            for (int i = 0; i < 3; i++)
                Pickups.Add(new Pickup(RandomPointAroundPlayer(SpawnMinRange, SpawnMaxRange), PickupKind.Battery));
            for (int i = 0; i < 2; i++)
                Pickups.Add(new Pickup(RandomPointAroundPlayer(SpawnMinRange, SpawnMaxRange), PickupKind.Ammo));
        }

        // One hunter to open on. Capture drops it in close on-axis so its polygon
        // silhouette can be inspected; play fades it in from a random bearing out in
        // the fog, and the director adds more over time. A host who turned enemies off
        // gets no opening hunter and no director top-ups (see the spawn director in Step) —
        // the salvage above still seeds, since salvage is not an enemy.
        if (nearEnemy == "1")
            Enemies.Add(new EnemyTank(new Vector2(4f, 16f), elite: false));
        else if (nearEnemy == "elite")
            Enemies.Add(new EnemyTank(new Vector2(4f, 16f), elite: true));
        else if (Match.SpawnEnemies)
            Enemies.Add(new EnemyTank(RandomPointAroundPlayer(60f, 80f), elite: false));

        // The Crab-Core is no longer pre-placed on the field. A capture override drops
        // it in point-blank for screenshots; in play it starts absent and rises rarely
        // out of the fog via the spawn director.
        // "seize" stands the boss right on top of the player instead, inside
        // CrabSeizure.GrabRadius, so the protocol runs itself up to pursuit and the
        // grab fires on its own a second or two in — the only way to get a screenshot
        // of the cinematic, which otherwise needs a human to let the thing corner them.
        Boss = nearBoss switch
        {
            "1"     => new CrabCore(new Vector2(0f, 44f)),
            "seize" => new CrabCore(new Vector2(0f, 9f)),
            _       => null,
        };

        // Same arrangement for the Maw-Core. "swallow" hangs it directly over the
        // player's head, inside its own strike column, so it winds up and drops on a
        // stationary craft within a second — the only way to screenshot the digestion,
        // which otherwise needs a human willing to stand still and be eaten.
        Maw = nearMaw switch
        {
            "1"       => new MawCore(new Vector2(0f, 20f)),
            "swallow" => new MawCore(Vector2.Zero),
            _         => null,
        };

        // A squad cannot be "placed near" the way a monster can — the whole opening is
        // four people standing on a building, so the harness picks the nearest tower out
        // in front of the craft and hangs them off that instead, then turns the view onto
        // it. Which is exactly the picture the enemy is for: you look up at a spire and
        // there are four of them on it.
        if (Environment.GetEnvironmentVariable("VOIDTANKS_SQUAD_NEAR") is "1" or "track" or "pose")
        {
            DynamicSpawning = false;
            Vector2 ahead = Torus.Wrap(Player.Position + Player.Forward * 70f);
            SpawnSoldierSquad(ahead);
            if (Soldiers.Count > 0) FaceTheSquad();
        }
    }

    /// <summary>Capture harness only: points the craft at the squad it was just handed,
    /// eyes up, so the picture is of the thing on the tower rather than of the tower. Kept
    /// callable every frame so a capture can follow one of them through an arc, which is
    /// the only way to photograph an enemy that spends its life moving.</summary>
    public void FaceTheSquad()
    {
        if (Soldiers.Count == 0) return;

        // The nearest of them, so a tracking capture follows whoever is actually in the
        // fight rather than whichever one happens to be first in the list.
        EnemySoldier mark = Soldiers[0];
        float best = float.MaxValue;
        foreach (var s in Soldiers)
        {
            float d = Torus.DistanceSquared(s.Position, Player.Position);
            if (d >= best) continue;
            best = d;
            mark = s;
        }

        Vector2 to = Torus.Delta(Player.Position, mark.Position);
        Player.Heading = MathF.Atan2(to.X, to.Y);
        Player.Pitch = MathF.Atan2(mark.Height - Player.EyeHeight, MathF.Max(1f, to.Length()));
    }

    public IReadOnlyList<Projectile> Projectiles => _projectiles;

    // --- Sound cues ---------------------------------------------------------------

    /// <summary>One positioned sound the sim asked for this step. The host collects these and
    /// broadcasts them; each machine plays them attenuated to its own craft.</summary>
    public readonly record struct SoundCue(Cue Id, Vector2 Pos, float Param, int Owner);

    /// <summary>Cues raised since the last broadcast. Host-side; drained by the session.</summary>
    public readonly List<SoundCue> SoundCues = new();

    /// <summary>Particle bursts raised since the last broadcast — deaths, hits, footfalls, laid
    /// smoke. Host-side; drained by the session and replayed on every client so the field's
    /// debris is seen off the host's machine too. Fed by the DebrisSystem's sink and LaySmoke.</summary>
    public readonly List<EffectCue> EffectCues = new();

    /// <summary>Each seat's display name, for the floating tags over team-mates in a match.
    /// Filled once at launch from the lobby's roster; empty in a solo run.</summary>
    public readonly Dictionary<int, string> SeatNames = new();

    /// <summary>The name over a seat's craft, or an empty string if none is known.</summary>
    public string NameOf(int seat) => SeatNames.TryGetValue(seat, out var n) ? n : "";

    /// <summary>A name for the feed and the scoreboard, falling back to the seat number so a
    /// line is never about nobody. Solo has no roster at all and reads as PILOT.</summary>
    public string NameOrSeat(int seat)
    {
        string n = NameOf(seat);
        if (n.Length > 0) return n;
        return Match.MaxPlayers <= 1 ? "PILOT" : $"SEAT {seat}";
    }

    /// <summary>True only when a host session is listening for cues to broadcast. Off in a solo
    /// run, so <see cref="Emit"/> never files cues nobody will ever drain — otherwise the list
    /// would grow without bound over a long single-player game. Setting it also points the debris
    /// system's sink at <see cref="EffectCues"/>, so the same host-only switch gathers particles.</summary>
    public bool CollectSoundCues
    {
        get => _collectSoundCues;
        set { _collectSoundCues = value; Debris.CueSink = value ? EffectCues : null; }
    }
    private bool _collectSoundCues;

    /// <summary>
    /// Asks for a one-shot sound at a world position. This is how every combat noise now
    /// leaves the sim, so multiplayer can carry it: the machine that should hear it plays it
    /// attenuated to its own craft, and the host also files it to broadcast to everyone else.
    ///
    /// <paramref name="owner"/> is the seat that caused it, or <see cref="Projectile.NoOwner"/>
    /// for anything the world did. A client plays its <em>own</em> cues here for an instant
    /// response and then ignores the host's echo of them; everyone else's it hears only when
    /// the host's sound packet lands.
    /// </summary>
    /// <param name="personal">A cue only its owner should ever hear — a pickup chime, a
    /// gauge warning. Without this the host, which plays every cue the sim raises, would
    /// chime for salvage collected by a player on the far side of the world: these clips
    /// carry no distance attenuation, so "far away" and "in your ear" sound identical.</param>
    public void Emit(Cue id, Vector2 pos, float param = 0f, int owner = Projectile.NoOwner,
        bool personal = false)
    {
        bool mine = owner == LocalIndex;
        float range = Torus.Distance(pos, Eye.Position);

        // Out of earshot. Several clips in the bank carry no distance attenuation at all —
        // a rifle report, a hard landing, a cable coming home — so on the host, which raises
        // every seat's cues, one player crashing into a tower on the far side of the world
        // would be as loud as one crashing beside you. The cull is the same radius past which
        // a client is never sent the cue in the first place, so what the host hears is what a
        // client standing in the same place would hear. Never applied to our own.
        if (!mine && range > Net.Snapshot.InterestRadius)
        {
            if (Authoritative && CollectSoundCues) SoundCues.Add(new SoundCue(id, pos, param, owner));
            return;
        }

        if (mine || (Authoritative && !personal))
        {
            CueBank.Play(id, pos, param);
            // Anything heavy going off close enough rings the ears: the mix drops away, the
            // top end goes with it and a tone is left behind. Raised here rather than at
            // the forty places a blast can come from, so nothing can be forgotten.
            if (IsConcussive(id)) ShakeFromBlast(range);
        }
        if (Authoritative && CollectSoundCues) SoundCues.Add(new SoundCue(id, pos, param, owner));
    }

    /// <summary>Counts cues actually taken from the wire and played. A verification hook — the
    /// headless self-test asserts sounds cross the wire, since it can open no audio device to
    /// hear them.</summary>
    public int RemoteCuesPlayed { get; private set; }

    // --- Client boss puppets ------------------------------------------------------
    // A client is shown the host's bosses, not simulating them. These install and refresh
    // render-only puppets from the snapshot; Boss/Maw's own setters are private, so the adopt
    // has to live here on the world that owns them.

    /// <summary>Client-side: install or refresh the host's Crab-Core as a render-only puppet.</summary>
    public void AdoptBoss(Vector2 pos, float heading, CrabCore.State phase, float coreFrac,
        float grabArm, float strikeArm, float seizureGlow)
    {
        if (Boss is not { IsPuppet: true } p) { p = CrabCore.Puppet(pos, heading); Boss = p; }
        p.NetSet(pos, heading, phase, coreFrac, grabArm, strikeArm, seizureGlow);
    }

    /// <summary>Client-side: the host no longer reports a Crab-Core — drop the puppet.</summary>
    public void ClearBossPuppet() { if (Boss is { IsPuppet: true }) Boss = null; }

    /// <summary>Client-side: install or refresh the host's Maw-Core as a render-only puppet.</summary>
    public void AdoptMaw(Vector2 pos, MawCore.State phase, float crystalFrac, float bodyY, float jaw)
    {
        if (Maw is not { IsPuppet: true } m) { m = MawCore.Puppet(pos); Maw = m; }
        m.NetSet(pos, phase, crystalFrac, bodyY, jaw);
    }

    /// <summary>Client-side: the host no longer reports a Maw-Core — drop the puppet.</summary>
    public void ClearMawPuppet() { if (Maw is { IsPuppet: true }) Maw = null; }

    /// <summary>Client-side: opens the squad adopt sweep. The soldiers are kept between packets
    /// (matched on their slot) so they can be interpolated rather than rebuilt; this only marks
    /// them all unseen, and <see cref="EndAdoptSoldiers"/> drops the ones this packet omits.</summary>
    public void BeginAdoptSoldiers()
    {
        foreach (var s in Soldiers) s.NetSeen = false;
    }

    /// <summary>Client-side: updates the host-described soldier in this slot, or creates its
    /// puppet the first time. <see cref="EnemySoldier.NetSet"/> eases the transform rather than
    /// snapping it, so a member kept across packets flies smoothly.</summary>
    public void AdoptSoldier(Vector2 pos, float height, float heading, SoldierMove move,
        float bank, float speed, bool leader, bool allied, int slot)
    {
        EnemySoldier? s = null;
        foreach (var m in Soldiers) if (m.Slot == slot) { s = m; break; }
        if (s is null) { s = EnemySoldier.Puppet(pos, height, leader, slot); Soldiers.Add(s); }
        s.NetSet(pos, height, heading, move, bank, speed, allied, alive: true);
        s.NetSeen = true;
    }

    /// <summary>Client-side: closes the squad adopt sweep, dropping any member the latest packet
    /// did not name.</summary>
    public void EndAdoptSoldiers() => Soldiers.RemoveAll(s => !s.NetSeen);

    /// <summary>Client-side: opens the hunter adopt sweep. Hunters are kept between field packets
    /// (matched on the host's id) so they can be interpolated rather than rebuilt each one; this
    /// marks them all unseen, and <see cref="EndAdoptEnemies"/> drops the ones a packet omits.</summary>
    public void BeginAdoptEnemies()
    {
        foreach (var e in Enemies) e.NetSeen = false;
    }

    /// <summary>Client-side: updates the host-described hunter with this id, or creates its
    /// puppet the first time. The transform is eased (see <see cref="EnemyTank.NetTarget"/>), so
    /// a hunter kept across packets glides rather than stepping twenty times a second.</summary>
    public void AdoptEnemy(int id, Vector2 pos, float heading, bool elite)
    {
        EnemyTank? e = null;
        foreach (var m in Enemies) if (m.NetId == id) { e = m; break; }
        if (e is null) { e = new EnemyTank(pos, elite) { NetId = id }; Enemies.Add(e); }
        e.NetTarget(pos, heading);
        e.NetSeen = true;
    }

    /// <summary>Client-side: closes the hunter adopt sweep, dropping any the latest packet did
    /// not name (killed, or drifted out of interest range).</summary>
    public void EndAdoptEnemies() => Enemies.RemoveAll(e => !e.NetSeen);

    // --- Remote rigs: the transient combat light ----------------------------------
    // Cables, the virus's stolen lance and the spider's charged beam are the parts of a fight
    // that are pure picture — nothing in the sim reads them. They were also entirely local, so
    // a team-mate cutting a street in half did it invisibly on every other screen. The host
    // now describes them per snapshot and they land here, deliberately in a render-only
    // structure rather than being poked into the puppet craft's own rig: none of this is
    // state, and giving a puppet a half-real grapple would invite something to believe it.

    /// <summary>One remote craft's combat light, as the host last described it.</summary>
    public sealed class RemoteRig
    {
        /// <summary>Named by the latest packet. Anything unseen is dropped at the end of the
        /// sweep, which is how a cable coming home or a beam finishing goes dark.</summary>
        public bool Seen;

        /// <summary>Seconds since a packet last mentioned this craft. The sweep above only
        /// runs when a packet <em>arrives</em>, and the host stops sending one at all once
        /// nothing near this client is throwing light — so without a clock the last beam of a
        /// fight would hang in the air forever.</summary>
        public float Age;

        public bool HasCables;
        public HookState LeftState, RightState;
        public Vector2 LeftTip, RightTip;
        public float LeftTipY, RightTipY;

        /// <summary>Live shafts of the virus's stolen lance, newest packet's worth.</summary>
        public int ShaftCount;
        public readonly VirusRig.LanceShaft[] Shafts = new VirusRig.LanceShaft[VirusRig.MaxShafts];

        public bool HasBeam;
        public Vector3 BeamOrigin, BeamDir;
        public float BeamPower, BeamCharge;
        /// <summary>How far through its burn the shaft is, or -1 while the meter is still
        /// filling and there is no shaft yet — the same convention the local craft uses.</summary>
        public float BeamProgress = -1f;
    }

    /// <summary>Every remote craft's combat light, by seat. Read by the renderer; empty on a
    /// host or a solo run, where the real rigs are right there to draw from.</summary>
    public readonly Dictionary<int, RemoteRig> RemoteRigs = new();

    /// <summary>Client-side: opens the rig sweep, marking every remote craft's light unseen.</summary>
    public void BeginAdoptRigs()
    {
        foreach (var r in RemoteRigs.Values) { r.Seen = false; r.HasCables = false; r.HasBeam = false; }
    }

    /// <summary>
    /// The record for one seat, or null for the one seat this machine simulates itself. Its
    /// own craft's rig is live and right here, so taking the host's account of it as well
    /// would draw every shaft twice — at double brightness, a round trip out of date.
    /// </summary>
    private RemoteRig? RigFor(int seat)
    {
        if (seat == LocalIndex) return null;
        if (!RemoteRigs.TryGetValue(seat, out var r)) RemoteRigs[seat] = r = new RemoteRig();
        r.Seen = true;
        r.Age = 0f;
        return r;
    }

    public void AdoptHook(int seat, bool right, HookState state, Vector2 tip, float tipY)
    {
        if (RigFor(seat) is not { } r) return;
        r.HasCables = true;
        if (right) { r.RightState = state; r.RightTip = tip; r.RightTipY = tipY; }
        else { r.LeftState = state; r.LeftTip = tip; r.LeftTipY = tipY; }
    }

    public void AdoptShaft(int seat, int slot, Vector3 origin, Vector3 dir, float life)
    {
        if (RigFor(seat) is not { } r) return;
        if ((uint)slot >= (uint)r.Shafts.Length) return;
        r.Shafts[slot] = new VirusRig.LanceShaft { Origin = origin, Dir = dir, Life = life };
    }

    /// <summary>Client-side: how many shafts this packet actually carried, so the ones left
    /// over from a busier discharge stop being drawn.</summary>
    public void EndAdoptShafts(int seat, int count)
    {
        if (RigFor(seat) is { } r) r.ShaftCount = count;
    }

    public void AdoptBeam(int seat, Vector3 origin, Vector3 dir, float power, float charge,
        float progress)
    {
        if (RigFor(seat) is not { } r) return;
        r.HasBeam = true;
        r.BeamOrigin = origin;
        r.BeamDir = dir;
        r.BeamPower = power;
        r.BeamCharge = charge;
        r.BeamProgress = progress;
    }

    /// <summary>Client-side: closes the rig sweep, letting go of any craft the latest packet
    /// did not mention — it has stopped doing anything worth drawing, or drifted out of range.</summary>
    public void EndAdoptRigs()
    {
        if (RemoteRigs.Count == 0) return;
        _rigSweep.Clear();
        foreach (var (seat, r) in RemoteRigs)
        {
            if (r.Seen) continue;
            _rigSweep.Add(seat);
        }
        foreach (int seat in _rigSweep) RemoteRigs.Remove(seat);
    }

    private readonly List<int> _rigSweep = new();

    /// <summary>How long a described rig survives without a fresh packet. Comfortably more
    /// than the snapshot interval, so ordinary loss never blinks a cable out, and short enough
    /// that the last beam of a fight is gone before anyone wonders about it.</summary>
    private const float RigStaleTime = 0.35f;

    /// <summary>Client-side: lets go of any described rig the host has stopped mentioning. The
    /// host sends no packet at all when nothing near this client is throwing light, so this is
    /// the only thing that ever clears the last one.</summary>
    private void AgeRemoteRigs(float dt)
    {
        if (RemoteRigs.Count == 0) return;
        _rigSweep.Clear();
        foreach (var (seat, r) in RemoteRigs)
        {
            r.Age += dt;
            if (r.Age > RigStaleTime) _rigSweep.Add(seat);
        }
        foreach (int seat in _rigSweep) RemoteRigs.Remove(seat);
    }

    /// <summary>Client-side: plays a cue the host sent, attenuated to this craft. Skips a cue
    /// this player caused, which they already heard the instant they did it.</summary>
    public void PlayRemoteCue(Cue id, Vector2 pos, float param, int owner)
    {
        // Our own cues we already heard the instant we raised them — except the handful the
        // host alone can raise, which we never played and would otherwise never hear.
        if (owner == LocalIndex && !CueBank.RaisedOnlyByHost(id)) return;
        RemoteCuesPlayed++;
        CueBank.Play(id, pos, param);
        if (IsConcussive(id)) ShakeFromBlast(Torus.Distance(pos, Eye.Position));
    }

    /// <summary>
    /// Clears the round pool ahead of a snapshot. Client-side only — the host owns every
    /// round on the field and a client's pool is a picture of it, redrawn each packet.
    /// </summary>
    public void BeginAdoptRounds()
    {
        // Keep this client's OWN rounds — it predicts those itself for an instant shot, and the
        // adopt pass below deliberately skips them (see Snapshot.ApplyField). Everyone else's
        // rounds are the host's to describe and are cleared to be repainted from the packet.
        foreach (var p in _projectiles)
            if (p.Owner != LocalIndex) p.Active = false;
    }

    /// <summary>Puts one host-described round into the pool. Silently drops it if the pool
    /// is full, exactly as a local over-spawn does.</summary>
    public void AdoptRound(Vector2 pos, float height, Vector2 velocity, int owner, byte flags)
    {
        foreach (var p in _projectiles)
        {
            if (p.Active) continue;
            p.AdoptFromWire(pos, height, velocity, owner, flags);
            return;
        }
    }

    public void Update(float dt, in InputFrame input, bool acceptCombatInput = true)
    {
        // The local seat's keys, as the loop just sampled them. Every other seat was filed
        // by SetInput when its packet arrived.
        _inputs[LocalIndex] = input;

        // Read each craft's own intent and let it act on it. This used to be one player's
        // worth of trigger handling written straight into the step; it is the same code,
        // asked once per seat.
        //
        // acceptCombatInput mutes the *local* craft only — it means "this machine's
        // crafting panel is open and owns the mouse", which is a fact about this keyboard
        // and nobody else's. A remote player with their own panel up sends a frame with
        // the combat bits already cleared, so the host never has to know.
        for (int seat = 0; seat < Players.Count; seat++)
        {
            if (Players[seat].Away) continue;   // a dropped player pulls no triggers
            DriveSeat(Players[seat], _inputs[seat], dt,
                      live: seat != LocalIndex || acceptCombatInput);
        }

        StepForTest(dt, input);
    }

    /// <summary>
    /// One craft's turn: hand its rig the movement keys, turn its look with its mouse, and
    /// pull whatever triggers it is holding. Everything here was the body of
    /// <see cref="Update"/> back when there was only ever one of them.
    /// </summary>
    private void DriveSeat(PlayerTank who, in InputFrame input, float dt, bool live)
    {
        // The SOLDIER walks on its own keys rather than through the craft's throttle, so its
        // WASD is read here and handed to the rig every frame — including while the crafting
        // panel is up, exactly as a tank keeps coasting under the overlay. The *look* is not:
        // the panel has the mouse, and a player dragging a stack across a slot must not also
        // be spinning on the spot behind it.
        //
        // The scripted hooks below are the self-test's and only ever drive the local craft,
        // which is the only one it has.
        bool local = ReferenceEquals(who, Player);

        if (who.Soldier is { } rig)
        {
            rig.MoveInput = (local ? ScriptedSoldierMove : null) ?? input.SoldierMove;
            if (live && !who.Captured) UpdateMouseLook(input, who);
        }

        // The FISH is the same arrangement with a different pair of keys: A/D roll the body
        // and S folds the fins. The beat is not read here because it is not a state — it is
        // an event, and it belongs with the triggers below.
        if (who.Fish is { } body)
        {
            body.MoveInput = (local ? ScriptedFishMove : null)
                ?? new Vector2(input.RollInput, input.BrakeDown ? 1f : 0f);
            if (live && !who.Captured) UpdateMouseLook(input, who);
        }

        // The VIRUS is the same arrangement again: WASD is its movement — the mote's flight
        // and the worn host's drive both read it — and the mouse is the look, the whole way
        // up and down since the mote flies where it points.
        if (who.Virus is { } payload)
        {
            payload.MoveInput = (local ? ScriptedVirusMove : null) ?? input.VirusMove;
            if (live && !who.Captured) UpdateMouseLook(input, who);
        }

        // The machines — the TANK and the SPIDER — read the same mouse to turn the whole
        // craft, exactly as the two bodies above do.
        if (who.IsMachine && live && !who.Captured) UpdateMouseLook(input, who);

        if (live)
        {
            // Inside the Maw-Core's throat every trigger is the same trigger: there is
            // nothing to lob a splash round at inside a mouth, so both buttons route to the
            // one action that can save them. Only the craft actually being digested — which
            // is no longer assumed to be this machine's own, since the mouth can now swallow
            // any seat and the host runs the escape for whoever is in there.
            if (SwallowedIn(who) is { Held: true })
            {
                if (input.Fire || input.Grenade) FirePlayerShot(laser: false, by: who);
            }
            else if (who.Spider is { } spider)
                UpdateSpiderTriggers(spider, dt, input, who);
            else if (who.Soldier is { } soldier)
                UpdateSoldierTriggers(soldier, dt, input, who);
            else if (who.Fish is { } swimmer)
                UpdateFishTriggers(swimmer, input, who);
            else if (who.Virus is { } virusRig)
                UpdateVirusTriggers(virusRig, input, who);
            else
                UpdateTankTriggers(input, who);   // the TANK's kit, and the plain machine default

            if (input.HyperspacePressed && who.TryHyperspace()) NoteHyperspace(who);

            // R/T/Y/U throw whatever the matching equip slot holds. Driven from the input
            // frame rather than polled off this machine's keyboard, which is what lets a
            // remote player actually throw the CRAB CORE they crafted: their press rides
            // their input packet to the host, which spends it from *their* pack. The local
            // seat still predicts the throw for an instant lob; the host's inventory echo is
            // what settles whether the core was really there to spend.
            int equip = input.WeaponSlotPressed();
            if (equip >= 0) UseWeaponSlot(equip, who);
        }
        else
        {
            // The panel is up and the mouse belongs to it. Drop any part-wound lance rather
            // than letting it sit charged (and the craft rooted) behind an overlay the
            // player is busy dragging items around in.
            who.Spider?.Cancel();
            if (who.Spider != null && local) Audio.SetLanceCharge(false, 0f);
            who.Rooted = false;
            // A tank reading the crafting panel is not dug in.
            who.Unplant();
        }

        // A soldier reading the crafting panel is not reeling, and a fish reading it is not
        // tearing through the water. Sound, so only ever the craft at this machine.
        if (!live && local && (who.Soldier != null || who.Fish != null || who.Virus != null))
        {
            Audio.SetReel(false, 0f);
            Audio.SetWind(false, 0f);
        }
    }

    /// <summary>
    /// The input-free simulation step: player physics, enemy AI, projectiles,
    /// collisions, cleanup. Shared by the live loop and the headless self-test
    /// (which injects player fire via <see cref="FirePlayerShot"/>).
    /// </summary>
    public void StepForTest(float dt, in InputFrame input = default)
    {
        // The local seat's intent is whatever the loop just sampled; every other seat was
        // filed by SetInput as its packet arrived. Doing it here rather than in the caller
        // means the self-test and the capture harness get the same empty frame they always
        // did without either of them knowing seats exist.
        _inputs[LocalIndex] = input;

        // Every craft drives, each from its own slot. In a solo run this is the one loop
        // iteration it has always been. A craft whose player has dropped is frozen where it
        // stands until they reconnect — not stepped, so it neither drifts nor decays.
        for (int i = 0; i < Players.Count; i++)
            // On a client only the local craft predicts; every other seat is a puppet eased
            // toward the host's account by InterpolateRemotes, so stepping it here with a stale
            // (empty) input frame would only fight that. On the host and solo, all seats step.
            if (!Players[i].Away && (Authoritative || i == LocalIndex))
                Players[i].Update(dt, _inputs[i]);
        // What the craft is standing on, before anything asks whether it is inside a
        // building: a SPIDER that has just landed on a roof is up there for the whole of
        // the rest of this tick, not from the next one.
        ResolveRoofs();
        ResolveStructureCollisions();
        // Thrown hulls fly on their own clock, before the hunters take their turn — a
        // machine still in the air is not driving, and one that lands this tick should be
        // back on the grid by the time anything measures the range to it.
        UpdateFlungBodies(dt);
        // After the collision pass, so a swing that ended in a wall is one of the events
        // being drained rather than something that happens a tick later.
        //
        // Seat-aware, like the virus drain below it: on the host every rig is drained, so a
        // team-mate's anchor biting, gas jump, hard landing and fish strike all become cues
        // with an owner and cross the wire. Before this they were raised straight at the local
        // speaker, so half the noises a soldier or a fish makes existed only on the machine
        // flying it. A client drains only its own rig — everyone else's reaches it as sound.
        for (int i = 0; i < Players.Count; i++)
        {
            PlayerTank who = Players[i];
            if (who.Away) continue;
            if (!Authoritative && i != LocalIndex) continue;
            if (who.Rig is { } rig) UpdateSoldierEvents(who, rig, dt);
            if (who.Fish is { } body) UpdateFishEvents(who, body);
        }

        // Where the ears are, and what kind of space they are in. Above the authority gate
        // on purpose: a client hears the world too, and hears it from its own seat.
        DriveListener(dt);

        // A client stops here. Everything past this point — the hunters, the squads, the boss,
        // the mouth, every round that is not its own — is the host's to simulate and this
        // machine's only to be shown, through the snapshots that overwrite the lists each
        // packet. It still drains its own mote and flies its own rounds for the frames before
        // the host's account arrives, so its own craft and its own shots feel instant; the host
        // decides what any of it hits.
        if (!Authoritative)
        {
            // Ease our own craft toward the host's authoritative transform. Its status —
            // shield, lives, capture — was already applied from the snapshot; this keeps the
            // predicted craft honest in space without snapping the camera in the player's hands.
            SmoothLocalCraft(dt);

            // Ease every remote puppet — team-mates, hunters, the squad — toward the host's
            // last account of it, so the 20 Hz snapshot reads as smooth motion instead of a
            // strobe. The bosses ease themselves in their own Animate, below.
            InterpolateRemotes(dt);

            if (Player.Virus is { } mine) UpdateVirusEvents(mine, Player);
            // Advance every live round, not just our own: ours are the prediction, and the rest
            // are dead-reckoned along the velocity the host sent, so remote fire flies smoothly
            // between field packets rather than hopping. The host still decides what any of it hits.
            foreach (var p in _projectiles)
                if (p.Active) p.Update(dt);
            // The boss puppets carry the host's position and phase; their spin and death glitch
            // are cosmetic and run off a local clock here, no AI behind them.
            if (Boss is { IsPuppet: true } bp) bp.Animate(dt);
            if (Maw is { IsPuppet: true } mp) mp.Animate(dt);
            // Salvage the host placed just bobs and turns — a local clock, since the client
            // never collects it (the host does, and drops it from the next packet).
            foreach (var pk in Pickups) pk.Update(dt);
            UpdateSmoke(dt);
            // The skyline ages on the client too. It never *cuts* anything here — the damage
            // arrives from the host — but a building the host has said is coming down has to
            // actually go over on this screen, and a cleared lot has to be swept.
            UpdateStructures(dt);
            // The continuous beds. These were only ever driven from the host's authoritative
            // path, so a client stood underneath a stalking Crab-Core heard nothing but its
            // one-shots — the rotor, the single loudest thing about the encounter, was silent
            // on every machine but one. Driven here off the puppets the snapshot installs.
            DriveMonsterBeds();
            AgeRemoteRigs(dt);
            AgeMarkers(dt);
            Debris.Update(dt);
            PickSpectatorSeat();
            return;
        }

        // The virus is seat-aware: the host drains every seat's mote, not just its own, so a
        // remote player's infection, withering and ejection all actually happen on the machine
        // that owns the simulation — the whole reason the class was inert for anyone but the
        // host. A dropped seat is frozen (not stepped), exactly like the physics loop above.
        for (int i = 0; i < Players.Count; i++)
            if (!Players[i].Away && Players[i].Virus is { } payload)
                UpdateVirusEvents(payload, Players[i]);

        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            // Hand the hunter the player's height so it can elevate onto a craft off the
            // grid; the pitch it hands back rides the enemy round up the same way the
            // tank's cannon climbs on its own aim. A firing solution that has to cross a
            // TANK's screening smoke is simply lost — the hunter still spends its cooldown,
            // so a laid screen reads as the field going half-blind rather than pausing.
            // Each hunter picks the craft nearest to itself rather than all twenty of them
            // converging on one player. Chosen per-tick, so a hunter switches to whoever
            // drives into its lap the moment they do.
            PlayerTank quarry = NearestPlayer(e.Position);
            if (e.Update(dt, quarry.Position, quarry.Height,
                out Vector2 eOrigin, out Vector2 eDir, out float ePitch)
                && !SmokeBlocks(e.Position, quarry.Position))
                SpawnProjectile(eOrigin, eDir, owner: Projectile.NoOwner, pitch: ePitch);
        }

        // The soldier squads, stepped after the hunters and before anything that reads
        // where a body ended up: they move furthest in a tick of anything on the field, and
        // a blade pass scored against last tick's position would be a blade pass scored
        // against thin air.
        UpdateSquads(dt);

        // The tank's ram: with the enemies stepped to their spots this tick, a hull that is
        // genuinely driving into one crushes it. Runs before the projectile pass so a shoved
        // hunter is already clear when its own shot is resolved.
        UpdateRam();

        // The boss stalks on its own clock; a true return means the carapace just
        // slammed shut this tick, so sound the bit-crushed CLANG. Wherever a leg
        // planted this tick, kick up a puff of grid dust under the foot — and thud
        // out a stomp, mixed by how close the nearest planting foot is to the
        // player so the gait swells as the crab closes in and is a faint tremor
        // while it's still stalking across the arena.
        // The seizure runs before the boss does, because while one is live it owns
        // the player's transform and the boss is frozen in the hold — stepping them
        // the other way round would let the crab chase a position the cinematic is
        // about to overwrite.
        UpdateSeizure(dt);

        if (Boss is { } boss)
        {
            // The crab stalks whoever is closest to it. Its footfalls and hum below are
            // still measured against the *local* craft, and deliberately so: those are
            // things this machine's player hears, not things the boss does.
            if (boss.Update(dt, NearestPlayer(boss.Position).Position))
                Emit(Cue.Clamp, boss.Position);
            // Every planting foot is voiced on its own — a tripod lands together, so this is
            // three overlapping impacts, each with its own limb's pitch and its own distance
            // to the listener, wherever they are.
            foreach (var f in boss.Footfalls)
            {
                Debris.FootPuff(new Vector3(f.Pos.X, 0f, f.Pos.Y));
                Emit(Cue.Footstep, f.Pos, f.Leg);
            }

            // The one-shots the boss raised this tick — its alarm and the beam's charge, warning
            // and fire. Voiced from here, where there is a world to broadcast them, so a client
            // hears the beam spooling up over its shoulder and not only the host does.
            foreach (var (id, param) in boss.Cues)
                Emit(id, boss.Position, param);

            // Its machinery hums the whole time it exists, spooling up the moment it
            // notices the player. Fed every tick; Audio eases the rate and level. Measured
            // from whichever craft the camera is riding, so a spectator hears the fight they
            // are watching rather than the silence around their own wreck.
            Audio.SetBossHum(true, boss.Position, boss.Agitation);

            UpdateBeam(boss, dt);

            // Once the death glitch has fully torn the rig apart, drop the boss so it
            // stops updating and the director is free to raise a new one later.
            if (boss.Dead) Boss = null;
        }
        else
        {
            // No boss on the field — let the rotor fade out and stop.
            Audio.SetBossHum(false, Vector2.Zero, 0f);
        }

        // The hanging mouth runs on the same pattern: its set piece steps first,
        // because while one is live it owns the player's transform and the monster is
        // frozen around them.
        UpdateDigestion(dt);
        UpdateMaw(dt);

        // Everything has moved. File where it all stands before a single round is resolved
        // against it, so a client's shot can be scored against the world that client was
        // actually looking at when they pulled the trigger.
        RecordRewind();

        UpdateProjectiles(dt);
        UpdateCrabBlasts(dt);
        UpdateSmoke(dt);
        UpdateStructures(dt);
        UpdatePickups(dt);
        // The director tops up hunters, bosses, maws and squads — everything hostile. A host
        // who turned enemies off in the lobby keeps the city and the salvage but never the fight.
        if (DynamicSpawning && Match.SpawnEnemies) UpdateSpawning(dt);
        AgeMarkers(dt);
        Debris.Update(dt);
        // After the debris has moved this tick, bill any falling structural chunk that came
        // down on a character. Run before the dead are swept so a hunter crushed this tick is
        // removed with the rest rather than lingering a frame.
        ResolveCrush(dt);
        // Nothing goes to the sweep still in a hand. A catch can be finished by anything at
        // all while it is up there — the squeeze, a stray round, a building coming down on
        // it — and the claw must not be left holding a reference to a hull the world has
        // stopped believing in.
        foreach (var p in Players)
            if (p.Claw is { Victim.Alive: false } claw) claw.Drop();
        Enemies.RemoveAll(e => !e.Alive);
        Soldiers.RemoveAll(s => !s.Alive);
        Squads.RemoveAll(sq => !sq.Alive);
        PickSpectatorSeat();
    }

    // --- Spectating ---------------------------------------------------------------

    /// <summary>
    /// The seat the camera is looking out of. Normally this machine's own; once its revives
    /// are spent and it is <see cref="PlayerTank.Spectating"/>, a living team-mate's instead.
    ///
    /// A player out of the match used to be left staring out of their own dead craft, which
    /// is where a run ended for them whether or not their team was still fighting. The revive
    /// model is what makes that wrong: being spent is meant to send you to <em>watch the
    /// survivors</em>, and there was nothing in the loop that pointed the camera at one.
    /// </summary>
    public int ViewSeat { get; private set; }

    /// <summary>The craft the camera rides. The same as <see cref="Player"/> except while
    /// spectating, which is exactly the distinction the renderer wants and the HUD does
    /// not — the bars stay this player's own.</summary>
    public PlayerTank Eye => Players[(uint)ViewSeat < (uint)Players.Count ? ViewSeat : LocalIndex];

    /// <summary>True while the camera is riding somebody else's craft.</summary>
    public bool Spectating => ViewSeat != LocalIndex;

    /// <summary>
    /// Steps the camera to the previous or next living team-mate.
    ///
    /// <para>The picker below is sticky by design — it will not cut away from whoever you are
    /// watching while they are alive — which is right for a camera nobody is steering and
    /// wrong the moment somebody wants to steer it. Spending your last revive and then being
    /// welded to one player until they die is not watching a match, it is waiting for one.
    /// This is the steering: the sticky rule then holds the new choice exactly as it held the
    /// old one, and a revive still snaps you home.</para>
    /// </summary>
    public void CycleSpectator(int dir)
    {
        if (!Players[LocalIndex].Spectating || Players.Count <= 1) return;

        int n = Players.Count;
        int step = dir >= 0 ? 1 : -1;
        for (int i = 1; i <= n; i++)
        {
            int seat = ((ViewSeat + step * i) % n + n) % n;
            if (seat == LocalIndex) continue;
            if (Players[seat].Spectating || Players[seat].Away) continue;
            ViewSeat = seat;
            // Chirp from the craft we have just moved to, so the switch is confirmed even on
            // a screen where two team-mates are standing in similar-looking streets.
            Emit(Cue.Marker, Players[seat].Position, owner: LocalIndex);
            return;
        }
    }

    /// <summary>
    /// Settles which craft the camera rides, once per step. Sticky: a spectator stays with
    /// whoever they were watching until that player is spent too, because a camera that
    /// re-picked the nearest survivor every tick would cut between team-mates driving past
    /// each other. Falls back to this machine's own craft the moment it is alive again — a
    /// revive puts the player straight back behind their own eyes.
    /// </summary>
    private void PickSpectatorSeat()
    {
        if ((uint)LocalIndex >= (uint)Players.Count) { ViewSeat = 0; return; }

        if (!Players[LocalIndex].Spectating) { ViewSeat = LocalIndex; return; }

        // Still watching somebody worth watching.
        if (ViewSeat != LocalIndex && (uint)ViewSeat < (uint)Players.Count
            && !Players[ViewSeat].Spectating && !Players[ViewSeat].Away)
            return;

        // Otherwise take the nearest survivor, so the first thing a spent player sees is the
        // fight they just fell out of rather than whoever happens to hold seat one.
        int best = LocalIndex;
        float nearest = float.MaxValue;
        Vector2 from = Players[LocalIndex].Position;
        for (int i = 0; i < Players.Count; i++)
        {
            if (i == LocalIndex || Players[i].Spectating || Players[i].Away) continue;
            float d = Torus.DistanceSquared(Players[i].Position, from);
            if (d >= nearest) continue;
            nearest = d;
            best = i;
        }
        ViewSeat = best;
    }

    /// <summary>
    /// Feeds the two monsters' continuous beds — the crab's rotor and the maw's hover. Split
    /// out of the host's step so a client can drive the same beds off the render-only puppets
    /// its snapshots install, which is the whole of why the encounters were silent off the
    /// host's machine. Measured against the craft the camera is actually riding, so a
    /// spectator hears the fight they are watching.
    /// </summary>
    private void DriveMonsterBeds()
    {
        if (Boss is { } b) Audio.SetBossHum(true, b.Position, b.Agitation);
        else Audio.SetBossHum(false, Vector2.Zero, 0f);

        if (Maw is { } m) Audio.SetMawHover(true, m.Position, m.Agitation);
        else Audio.SetMawHover(false, Vector2.Zero, 0f);
    }

    // --- Acoustics --------------------------------------------------------------
    // Everything the sound engine needs to know about this world, once a frame: where the
    // ears are, what is standing around them, and what the player is currently inside of.
    // The engine itself knows nothing about tanks — this is the whole of the binding.

    /// <summary>How long the jump keeps hold of the mix after a hyperspace. Short: it is a
    /// smear, not a place, and the craft is already out the other side.</summary>
    private const float JumpEchoTime = 0.55f;
    private float _jumpEcho;

    /// <summary>Called when a craft folds space. Only the craft the camera is riding gets
    /// the mix treatment — a team-mate jumping across the arena is their business.</summary>
    public void NoteHyperspace(PlayerTank who)
    {
        if (ReferenceEquals(who, Eye)) _jumpEcho = JumpEchoTime;
    }

    /// <summary>
    /// Points the listener at whatever the camera is riding and tells the engine what kind
    /// of space it is standing in.
    ///
    /// <para>Measured against <see cref="Eye"/> rather than <see cref="Player"/> throughout,
    /// which matters the moment revives run out: a spectator rides a team-mate's shoulder,
    /// and their ears have to go with them rather than staying behind on their own wreck.</para>
    /// </summary>
    private void DriveListener(float dt)
    {
        if (_jumpEcho > 0f) _jumpEcho -= dt;

        // The engine asks this world about its city. Rebound rather than assigned once in
        // the constructor, because the engine is a singleton and the world is per-match: the
        // stage that is actually running has to be the one answering, and the one that ended
        // has to stop being held alive by a delegate nobody cleared.
        _occlusionProbe ??= MeasureOcclusion;
        if (!ReferenceEquals(Audio.Engine.OcclusionProbe, _occlusionProbe))
            Audio.Engine.OcclusionProbe = _occlusionProbe;

        PlayerTank eye = Eye;
        Audio.Listen(eye.Position, eye.Height + eye.EyeHeight, eye.Heading, eye.Pitch);

        var room = Audio.Room;
        MeasureRoom(eye.Position, out float enclosure, out float openness);
        room.Enclosure = enclosure;
        room.Openness = openness;

        // Being somewhere overrides being anywhere. Order matters: a craft in a mouth is in
        // a mouth whatever else is true of the grid it was standing on.
        room.Where =
            _jumpEcho > 0f ? Interior.Hyperspace
            : SwallowedIn(eye) is { Held: true } ? Interior.Swallowed
            : HeldInClaw(eye) ? Interior.Seized
            : Interior.Open;

        Audio.SetIntensity(MeasureIntensity(eye));
    }

    /// <summary>
    /// How hard the fight is around the listener, 0..1. The soundtrack rides this: an empty
    /// grid and a boss fight should not sound the same in more than the gunfire.
    ///
    /// <para>Counted rather than tracked, on purpose. A tally of what is actually standing
    /// nearby cannot drift out of step with the fight the way a score that things remember
    /// to increment can, and it costs a walk of two short lists.</para>
    /// </summary>
    private float MeasureIntensity(PlayerTank eye)
    {
        const float Near = 90f;
        float heat = 0f;

        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            if (Torus.DistanceSquared(e.Position, eye.Position) < Near * Near) heat += 0.09f;
        }
        foreach (var s in Soldiers)
            if (Torus.DistanceSquared(s.Position, eye.Position) < Near * Near) heat += 0.05f;

        // A boss on the field is most of the answer by itself, and one that has noticed
        // somebody is the rest of it.
        if (Boss is { } boss && Torus.DistanceSquared(boss.Position, eye.Position) < 160f * 160f)
            heat += 0.35f + 0.3f * boss.Agitation;
        if (Maw is { } maw && Torus.DistanceSquared(maw.Position, eye.Position) < 160f * 160f)
            heat += 0.35f + 0.3f * maw.Agitation;

        // Being in something's grip is as loud as the fight gets.
        if (HeldByAnything(eye)) heat = 1f;

        // Nearly dead counts too. It is not more enemies, but it is more at stake.
        if (eye.Alive && eye.MaxShield > 0f) heat += 0.25f * (1f - eye.Shield / eye.MaxShield);

        return Math.Clamp(heat, 0f, 1f);
    }

    private bool HeldByAnything(PlayerTank who)
        => HeldInClaw(who) || SwallowedIn(who) is { Held: true };

    /// <summary>How far out a building still counts toward the size of the space you are
    /// standing in. Roughly a city block: past it a tower is scenery, not a wall.</summary>
    private const float RoomRadius = 34f;

    /// <summary>
    /// Two numbers describing the space around a point: how much is standing near it, and
    /// how much room there is between what is standing. That second one is what separates a
    /// plaza — walls at a distance, sky above, a long bright tail — from a gap between two
    /// towers, which is close, dark and dead.
    /// </summary>
    private void MeasureRoom(Vector2 at, out float enclosure, out float openness)
    {
        float mass = 0f, nearest = float.MaxValue;
        foreach (var s in Structures)
        {
            if (s.Falling) continue;
            float d = Torus.Distance(s.Position, at);
            if (d > RoomRadius) continue;
            // Weighted by how close it is and how big it is: a spire two blocks away is not
            // the same wall as a bank of towers you could touch.
            mass += (1f - d / RoomRadius) * s.Scale;
            if (d < nearest) nearest = d;
        }

        enclosure = Math.Clamp(mass / 3.2f, 0f, 1f);
        // Right up against something, the space closes over you. Out in the open it never
        // does, however many towers are visible on the skyline.
        openness = nearest >= RoomRadius ? 1f : Math.Clamp((nearest - 4f) / 14f, 0f, 1f);
    }

    /// <summary>
    /// How much city stands between the listener and a sound, 0 clear to 1 buried. Handed to
    /// the engine as a delegate; it calls this for a few voices a frame and eases the answer,
    /// so the cost stays flat however loud the field gets.
    ///
    /// <para>Measures the actual chord of material the line passes through rather than
    /// counting hits, so clipping the corner of one tower muffles a shot far less than
    /// driving it through the middle of three. A line that clears the roofs is not blocked at
    /// all — which is the whole reason a soldier up on the skyline can hear the fight below
    /// that a tank in the street cannot.</para>
    /// </summary>
    public float MeasureOcclusion(Vector2 from, float fromHeight, Vector2 to, float toHeight)
    {
        Vector2 span = Torus.Delta(from, to);
        float len = span.Length();
        if (len < 0.5f) return 0f;
        Vector2 dir = span / len;

        float material = 0f;
        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];

        foreach (var s in Structures)
        {
            // A structure already coming down blocks nothing — the same rule the collision
            // passes use, so what you can drive through you can also hear through.
            int n = s.Blockers(blockers);
            if (n == 0) continue;

            float roof = s.BlockHeight;
            for (int i = 0; i < n; i++)
            {
                Vector2 rel = Torus.Delta(from, blockers[i].At);
                float along = Vector2.Dot(rel, dir);
                if (along <= 0f || along >= len) continue;          // behind us, or past the source

                float radius = blockers[i].Radius;
                float perp = (rel - dir * along).Length();
                if (perp >= radius) continue;                       // the line misses it

                // How high the line is where it crosses. Over the parapet and it is clear.
                float height = fromHeight + (toHeight - fromHeight) * (along / len);
                if (height > roof) continue;

                material += 2f * MathF.Sqrt(MathF.Max(0f, radius * radius - perp * perp));
            }
        }

        // One tower's worth of stone is a complete block. Two thin clips of masonry add up
        // to most of one, which is right: they are both walls.
        return Math.Clamp(material / OcclusionFullBlock, 0f, 1f);
    }

    /// <summary>Metres of material that count as fully occluded — about one tower.</summary>
    private const float OcclusionFullBlock = 13f;

    /// <summary>
    /// Emits whatever an entity named this tick and empties its buffer.
    ///
    /// <para>The bosses and the two cinematics have no world reference, so every noise they
    /// made used to go straight at the audio device — heard on the machine running the
    /// simulation and on no other. A team-mate being lifted out of the grid by a claw, or
    /// ground up inside a mouth, was completely silent to the nineteen people watching it.
    /// Draining a named buffer instead puts all of it through <see cref="Emit"/>, and so on
    /// the wire.</para>
    /// </summary>
    private void DrainCues(IReadOnlyList<EntityCue> cues, Action clear)
    {
        if (cues.Count == 0) return;
        foreach (var c in cues) Emit(c.Id, c.At, c.Param);
        clear();
    }

    private Func<Vector2, float, Vector2, float, float>? _occlusionProbe;

    /// <summary>Cues heavy enough to ring the player's ears if they go off nearby. The list
    /// is the point: a rifle shot beside you is loud, but it does not knock the world out
    /// from under you, and treating it as if it did would make the effect meaningless.</summary>
    private static bool IsConcussive(Cue id)
        => id is Cue.Explosion or Cue.RocketBlast or Cue.CrabCoreBlast or Cue.BeamFire
              or Cue.BossDeath or Cue.ClawSlam or Cue.ExplosionAt;

    /// <summary>
    /// A blast this machine can hear rings its ears and kicks its view.
    ///
    /// <para>Both used to be raised where the blast was resolved, which is host-only code —
    /// so a client standing beside a detonation neither felt it nor was deafened by it, and a
    /// spectator felt their own corpse's blasts rather than the ones landing around the craft
    /// they were watching. Hanging it off the cue instead means it is driven by exactly what
    /// the player can hear, wherever they are and whichever machine they are on.</para>
    /// </summary>
    private void ShakeFromBlast(float range)
    {
        Audio.Room.Blast(range);
        if (range >= RocketShakeRange) return;
        JoltPlayerView(Eye, 0.4f * (1f - range / RocketShakeRange));
    }

    // --- The skyline ------------------------------------------------------------

    /// <summary>
    /// The dead alien city standing on this torus — towers and the arcs slung between
    /// them. The layout is a fixed feature of the world: the same buildings stand in the
    /// same places in every run (see <see cref="StructureField"/>), which is what makes
    /// them landmarks instead of scenery. The <em>instances</em> belong to this stage,
    /// because they can be cut down.
    /// </summary>
    public readonly List<Structure> Structures = StructureField.Create();

    /// <summary>
    /// Every building this stage was laid out with, by <see cref="Structure.Index"/> and
    /// including the ones since razed. <see cref="Structures"/> is the <em>live</em> field and
    /// loses entries as lots are cleared; this is the directory the wire addresses, so a host
    /// saying "number 41 is gone" can still be understood on a machine that has already swept
    /// it (and is then correctly ignored).
    /// </summary>
    private readonly Structure[] _byIndex;

    /// <summary>
    /// Buildings that have finished coming down and left <see cref="Structures"/>. Kept so the
    /// host keeps <em>telling</em> clients about them: a razed lot is a fact about the world
    /// that a client which was across the map at the time has still never heard, and the
    /// structure packet is keep-last, so it has to stay in the outgoing set to reach them.
    /// Host-side; bounded by the size of the city.
    /// </summary>
    private readonly List<Structure> _razed = new();

    /// <summary>The lots this world has cleared, for the snapshot writer to keep mentioning.
    /// See <see cref="_razed"/> for why they cannot simply be forgotten.</summary>
    public IReadOnlyList<Structure> RazedStructures => _razed;

    /// <summary>
    /// Client-side: lays the host's account of one building over the local copy. Throws the
    /// rubble for whatever cells died since the last packet, so a tower another player cut
    /// down comes apart on this screen with the same shower of masonry rather than silently
    /// losing its middle. Unknown or already-swept indices are ignored.
    /// </summary>
    public void AdoptStructure(int index, bool falling, bool gone, bool fractured,
        ReadOnlySpan<byte> mask)
    {
        if ((uint)index >= (uint)_byIndex.Length) return;
        Structure s = _byIndex[index];
        if (s.Gone) return;   // already finished with here; the host is just still saying so

        _detached.Clear();
        bool wasFalling = s.Falling;
        s.NetApply(falling, gone, fractured, mask, _detached);

        int spawned = 0;
        foreach (var d in _detached)
        {
            Debris.RubbleChunk(d.Pos, d.Size, d.Color);
            if (++spawned >= MaxChunksPerCut) break;
        }

        // The dust and the report, once, on the frame this machine first learns the mass is
        // going. The cells' own rubble above covers a cut; this covers the collapse.
        if (!wasFalling && s.Falling)
        {
            float top = s.BlockHeight;
            for (int i = 1; i <= 3; i++)
                Debris.Burst(new Vector3(s.Position.X, top * (i / 4f), s.Position.Y),
                    i == 1 ? Palette.StructureGlow : Palette.StructureShell, elite: i == 3);
            Emit(Cue.StructureGroan, s.Position);
        }
        else if (spawned > 0)
        {
            Emit(Cue.StructureCrack, s.Position);
        }
    }

    /// <summary>
    /// Ages any collapse in progress and lets the finished ones go. The tick a mass
    /// lands, it throws dust off the grid where it came down and thuds at whatever
    /// volume the distance earns. Run on clients too — a client is told a building is
    /// <em>coming down</em>, not sixty frames of it coming down, and plays the topple out on
    /// its own clock from there.
    /// </summary>
    private void UpdateStructures(float dt)
    {
        for (int i = Structures.Count - 1; i >= 0; i--)
        {
            var s = Structures[i];
            if (!s.Falling) continue;

            // Towers come apart into chunks at the moment they're cut (their falling pieces
            // are debris the world already steps), so Update is a no-op on them and this block
            // is only ever an arch going over as one piece. A felled tower simply waits here to
            // be let go once its last cell is gone (Gone, below).
            if (s.Update(dt))
            {
                // The impact: a long spray of dust down the line the span fell along, rather
                // than one puff at the middle, since the thing that just hit the grid is a
                // whole arc of it.
                var along = new Vector2(MathF.Cos(s.Heading), -MathF.Sin(s.Heading));
                for (int k = 1; k <= 3; k++)
                    Debris.Burst(new Vector3(
                        s.Position.X + along.X * s.BlockHeight * 0.25f * k, 0.5f,
                        s.Position.Y + along.Y * s.BlockHeight * 0.25f * k),
                        Palette.StructureShell, elite: false);

                // And the mass itself coming down: a wave of crushing rubble strewn along
                // the same line, in sections heavy enough to kill whatever was slow to clear
                // out of the topple's path. This is the payoff of the groan that warned it.
                Debris.Collapse(new Vector3(s.Position.X, 0.6f, s.Position.Y), along,
                    s.BlockHeight * 0.75f, Palette.StructureShell, s.Scale);

                Emit(Cue.ExplosionAt, s.Position);
            }

            if (!s.Gone) continue;
            Structures.RemoveAt(i);
            // A cleared lot still has to be broadcast — a client that was on the far side of
            // the world when it fell has never been told, and the packet is keep-last.
            if (Authoritative) _razed.Add(s);
        }
    }

    /// <summary>
    /// Cuts into whatever standing structures a beam passes through, dealing
    /// <paramref name="structureDamage"/> to each — scaled by how squarely the shaft strikes
    /// it (dead on the axis lands full, a graze at the edge barely scratches) — and staging
    /// the collapse on any that this bite finally fails. A bite that leaves it standing
    /// throws localized chips off the exact point struck, the visible carving that precedes
    /// the topple; multiple bites on the same spot compound toward the rupture.
    ///
    /// Only beams do this. A round of any kind — the cannon, the heavy grenade, an
    /// enemy's shot, the SPIDER's laser bolt — stops dead against a wall and leaves it
    /// standing (see <see cref="BlockShotOnStructure"/>). The distinction is the whole
    /// point of the buildings: they are cover, and cover you can shoot through is not
    /// cover. What defeats them is the thing that was never a bullet in the first place —
    /// the Crab-Core's lance, the SPIDER's charged beam, the ring thrown off a detonating
    /// CRAB CORE — and a wall coming down under one of those should be worth watching. A
    /// decisive siege beam carries far more than any tower's integrity in a single bite, so
    /// it fells on contact as it always did; only a dwelling beam is billed by attrition.
    ///
    /// The whole shaft is swept rather than stopping at the first hit, because a beam
    /// that has already cut through one tower has visibly not stopped, and a second wall
    /// standing untouched in the same light would read as a bug.
    /// </summary>
    /// <returns>How many structures this bite felled.</returns>
    private int CutStructuresAlong(Vector3 origin, Vector3 direction, float length, float radius,
        float structureDamage)
    {
        var originXZ = new Vector2(origin.X, origin.Z);
        var dirXZ = new Vector2(direction.X, direction.Z);
        float planar = dirXZ.Length();
        if (planar > 1e-4f) dirXZ /= planar;
        // How fast the shaft climbs per unit of ground covered — a beam loosed from the
        // top of a jump sails over the low legs of an arc, and should.
        float slope = planar > 1e-4f ? direction.Y / planar : 0f;

        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        int cut = 0;

        foreach (var s in Structures)
        {
            // A structure already coming down yields no blockers, so a beam sweeping the
            // wreck neither re-fells it nor showers fresh chips off a corpse.
            int n = s.Blockers(blockers);
            for (int b = 0; b < n; b++)
            {
                var (at, r) = blockers[b];
                Vector2 near = Torus.NearestImage(at, originXZ);
                float along = Math.Clamp(Vector2.Dot(near - originXZ, dirXZ), 0f, length);
                float miss = Vector2.Distance(near, originXZ + dirXZ * along);
                if (miss > r + radius) continue;

                // Passing overhead is passing overhead, however wide the shaft is.
                float beamY = origin.Y + slope * along;
                if (beamY > s.BlockHeight + radius) continue;

                // Radial falloff from the shaft's axis: full on centre, tapering to nothing at
                // the edge of its reach, so an edge graze bites less than a square hit. Where
                // on the mass it struck — the footprint centre nearest the beam, at the beam's
                // height clamped to the body — is the point the tower comes apart around.
                float falloff = Math.Clamp(1f - miss / (r + radius), 0f, 1f);
                float impactY = Math.Clamp(beamY, 0.4f, s.BlockHeight);
                var impact = new Vector3(near.X, impactY, near.Y);

                if (s.Kind == StructureKind.Arch)
                {
                    // An arch has no chunk model — a beam takes the whole span down.
                    if (FellStructure(s, impact)) cut++;
                }
                else
                {
                    // A tower comes apart where it is cut. The impact pocket reaches a little
                    // wider than the shaft so a bite clears a believable hole, and a square low
                    // hit takes out the whole base cross-section (so a full lance still fells a
                    // tower outright) while a hit high up only lets the crown go.
                    _detached.Clear();
                    bool felled = s.CutTower(impact, radius + BeamImpactMargin,
                        structureDamage * falloff, _detached, out bool newlyFractured);
                    EmitTowerFractureDebris(s, impact, _detached, newlyFractured, felled);
                    if (felled) cut++;
                }
                break;   // one strike per structure, whichever leg it was
            }
        }

        return cut;
    }

    /// <summary>Reused scratch list of the cells a cut knocks loose, so the beam passes
    /// allocate nothing per tick. Never held across calls.</summary>
    private readonly List<DebrisSpawn> _detached = new();

    /// <summary>How much wider than the beam's own shaft a cut's impact pocket reaches into a
    /// tower — the "~2–5m impact radius". Wide enough that a bite clears a believable pocket
    /// and a square low hit clears the whole base cross-section, narrow enough that a hit high
    /// up leaves the base standing.</summary>
    private const float BeamImpactMargin = 5f;

    /// <summary>Cap on how many loosed cells one cut turns into debris in a single frame, so a
    /// whole tower coming apart at once can't swamp the shared debris pool.</summary>
    private const int MaxChunksPerCut = 40;

    /// <summary>
    /// Dresses a tower cut: throws the cells knocked loose as crushing rubble where they stood,
    /// a dust flash to mask the first frame's swap from the intact mesh to the chunk model, and
    /// the right report — a groan when a support fails and the mass starts down, otherwise a
    /// crack of masonry shearing (played only on ticks a cell actually broke, so a dwelling beam
    /// reads as stone breaking rather than one clip stuttering at sixty hertz).
    /// </summary>
    private void EmitTowerFractureDebris(Structure s, Vector3 impact,
        List<DebrisSpawn> detached, bool newlyFractured, bool felled)
    {
        float dist = Torus.Distance(new Vector2(impact.X, impact.Z), Player.Position);

        if (newlyFractured)
            Debris.Burst(impact, Palette.StructureShell, elite: false);

        int spawned = 0;
        foreach (var d in detached)
        {
            Debris.RubbleChunk(d.Pos, d.Size, d.Color);
            if (++spawned >= MaxChunksPerCut) break;
        }

        var hit = new Vector2(impact.X, impact.Z);
        if (felled)
        {
            Emit(Cue.StructureGroan, hit);
            Emit(Cue.ExplosionAt, hit);
        }
        else if (detached.Count > 0 && Random.Shared.NextSingle() < 0.5f)
        {
            Emit(Cue.StructureCrack, hit);
        }
    }

    /// <summary>Integrity a dwelling enemy beam chews per second — a scale-1 tower's cells
    /// take a few tenths of a second of steady contact to shear, sloughing chunks the whole
    /// way, rather than a section vanishing the instant the shaft grazes it.</summary>
    private const float CrabBeamStructureRate = 200f;

    /// <summary>What a charged siege lance deals to a tower's cells in its one synchronous
    /// bite: far more than any cell can soak, so every block inside the impact pocket lets go
    /// at once — a square low hit clears the base cross-section and the whole tower comes
    /// down, which is the decisive collapse a full lance always bought.</summary>
    private const float LanceStructureDamage = 100000f;

    /// <summary>
    /// Starts a structure's collapse and dresses it: chunks blown off the mass at three
    /// heights up its body, so a tall thing comes apart down its length rather than
    /// popping at the base, and the report of it carrying to wherever the player is.
    /// Silently does nothing to something already on its way down.
    /// </summary>
    private bool FellStructure(Structure s)
        => FellStructure(s, new Vector3(s.Position.X, s.BlockHeight * 0.5f, s.Position.Y));

    /// <summary>
    /// As <see cref="FellStructure(Structure)"/>, but told where on the mass the killing blow
    /// landed so the rupture erupts there — heavier, crushing rubble blown off the exact
    /// strike point, so a wall cut down beside a hunter buries it then and there rather than
    /// only dressing the base. A groan sounds the support failing, the warning that the
    /// ~1.6s topple has begun.
    /// </summary>
    private bool FellStructure(Structure s, Vector3 impact)
    {
        _detached.Clear();
        if (!s.Strike(_detached)) return false;

        float top = s.BlockHeight;
        for (int i = 1; i <= 3; i++)
            Debris.Burst(new Vector3(s.Position.X, top * (i / 4f), s.Position.Y),
                i == 1 ? Palette.StructureGlow : Palette.StructureShell, elite: i == 3);

        // A detonated tower throws its own cells as crushing rubble where they stood.
        int spawned = 0;
        foreach (var d in _detached)
        {
            Debris.RubbleChunk(d.Pos, d.Size, d.Color);
            if (++spawned >= MaxChunksPerCut) break;
        }
        // An arch has no cells; give it a burst of rubble off the strike point instead so a
        // felled span still rains something that can crush.
        if (s.Kind == StructureKind.Arch)
            Debris.Rubble(impact, Palette.StructureShell, chunks: 6, scale: s.Scale);

        Emit(Cue.StructureGroan, s.Position);
        Emit(Cue.ExplosionAt, s.Position);
        return true;
    }

    /// <summary>
    /// Stops a round against the skyline. Returns true if the shot struck a wall, having
    /// already spent it: the projectile is killed, a scatter of masonry is thrown off the
    /// point of impact and the hit is heard at whatever the range earns.
    ///
    /// Height is honoured, which is what makes an arc worth driving through: its legs are
    /// short, so a shot passes under the span the same way the craft does. Towers reach
    /// far higher than anything can shoot, so they simply stop everything.
    /// </summary>
    private bool BlockShotOnStructure(Projectile p)
    {
        // The AP slug punches through the skyline as readily as through a line of hunters —
        // going through cover is the whole point of it, so it is never blocked here.
        if (p.IsPiercing) return false;

        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];

        foreach (var s in Structures)
        {
            if (p.Height > s.BlockHeight) continue;

            int n = s.Blockers(blockers);
            for (int b = 0; b < n; b++)
            {
                var (at, r) = blockers[b];
                if (Torus.DistanceSquared(p.Position, at) > r * r) continue;

                // A thrown CRAB CORE is not a round — it is a bomb that happened to hit a
                // wall, so it does what it would have done anywhere else, and the ring of
                // lances it throws is perfectly capable of bringing the wall down.
                if (p.IsCrabBomb)
                {
                    StageCrabBlast(p.Position);
                }
                else if (p.IsRocket)
                {
                    // Neither is a rocket. Its contact fuse is exactly what a wall is for.
                    DetonateRocket(p);
                }
                else if (p.IsGrenade)
                {
                    // A mortar that clipped a tower it couldn't clear bursts against it — the
                    // same splash it would have thrown on the grid, just up the wall instead.
                    DetonateMortar(p);
                }
                else
                {
                    Debris.Burst(new Vector3(p.Position.X, MathF.Max(0.4f, p.Height), p.Position.Y),
                        Palette.StructureShell, elite: false);
                    Emit(Cue.ExplosionAt, p.Position);
                }

                p.Active = false;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Keeps the craft out of the solid parts of the skyline: a tower's footprint, an
    /// arc's two legs. Anything overlapped this tick is resolved by sliding the craft
    /// straight back out along the shortest way — which, because it never touches the
    /// player's speed or heading, reads as scraping along a wall rather than as being
    /// stopped by one, and can't leave the craft stuck inside geometry with nowhere to go.
    ///
    /// Run right after the player's own move, so nothing else this tick — aiming,
    /// shooting, a monster measuring the range — ever sees the craft inside a building.
    /// A captured player is skipped outright: a set piece owns the transform and is
    /// entitled to drag them through a wall if that is where the claw goes.
    ///
    /// Nothing else on the field collides with the city. Hunters drive through it, and
    /// so do rounds and beams — buildings are terrain to navigate, not cover, and giving
    /// the AI walls to path around is a much larger piece of work than this.
    /// </summary>
    private void ResolveStructureCollisions()
    {
        // Every craft this machine is responsible for. The host owns all twenty, so all
        // twenty are kept out of the walls; a client owns only its own prediction and lets
        // the host settle everyone else. Before this the pass ran on the local craft alone,
        // so on the host every REMOTE player drove clean through the city — and the host's
        // own snapshots then placed them inside towers on everybody's screen.
        for (int seat = 0; seat < Players.Count; seat++)
        {
            PlayerTank who = Players[seat];
            if (who.Away) continue;
            if (!Authoritative && seat != LocalIndex) continue;
            ResolveStructureCollisionsFor(who);
        }
    }

    private void ResolveStructureCollisionsFor(PlayerTank who)
    {
        if (who.Captured) return;

        // An exposed VIRUS mote passes straight through the city, and that is not a
        // convenience — it is the other half of being blind. A payload with no body cannot
        // see matter because it does not touch matter, and a player who has been told they
        // cannot see the walls must not then be stopped by them. Take a body and the world
        // becomes solid again in the same instant it becomes visible.
        if (who.Virus is { Exposed: true }) return;

        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];

        for (int i = 0; i < Structures.Count; i++)
        {
            Structure s = Structures[i];

            // Over the top of it is past it. This never mattered while the only thing on
            // the field was a craft whose whole jump peaks at eight units, but a soldier
            // spends most of the run above the height of an arch's legs, and being
            // shoved sideways by a wall thirty metres beneath them would be absurd.
            //
            // At the parapet exactly, the craft is standing on the roof rather than
            // pressed against the wall (see ResolveRoofs, which put it there a moment ago)
            // — so the test has to include its own height, or a SPIDER that has just
            // landed on a tower is immediately shoved off the side of it.
            if (who.Height >= s.BlockHeight) continue;

            int n = s.Blockers(blockers);
            for (int b = 0; b < n; b++)
            {
                var (at, radius) = blockers[b];
                float reach = radius + PlayerTank.Radius;

                // Measured the short way round the torus, so a building sitting over the
                // seam blocks the craft that has just wrapped past it.
                Vector2 out_ = Torus.Delta(at, who.Position);
                float distSq = out_.LengthSquared();
                if (distSq >= reach * reach) continue;

                // Dead centre (only reachable if something teleported the craft into a
                // pylon): shove it out along a fixed axis rather than dividing by zero.
                Vector2 push = distSq > 1e-6f
                    ? out_ / MathF.Sqrt(distSq) * reach
                    : new Vector2(reach, 0f);

                who.Position = Torus.Wrap(at + push);

                // For a soldier this is not a scrape but a possible crash: meeting a wall
                // at speed is the way this chassis dies, and the rig decides which of the
                // two just happened from the momentum it was carrying. A fish threading a
                // reef is the same bargain at a lower threshold — it has no armour and the
                // whole run is spent at speed between buildings.
                who.Rig?.RegisterWallHit();
                who.Fish?.RegisterWallHit();
            }
        }
    }

    /// <summary>What standing in the Crab-Core's beam costs, per damage tick.</summary>
    private const float BeamDamage = 9f;

    /// <summary>
    /// How often the beam bites while the player is inside it. Ticked rather than
    /// applied per-frame for two reasons: the damage stops depending on the frame
    /// rate, and each bite fires the hit cue, which at 60Hz would be a solid tone
    /// rather than the sound of being hurt repeatedly.
    /// </summary>
    private const float BeamTickInterval = 0.35f;

    /// <summary>One bite clock per seat. Shared, the first craft the beam touched would soak
    /// the tick and everyone else standing in the same shaft would be spared it.</summary>
    private readonly float[] _beamTick = new float[MatchSettings.MaxSeats];

    /// <summary>
    /// Applies the Crab-Core's beam to every craft standing in it: while it is burning,
    /// anything inside the shaft takes a bite every <see cref="BeamTickInterval"/>.
    ///
    /// The test is a plain point-to-ray distance in 3D, which is exactly what the
    /// renderer draws — so what looks like standing in the light is standing in the
    /// light. Because the boss locked its direction before firing, the player's own
    /// movement is the entire defence: walk out of the line and the beam keeps
    /// burning empty grid for the rest of its five seconds.
    /// </summary>
    private void UpdateBeam(CrabCore boss, float dt)
    {
        if (!boss.BeamActive)
        {
            // Reset between shots so stepping into a fresh beam bites immediately
            // rather than on whatever was left of the last one's clock.
            Array.Clear(_beamTick);
            return;
        }

        // Whatever the lance is standing in, it cuts down. Swept every tick it burns rather
        // than once when it lights: the shaft is fixed, but a dwelling beam now chews through
        // integrity rather than felling on the first touch, so it chips a tower for a
        // fraction of a second before it goes — and the moment one falls out of the way, the
        // sweep reaches the next in line and starts on that.
        CutStructuresAlong(boss.BeamOrigin, boss.BeamDirection, CrabCore.BeamLength,
            CrabCore.BeamRadius, CrabBeamStructureRate * dt);

        Vector3 from = boss.BeamOrigin;
        Vector3 dir = boss.BeamDirection;

        // Every craft the shaft passes through, not just the one at this keyboard. A beam is
        // a line burning across the world and anybody standing in it should be burning too —
        // measured against seat 0 alone, nineteen other players could walk straight down the
        // middle of it and feel nothing. The tick clock is per seat for the same reason the
        // bloom's is: one shared countdown means the first player the beam touches soaks the
        // damage and everyone behind them is spared until it comes round again.
        for (int seat = 0; seat < Players.Count; seat++)
        {
            PlayerTank mark = Players[seat];
            _beamTick[seat] -= dt;
            if (mark.Away || !mark.Alive) continue;

            // Measure the craft against the boss's nearest image across the torus, so a
            // beam fired near the world's edge still burns the craft standing just over it.
            Vector2 near = Torus.NearestImage(mark.Position, boss.Position);
            var target = new Vector3(near.X, mark.Height + 1f, near.Y);

            // Distance from the craft to the beam's axis, clamped to the shaft's own
            // length so the ray doesn't reach backwards out of the emitter.
            float along = Math.Clamp(Vector3.Dot(target - from, dir), 0f, CrabCore.BeamLength);
            float miss = Vector3.Distance(target, from + dir * along);

            if (miss > CrabCore.BeamRadius + PlayerTank.Radius) continue;
            if (_beamTick[seat] > 0f) continue;

            _beamTick[seat] = BeamTickInterval;
            DamagePlayer(BeamDamage, mark);
        }
    }

    /// <summary>
    /// Runs the boss's execution cinematic: starts one when a hunting Crab-Core has
    /// closed to arm's length, steps the live one, and applies the two moments that
    /// actually cost the player anything — the claw's blow and the landing.
    ///
    /// A seizure is held for its whole length, recovery included, and no new one can
    /// begin until it has finished. That window is what stops a crab standing over a
    /// grounded player and grabbing them again the instant they land, which would be
    /// an inescapable loop rather than a set piece.
    /// </summary>
    private void UpdateSeizure(float dt)
    {
        if (Seizure is { } active)
        {
            PlayerTank caught = active.Victim;
            switch (active.Update(dt))
            {
                case CrabSeizure.Event.Struck:
                    DamagePlayer(CrabSeizure.StrikeDamage, caught);
                    break;
                case CrabSeizure.Event.Landed:
                    // A fraction of the shield's maximum, so the landing costs the
                    // same whatever state the player was in when they were caught.
                    DamagePlayer(caught.MaxShield * CrabSeizure.LandingDamageFraction, caught);
                    break;
            }

            DrainCues(active.Cues, active.ClearCues);

            if (!active.Active) Seizure = null;
            return;
        }

        // Whoever it has actually cornered — not "the player", which on a host meant seat 0
        // and on nineteen other machines meant nobody at all. Until this, a client could
        // stand in a Crab-Core's arms indefinitely and never be picked up: the boss walked
        // over, the protocol ran, and the grab test was asked about somebody else entirely.
        //
        // Still one seizure at a time, because the boss has one grip. The nearest eligible
        // craft wins it, so a crab surrounded takes whoever is actually in reach.
        if (Boss is not { } boss) return;
        PlayerTank? prey = null;
        float best = float.MaxValue;
        foreach (var mark in Players)
        {
            if (mark.Away || !mark.Alive) continue;
            if (!CrabSeizure.CanSeize(boss, mark)) continue;
            float d = Torus.DistanceSquared(mark.Position, boss.Position);
            if (d < best) { best = d; prey = mark; }
        }
        if (prey != null) Seizure = new CrabSeizure(boss, prey);
    }

    // --- The Maw-Core ---------------------------------------------------------

    /// <summary>What one of the mouth's little lasers costs. Less than a hunter's
    /// round: they are slow enough to walk away from, so a player who eats one has
    /// usually chosen to stand their ground and shoot back, and that shouldn't be
    /// punished as hard as being caught out by a tank.</summary>
    private const float MawLaserDamage = 7f;

    // Cadences for the mouth's two continuous layers. Held here rather than in the
    // entity so the simulation stays free of anything that has to know about ranges
    // and mixing — exactly where the crab's hunting call lives.
    private const float MawTeethInterval = 1.1f;
    private const float MawCrystalInterval = 2.4f;
    private float _mawTeethTimer;
    private float _mawCrystalTimer;

    /// <summary>
    /// Steps the Maw-Core: its own hunt, the lasers it has in the air, and the two
    /// sound layers that run the whole time it exists. Its hover bed is fed every tick
    /// the way the crab's rotor is, and faded out on the ticks where there is nothing
    /// up there.
    /// </summary>
    private void UpdateMaw(float dt)
    {
        if (Maw is not { } maw)
        {
            Audio.SetMawHover(false, Vector2.Zero, 0f);
            return;
        }

        // The mouth hangs over whoever is nearest it. Everything below this line measures
        // against the craft the camera is riding instead, because all of it is sound — and
        // while spectating that is the team-mate being watched, not this player's own wreck.
        PlayerTank under = NearestPlayer(maw.Position);
        if (maw.Update(dt, under.Position, under.Height))
            Emit(Cue.MawSpit, maw.Position);
        DrainCues(maw.Cues, maw.ClearCues);

        float dist = Torus.Distance(maw.Position, Eye.Position);
        Audio.SetMawHover(true, maw.Position, maw.Agitation);

        // The teeth and the crystal, each on their own unrelated clock so the two
        // never fall into a rhythm together. Suppressed while it is digesting — the
        // cinematic voices its own, much closer, grinding.
        if (maw.Alive && !maw.Digesting)
        {
            _mawTeethTimer -= dt;
            if (_mawTeethTimer <= 0f)
            {
                _mawTeethTimer = MawTeethInterval;
                // Through the cue channel, so the mouth is audible to everyone standing
                // under it and not only to whoever happens to be hosting. The timers are the
                // host's, which is exactly right — one mouth, one rhythm, heard by all.
                Emit(Cue.MawTeeth, maw.Position);
            }

            _mawCrystalTimer -= dt;
            if (_mawCrystalTimer <= 0f)
            {
                _mawCrystalTimer = MawCrystalInterval;
                Emit(Cue.MawCrystal, maw.Position, maw.Agitation);
            }
        }

        UpdateMawLasers(maw);

        // Once the death glitch has fully torn it apart, drop it so the director is
        // free to hang a new one later.
        if (maw.Dead) Maw = null;
    }

    /// <summary>
    /// Applies the mouth's little lasers to the player. Unlike a hunter's round these
    /// bite an airborne craft too: the jump is the counter to everything on the grid,
    /// and this monster's whole point is that it forces you into the air — sparing a
    /// leaping player would mean the safe answer is to simply never come down.
    /// </summary>
    private void UpdateMawLasers(MawCore maw)
    {
        float reach = MawCore.LaserRadius + PlayerTank.Radius;
        var lasers = maw.Lasers;

        // Every craft, not only seat 0 — a mouth hanging over a fight was firing exclusively
        // at whoever happened to be hosting it. One bolt still bites once: the first craft it
        // reaches spends it, so a burst cannot rake a line of players.
        for (int i = 0; i < lasers.Length; i++)
        {
            if (!lasers[i].Active) continue;
            foreach (var mark in Players)
            {
                // Nothing can touch a player inside a set piece; they cannot act, so they
                // must not be shot at by anything else either.
                if (mark.Captured || mark.Away || !mark.Alive) continue;

                // The lasers live in absolute coordinates around the maw; measure the craft
                // against the maw's nearest image so a bolt still bites a player just over
                // the seam from it.
                Vector2 near = Torus.NearestImage(mark.Position, maw.Position);
                var craft = new Vector3(near.X, mark.Height + 1f, near.Y);
                if (Vector3.DistanceSquared(lasers[i].Position, craft) > reach * reach) continue;

                maw.ConsumeLaser(i);      // one bolt, one bite
                DamagePlayer(MawLaserDamage, mark);
                break;
            }
        }
    }

    /// <summary>
    /// Runs the digestion: starts one on the tick a lunging mouth closes over the
    /// player, steps the live one, and applies the two things that cost anything — the
    /// bites while they are held, and the landing when they are spat out.
    /// </summary>
    private void UpdateDigestion(float dt)
    {
        if (Digestion is { } active)
        {
            PlayerTank eaten = active.Victim;
            switch (active.Update(dt))
            {
                case MawDigestion.Event.Bitten:
                    // A fraction of the maximum, so being eaten costs the same share
                    // of a life whatever state the player was caught in.
                    DamagePlayer(eaten.MaxShield * MawDigestion.BiteFraction, eaten);
                    break;
                case MawDigestion.Event.Landed:
                    DamagePlayer(eaten.MaxShield * MawDigestion.LandingFraction, eaten);
                    break;
            }

            DrainCues(active.Cues, active.ClearCues);

            if (!active.Active) Digestion = null;
            return;
        }

        // Whoever is standing under it — same story as the crab's claw above: the mouth has
        // one throat, but which craft ends up in it was decided by asking about seat 0.
        if (Maw is not { } maw) return;
        PlayerTank? meal = null;
        float best = float.MaxValue;
        foreach (var mark in Players)
        {
            if (mark.Away || !mark.Alive) continue;
            if (!MawDigestion.CanSwallow(maw, mark)) continue;
            float d = Torus.DistanceSquared(mark.Position, maw.Position);
            if (d < best) { best = d; meal = mark; }
        }
        if (meal != null) Digestion = new MawDigestion(maw, meal);
    }

    /// <summary>
    /// The Maw-Core's death: the shell blows apart, the teeth go everywhere and the
    /// whole thing stops holding itself up. Bursts are staged at the three heights the
    /// rig actually occupies rather than at one centre, so a tall floating thing comes
    /// apart down its length instead of popping like a tank.
    /// </summary>
    private void DestroyMaw(MawCore maw)
    {
        Emit(Cue.BossDeath, maw.Position);   // the mouth fails like the crab does

        var c = maw.Position;
        float body = maw.BodyY;
        // The crystal, up in the well — a hot neon burst where the weak spot was.
        Debris.Burst(new Vector3(c.X, body + MawRig.CrystalLocalY * MawRig.Scale, c.Y),
            Palette.NeonRed, elite: true);
        // The shell shattering at the seam where its middle used to be.
        Debris.Burst(new Vector3(c.X, body, c.Y), Palette.MawShell, elite: true);
        // And the teeth, thrown off the ring they were still turning on.
        Debris.Burst(new Vector3(c.X, body + MawRig.ToothLocalY * MawRig.Scale, c.Y),
            Palette.MawTooth, elite: false);
    }

    /// <summary>
    /// Debug hatch: hangs a Maw-Core well ahead of the player, outside its own detect
    /// radius so it drifts nowhere until they walk up on it. Replaces any already on
    /// the field rather than stacking a second.
    /// </summary>
    public void SpawnMawAhead()
    {
        const float Ahead = MawCore.DetectRadius + 15f;
        Maw = new MawCore(Player.Position + Player.Forward * Ahead);
    }

    /// <summary>Test hatch: hangs a prepared Maw-Core on the field, so the headless
    /// self-test can drive a swallow end to end against a real world rather than
    /// against the entity in isolation.</summary>
    public void AttachMawForTest(MawCore maw) => Maw = maw;

    /// <summary>Test hatch: raises a Maw-Core at a named spot rather than in front of the
    /// local craft — which is the only way to ask whether it can reach a seat that is not
    /// this machine's own.</summary>
    public void SpawnMawAt(Vector2 at) => Maw = new MawCore(Torus.Wrap(at));

    /// <summary>Test hatch: the Crab-Core equivalent of <see cref="SpawnMawAt"/>.</summary>
    public void SpawnCrabAt(Vector2 at) => Boss = new CrabCore(Torus.Wrap(at));

    /// <summary>Test hatch: destroys a hunter and credits it to a seat, so the scoreboard and
    /// the combat feed can be asked what they did about it without flying a round there.</summary>
    public void KillEnemyForTest(EnemyTank enemy, int by)
        => DamageEnemy(enemy, enemy.Shield + 1f, by);

    /// <summary>Test hatch: bursts a mortar shell at a named spot, so the splash can be
    /// asked who it actually bills without flying a round there first.</summary>
    public void DetonateMortarForTest(Vector2 at)
    {
        var shell = new Projectile();
        shell.FireGrenade(Torus.Wrap(at), new Vector2(0f, 1f), Projectile.NoOwner);
        shell.Position = Torus.Wrap(at);
        DetonateMortar(shell);
    }

    /// <summary>
    /// The horizon spawn director: on independent timers, rolls to raise a new hunter,
    /// drift in fresh salvage, or — rarely — bring up a Crab-Core, always at a random
    /// bearing out in the fog around the roaming craft and only while under each cap.
    /// </summary>
    private void UpdateSpawning(float dt)
    {
        _enemyTimer += dt;
        if (_enemyTimer >= EnemySpawnInterval)
        {
            _enemyTimer = 0f;
            if (Random.Shared.NextSingle() < EnemySpawnChance)
            {
                // At the cap, release the farthest hunter so a fresh one always has room
                // to fade in — the population is bounded but the arrivals never stop.
                if (Enemies.Count >= MaxEnemies) RemoveFarthest(Enemies, e => e.Position);
                bool elite = Random.Shared.NextSingle() < EliteChance;
                Enemies.Add(new EnemyTank(RandomPointAroundPlayer(SpawnMinRange, SpawnMaxRange), elite));
            }
        }

        _pickupTimer += dt;
        if (_pickupTimer >= PickupSpawnInterval)
        {
            _pickupTimer = 0f;
            if (Random.Shared.NextSingle() < PickupSpawnChance)
            {
                // Same rule for salvage: when full, the farthest piece drifts out and a
                // new one drifts in, so batteries and rounds keep coming forever.
                if (Pickups.Count >= MaxPickups) RemoveFarthest(Pickups, pk => pk.Position);
                var kind = Random.Shared.NextSingle() < BatteryShare ? PickupKind.Battery : PickupKind.Ammo;
                Pickups.Add(new Pickup(RandomPointAroundPlayer(SpawnMinRange, SpawnMaxRange), kind));
            }
        }

        _bossTimer += dt;
        if (_bossTimer >= BossSpawnInterval)
        {
            _bossTimer = 0f;
            // Only ever one crab, and only when the field is clear of a live one.
            if (Boss is null && Random.Shared.NextSingle() < BossSpawnChance)
                Boss = new CrabCore(RandomPointAroundPlayer(SpawnMinRange, SpawnMaxRange));
        }

        _mawTimer += dt;
        if (_mawTimer >= MawSpawnInterval)
        {
            _mawTimer = 0f;
            // Only ever one mouth, and never while one is already up there.
            if (Maw is null && Random.Shared.NextSingle() < MawSpawnChance)
                Maw = new MawCore(RandomPointAroundPlayer(SpawnMinRange, SpawnMaxRange));
        }

        _squadTimer += dt;
        if (_squadTimer >= SquadSpawnInterval)
        {
            _squadTimer = 0f;
            // Squads are never released to make room for a new one the way hunters are: a
            // squad is a fight with a beginning and an end, and quietly deleting four
            // people mid-arc to let four more fade in would be nonsense. Under the cap they
            // arrive; at it, nothing happens until one is finished with.
            if (Squads.Count < MaxSquads && Random.Shared.NextSingle() < SquadSpawnChance)
                SpawnSoldierSquad();
        }
    }

    /// <summary>
    /// Bobs and spins each pickup and collects any craft has driven over. A collected pickup
    /// goes into <em>that seat's</em> pack, sparks in its own colour, then teleports back out
    /// into the fog so the field stays stocked — the salvage is endless.
    ///
    /// Every seat is tested, not just this machine's: salvage is the run's progression and it
    /// has to be reachable by all twenty craft, not only by the one holding the keyboard. Only
    /// the host ever runs this (a client's pickups merely bob — see the client's early return
    /// in <see cref="StepForTest"/>), so there is exactly one machine deciding who got what.
    /// </summary>
    private void UpdatePickups(float dt)
    {
        float reach = PlayerTank.Radius + Pickup.Radius;
        float reachSq = reach * reach;
        _fragmentToRemove = null;
        foreach (var pk in Pickups)
        {
            pk.Update(dt);
            // First craft to reach it takes it — the roster order breaks a tie between two
            // players standing on the same cell, which is as fair as anything and, unlike a
            // distance test, never hands the same shard to both.
            for (int seat = 0; seat < Players.Count; seat++)
            {
                PlayerTank who = Players[seat];
                if (who.Away || !who.Alive) continue;
                if (Torus.DistanceSquared(pk.Position, who.Position) > reachSq) continue;
                Collect(pk, seat);
                break;
            }
        }
        // A spent fragment is pulled off the field after the walk so the list isn't
        // mutated mid-iteration.
        if (_fragmentToRemove != null) Pickups.Remove(_fragmentToRemove);
    }

    /// <summary>
    /// Stows a driven-over pickup into <paramref name="seat"/>'s pack, sounds the collect, and
    /// relocates it. Salvage no longer charges the craft on contact — a battery becomes one
    /// battery item, a stray round a random handful of bullets, a shard one fragment; the
    /// player spends them from the panel (E). Overflow that won't fit the pack is simply lost
    /// as the pickup drifts back out to the fog.
    /// </summary>
    private void Collect(Pickup pk, int seat)
    {
        ItemKind kind = pk.Kind switch
        {
            PickupKind.Battery      => ItemKind.Battery,
            PickupKind.CrabFragment => ItemKind.CrabFragment,
            _                       => ItemKind.Bullet,
        };
        InventoryOf(seat).Add(kind, pk.Amount);

        // Through the cue channel rather than straight at the speaker, so the player who
        // actually scooped it hears it wherever they are — and nobody else's machine chimes
        // for salvage they had nothing to do with.
        Emit(Cue.Pickup, pk.Position, owner: seat, personal: true);

        // A small sparkle in the pickup's colour marks the grab, reusing the debris
        // system (cosmetic only — it never touches damage or collision).
        Color spark = pk.Kind switch
        {
            PickupKind.Battery      => Palette.BatteryCore,
            PickupKind.CrabFragment => Palette.NeonRed,
            _                       => Palette.Flag,
        };
        Debris.Burst(new Vector3(pk.Position.X, pk.BobHeight, pk.Position.Y), spark, elite: false);

        // A collected fragment is spent, not endless salvage: drop it from the field
        // rather than respawning it in the fog. Batteries and rounds keep drifting back.
        if (pk.Kind == PickupKind.CrabFragment)
        {
            _fragmentToRemove = pk;
            return;
        }

        // Respawn out in the fog around the craft that took it, so it drifts back into that
        // player's view later rather than always re-seeding around the host.
        pk.Position = PointAround(Players[seat].Position, 45f, 100f);
        pk.Age = 0f;
    }

    /// <summary>What a rocket costs in bullet salvage. Dear enough that a soldier is choosing
    /// between a full magazine and something that can take a building down.</summary>
    private const int RoundsPerRocket = 10;

    /// <summary>
    /// Burns one grid slot straight into a craft — the panel's right-click. Lives on the world
    /// rather than on the panel because the host has to be able to run it for a client that
    /// asked for it (see <see cref="ApplyInvIntent"/>), and both ends must spend exactly the
    /// same thing.
    ///
    /// A battery is worth the same shield <em>and</em> hyper the salvage used to give, the
    /// whole stack at once. A stack of rounds loads only what the magazine has room for, so
    /// right-clicking 12 into a 40/50 magazine loads 10 and leaves 2 behind — and on a soldier
    /// every ten rounds also seats a rocket, because they have to come from somewhere and the
    /// alternative is a second kind of pickup for one chassis.
    /// </summary>
    public bool ChargeFromSlot(PlayerTank player, Inventory inv, int index)
    {
        if (!Inventory.Addressable(InvRegion.Slots, index)) return false;
        ref ItemStack slot = ref inv.Slots[index];
        if (slot.IsEmpty) return false;

        bool local = ReferenceEquals(player, Players[LocalIndex]);

        switch (slot.Kind)
        {
            case ItemKind.Battery:
                player.RefillShield(BatteryChargeFraction * slot.Count);
                player.RefillHyper(BatteryChargeFraction * slot.Count);
                slot = ItemStack.Empty;
                if (local) Audio.PlayPickup(player.Position);
                return true;

            case ItemKind.Bullet:
            {
                int room = player.MaxAmmo - player.Ammo;
                if (room <= 0 && player.Rockets >= player.MaxRockets)
                {
                    if (local) Audio.PlayFull();
                    return false;
                }
                int loaded = Math.Min(Math.Max(room, 0), slot.Count);
                player.Ammo += loaded;
                if (player.Soldier != null)
                {
                    // Charged off the whole stack, not just the part that fit in the magazine,
                    // so a full-up soldier can still spend rounds on rockets.
                    int rockets = slot.Count / RoundsPerRocket;
                    if (rockets > 0)
                    {
                        int seated = Math.Min(rockets, player.MaxRockets - player.Rockets);
                        player.RefillRockets(seated);
                        loaded = Math.Max(loaded, seated * RoundsPerRocket);
                    }
                }
                slot.Count -= Math.Min(loaded, slot.Count);
                if (slot.Count <= 0) slot = ItemStack.Empty;
                if (local) Audio.PlayPickup(player.Position);
                return true;
            }

            // Fragments and crafted cores aren't fuel — right-click does nothing.
            default:
                return false;
        }
    }

    // A fragment collected this tick, queued for removal after the pickup loop so the
    // list isn't mutated while it's being walked. Cleared each pass.
    private Pickup? _fragmentToRemove;

    /// <summary>A random point on the plane at [min,max] from the player.</summary>
    /// <summary>
    /// Debug hatch: plants a Crab-Core directly ahead of the player's current heading,
    /// far enough out that it sits outside <see cref="CrabCore.DetectRadius"/> and stays
    /// dormant — so a tester can walk up on a sleeping boss and choose the moment it
    /// wakes. Replaces any Crab-Core already on the field rather than stacking a second.
    /// </summary>
    public void SpawnCrabAhead()
    {
        // A margin past the wake radius: close enough to see and approach, but the
        // boss is unmistakably still asleep when it lands.
        const float Ahead = CrabCore.DetectRadius + 15f;
        Boss = new CrabCore(Player.Position + Player.Forward * Ahead);
    }

    /// <summary>
    /// Test hatch: hands the player a ready-made CRAB CORE weapon — equipped straight
    /// into the first empty R/T/Y/U slot so it can be thrown at once, or dropped in the
    /// pack if all four are taken. Wired to the same 'K' key that plants a Crab-Core, so
    /// one press both raises the enemy and arms you against it.
    /// </summary>
    public void GiveCrabCore()
    {
        for (int i = 0; i < Inventory.Weapons.Length; i++)
        {
            if (!Inventory.Weapons[i].IsEmpty) continue;
            Inventory.Weapons[i] = new ItemStack(ItemKind.CrabCore, 1);
            return;
        }
        Inventory.Add(ItemKind.CrabCore, 1);
    }

    /// <summary>Test hatch: stages a CRAB CORE blast a fixed distance ahead of the
    /// player, so the capture harness can photograph the cinematic without timing a throw.</summary>
    public void StageCrabBlastAheadForTest() => StageCrabBlast(Player.Position + Player.Forward * 20f);

    /// <summary>
    /// A random fog-ring point around <em>somebody</em> — a living seat picked at random each
    /// time, not always this machine's craft.
    ///
    /// This is what the whole spawn director hangs off, and while it read <c>Player</c> the
    /// field was built entirely around the host: a client who drove away from them found an
    /// empty world with nothing in it to fight, salvage or run from, however long they
    /// roamed. Rolling the seat per spawn means the field fills around everyone over time
    /// without any one player's neighbourhood being favoured.
    /// </summary>
    private Vector2 RandomPointAroundPlayer(float minDist, float maxDist)
        => PointAround(SpawnAnchor().Position, minDist, maxDist);

    /// <summary>The craft a fresh spawn is measured from. A living seat at random, falling
    /// back to this machine's own when everybody is spent (or in a solo run, where it is the
    /// only answer there has ever been).</summary>
    private PlayerTank SpawnAnchor()
    {
        int alive = 0;
        for (int i = 0; i < Players.Count; i++)
            if (!Players[i].Away && Players[i].Alive) alive++;
        if (alive == 0) return Players[LocalIndex];

        int pick = Random.Shared.Next(alive);
        for (int i = 0; i < Players.Count; i++)
        {
            if (Players[i].Away || !Players[i].Alive) continue;
            if (pick-- == 0) return Players[i];
        }
        return Players[LocalIndex];
    }

    /// <summary>Drops the item farthest from everyone from a list — used to make room for a
    /// fresh spawn so a full field never blocks new arrivals. Measured against the NEAREST
    /// player rather than against this machine's craft, so a hunter parked in a team-mate's
    /// face is never the one released to make room.</summary>
    private void RemoveFarthest<T>(List<T> list, Func<T, Vector2> posOf)
    {
        if (list.Count == 0) return;
        int farthest = 0;
        float best = DistanceToNearestPlayer(posOf(list[0]));
        for (int i = 1; i < list.Count; i++)
        {
            float d = DistanceToNearestPlayer(posOf(list[i]));
            if (d > best) { best = d; farthest = i; }
        }
        list.RemoveAt(farthest);
    }

    /// <summary>How far the nearest living craft is from a point, squared. The field's
    /// measure of "out of play" once there is more than one player in it.</summary>
    private float DistanceToNearestPlayer(Vector2 at)
    {
        float best = float.MaxValue;
        for (int i = 0; i < Players.Count; i++)
        {
            if (Players[i].Away || !Players[i].Alive) continue;
            best = MathF.Min(best, Torus.DistanceSquared(at, Players[i].Position));
        }
        return best == float.MaxValue
            ? Torus.DistanceSquared(at, Players[LocalIndex].Position) : best;
    }

    private static Vector2 PointAround(Vector2 origin, float minDist, float maxDist)
    {
        float angle = Random.Shared.NextSingle() * MathF.Tau;
        float dist = minDist + Random.Shared.NextSingle() * (maxDist - minDist);
        // Fold back into the wrap window: a bearing past the world's edge lands on the
        // opposite side of the torus, which the renderer re-images near the player.
        return Torus.Wrap(origin + new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * dist);
    }

    /// <summary>
    /// Debug hatch: drops one random enemy from the live roster onto the horizon at
    /// a random bearing — a standard hunter, an elite hunter, or (only if the field
    /// is clear of one) a Crab-Core. Wired to the in-game 'L' key so a tester can
    /// stack up threats on demand without waiting on the spawn director. Respects the
    /// same caps as the director: at the hunter cap the farthest is released first.
    /// </summary>
    public void SpawnRandomEnemy()
    {
        Vector2 at = RandomPointAroundPlayer(SpawnMinRange, SpawnMaxRange);

        // Weight toward tanks; only offer each big form when the field is clear of
        // one, so the roll never wastes on an impossible pick.
        int forms = 2 + (Boss is null ? 1 : 0) + (Maw is null ? 1 : 0);
        int pick = Random.Shared.Next(forms);
        // With the crab already up, the boss slot is skipped and the mouth slides
        // down into index 2 — so the roll always lands on something that can spawn.
        if (pick == 2 && Boss is not null) pick = 3;
        switch (pick)
        {
            case 0: // standard hunter
                if (Enemies.Count >= MaxEnemies) RemoveFarthest(Enemies, e => e.Position);
                Enemies.Add(new EnemyTank(at, elite: false));
                break;
            case 1: // elite hunter
                if (Enemies.Count >= MaxEnemies) RemoveFarthest(Enemies, e => e.Position);
                Enemies.Add(new EnemyTank(at, elite: true));
                break;
            case 2: // Crab-Core (only reachable when Boss is null)
                Boss = new CrabCore(at);
                break;
            default: // Maw-Core (only reachable when Maw is null)
                Maw = new MawCore(at);
                break;
        }
    }

    /// <summary>
    /// Requests a player shot; honoured only if off cooldown with ammo.
    ///
    /// Fired from inside the Maw-Core's throat the round never becomes a projectile:
    /// there is nothing to aim at and nothing to miss at point-blank inside a mouth,
    /// so the shot is handed straight to the digestion as one of the three that break
    /// its hold. Note that the craft still pays for it in ammo and cooldown — being
    /// swallowed does not come with free bullets, and the cooldown is what makes the
    /// escape take a few seconds of being chewed rather than one panicked trigger pull.
    /// </summary>
    public void FirePlayerShot() => FirePlayerShot(laser: false);

    /// <summary>Which seat a craft is sitting in, or <see cref="Projectile.NoOwner"/> if it
    /// is not in the roster at all. What a round is stamped with when it is fired.</summary>
    public int Seat(PlayerTank who)
    {
        int i = Players.IndexOf(who);
        return i < 0 ? Projectile.NoOwner : i;
    }

    /// <summary>
    /// The player's ordinary trigger pull. <paramref name="laser"/> only changes what
    /// the round is drawn as (the SPIDER's emitter throws neon streaks where the tank
    /// throws bolts) — the ammo cost, the cooldown and the damage are the same round.
    /// </summary>
    public void FirePlayerShot(bool laser, PlayerTank? by = null)
    {
        // Null means the craft this machine is driving, which is what every existing caller
        // — the loop, the self-test, the capture harness — has always meant by "the player".
        PlayerTank who = by ?? Player;
        // The Crab-Core's seizure is a cutscene, not a trap: there is no shooting your
        // way out of it, so the trigger is dead for its duration and the round isn't
        // even spent. Checked before TryFire so a held player doesn't quietly burn
        // ammo on shots that go nowhere. This matters more than it used to — the gun
        // now cools while captured (so the Maw's escape can work), which without this
        // would let a seized player plink bolts out of the crab's claw all through the
        // scream.
        if (HeldInClaw(who)) return;

        if (!who.TryFire(out Vector2 origin, out Vector2 dir, out float launchHeight))
            return;

        if (SwallowedIn(who) is { Held: true } digestion)
        {
            digestion.RegisterShot();
            Emit(Cue.Detonation, who.Position, owner: Seat(who));
            return;
        }

        // The vertical half of the head's aim. Flat on a hull that can't crane its gun —
        // GunElevation is zero there — and up to the head's own stop on the two chassis
        // that can, so the bolt climbs or dives to where the crosshair is resting.
        SpawnProjectile(origin, dir, owner: Seat(who), launchHeight: launchHeight,
            laser: laser, pitch: who.GunElevation);
    }

    // --- The TANK chassis: the siege kit ----------------------------------------

    /// <summary>
    /// Reads the TANK's expanded kit — and, since the SPIDER, FISH, SOLDIER and VIRUS are all
    /// dispatched earlier, this is the only chassis that reaches here, so it also carries the
    /// plain machine default of cannon-and-heavy-round. The order is deliberate: the plant
    /// toggles first (so a plant and a shot on the same frame both land), then the two Hyper /
    /// cooldown moves, then the AP slug takes priority over the cannon so tapping its key never
    /// loses the shot under a held fire button.
    /// </summary>
    private void UpdateTankTriggers(in InputFrame input, PlayerTank who)
    {
        // The siege plant, on the freed jump key. Locks the tracks, cranes the gun the full way
        // up and turns the front plate to the field — the tank's answer to a game it can no
        // longer leave the ground to solve.
        if (input.TankPlantPressed)
        {
            who.TogglePlant();
            // The clank of the tracks locking, or letting go. Through the cue channel like
            // every other combat noise, so a team-mate digging in beside you is something
            // you hear rather than something only their own machine knows about.
            Emit(Cue.Clamp, who.Position, owner: Seat(who));
        }

        // The lurch: a Hyper-fed track-boost dodge in the drive direction. Refused while dug in.
        if (input.TankLurchPressed && who.TryLurch(input))
            // Borrow the soldier's kick — a hard gout of thrust.
            Emit(Cue.GasJump, who.Position, 0f, owner: Seat(who));

        // The dischargers: a screen of smoke to blind the field and break contact.
        if (input.TankSmokePressed && who.TryDeploySmoke())
            DeploySmoke(who);

        // The AP slug: a heavy piercing shot. Ahead of the cannon so holding fire and tapping
        // its key fires the slug rather than swallowing it under the ordinary round.
        if (input.TankSlugPressed)
        {
            FirePlayerSlug(who);
            return;
        }

        // The ordinary triggers: the mortar on the heavy button, the cannon on fire.
        if (input.Grenade) FirePlayerGrenade(who);
        else if (input.Fire) FirePlayerShot(laser: false, by: who);
    }

    /// <summary>
    /// Fires the AP slug: a heavy piercing round down the gun line that punches through a whole
    /// line of hunters and through cover both (see the projectile pass and BlockShotOnStructure).
    /// Costs a fistful of the magazine at once. Dead while seized, like the cannon.
    /// </summary>
    public void FirePlayerSlug(PlayerTank? by = null)
    {
        PlayerTank who = by ?? Player;
        if (HeldInClaw(who)) return;
        if (!who.TryFireSlug(out Vector2 origin, out Vector2 dir, out float launchHeight)) return;
        SpawnProjectile(origin, dir, owner: Seat(who), launchHeight: launchHeight,
            pitch: who.GunElevation, piercing: true);
    }

    /// <summary>Lays a smoke screen off the dischargers: one bank on the hull and one just
    /// behind it, so the murk is a wall to hide the whole craft rather than a puff beside it.
    /// Off <paramref name="who"/>'s hull — the host runs this for every seat, and a remote
    /// tank's screen used to appear around the host's own craft instead of theirs.</summary>
    private void DeploySmoke(PlayerTank who)
    {
        LaySmoke(who.Position);
        LaySmoke(who.Position - who.Forward * 6f);
        Emit(Cue.ThrowWhoosh, who.Position, owner: Seat(who));
        Debris.Burst(new Vector3(who.Position.X, 1.2f, who.Position.Y),
            Palette.StructureShell, elite: false);
    }

    private void LaySmoke(Vector2 at)
    {
        if (Smoke.Count >= MaxSmoke) Smoke.RemoveAt(0);
        Smoke.Add(new SmokeCloud(Torus.Wrap(at), SmokeLife, SmokeRadius));
        // Record the screen for the wire so it hides the field on every machine, not just the
        // one that laid it. Host-only; a client replays this through PlayRemoteEffect.
        if (CollectSoundCues) EffectCues.Add(new EffectCue(EffectKind.Smoke, new Vector3(at.X, 0f, at.Y), default));
    }

    /// <summary>
    /// Client-side: replays one particle burst the host raised, so a kill, a footfall or a laid
    /// smoke screen is seen on this machine and not only the host's. The scatter is re-rolled
    /// here — only the burst's kind, place and colour crossed — which is all a chaotic, one-second
    /// spray of debris ever shows. Counted for the self-test, which can open no window to see it.
    /// </summary>
    public void PlayRemoteEffect(EffectKind kind, Vector3 origin, Color color)
    {
        RemoteEffectsPlayed++;
        switch (kind)
        {
            case EffectKind.Burst: Debris.Burst(origin, color, elite: false); break;
            case EffectKind.EliteBurst: Debris.Burst(origin, color, elite: true); break;
            case EffectKind.FootPuff: Debris.FootPuff(origin); break;
            case EffectKind.Smoke: LaySmoke(new Vector2(origin.X, origin.Z)); break;
        }
    }

    /// <summary>Counts effects taken from the wire and replayed — the verification hook the
    /// self-test reads, since headless it can see no debris.</summary>
    public int RemoteEffectsPlayed { get; private set; }

    /// <summary>Test hook: lay a screen exactly as the discharger trigger would, if it has
    /// cooled. Returns whether it fired — the headless self-test can't press E.</summary>
    public bool DeploySmokeForTest()
    {
        if (!Player.TryDeploySmoke()) return false;
        DeploySmoke(Player);
        return true;
    }

    private void UpdateSmoke(float dt)
    {
        if (Smoke.Count == 0) return;
        for (int i = Smoke.Count - 1; i >= 0; i--)
        {
            Smoke[i].Update(dt);
            if (!Smoke[i].Active) Smoke.RemoveAt(i);
        }
    }

    /// <summary>
    /// The tank's ram: a hull genuinely driving into a hunter crushes it. Gated on the TANK,
    /// on the grid, and on the drive velocity actually clearing <see cref="PlayerTank.RamThreshold"/>
    /// — a cruise bump chips, a lurch-speed slam erases. The struck hunter is thrown clear along
    /// the contact line, and that separation (not a per-enemy timer) is what makes one slam one
    /// hit. Reads the velocity <see cref="PlayerTank"/> stashed on its own move this tick.
    /// </summary>
    /// <remarks>
    /// Every seat's hull, not just this machine's. A ram is a thing a driving craft does to
    /// whatever is in front of it, and there are up to twenty of them driving — this used to
    /// read <c>Player</c> throughout, so on the host only the host's own tank could ever
    /// crush anything, and a remote player could drive through a hunter at full speed to no
    /// effect whatsoever.
    /// </remarks>
    private void UpdateRam()
    {
        for (int seat = 0; seat < Players.Count; seat++)
        {
            PlayerTank who = Players[seat];
            if (who.Away) continue;
            RamFor(who, seat);
        }
    }

    private void RamFor(PlayerTank who, int seat)
    {
        if (who.Class != PlayerClass.Tank || who.IsAirborne || who.Captured) return;

        float speed = who.DriveVelocity.Length();
        if (speed < who.RamThreshold) return;

        float reach = PlayerTank.Radius + EnemyTank.Radius;
        float over = Math.Clamp((speed - who.RamThreshold) / (PlayerTank.MaxSpeed * 0.9f), 0f, 1.5f);

        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            if (!WithinHit(who.Position, e.Position, reach)) continue;

            DamageEnemy(e, RamDamage * (0.6f + over), seat);

            Vector2 push = Torus.Delta(who.Position, e.Position);
            push = push.LengthSquared() > 1e-4f ? Vector2.Normalize(push) : who.Forward;
            e.Position = Torus.Wrap(e.Position + push * RamShove);

            who.Jolt(0.35f);
            // The crunch of the hull meeting a hull.
            Emit(Cue.Detonation, who.Position, owner: seat);
        }

        // A soldier who has been put on the grid is, for as long as that lasts, a person
        // standing in front of a tank. The hull does not need to be told what to do about
        // that, and the reach is tighter because there is much less of them to hit.
        float personReach = PlayerTank.Radius + EnemySoldier.Radius;
        foreach (var s in Soldiers)
        {
            if (!s.Alive || s.Height > EnemySoldier.BodyHeight) continue;
            if (!WithinHit(who.Position, s.Position, personReach)) continue;
            DamageSoldier(s, RamDamage * (0.6f + over) * 2f);
            who.Jolt(0.2f);
            Emit(Cue.Detonation, who.Position, owner: seat);
        }
    }

    /// <summary>
    /// True if a live, opaque smoke screen sits across the segment from a shooter
    /// (<paramref name="from"/>) to the player (<paramref name="to"/>). Both the shooter and
    /// each cloud are folded into the player's own image across the wrap, so a hunter and a
    /// screen just over the seam are measured against the same line the round would fly.
    /// </summary>
    public bool SmokeBlocks(Vector2 from, Vector2 to)
    {
        if (Smoke.Count == 0) return false;
        Vector2 a = Torus.NearestImage(from, to);
        foreach (var c in Smoke)
        {
            if (!c.Opaque) continue;
            Vector2 centre = Torus.NearestImage(c.Position, to);
            if (PointSegmentDistanceSq(centre, a, to) <= c.Radius * c.Radius) return true;
        }
        return false;
    }

    /// <summary>True if a point sits inside any live, opaque screen — what a round already in
    /// the air is tested against, so a shot dies in the murk it flies into.</summary>
    public bool SmokeAbsorbs(Vector2 at)
    {
        foreach (var c in Smoke)
        {
            if (!c.Opaque) continue;
            if (Torus.DistanceSquared(at, c.Position) <= c.Radius * c.Radius) return true;
        }
        return false;
    }

    /// <summary>Squared distance from point <paramref name="p"/> to segment a→b, all in one
    /// planar frame — the caller has already resolved the torus wrap into common images.</summary>
    private static float PointSegmentDistanceSq(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.LengthSquared();
        if (len2 < 1e-6f) return (p - a).LengthSquared();
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        Vector2 proj = a + ab * t;
        return (p - proj).LengthSquared();
    }

    /// <summary>
    /// The mortar's burst where its lob came down: a splash on the whole cluster there, on either
    /// crystal if it landed on one, and on any footprint it dropped against — the same reach and
    /// damage a grenade always dealt, now delivered from above. Mirrors the rocket's detonation
    /// but throws the shake onto the hull directly, since a tank has no soldier rig to feel it.
    /// </summary>
    private void DetonateMortar(Projectile p)
    {
        var at = new Vector3(p.Position.X, MathF.Max(0.4f, p.Height), p.Position.Y);
        float reach = p.SplashRadius + EnemyTank.Radius;

        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            if (WithinHit(p.Position, e.Position, reach)) DamageEnemy(e, GrenadeDamage);
        }

        // A soldier caught by the burst, which for a shell coming down on the grid mostly
        // means one who had already been put on it.
        DamageSoldiersInBlast(p.Position, at.Y, p.SplashRadius, GrenadeDamage);

        // The two monsters are bitten by planar proximity, not at the burst's own height: the
        // shell comes down on the grid far below the core or crystal, so a height-gated check
        // (the way a flat bolt is scored) would never land. A heavy detonation within reach of
        // the column simply strikes the weak point — the exact bargain the thrown CRAB CORE
        // strikes — for the smaller MortarBossDamage, so it takes a few good lobs, not one.
        if (Boss is { Alive: true } boss
            && WithinHit(p.Position, boss.Position, p.SplashRadius + CrabCore.CoreHitRadius))
        {
            if (boss.DamageCore(MortarBossDamage)) DestroyBoss(boss);
            else Emit(Cue.CoreHit, boss.Position, 1f - boss.CoreFraction);
        }
        if (Maw is { Alive: true } maw
            && WithinHit(p.Position, maw.Position, p.SplashRadius + MawRig.HitRadius))
        {
            if (maw.DamageCrystal(MortarBossDamage)) DestroyMaw(maw);
            else Emit(Cue.MawHurt, maw.Position, 1f - maw.CrystalFraction);
        }

        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        for (int i = Structures.Count - 1; i >= 0; i--)
        {
            Structure s = Structures[i];
            int n = s.Blockers(blockers);
            for (int b = 0; b < n; b++)
            {
                var (bAt, r) = blockers[b];
                if (!WithinHit(p.Position, bAt, p.SplashRadius + r)) continue;
                if (p.Height > s.BlockHeight + p.SplashRadius) continue;
                FellStructure(s);
                break;
            }
        }

        Debris.Burst(at, Palette.EliteFill, elite: true);
        Emit(Cue.RocketBlast, p.Position);

        // Dropped too close and caught in the burst: it bites. Every craft near the impact,
        // not only seat 0 — a mortar is a splash weapon and standing in one's radius should
        // cost you whoever you are.
        //
        // The view kick is NOT applied here any more. It used to be, gated on the local
        // craft, which meant a client — who never runs this method at all, because the host
        // owns the field — felt nothing when a rocket went off beside them. It now rides the
        // cue instead (see ShakeFromBlast), so every machine shakes for every blast it can
        // actually hear, and the one code path serves the host, the client and the spectator.
        foreach (var mark in Players)
        {
            if (mark.Away || !mark.Alive) continue;
            if (Torus.Distance(p.Position, mark.Position) < p.SplashRadius + PlayerTank.Radius)
                DamagePlayer(GrenadeDamage, mark);
        }
    }

    // --- The SPIDER chassis -----------------------------------------------------

    /// <summary>
    /// Reads the spider's two triggers. Left is the emitter's ordinary fire, routed
    /// straight through the craft's cannon so it costs and cools identically. Right
    /// winds the lance: while it is held the craft is rooted, and the frame it is let go
    /// the meter is spent as a beam.
    ///
    /// The order matters. The release is handled before the laser so that letting go of
    /// the right button on the same frame the left one is down fires the lance rather
    /// than swallowing it — a player mashing both should get the expensive shot, not
    /// silently lose the charge they spent two seconds standing still for.
    /// </summary>
    private void UpdateSpiderTriggers(SpiderWeapon spider, float dt, in InputFrame input, PlayerTank who)
    {
        SpiderClaw claw = who.Claw!;
        // The beds below belong to the listener, not to the craft: the host winds every
        // seat's meter, and only the one at this keyboard is worth hearing.
        bool local = ReferenceEquals(who, Players[LocalIndex]);

        // A cinematic has hold of the craft: nothing is charging, nothing is rooted, the
        // hands are empty. A wound-up meter is stowed rather than fired into a claw, and
        // whatever was being carried is dropped — the boss is about to pick the player up,
        // and it is not picking up two of them.
        if (HeldInClaw(who))
        {
            spider.Cancel();
            ReleaseHeldBody(claw);
            if (local) Audio.SetLanceCharge(false, 0f);
            who.Rooted = false;
            return;
        }

        // A body crushed to nothing in the hand — or swept out of the world by anything
        // else while it was in there — is let go before any of this reads the claw.
        if (claw.Victim is { Alive: false }) ReleaseHeldBody(claw);

        // The legs. Off the triggers entirely, so a climb can be made with the hands full
        // or the meter half wound.
        if (input.SpiderPouncePressed) TrySpiderPounce(who);

        // The right trigger: the lance. Refused outright while the claw is full, and that
        // is the whole relationship between the two — this chassis has one pair of front
        // limbs, and they are either holding a machine or braced around a charge.
        if (input.Grenade && !claw.Holding)
        {
            spider.Hold(dt);
            // Rooted for exactly as long as the meter is actually winding, so a trigger
            // held through a break lockout doesn't pin the craft to the spot for a beat
            // it is getting nothing for.
            who.Rooted = spider.Charging;
            // The whine climbs with the meter, so the player can hear how loaded the
            // shot is while they are busy watching the thing that is walking at them. A bed,
            // so only ever the craft at this machine — the host winds every seat's meter and
            // a remote player's charge must not whine in the host's ear.
            if (local) Audio.SetLanceCharge(spider.Charging, spider.ChargeFraction);
            return;
        }

        if (local) Audio.SetLanceCharge(false, 0f);
        who.Rooted = false;

        if (spider.Charging)
        {
            FireSpiderLance(spider, who);
            return;
        }

        UpdateSpiderClaw(who, claw, dt, input);
    }

    // --- The claw ------------------------------------------------------------------

    /// <summary>
    /// Reads the left trigger, which does one of two things depending on what is standing
    /// in front of the craft. With a hunter inside the claw's reach it closes on it; with
    /// nothing there it is the emitter's ordinary fire, routed through the craft's cannon
    /// path so it costs and cools identically to a tank's round.
    ///
    /// The contextual split is deliberate and it is the only way the two fit on one
    /// button: the ranges do not overlap. Anything close enough to grab is close enough
    /// that shooting it was never the interesting option, and anything far enough to be
    /// worth shooting is far out of arm's reach. The player never has to choose which one
    /// they meant — the field has already chosen.
    ///
    /// While something is in the hand the same trigger is the hold: down squeezes, and the
    /// frame it comes up the body is thrown. Same grammar as the lance, one limb over.
    /// </summary>
    private void UpdateSpiderClaw(PlayerTank who, SpiderClaw claw, float dt, in InputFrame input)
    {
        if (claw.Holding)
        {
            if (!input.Fire)
            {
                ThrowHeldBody(who, claw);
                return;
            }

            // Carried out in front of the core, turned to face the way the player is —
            // which is what makes it cover as well as cargo (see ShieldsFrom, below).
            // In front of <paramref name="who"/>: the host runs this for every seat, and a
            // remote spider used to carry its hostage out in front of the host's craft.
            Vector2 grip = Torus.Wrap(who.Position + who.Forward * SpiderClaw.HoldReach);
            float lift = who.Height + SpiderClaw.HoldHeight;
            float bite = claw.Hold(dt, grip, lift, who.Heading);
            if (bite > 0f && claw.Victim is { } held) DamageEnemy(held, bite);
            return;
        }

        if (!input.Fire) return;

        if (NearestGrabbable(who) is { } prey && claw.TryGrab(prey))
        {
            // The same brutal mechanical snap the boss's clamp display makes, now with
            // something else in it. The hull rocks as it takes the weight.
            Emit(Cue.Clamp, who.Position, owner: Seat(who));
            who.Jolt(0.3f);
            return;
        }

        FirePlayerShot(laser: true, by: who);
    }

    /// <summary>
    /// The nearest hunter the claw could close on: alive, not already in a hand or in the
    /// air, inside <see cref="SpiderClaw.Reach"/> of the craft, and roughly level with it
    /// — a machine forty metres below a craft standing on a roof is not within arm's
    /// reach however good the planar distance looks.
    /// </summary>
    private EnemyTank? NearestGrabbable(PlayerTank who)
    {
        EnemyTank? best = null;
        float bestSq = SpiderClaw.Reach * SpiderClaw.Reach;

        foreach (var e in Enemies)
        {
            if (!e.Alive || e.Grabbed || e.Flung) continue;
            if (MathF.Abs(who.Height - e.Height) > EnemyTank.BodyHeight) continue;

            float d = Torus.DistanceSquared(e.Position, who.Position);
            if (d > bestSq) continue;
            bestSq = d;
            best = e;
        }
        return best;
    }

    /// <summary>
    /// Hurls whatever is in the claw down the craft's full look line and stages the
    /// world's half of it. The body flies on its own from here (see
    /// <see cref="UpdateFlungBodies"/>); what happens here is only the launch.
    /// </summary>
    private void ThrowHeldBody(PlayerTank who, SpiderClaw claw)
    {
        // Down <paramref name="who"/>'s look line. This read the local craft's heading, so on
        // the host a remote player's throw flew off along whatever direction the host happened
        // to be facing — a hostage hurled at nothing, from the thrower's point of view.
        if (claw.Throw(who.Forward3) is null) return;
        Emit(Cue.ThrowWhoosh, who.Position, owner: Seat(who));
        who.Jolt(0.2f);
    }

    /// <summary>Opens the hand without throwing — a crushed catch, a cinematic, the
    /// crafting panel. The body drops where it stands.</summary>
    private void ReleaseHeldBody(SpiderClaw claw) => claw.Drop();

    /// <summary>
    /// Steps every hull currently in the air after being thrown, and lands the ones that
    /// have met the grid. A thrown machine is a heavy object travelling fast: what it
    /// comes down on wears it, and so does the thing that was thrown — worse, in fact,
    /// which is what keeps a throw a way of spending a hostage rather than a way of
    /// farming one.
    /// </summary>
    private void UpdateFlungBodies(float dt)
    {
        for (int i = 0; i < Enemies.Count; i++)
        {
            var e = Enemies[i];
            if (!e.Flung) continue;

            e.Toss = e.Toss with { Y = e.Toss.Y - SpiderClaw.ThrowGravity * dt };
            e.Position = Torus.Wrap(e.Position + new Vector2(e.Toss.X, e.Toss.Z) * dt);
            e.Height += e.Toss.Y * dt;
            // Turned along its own flight, so a thrown hull tumbles nose-first rather than
            // sailing across the arena still politely facing the way it was parked.
            e.Heading = MathF.Atan2(e.Toss.X, e.Toss.Z);

            if (e.Height > 0f) continue;
            LandFlungBody(e);
        }
    }

    /// <summary>The moment a thrown hull meets the grid: the impact, what it costs
    /// everything standing there, and the wreckage it throws off.</summary>
    private void LandFlungBody(EnemyTank body)
    {
        body.Height = 0f;
        body.Flung = false;
        body.Toss = Vector3.Zero;
        body.Position = Torus.Wrap(body.Position);

        Vector2 at = body.Position;

        // Everything else standing where it came down. Walked by index against a snapshot
        // count so the list can't shift underneath the loop, and skipping the body itself
        // — it is billed separately below, and harder.
        foreach (var e in Enemies)
        {
            if (ReferenceEquals(e, body) || !e.Alive || e.Flung) continue;
            if (!WithinHit(at, e.Position, SpiderClaw.ImpactRadius + EnemyTank.Radius)) continue;
            DamageEnemy(e, SpiderClaw.ThrownBodyDamage);
        }

        DamageSoldiersInBlast(at, 0f, SpiderClaw.ImpactRadius, SpiderClaw.ThrownBodyDamage);

        Debris.Collapse(new Vector3(at.X, 0.6f, at.Y), Vector2.UnitX, 2f,
            body.IsElite ? Palette.EliteFill : Palette.EnemyFill, 1f);
        // A hull the size of a car meeting the grid, heard wherever anyone is standing.
        Emit(Cue.ExplosionAt, at);

        // And the landing itself, which the thrown machine wears whether it hit anything
        // or not. Last, so a body that dies on impact bursts where it came down.
        DamageEnemy(body, SpiderClaw.ImpactDamage);
    }

    // --- The legs ------------------------------------------------------------------

    /// <summary>
    /// The pounce: a kick off whatever wall is in reach. Finds the nearest solid face of
    /// the city and hands the craft the outward normal to shove against — the world does
    /// the looking because the world is the only thing that knows where the buildings
    /// are.
    ///
    /// Chained up the side of a tower this is how the SPIDER gets on top of the city,
    /// which is the whole reason it exists: a beam loosed from a roof passes clean over
    /// the hunters on the grid and reaches things a shot from down there never will, and
    /// the class with six legs should be the one that can get up there.
    /// </summary>
    /// <param name="who">The craft doing the climbing. This used to be the local player
    /// unconditionally, so on the host a remote spider's kick was measured against the walls
    /// near the <em>host's</em> craft and then launched the host up one of them.</param>
    private bool TrySpiderPounce(PlayerTank who)
    {
        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];

        foreach (var s in Structures)
        {
            // Above the parapet there is nothing left to push against — you are standing
            // on it, and what you want from there is the ordinary hop.
            if (who.Height >= s.BlockHeight) continue;

            int n = s.Blockers(blockers);
            for (int b = 0; b < n; b++)
            {
                var (wall, radius) = blockers[b];
                float reach = radius + PlayerTank.Radius + PlayerTank.PounceReach;

                Vector2 out_ = Torus.Delta(wall, who.Position);
                float distSq = out_.LengthSquared();
                if (distSq >= reach * reach) continue;

                Vector2 outward = distSq > 1e-6f
                    ? out_ / MathF.Sqrt(distSq)
                    : who.Forward;

                if (!who.TryPounce(outward)) return false;
                Emit(Cue.ThrowWhoosh, who.Position, owner: Seat(who));
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// How close to a roof's own height the craft has to be falling before the legs catch
    /// it. Comfortably more than a tick's worth of fall, so nothing can drop through a
    /// building between two frames.
    /// </summary>
    private const float RoofSnap = 0.8f;

    /// <summary>
    /// Works out what the craft is standing on this tick — the grid, or the flat top of
    /// something in the city. Run after the craft has moved and before the wall pass, so
    /// a hull that has just come down on a roof is already up there by the time anything
    /// asks whether it is inside a building.
    ///
    /// Only the machines have any business up there: the two bodies and the mote own their
    /// own transforms entirely and land their own landings. In practice this is the
    /// SPIDER's alone, since the other machine is a TANK and a TANK cannot leave the grid.
    /// </summary>
    private void ResolveRoofs()
    {
        // Per seat, for the same reason the wall pass is: a remote SPIDER that pounced onto
        // a tower used to have no roof under it at all and simply fell back to the grid on
        // the one machine that decides where it really is.
        for (int seat = 0; seat < Players.Count; seat++)
        {
            PlayerTank who = Players[seat];
            if (who.Away) continue;
            if (!Authoritative && seat != LocalIndex) continue;
            ResolveRoofsFor(who);
        }
    }

    private void ResolveRoofsFor(PlayerTank who)
    {
        if (!who.IsMachine || who.Captured)
        {
            who.GroundHeight = 0f;
            return;
        }

        float roof = 0f;
        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];

        foreach (var s in Structures)
        {
            if (s.Falling) continue;                       // no standing on something on its way down
            if (who.Height < s.BlockHeight - RoofSnap) continue;   // still down the side of it
            if (s.BlockHeight <= roof) continue;

            int n = s.Blockers(blockers);
            for (int b = 0; b < n; b++)
            {
                var (at, radius) = blockers[b];
                // Genuinely over the footprint, not merely brushing the parapet: measured
                // against the block's own radius rather than that plus the craft's, so a
                // hull hanging half off the edge falls, as it should.
                if (Torus.Delta(at, who.Position).LengthSquared() > radius * radius) continue;
                roof = s.BlockHeight;
                break;
            }
        }

        who.GroundHeight = roof;

        // Caught on the way down: put the craft on the surface this tick rather than a
        // frame later, so the wall pass below sees a hull that is standing on the roof
        // instead of one buried a few centimetres inside the top of a tower.
        if (roof > 0f && who.Height < roof) who.Height = roof;
    }

    /// <summary>
    /// Looses the charged lance: spends rounds in proportion to the meter, raises the
    /// beam, and bites everything standing on the shaft. The damage is applied once,
    /// here — the beam that burns for the next half second is the picture of a shot that
    /// has already happened, not a lingering hazard, so a target can't be billed twice
    /// for one trigger pull.
    ///
    /// Refused outright when the magazine can't cover the bill, and the charge is
    /// dropped either way: an empty craft can't fire a lance any more than it can fire
    /// a cannon.
    /// </summary>
    private void FireSpiderLance(SpiderWeapon spider, PlayerTank? by = null)
    {
        // Null is the craft this machine is driving — what every caller before seats meant.
        PlayerTank who = by ?? Player;
        int cost = spider.AmmoCost;
        float damage = spider.Damage;

        // The emitter sits out past the carapace along the craft's heading, and the shaft
        // leaves down the full look line — the SPIDER's ring aims anywhere, up the face of
        // a tower or down at the grid, unlike the tank's stopped-short gun.
        Vector2 look = who.Forward;
        Vector2 muzzleXZ = who.Position + look * SpiderWeapon.MuzzleForward;
        var origin = new Vector3(muzzleXZ.X, SpiderWeapon.MuzzleHeight + who.Height, muzzleXZ.Y);
        Vector3 dir = who.Forward3;

        if (who.Ammo < cost)
        {
            spider.Cancel();   // the meter is spent whether or not a shot comes out
            return;
        }

        if (!spider.Release(origin, dir, out _)) return;

        who.Ammo -= cost;
        Emit(Cue.LanceFire, who.Position, owner: Seat(who));
        BurnSpiderLance(spider, damage, Seat(who));
    }

    /// <summary>
    /// Looses whatever is wound into the spider's meter, without waiting on a trigger
    /// edge — the headless self-test's way in, since it has no mouse. Silently does
    /// nothing on a chassis with no emitter.
    /// </summary>
    public void FireSpiderLanceForTest()
    {
        if (Player.Spider is { } spider) FireSpiderLance(spider);
    }

    /// <summary>
    /// Applies a fired lance to everything on its axis. Enemies are tested against the
    /// shaft directly (it pierces — one beam rakes a whole line of hunters); the two
    /// monsters' weak points are found by walking the ray and asking each of them the
    /// same question an ordinary bolt asks, so what the lance can hit is exactly what a
    /// round flying down the same line could hit, and no new geometry has to agree with
    /// the old geometry about where a core is.
    /// </summary>
    private void BurnSpiderLance(SpiderWeapon spider, float damage, int by = Projectile.NoOwner)
        => BurnBeamAlong(spider.BeamOrigin, spider.BeamDirection,
            spider.BeamLength, spider.BeamRadius, damage, by);

    /// <summary>
    /// Applies one fired beam of any owner's making to the world — the SPIDER's charged
    /// lance and each shaft of the worn crab's broken one both come through here, so what a
    /// beam can do is decided in exactly one place.
    /// </summary>
    private void BurnBeamAlong(Vector3 origin, Vector3 direction, float length, float radius,
        float damage, int by = Projectile.NoOwner)
    {
        var originXZ = new Vector2(origin.X, origin.Z);

        // A charged beam is a lance, not a round: what it meets, it fells. Which is the
        // payoff for whatever the shot cost — two seconds rooted in the open, or a slice
        // of the body the emitter is running on. One bite carries far more than any tower's
        // integrity, so a full lance drops on contact; the chips it sheds are the eruption
        // at the strike point, not a war of attrition.
        CutStructuresAlong(origin, direction, length, radius, LanceStructureDamage);

        // A hunter is treated as the standing block it is drawn as, not as a point:
        // the shaft has to pass within its planar radius *and* be somewhere between the
        // grid and the top of its hull where it does so. Which is what makes the two
        // ways of firing a lance genuinely different — a grounded beam rakes a whole
        // line of tanks, and a beam loosed from height sails clean over them on its way
        // to something's core.
        var dirXZ = new Vector2(direction.X, direction.Z);
        float planar = dirXZ.Length();
        if (planar > 1e-4f) dirXZ /= planar;
        float slope = planar > 1e-4f ? direction.Y / planar : 0f;

        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            // Nearest image across the torus, so a beam fired near the seam still burns
            // the hunter standing just over it.
            Vector2 near = Torus.NearestImage(e.Position, originXZ);
            float along = Math.Clamp(Vector2.Dot(near - originXZ, dirXZ), 0f, length);
            if (Vector2.Distance(near, originXZ + dirXZ * along)
                > radius + EnemyTank.Radius) continue;

            // Measured against where the hull actually is, which is the grid for the whole
            // of an ordinary hunter's life and is not while one is being carried in a claw
            // or is in the air after being thrown out of one.
            float beamY = origin.Y + slope * along;
            if (beamY < e.Height - radius
                || beamY > e.Height + EnemyTank.BodyHeight + radius) continue;

            DamageEnemy(e, damage, by);
        }

        // The same test against the squads, and the difference between the two is the whole
        // argument for a lance loosed upward: a beam raked along the grid burns a line of
        // hunters and passes harmlessly under the soldiers, and one fired up the face of a
        // tower does exactly the reverse.
        foreach (var s in Soldiers)
        {
            if (!s.Alive) continue;
            Vector2 near = Torus.NearestImage(s.Position, originXZ);
            float along = Math.Clamp(Vector2.Dot(near - originXZ, dirXZ), 0f, length);
            if (Vector2.Distance(near, originXZ + dirXZ * along)
                > radius + EnemySoldier.Radius) continue;

            float beamY = origin.Y + slope * along;
            float body = s.Height + EnemySoldier.AimHeight;
            if (MathF.Abs(beamY - body) > radius + EnemySoldier.BodyHeight * 0.5f) continue;

            DamageSoldier(s, damage);
        }

        // Step down the shaft looking for the two crystals. A one-unit stride is well
        // finer than either weak point's own reach, so nothing can sit between samples.
        const float step = 1f;
        bool hitCore = false, hitCrystal = false;
        for (float d = 0f; d <= length && !(hitCore && hitCrystal); d += step)
        {
            Vector3 at = origin + direction * d;
            var xz = new Vector2(at.X, at.Z);

            if (!hitCore && Boss is { } boss && boss.HitsCore(xz, at.Y))
            {
                hitCore = true;
                if (boss.DamageCore(damage)) DestroyBoss(boss);
                else Emit(Cue.CoreHit, boss.Position, 1f - boss.CoreFraction);
            }

            if (!hitCrystal && Maw is { } maw && maw.HitsCrystal(xz, at.Y))
            {
                hitCrystal = true;
                if (maw.DamageCrystal(damage)) DestroyMaw(maw);
                else Emit(Cue.MawHurt, maw.Position, 1f - maw.CrystalFraction);
            }
        }
    }

    // --- The SOLDIER chassis ----------------------------------------------------

    /// <summary>
    /// Walks the soldier out of the empty clearing and stands them in front of the
    /// nearest tower, facing it, one comfortable cable's throw away with the crosshair
    /// already resting on its flank. The very first thing that chassis can do is
    /// therefore press E, and the very first thing that happens is a hook biting stone.
    ///
    /// Picked as the tower nearest the origin rather than rolled, so the opening is the
    /// same place every run — the skyline is a fixed feature of this world, and where a
    /// class starts in it should be too.
    /// </summary>
    private void StandTheSoldierInTheCity(PlayerTank who)
    {
        Structure? nearest = null;
        float best = float.MaxValue;
        foreach (var s in Structures)
        {
            if (s.Kind != StructureKind.Tower) continue;
            float d = s.Position.LengthSquared();
            if (d < best) { best = d; nearest = s; }
        }
        if (nearest == null) return;   // a razed city (VOIDTANKS_STRUCTURES=0) — stay put

        // Stand off it on the origin's side, so the walk back out to open grid is behind
        // the player rather than through the building they are looking at.
        Vector2 away = nearest.Position.LengthSquared() > 1e-4f
            ? Vector2.Normalize(-nearest.Position)
            : new Vector2(0f, -1f);

        who.Position = Torus.Wrap(nearest.Position + away * SoldierStandoff);
        who.Heading = MathF.Atan2(-away.X, -away.Y);
        // Aimed a touch above level: the tower is forty metres of wall and the useful
        // anchors are up it, not at its feet.
        who.Pitch = 0.22f;
    }

    /// <summary>How far off the tower a soldier opens — comfortably inside a cable's
    /// reach, comfortably outside the footprint.</summary>
    private const float SoldierStandoff = 34f;

    // --- Seats -------------------------------------------------------------------

    /// <summary>
    /// Seats nobody is sitting in — people who walked out of the lobby before LAUNCH. The
    /// roster list itself stays dense (a seat is one byte on the wire and everything addresses
    /// a craft by its index, so seats can never be renumbered under a running session); these
    /// are simply the indices the next joiner is handed before a fresh one is appended.
    ///
    /// Without this, a room that five people wandered in and out of opened its match with a
    /// dozen abandoned craft standing on the grid, each still counting against the seat limit.
    /// Host-side: a client never vacates anything, it is told what every seat holds.
    /// </summary>
    private readonly HashSet<int> _vacant = new();

    /// <summary>How many seats actually have somebody in them.</summary>
    public int Occupied => Players.Count - _vacant.Count;

    /// <summary>True once the match is as full as the host said it could get. Counts people,
    /// not list entries — an abandoned seat is not somebody taking up room.</summary>
    public bool Full => Occupied >= Match.MaxPlayers;

    /// <summary>
    /// Host-side: nobody is in this seat any more. Used when a player leaves <em>before</em>
    /// the match starts, where there is nothing to hold for them — a mid-match drop keeps its
    /// craft frozen and waiting instead (see <see cref="PlayerTank.Away"/>).
    ///
    /// The craft is emptied rather than removed: no lives and no shield makes it
    /// <see cref="PlayerTank.Alive"/>-false, which every renderer, every tag, the spectator
    /// picker and every hunter in the game already reads as "not there" — so the seat goes
    /// quiet everywhere at once, including on clients, without a byte of new protocol.
    /// </summary>
    public void VacateSeat(int seat)
    {
        if ((uint)seat >= (uint)Players.Count) return;
        _vacant.Add(seat);
        PlayerTank gone = Players[seat];
        gone.Away = true;
        gone.Lives = 0;
        gone.Shield = 0f;
    }

    /// <summary>
    /// Client-side: makes sure <paramref name="seat"/> exists in the roster, appending
    /// placeholder craft up to it. Deliberately not <see cref="AddPlayer"/>, which reuses
    /// abandoned seats and so cannot be relied on to make the list longer — a growth loop
    /// built on it would spin for ever the first time a seat came free.
    /// </summary>
    public bool EnsureSeat(int seat)
    {
        if (seat < 0 || seat >= MatchSettings.MaxSeats) return false;
        while (Players.Count <= seat)
        {
            if (Players.Count >= Match.MaxPlayers) return false;
            OpenSeat(Players.Count, null);
        }
        return true;
    }

    /// <summary>
    /// What each seat is doing this tick. The host fills every entry — its own from the
    /// keyboard, everyone else's from the wire — and the step drives each craft from its
    /// own slot. A seat nobody has spoken for stays <see cref="InputFrame.Empty"/>, which
    /// is a player with their hands off the keys: the right answer both for a peer whose
    /// packet is late and for the nineteen seats a solo run never fills.
    /// </summary>
    private readonly InputFrame[] _inputs = new InputFrame[MatchSettings.MaxSeats];

    /// <summary>Files one seat's intent for the next step. Host-side this is where a
    /// client's input frame lands after it comes off the wire.</summary>
    public void SetInput(int seat, in InputFrame frame)
    {
        if ((uint)seat < (uint)_inputs.Length) _inputs[seat] = frame;
    }

    /// <summary>
    /// The living craft closest to a point — what everything that hunts should be asking
    /// for instead of "the player", now that there can be twenty of them.
    ///
    /// Spectators are not prey: a player who has spent their revives still has a position
    /// (their camera is somewhere) and enemies must not queue up to shoot at it. When
    /// nobody is left alive at all this falls back to the local craft rather than null, so
    /// that a field full of hunters with nothing to chase keeps stepping instead of
    /// throwing on the last player's death.
    /// </summary>
    public PlayerTank NearestPlayer(Vector2 to)
    {
        PlayerTank best = Players[LocalIndex];
        float bestSq = float.MaxValue;
        bool found = false;
        foreach (var p in Players)
        {
            if (!p.Alive || p.Away) continue;   // spectators and dropped players are not prey
            float d = Torus.DistanceSquared(p.Position, to);
            if (!found || d < bestSq) { best = p; bestSq = d; found = true; }
        }
        return best;
    }

    /// <summary>
    /// How far out from the origin the seats after the first are placed. Comfortably inside
    /// <see cref="StructureField.ClearRadius"/>, so twenty craft can open on clean grid
    /// without anyone spawning inside a tower — and wide enough that they are not stacked
    /// in each other's hulls on the first frame.
    /// </summary>
    private const float SeatRing = 26f;

    /// <summary>How far ahead of the host the joiners open — close enough that the two of you
    /// are looking at each other on the first frame, far enough not to be nose to nose.</summary>
    private const float SeatAhead = 16f;

    /// <summary>Sideways gap between joiners fanning out along the opening line.</summary>
    private const float SeatSpacing = 9f;

    /// <summary>
    /// Seats another player and hands back their craft, or null when the match is full.
    ///
    /// Host-only in a session: the host decides who is in which seat and tells everyone
    /// else. A client calling this would be inventing a player that exists nowhere but on
    /// its own screen.
    /// </summary>
    public PlayerTank? AddPlayer(Loadout? loadout = null)
    {
        if (Full) return null;
        if (Players.Count >= MatchSettings.MaxSeats && _vacant.Count == 0) return null;

        // A seat somebody abandoned in the lobby comes back into use before the roster is
        // made any longer, so a room people walk in and out of does not grow a tail of dead
        // craft. Lowest first, purely so seat numbers stay as tidy as they can.
        int seat = Players.Count;
        foreach (int free in _vacant) if (free < seat) seat = free;
        _vacant.Remove(seat);
        return OpenSeat(seat, loadout);
    }

    /// <summary>
    /// Builds the craft for one seat and puts it in the roster — appending if the seat is
    /// past the end, replacing if it is a vacancy being reused.
    ///
    /// The joiners open clustered just ahead of the host and facing back toward the origin,
    /// so on the first frame everyone can already see everyone — which is the whole point
    /// while there are only a few of them and you want to check the other craft is really
    /// there, really the right chassis, and really moving. They fan out along a short line
    /// rather than stacking: seat 1 dead ahead, seat 2 a lane to its left, seat 3 to its
    /// right, and so on, so nobody opens inside anybody.
    /// </summary>
    private PlayerTank OpenSeat(int seat, Loadout? loadout)
    {
        float lane = ((seat + 1) / 2) * SeatSpacing * ((seat & 1) == 1 ? 1f : -1f);
        var at = new Vector2(lane, SeatAhead);

        // Faced at the origin the host sits on, so the craft is looking back at seat 0.
        float bearing = MathF.Atan2(-at.X, -at.Y);

        var craft = new PlayerTank(Torus.Wrap(at), bearing, loadout ?? new Loadout())
        {
            Lives = Match.Revives + 1,
        };
        if (seat < Players.Count) Players[seat] = craft;
        else Players.Add(craft);

        // A reused seat starts clean: the pack the last occupant filled is not this
        // person's, and the salvage they collected should not be waiting for a stranger.
        if (seat < _inventories.Count) _inventories[seat] = new Inventory();

        // The two chassis that cannot open where everyone else does — see the constructor
        // for why a soldier starts in the city and a fish starts already swimming.
        if (craft.Soldier != null) StandTheSoldierInTheCity(craft);
        if (craft.Fish != null) SwimTheFishOffTheDeck(craft);
        return craft;
    }

    /// <summary>
    /// Swaps the craft in a seat for one of a different chassis, keeping its place in the
    /// roster. Client-side only: the host tells a client what everyone is driving through the
    /// snapshot, and a placeholder tank becomes the fish it always was the moment it is named.
    /// The new craft opens where the old one stood so it does not jump on the frame it changes.
    /// </summary>
    public void ReplacePlayer(int seat, PlayerClass chassis)
        => ReplacePlayer(seat, new Loadout { Class = chassis });

    /// <summary>
    /// Swaps the craft in a seat for one built from <paramref name="build"/> — its full paint
    /// as well as its chassis. Used both for a snapshot's placeholder-to-real swap (class only)
    /// and for installing a client's own chosen craft at the seat the host gave it (full build).
    /// Keeps the seat's place, position, height and lives so nothing jumps on the swap.
    /// </summary>
    public void ReplacePlayer(int seat, Loadout build)
    {
        if ((uint)seat >= (uint)Players.Count) return;
        PlayerTank old = Players[seat];
        Players[seat] = new PlayerTank(old.Position, old.Heading, build)
        {
            Height = old.Height,
            Lives = old.Lives,
        };
    }

    /// <summary>
    /// Whether <paramref name="shooter"/>'s round is allowed to hurt <paramref name="target"/>.
    /// Always false for a craft and itself — a player's own splash has never hurt them and
    /// turning friendly fire on must not change that — and otherwise the host's toggle.
    /// </summary>
    public bool CanHarm(PlayerTank shooter, PlayerTank target)
        => !ReferenceEquals(shooter, target) && Match.FriendlyFire;

    /// <summary>
    /// How far the mouse turns the look, in radians per pixel. Tuned against the
    /// internal 320×240 rather than against the window: the whole picture is three
    /// hundred pixels wide, so a sweep that feels ordinary in a modern shooter throws
    /// the view most of the way round the world here.
    /// </summary>
    private const float LookSensitivity = 0.0032f;

    /// <summary>Movement past this in one frame is a pointer warp, not a hand, and the
    /// whole frame's look is discarded. In pixels.</summary>
    private const float MaxLookJump = 180f;

    /// <summary>
    /// Where the crosshair's ray last landed, if it landed on anything — the point a
    /// hook fired this frame would bite. Read by the HUD, which brackets the crosshair
    /// on a valid anchor and greys it out otherwise, and by nothing in the sim: firing
    /// re-casts rather than trusting a cached answer, so a hook can never bite something
    /// that stopped existing between the frame and the trigger.
    /// </summary>
    public Vector3? AnchorInSight { get; private set; }

    /// <summary>True when the anchor in sight is weak enough to tear out under load —
    /// the HUD warns rather than letting the player find out mid-arc.</summary>
    public bool AnchorIsWeak { get; private set; }

    /// <summary>
    /// Mouse look, for every chassis. The yaw turns the whole craft — it runs into the
    /// shared <see cref="PlayerTank.Heading"/> the same way on a body and a machine alike
    /// — and the pitch cranes the eye as far as that chassis allows (see
    /// <see cref="PlayerTank.Look"/>). Everything downstream reads the same
    /// <see cref="PlayerTank.Forward"/> and <see cref="PlayerTank.Forward3"/>.
    ///
    /// The sign is the one thing here worth stating: the camera renders +X on the
    /// <em>left</em> of the screen, so a heading increase swings the view left, and
    /// pushing the mouse right therefore has to <em>decrease</em> it — hence both deltas
    /// go in negated.
    /// </summary>
    private void UpdateMouseLook(in InputFrame input, PlayerTank who)
    {
        Vector2 delta = input.LookDelta;
        if (delta == Vector2.Zero) return;

        // A capture being taken or handed back — opening the pack, tabbing away, the
        // frame the cursor is locked to the window — reports one enormous jump as the
        // pointer is warped to the centre. That is not input and must not be treated as
        // any amount of input: clamping it still throws the view a quarter turn, so a
        // frame that reports a movement no hand could make is dropped outright. The
        // threshold sits well above a genuine fast flick.
        if (MathF.Abs(delta.X) > MaxLookJump || MathF.Abs(delta.Y) > MaxLookJump) return;

        who.Look(-delta.X * LookSensitivity, -delta.Y * LookSensitivity);
    }

    /// <summary>
    /// The SOLDIER's buttons: the two hooks, the gas jump, the rifle and the rockets.
    /// Also re-casts the crosshair every frame so the HUD can bracket a valid anchor —
    /// reading the city at a glance, mid-flight, is the skill this class is built on,
    /// and it needs the answer before the player commits, not after.
    /// </summary>
    private void UpdateSoldierTriggers(SoldierRig rig, float dt, in InputFrame input, PlayerTank who)
    {
        if (!UpdateCableKit(rig, input, who)) return;

        // Rockets before the rifle: on a frame both are down, the deliberate shot wins.
        // They share the fire cooldown, and a player holding fire while clicking for a
        // rocket should get the rocket rather than have it eaten by the stream.
        if (input.RocketPressed) FireSoldierRocket(who);
        else if (input.RifleDown) FireSoldierRifle(who);
    }

    /// <summary>
    /// The cable kit's own controls: the crosshair's anchor cast, the two hooks and the gas
    /// jump. Everything that belongs to the <em>rig</em> rather than to whoever is holding
    /// it, which is why it is a function of its own — the SOLDIER chassis owns one of these
    /// and a VIRUS wearing a stolen body owns one too, and neither should be flying on a
    /// second copy of this logic that drifts away from the first.
    /// </summary>
    /// <returns>False when a cinematic has the player and nothing else should be read.</returns>
    private bool UpdateCableKit(SoldierRig rig, in InputFrame input, PlayerTank who)
    {
        // A cinematic has hold of the player: both cables go, and nothing else is read.
        // Being swung around by a monster while still anchored to a tower is not a
        // situation the constraint solver has any sensible answer for.
        if (who.Captured)
        {
            rig.ReleaseBoth();
            AnchorInSight = null;
            return false;
        }

        Vector3 eye = who.Eye;
        Vector3 look = who.Forward3;

        bool found = TryFindAnchor(eye, look, out Vector3 at, out Structure? holding);

        // The crosshair's answer is a HUD readout, and this machine has one crosshair. The
        // host runs this for every seat, so writing the world's fields from a remote player's
        // aim would put somebody else's anchor bracket on our screen — and, worse, hand our
        // own hook their anchor when we fired (the hook used to read the field rather than
        // the cast, which is exactly that bug). The cast is passed through explicitly below.
        if (ReferenceEquals(who, Players[LocalIndex]))
        {
            AnchorInSight = found ? at : null;
            AnchorIsWeak = found && holding is { } h && h.Scale < SoldierRig.WeakScale;
        }

        // E and Q are the same button twice: throw if stowed, let go if out. Which is
        // what makes the alternating chain — fire E, swing, fire Q, release E, re-anchor
        // E further ahead — a rhythm on two keys rather than a chord on four.
        if (input.RightHookPressed) ToggleSoldierHook(who, rig, right: true, look, found, at, holding);
        if (input.LeftHookPressed) ToggleSoldierHook(who, rig, right: false, look, found, at, holding);

        if (input.HighJumpPressed && rig.Jump(who))
        {
            Emit(Cue.GasJump, who.Position, rig.Starvation, owner: Seat(who));
            // The ring of dust blasted out from under the launch.
            Debris.FootPuff(new Vector3(who.Position.X, 0f, who.Position.Y));
        }

        return true;
    }

    /// <summary>
    /// One hook's key press. Fires it at whatever the crosshair is on, or — if it is
    /// already flying or anchored — brings it home. A shot at nothing still goes: the
    /// cable pays out its full reach and zips back, which is the honest feedback that
    /// the player aimed at sky.
    /// </summary>
    private void ToggleSoldierHook(PlayerTank who, SoldierRig rig, bool right, Vector3 look,
        bool found, Vector3 at, Structure? holding)
    {
        GrappleHook hook = rig.Hook(right);
        int seat = Seat(who);

        if (hook.Out)
        {
            rig.ReleaseHook(right);
            Emit(Cue.CableZip, hook.Tip, owner: seat);
            return;
        }

        // Fired from the hip on the matching side rather than from the eye, so the two
        // cables visibly leave different points on the body and cross where they should.
        // Off <paramref name="who"/>'s hip, not this machine's player's: the host runs this
        // for every seat, and a remote soldier's cable used to leave the host's own body.
        Vector3 from = SoldierMuzzle(who, right);
        rig.FireHook(right, new Vector2(from.X, from.Z), from.Y, look,
            found ? at : null, found ? holding : null);
        Emit(Cue.CableFire, who.Position, owner: seat);
    }

    /// <summary>
    /// Where a cable leaves the rig: out from the hip on its own side, at belt height.
    /// Shared by the sim (which fires from here) and the renderer (which draws from
    /// here), so the line the player sees is the line the hook actually flew.
    /// </summary>
    public Vector3 SoldierMuzzle(bool right) => SoldierMuzzle(Player, right);

    /// <summary>As above, for a named seat's body — what the host needs when it fires a
    /// hook on behalf of a remote player.</summary>
    public Vector3 SoldierMuzzle(PlayerTank who, bool right)
    {
        Vector2 fwd = who.Forward;
        var side = new Vector2(-fwd.Y, fwd.X) * (right ? 0.55f : -0.55f);
        return new Vector3(
            who.Position.X + side.X + fwd.X * 0.4f,
            who.Height + SoldierRig.ShoulderHeight,
            who.Position.Y + side.Y + fwd.Y * 0.4f);
    }

    /// <summary>
    /// Walks the crosshair's ray out through the city looking for something to bite.
    /// Returns the point on the surface it found and which building it belongs to.
    ///
    /// Only the skyline is an anchor. Not the grid — a hook that could always bite the
    /// floor would make every one of these decisions free — and not the monsters, which
    /// would be a different game. That the city is the <em>only</em> thing holding the
    /// player up is what makes a rocket fired at the wrong tower a genuine mistake.
    ///
    /// Marched rather than solved, for the same reason the spider's lance walks its
    /// shaft: a half-metre stride is far finer than any footprint out there, it handles
    /// a ray that climbs or dives without a second case, and it costs a few hundred
    /// distance tests against the handful of buildings actually in front of the player.
    /// </summary>
    public bool TryFindAnchor(Vector3 origin, Vector3 direction,
        out Vector3 point, out Structure? holding)
    {
        point = default;
        holding = null;

        Vector3 d = Vector3.Normalize(direction);
        var originXZ = new Vector2(origin.X, origin.Z);
        var dirXZ = new Vector2(d.X, d.Z);

        // Everything the ray could possibly reach, gathered once so the march itself
        // only ever tests a handful of circles.
        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        Span<(Vector2 At, float Radius, float Top, int Owner)> near =
            stackalloc (Vector2, float, float, int)[MaxAnchorCandidates];
        int count = 0;

        for (int i = 0; i < Structures.Count && count < MaxAnchorCandidates; i++)
        {
            Structure s = Structures[i];
            int n = s.Blockers(blockers);
            for (int b = 0; b < n && count < MaxAnchorCandidates; b++)
            {
                var (at, r) = blockers[b];
                float reach = SoldierRig.MaxRange + r;
                if (Torus.DistanceSquared(at, originXZ) > reach * reach) continue;
                // Held as the nearest image so the march never has to wrap: a building
                // just over the seam is tested where the player can actually see it.
                near[count++] = (Torus.NearestImage(at, originXZ), r, s.BlockHeight, i);
            }
        }
        if (count == 0) return false;

        for (float t = MinAnchorRange; t <= SoldierRig.MaxRange; t += AnchorStep)
        {
            float y = origin.Y + d.Y * t;
            if (y < 0f) return false;             // the ray has gone into the grid

            Vector2 xz = originXZ + dirXZ * t;

            for (int i = 0; i < count; i++)
            {
                var (at, r, top, owner) = near[i];
                if (y > top) continue;
                if (Vector2.DistanceSquared(xz, at) > r * r) continue;

                // Pull the bite back out onto the surface rather than leaving it at the
                // sample point inside the wall, so the cable ends on the building and
                // the swing radius is the one the player can see.
                Vector2 outward = xz - at;
                float len = outward.Length();
                Vector2 surface = len > 1e-4f ? at + outward / len * r : at + new Vector2(r, 0f);

                point = new Vector3(surface.X, MathF.Max(1f, y), surface.Y);
                holding = Structures[owner];
                return true;
            }
        }

        return false;
    }

    /// <summary>Stride the anchor march walks the ray in. Well under the tightest
    /// footprint out there, so nothing can sit between two samples.</summary>
    private const float AnchorStep = 0.5f;

    /// <summary>No anchoring closer than this. A hook that bit the wall you are already
    /// scraping along would be a hook that does nothing.</summary>
    private const float MinAnchorRange = 4f;

    /// <summary>How many footprints the march will consider at once. Comfortably more
    /// than the city ever puts inside one cable's reach.</summary>
    private const int MaxAnchorCandidates = 16;

    /// <summary>
    /// Capture harness only: a WASD to drive the rig with instead of the keyboard's.
    /// Screenshotting this chassis means holding a reel in for a second or two while a
    /// swing builds, and there is nobody at the keys during a capture run.
    /// </summary>
    public Vector2? ScriptedSoldierMove;

    /// <summary>Pulls the rifle's trigger without a mouse — the capture harness and the
    /// self-test's way in. Honours the cooldown and the magazine exactly as a click
    /// does, so a burst driven through here is a burst the player could fire.</summary>
    public void FireSoldierRifleForTest() => FireSoldierRifle();

    /// <summary>The same for a rocket.</summary>
    public void FireSoldierRocketForTest() => FireSoldierRocket();

    /// <summary>
    /// Throws one hook at whatever the eye is currently pointed at, without waiting on a
    /// key edge — the headless self-test's way in, since it has no mouse. Returns whether
    /// there was anything out there to bite; a false still throws the cable, exactly as
    /// pressing the key at open sky does.
    /// </summary>
    public bool FireSoldierHookForTest(bool right)
    {
        if (Player.Soldier is not { } rig) return false;

        Vector3 eye = Player.Eye;
        Vector3 look = Player.Forward3;
        bool found = TryFindAnchor(eye, look, out Vector3 at, out Structure? holding);

        Vector3 from = SoldierMuzzle(right);
        rig.FireHook(right, new Vector2(from.X, from.Z), from.Y, look,
            found ? at : null, found ? holding : null);
        return found;
    }

    /// <summary>
    /// Drains the rig's events after the physics have run: the cues, the dust, and the
    /// damage a landing or a crash costs. Split out from the rig itself so that the
    /// physics stay a pure function of their own state and the world keeps sole
    /// ownership of hurting the player.
    /// </summary>
    private void UpdateSoldierEvents(PlayerTank who, SoldierRig rig, float dt)
    {
        int seat = Seat(who);
        // The beds belong to the listener, not to the rig: this machine's ears are attached to
        // one craft, so only that craft's reel, wind and cable strain are fed. A remote
        // soldier's noise reaches here as one-shots off the wire, not as a second wind track.
        bool local = ReferenceEquals(who, Players[LocalIndex]);

        // Both hooks, named rather than iterated: this runs every tick of every frame,
        // and an array built to walk two fields is two fields' worth of garbage a tick.
        SoundHookEvents(seat, rig.Left);
        SoundHookEvents(seat, rig.Right);

        if (local)
        {
            // The gas jet while it pulls, the wind past the ears, and the steel creaking
            // under whatever weight it is carrying. All three are beds, fed every tick with
            // whatever the rig is doing and left to fade on their own the moment it stops —
            // see Audio.Update, which clears them after servicing.
            Audio.SetReel(rig.Reeling, 1f - rig.Starvation);
            Audio.SetWind(rig.PlanarSpeed > WindSpeed,
                Math.Clamp((rig.PlanarSpeed - WindSpeed) / 18f, 0f, 1f));

            float strain = MathF.Max(rig.Left.Taut ? rig.Left.Tension : 0f,
                                     rig.Right.Taut ? rig.Right.Tension : 0f);
            Audio.SetCableStrain(strain > 0f, strain);

            // And the tick that says the bottle is nearly out. Rate-limited inside Audio, so
            // it is safe to ask for on every tick the gauge is in the red.
            if (who.Hyper < GasWarnLevel && !rig.Grounded) Audio.PlayGasLow();   // local bed-adjacent tick
        }

        if (rig.JustCrashed)
        {
            Emit(Cue.CrashLanding, who.Position, owner: seat);
            DamagePlayer(CrashDamage, who);
            Debris.Burst(new Vector3(who.Position.X, who.Height + 1f, who.Position.Y),
                Palette.StructureShell, elite: false);
        }

        if (rig.JustLanded && rig.LandingSpeed > SoldierRig.HardLanding)
        {
            Emit(Cue.CrashLanding, who.Position, owner: seat);
            Debris.FootPuff(new Vector3(who.Position.X, 0f, who.Position.Y));

            // Anything past a twenty-metre drop is paid for in shield, scaled by how far
            // past it went — a bad landing hurts, a catastrophic one nearly ends you.
            if (rig.LandingSpeed > SoldierRig.FallDamageSpeed)
            {
                float over = (rig.LandingSpeed - SoldierRig.FallDamageSpeed)
                           / (SoldierRig.FallDamageSpeed * 0.6f);
                DamagePlayer(FallDamageBase + FallDamageBase * Math.Clamp(over, 0f, 2f), who);
            }
        }
    }

    /// <summary>
    /// Voices one hook's single-frame events: the clank of a bite, the zip of a miss,
    /// and the splintering crack of an anchor tearing out — with a spray of masonry off
    /// the point it tore from, so the failure is seen as well as heard.
    ///
    /// Positioned at the hook's tip rather than at the body, which is the whole point of
    /// sending it: an anchor biting a tower forty metres away should be heard over there.
    /// </summary>
    private void SoundHookEvents(int seat, GrappleHook h)
    {
        if (h.JustBit)
            Emit(Cue.AnchorBite, h.Tip, owner: seat);
        if (h.JustMissed)
            Emit(Cue.CableZip, h.Tip, owner: seat);
        if (h.JustTore)
        {
            Emit(Cue.AnchorTear, h.Tip, owner: seat);
            Debris.Burst(new Vector3(h.Tip.X, h.TipY, h.Tip.Y), Palette.StructureShell, elite: false);
        }
    }

    /// <summary>Planar speed past which the wind is audible at all.</summary>
    private const float WindSpeed = 12f;

    /// <summary>Reserve below which the rig starts ticking a warning — about one jump's
    /// worth left, which is the only amount worth being told about.</summary>
    private const float GasWarnLevel = 26f;

    /// <summary>What swinging into a wall costs. Well short of lethal on a full shield:
    /// the real punishment for a crash is the momentum, and momentum is this chassis's
    /// only currency.</summary>
    private const float CrashDamage = 14f;

    /// <summary>What the mildest damaging landing costs, before the overshoot scaling.</summary>
    private const float FallDamageBase = 12f;

    /// <summary>
    /// A rifle round down the eye's line. Cheap, fast, and — the point of the whole
    /// weapon — entirely usable mid-swing: nothing here touches the rig, so firing never
    /// breaks an arc.
    /// </summary>
    private void FireSoldierRifle(PlayerTank? by = null)
    {
        // Null is the craft this machine is driving — what every caller before seats meant.
        PlayerTank who = by ?? Player;
        if (HeldInClaw(who)) return;
        if (!who.TryFireRifle(out Vector3 origin, out Vector3 dir)) return;

        if (SwallowedIn(who) is { Held: true } digestion)
        {
            digestion.RegisterShot();
            Emit(Cue.RifleShot, who.Position, owner: Seat(who));
            return;
        }

        SpawnDirected(origin, dir, Projectile.RifleSpeed, rocket: false, owner: Seat(who));
        Emit(Cue.RifleShot, who.Position, owner: Seat(who));

        if (who.Soldier is not { } rig) return;

        // The punch: a small nudge up the view, and a shell case tumbling out past the
        // ear. Both exist for the same reason — a weapon that produced a tracer and
        // nothing else would read as a cursor emitting dots.
        rig.Kick(0.012f);

        Vector2 fwd = who.Forward;
        var side = new Vector2(-fwd.Y, fwd.X) * (rig.FlashOnRight ? 1f : -1f);
        Debris.Fleck(
            who.Eye + new Vector3(side.X, -0.15f, side.Y) * 0.5f,
            new Vector3(side.X * 3.4f, 1.6f, side.Y * 3.4f)
                + new Vector3(-fwd.X, 0f, -fwd.Y) * 1.8f
                + new Vector3(rig.Velocity.X, 0f, rig.Velocity.Z),
            Palette.Flag, life: 0.45f);
    }

    /// <summary>
    /// A rocket. Contact-fused, so the world's ordinary projectile pass detonates it the
    /// instant it meets anything — including the grid, which a round fired down the line
    /// of a dive will find quickly.
    /// </summary>
    private void FireSoldierRocket(PlayerTank? by = null)
    {
        // Null is the craft this machine is driving — what every caller before seats meant.
        PlayerTank who = by ?? Player;
        if (HeldInClaw(who) || SwallowedIn(who) is { Held: true }) return;
        if (!who.TryFireRocket(out Vector3 origin, out Vector3 dir)) return;

        SpawnDirected(origin, dir, Projectile.RocketSpeed, rocket: true, owner: Seat(who));
        Emit(Cue.RocketLaunch, who.Position, owner: Seat(who));
    }

    private void SpawnDirected(Vector3 origin, Vector3 dir, float speed, bool rocket,
        bool acid = false, int owner = 0, bool seeds = false, bool fromAlly = false)
    {
        foreach (var p in _projectiles)
        {
            if (p.Active) continue;
            p.FireDirected(origin, dir, speed, rocket, acid, owner, seeds, fromAlly);
            return;
        }
    }

    // --- The SOLDIER squads (the enemy that flies) --------------------------------
    //
    // Everything else that hunts the player drives. These do not: they are people with the
    // same twin launchers the SOLDIER chassis carries, and the city is their floor. The
    // world's job here is small and strictly bounded — answer the one question they ask it
    // (what can I hang from, in the direction I want to go), keep them out of the walls,
    // and own every consequence: their rounds, their blades, and what killing one leaves
    // behind. The flying itself is entirely theirs, in EnemySoldier.

    /// <summary>
    /// Steps every squad and then every soldier in them. The squads think first because
    /// their orders are this tick's input to the members, not last tick's — a soldier told
    /// to strike should leave on the tick they were told, not the one after.
    /// </summary>
    private void UpdateSquads(float dt)
    {
        if (_coverBlown > 0f) _coverBlown -= dt;

        // The escort only stands while the body it was earned with is still being worn. Take
        // the host off — rot it out, spend it, lose it to a hunter — and the three people
        // flying beside you are, on the very next tick, three people who have just watched a
        // mote of corruption climb out of their friend.
        // The escort follows the seat that earned it, not this machine's craft: it was
        // bought by one player flying in a stolen body, and it ends when THAT body does.
        if (_escortFor?.Virus is not { HostKind: VirusHost.Soldier }) { _escort = null; _escortFor = null; }
        if (_escort != null && !Squads.Contains(_escort)) { _escort = null; _escortFor = null; }

        foreach (var squad in Squads)
        {
            bool escorting = ReferenceEquals(squad, _escort);
            squad.SetEscort(escorting);
            foreach (var m in squad.Members) m.Allied = escorting;

            // Whoever is nearest the squad, exactly as the hunters pick their quarry. This
            // used to be the local craft flat out, so on the host a squad only ever flew at
            // the host — nineteen other players could stand in the open and never be hunted.
            PlayerTank quarry = NearestPlayer(squad.Members.Count > 0
                ? squad.Members[0].Position : Players[LocalIndex].Position);
            squad.Update(dt, quarry.Position,
                targetVisible: escorting || !PassesForOneOfThem(quarry));
            // Going loud. One call for the whole squad, pitched by how far away the nearest
            // of them is, so four figures dropping off a spire two hundred metres out is a
            // sound on the horizon rather than a shout in your ear.
            if (squad.JustCalled && squad.Members.Count > 0)
                Emit(Cue.HuntCall, squad.Members[0].Position);
        }

        foreach (var s in Soldiers)
        {
            if (!s.Alive) continue;

            // The tower they were sitting on is coming down, or has had a hole shot through
            // it: they go with it, which is to say they let go of it and start flying,
            // which is the only sane thing a person clinging to a failing building can do.
            // Their cables answer the same question for themselves, a tick at a time, in
            // EnemySoldier.StepHook — this is only the perch, which is a stance rather than
            // a constraint and so has nothing else watching it.
            if (s.PerchedOn is { } wall && (wall.Falling || wall.Gone)) s.LosePerch();

            // Who this one is working against. For an enemy squad it is always the player;
            // for a carrier or an escort it is whichever of the other side is nearest, which
            // is the whole of what changing sides does — the brain is identical, and only the
            // target moved.
            // Each of them picks the nearest craft for themselves, so a squad that has
            // spread across a street splits onto whoever is in front of them rather than all
            // four converging on one player.
            PlayerTank target = NearestPlayer(s.Position);
            Vector2 mark = target.Position;
            float markY = target.Height;
            bool starved = false;
            bool holdFire = false;

            if (s.Carrier || s.Allied)
            {
                if (NearestPrey(s) is { } prey)
                {
                    mark = prey.At;
                    markY = prey.Height;
                }
                else if (s.Carrier)
                {
                    // Nothing left of the side they turned on. The corruption has nowhere to
                    // go and takes the body apart instead — a carrier is never left circling
                    // a player it has no quarrel with.
                    starved = true;
                }
                else
                {
                    // An escort with nothing to shoot at flies formation instead: the ring
                    // they already work, kept around the player, with their fingers off the
                    // triggers. Which is exactly what the ring was always for — it is the
                    // same four arcs sweeping the same circle, and the only thing that has
                    // changed is that the thing in the middle of it is on their side.
                    holdFire = true;
                }
            }

            s.Update(dt, mark, markY, this, starved, holdFire);
            KeepSoldierOutOfTheCity(s);
            DrainSoldierEvents(s, target, mark, markY);
        }
    }

    /// <summary>
    /// What anybody fighting on the player's side hunts: the nearest thing still fighting for
    /// the other one — a hunter on the grid, or a soldier who is neither turned nor escorting.
    /// Explicitly not the player and explicitly not each other, so a plague and an escort in
    /// the same sky sort themselves out rather than opening fire on one another.
    /// </summary>
    private (Vector2 At, float Height)? NearestPrey(EnemySoldier hunter)
    {
        (Vector2, float)? best = null;
        float bestSq = CarrierHuntRange * CarrierHuntRange;

        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            float d = Torus.DistanceSquared(e.Position, hunter.Position);
            if (d >= bestSq) continue;
            bestSq = d;
            best = (e.Position, EnemyTank.AimHeight);
        }

        foreach (var s in Soldiers)
        {
            if (!s.Alive || s.Carrier || s.Allied || ReferenceEquals(s, hunter)) continue;
            float d = Torus.DistanceSquared(s.Position, hunter.Position);
            if (d >= bestSq) continue;
            bestSq = d;
            best = (s.Position, s.Height);
        }

        return best;
    }

    /// <summary>How far a carrier will look for something of its own side to turn on. Wide:
    /// a body with a few seconds left should spend them crossing the arena at somebody, not
    /// standing around because the nearest target was over the fog line.</summary>
    private const float CarrierHuntRange = 220f;

    /// <summary>
    /// Spends one soldier's single-frame events: the cues off their cables, the dust off a
    /// launch or a bad landing, the round they just loosed, and the blades. Split out from
    /// the entity for the same reason the player's rig is — the physics stay a pure
    /// function of their own state, and the world keeps sole ownership of hurting anybody.
    /// </summary>
    private void DrainSoldierEvents(EnemySoldier s, PlayerTank target, Vector2 mark, float markY)
    {
        float range = Torus.Distance(s.Position, target.Position);

        // The tick the corruption takes root. One cue, and a spray of the stuff off the body
        // it has just claimed.
        if (s.JustTurned)
        {
            Emit(Cue.UnstableLance, s.Position);
            Debris.Burst(new Vector3(s.Position.X, s.Height + EnemySoldier.AimHeight, s.Position.Y),
                Palette.NeonMagenta, elite: true);
        }

        SoundEnemyHook(s.Left);
        SoundEnemyHook(s.Right);

        if (s.JustLaunched)
        {
            Emit(Cue.GasJump, s.Position, 0.35f);
            if (s.Height < 1.5f)
                Debris.FootPuff(new Vector3(s.Position.X, 0f, s.Position.Y));
        }

        if (s.JustLanded && s.LandingSpeed > SoldierRig.HardLanding)
        {
            Emit(Cue.CrashLanding, s.Position);
            Debris.FootPuff(new Vector3(s.Position.X, 0f, s.Position.Y));
        }

        // The rifle. Routed through the same directed-round path the player's own rifle
        // uses — it is the same weapon — and stopped dead by a TANK's screening smoke,
        // which blinds them exactly as it blinds a hunter.
        //
        // A carrier's round goes out flagged as the player's, and that one bool is the whole
        // of which side they are on: it makes the round bite hunters and their own former
        // squad through the paths that already exist, and makes it pass harmlessly through
        // the player. The kills even pay out salvage, because as far as the world is
        // concerned the mote that turned them fired it.
        bool friendly = s.Carrier || s.Allied;
        if (s.JustFired && (friendly || (!target.Captured && !PassesForOneOfThem(target)
                && !SmokeBlocks(s.Position, target.Position))))
        {
            SpawnDirected(s.ShotOrigin, s.ShotDir, Projectile.RifleSpeed,
                rocket: false, acid: false,
                owner: friendly ? Projectile.AllyOwner : Projectile.NoOwner, fromAlly: friendly);
            Emit(Cue.RifleShot, s.Position);
        }

        if (friendly) StrikeFriendlyBlades(s, mark, markY);
        else StrikeWithBlades(s, target, range);
    }

    /// <summary>
    /// A friendly pass � a carrier's or an escort's � scored against whatever they are
    /// fighting rather than against the player. Deliberately worth more than their rifle: a
    /// soldier diving into the squad they arrived with is the picture this whole mechanic
    /// exists to produce, and it should visibly take somebody apart when it lands.
    /// </summary>
    private void StrikeFriendlyBlades(EnemySoldier s, Vector2 mark, float markY)
    {
        if (!s.BladesOut || !s.BladeReady) return;
        if (s.PlanarSpeed < EnemySoldier.BladeSpeed) return;
        if (Torus.DistanceSquared(s.Position, mark)
            > EnemySoldier.BladeReach * EnemySoldier.BladeReach) return;
        if (markY < s.Height - BladeVertical
            || markY > s.Height + EnemySoldier.BodyHeight + BladeVertical) return;

        s.RegisterSlash();
        Emit(Cue.ClawSlam, s.Position);
        Debris.Burst(new Vector3(mark.X, markY + 1f, mark.Y), Palette.NeonMagenta, elite: true);

        // Whichever of the other side was standing there. Hunters first — they are the
        // bigger target and the likelier one — then the squad they used to belong to.
        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            if (!WithinHit(mark, e.Position, EnemyTank.Radius)) continue;
            DamageEnemy(e, CarrierBladeDamage);
            return;
        }
        foreach (var other in Soldiers)
        {
            if (!other.Alive || other.Carrier || other.Allied || ReferenceEquals(other, s)) continue;
            if (!WithinHit(mark, other.Position, EnemySoldier.Radius)) continue;
            DamageSoldier(other, CarrierBladeDamage);
            return;
        }
    }

    /// <summary>What a turned soldier's pass costs whatever it lands on. Enough to erase a
    /// hunter outright — the plague is not a distraction, it is a weapon.</summary>
    private const float CarrierBladeDamage = 4f;

    /// <summary>Voices one enemy hook's events at whatever the range earns. Through the cue
    /// channel, so a client hears the squad swinging through the city over its shoulder — the
    /// steel biting was audible on the host's machine and nowhere else.</summary>
    private void SoundEnemyHook(GrappleHook h)
    {
        if (h.JustBit) Emit(Cue.AnchorBite, h.Tip);
        if (h.JustTore)
        {
            Emit(Cue.AnchorTear, h.Tip);
            Debris.Burst(new Vector3(h.Tip.X, h.TipY, h.Tip.Y), Palette.StructureShell, elite: false);
        }
    }

    /// <summary>
    /// The pass. A soldier on a committed run who arrives at the player's body at speed
    /// opens them up — the one attack in this game that is delivered by a <em>trajectory</em>
    /// rather than by a projectile, which is why it is checked here, after everything has
    /// moved, and why every part of it is a movement problem: they have to be close, they
    /// have to be level, and they have to still be carrying the arc that got them there. A
    /// player who broke the geometry of the run in any of those three ways has dodged it.
    /// </summary>
    private void StrikeWithBlades(EnemySoldier s, PlayerTank target, float range)
    {
        if (!s.BladesOut || !s.BladeReady || target.Captured) return;
        if (PassesForOneOfThem(target)) return;   // they are not running at one of their own
        if (s.PlanarSpeed < EnemySoldier.BladeSpeed) return;
        if (range > EnemySoldier.BladeReach + PlayerTank.Radius) return;

        // Level with them, give or take. Measured against the whole body rather than
        // against a point on it, because they are twice a person's size and a run at
        // something standing on the grid arrives with the blades somewhere down the length
        // of them — a soldier who screams past overhead has missed and reads as having
        // missed, and one whose boots go through you has not.
        float mine = target.Height + EnemyTank.AimHeight;
        if (mine < s.Height - BladeVertical
            || mine > s.Height + EnemySoldier.BodyHeight + BladeVertical) return;

        s.RegisterSlash();
        DamagePlayer(SoldierBladeDamage, target);
        Emit(Cue.ClawSlam, s.Position);
        JoltPlayerView(target, 0.65f);
        Debris.Burst(new Vector3(target.Position.X, mine, target.Position.Y),
            Palette.SoldierBlade, elite: true);
    }

    /// <summary>
    /// Throws the player's view about, whichever chassis they are in. Every one of them
    /// carries its own shake and the camera takes the largest, so this hands the jolt to the
    /// one that is actually driving rather than to all of them at once — which would be a
    /// way of setting a number on three objects that nothing on the live path rings down.
    /// </summary>
    private void JoltPlayerView(PlayerTank who, float amount)
    {
        if (who.Rig is { } rig) rig.Jolt(amount);
        else if (who.Fish is { } body) body.Jolt(amount);
        else if (who.Virus is { } mote) mote.Jolt(amount);
        else who.Jolt(amount);
    }

    /// <summary>How far off level a pass can be and still cut. Roughly a body's height
    /// either way, so ducking under a run genuinely works.</summary>
    private const float BladeVertical = 2.4f;

    /// <summary>
    /// Keeps a soldier out of the solid parts of the skyline, exactly as the craft is kept
    /// out of them. Unlike the craft they spend the whole run at the height of the walls
    /// they are swinging past, so this runs far more often for them than it ever does for
    /// the player — and a fast arrival is a genuine crash that costs them everything they
    /// had built and leaves them hanging there, staggered, for a second and a half.
    /// </summary>
    private void KeepSoldierOutOfTheCity(EnemySoldier s)
    {
        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];

        foreach (var st in Structures)
        {
            if (s.Height > st.BlockHeight) continue;

            int n = st.Blockers(blockers);
            for (int b = 0; b < n; b++)
            {
                var (at, radius) = blockers[b];
                float reach = radius + EnemySoldier.Radius;

                Vector2 out_ = Torus.Delta(at, s.Position);
                float distSq = out_.LengthSquared();
                if (distSq >= reach * reach) continue;

                Vector2 outward = distSq > 1e-6f
                    ? out_ / MathF.Sqrt(distSq)
                    : new Vector2(1f, 0f);

                s.Position = Torus.Wrap(at + outward * reach);
                // Handed the direction they were shoved, so the rig can keep whatever was
                // running along the wall and lose only what was going into it.
                s.RegisterWallHit(outward);
            }
        }
    }

    // --- What the city offers a flier ---------------------------------------------

    /// <summary>
    /// The one question a soldier asks the world: given where I am and where I want to go,
    /// what can I hang from?
    ///
    /// Answered as a direct query rather than as the marched ray the player's crosshair
    /// uses, because the two are asking genuinely different things. A player asks "what is
    /// under my crosshair"; an AI asks "what is the <em>best</em> thing in a whole
    /// half-space", which a ray cannot answer and which a few dozen circle tests can.
    ///
    /// What makes the answer read as expertise is the scoring, and it is worth stating
    /// plainly, because it is the entire difference between a soldier and a thing on a
    /// string. A good anchor is (a) ahead along the wish, because a cable behind you brakes,
    /// (b) at a comfortable throw rather than at arm's length or at the limit of the reach,
    /// (c) high enough above the flier to swing <em>under</em>, and (d) solid — the smallest
    /// quarter of the skyline tears out under load, and something that has done this for a
    /// living does not bet an arc on a spire.
    /// </summary>
    public bool TryFindSwing(Vector3 from, Vector3 wish, float maxRange,
        out Vector3 point, out Structure? holding)
    {
        point = default;
        holding = null;

        var fromXZ = new Vector2(from.X, from.Z);
        var wishXZ = new Vector2(wish.X, wish.Z);
        float wl = wishXZ.Length();
        // A wish that is straight up or straight down has no bearing to prefer, so every
        // direction scores alike and the range and the height decide it.
        if (wl > 1e-4f) wishXZ /= wl;
        else wishXZ = Vector2.Zero;

        float best = float.MinValue;
        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];

        foreach (var s in Structures)
        {
            int n = s.Blockers(blockers);   // zero for anything already coming down
            for (int b = 0; b < n; b++)
            {
                var (at, r) = blockers[b];

                // Held as the nearest image, so a tower just over the world's seam is
                // considered where the flier can actually see it.
                Vector2 near = Torus.NearestImage(at, fromXZ);
                Vector2 delta = near - fromXZ;
                float d = delta.Length();
                if (d < MinSwingRange + r || d > maxRange) continue;

                // How far up the side to bite. As high as the cable can reach on a straight
                // line from here, capped by the building's own top and by how much rise is
                // useful — an anchor directly overhead is a rope to hang from, not to swing
                // on.
                float headroom = MathF.Sqrt(MathF.Max(0f, maxRange * maxRange - d * d));
                float y = MathF.Min(s.BlockHeight * 0.92f,
                                    from.Y + MathF.Min(headroom, PreferredSwingRise));
                if (y < from.Y + 1.5f) continue;

                Vector2 planarDir = delta / d;
                float align = wishXZ == Vector2.Zero ? 0.5f : Vector2.Dot(planarDir, wishXZ);

                // Alignment is scored rather than required, and the difference matters. A
                // cable behind you is a brake and is worth a long way less than one ahead —
                // the weighting below guarantees any forward anchor beats every backward one
                // — but it is still a cable, and a soldier falling through a gap in the
                // skyline with nothing ahead of them takes the brake and lives. Rejecting
                // outright is what produces the one thing this enemy must never do: drop out
                // of the sky in a straight line with both hooks stowed because the only
                // building for eighty metres happened to be the wrong side of them.

                // A soft preference for a proper throw's distance, falling off either side.
                float band = 1f - MathF.Abs(d - PreferredSwingRange) / PreferredSwingRange;
                float trust = s.Scale < SoldierRig.WeakScale ? -1.3f : 0.4f;

                // Direction outweighs range by enough that no amount of being ideally placed
                // lets a wall off to the side beat one genuinely on the way. Trust is the one
                // thing that *can* outweigh direction, and deliberately so: given a spire
                // dead ahead and solid stone a few degrees off it, something that has done
                // this for a living takes the stone, because the spire is a tear halfway
                // through the arc and the few degrees are nothing.
                float score = align * 3f + band + trust;
                if (score <= best) continue;

                best = score;
                // Out onto the near face, so the cable ends on the building rather than in
                // the middle of it and the swing radius is the one that can be seen.
                Vector2 surface = near - planarDir * r;
                point = new Vector3(surface.X, y, surface.Y);
                holding = s;
            }
        }

        return best > float.MinValue;
    }

    /// <summary>Nothing closer than this is worth a cable — a hook into the wall you are
    /// already scraping along does nothing but stop you.</summary>
    private const float MinSwingRange = 12f;

    /// <summary>The throw a soldier would pick given a free choice: long enough to be a
    /// real arc, short enough to arrive on.</summary>
    private const float PreferredSwingRange = 46f;

    /// <summary>And how far above themselves they like to bite. Enough to swing under.</summary>
    private const float PreferredSwingRise = 24f;

    // --- Raising them --------------------------------------------------------------

    /// <summary>
    /// Puts a squad on the field: four of them, hung off the side of a tower out in the fog
    /// at a random bearing, watching. They are not spawned mid-air and they are not spawned
    /// walking — the first thing the player should ever see of a squad is four figures on a
    /// spire that were not there last time they looked that way.
    ///
    /// With no tower in reach (a razed city, or an unlucky bearing) they arrive on the grid
    /// instead and walk until they find something to hook, which is a worse opening for them
    /// and an easier one for the player. That is the honest failure mode and it is left in.
    /// </summary>
    public void SpawnSoldierSquad(Vector2? near = null)
    {
        Vector2 where = near ?? RandomPointAroundPlayer(SpawnMinRange, SpawnMaxRange);
        Structure? tower = NearestTowerTo(where);

        var members = new List<EnemySoldier>(SoldierSquad.Size);
        int leader = Random.Shared.Next(SoldierSquad.Size);

        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        float footprint = 4f;
        if (tower != null && tower.Blockers(blockers) > 0) footprint = blockers[0].Radius;

        for (int i = 0; i < SoldierSquad.Size; i++)
        {
            bool boss = i == leader;
            if (tower == null)
            {
                // No city here. They walk in.
                Vector2 spread = new Vector2(MathF.Sin(i * 1.9f), MathF.Cos(i * 1.9f)) * 3.5f;
                members.Add(new EnemySoldier(where + spread, 0f, boss, i));
                continue;
            }

            // Spaced round the tower and staggered up it, so a squad reads as four people
            // who chose their own spots rather than as four copies at one height.
            float angle = i * MathF.Tau / SoldierSquad.Size + Random.Shared.NextSingle() * 0.4f;
            var outward = new Vector2(MathF.Sin(angle), MathF.Cos(angle));
            float height = tower.BlockHeight * (0.45f + 0.12f * i);

            var perch = Torus.Wrap(tower.Position + outward * (footprint + 0.7f));
            var soldier = new EnemySoldier(perch, height, boss, i);
            // Clung on by a short cable into the wall behind them, facing out over the city.
            soldier.SeedPerch(Torus.Wrap(tower.Position + outward * footprint), height + 2.5f,
                tower, MathF.Atan2(outward.X, outward.Y));
            members.Add(soldier);
        }

        var squad = new SoldierSquad(members);
        Squads.Add(squad);
        Soldiers.AddRange(members);
    }

    /// <summary>
    /// Capture harness only: holds one of them in front of the craft, folded flat, crossing
    /// at speed with the blades out. Written every frame, so the sim's own step is
    /// overwritten and the figure stays put — the one enemy in the game that cannot be
    /// photographed by standing still and pointing at it, since standing still is the one
    /// thing it never does.
    /// </summary>
    public void PoseSoldierForCapture()
    {
        if (Soldiers.Count == 0) return;
        EnemySoldier mark = Soldiers[0];

        Vector2 fwd = Player.Forward;
        var side = new Vector2(-fwd.Y, fwd.X);

        mark.Position = Torus.Wrap(Player.Position + fwd * 7f);
        mark.Height = Player.EyeHeight + 0.4f;
        mark.Velocity = new Vector3(side.X * 24f, 0f, side.Y * 24f);
        mark.Heading = MathF.Atan2(-fwd.X, -fwd.Y);   // looking back at the craft
        mark.CommitRunForTest();

        Player.Pitch = 0.08f;
    }

    /// <summary>
    /// The standing tower nearest a spot, within a squad's own patience of it, or null if
    /// that part of the map is open grid.
    ///
    /// Bounded from the <em>player's</em> position as well as from the rolled spot, and that
    /// second bound is the load-bearing one: a search that only measured from the bearing
    /// could walk a squad half again as far out as it was meant to arrive, and put them down
    /// on a spire past their own eyesight — where they would sit watching an empty horizon
    /// for the rest of the run. A squad has to arrive somewhere it can see the fight from.
    /// </summary>
    private Structure? NearestTowerTo(Vector2 at)
    {
        Structure? best = null;
        float bestSq = SquadTowerSearch * SquadTowerSearch;
        foreach (var s in Structures)
        {
            if (s.Kind != StructureKind.Tower || s.Falling) continue;
            if (Torus.DistanceSquared(s.Position, Player.Position) > SpawnMaxRange * SpawnMaxRange)
                continue;
            float d = Torus.DistanceSquared(s.Position, at);
            if (d >= bestSq) continue;
            bestSq = d;
            best = s;
        }
        return best;
    }

    /// <summary>How far from the rolled bearing a squad will look for something to perch on
    /// before giving up and arriving on foot.</summary>
    private const float SquadTowerSearch = 70f;

    // --- Hurting them --------------------------------------------------------------

    /// <summary>
    /// Deals damage to one soldier and, on the blow that ends them, drops the body: the
    /// report, a burst of their own colours where they were hanging, and salvage on the grid
    /// underneath — a kill you earned out of the air is worth doubling back for, and where it
    /// falls is where they were when you hit them.
    /// </summary>
    private void DamageSoldier(EnemySoldier s, float amount)
    {
        // Hurting somebody who was flying cover for you ends that, however it happened � a
        // round, a rocket's splash, a beam, a building dropped on them. There is no version
        // of this where they carry on: the check lives here, at the one place all of it goes
        // through, rather than at each of the half-dozen ways to be careless.
        if (s.Allied) BreakEscort();

        bool wasAlive = s.Alive;
        s.TakeDamage(amount);
        if (!wasAlive || s.Alive) return;

        Emit(Cue.ExplosionAt, s.Position);
        Debris.Burst(new Vector3(s.Position.X, s.Height + EnemySoldier.AimHeight, s.Position.Y),
            s.IsLeader ? Palette.SoldierMark : Palette.SoldierCloth, s.IsLeader);

        var kind = Random.Shared.NextSingle() < DropBatteryShare
            ? PickupKind.Battery : PickupKind.Ammo;
        Pickups.Add(new Pickup(s.Position, kind));
    }

    /// <summary>
    /// A player round meeting a soldier. Height is the whole difference between this and the
    /// hunters' own test: a hunter stands on the grid where every flat bolt in the game
    /// already is, and these live thirty metres up it, so a round has to arrive at the right
    /// place on the plane <em>and</em> at the right place up the column. Which is exactly the
    /// bargain the class is meant to strike — they are not tough, they are hard to hit.
    /// </summary>
    private void StrikeSoldiers(Projectile p)
    {
        foreach (var s in Soldiers)
        {
            if (!s.Alive) continue;
            // A round from one of the player's own bodies passes straight through the rest of
            // them. Everything on that side is flying the same sky at the same speeds, and a
            // plague that shot itself in the back would be a plague that never got anywhere.
            if (p.FromAlly && (s.Carrier || s.Allied)) continue;
            // Where the shooter saw them. A squad crosses the sky at thirty metres a second,
            // which makes this the single place lag compensation is worth the most: at 100 ms
            // an uncompensated soldier is three metres from where the shooter aimed.
            if (!WithinHit(p.Position, Rewound(s.HitId, p.Owner, s.Position),
                    EnemySoldier.Radius + 0.3f)) continue;
            if (MathF.Abs(p.Height - (s.Height + EnemySoldier.AimHeight))
                > EnemySoldier.HitVertical) continue;

            // A round in one of their backs ends any pretence, whatever fired it — and if
            // they were flying cover for you, it ends that too.
            if (!s.Carrier && !s.Allied) BlowCover();
            else if (s.Allied) BreakEscort();

            if (p.IsPiercing)
            {
                // The AP slug bulls on through. It carries no memory of a person the way it
                // does of a hunter, and does not need one: a slug deals three and a soldier
                // has two, so the body it just crossed is dead and skipped on the next tick.
                DamageSoldier(s, SlugDamage);
                continue;
            }

            if (p.IsCrabBomb) StageCrabBlast(p.Position);
            else if (p.IsRocket) DetonateRocket(p);
            else if (p.IsGrenade) DetonateMortar(p);
            else
            {
                // A VIRUS round does not merely hurt a person — it puts something in them.
                // Seeded before the damage, so a round that finishes a soldier outright
                // still reads as the corruption arriving; and seeded on every hit, so the
                // second round into the same body refreshes the window rather than wasting
                // it. Which is the decision the whole weapon is built around: one shot buys
                // you a body you can wear or set loose, and a second one only buys a corpse.
                if (p.Seeds) SeedSoldier(s);
                DamageSoldier(s, PlayerShotDamage);
            }

            p.Active = false;
            return;
        }
    }

    // --- The plague ----------------------------------------------------------------
    //
    // What a VIRUS does to a squad, and the reason the class needed a twist rather than
    // simply a fifth body on the menu. Every other host in this game is a thing you *wear*:
    // you fly into it, it becomes armour, it rots, you leave. A person is different, because
    // a person has friends — so the corruption does not stop at the body it lands in.
    //
    // One round seeds a soldier. For a few seconds they are yours to take: slowed, coming
    // apart, and close enough to dead for the mote to climb inside. Let that window close
    // and the seed roots on its own — they turn, and go to work on the squad they arrived
    // with. So every shot the mote fires at a person is a question with a timer on it, and
    // both answers are good ones: wear this body, or spend it on the other three.

    /// <summary>
    /// Climbs into a soldier the mote has caught.
    ///
    /// Contact is enough, exactly as it is with a hunter — the corruption does not need an
    /// invitation, and gating this on having seeded them first turned the best body in the
    /// game into one nobody could ever get their hands on. What the seed is for is the
    /// <em>other</em> half of the mechanic: a tagged body is slowed and easy to run down,
    /// and a tag left uncollected turns them. Taking one is simply flying into it.
    ///
    /// <paramml name="reach"/> is how far the mote's grasp extends this attempt: a body's
    /// width on passive contact, and a great deal further when the player has actually asked
    /// for it (see <see cref="LungeAtSoldier"/>). Fully three-dimensional either way, unlike
    /// the hunters' grounded test — both parties are flying, and asking someone to touch a
    /// specific point on a moving body at fifty metres a second of closing speed is asking
    /// for something nobody can do twice.
    /// </summary>
    private bool TryWearSoldier(VirusRig mote, float reach, PlayerTank who)
    {
        float reachSq = reach * reach;

        EnemySoldier? prey = null;
        float best = reachSq;

        foreach (var s in Soldiers)
        {
            if (!s.Alive) continue;
            float dy = (s.Height + EnemySoldier.AimHeight) - (who.Height + 0.5f);
            if (MathF.Abs(dy) > reach) continue;
            float d = Torus.DistanceSquared(s.Position, who.Position) + dy * dy;
            if (d < best) { best = d; prey = s; }
        }

        if (prey is null) return false;

        // Whichever squad has just lost a member without noticing. They inherit the player:
        // from their side one of the four is still right there in the same kit, so they fly
        // cover for it, work the ring around it and put their rounds into whatever it is
        // pointed at. The best thing the class can buy, and it is bought by taking a body out
        // of the middle of the people who came with it. The escort is a disguise-side notion
        // tied to the local player's cover; only the local seat claims one.
        _escort = SquadOf(prey);
        _escortFor = who;

        // Worn, not wrecked: the body comes off the roster the way every other host does,
        // without a death, a blast or a payout. It is not a kill — it is a change of driver.
        who.Position = prey.Position;
        who.Height = prey.Height;
        prey.TakeDamage(float.MaxValue);
        Soldiers.Remove(prey);
        _escort?.Members.Remove(prey);

        mote.Possess(who, VirusHost.Soldier);
        // A body being climbed into, heard by anyone near enough to watch it happen.
        Emit(Cue.MawSwallow, who.Position, owner: Seat(who));
        Emit(Cue.AnchorBite, who.Position, owner: Seat(who));
        return true;
    }

    /// <summary>How much slack the mote gets on passive contact with a person. Wide, on
    /// purpose: two bodies flying at each other is a far harder touch than a mote settling
    /// onto a hunter parked on the grid.</summary>
    private const float SoldierInfectMargin = 3.5f;

    /// <summary>The passive reach: fly into one and it is yours.</summary>
    private float SoldierTouchReach
        => PlayerTank.Radius + EnemySoldier.Radius + SoldierInfectMargin;

    /// <summary>
    /// The reach of a deliberate grab. Most of a swing's length, because the player has
    /// asked for it and because the thing they are asking for is moving: a window this wide
    /// is what turns "impossible" into "get near one and commit".
    /// </summary>
    private const float SoldierLungeReach = 18f;

    /// <summary>
    /// The grab. Pressed near a person, the mote throws itself at the nearest one it can
    /// reach and takes them — and if there is nobody in range it still lunges, spending the
    /// gesture rather than silently doing nothing, so the control always answers.
    ///
    /// This exists because contact alone, against a target that crosses the sky at
    /// thirty-four metres a second, is a coin toss dressed as a skill. The lunge is the
    /// player saying <em>that one</em>, and it is the difference between a mechanic and a
    /// tease.
    /// </summary>
    private void LungeAtSoldier(VirusRig mote, PlayerTank who)
    {
        if (mote.Hosted || who.Captured) return;

        if (TryWearSoldier(mote, SoldierLungeReach, who)) return;

        // Nothing in reach: throw the mote down its own look anyway. A reach that came back
        // empty should still feel like a lunge, and the shove is often enough to close on
        // whatever was just outside it.
        mote.Velocity += who.Forward3 * LungeKick;
        Emit(Cue.CableZip, who.Position, owner: Seat(who));
    }

    /// <summary>How hard an empty grab throws the mote forward.</summary>
    private const float LungeKick = 26f;

    /// <summary>Presses the grab without a keyboard — the self-test's and the harness's way
    /// in. Honours the same reach and the same refusals a key press does.</summary>
    public void LungeAtSoldierForTest()
    {
        if (Player.Virus is { } mote) LungeAtSoldier(mote, Player);
    }

    /// <summary>Bills damage to one soldier through the world's own path — the self-test's way
    /// to land a shot on a specific body without arranging the geometry for a real round.</summary>
    public void HurtSoldierForTest(EnemySoldier s, float amount) => DamageSoldier(s, amount);

    /// <summary>
    /// True while the squads have no idea the player is anything but one of their own: a
    /// VIRUS wearing a stolen body, and not having done anything with it yet.
    ///
    /// This is the quietest thing the class does and probably the best. You take a soldier
    /// out of a squad and the other three go back to their walls, because from outside there
    /// is nothing to see — same body, same kit, same silhouette in the same sky. You can
    /// swing through the middle of them. And the moment you use any of it, they know: the
    /// cover is spent the instant the corruption leaves your hands, and it takes a while to
    /// get back, because a squad that has just watched one of its own open fire on it does
    /// not go back to assuming.
    /// </summary>
    public bool PlayerPassesForOneOfThem => PassesForOneOfThem(Player);

    /// <summary>Whether a squad reads this particular craft as one of their own. Per craft,
    /// because the disguise is a body one player is wearing — asked of the local seat alone
    /// it made every OTHER player invisible to a squad whenever the host happened to be
    /// wearing a stolen soldier.</summary>
    public bool PassesForOneOfThem(PlayerTank who)
        => who.Virus is { HostKind: VirusHost.Soldier } && _coverBlown <= 0f;

    private float _coverBlown;

    /// <summary>
    /// The squad currently flying cover for the player, or null. Set by taking one of their
    /// own (see <see cref="TryWearSoldier"/>), dropped the moment the body is off — or the
    /// moment the player puts a round into one of them, which nobody forgives.
    /// </summary>
    public SoldierSquad? Escort => _escort;

    private SoldierSquad? _escort;

    /// <summary>The craft the escort is flying for. The squad was bought by one player
    /// climbing into one of their bodies, and it ends when that body does — which is a
    /// question about that seat, not about whoever is at this keyboard.</summary>
    private PlayerTank? _escortFor;

    /// <summary>Which squad a soldier belongs to, or null for one flying on their own.</summary>
    private SoldierSquad? SquadOf(EnemySoldier who)
    {
        foreach (var squad in Squads)
            if (squad.Members.Contains(who)) return squad;
        return null;
    }

    /// <summary>
    /// Ends an escort because the player shot one of them. Worth its own method for the
    /// comment: an escort is the only thing in this game the player can lose by being
    /// careless rather than by being beaten, and a squad that has just watched the comrade
    /// they were covering open fire on them is not going to be talked round.
    /// </summary>
    private void BreakEscort()
    {
        if (_escort == null) return;
        PlayerTank? betrayed = _escortFor;
        _escort = null;
        _escortFor = null;
        BlowCover();
        // Heard by the player who just lost their cover, wherever they are — and by anyone
        // close enough to hear a squad turn on one of its own.
        Emit(Cue.CrabScream, betrayed?.Position ?? Players[LocalIndex].Position,
            owner: betrayed is null ? Projectile.NoOwner : Seat(betrayed));
    }

    /// <summary>How long the squads stay wise to a body that has shown its hand. Long
    /// enough that a player who fires from inside the ring has bought a real fight; short
    /// enough that going quiet and drifting off is a plan.</summary>
    private const float CoverBlownTime = 9f;

    /// <summary>Blows the disguise. Called by everything that could only be a virus doing
    /// it — a corruption round, a spent host, a blade in one of their backs.</summary>
    private void BlowCover()
    {
        if (!PlayerPassesForOneOfThem) { _coverBlown = MathF.Max(_coverBlown, CoverBlownTime); return; }
        _coverBlown = CoverBlownTime;
        // One cue, on the tick it happens: the call going up, the same one a squad makes
        // when it first sees anybody. From their side that is exactly what this is.
        Emit(Cue.HuntCall, Player.Position);
    }

    /// <summary>Seeds one soldier, and voices it. A no-op on one already turned.</summary>
    private void SeedSoldier(EnemySoldier s)
    {
        if (s.Carrier) return;
        bool fresh = s.Tagged <= 0f;
        s.Tag();
        if (!fresh) return;

        Emit(Cue.CoreHit, s.Position, 0.7f);
        Debris.Burst(new Vector3(s.Position.X, s.Height + EnemySoldier.AimHeight, s.Position.Y),
            Palette.NeonMagenta, elite: false);
    }

    /// <summary>
    /// The overload, seen from the squad's side. A host spent as a detonation does not merely
    /// kill the people standing in it — it <em>sprays</em> them, and every soldier caught
    /// turns on the spot. This is the class's heavy used as what it actually is: not a bomb,
    /// but the moment the infection stops being one body's problem.
    ///
    /// Turned rather than damaged on purpose. A blast that killed three soldiers is a good
    /// blast; a blast that hands you three of them, mid-air, already diving at the fourth, is
    /// the thing this chassis exists for.
    /// </summary>
    private void SpreadPlague(Vector2 at, float height, float radius)
    {
        foreach (var s in Soldiers)
        {
            if (!s.Alive || s.Carrier) continue;
            if (!WithinHit(at, s.Position, radius + EnemySoldier.Radius)) continue;
            if (MathF.Abs(s.Height + EnemySoldier.AimHeight - height)
                > radius + EnemySoldier.BodyHeight) continue;
            s.Turn();
            Emit(Cue.UnstableLance, s.Position);
            Debris.Burst(new Vector3(s.Position.X, s.Height + EnemySoldier.AimHeight, s.Position.Y),
                Palette.NeonMagenta, elite: true);
        }
    }

    /// <summary>How far an overload's spray reaches for people. Wider than the blast's own
    /// damage field: the corruption carries further than the explosion does, which is what
    /// makes spending a body in the middle of a squad worth doing.</summary>
    private const float PlagueRadius = 16f;

    /// <summary>
    /// Bills every soldier inside a blast. Unlike the hunters' sweep this one honours
    /// height: a mortar bursting on the grid does not reach somebody at the top of an arc,
    /// and a rocket that goes off against a wall halfway up one very much does.
    /// </summary>
    private void DamageSoldiersInBlast(Vector2 at, float height, float radius, float amount)
    {
        if (Soldiers.Count == 0) return;
        foreach (var s in Soldiers)
        {
            if (!s.Alive) continue;
            if (!WithinHit(at, s.Position, radius + EnemySoldier.Radius)) continue;
            if (MathF.Abs(s.Height + EnemySoldier.AimHeight - height)
                > radius + EnemySoldier.BodyHeight) continue;
            DamageSoldier(s, amount);
        }
    }

    // --- The FISH ---------------------------------------------------------------

    /// <summary>
    /// Where a fish opens the run. Not at the origin on the deck — a chassis whose entire
    /// design is about never touching the grid must not begin by touching it — but up
    /// level with the middle of the skyline, already moving, looking out across the city
    /// it is about to thread. The first thing the player sees is the thing the class is
    /// for, and they see it before they have pressed anything.
    /// </summary>
    private void SwimTheFishOffTheDeck(PlayerTank who)
    {
        who.Height = OpeningDepth;
        who.Pitch = -0.12f;   // nosed a touch down, so the reef is in frame and the sky isn't
        if (who.Fish is { } body)
            body.Velocity = new Vector3(0f, 0f, FishRig.BeatImpulse);
    }

    /// <summary>
    /// How high a fish opens: squarely in the middle of the band it actually plays in —
    /// well clear of the grid, well under the warned water, and level with the middle of
    /// the towers rather than above them. The opening frame should show the player the
    /// city they are about to thread, not a view down onto it.
    /// </summary>
    private const float OpeningDepth = 20f;

    /// <summary>
    /// The FISH's buttons: the tail, the strike and the spit. Short, because on this
    /// chassis almost everything that happens is physics rather than input — one beat is
    /// one press and the water does the rest.
    /// </summary>
    private void UpdateFishTriggers(FishRig body, in InputFrame input, PlayerTank who)
    {
        // A cinematic has hold of the player. Nothing is read: a monster swinging a body
        // around by its tail is not a situation the water has an opinion about.
        if (who.Captured) return;

        if (input.BeatPressed) BeatFish(who, body);

        // The strike before the spit: on a frame both are down, the committed attack
        // wins. They share the fire cooldown, and a player holding the spit while
        // clicking for a strike should get the strike rather than have it eaten.
        if (input.StrikePressed) StrikeFish(who, body);
        else if (input.SpitDown) FireFishSpit(who);
    }

    /// <summary>One beat of the tail, with its cue and — off the deck — the puff of grid
    /// dust a flop kicks up under it. The beat is spent by <paramref name="who"/>'s own
    /// body: the host runs this for every seat, and a remote swimmer used to beat the
    /// host's tail and drain the host's breath.</summary>
    private void BeatFish(PlayerTank who, FishRig body)
    {
        bool beached = body.Beached;
        if (!body.Beat(who)) return;

        Emit(beached ? Cue.TailFlop : Cue.TailBeat, who.Position, body.Starvation,
            owner: Seat(who));
        if (beached)
            Debris.FootPuff(new Vector3(who.Position.X, 0f, who.Position.Y));
    }

    /// <summary>Winds a strike, if the reserve and the body will allow one.</summary>
    private void StrikeFish(PlayerTank who, FishRig body)
    {
        if (body.BeginStrike(who)) Emit(Cue.FishCoil, who.Position, owner: Seat(who));
    }

    /// <summary>
    /// A spit down the eye's line. Cheap, fast, and — the point of the whole weapon —
    /// entirely usable mid-carve: nothing here touches the body, so firing never breaks
    /// an arc.
    /// </summary>
    private void FireFishSpit(PlayerTank? by = null)
    {
        // Null is the craft this machine is driving — what every caller before seats meant.
        PlayerTank who = by ?? Player;
        if (HeldInClaw(who)) return;
        if (!who.TryFireSpit(out Vector3 origin, out Vector3 dir)) return;

        // Inside the Maw-Core's throat every trigger is the escape trigger, exactly as it
        // is on every other chassis: there is nothing to aim at in a mouth.
        if (SwallowedIn(who) is { Held: true } digestion)
        {
            digestion.RegisterShot();
            Emit(Cue.FishSpit, who.Position, owner: Seat(who));
            return;
        }

        SpawnDirected(origin, dir, Projectile.RifleSpeed, rocket: false, owner: Seat(who));
        Emit(Cue.FishSpit, who.Position, owner: Seat(who));
        who.Fish?.Kick(0.010f);
    }

    /// <summary>
    /// Drains the body's events after the physics have run: the cues, the beds, the
    /// strike's one hit, and what a crash or a beaching costs. Split out from the rig for
    /// the same reason the soldier's is — the physics stay a pure function of their own
    /// state, and the world keeps sole ownership of hurting anything.
    /// </summary>
    private void UpdateFishEvents(PlayerTank who, FishRig body)
    {
        int seat = Seat(who);
        bool local = ReferenceEquals(who, Players[LocalIndex]);

        if (local)
        {
            // The water going past. The single loudest carrier of speed on a chassis with no
            // engine note to ride, fed every tick and left to fade on its own. A bed, so only
            // ever the body this machine is inside.
            Audio.SetWind(body.PlanarSpeed > FishWashSpeed,
                Math.Clamp((body.PlanarSpeed - FishWashSpeed) / 20f, 0f, 1f));

            // And the tick that says the breath is nearly out — only while off the deck,
            // since a beached fish has larger problems and is already being told about them.
            if (who.Hyper < BreathWarnLevel && !body.Beached) Audio.PlayGasLow();
        }

        UpdateBloom(who, body);

        if (body.JustStruck) Emit(Cue.FishStrike, who.Position, owner: seat);
        if (body.StrikeActive) SweepStrike(who, body);

        if (body.JustCrashed)
        {
            Emit(Cue.CrashLanding, who.Position, owner: seat);
            DamagePlayer(CrashDamage, who);
            Debris.Burst(new Vector3(who.Position.X, who.Height + 1f, who.Position.Y),
                Palette.StructureShell, elite: false);
        }

        if (body.JustBeached)
        {
            Emit(Cue.FishBeach, who.Position,
                Math.Clamp(body.BeachImpact / 20f, 0f, 1f), owner: seat);
            Debris.FootPuff(new Vector3(who.Position.X, 0f, who.Position.Y));

            // Meeting the seabed hard costs shield, scaled by how hard. A fish has no
            // armour and no legs to take it with — the grid is simply not somewhere this
            // body is built to arrive at.
            if (body.BeachImpact > FishRig.BeachImpactSpeed)
            {
                float over = (body.BeachImpact - FishRig.BeachImpactSpeed)
                           / (FishRig.BeachImpactSpeed * 0.8f);
                DamagePlayer(BeachDamage + BeachDamage * Math.Clamp(over, 0f, 2f), who);
            }
        }
    }

    /// <summary>
    /// One tick of a live strike, looking for the single thing it spears. Nearest-first
    /// is deliberately <em>not</em> what this does: the lunge is short and fast enough
    /// that at most one candidate is ever inside its reach on a given tick, and walking
    /// the roster in a fixed order costs nothing and never allocates.
    ///
    /// The snout reaches the two crystals as well as the hunters, which is the whole
    /// reward for the class living up where they do. A Maw-Core in particular has spent
    /// the entire game being hittable only from the top of a jump; to a fish it is simply
    /// something else swimming at the same depth.
    /// </summary>
    private void SweepStrike(PlayerTank who, FishRig body)
    {
        var at = new Vector3(who.Position.X, who.Height, who.Position.Y);
        float reach = FishRig.StrikeReach;
        int seat = Seat(who);

        // The squads first, because they are the one target on the field that lives in the
        // same volume the fish does. Everything else this can spear is either on the floor
        // or is a fixed point in the air; a soldier is neither, and a strike that catches
        // one mid-arc is the best thing this chassis can do.
        foreach (var s in Soldiers)
        {
            if (!s.Alive) continue;
            if (!WithinHit(who.Position, s.Position, reach + EnemySoldier.Radius)) continue;
            if (MathF.Abs(who.Height - (s.Height + EnemySoldier.AimHeight))
                > reach + EnemySoldier.BodyHeight * 0.5f) continue;
            if (!body.ConsumeStrike()) return;
            DamageSoldier(s, FishRig.StrikeDamage);
            LandStrike(who, seat, at, Palette.SoldierCloth);
            return;
        }

        // Hunters sit on the grid, so a strike only reaches one if the dive has genuinely
        // come down to them — a fish cruising at thirty metres cannot spear something on
        // the floor by pointing at it, and the whole cost of the attack is committing to
        // the descent that makes it possible.
        if (who.Height <= reach + EnemyTank.Radius)
        {
            foreach (var e in Enemies)
            {
                if (!e.Alive) continue;
                if (!WithinHit(who.Position, e.Position, reach + EnemyTank.Radius)) continue;
                if (!body.ConsumeStrike()) return;
                DamageEnemy(e, FishRig.StrikeDamage, Seat(who));
                LandStrike(who, seat, at, Palette.EnemyFill);
                return;
            }
        }

        if (Boss is { } boss && boss.HitsCore(who.Position, who.Height))
        {
            if (!body.ConsumeStrike()) return;
            if (boss.DamageCore(FishRig.StrikeDamage)) DestroyBoss(boss);
            else Emit(Cue.CoreHit, boss.Position, 1f - boss.CoreFraction);
            LandStrike(who, seat, at, Palette.NeonRed);
            return;
        }

        if (Maw is { } maw && maw.HitsCrystal(who.Position, who.Height))
        {
            if (!body.ConsumeStrike()) return;
            if (maw.DamageCrystal(FishRig.StrikeDamage)) DestroyMaw(maw);
            else Emit(Cue.MawHurt, maw.Position, 1f - maw.CrystalFraction);
            LandStrike(who, seat, at, Palette.NeonRed);
        }
    }

    /// <summary>A strike connecting: the impact cue and a spray off whatever it went
    /// into. Shared by all three targets so a spear always reads the same.</summary>
    private void LandStrike(PlayerTank who, int seat, Vector3 at, Color colour)
    {
        Emit(Cue.FishImpact, who.Position, owner: seat);
        Debris.Burst(at, colour, elite: true);
        who.Fish?.Jolt(0.45f);
    }

    /// <summary>
    /// The ceiling biting. Two separate things happen here and the order they happen in is
    /// the whole point: a player who climbs too high is <em>told</em> first — an alarm, a
    /// stain across the top of the frame, and a tail that visibly stops gripping — across
    /// a ten-metre band that costs them nothing at all. Only past that does it start
    /// taking shield.
    ///
    /// The damage is ticked rather than applied per frame for the same two reasons the
    /// Crab-Core's beam is: it stops depending on the frame rate, and each bite fires its
    /// own cue, which at sixty a second would be a solid tone rather than the sound of
    /// being repeatedly hurt.
    /// </summary>
    private void UpdateBloom(PlayerTank who, FishRig body)
    {
        int seat = Math.Clamp(Seat(who), 0, MatchSettings.MaxSeats - 1);
        // The alarms belong to the listener; the bite belongs to the body. Splitting them is
        // what lets the host run every seat's bloom without klaxoning at whoever is sitting
        // at the host's keyboard for a fish on the other side of the map.
        bool local = ReferenceEquals(who, Players[LocalIndex]);

        // The warning band. Free, loud, and rate-limited inside Audio so it is safe to ask
        // for every tick the body is up here.
        if (local && body.BloomNotice > 0f && !body.InBloom) Audio.PlayBloomWarning();

        if (!body.InBloom)
        {
            _bloomTick[seat] = 0f;
            return;
        }

        // In it. The alarm goes from a tick to a proper klaxon, and the clock starts.
        if (local) Audio.PlayBloomAlarm();

        _bloomTick[seat] += (float)Config.FixedDt;
        if (_bloomTick[seat] < BloomTickInterval) return;
        _bloomTick[seat] -= BloomTickInterval;

        // Scaled by how far past the floor of it the body has pushed. At the boundary this
        // is a slow leak that a player can climb out of and repair; deep in, it is roughly
        // a shot from a hunter every half second, which no build survives for long.
        float bite = BloomBiteDamage * (1f + 2.2f * body.Toxicity);
        DamagePlayer(bite, who);
        body.Jolt(0.12f + 0.2f * body.Toxicity);
    }

    /// <summary>One bloom clock per seat. It has to be per seat now the host drains every
    /// fish's bloom, not just its own: a single shared clock would have two swimmers stealing
    /// each other's half-seconds and neither being billed on time.</summary>
    private readonly float[] _bloomTick = new float[MatchSettings.MaxSeats];

    /// <summary>How often the bloom bites while the body is in it. Twice a second — often
    /// enough that the drain is unmistakable, rare enough that each bite is a discrete
    /// event the player can count.</summary>
    private const float BloomTickInterval = 0.5f;

    /// <summary>What one bite costs at the very floor of the bloom, before the depth
    /// scaling. Deliberately survivable at the boundary: the first second up there should
    /// be a mistake, not a death.</summary>
    private const float BloomBiteDamage = 4f;

    /// <summary>Planar speed past which the water is audible at all.</summary>
    private const float FishWashSpeed = 14f;

    /// <summary>Reserve below which the body starts ticking a warning — about two beats'
    /// worth left, which is the only amount worth being told about.</summary>
    private const float BreathWarnLevel = 20f;

    /// <summary>What the mildest damaging arrival on the seabed costs, before the
    /// overshoot scaling.</summary>
    private const float BeachDamage = 10f;

    /// <summary>
    /// Capture harness and self-test only: a roll/brake to drive the body with instead of
    /// the keyboard's. Screenshotting this chassis means holding a carve through most of a
    /// second while a turn develops, and there is nobody at the keys during a capture run.
    /// </summary>
    public Vector2? ScriptedFishMove;

    /// <summary>Beats the tail without a keyboard — the harness's and the self-test's way
    /// in. Honours the refractory period and the reserve exactly as a press does.</summary>
    public bool BeatFishForTest()
    {
        if (Player.Fish is not { } body) return false;
        bool before = body.Beached;
        if (!body.Beat(Player)) return false;
        if (before) Debris.FootPuff(new Vector3(Player.Position.X, 0f, Player.Position.Y));
        return true;
    }

    /// <summary>The same for a strike.</summary>
    public bool StrikeFishForTest()
        => Player.Fish is { } body && body.BeginStrike(Player);

    /// <summary>And for the spit.</summary>
    public void FireFishSpitForTest() => FireFishSpit();

    // --- The VIRUS --------------------------------------------------------------

    /// <summary>
    /// The VIRUS's two triggers. Left fires whatever the current body fires — the mote's
    /// and a worn hunter's corruption bolt, the worn maw's acid spit, or the worn crab's
    /// broken lance. Right overloads the host into a bomb, and takes precedence on a frame
    /// both are down: the committed spend wins, exactly as the grenade beats the cannon and
    /// the strike beats the spit.
    /// </summary>
    private void UpdateVirusTriggers(VirusRig mote, in InputFrame input, PlayerTank who)
    {
        if (who.Captured) return;

        if (mote.Hosted && input.VirusOverloadPressed)
        {
            OverloadVirus(mote, who);
            return;   // it ejected — nothing else fires from a body that no longer exists
        }

        // A worn person comes with their launchers, and the launchers come with their own
        // controls: the same two hook keys and the same gas jump the SOLDIER chassis flies
        // on. The trigger stays the mote's corruption round rather than the soldier's rifle
        // — what the virus does to people is the whole class, and it should not have to put
        // that down to use their legs.
        if (mote.WornRig is { } worn && !UpdateCableKit(worn, input, who)) return;

        if (mote.WornRig == null)
        {
            // Only ever this machine's own crosshair readout — see UpdateCableKit.
            if (ReferenceEquals(who, Players[LocalIndex])) AnchorInSight = null;
            // Exposed, the same key is the grab. E throws the mote at whatever body is
            // nearest and climbs into it — the deliberate version of the contact that
            // happens on its own, and the answer to a target too quick to simply bump into.
            if (mote.Exposed && input.RightHookPressed) LungeAtSoldier(mote, who);
        }

        if (!input.VirusFireDown) return;

        if (mote.HostKind == VirusHost.Crab) FireVirusLance(mote, who);
        else FireVirusRound(mote, who);
    }

    /// <summary>
    /// A virus round down the eye's line. Cheap, fast, and usable in the middle of a dart or
    /// a drive — nothing here touches the rig, so firing never breaks the flight. Routed
    /// through the same directed-round path the soldier's rifle and the fish's spit use, so
    /// it is blocked by the skyline, bites hunters and finds the two crystals identically.
    /// Fired out of a worn maw it leaves as the mouth's own acid bolt instead — slower, and
    /// unmistakably the monster's ordnance rather than a re-tinted rifle.
    /// </summary>
    private void FireVirusRound(VirusRig mote, PlayerTank? by = null)
    {
        // Null is the craft this machine is driving — what every caller before seats meant.
        PlayerTank who = by ?? Player;
        if (HeldInClaw(who)) return;
        if (!who.TryFireVirus(out Vector3 origin, out Vector3 dir)) return;

        // Inside the Maw-Core's throat every trigger is the escape trigger — there is
        // nothing to aim at in a mouth.
        if (SwallowedIn(who) is { Held: true } digestion)
        {
            digestion.RegisterShot();
            Emit(Cue.Laser, who.Position, owner: Seat(who));
            return;
        }

        // Firing is the tell. Whatever a stolen body looks like from outside, what comes out
        // of it does not look like a rifle, and the squad it belonged to is watching.
        if (PlayerPassesForOneOfThem) BlowCover();

        bool acid = mote.HostKind == VirusHost.Maw;
        SpawnDirected(origin, dir, acid ? AcidSpitSpeed : Projectile.RifleSpeed,
            rocket: false, acid: acid, owner: Seat(who), seeds: true);
        Emit(Cue.Laser, who.Position, owner: Seat(who));   // a dry corruption zap
        mote.Jolt(0.05f);
    }

    /// <summary>How fast a worn maw's acid bolt travels. Well under the rifle's ninety —
    /// the monster's own spit has always been slow enough to walk away from, and its
    /// stolen version keeps the family resemblance while staying usable as a weapon.</summary>
    private const float AcidSpitSpeed = 48f;

    /// <summary>
    /// The stolen lance. The Crab-Core's own beam, fired by whatever is wearing the crab —
    /// and it did not survive the theft intact. A corrupted core cannot hold a line, so one
    /// trigger pull leaves as a main shaft thrown roughly (only roughly) where it was aimed
    /// plus a spray of breaks in directions nobody chose, every one of them a genuine beam:
    /// it cuts buildings down, rakes hunters, and finds the two crystals, exactly as the
    /// intact weapon would. The bill is paid in the host itself — each discharge burns a
    /// slice of the decay meter, and the rig lets a shot on the last of it finish the body.
    /// </summary>
    private void FireVirusLance(VirusRig mote, PlayerTank who)
    {
        if (HeldInClaw(who) || SwallowedIn(who) is { Held: true }) return;
        if (!mote.TryLance()) return;

        bool voice = who == Player;
        Vector3 aim = who.Forward3;

        // The aimed shaft plus 2..4 breaks. The main one is jittered a touch off true —
        // even the shot you meant is not quite the shot you get — and the breaks are
        // thrown well off it, each on its own random axis.
        //
        // Each shaft carries two origins on purpose. The damage runs from just off the
        // eye, so nothing standing at point-blank sits in a dead zone under the beam. The
        // *picture* starts several units further out along the shaft's own line: its near
        // end (cap, sheath and muzzle flare) is the widest thing in a first-person frame,
        // several of them leave on one pull, and drawn from the eye they union into a wall
        // of red the player fires blind through. Spreading the visual origins down each
        // shaft's own direction also breaks the single shared ball into a visible fan.
        int breaks = 2 + Random.Shared.Next(3);
        for (int i = 0; i <= breaks; i++)
        {
            Vector3 dir = JitterDirection(aim, i == 0
                ? 0.05f
                : 0.18f + 0.45f * Random.Shared.NextSingle());
            mote.AddShaft(who.Eye + dir * 8f, dir);
            BurnBeamAlong(who.Eye + dir * 0.8f, dir, VirusRig.LanceLength,
                VirusRig.LanceRadius, VirusRig.LanceDamage, Seat(who));
        }

        // Unconditional: Emit already decides who hears it, and gating it on "is this our own
        // seat" meant a remote player's stolen lance was never even filed for the wire — the
        // loudest thing this class does went off in silence on every other machine.
        Emit(Cue.UnstableLance, who.Position, owner: Seat(who));
        mote.Jolt(0.3f);

        // A shot fired on the last of the meter finished the host (see VirusRig.TryLance):
        // the body it just burned out goes up around the player.
        if (!mote.Hosted) StageVirusBurst(overload: false, who);
    }

    /// <summary>A unit direction thrown up to <paramref name="spread"/> radians off
    /// <paramref name="dir"/>, on a random axis — how the broken lance decides where a
    /// shaft actually goes.</summary>
    private static Vector3 JitterDirection(Vector3 dir, float spread)
    {
        // A random perpendicular: cross with the axis least aligned to the direction, then
        // roll it around the direction itself.
        Vector3 seed = MathF.Abs(dir.Y) < 0.8f ? new Vector3(0f, 1f, 0f) : new Vector3(1f, 0f, 0f);
        Vector3 side = Vector3.Normalize(Vector3.Cross(dir, seed));
        float roll = Random.Shared.NextSingle() * MathF.Tau;
        Vector3 axis = side * MathF.Cos(roll)
                     + Vector3.Normalize(Vector3.Cross(dir, side)) * MathF.Sin(roll);

        float off = spread * (0.35f + 0.65f * Random.Shared.NextSingle());
        return Vector3.Normalize(dir * MathF.Cos(off) + axis * MathF.Sin(off));
    }

    /// <summary>
    /// Spends the worn host as a detonation: the rig ejects the player back to the mote, and
    /// the husk goes off as a corrupted energy burst — the very same radial star a thrown
    /// CRAB CORE throws, which is exactly the right picture for a core forced to overload and
    /// comes with its area damage already wired. One place, so the test hatch and the trigger
    /// stage identical blasts.
    /// </summary>
    private void OverloadVirus(VirusRig mote, PlayerTank who)
    {
        // Guarded here rather than only at the trigger, so the test hatch can never stage
        // a free detonation off a mote with no body to spend.
        if (!mote.Hosted) return;

        // Where it goes off — read before the rig ejects, since the eject kicks the mote
        // upward and the burst belongs where the body was standing.
        Vector2 at = who.Position;
        float height = who.Height + 1f;

        // Nothing survives an overload's cover. Blown before the eject, while the disguise
        // still technically holds, so the timer starts from the moment they saw it.
        BlowCover();

        mote.Overload();
        StageVirusBurst(overload: true, who);
        SpreadPlague(at, height, PlagueRadius);
    }

    /// <summary>
    /// Drains the mote's events after the physics have run: the infection contact test, the
    /// withering clock, the rush of flight, and the one ejection the world can only learn
    /// about from a flag — the decay clock running out. The other ejections (an overload, a
    /// host shot out from under the player, a lance fired on the last of the meter) are
    /// staged at their source, because they happen at points in the tick where a flag
    /// drained here would be a frame late or lost entirely.
    /// </summary>
    private void UpdateVirusEvents(VirusRig mote, PlayerTank who)
    {
        // While exposed, flying the mote into a body seizes it — checked every tick the
        // way salvage collection is, so contact is enough and there is no button to fumble.
        if (mote.Exposed) TryInfect(mote, who);

        UpdateWithering(mote, who);

        // The rush past the ears, fed off the rig's flight speed and left to fade on its
        // own — the same bed the soldier's wind and the fish's wash ride. Only the mote at
        // this machine roars: the wind bed belongs to the local listener, not to a remote
        // seat the host happens to be simulating.
        if (who == Player)
            Audio.SetWind(mote.PlanarSpeed > VirusRushSpeed,
                Math.Clamp((mote.PlanarSpeed - VirusRushSpeed) / 20f, 0f, 1f));

        if (mote.JustEjected) StageVirusBurst(overload: false, who);
    }

    /// <summary>
    /// The mote coming apart in the open. The grace itself is free and mostly silent — a
    /// low tick starts near its end, so the player is told before the first bite — and past
    /// it the withering bills shield on a slow clock until a body is reached.
    ///
    /// The damage goes through <see cref="PlayerTank.TakeDamage"/> directly rather than
    /// <see cref="DamagePlayer"/> on purpose: that path amplifies hits on an exposed mote,
    /// which is a rule about <em>weapons</em> finding an unarmoured target. The withering
    /// is not a weapon — it is the mote's own physiology, already tuned in this constant.
    /// </summary>
    private void UpdateWithering(VirusRig mote, PlayerTank who)
    {
        if (!mote.Exposed || who.Captured) return;

        bool voice = who == Player;
        int seat = Math.Clamp(Seat(who), 0, MatchSettings.MaxSeats - 1);

        // The courtesy tick as the grace runs out — rate-limited inside Audio, so it is
        // safe to ask for on every tick of the last stretch.
        if (!mote.Withering)
        {
            _witherTick[seat] = 0f;
            if (voice && mote.GraceRemaining < WitherWarnTime) Audio.PlayGasLow();
            return;
        }

        _witherTick[seat] += (float)Config.FixedDt;
        if (_witherTick[seat] < WitherTickInterval) return;
        _witherTick[seat] -= WitherTickInterval;

        who.TakeDamage(WitherBite);
        mote.Jolt(0.15f);
        // The bite reaches its owner wherever they are — this used to sound only when the
        // withering mote happened to be the one at this keyboard, so a remote player rotting
        // in the open was told nothing at all.
        if (!who.Alive) Emit(Cue.Explosion, who.Position, owner: Seat(who));
        else Emit(Cue.Warning, who.Position, owner: Seat(who), personal: true);
    }

    /// <summary>One withering clock per seat. Per seat because the host drains every mote on
    /// the field, not only its own: a shared clock would have two exposed viruses stealing
    /// each other's half-seconds and neither being bitten on time.</summary>
    private readonly float[] _witherTick = new float[MatchSettings.MaxSeats];

    /// <summary>How often the withering bites once the grace is spent. Twice a second, the
    /// bloom's own cadence — discrete events the player can count, not a smooth drain.</summary>
    private const float WitherTickInterval = 0.5f;

    /// <summary>What one bite costs. Slow enough to cross most of the arena on after the
    /// grace has already run out; fast enough that living unhosted is never a plan.</summary>
    private const float WitherBite = 3f;

    /// <summary>Seconds of grace left when the warning tick starts.</summary>
    private const float WitherWarnTime = 5f;

    /// <summary>
    /// Seizes whatever body the mote has flown into, if any. The world owns this rather
    /// than the rig because only the world knows what is out there — and every body is
    /// <em>consumed</em>, not destroyed: no death blast, no debris, no fragment. A hunter
    /// drops silently off the roster; a monster is simply no longer on the field, because
    /// the player is now wearing it.
    ///
    /// The two big machines are entered the way everything else in this game reaches them:
    /// through the bright core. The same geometry that scores a bullet on the crab's gem or
    /// the maw's crystal admits a mote flown into it — the weak point is, for this one
    /// class, a door.
    ///
    /// A person has no door, and that is the whole of why the squads needed their own rule.
    /// A hunter can be taken by flying into it because a hunter is a slow thing on a floor;
    /// a soldier crosses the sky at thirty metres a second and is never in the same place
    /// twice, so contact alone would be a lottery nobody could play. They have to be
    /// <em>seeded</em> first — one round of corruption, which slows them and opens them —
    /// and then taken inside that window. Which turns the mote's gun from a weapon into an
    /// instrument, and is by some distance the most interesting thing this class does.
    /// </summary>
    private void TryInfect(VirusRig mote, PlayerTank who)
    {
        if (who.Captured) return;

        // Every possession is voiced now, wherever the mote is: the gulp goes out as a cue
        // with its owner's seat, so the player wearing the body hears it and so does anyone
        // standing close enough to watch. (This used to be gated on "is it our own mote",
        // which meant a remote seat took its body in silence on every machine.)

        // A soldier, taken in the air. Checked before the machines: a squad in the middle
        // of a fight is a far more urgent offer than a monster standing about.
        if (TryWearSoldier(mote, SoldierTouchReach, who)) return;

        // The Crab-Core, entered through the gem. Alive only — a dying rig mid-glitch is
        // not a body anymore. The probe rides half a unit up the mote, roughly its middle.
        if (Boss is { Alive: true } boss
            && boss.HitsCore(who.Position, who.Height + 0.5f))
        {
            who.Position = boss.Position;
            Boss = null;                        // worn, not wrecked
            mote.Possess(who, VirusHost.Crab);
            Emit(Cue.MawSwallow, who.Position, owner: Seat(who));
            Emit(Cue.Clamp, who.Position, owner: Seat(who));
            return;
        }

        // The Maw-Core, entered through the crystal. The player keeps their height: they
        // are the hovering mouth now, and it does not fall out of the sky on possession.
        if (Maw is { Alive: true } maw
            && maw.HitsCrystal(who.Position, who.Height + 0.5f))
        {
            who.Position = maw.Position;
            Maw = null;
            mote.Possess(who, VirusHost.Maw);
            Emit(Cue.MawSwallow, who.Position, owner: Seat(who));
            return;
        }

        float reach = PlayerTank.Radius + EnemyTank.Radius + VirusRig.InfectMargin;
        float reachSq = reach * reach;

        EnemyTank? prey = null;
        float best = reachSq;
        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            // The mote has to have come down onto the body, not merely be sailing overhead:
            // a hunter is a thing on the grid, and taking one means diving to its level.
            if (who.Height > EnemyTank.BodyHeight + InfectVertical) continue;
            float d = Torus.DistanceSquared(e.Position, who.Position);
            if (d < best) { best = d; prey = e; }
        }

        if (prey is null) return;

        bool elite = prey.IsElite;
        prey.TakeDamage(float.MaxValue);   // quietly consumed; RemoveAll clears it this tick
        who.Position = prey.Position;      // climb into where the body stood
        mote.Possess(who, elite ? VirusHost.Elite : VirusHost.Hunter);
        // A wet gulp — something climbing inside a body.
        Emit(Cue.MawSwallow, who.Position, owner: Seat(who));
    }

    /// <summary>How far off the grid the mote may be and still reach a hunter to infect —
    /// low enough that seizing a body genuinely means diving onto it.</summary>
    private const float InfectVertical = 2.5f;

    /// <summary>
    /// Dresses an ejection. An overload spends the whole host as the CRAB CORE's own radial
    /// blast; a body that simply gave out throws off far less — a spatter of the mote's
    /// colour and a dull report, and no area damage, because letting a host rot is never
    /// rewarded the way deliberately spending one is.
    /// </summary>
    private void StageVirusBurst(bool overload, PlayerTank who)
    {
        if (overload)
        {
            StageCrabBlast(who.Position);
            return;
        }

        Debris.Burst(new Vector3(who.Position.X, who.Height + 1f, who.Position.Y),
            Palette.NeonMagenta, elite: false);
        Emit(Cue.Detonation, who.Position, owner: Seat(who));
    }

    /// <summary>Planar flight speed past which the mote's rush is audible at all.</summary>
    private const float VirusRushSpeed = 16f;

    /// <summary>
    /// Capture harness and self-test only: a WASD to fly the mote or drive the host with
    /// instead of the keyboard's. Screenshotting this chassis means holding a dart for a
    /// second while speed builds, and there is nobody at the keys during a capture run.
    /// </summary>
    public Vector2? ScriptedVirusMove;

    /// <summary>Fires the virus round without a mouse — the harness's and the self-test's
    /// way in.</summary>
    public void FireVirusRoundForTest()
    {
        if (Player.Virus is { } mote) FireVirusRound(mote);
    }

    /// <summary>Overloads the worn host without a mouse, through the same path the trigger
    /// uses — so a test that drives an overload stages exactly the blast play does.</summary>
    public void OverloadVirusForTest()
    {
        if (Player.Virus is { } mote) OverloadVirus(mote, Player);
    }

    /// <summary>Fires the worn crab's broken lance without a mouse — the harness's and the
    /// self-test's way in. Honours the cooldown and the decay cost exactly as a click does.</summary>
    public void FireVirusLanceForTest()
    {
        if (Player.Virus is { } mote) FireVirusLance(mote, Player);
    }

    /// <summary>
    /// A rocket going off: the splash on everything nearby, and — alone among the
    /// player's ordnance short of a charged lance — the buildings inside the blast cut
    /// down. That last clause is the whole reason rockets are rationed. Anything the
    /// blast destroys stops being an anchor, and the game does not check first whether
    /// the player is currently hanging from it.
    /// </summary>
    private void DetonateRocket(Projectile p)
    {
        var at = new Vector3(p.Position.X, MathF.Max(0.4f, p.Height), p.Position.Y);

        float reach = Projectile.RocketSplash + EnemyTank.Radius;
        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            if (WithinHit(p.Position, e.Position, reach)) DamageEnemy(e, GrenadeDamage);
        }

        // A rocket is the one answer a player has to a squad that has settled into its ring:
        // it goes off where it is pointed rather than where the grid is, so it reaches them.
        DamageSoldiersInBlast(p.Position, at.Y, Projectile.RocketSplash, GrenadeDamage);

        // The blast reaches the two crystals too, if either happens to be in it.
        if (Boss is { } boss && boss.HitsCore(p.Position, p.Height))
        {
            if (boss.DamageCore(GrenadeDamage)) DestroyBoss(boss);
            else Emit(Cue.CoreHit, boss.Position, 1f - boss.CoreFraction);
        }
        if (Maw is { } maw && maw.HitsCrystal(p.Position, p.Height))
        {
            if (maw.DamageCrystal(GrenadeDamage)) DestroyMaw(maw);
            else Emit(Cue.MawHurt, maw.Position, 1f - maw.CrystalFraction);
        }

        // Every standing footprint inside the blast comes down.
        Span<(Vector2 At, float Radius)> blockers = stackalloc (Vector2, float)[Structure.MaxBlockers];
        for (int i = Structures.Count - 1; i >= 0; i--)
        {
            Structure s = Structures[i];
            int n = s.Blockers(blockers);
            for (int b = 0; b < n; b++)
            {
                var (bAt, r) = blockers[b];
                if (!WithinHit(p.Position, bAt, Projectile.RocketSplash + r)) continue;
                // Only what the blast can actually reach up the building's side: a rocket
                // going off at the foot of a forty-metre tower still fells it, but one
                // detonating in mid-air well above an arch's legs does not.
                if (p.Height > s.BlockHeight + Projectile.RocketSplash) continue;
                FellStructure(s);
                break;
            }
        }

        Debris.Burst(at, Palette.EliteFill, elite: true);
        Emit(Cue.RocketBlast, p.Position);

        // The pressure ripple: everything nearby is thrown about, every player included.
        // A rocket fired at a wall you are swinging toward should be felt through the
        // camera, not merely heard — and it should sting whoever is standing in it, which
        // asked about seat 0 meant a soldier could rocket their own feet in perfect safety
        // on every machine but the host's.
        foreach (var mark in Players)
        {
            if (mark.Away || !mark.Alive) continue;
            float range = Torus.Distance(p.Position, mark.Position);
            if (range >= RocketShakeRange) continue;
            // The kick is a camera effect and belongs to the eye behind it.
            if (mark.Rig is { } rig && ReferenceEquals(mark, Eye))
                rig.Jolt(0.35f + 0.65f * (1f - range / RocketShakeRange));
            // And it stings if they are genuinely inside the blast, which a contact fuse on
            // a fast approach makes entirely possible.
            if (range < Projectile.RocketSplash + PlayerTank.Radius)
                DamagePlayer(GrenadeDamage * 3f, mark);
        }
    }

    /// <summary>How far a rocket's concussion still throws the view about.</summary>
    private const float RocketShakeRange = 34f;

    /// <summary>Requests a heavy grenade; honoured only if off cooldown with 10 ammo.</summary>
    public void FirePlayerGrenade(PlayerTank? by = null)
    {
        PlayerTank who = by ?? Player;
        if (who.TryFireGrenade(out Vector2 origin, out Vector2 dir))
            SpawnProjectile(origin, dir, owner: Seat(who), grenade: true);
    }

    /// <summary>
    /// Throws whatever the given equip slot (R/T/Y/U → 0..3) holds. Only the crafted
    /// CRAB CORE is throwable: it lobs out in front like a grenade and detonates into a
    /// ring of lances. Spends the item — the slot empties. A no-op for an empty slot or
    /// a non-throwable item, and never while a set piece owns the craft.
    /// </summary>
    public void UseWeaponSlot(int slot, PlayerTank? by = null)
    {
        // Null is the craft this machine is driving — what every caller before seats meant.
        PlayerTank who = by ?? Player;
        if (who.Captured) return;
        Inventory pack = InventoryOf(Seat(who));
        if (slot < 0 || slot >= pack.Weapons.Length) return;
        ref ItemStack w = ref pack.Weapons[slot];
        if (w.IsEmpty || w.Kind != ItemKind.CrabCore) return;

        // Consume one core and lob it from the muzzle along the craft's heading.
        w.Count--;
        if (w.Count <= 0) w = ItemStack.Empty;

        Vector2 dir = who.Forward;
        Vector2 origin = who.Position + dir * (PlayerTank.Radius + 0.6f);
        SpawnProjectile(origin, dir, owner: Seat(who), crabBomb: true);
    }

    private void SpawnProjectile(Vector2 origin, Vector2 dir, int owner,
        bool grenade = false, bool crabBomb = false,
        float launchHeight = Projectile.BoltHeight, bool laser = false, float pitch = 0f,
        bool piercing = false)
    {
        foreach (var p in _projectiles)
        {
            if (p.Active) continue;
            if (crabBomb) p.FireCrabBomb(origin, dir, owner);
            else if (grenade) p.FireGrenade(origin, dir, owner);
            else p.Fire(origin, dir, owner, launchHeight, laser, pitch, piercing);
            // The report of a barrel firing — same clip for player and enemy
            // shots, since both spawn through here. The thrown core gets a heavier
            // launch thud instead of the light bolt report, and the SPIDER's laser its
            // own dry zap: the cannon clip has enough body that a stream of them at the
            // emitter's cadence stacks into a continuous roar.
            if (crabBomb) Emit(Cue.ThrowWhoosh, origin, owner: owner);
            else if (laser) Emit(Cue.Laser, origin, owner: owner);
            else Emit(Cue.Detonation, origin, owner: owner);
            return;
        }
        // Pool full: silently drop the shot rather than allocate. Rare.
    }

    private void UpdateProjectiles(float dt)
    {
        foreach (var p in _projectiles)
        {
            if (!p.Active) continue;
            p.Update(dt);

            // A player's air shot that ran its course without hitting anything comes
            // down on the horizon: a far-off blast and a puff of debris out where it
            // fell, the explosion fading with distance.
            if (p.JustExpired && p.IsAirShot && p.FromPlayer)
                ExplodeAirShot(p);

            // A thrown CRAB CORE that reached the end of its short lob without striking
            // anything goes off where it landed — the ring of lances erupts there.
            if (p.JustExpired && p.IsCrabBomb)
                StageCrabBlast(p.Position);

            // A rocket has a contact fuse and nothing else: it goes off wherever it
            // stops, which includes the grid it flew into and the empty air at the end
            // of its run.
            if (p.JustExpired && p.IsRocket)
                DetonateRocket(p);

            // The mortar's lob ends on the grid (or, rarely, at the very end of its life):
            // it bursts where it came down, splashing whatever it was dropped onto. A thrown
            // CRAB CORE is also flagged IsGrenade but is handled by its own branch above.
            if (p.JustExpired && p.IsGrenade && !p.IsCrabBomb)
                DetonateMortar(p);

            if (!p.Active) continue;

            // The skyline stops rounds — every round, from anyone. Tested before any
            // target, so a hunter standing behind a wall is genuinely behind it and a
            // shot cannot reach a boss's core through a tower. This is what makes the
            // buildings cover rather than decoration, and it cuts both ways: the thing
            // the player is hiding behind is also the thing they cannot shoot past.
            if (BlockShotOnStructure(p)) continue;

            if (p.FromPlayer)
            {
                // A rocket meeting either crystal goes off against it rather than
                // plinking it: the fuse does not care what it touched, and the blast is
                // already wired to bill both weak points for whatever it reaches.
                if (p.IsRocket
                    && ((Boss is { } rBoss && rBoss.HitsCore(p.Position, p.Height))
                     || (Maw is { } rMaw && rMaw.HitsCrystal(p.Position, p.Height))))
                {
                    DetonateRocket(p);
                    p.Active = false;
                    continue;
                }

                // A mortar (and only it) bursts on either boss it passes over. The lob flies a
                // real arc, so unlike a flat bolt it can be dropped onto or lobbed across the
                // column of a monster whose weak point hangs high overhead — a planar check, the
                // same one the thrown CRAB CORE uses, since a heavy detonation this near strikes
                // the core through the inert armour rather than needing to arrive at its height.
                // This is the tank's indirect answer to a boss it can no longer leap up to hit.
                if (p.IsGrenade && !p.IsCrabBomb
                    && ((Boss is { Alive: true } gBoss
                            && WithinHit(p.Position, gBoss.Position, p.SplashRadius + CrabCore.CoreHitRadius))
                     || (Maw is { Alive: true } gMaw
                            && WithinHit(p.Position, gMaw.Position, p.SplashRadius + MawRig.HitRadius))))
                {
                    DetonateMortar(p);
                    p.Active = false;
                    continue;
                }

                // The Crab-Core's only weak spot: a level air shot threading the
                // raised neon core. Checked before the tanks, since the core sits
                // where nothing else does — high overhead.
                if (!p.IsGrenade && !p.IsRocket
                    && Boss is { } liveBoss && liveBoss.HitsCore(p.Position, p.Height))
                {
                    if (liveBoss.DamageCore(PlayerShotDamage))
                        DestroyBoss(liveBoss);
                    else
                        // A shriek over the impact, pitched by how far gone the core
                        // is — the boss loses composure as it's worn down.
                        Emit(Cue.CoreHit, liveBoss.Position, 1f - liveBoss.CoreFraction);
                    p.Active = false;
                    continue;
                }

                // The Maw-Core's crystal, hanging at the top of a jump. Same bargain
                // as the crab's core and a stricter one: this band sits so far above
                // barrel height that a grounded shot cannot reach it at all, so
                // landing this is proof the player was in the air when they fired.
                if (!p.IsGrenade && !p.IsRocket
                    && Maw is { } liveMaw && liveMaw.HitsCrystal(p.Position, p.Height))
                {
                    if (liveMaw.DamageCrystal(PlayerShotDamage))
                        DestroyMaw(liveMaw);
                    else
                        Emit(Cue.MawHurt, liveMaw.Position, 1f - liveMaw.CrystalFraction);
                    p.Active = false;
                    continue;
                }

                // A round riding high over a hunter's hull passes over it. The mortar joins
                // the rocket here: while it is still up the arc it sails over the hunters
                // massed in front of the target, and only meets them once it has come back
                // down onto them. Everything else in the pool travels flat at barrel height,
                // where the check would be noise.
                bool overhead = (p.IsRocket || p.IsGrenade) && p.Height > EnemyTank.BodyHeight + 1f;

                foreach (var e in Enemies)
                {
                    if (!e.Alive || overhead) continue;
                    // Tested against where the shooter saw it, not where it is. On the host's
                    // own shots and in a solo run this is the hunter's live position and
                    // nothing has changed; for a client at 120 ms it is the difference between
                    // leading a hunter correctly and watching the round pass through it.
                    if (!WithinHit(p.Position, Rewound(e.HitId, p.Owner, e.Position),
                            EnemyTank.Radius)) continue;

                    if (p.IsPiercing)
                    {
                        // The AP slug bulls straight on through the line: bill each body once —
                        // PierceLast guards the one it is currently crossing — and keep flying.
                        if (!ReferenceEquals(e, p.PierceLast))
                        {
                            DamageEnemy(e, SlugDamage, p.Owner);
                            p.PierceLast = e;
                        }
                        continue;
                    }

                    if (p.IsCrabBomb)
                        StageCrabBlast(p.Position); // erupts into the lance ring here
                    else if (p.IsRocket)
                        DetonateRocket(p);
                    else if (p.IsGrenade)
                        DetonateMortar(p);     // the shell has landed on the cluster
                    else
                        DamageEnemy(e, PlayerShotDamage, p.Owner);
                    p.Active = false;
                    break;
                }

                // And the squads, which are somewhere else entirely: up in the city rather
                // than on the grid, so they get their own pass with a height gate on it.
                if (p.Active) StrikeSoldiers(p);

                // Last of all, the other players. Only reached when the round found nothing
                // hostile, so a shot that was going to kill a hunter still kills the hunter
                // rather than being eaten by a team-mate standing behind it — and only ever
                // when the host turned friendly fire on. CanHarm refuses a craft its own
                // round whatever the setting says.
                if (p.Active) StrikePlayers(p);
            }
            else
            {
                // Enemy shot vs player. The hunters elevate onto the craft's height now,
                // so a leaping player is no longer simply spared — the round has to arrive
                // at the right place on the plane *and* the right height up the column, the
                // craft's body centre give or take its own height. The jump is still a
                // dodge, but by out-moving the shot in flight (no lead) rather than by the
                // old blanket immunity the instant a wheel left the grid.
                //
                // A captured player is the one exception. Being airborne used to be what
                // spared them while a set piece had them helpless; now that height no
                // longer grants immunity, the cinematic hold has to say so outright — a
                // player who cannot act must not be shot at by anything else.
                // Enemy rounds die in a TANK's smoke the same way the shooters are blinded by
                // it — a screen the player drove behind actually stops what is already in the
                // air, not only what is about to be fired.
                if (SmokeAbsorbs(p.Position))
                {
                    p.Active = false;
                    continue;
                }

                // Every craft on the field, not just the one at this keyboard. The first one
                // the round arrives at takes it and the round dies there, so a bolt cannot
                // rake a line of players.
                foreach (var mark in Players)
                {
                if (!mark.Alive || mark.Away) continue;
                float aimH = mark.Height + EnemyTank.AimHeight;
                if (!mark.Captured
                    && WithinHit(p.Position, mark.Position, PlayerTank.Radius)
                    && MathF.Abs(p.Height - aimH) < EnemyHitVertical)
                {
                    // Directional armour: on the TANK the blow is turned by the sloped front,
                    // taken square on the flanks and taken worse from behind (returns 1 for
                    // every other chassis, which has no plating). Facing is the tank's defence.
                    //
                    // A tracer arriving from this side is a soldier's rifle — the only enemy
                    // weapon in the game that isn't a cannon — and bills the lighter figure.
                    // Nothing else the field can fire is directed, so the flag stands in for
                    // the shooter without needing to be carried on the round.
                    float bite = p.IsTracer ? SoldierShotDamage : EnemyShotDamage;

                    // The SPIDER holding a machine out in front of its core: the round
                    // meets the hostage instead. This is the counterplay to having the
                    // weak point on the front of the craft, and it is why the claw is a
                    // defensive tool before it is an offensive one — the field ends up
                    // shooting its own, and the player is standing behind it.
                    if (mark.Claw is { } claw
                        && claw.ShieldsFrom(p.Velocity, mark.Forward)
                        && claw.Victim is { } shieldBody)
                    {
                        DamageEnemy(shieldBody, claw.Soak());
                        mark.Jolt(0.15f);
                        Emit(Cue.Hit, mark.Position, owner: Seat(mark));
                        p.Active = false;
                        break;
                    }

                    DamagePlayer(bite * mark.ArmorMultiplierFromShot(p.Velocity), mark);

                    // And a round that found the core while the lance was winding takes
                    // the wind with it. The charge is gone, the emitter is dead for a
                    // beat, and the two seconds of standing still that bought it were
                    // spent for nothing — which is exactly the risk the brace is the
                    // reward for. See SpiderWeapon.Break.
                    if (mark.StruckInTheCore(p.Velocity)
                        && mark.Spider is { } emitter && emitter.Break())
                    {
                        mark.Rooted = false;
                        mark.Jolt(0.5f);
                        if (ReferenceEquals(mark, Player))
                        {
                            Audio.SetLanceCharge(false, 0f);
                            Audio.PlayWarning(mark.Position);
                        }
                    }

                    p.Active = false;
                    break;
                }
                }
            }
        }
    }

    /// <summary>
    /// Applies enemy damage to the player and sounds the right combat cue: the
    /// death explosion if this shot ends the run; otherwise the low-shield
    /// warning while the craft sits at/below the danger line (it *replaces* the
    /// normal hit clip there, so a wounded player hears the alarm on every hit);
    /// otherwise the plain hit. A respawn refills the shield above the line, so
    /// hits go back to the normal clip.
    /// </summary>
    /// <summary>
    /// A player's round against the other players. Silent and skipped entirely unless the
    /// host turned friendly fire on, which is the common case — this is the last thing any
    /// round is tested against, so with it off the pass costs one boolean.
    ///
    /// The shooter is read back off the round rather than passed in, because by the time a
    /// bolt lands the craft that fired it may be somewhere else entirely, or dead.
    /// </summary>
    private void StrikePlayers(Projectile p)
    {
        if (!Match.FriendlyFire) return;
        if (p.Owner is Projectile.NoOwner or Projectile.AllyOwner) return;
        if ((uint)p.Owner >= (uint)Players.Count) return;

        PlayerTank shooter = Players[p.Owner];
        for (int seat = 0; seat < Players.Count; seat++)
        {
            PlayerTank mark = Players[seat];
            if (!mark.Alive || mark.Captured || mark.Away) continue;
            if (!CanHarm(shooter, mark)) continue;

            float aimH = mark.Height + EnemyTank.AimHeight;
            // Where the shooter saw them, so a duel between two clients is decided by what
            // each of them could see rather than by which of them has the better ping.
            if (!WithinHit(p.Position, RewoundSeat(seat, p.Owner, mark.Position),
                    PlayerTank.Radius)) continue;
            if (MathF.Abs(p.Height - aimH) >= EnemyHitVertical) continue;

            // Billed as a player's round, because it is one — the same damage a hunter
            // would have taken from it, turned by the victim's own plating.
            DamagePlayer(PlayerShotDamage * mark.ArmorMultiplierFromShot(p.Velocity), mark);
            mark.Jolt(0.2f);
            p.Active = false;
            return;
        }
    }

    private void DamagePlayer(float amount, PlayerTank? to = null)
    {
        // Null is the craft this machine is driving. Every caller that predates seats meant
        // exactly that, and in a solo run there is nothing else it could mean.
        PlayerTank victim = to ?? Player;

        // The VIRUS stands its worn host between itself and every blow: while a body is on,
        // damage drains the host's integrity instead of the player's shield, and only what a
        // broken husk fails to soak spills through. Exposed, it is the opposite — the naked
        // mote takes it amplified, because a payload has no armour of its own.
        if (victim.Virus is { } virus)
        {
            if (virus.Hosted)
            {
                amount = virus.AbsorbDamage(amount);
                // A hit that emptied the meter burst the host: stage that here, at the
                // moment it happens, because this pass runs after the tick's virus events
                // and a flag drained there would be a frame late or lost.
                if (!virus.Hosted) StageVirusBurst(overload: false, victim);

                if (amount <= 0f)
                {
                    // Wholly soaked: the body took it, the player didn't. A plain hit cue,
                    // so a corrupted host being worn down still sounds like being shot at.
                    Emit(Cue.Hit, victim.Position, owner: Seat(victim));
                    return;
                }
                // The host broke and the overflow is about to reach a naked mote — it eats
                // that spill amplified, the same as any exposed hit.
                amount *= MoteVulnerability;
            }
            else
            {
                amount *= MoteVulnerability;
            }
        }

        victim.TakeDamage(amount);

        // A craft blowing up and a hull absorbing a round are world sounds: anyone near the
        // fight hears them, on the host and every client, positioned to their own craft. That
        // is the whole point of parity — a team-mate being torn into across the square should
        // be audible, not silent. The low-shield warning is the exception: it is a personal
        // HUD alarm, so it stays with the craft it is warning and never travels.
        if (!victim.Alive)
        {
            Emit(Cue.Explosion, victim.Position);
            // A craft going down is the loudest event on the scoreboard and the one the room
            // most needs told about. Counted here, at the alive→dead transition, so a revive
            // that keeps the craft alive is correctly not a death — the same distinction the
            // explosion cue already makes.
            int seat = Seat(victim);
            if ((uint)seat < (uint)_deaths.Length) _deaths[seat]++;
            Announce?.Invoke(victim.Spectating
                ? $"{NameOrSeat(seat)} IS OUT"
                : $"{NameOrSeat(seat)} WENT DOWN");
        }
        else
            Emit(Cue.Hit, victim.Position);
        if (victim.Alive && victim.ShieldFraction <= LowShieldWarning)
            Emit(Cue.Warning, victim.Position, owner: Seat(victim), personal: true);
    }

    /// <summary>
    /// Deals damage to an enemy and sounds the explosion if this hit is what
    /// destroys it — the alive→dead transition, so a cluster killed by one blast
    /// each reports its own death.
    /// </summary>
    /// <param name="by">The seat that earned it, where anything knows. Optional because a
    /// hunter can also be crushed by falling masonry or torn apart by its own squad, and a
    /// scoreboard that insisted on a culprit would have to invent one.</param>
    private void DamageEnemy(EnemyTank enemy, float amount, int by = Projectile.NoOwner)
    {
        bool wasAlive = enemy.Alive;
        enemy.TakeDamage(amount);
        if (wasAlive && !enemy.Alive)
        {
            if (by != Projectile.NoOwner)
            {
                Credit(by);
                Announce?.Invoke($"{NameOrSeat(by)} KILLED {(enemy.IsElite ? "AN ELITE" : "A HUNTER")}");
            }
            Emit(Cue.Explosion, enemy.Position);
            // Break the hunter into flying polygon shards + sparks at roughly its
            // body's centre height (the mesh sits on the grid, scaled up in view).
            // Roughly the middle of the hull, taken off the mesh's own size rather than
            // typed in, so a hunter that is resized throws its wreckage from its new middle.
            var origin = new Vector3(enemy.Position.X, EnemyTank.BodyHeight * 0.5f,
                enemy.Position.Y);
            Color body = enemy.IsElite ? Palette.EliteFill : Palette.EnemyFill;
            Debris.Burst(origin, body, enemy.IsElite);

            // Every kill leaves salvage: a stray round or a battery cell, dropped at the
            // corpse and stowed the instant the craft drives over it, exactly as the floating
            // salvage is. Left on the field rather than out in the fog, so clearing a
            // firefight is worth doubling back through. Unlike the ambient drip it ignores the
            // salvage cap — a kill you earned always leaves its reward.
            var kind = Random.Shared.NextSingle() < DropBatteryShare
                ? PickupKind.Battery : PickupKind.Ammo;
            Pickups.Add(new Pickup(enemy.Position, kind));
        }
    }

    /// <summary>What fraction of a kill's drop is a battery (the "heal"); the rest are stray
    /// rounds. Weighted toward ammo so a firefight feeds the gun it was fought with, and the
    /// battery is the rarer prize.</summary>
    private const float DropBatteryShare = 0.45f;

    // --- Falling-debris crush -----------------------------------------------------

    /// <summary>How long the player is immune to further crush after taking a chunk: the
    /// brief invulnerability that stops one collapse deleting the craft in three frames, and
    /// paces being pinned under rubble into damage-over-time rather than an instant erase.</summary>
    private const float CrushInvuln = 0.6f;

    /// <summary>Turns a chunk's mass × impact speed into hit points. Tuned so a chip stings,
    /// a chunk staggers and a full section coming down at speed is lethal.</summary>
    private const float CrushDamagePerImpulse = 3f;

    /// <summary>The i-frame clock, per seat. Shared, it would have one player's lucky escape
    /// cover everybody standing under the same collapse — and, worse, one player being pinned
    /// under rubble would make the whole team briefly immune to it.</summary>
    private readonly float[] _crushInvuln = new float[MatchSettings.MaxSeats];

    /// <summary>
    /// Bills every falling structural chunk that lands on a character this tick. Only pieces
    /// carrying <see cref="Shard.Mass"/> count — cosmetic sparks and hull shards are skipped —
    /// and only while a chunk is near the ground and actually coming down, so one still
    /// fountaining up off a strike point hasn't crushed anyone yet. Damage is mass × speed, so
    /// a slow-settling flake is harmless where a section at speed kills.
    ///
    /// The player gets brief i-frames after a hit and, while any shield holds, cannot be
    /// one-shot — a section guts the shield but doesn't end the run in a single blow. An enemy
    /// caught under a chunk is crushed outright, and the chunk is spent (mass zeroed, so it
    /// tumbles on as cosmetic wreckage) so one section doesn't erase a whole line of them.
    /// </summary>
    private void ResolveCrush(float dt)
    {
        for (int i = 0; i < _crushInvuln.Length; i++)
            if (_crushInvuln[i] > 0f) _crushInvuln[i] -= dt;

        var shards = Debris.Shards;
        for (int i = 0; i < shards.Length; i++)
        {
            ref Shard c = ref shards[i];
            if (!c.Active || c.Mass <= 0f) continue;
            // Near the grid and descending: a chunk higher than head height, or one still on
            // its way up off the rupture, is not landing on anybody this tick.
            if (c.Position.Y > 3f || c.Velocity.Y > 1f) continue;

            float speed = c.Velocity.Length();
            float damage = c.Mass * speed * CrushDamagePerImpulse;
            if (damage < 1f) continue;

            var atXZ = new Vector2(c.Position.X, c.Position.Z);
            float chunkR = c.Size * 0.6f;

            // The players: grounded-ish, not mid-cinematic, and off i-frames. Every seat, not
            // just this machine's — a tower coming down on a team-mate used to pass straight
            // through them, so the one thing the whole destruction system is for could only
            // ever happen to one of the twenty people standing under it.
            for (int seat = 0; seat < Players.Count; seat++)
            {
                PlayerTank who = Players[seat];
                if (who.Away || _crushInvuln[Math.Min(seat, _crushInvuln.Length - 1)] > 0f) continue;
                if (who.Captured || who.Height >= 3f) continue;

                float reach = PlayerTank.Radius + chunkR;
                if (Torus.DistanceSquared(atXZ, who.Position) > reach * reach) continue;

                float dmg = damage;
                // An active shield eats a would-be one-shot: a section can gut it but not
                // end the run in a single blow while any of the shield remains.
                if (dmg >= who.Shield && who.Shield > 1f) dmg = who.Shield - 1f;
                DamagePlayer(dmg, who);
                _crushInvuln[Math.Min(seat, _crushInvuln.Length - 1)] = CrushInvuln;
            }

            // Enemies: a chunk that lands on a hunter crushes it and is spent.
            bool spent = false;
            foreach (var e in Enemies)
            {
                if (!e.Alive) continue;
                float reach = EnemyTank.Radius + chunkR;
                if (Torus.DistanceSquared(atXZ, e.Position) > reach * reach) continue;
                DamageEnemy(e, damage);
                c.Mass = 0f;   // spent — no longer bills anyone, keeps tumbling as wreckage
                spent = true;
                break;
            }
            if (spent) continue;

            // And the squads, who are far more likely to meet falling masonry than anyone
            // else on the field: they live in the part of the world that comes down. Only a
            // soldier near the grid is caught, for the same reason the player is — a chunk
            // still high in the air has not landed on anybody yet.
            foreach (var s in Soldiers)
            {
                if (!s.Alive || s.Height > 3f) continue;
                float reach = EnemySoldier.Radius + chunkR;
                if (Torus.DistanceSquared(atXZ, s.Position) > reach * reach) continue;
                DamageSoldier(s, damage);
                c.Mass = 0f;
                break;
            }
        }
    }

    /// <summary>
    /// Stages the horizon detonation of a spent air shot: a spark-and-chunk burst
    /// where it came down and the explosion clip played back at a distance-faded
    /// volume, so a shot fizzling far downrange reads as a faint thud on the skyline.
    /// </summary>
    private void ExplodeAirShot(Projectile p)
    {
        var origin = new Vector3(p.Position.X, MathF.Max(0.4f, p.Height), p.Position.Y);
        Debris.Burst(origin, Palette.Flag, elite: false);
        Emit(Cue.ExplosionAt, p.Position);
    }

    /// <summary>
    /// The Crab-Core's death: its parts blow apart in a rain of debris and the whole
    /// rig glitches out (the renderer drives the tearing from the boss's death
    /// progress). A full-volume blast at the core, a chassis burst at the body, and a
    /// shower from each leg where it planted, so the giant comes apart everywhere at
    /// once rather than popping like a tank.
    /// </summary>
    private void DestroyBoss(CrabCore boss)
    {
        // Not the stock one-shot blast the tanks get — the boss gets its own death:
        // a falling scream over a cascade of pitched detonations, spread across the
        // glitch-apart animation rather than fired all at once.
        Emit(Cue.BossDeath, boss.Position);

        var c = boss.Position;
        // The core itself, up high where the gem sat — a hot neon-red burst.
        Debris.Burst(new Vector3(c.X, CrabCore.CoreHitHeight, c.Y), Palette.NeonRed, elite: true);
        // The carapace shattering at mid-body height.
        Debris.Burst(new Vector3(c.X, CrabRig.BodyHeight * CrabRig.Scale, c.Y),
            Palette.CrabChassis, elite: true);
        // Each leg flings its own chunks from where it met the floor.
        foreach (var leg in CrabRig.Legs)
        {
            Vector2 foot = CrabRig.FootWorldXZ(leg, c, boss.Heading);
            Debris.Burst(new Vector3(foot.X, 1.0f, foot.Y), Palette.CrabChassis, elite: false);
        }

        // The kill leaves a shard of the core behind — a CRAB CORE fragment to collect.
        // Three of them craft a thrown CRAB CORE of the player's own.
        Pickups.Add(new Pickup(boss.Position, PickupKind.CrabFragment));
    }

    /// <summary>
    /// Grenade blast: deals damage to every live enemy whose centre falls inside
    /// the projectile's splash radius — the whole cluster feels one hit.
    /// </summary>
    private void Detonate(Projectile p)
    {
        float reach = p.SplashRadius + EnemyTank.Radius;
        foreach (var e in Enemies)
        {
            if (!e.Alive) continue;
            if (WithinHit(p.Position, e.Position, reach))
                DamageEnemy(e, GrenadeDamage);
        }
    }

    private static bool WithinHit(Vector2 a, Vector2 b, float radius)
        => Torus.DistanceSquared(a, b) <= radius * radius;

    // --- The thrown CRAB CORE's radial blast -----------------------------------

    /// <summary>Crab-beam-tier damage each lance of the blast deals per tick — enough
    /// to erase a hunter over the burn and badly hurt an elite.</summary>
    private const float CrabBlastDamage = BeamDamage;

    /// <summary>How often each lance bites while the star burns — the same cadence the
    /// boss's own beam uses, so a caught enemy is chewed a few times over the short life
    /// rather than instantly or once.</summary>
    private const float CrabBlastTickInterval = BeamTickInterval;

    private float _crabBlastTick;

    /// <summary>
    /// Stages a CRAB CORE detonation at <paramref name="at"/>: raises the lance ring,
    /// sounds its creepier, layered echo of the boss's beam, and throws a hot neon
    /// burst of debris at the centre.
    /// </summary>
    private void StageCrabBlast(Vector2 at)
    {
        Blasts.Add(new CrabCoreBlast(at));
        Emit(Cue.CrabCoreBlast, at);
        Debris.Burst(new Vector3(at.X, CrabCoreBlast.CoreHeight, at.Y), Palette.NeonRed, elite: true);
        Debris.Burst(new Vector3(at.X, 0.4f, at.Y), Palette.CrabChassis, elite: false);
    }

    /// <summary>
    /// Ages the live blasts and bites anything inside their energy field. Now that the
    /// burst fires in every direction and churns, the damage is a swelling-then-shrinking
    /// sphere around the core rather than a per-lance ray test: any enemy within the
    /// blast's current planar reach (which grows with the swell and retracts as it dies)
    /// takes a bite. Ticked so the damage doesn't scale with the frame rate and a caught
    /// enemy is worn down across the three seconds.
    /// </summary>
    private void UpdateCrabBlasts(float dt)
    {
        if (Blasts.Count == 0) return;

        _crabBlastTick -= dt;
        bool bite = _crabBlastTick <= 0f;
        if (bite) _crabBlastTick = CrabBlastTickInterval;

        foreach (var blast in Blasts)
        {
            blast.Update(dt);
            if (!bite) continue;

            float field = blast.CurrentDamageRadius;
            if (field <= 0f) continue;

            // The ring of lances is beam-grade, so anything of the city caught inside the
            // swell comes down with everything else. Structures are struck once and then
            // stop being blockers, so a core thrown into a plaza takes the whole block on
            // the tick it reaches them rather than gnawing at them for three seconds.
            foreach (var s in Structures)
            {
                if (s.Falling) continue;
                if (Torus.DistanceSquared(s.Position, blast.Position)
                    <= (field + BlastBossMargin) * (field + BlastBossMargin))
                    FellStructure(s);
            }

            // The blast is a sphere of energy, not a disc on the floor, so it reaches a
            // soldier hanging in the air over it exactly as far as it reaches a hunter
            // standing beside it.
            DamageSoldiersInBlast(blast.Position, CrabCoreBlast.CoreHeight, field, CrabBlastDamage);

            float reach = field + EnemyTank.Radius;
            float reachSq = reach * reach;
            foreach (var e in Enemies)
            {
                if (!e.Alive) continue;
                // Measured across the torus so a burst near the seam still catches bodies
                // just over it.
                if (Torus.DistanceSquared(e.Position, blast.Position) <= reachSq)
                    DamageEnemy(e, CrabBlastDamage);
            }

            // The big monsters are fair game too — a thrown core is powerful enough to
            // bite the Crab-Core's own gem and the Maw-Core's crystal. Both are only
            // damageable through those weak points normally, but a detonation this close
            // simply strikes them directly; a killing bite stages the usual death (which,
            // for the crab, also drops the fragment its own kill would).
            float bossReach = field + BlastBossMargin;
            float bossReachSq = bossReach * bossReach;

            if (Boss is { Alive: true } boss
                && Torus.DistanceSquared(boss.Position, blast.Position) <= bossReachSq)
            {
                if (boss.DamageCore(CrabBlastDamage)) DestroyBoss(boss);
                else Emit(Cue.CoreHit, boss.Position, 1f - boss.CoreFraction);
            }

            if (Maw is { Alive: true } maw
                && Torus.DistanceSquared(maw.Position, blast.Position) <= bossReachSq)
            {
                if (maw.DamageCrystal(CrabBlastDamage)) DestroyMaw(maw);
                else Emit(Cue.MawHurt, maw.Position, 1f - maw.CrystalFraction);
            }
        }

        Blasts.RemoveAll(b => !b.Active);
    }

    /// <summary>Extra planar slack added to a blast's field when checking the two big
    /// monsters, whose bodies are far larger than a tank's — so a burst that clearly
    /// engulfs one connects even when its centre is a few units off the monster's.</summary>
    private const float BlastBossMargin = 6f;
}
