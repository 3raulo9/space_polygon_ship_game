using System.Numerics;
using VoidTanks.Core;

namespace VoidTanks.Entities;

/// <summary>
/// Four soldiers and the one thing they share: a plan.
///
/// The members do their own flying — every cable, every arc and every shot is decided in
/// <see cref="EnemySoldier"/> off that soldier's own state. What the squad owns is
/// smaller and does far more work: <em>which side of the target each of them holds</em>
/// and <em>whose turn it is to go in</em>. Those two decisions are the entire difference
/// between four enemies and a squad.
///
/// The ring is the first of them. Each member is handed a bearing a quarter-turn from the
/// next and the whole ring turns slowly, so the four are always spread around the player
/// and always drifting — a player who backs away from one is backing toward another, and
/// there is never a safe direction, only a temporarily emptier one.
///
/// The rota is the second. Exactly one soldier at a time is told to strike, and the rest
/// are told to keep the pressure up from their bearings. That is what makes the fight
/// legible: there is always one blade coming, you can always see which one it is, and the
/// three you are not currently dealing with are the reason you cannot simply stand and
/// deal with it. A squad worn down to its last member gets no less dangerous — the rota
/// has nobody else to hand the turn to, so the survivor simply comes again, and again.
/// </summary>
public sealed class SoldierSquad
{
    /// <summary>How many arrive. Four is not a difficulty knob — it is the number that
    /// makes a ring read as a ring, and the number the rota needs to keep three sets of
    /// pressure on while one of them is committed.</summary>
    public const int Size = 4;

    public readonly List<EnemySoldier> Members = new(Size);

    /// <summary>True once they have seen the player. Before that they sit on their tower
    /// and the whole squad is scenery.</summary>
    public bool Alerted { get; private set; }

    /// <summary>Set on the single tick the squad goes loud — the world spends it on the
    /// call that carries across the grid.</summary>
    public bool JustCalled { get; private set; }

    /// <summary>Where the ring currently sits, in radians. Turns for as long as the squad
    /// lives.</summary>
    public float Ring { get; private set; }

    public bool Alive => Members.Count > 0;

    /// <summary>The one currently committed to a run, or null. Public so the HUD can mark
    /// the blade that is actually coming — with four figures in the air, knowing which one
    /// to watch is the whole of surviving them.</summary>
    public EnemySoldier? Striker { get; private set; }

    /// <summary>
    /// How far off a soldier the player has to be for the squad to see them. Deliberately
    /// wider than the band a squad is ever raised in, for a plain reason: a squad that
    /// arrived out past its own eyesight would sit on its tower until the player happened
    /// to wander toward it, which is not an enemy, it is scenery with a trigger. They see
    /// you when they arrive. What buys the beat where they are four silhouettes on a spire
    /// is the perch's own wait, not a blindness they never had.
    /// </summary>
    private const float AlertRange = 130f;

    /// <summary>And how far the player has to get for them to lose interest and go back to
    /// watching. Wider than the alert, so a fight doesn't flicker on and off at the edge.</summary>
    private const float LoseRange = 190f;

    /// <summary>
    /// How fast the whole ring turns, in radians a second. At the engagement radius this is
    /// something like eighteen metres a second of sideways travel that every member is
    /// permanently chasing — which is the point. The ring is not a formation they sit in,
    /// it is a formation that is always moving out from under them, so holding a bearing is
    /// itself a reason to keep swinging. A slower ring lets them settle, and settled is the
    /// one thing they are not allowed to be.
    /// </summary>
    private const float RingRate = 0.55f;

    /// <summary>How long one soldier holds the turn before it passes on. Long enough for a
    /// run to be a run: cross the gap, make the pass, break away.</summary>
    private const float StrikeWindow = 3.1f;

    /// <summary>And the breath between runs, when nobody has the turn and all four are
    /// simply working the ring. Without it a squad is a conveyor belt of knives.</summary>
    private const float StrikeGap = 1.9f;

    private float _rota;
    private bool _windowOpen;
    private int _turn = -1;

    public SoldierSquad(IEnumerable<EnemySoldier> members)
    {
        int slot = 0;
        foreach (var m in members)
        {
            m.Slot = slot;
            m.Bearing = slot * MathF.Tau / Size;
            Members.Add(m);
            slot++;
        }
        // The ring starts wherever the squad happens to have been dropped, so two squads
        // raised in the same minute don't rotate in sympathy.
        Ring = Random.Shared.NextSingle() * MathF.Tau;
        _rota = StrikeGap;
    }

    /// <summary>
    /// One tick of the plan: bury the dead, decide whether the squad has seen anything,
    /// turn the ring, pass the turn along, and write one order onto each member. Nothing
    /// here moves anybody — the soldiers read these orders on their own step.
    /// </summary>
    /// <summary>
    /// True while this squad is flying cover for the player rather than hunting them: their
    /// comrade is being worn, they cannot tell, and as far as they are concerned one of the
    /// four is right there and the enemy is somewhere else.
    ///
    /// It changes almost nothing about how they work, which is the point. The ring still
    /// turns, the rota still hands out the strike, the members still fly their own arcs —
    /// they are simply all pointed the other way. A squad is a squad; whose side it is on is
    /// one bool and a target.
    /// </summary>
    public bool Escorting { get; private set; }

    /// <summary>Told to them from outside — the world owns the question of who is wearing
    /// whom. Dropping the escort also drops the rota, so the first thing a betrayed squad
    /// does is pick a fresh turn rather than finish somebody else's.</summary>
    public void SetEscort(bool escorting)
    {
        if (Escorting == escorting) return;
        Escorting = escorting;
        _windowOpen = false;
        _rota = StrikeGap;
        Striker = null;
        // An escort is, by definition, a squad that has already been called out and is
        // working — there is nothing left to notice.
        Alerted = escorting;
    }

    /// <param name="targetVisible">
    /// False while the squad has no idea the target is a target — which, in practice, means a
    /// VIRUS wearing one of their own. They are not fooled by a disguise so much as they
    /// simply never look twice: four of them left the tower and four shapes are in the air,
    /// and one of them being driven by something else is not a distinction anybody out here
    /// is equipped to make. Ignored entirely while <see cref="Escorting"/>, which is the
    /// stronger version of the same idea: not merely failing to see an enemy, but flying
    /// beside them.
    /// </param>
    public void Update(float dt, Vector2 targetPos, bool targetVisible = true)
    {
        JustCalled = false;
        // The dead and the turned, in one sweep. A carrier is no longer theirs to give
        // orders to — the squad has lost them exactly as surely as if they had been killed,
        // and rather more inconveniently, because they are still in the air.
        Members.RemoveAll(m => !m.Alive || m.Carrier);
        if (Members.Count == 0) { Striker = null; return; }

        float nearest = float.MaxValue;
        foreach (var m in Members)
            nearest = MathF.Min(nearest, Torus.Distance(m.Position, targetPos));

        if (!targetVisible && !Escorting)
        {
            // Nothing to call, nothing to work a bearing against. They drop back to what a
            // squad does when it cannot see anybody: hold, and go back to the walls.
            Alerted = false;
            Striker = null;
            _windowOpen = false;
            _rota = StrikeGap;
            foreach (var m in Members) m.Order = SoldierOrder.Hold;
            return;
        }

        // An escort never loses interest and never has to be called: they are already flying
        // with the thing this measurement is about, and a squad that "lost sight" of the
        // comrade it is escorting would simply stop working.
        if (Escorting)
        {
            Alerted = true;
        }
        else if (!Alerted && nearest < AlertRange)
        {
            Alerted = true;
            JustCalled = true;
        }
        else if (Alerted && nearest > LoseRange)
        {
            // Lost them. Back onto the towers, and the next sighting calls again.
            Alerted = false;
            Striker = null;
            _windowOpen = false;
            _rota = StrikeGap;
        }

        if (!Alerted)
        {
            foreach (var m in Members) m.Order = SoldierOrder.Hold;
            return;
        }

        Ring += RingRate * dt;
        if (Ring > MathF.Tau) Ring -= MathF.Tau;

        // The rota. One window open, one closed, round and round for as long as anybody is
        // left to take a turn.
        _rota -= dt;
        if (_rota <= 0f)
        {
            _windowOpen = !_windowOpen;
            _rota = _windowOpen ? StrikeWindow : StrikeGap;
            if (_windowOpen) _turn = PickStriker(targetPos);
        }

        Striker = _windowOpen && _turn >= 0 && _turn < Members.Count ? Members[_turn] : null;

        for (int i = 0; i < Members.Count; i++)
        {
            EnemySoldier m = Members[i];
            // Bearings are re-dealt every tick rather than fixed at construction, so a
            // squad that has lost two members closes the ring back up into an even spread
            // instead of leaving the gaps its dead used to hold.
            m.Bearing = Ring + i * MathF.Tau / Members.Count;
            m.Slot = i;

            if (ReferenceEquals(m, Striker)) m.Order = SoldierOrder.Strike;
            // Anyone still out past the ring is crossing ground rather than working it,
            // which is a different job and gets a different word.
            else if (Torus.Distance(m.Position, targetPos) > EnemySoldier.EngageRange * 1.8f)
                m.Order = SoldierOrder.Flank;
            else m.Order = SoldierOrder.Press;
        }
    }

    /// <summary>
    /// Whose turn it is. Not a plain rotation through the list but the member best placed
    /// to actually arrive: closest wins, with height counted as most of the way there,
    /// since a soldier above the target is already halfway through their run and one below
    /// it has to climb before it can even start.
    ///
    /// With one hard rule on top of the scoring: never the same soldier twice running while
    /// anyone else can go. Left to the placement alone, whichever member happened to be
    /// aggressive would win every window and the squad would collapse into one enemy and
    /// three spectators — which is not a rota and, far worse, is not a fight the player can
    /// learn to read. Taking turns is the thing they are for.
    /// </summary>
    private int PickStriker(Vector2 targetPos)
    {
        int eligible = 0;
        foreach (var m in Members)
            if (m.Move != SoldierMove.Running) eligible++;   // nobody charges on foot

        int best = -1;
        float bestScore = float.MaxValue;

        for (int i = 0; i < Members.Count; i++)
        {
            EnemySoldier m = Members[i];
            if (m.Move == SoldierMove.Running) continue;
            // The one who just went sits this window out — unless they are the only one
            // still flying, in which case there is nobody to hand it to and they come
            // again. A lone survivor gets no gentler.
            if (eligible > 1 && ReferenceEquals(m, _wentLast)) continue;

            float score = Torus.Distance(m.Position, targetPos) - m.Height * 0.8f;
            if (score >= bestScore) continue;
            bestScore = score;
            best = i;
        }

        // Everyone is on the grid: no run this window. They will get themselves back into
        // the air on their own, and the rota comes round again.
        _wentLast = best >= 0 ? Members[best] : null;
        return best;
    }

    /// <summary>Who took the last window, so the next one goes to somebody else. Held as
    /// the soldier rather than as an index, because the list shifts under it every time one
    /// of them is killed.</summary>
    private EnemySoldier? _wentLast;
}
