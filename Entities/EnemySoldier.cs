using System.Numerics;
using Unrendered.Core;
using Unrendered.World;

namespace Unrendered.Entities;

/// <summary>
/// What the city looks like to something that hangs from it. The one question a flier
/// ever asks the world — "given where I am and where I want to go, what can I bite?" —
/// handed over as an interface so the brain below stays a pure function of its own state
/// and a headless check can answer it with a single made-up tower.
/// </summary>
public interface IAnchorField
{
    /// <summary>
    /// Finds the best thing to throw a hook at from <paramref name="from"/>, for something
    /// meaning to travel along <paramref name="wish"/>. Returns the surface point in
    /// coordinates measured around <paramref name="from"/> (not wrapped), so the caller can
    /// treat it as a straight line, and which building it belongs to.
    /// </summary>
    bool TryFindSwing(Vector3 from, Vector3 wish, float maxRange,
        out Vector3 point, out Structure? holding);
}

/// <summary>
/// What the squad has told one soldier to do this second. The whole of the coordination
/// is in these four words: the members run their own flying, and the squad only ever
/// decides <em>which side</em> each of them works and <em>whose turn</em> it is to go in.
/// </summary>
public enum SoldierOrder
{
    /// <summary>Sit still and watch. Nothing has been seen yet.</summary>
    Hold,

    /// <summary>Take your bearing — swing out and around to your side of the target.</summary>
    Flank,

    /// <summary>Hold the arc and keep shooting. The pressure that sets a kill up.</summary>
    Press,

    /// <summary>Go. The committed run, blades out, one pass.</summary>
    Strike,
}

/// <summary>Where one soldier is in their own flying, under whatever order they hold.</summary>
public enum SoldierMove
{
    /// <summary>Clung to a wall on a short cable, weight on the boots. The state they
    /// wait in and the state they retreat to.</summary>
    Perched,

    /// <summary>Swinging in from range toward their assigned side.</summary>
    Closing,

    /// <summary>Arcing round the target at engagement range, firing on the way past.</summary>
    Circling,

    /// <summary>The run. Everything pointed at one place and the blades out.</summary>
    Diving,

    /// <summary>Off the back of a pass: away, up, and round for another.</summary>
    Breaking,

    /// <summary>On the grid. No gas, no anchor, or freshly dropped out of the sky —
    /// the one state where they can be caught.</summary>
    Running,
}

/// <summary>
/// A soldier of whatever is left out here: a person with the same twin cable launchers
/// the player's SOLDIER carries, flown by a brain rather than by a hand.
///
/// Everything else on this roster is a machine that drives at you. This one does not
/// drive at all — it has no throttle and no heading to steer, only a velocity vector, two
/// steel hooks and the city. It converts height into speed and speed back into height,
/// hangs its arc off a tower <em>ahead</em> of where it means to be rather than the one it
/// is passing, lets go at the bottom of the swing and keeps every bit of what the arc
/// built. That is the entire difference between this enemy and a hunter, and it is why
/// they are dangerous: a tank can be out-driven, and a thing that falls upward through a
/// skyline cannot.
///
/// They never come alone. Four of them arrive as a <see cref="SoldierSquad"/>, which
/// hands each member a bearing to work and decides whose turn it is to go in — so what
/// the player meets is not four identical hunters but a ring closing from four sides
/// with one blade coming out of it at a time.
///
/// Pure state and physics, in the same spirit as <see cref="SoldierRig"/>: no Raylib, no
/// world lookups beyond the one <see cref="IAnchorField"/> question, and every number that
/// decides how a cable <em>feels</em> is taken from the player's own rig, so retuning that
/// chassis retunes the thing hunting it too.
/// </summary>
public sealed class EnemySoldier
{
    // --- The body ----------------------------------------------------------------

    /// <summary>Where their boots are, in canonical wrapped torus coordinates.</summary>
    public Vector2 Position;

    /// <summary>Metres of air under those boots.</summary>
    public float Height;

    /// <summary>Full 3D momentum. As with the player's rig, this rather than a heading and
    /// a throttle is what the chassis actually is.</summary>
    public Vector3 Velocity;

    /// <summary>Which way they are looking — where the rifle points, which is decoupled
    /// from where they are travelling exactly as the player's is.</summary>
    public float Heading;

    /// <summary>The lean into an arc, in radians. Read only by the renderer, but decayed
    /// on the sim's clock so it means the same thing on every machine.</summary>
    public float Bank { get; private set; }

    public float Shield;
    public bool Alive => Shield > 0f;

    /// <summary>One of the four wears the squad's marks and is a shade harder to kill.
    /// Killing them does not break the squad — the survivors simply press harder — but it
    /// is the shot worth taking, because the leader is the one that shoots straight.</summary>
    public readonly bool IsLeader;

    /// <summary>The gas bottle, 0..<see cref="MaxGas"/>. Every reel and every burst spends
    /// it and coasting refills it, so a squad that has been chasing hard for a while starts
    /// dropping out of the sky — which is the only reliable window the player gets.</summary>
    public float Gas { get; private set; } = MaxGas;

    /// <summary>Seconds of stagger left after a bad arrival. They cannot fire or throw a
    /// hook through it, and the renderer drops them to a knee.</summary>
    public float Stagger { get; private set; }

    // --- The kit ------------------------------------------------------------------

    /// <summary>The two hooks, the same steel the player throws — literally the same type,
    /// so the renderer draws both sides' cables through one function.</summary>
    public readonly GrappleHook Left = new(right: false);
    public readonly GrappleHook Right = new(right: true);

    public GrappleHook Hook(bool right) => right ? Right : Left;
    public bool AnyAnchored => Left.Anchored || Right.Anchored;

    // --- The corruption ------------------------------------------------------------
    //
    // What a VIRUS does to one of these, in two stages, and the two stages are the whole
    // mechanic. A round from the mote does not kill a soldier — it <em>seeds</em> them.
    // For a few seconds they are tagged: slowed, coming apart at the edges, and close
    // enough to dead that the mote can fly into them and put them on.
    //
    // And if the mote does not cash that in, the seed takes root on its own. A tag that
    // expires turns the soldier into a carrier: they stop being the squad's, they turn on
    // the people they arrived with, and they rot until there is nothing left. Which means
    // every shot the mote fires is a question it has a few seconds to answer — wear this
    // one, or let it loose on its friends.

    /// <summary>Seconds of tag left. Above zero they are seeded and takeable.</summary>
    public float Tagged { get; private set; }

    /// <summary>True once the corruption has taken root: they belong to the plague now.
    /// The squad drops them, the world points them at their own side, and they are dying
    /// the whole time.</summary>
    public bool Carrier { get; private set; }

    /// <summary>How corrupted they look, 0..1 — a tag spreading, or a carrier fully gone.
    /// Read only by the renderer and the radar.</summary>
    public float Corruption => Carrier ? 1f : Math.Clamp(1f - Tagged / TagTime, 0f, 1f) * TagLook;

    /// <summary>How long a seeded soldier stays takeable before the corruption takes root
    /// on its own. The window the whole weapon is about: long enough to cross to them at
    /// the mote's pace, short enough that a shot fired hopefully is a shot spent.</summary>
    public const float TagTime = 3.5f;

    /// <summary>How hard a seeded body drags. Not a stop — a soldier fighting their own kit
    /// — but enough that a tagged one is visibly the one you can catch.</summary>
    private const float TagDrag = 0.55f;

    /// <summary>How far the tag's veins get before it either resolves or roots. Held under
    /// one so a tagged soldier and a carrier never look the same.</summary>
    private const float TagLook = 0.75f;

    /// <summary>How long a carrier lasts once it turns. They are running on stolen code with
    /// nothing maintaining it, and it shows: this is a body on its way out, spending what is
    /// left on the people standing next to it.</summary>
    private const float CarrierLife = 14f;

    /// <summary>And how fast one comes apart with nothing left to turn on. A plague with no
    /// hosts left eats itself, which is also the answer to what a carrier should do when the
    /// field is clear — it should stop existing, rather than orbit a player it has no
    /// quarrel with.</summary>
    private const float StarvedRot = 6f;

    /// <summary>
    /// Seeds this soldier with the mote's corruption. Re-tagging refreshes the window rather
    /// than stacking, so a second round into the same body buys time rather than doubling
    /// anything. A no-op on one that has already turned — there is nothing left to seed.
    /// </summary>
    public void Tag()
    {
        if (Carrier) return;
        Tagged = TagTime;
        // Being shot at all breaks a perch, and being shot with this breaks it hardest.
        if (Move == SoldierMove.Perched) LeavePerch(4f);
    }

    /// <summary>
    /// Turns them. Called when a tag runs out with nobody having claimed it, and by the
    /// world when an overload's blast catches one. From here they are the plague's: the
    /// squad lets them go, the world points them at their own side, and the clock runs.
    /// </summary>
    public void Turn()
    {
        if (Carrier) return;
        Carrier = true;
        Tagged = 0f;
        Order = SoldierOrder.Press;
        // Shield is left where it is: a carrier is not healed by turning, and one shot to
        // pieces before it turned is a carrier with one shot left in it.
        _rot = 1f / CarrierLife;
    }

    private float _rot;

    /// <summary>Set on the tick the corruption takes root, so the world can voice it once.</summary>
    public bool JustTurned { get; private set; }

    /// <summary>
    /// Ages the corruption. A tag either gets claimed by the mote or roots on its own; a
    /// carrier burns down whatever it has left, faster when there is nobody left to spend it
    /// on. Nothing here decides <em>who</em> they fight — that is the world's, and it is one
    /// argument to the step.
    /// </summary>
    private void StepCorruption(float dt, bool starved)
    {
        if (Tagged > 0f)
        {
            Tagged -= dt;
            if (Tagged <= 0f)
            {
                Tagged = 0f;
                Turn();
                JustTurned = true;
            }
        }

        if (!Carrier) return;

        // A carrier is dying the whole time it is useful. With no side left to turn on it
        // goes far quicker: a plague that has run out of hosts eats itself rather than
        // hanging around the sky with nothing to do.
        float rate = starved ? _rot * StarvedRot : _rot;
        Shield -= Shield0 * rate * dt;
    }

    /// <summary>What one of these was worth at full health, so the rot can be billed as a
    /// fraction of a life rather than as a flat number that would erase a leader instantly
    /// and leave a standard soldier standing.</summary>
    private readonly float Shield0;

    // --- What the squad has told them ---------------------------------------------

    /// <summary>This second's order. Written by the squad every tick.</summary>
    public SoldierOrder Order = SoldierOrder.Hold;

    /// <summary>The bearing off the target this member works, in radians. The squad keeps
    /// the four of them a quarter-turn apart and rotates the whole ring, which is what
    /// makes a squad feel like it is <em>surrounding</em> the player rather than queueing
    /// up behind them.</summary>
    public float Bearing;

    /// <summary>Which of the four they are. Only used to stagger their clocks so the squad
    /// never breathes in unison.</summary>
    public int Slot;

    /// <summary>The key this soldier goes by in the host's rewind history, so a laggy client's
    /// shot can be tested against where it actually was on that client's screen. Assigned
    /// lazily host-side — see <c>World.RecordRewind</c>.</summary>
    public int HitId;

    /// <summary>
    /// True while this one is fighting for the player rather than against them — a squad
    /// escorting the body their comrade is being worn as. Written by the world every tick,
    /// because allegiance is not something a soldier can work out from inside their own
    /// head: they have not changed their mind about anything, they are simply flying beside
    /// somebody they believe is one of them.
    ///
    /// Read by the world (whose side their rounds land on) and by the renderer (which side
    /// the player can see they are on). The brain itself never looks at it: it flies at
    /// whatever target it is handed, and pointing it at something else is the whole of
    /// changing sides.
    /// </summary>
    public bool Allied;

    /// <summary>Set while they are escorting somebody and have nothing to shoot at. They
    /// hold formation and hold their fire — a squad flying cover for one of their own does
    /// not loose rounds at them for want of anything better to do.</summary>
    private bool _holdFire;

    public SoldierMove Move { get; private set; } = SoldierMove.Perched;

    /// <summary>True while the blades are out — the committed run. The world reads it to
    /// decide whether a pass this close is a kill attempt, and the renderer puts the steel
    /// in their hands.</summary>
    public bool BladesOut => Move == SoldierMove.Diving && Stagger <= 0f;

    // --- Single-frame events the world drains -------------------------------------

    /// <summary>Set on the tick a rifle round leaves. <see cref="ShotOrigin"/> and
    /// <see cref="ShotDir"/> are the firing solution.</summary>
    public bool JustFired { get; private set; }
    public Vector3 ShotOrigin { get; private set; }
    public Vector3 ShotDir { get; private set; }

    /// <summary>Set the tick they push off a wall, so the world can throw the dust.</summary>
    public bool JustLaunched { get; private set; }

    /// <summary>Set the tick they arrive on the grid hard enough to matter.</summary>
    public bool JustLanded { get; private set; }
    public float LandingSpeed { get; private set; }

    // --- Numbers ------------------------------------------------------------------
    // The feel constants are pulled from the player's own rig wherever there is a
    // shared one, on purpose: these are the same launchers and the same steel, and a
    // player who has learned what a cable does should be able to read what the thing
    // hunting them is about to do out of the same physics.

    /// <summary>
    /// How much bigger than a person they are. Everything below is written at human size —
    /// the figure the hangar turntable shows is 1.86m to the crown — and then blown up by
    /// this one number, which the renderer reads as well, so what you can hit is exactly
    /// what you can see at any scale at all.
    ///
    /// Two, currently. They are not people any more at that size, and that is the point:
    /// against a forty-metre skyline and a tank you are sitting inside, a human silhouette
    /// crossing the frame at thirty metres a second is three pixels and a rumour.
    /// </summary>
    public const float Scale = 2f;

    /// <summary>Planar hitbox. Small enough that hitting one mid-swing is a real shot and
    /// not a formality — it is a body, not a hull.</summary>
    public const float Radius = 0.8f * Scale;

    /// <summary>How tall they stand, how high up that the chest sits, and how far off that
    /// a round has to arrive to bite. The band is generous enough to forgive a fast round's
    /// per-tick step.</summary>
    public const float BodyHeight = 1.86f * Scale;
    public const float AimHeight = 1.05f * Scale;
    public const float HitVertical = 1.5f * Scale;

    /// <summary>Where the cables actually leave the body: the harness, at belt height, the
    /// player's own rig's attachment carried up to their size. Shared by the constraint
    /// solver and the renderer, so the line you see is the line they hang from.</summary>
    public const float HarnessHeight = SoldierRig.ShoulderHeight * Scale;

    /// <summary>What one is worth in shots. Two of the player's rounds — a person out here
    /// has no hull, and the entire threat is that they are hard to <em>hit</em>.</summary>
    public const float BaseShield = 2f;
    public const float LeaderShield = 3f;

    // Gravity, in the same three bands the player's rig uses and for the same reason:
    // heavy on the rise so a launch decelerates crisply, nearly weightless through the
    // apex so there is a beat to pick the next anchor out of, honest on the way down so
    // height turns into speed at the rate it should.
    private const float RiseGravity = 20.8f;
    private const float HangGravity = 5.2f;
    private const float HangBand = 3.2f;

    /// <summary>Hard ceiling on the whole velocity vector — a long reeled dive would
    /// otherwise stack cable pull on gravity for ever.</summary>
    private const float MaxSpeed = 34f;

    /// <summary>How fast they walk when they have been put on the ground. Deliberately
    /// slow: being on the grid is meant to read as the wrong state for them too.</summary>
    private const float GroundSpeed = 7f * Scale * 0.75f;
    private const float GroundAccel = 30f;

    // The cables. The reach, the pay-out speed and what makes an anchor untrustworthy are
    // the player's own numbers.
    private const float ReelRate = 15f;
    private const float ReelThrust = 30f;
    private const float MinLength = 4f * Scale;
    private const float CableRestitution = 0.02f;
    private const int ConstraintPasses = 4;

    /// <summary>How hard the gas jets push in free air. Enough to shape an arc and correct
    /// a line, nowhere near enough to fly — a soldier with no cable out is falling, exactly
    /// as the player is.</summary>
    private const float BoostAccel = 13f;
    private const float BoostCap = 13f;

    // The bottle. Reeling is the expensive thing, bursts are cheap, and coasting pays it
    // back — so a squad that has been hauling itself around for thirty seconds is visibly
    // running out of altitude by the end of it.
    private const float MaxGas = 100f;
    private const float ReelGas = 16f;
    private const float BoostGas = 7f;
    private const float GasRegen = 13f;

    /// <summary>Gas floor under which the reel and the jets simply stop answering. They
    /// glide, they land, and they run — which is the window.</summary>
    private const float DryGas = 4f;

    /// <summary>What it costs to kick off a wall, and how hard that kick is.</summary>
    private const float LaunchGas = 12f;
    private const float LaunchSpeed = 13f;

    // --- Engagement geometry --------------------------------------------------------

    /// <summary>The ring they work at while pressing: far enough out that the arcs are
    /// wide and readable, close enough that the rifles reach.</summary>
    public const float EngageRange = 32f;

    /// <summary>
    /// How high above the target they try to stay while circling. Height is their whole
    /// advantage and they do not give it up for free — but it is deliberately not as much
    /// height as they could hold, and the reason is the TANK.
    ///
    /// A flat cannon bolt rides at barrel height and always will, so an enemy that orbited
    /// at twenty metres would be a fight the heaviest chassis in the game simply cannot
    /// take part in. At this rise, with the weave the orbit carries, they pass through the
    /// band a lobbed mortar covers and their runs come the rest of the way down to meet the
    /// gun. Being hard to hit is the point of them; being impossible to hit is a bug with a
    /// design document.
    /// </summary>
    private const float EngageRise = 9f;

    /// <summary>How far a rifle will be fired at all.</summary>
    private const float RifleRange = 62f;

    /// <summary>Seconds between rounds. The leader is quicker, which is most of why the
    /// leader is the one to shoot first.</summary>
    private const float FireInterval = 1.05f;
    private const float LeaderFireInterval = 0.75f;

    /// <summary>How wide they shoot, in radians, at rest and at full speed. Aim falls
    /// apart on a committed swing — which is what makes a soldier who has slowed down to
    /// line one up the more frightening of the two.</summary>
    private const float SpreadStill = 0.012f;
    private const float SpreadFlying = 0.075f;

    /// <summary>How close a pass has to be to open someone up, and how fast they have to
    /// be going for it to count. Both read by the world, which owns hurting the player.</summary>
    public const float BladeReach = 1.7f * Scale;
    public const float BladeSpeed = 11f;

    /// <summary>How long after a strike before they can land another. Long enough that
    /// four soldiers in a knife fight is a rhythm rather than a blender.</summary>
    private const float BladeCooldownTime = 2.4f;

    // --- Never standing still ---------------------------------------------------------
    //
    // The single most important rule this enemy has, and the one thing a cable makes very
    // easy to get wrong: a pendulum that has run out of swing hangs. A soldier hanging in
    // the air taking pot-shots is a target, not a threat — it is the tank's fight, fought
    // by something that had every advantage and threw it away. So loitering is not merely
    // discouraged by the tuning, it is forbidden outright: hang about below walking pace
    // for longer than the grace and whatever line they are on is dropped on the spot and
    // they go and find another one. What the player is shooting at is always crossing.

    /// <summary>Below this planar speed they are not, for these purposes, moving.</summary>
    private const float LoiterSpeed = 6.5f;

    /// <summary>How long they are allowed to be like that before the line goes. Short
    /// enough that it never reads as hovering, long enough to leave the top of an arc — the
    /// moment a swing legitimately trades all its speed for height — alone.</summary>
    private const float LoiterGrace = 0.55f;

    /// <summary>The speed they work to keep while circling. Above it they coast and let the
    /// arc carry them; below it they are spending gas to get it back.</summary>
    private const float CruiseSpeed = 17f;

    /// <summary>How hard the burst that breaks a dead swing throws them. Enough to be
    /// unmistakably moving on the tick it fires.</summary>
    private const float BreakoutSpeed = 10f;

    /// <summary>How long a soldier with an empty bottle falls before they are allowed to
    /// reach for the city again — long enough that what they catch the next wall with is
    /// speed rather than another dead hang.</summary>
    private const float DryFallTime = 0.45f;

    // --- Clocks ---------------------------------------------------------------------

    private float _fireCooldown;
    private float _blade;
    private float _hookCooldown;
    private float _stateAge;
    private float _perchWait;

    /// <summary>Seconds spent hanging around in the air going nowhere. The one thing this
    /// enemy is never allowed to do — see <see cref="LoiterGrace"/>.</summary>
    private float _stalled;

    /// <summary>Where the current plan says to be. Held between ticks only so the renderer
    /// and the tests can see what a soldier thinks it is doing.</summary>
    public Vector3 Wish { get; private set; }

    /// <summary>The structure they are clung to while perched, so the perch dies with the
    /// building the way an anchor does.</summary>
    private Structure? _perch;

    // --- Client puppet --------------------------------------------------------
    // A client is shown the squad, not simulating it. A puppet carries the host's transform,
    // movement state and bank for the flight pose; its cables and AI never run.

    /// <summary>True on a client's render-only copy of a soldier.</summary>
    public bool IsPuppet { get; private set; }

    /// <summary>Makes a render-only soldier for a client to place a host's squad member.</summary>
    public static EnemySoldier Puppet(Vector2 at, float height, bool leader, int slot)
        => new(at, height, leader, slot) { IsPuppet = true };

    /// <summary>Client-side: adopt the host's account of this soldier this snapshot. Sets only
    /// what the renderer reads; no AI, no cables, no Random.</summary>
    public void NetSet(Vector2 pos, float height, float heading, SoldierMove move,
        float bank, float speed, bool allied, bool alive)
    {
        // Transform is eased, not snapped: store it as the target and let the client's step
        // glide the drawn soldier onto it, so a squad that updates twenty times a second still
        // flies smoothly. The first sighting snaps so a new member appears where it is.
        if (_glide.Report(pos)) { Position = pos; Height = height; Heading = heading; }
        _netHeight = height; _netHeading = heading;
        // The rest is display state the renderer reads outright — no in-between to interpolate.
        Move = move;
        Bank = bank;
        Allied = allied;
        Velocity = new Vector3(MathF.Sin(heading) * speed, 0f, MathF.Cos(heading) * speed);
        Shield = alive ? MathF.Max(Shield, BaseShield) : 0f;
    }

    /// <summary>Scratch flag for the client's adopt sweep, matched on <see cref="Slot"/>: set on
    /// every squad member the latest packet named so the rest can be dropped.</summary>
    public bool NetSeen;

    private NetGlide _glide;
    private float _netHeight, _netHeading;

    /// <summary>Client-side: eases this soldier one frame toward its last reported transform,
    /// which coasts through a missed packet rather than standing still — see <see cref="NetGlide"/>.</summary>
    public void EaseToNet(float k, float dt)
    {
        if (!_glide.Has) return;
        _glide.Coast(dt);
        Position = Torus.Wrap(Position + Torus.Delta(Position, _glide.Target) * k);
        Height += (_netHeight - Height) * k;
        Heading += MathF.IEEERemainder(_netHeading - Heading, MathF.Tau) * k;
    }

    public EnemySoldier(Vector2 at, float height, bool leader, int slot)
    {
        Position = Torus.Wrap(at);
        Height = MathF.Max(0f, height);
        IsLeader = leader;
        Slot = slot;
        Shield = leader ? LeaderShield : BaseShield;
        Shield0 = Shield;

        // Desync every clock off the slot so four of them never breathe, shoot or think
        // in lockstep — the single cheapest thing that stops a squad reading as one entity
        // drawn four times.
        _fireCooldown = FireInterval * (0.3f + 0.25f * slot);
        _perchWait = 1.2f + 0.6f * slot + Random.Shared.NextSingle();
        Move = height > 1f ? SoldierMove.Perched : SoldierMove.Running;
    }

    /// <summary>
    /// Hangs a freshly-raised soldier off a wall: one short cable already bitten in, the
    /// body facing out over the city, weight on the boots. This is how a squad arrives —
    /// not dropped into the air and not walking in, but already standing on the side of a
    /// tower, which is the entire first impression the enemy gets to make.
    /// </summary>
    public void SeedPerch(Vector2 anchor, float anchorY, Structure? holding, float facing)
    {
        Right.State = HookState.Anchored;
        Right.Tip = Torus.Wrap(anchor);
        Right.TipY = anchorY;
        Right.Holding = holding;
        Right.Dir = new Vector3(0f, 1f, 0f);
        Right.Flown = 0f;
        Right.Reach = 0f;
        Right.Bites = true;
        Right.Length = MathF.Max(2f, ToAnchor(Right).Length());

        _perch = holding;
        Heading = facing;
        Velocity = Vector3.Zero;
        Move = SoldierMove.Perched;
    }

    public void TakeDamage(float amount) => TakeDamage(amount, null);

    /// <summary>
    /// Takes a hit. <paramref name="from"/> is where it came from, if the caller knows —
    /// which is the only thing the body needs in order to flinch away from it rather than
    /// merely flinch.
    ///
    /// The direction is recorded rather than acted on: nothing about the simulation changes
    /// because a round arrived from the left. What changes is that the drawn figure snaps
    /// its head away from the left, and a hit that reads as having come from somewhere is
    /// worth more than any amount of shake that does not.
    /// </summary>
    public void TakeDamage(float amount, Vector2? from)
    {
        Shield -= amount;

        if (from is { } at)
        {
            Vector2 d = Torus.Delta(Position, at);
            if (d.LengthSquared() > 1e-6f) FlinchAngle = MathF.Atan2(d.X, d.Y);
        }
        FlinchAmount = Math.Clamp(amount / BaseShield, 0.25f, 1f);
        FlinchSeq++;

        // Being hit at all breaks a perch: nobody hangs still on a wall once a round has
        // gone past their head. They drop off it and start flying, which makes shooting at
        // a perched soldier and missing an actively bad idea.
        if (Move == SoldierMove.Perched && Alive) LeavePerch(0.5f);
    }

    /// <summary>
    /// The last hit, for the figure to react to: which way it came from in world radians
    /// (the heading convention — 0 is +Z), how hard, and a counter that ticks once per hit.
    ///
    /// The counter is the part that matters. A renderer running faster than the simulation
    /// would otherwise replay the same flinch every frame it saw the angle sitting there,
    /// and one running slower would miss hits entirely; a sequence number it can compare
    /// against the last one it acted on is right at any pair of rates.
    /// </summary>
    public float FlinchAngle { get; private set; }
    public float FlinchAmount { get; private set; }
    public int FlinchSeq { get; private set; }

    /// <summary>Ticks once per blade pass, on the same principle: the figure swings when
    /// this changes, not while it is nonzero.</summary>
    public int SlashSeq { get; private set; }

    /// <summary>And once per round out of the rifle, so the shoulder takes each shot
    /// exactly once however fast the machine drawing them is running.</summary>
    public int ShotSeq { get; private set; }

    /// <summary>
    /// Test hatch: puts them on a committed run this instant, without waiting for a squad
    /// to hand them the turn. The headless checks need a run they can point at a known
    /// place at a known moment, and there is no way to ask for one through the ordinary
    /// path — the rota decides, and it decides on its own clock.
    /// </summary>
    public void CommitRunForTest()
    {
        Order = SoldierOrder.Strike;
        Enter(SoldierMove.Diving);
    }

    /// <summary>Bills a blade pass — the world scores the hit, this only spends it.</summary>
    public void RegisterSlash()
    {
        _blade = BladeCooldownTime;
        SlashSeq++;
        // Off the back of a connected pass they break away rather than grinding on the
        // spot. A slash is a fly-past, not a melee.
        Enter(SoldierMove.Breaking);
    }

    /// <summary>
    /// Tears an anchor out — what the world calls when the building a hook is holding is
    /// cut out from under it, and what the rig calls on itself when weak stone gives way.
    ///
    /// Losing a line to the city is not the same as choosing to let one go, and the
    /// difference is in the last line of this method: the throw clock is wiped, so the
    /// replacement cable leaves on the very next tick they have a free launcher. That
    /// reflex is most of what separates these from something that simply falls when its
    /// rope is cut — shooting the wall out from under one buys the second it takes them to
    /// find the next wall, and not a second more.
    /// </summary>
    public void TearAnchor(GrappleHook h)
    {
        if (!h.Anchored) return;
        h.State = HookState.Returning;
        h.Holding = null;
        h.Taut = false;
        h.Tension = 0f;
        h.Load = 0f;
        h.JustTore = true;
        _hookCooldown = 0f;
        if (Move == SoldierMove.Perched) LeavePerch(0f);
    }

    /// <summary>
    /// Registers meeting a wall. Fast enough and it is a crash: everything they had built
    /// is gone, they are staggered, and for a second and a half they are a person hanging
    /// off a rope doing nothing — the best moment the player will get to shoot one.
    /// </summary>
    /// <param name="outward">Unit vector out of the wall, along the way the world just
    /// pushed them.</param>
    public void RegisterWallHit(Vector2 outward)
    {
        if (PlanarSpeed >= CrashSpeed)
        {
            Velocity = new Vector3(0f, MathF.Min(Velocity.Y, 0f), 0f);
            Stagger = MathF.Max(Stagger, CrashStagger);
            if (Move != SoldierMove.Running) Enter(SoldierMove.Breaking);
            return;
        }

        // A scrape, and it has to be a real one — take away only what was travelling *into*
        // the wall and leave everything running along it whole.
        //
        // Damping the whole planar vector instead, which is the cheap version of this, has
        // a failure mode that is very nearly fatal to the entire class: a soldier whose next
        // anchor is on the far side of a tower gets pushed out of it every tick, loses most
        // of their speed every tick, and slides down the face of the building going nowhere
        // at all — which is exactly the hovering, stationary target this enemy must never
        // be. Sliding along the stone is what a person in this kit actually does, and it is
        // also the only response that lets them get around the thing.
        var planar = new Vector2(Velocity.X, Velocity.Z);
        float into = Vector2.Dot(planar, -outward);
        if (into > 0f) planar += outward * into;
        planar *= WallFriction;

        Velocity.X = planar.X;
        Velocity.Z = planar.Y;
    }

    /// <summary>What a scrape along a wall costs the speed running down it. Nearly nothing:
    /// the loss that matters is the direction, not the momentum.</summary>
    private const float WallFriction = 0.94f;

    /// <summary>Planar speed above which meeting a wall is a crash rather than a scrape —
    /// the player's own threshold, since it is the same body hitting the same stone.</summary>
    private const float CrashSpeed = SoldierRig.CrashSpeed;

    /// <summary>And how long they hang there afterwards with nothing to give. The single
    /// longest window the player gets on one of these, and the reward for shooting the
    /// building out from under a swing rather than shooting at the swing.</summary>
    private const float CrashStagger = 1.4f;

    /// <summary>
    /// One fixed step: think, work the cables, fly, then look where the work went. The
    /// order matters — the plan is made before the hooks are thrown so a cable is always
    /// fired at somewhere the soldier has already decided to be.
    ///
    /// <paramref name="targetPos"/> is whoever this soldier is currently working against,
    /// which for most of them is the player and for a carrier is whichever of their own side
    /// is nearest. The brain below never asks which — it flies at what it is given, and
    /// pointing it somewhere else is the whole of turning one.
    /// </summary>
    public void Update(float dt, Vector2 targetPos, float targetHeight, IAnchorField city,
        bool starved = false, bool holdFire = false)
    {
        _holdFire = holdFire;
        JustFired = false;
        JustLaunched = false;
        JustLanded = false;
        LandingSpeed = 0f;
        JustTurned = false;
        Left.ClearEvents();
        Right.ClearEvents();

        StepCorruption(dt, starved);

        if (Stagger > 0f) Stagger = MathF.Max(0f, Stagger - dt);
        if (_blade > 0f) _blade -= dt;
        if (_hookCooldown > 0f) _hookCooldown -= dt;
        if (_fireCooldown > 0f) _fireCooldown -= dt;
        _stateAge += dt;

        StepHook(Left, dt);
        StepHook(Right, dt);

        Vector2 toTarget = Torus.Delta(Position, targetPos);
        float range = toTarget.Length();

        Think(dt, toTarget, range, targetHeight);

        if (Move == SoldierMove.Perched)
        {
            // A perch is a held position, not a physics state: no gravity, no drift, and
            // the cable is a formality holding them to the wall. Everything else about
            // them still runs — they watch, they aim and they shoot from up there.
            Velocity = Vector3.Zero;
            Refuel(dt, spending: false);
        }
        else
        {
            WorkTheCables(dt, city);
            Fly(dt);

            for (int i = 0; i < ConstraintPasses; i++)
            {
                SolveCable(Left, dt, i == 0);
                SolveCable(Right, dt, i == 0);
            }

            Integrate(dt);

            // And the rule: airborne and going nowhere is a state with a timer on it.
            // Counted after the physics rather than before, so what it measures is where
            // this tick actually left them.
            if (Height > 1f && PlanarSpeed < LoiterSpeed) _stalled += dt;
            else _stalled = 0f;
        }

        Aim(dt, toTarget, range, targetHeight);
    }

    // --- The brain -----------------------------------------------------------------

    /// <summary>
    /// Picks the state and, with it, the one number the rest of the step is built on:
    /// <see cref="Wish"/>, the place in the air this soldier is trying to be. Everything
    /// downstream — which tower to bite, when to reel, when to let go — is derived from
    /// wanting to get there, which is what keeps the flying looking like intent rather
    /// than like a list of behaviours taking turns.
    /// </summary>
    private void Think(float dt, Vector2 toTarget, float range, float targetHeight)
    {
        // The ring position this member works: their bearing off the target at the
        // engagement radius. The squad rotates the whole ring, so holding a bearing is
        // itself an orbit.
        Vector2 ring = new Vector2(MathF.Sin(Bearing), MathF.Cos(Bearing)) * EngageRange;
        Vector2 targetXZ = Position + toTarget;

        switch (Move)
        {
            case SoldierMove.Perched:
            {
                _perchWait -= dt;
                // A perch ends when the squad calls, when the target has come close enough
                // that sitting on a wall is suicide, or when they have simply waited long
                // enough to want a better vantage.
                if (Order == SoldierOrder.Strike
                    || (Order != SoldierOrder.Hold && _perchWait <= 0f)
                    || range < EngageRange * 0.6f)
                {
                    LeavePerch(LaunchSpeed);
                }
                Wish = new Vector3(Position.X, Height, Position.Y);
                return;
            }

            case SoldierMove.Running:
            {
                // On the grid with nothing overhead to hang from. They close on foot, which
                // is slow and honest, and take the first anchor that offers itself.
                Wish = new Vector3(targetXZ.X, targetHeight + EngageRise, targetXZ.Y);
                // Off the ground with anything left in the bottle and they are flying again.
                // Held deliberately low: time spent on the grid is time spent being shot at,
                // and the whole of their competence is how briefly they stay there.
                if (Height > 1.2f && Gas > DryGas * 2f) Enter(SoldierMove.Closing);
                return;
            }

            case SoldierMove.Closing:
            {
                Vector2 at = targetXZ + ring;
                Wish = new Vector3(at.X, targetHeight + EngageRise + 4f, at.Y);

                if (Order == SoldierOrder.Strike) { Enter(SoldierMove.Diving); return; }
                // Arrived on station: start working the arc instead of crossing ground.
                if (range < EngageRange * 1.5f) Enter(SoldierMove.Circling);
                return;
            }

            case SoldierMove.Circling:
            {
                Vector2 at = targetXZ + ring;
                // Ride a slow rise and fall through the orbit so the ring is a helix rather
                // than a carousel — four figures all at one height reads as a menu.
                float weave = MathF.Sin(_stateAge * 0.9f + Slot * 1.7f) * 5f;
                Wish = new Vector3(at.X, targetHeight + EngageRise + weave, at.Y);

                if (Order == SoldierOrder.Strike) { Enter(SoldierMove.Diving); return; }
                if (Order == SoldierOrder.Hold && _stateAge > 4f) Enter(SoldierMove.Breaking);
                return;
            }

            case SoldierMove.Diving:
            {
                // Straight at them, and never lower than a body's height off the grid. The
                // floor matters more than it looks: a run aimed at the feet of something
                // standing on the floor ends with the soldier <em>on</em> the floor, which
                // is the one state they are catchable in and not one they should be walking
                // into four times a minute. They pass over, blades down.
                //
                // No lead, either: a run is committed the moment it starts, so a target that
                // moves after they have gone is a target they miss — the dodge is real and
                // it is the player's to take.
                Wish = new Vector3(targetXZ.X,
                    MathF.Max(targetHeight + 1.2f, BodyHeight * 0.9f), targetXZ.Y);

                // The run has a shelf life. Past it, whether or not the blades found
                // anything, they break off — a soldier who kept diving would just be a
                // homing missile with legs.
                if (_stateAge > 2.6f || Order != SoldierOrder.Strike || _blade > 0f)
                    Enter(SoldierMove.Breaking);
                return;
            }

            default: // Breaking
            {
                // Away and up, on the far side of their own bearing, gathering height for
                // the next arc. Retreating <em>upward</em> is the whole grammar of this
                // enemy: they do not run away, they climb out of reach.
                Vector2 at = targetXZ + ring * 1.7f;
                Wish = new Vector3(at.X, targetHeight + EngageRise + 14f, at.Y);

                if (_stateAge > 2.2f)
                    Enter(Order == SoldierOrder.Hold ? SoldierMove.Perched : SoldierMove.Circling);
                // Breaking into a perch only actually lands if there is a wall to land on;
                // TryPerch below is what makes it real, and it quietly fails otherwise.
                if (Move == SoldierMove.Perched && !TryPerch()) Enter(SoldierMove.Circling);
                return;
            }
        }
    }

    private void Enter(SoldierMove move)
    {
        if (Move == move) return;
        Move = move;
        _stateAge = 0f;
    }

    /// <summary>
    /// Drops off a wall with a shove. The kick is what makes a launch read as a person
    /// pushing off stone rather than as a model starting to move: they leave along their
    /// own facing with a hard upward component, and the cable that was holding them goes
    /// on the same tick.
    /// </summary>
    private void LeavePerch(float speed)
    {
        _perch = null;
        Release(Left);
        Release(Right);
        Enter(SoldierMove.Closing);

        if (speed <= 0f) return;
        var fwd = new Vector2(MathF.Sin(Heading), MathF.Cos(Heading));
        Velocity = new Vector3(fwd.X * speed * 0.7f, speed * 0.55f, fwd.Y * speed * 0.7f);
        Gas = MathF.Max(0f, Gas - LaunchGas);
        JustLaunched = true;
    }

    /// <summary>
    /// Turns a break into a perch if there is genuinely something to cling to: a short,
    /// taut cable and no speed left. Returns false when they are hanging in open air,
    /// where a "perch" would be a person standing on nothing.
    /// </summary>
    private bool TryPerch()
    {
        GrappleHook? h = Left.Anchored ? Left : Right.Anchored ? Right : null;
        if (h is null || h.Holding is null) return false;
        if (Velocity.Length() > 9f) return false;

        Vector3 to = ToAnchor(h);
        if (to.Length() > 9f) return false;

        _perch = h.Holding;
        Velocity = Vector3.Zero;
        h.Length = MathF.Max(2f, to.Length());
        _perchWait = 2.2f + Random.Shared.NextSingle() * 3f;
        Move = SoldierMove.Perched;
        _stateAge = 0f;
        return true;
    }

    // --- The cables -----------------------------------------------------------------

    /// <summary>
    /// The professional half. Three decisions, made every tick, and between them they are
    /// the whole reason this thing does not fly like a drone:
    ///
    /// <list type="number">
    /// <item>An anchor is thrown <em>ahead</em> of where the plan wants to go and above it,
    /// because a cable behind you brakes and a cable in front of you accelerates.</item>
    /// <item>The reel is pulled only while the anchor is still ahead — hauling on a line
    /// you have already passed is winching yourself backwards.</item>
    /// <item>And it is let go the moment the anchor falls behind and there is speed on the
    /// clock. Nothing is applied on release; the rope simply stops being there and every
    /// bit of what the arc built stays built. That is the slingshot, and chaining it is
    /// the difference between a soldier and a pendulum.</item>
    /// </list>
    /// </summary>
    private void WorkTheCables(float dt, IAnchorField city)
    {
        var self = new Vector3(Position.X, Height + HarnessHeight, Position.Y);
        Vector3 toWish = Wish - self;
        Vector3 wishDir = toWish.LengthSquared() > 1e-4f
            ? Vector3.Normalize(toWish)
            : new Vector3(MathF.Sin(Heading), 0f, MathF.Cos(Heading));

        // What a cable is asked to do is never quite what the body is asked to do: the
        // anchor wants to be up the slope of the travel, so the swing carries them along
        // it rather than merely tethering them near it.
        Vector3 swing = Vector3.Normalize(wishDir + new Vector3(0f, 0.75f, 0f));

        // Loitering. Whatever they are hanging from has stopped being an arc and started
        // being a rope, so it goes — all of it, at once — and the throw clock goes with it.
        // They will be falling on the next tick and hunting a fresh line on the one after,
        // which is the correct answer to having run out of swing and is also, not
        // incidentally, the thing that makes them impossible to line up.
        if (_stalled > LoiterGrace)
        {
            Release(Left);
            Release(Right);
            _hookCooldown = 0f;
            _stalled = 0f;

            // And a hard burst off the bottle to break out sideways, rather than simply
            // dropping the rope and hoping. Dropping alone leaves them falling in a straight
            // line for the second it takes a new hook to fly, which is not hanging but is
            // near enough to it to be shot: what gets a person out of a dead swing is the
            // gas, and it costs them the same as a launch off a wall does.
            if (Gas > LaunchGas)
            {
                var kick = new Vector2(wishDir.X, wishDir.Z);
                if (kick.LengthSquared() < 1e-4f)
                    kick = new Vector2(MathF.Sin(Heading), MathF.Cos(Heading));
                kick = Vector2.Normalize(kick);

                Velocity.X += kick.X * BreakoutSpeed;
                Velocity.Z += kick.Y * BreakoutSpeed;
                Velocity.Y += BreakoutSpeed * 0.35f;
                Gas -= LaunchGas;
                JustLaunched = true;
            }
            else
            {
                // Nothing left in the bottle to break out with. Then they drop, and they are
                // made to drop properly: no new line for a moment, so the height goes into
                // speed instead of into another dead hang off the next wall along. Out of
                // gas is meant to be the state the player can catch them in — falling and
                // committed is honest, hanging still on a rope is not.
                _hookCooldown = DryFallTime;
            }
        }

        bool anchored = false;
        bool pulling = false;
        // Both hooks, named rather than iterated: this runs sixty times a second for every
        // soldier on the field, and an enumerator built to walk two fields is two fields'
        // worth of garbage a tick each.
        WorkOne(Left, dt, ref anchored, ref pulling);
        WorkOne(Right, dt, ref anchored, ref pulling);

        Refuel(dt, spending: pulling);

        // Nothing out and nothing coming: find the next line. The cooldown is what keeps a
        // soldier over open grid from machine-gunning hooks at a city that isn't there.
        if (_hookCooldown > 0f || Stagger > 0f) return;

        bool spareLeft = Left.State == HookState.Stowed;
        bool spareRight = Right.State == HookState.Stowed;
        if (!spareLeft && !spareRight) return;

        // A second line goes out on a committed run and only there: bracketing the arc
        // between two anchors is what lets them turn hard enough to actually arrive at
        // something that is trying not to be arrived at.
        if (anchored && Move != SoldierMove.Diving) return;

        if (!city.TryFindSwing(self, swing, SoldierRig.MaxRange,
                out Vector3 point, out Structure? holding))
        {
            _hookCooldown = 0.35f;
            return;
        }

        Throw(spareLeft ? Left : Right, self, point, holding);
        _hookCooldown = 0.5f;
    }

    /// <summary>One anchored cable's worth of the decisions above: keep it, reel it, pay it
    /// out, or let it go.</summary>
    private void WorkOne(GrappleHook h, float dt, ref bool anchored, ref bool pulling)
    {
        if (!h.Anchored) return;
        anchored = true;

        Vector3 to = ToAnchor(h);
        float dist = to.Length();
        if (dist < 1e-3f) { Release(h); return; }

        Vector3 lineDir = to / dist;
        float speed = Velocity.Length();
        float ahead = speed > 0.5f ? Vector3.Dot(Velocity / speed, lineDir) : 1f;

        // Reeled into the anchor's face, or hanging off one they have swung well past:
        // both are dead cable, and a pro carries neither.
        if (dist < MinLength + 1f) { Release(h); return; }
        if (ahead < -0.25f && speed > 9f) { Release(h); return; }

        // The reel. It is both a shortening and a genuine thrust up the line — a winch
        // alone drags a body along a rope like a lift, and what makes the pull worth
        // spending gas on is that it is an acceleration they get to keep. A soldier still
        // on the grid always pulls, whatever the angle says: that haul is how they get off
        // it, and it is the only thing standing between them and walking the whole way.
        bool wantSpeed = Move is SoldierMove.Diving or SoldierMove.Closing || speed < CruiseSpeed;
        if (wantSpeed && (ahead > 0.1f || Height < 1.5f) && Gas > DryGas)
        {
            h.Length = MathF.Max(MinLength, h.Length - ReelRate * dt);
            Velocity += lineDir * (ReelThrust * dt);
            Gas = MathF.Max(0f, Gas - ReelGas * dt);
            pulling = true;
        }
        else if (!wantSpeed && dist > h.Length + 0.5f)
        {
            // Paying out costs nothing and widens the arc — the cheap way to cross ground
            // when there is no hurry.
            h.Length = MathF.Min(SoldierRig.MaxRange, h.Length + 6f * dt);
        }
    }

    /// <summary>Puts a hook in the air. Mirrors <see cref="SoldierRig.FireHook"/> — the
    /// cable pays out over the flight, so an anchor is somewhere they arrive at rather than
    /// somewhere they snap to.</summary>
    private void Throw(GrappleHook h, Vector3 from, Vector3 at, Structure? holding)
    {
        Vector3 line = at - from;
        float reach = line.Length();
        if (reach < 1e-3f) return;

        h.State = HookState.Flying;
        h.Tip = new Vector2(from.X, from.Z);
        h.TipY = from.Y;
        h.Dir = line / reach;
        h.Flown = 0f;
        h.Reach = MathF.Min(SoldierRig.MaxRange, reach);
        h.Bites = true;
        h.Holding = holding;
        h.Load = 0f;
        h.Taut = false;
        h.Tension = 0f;
    }

    private void Release(GrappleHook h)
    {
        if (h.State is HookState.Stowed or HookState.Returning) return;
        h.State = HookState.Returning;
        h.Holding = null;
        h.Taut = false;
        h.Tension = 0f;
        h.Load = 0f;
        h.JustReleased = true;
    }

    /// <summary>
    /// Advances one hook. A straight mirror of the player's rig, including the two ways an
    /// anchor dies on its own: the building coming down under it, and weak material giving
    /// way after <see cref="SoldierRig.TearTime"/> under load. They read the city better
    /// than a player does — the anchor query prefers solid stone — but the smallest towers
    /// still let go, and watching one drop out of the sky because it trusted a spire is
    /// worth every line of this.
    /// </summary>
    private void StepHook(GrappleHook h, float dt)
    {
        switch (h.State)
        {
            case HookState.Flying:
            {
                float step = SoldierRig.CableSpeed * dt;
                h.Flown += step;
                h.Tip += new Vector2(h.Dir.X, h.Dir.Z) * step;
                h.TipY += h.Dir.Y * step;
                if (h.Flown < h.Reach) return;

                h.Tip = Torus.Wrap(h.Tip);
                h.State = HookState.Anchored;
                h.Length = MathF.Max(MinLength, ToAnchor(h).Length());
                h.HoldingVersion = h.Holding?.Fracture?.Version ?? 0;
                h.JustBit = true;

                // Bitten off the ground: the bottle goes at the same instant. This one
                // beat is most of what makes a soldier caught on the grid frightening
                // rather than free — you have a second and a half to take the shot, and
                // then the hook lands somewhere and they are gone.
                if (Move == SoldierMove.Running && Gas > LaunchGas)
                {
                    Velocity.Y = MathF.Max(Velocity.Y, 9f);
                    Gas -= LaunchGas;
                    JustLaunched = true;
                    Enter(SoldierMove.Closing);
                }
                return;
            }

            case HookState.Returning:
                h.Flown -= 150f * dt;
                if (h.Flown <= 0f) h.Stow();
                return;

            case HookState.Anchored:
                // Three ways an anchor stops being one, and all three are the building's
                // doing rather than the soldier's. It is coming down; it is gone entirely;
                // or — the one that matters most now that towers come apart where they are
                // hit rather than toppling whole — somebody has cut a piece out of it since
                // the hook went in, and the hook has no way of knowing it wasn't that piece.
                // A shot into the wall a soldier is hanging from drops them off it, which is
                // by some distance the most satisfying way to deal with one.
                if (h.Holding is { } held
                    && (held.Falling || held.Gone
                        || (held.Fracture?.Version ?? 0) != h.HoldingVersion))
                {
                    TearAnchor(h);
                    return;
                }
                if (h.Taut && h.Holding is { } s && s.Scale < SoldierRig.WeakScale)
                {
                    h.Load += dt;
                    if (h.Load >= SoldierRig.TearTime) TearAnchor(h);
                }
                else if (!h.Taut)
                {
                    h.Load = MathF.Max(0f, h.Load - dt * 0.5f);
                }
                return;
        }
    }

    // --- Flight ---------------------------------------------------------------------

    /// <summary>
    /// Gravity, then the jets. The jets are deliberately feeble: they bend a line and
    /// nothing more, so a soldier with both hooks stowed is falling exactly as the player
    /// would be. Everything that looks like flying out there is a cable doing it.
    /// </summary>
    private void Fly(float dt)
    {
        float g = Velocity.Y > 0f
            ? RiseGravity
            : (Velocity.Y > -HangBand ? HangGravity : Gravity);
        Velocity.Y -= g * dt;

        if (Height <= 0f)
        {
            // On the grid: walk. Slowly.
            var toward = new Vector2(Wish.X - Position.X, Wish.Z - Position.Y);
            if (toward.LengthSquared() > 1e-4f)
            {
                Vector2 want = Vector2.Normalize(toward) * GroundSpeed;
                var planar = new Vector2(Velocity.X, Velocity.Z);
                planar = MoveToward(planar, want, GroundAccel * dt);
                Velocity.X = planar.X;
                Velocity.Z = planar.Y;
            }
            return;
        }

        if (Gas <= DryGas || Stagger > 0f) return;

        var self = new Vector3(Position.X, Height, Position.Y);
        Vector3 toWish = Wish - self;
        if (toWish.LengthSquared() < 1e-4f) return;

        Vector3 push = Vector3.Normalize(toWish) * (BoostAccel * dt);
        var was = new Vector2(Velocity.X, Velocity.Z);
        Velocity += push;

        // The jets may steer at any speed but may not build speed past their own cap:
        // anything already travelling faster than that only gets to change direction,
        // which is the rule that keeps the cables the only engine on this chassis.
        var now = new Vector2(Velocity.X, Velocity.Z);
        float ceiling = MathF.Max(BoostCap, was.Length());
        if (now.Length() > ceiling)
        {
            now *= ceiling / now.Length();
            Velocity.X = now.X;
            Velocity.Z = now.Y;
        }

        Gas = MathF.Max(0f, Gas - BoostGas * dt);
    }

    /// <summary>The bottle refills whenever it is not being emptied — which is to say
    /// whenever they are coasting on an arc rather than hauling on it.</summary>
    private void Refuel(float dt, bool spending)
    {
        if (spending && Gas > DryGas) return;
        Gas = MathF.Min(MaxGas, Gas + GasRegen * dt);
    }

    /// <summary>
    /// The cable as a hard constraint: slack inside its length and doing nothing at all,
    /// and at its length it takes away exactly the part of the momentum that was trying to
    /// travel further from the anchor while leaving everything sideways whole. That is the
    /// entire pendulum, and it is the same solver the player swings on.
    /// </summary>
    private void SolveCable(GrappleHook h, float dt, bool firstPass)
    {
        if (!h.Anchored) return;
        if (firstPass) { h.Taut = false; h.Tension *= 0.6f; }

        Vector3 to = ToAnchor(h);
        float dist = to.Length();
        if (dist <= h.Length || dist < 1e-4f) return;

        Vector3 n = to / dist;
        float over = dist - h.Length;
        Position = Torus.Wrap(Position + new Vector2(n.X, n.Z) * over);
        Height += n.Y * over;
        if (Height < 0f) Height = 0f;

        float outward = -Vector3.Dot(Velocity, n);
        if (outward > 0f)
        {
            Velocity += n * (outward * (1f + CableRestitution));
            h.Tension = MathF.Max(h.Tension, Math.Clamp(outward / 18f, 0f, 1f));
        }

        h.Taut = true;
        if (h.Tension < 0.12f) h.Tension = 0.12f;
    }

    private void Integrate(float dt)
    {
        // A seeded body drags. This is the entire reason the tag is worth firing at
        // something that moves like this: for those few seconds they are not the target that
        // cannot be caught, and the mote — which is faster than they are anyway — can close.
        if (Tagged > 0f) Velocity *= MathF.Max(0f, 1f - TagDrag * dt);

        float speed = Velocity.Length();
        if (speed > MaxSpeed) Velocity *= MaxSpeed / speed;

        Position = Torus.Wrap(Position + new Vector2(Velocity.X, Velocity.Z) * dt);
        Height += Velocity.Y * dt;

        if (Height > 0f) return;

        Height = 0f;
        if (Move != SoldierMove.Running)
        {
            JustLanded = true;
            LandingSpeed = -Velocity.Y;
            // A bad arrival costs them the same thing it costs the player: everything they
            // were carrying, plus a second on one knee.
            if (LandingSpeed > SoldierRig.HardLanding)
            {
                Stagger = MathF.Max(Stagger, 0.7f);
                Velocity.X *= 0.2f;
                Velocity.Z *= 0.2f;
            }
            Enter(SoldierMove.Running);
        }
        if (Velocity.Y < 0f) Velocity.Y = 0f;
    }

    // --- Looking and shooting --------------------------------------------------------

    /// <summary>
    /// Where they face and what they do about it. Look and travel are decoupled on this
    /// chassis exactly as they are on the player's, which is what lets one of them cross
    /// in front of the player backwards with the rifle still on them.
    /// </summary>
    private void Aim(float dt, Vector2 toTarget, float range, float targetHeight)
    {
        if (range > 0.01f)
        {
            float want = MathF.Atan2(toTarget.X, toTarget.Y);
            // Snapped a good deal harder than a hunter's turret glide: a person's head
            // turns, and half of what makes these read as people is that they are always
            // already looking at you.
            Heading = TurnToward(Heading, want, 5.5f * dt);
        }

        // The lean into the arc, taken from how much of the travel is sideways relative to
        // where they are looking — the same reading the player's own camera bank is.
        var fwd = new Vector2(MathF.Sin(Heading), MathF.Cos(Heading));
        var side = new Vector2(-fwd.Y, fwd.X);
        float lateral = Vector2.Dot(new Vector2(Velocity.X, Velocity.Z), side);
        float wantBank = Move == SoldierMove.Running || Move == SoldierMove.Perched
            ? 0f
            : -Math.Clamp(lateral / 22f, -1f, 1f) * SoldierRig.MaxBank;
        Bank = MoveToward(Bank, wantBank, 2.4f * dt);

        if (_fireCooldown > 0f || Stagger > 0f || _holdFire) return;
        if (Order == SoldierOrder.Hold && Move == SoldierMove.Perched && range > RifleRange * 0.7f) return;
        if (range > RifleRange) return;
        // Nobody shoots through their own run. The blades are the run.
        if (Move == SoldierMove.Diving) return;

        // Onto the target's body centre, the same height a hunter elevates to, so what the
        // world scores a hit at and what they aimed at are one number.
        var muzzle = new Vector3(
            Position.X + fwd.X * 0.5f,
            Height + AimHeight,
            Position.Y + fwd.Y * 0.5f);
        var at = new Vector3(
            Position.X + toTarget.X,
            targetHeight + EnemyTank.AimHeight,
            Position.Y + toTarget.Y);

        Vector3 line = at - muzzle;
        if (line.LengthSquared() < 1e-4f) return;
        Vector3 dir = Vector3.Normalize(line);

        // Aim falls apart with speed. A soldier hanging still on a wall shoots straight;
        // one halfway round an arc at thirty metres a second sprays, which is what makes
        // the fast ones a movement problem and the still ones a shooting problem.
        float spread = SpreadStill
                     + (SpreadFlying - SpreadStill) * Math.Clamp(Velocity.Length() / 24f, 0f, 1f);
        dir = Scatter(dir, spread);

        ShotOrigin = muzzle;
        ShotDir = dir;
        JustFired = true;
        ShotSeq++;
        _fireCooldown = IsLeader ? LeaderFireInterval : FireInterval;
    }

    /// <summary>Nudges a direction off true by up to <paramref name="spread"/> radians, in
    /// a random plane about it.</summary>
    private static Vector3 Scatter(Vector3 dir, float spread)
    {
        if (spread <= 0f) return dir;
        Vector3 side = MathF.Abs(dir.Y) > 0.95f
            ? Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitX))
            : Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY));
        Vector3 up = Vector3.Cross(dir, side);

        float a = Random.Shared.NextSingle() * MathF.Tau;
        float r = MathF.Sqrt(Random.Shared.NextSingle()) * spread;
        return Vector3.Normalize(dir + (side * MathF.Cos(a) + up * MathF.Sin(a)) * r);
    }

    // --- Geometry ---------------------------------------------------------------------

    /// <summary>The vector from the soldier's harness to an anchor, the short way round the
    /// torus on the plane and straight up the Y axis.</summary>
    private Vector3 ToAnchor(GrappleHook h)
    {
        Vector2 planar = Torus.Delta(Position, h.Tip);
        return new Vector3(planar.X, h.TipY - Height - HarnessHeight, planar.Y);
    }

    /// <summary>Honest gravity, shared with the player's rig so height converts to speed at
    /// the same rate for both sides of the fight.</summary>
    private const float Gravity = SoldierRig.Gravity;

    /// <summary>Where the blades and the launchers hang, for the world's blade test and for
    /// the renderer: the middle of the body rather than the boots.</summary>
    public Vector3 Chest => new(Position.X, Height + AimHeight, Position.Y);

    /// <summary>Planar speed — what the lean, the wind and the blade test all read.</summary>
    public float PlanarSpeed => new Vector2(Velocity.X, Velocity.Z).Length();

    /// <summary>True while there is a pass in progress that could still land — the world's
    /// gate on billing a slash.</summary>
    public bool BladeReady => _blade <= 0f;

    /// <summary>The building they are clung to, or null. The world watches it so a perch
    /// dies with the tower under it.</summary>
    public Structure? PerchedOn => Move == SoldierMove.Perched ? _perch : null;

    /// <summary>Knocks a soldier off a wall from outside — what the world calls when the
    /// thing they are standing on is cut down.</summary>
    public void LosePerch() => LeavePerch(3f);

    private static float TurnToward(float current, float target, float maxStep)
    {
        float d = (target - current) % MathF.Tau;
        if (d > MathF.PI) d -= MathF.Tau;
        if (d < -MathF.PI) d += MathF.Tau;
        if (MathF.Abs(d) <= maxStep) return target;
        return current + MathF.Sign(d) * maxStep;
    }

    private static float MoveToward(float value, float target, float maxDelta)
    {
        if (MathF.Abs(target - value) <= maxDelta) return target;
        return value + MathF.Sign(target - value) * maxDelta;
    }

    private static Vector2 MoveToward(Vector2 value, Vector2 target, float maxDelta)
    {
        Vector2 d = target - value;
        float len = d.Length();
        if (len <= maxDelta || len < 1e-5f) return target;
        return value + d / len * maxDelta;
    }
}
