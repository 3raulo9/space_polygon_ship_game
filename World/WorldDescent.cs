using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Entities;

namespace Unrendered.World;

/// <summary>
/// The DESCENT half of the world: the run director, the modular bosses it raises, and the
/// translation of a boss's decisions into things that actually happen on the grid.
///
/// <para>Split out of <c>World.cs</c> deliberately. That file is the sandbox — the hunters, the
/// squads, the two hand-built monsters, the salvage drip — and it is already seven thousand
/// lines of it. DESCENT is a different game running on the same field, and threading four
/// hundred more lines through the middle of the sandbox's loop would leave neither readable.
/// Everything here is additive: with <see cref="MatchSettings.Mode"/> on SANDBOX, not one line
/// of this file executes and the world behaves exactly as it always has.</para>
/// </summary>
public sealed partial class World : IDescentField
{
    /// <summary>The run, on a DESCENT world. Null in SANDBOX, and every read of it is guarded —
    /// which is the seam that keeps the two modes from becoming one mode with a flag.</summary>
    public Descent? Run { get; private set; }

    /// <summary>Whether this world is running a descent at all. Read by the HUD, the pause
    /// panel and the spawn gate.</summary>
    public bool IsDescent => Run is not null;

    /// <summary>
    /// The rolled bosses on the field. A list rather than the single slot the Crab-Core and the
    /// Maw-Core each get, because the TWINNED quirk ends a fight with two of them — and because
    /// a boss that arrives while another is dying should not silently delete the corpse.
    /// </summary>
    public readonly List<ModularBoss> Bosses = new();

    /// <summary>The one currently being fought, for the HUD's layer bars. The first living
    /// entry, so a TWINNED pair shows the half that is still up.</summary>
    public ModularBoss? Headline
    {
        get
        {
            foreach (var b in Bosses) if (b.Alive) return b;
            return Bosses.Count > 0 ? Bosses[^1] : null;
        }
    }

    /// <summary>
    /// A shard rising out of a corpse. Purely a thing to look at: the fragment is awarded to
    /// the seat that landed the killing blow the instant the boss dies, so it can never be
    /// missed, lost in the fog, or picked up by the wrong person. What rises here is the
    /// <em>ceremony</em> — the moment that tells the room what just dropped and who has it.
    /// </summary>
    public sealed class FragmentShard
    {
        public required Vector2 Position { get; init; }
        public required Fragment Kind { get; init; }
        public required int Seat { get; init; }
        public float Age;

        /// <summary>How long the rise lasts. Long enough to read the tag that just appeared
        /// under somebody's name and connect the two.</summary>
        public const float Life = 5.5f;
        public float Rise => MathF.Min(1f, Age / (Life * 0.55f));
        public bool Spent => Age >= Life;
    }

    private readonly List<FragmentShard> _shards = new();
    public IReadOnlyList<FragmentShard> Shards => _shards;

    /// <summary>Which seat last landed a blow on the boss, so the fragment has somebody to go
    /// to. Falls back to seat zero, which in a solo run is the only answer there is.</summary>
    private int _lastBossHitBy;

    /// <summary>Per-seat snare clocks. A snared craft has <see cref="PlayerTank.Rooted"/> held
    /// down for the duration and released here, rather than by whatever set it — nothing else
    /// in the game roots a player for a fixed time, so the timer has to live somewhere.</summary>
    private readonly float[] _snared = new float[MatchSettings.MaxSeats];

    /// <summary>Whether anybody is holding READY this frame. Filed by the input pass and read
    /// by the director during an intermission.</summary>
    private bool _readyHeld;

    /// <summary>Called from the input pass each tick so the director can see the hold. Any seat
    /// holding it counts — a break ends when <em>somebody</em> is done with it, not when
    /// everybody is, because one person waiting out ninety seconds for a team-mate who has
    /// walked away from the keyboard is the worst version of this.</summary>
    public void FileReady(bool held) => _readyHeld = held;

    /// <summary>Stands a run up. Called from the constructor on a DESCENT world; the seed is
    /// open so a self-test or a capture can pin the whole session's five bosses.</summary>
    private void OpenDescent(int? seed = null)
    {
        Run = new Descent(Match.Destination, seed);
        // The sandbox's opening hunter and its endless top-ups have no business here: a
        // descent's roster is exact, and something wandering in off the horizon would put the
        // wave bar permanently out of step with what is actually on the field.
        DynamicSpawning = false;

        // UNRENDERED_DESCENT can name a phase rather than just "1", so the layer stack and the
        // salvage clock can be photographed without playing four waves to reach them.
        switch (Environment.GetEnvironmentVariable("UNRENDERED_DESCENT"))
        {
            case "colossus": Run.SkipTo(DescentPhase.Colossus, Descent.WaveCount, this); break;
            case "herald": Run.SkipTo(DescentPhase.Herald, 2, this); break;
            case "intermission": Run.SkipTo(DescentPhase.Intermission, 3, this); break;
        }
    }

    /// <summary>
    /// Capture hatch: stands one rolled boss on the axis in front of the craft so a
    /// procedurally-built monster can actually be looked at.
    ///
    /// <para>This exists because a generator is only as good as the bodies you have checked, and
    /// a body that only appears three waves into a live run is a body nobody will ever check.
    /// <c>UNRENDERED_BOSS_ROLL=&lt;lineage&gt;[:colossus][:&lt;seed&gt;]</c> — for instance
    /// <c>drowner:colossus:404</c> — puts that exact one in front of the lens, every time, on any
    /// world. The seed makes it reproducible, which is the only way to photograph a thing whose
    /// whole design is being different each time.</para>
    /// </summary>
    private void StageRolledBossForCapture()
    {
        string? spec = Environment.GetEnvironmentVariable("UNRENDERED_BOSS_ROLL");
        if (string.IsNullOrWhiteSpace(spec)) return;

        var parts = spec.Split(':', StringSplitOptions.RemoveEmptyEntries);
        BossLineage lineage = BossLineage.Stalker;
        bool colossus = false;
        int seed = 1;
        // Which pose to freeze in. WALK is the default; the rest exist so the animation work can
        // actually be checked — a wind-up that only happens for a second and a half in every ten
        // is otherwise photographed by luck.
        ModularBoss.State pose = ModularBoss.State.Stalking;
        int shed = 0;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "wind": pose = ModularBoss.State.Winding; continue;
                case "strike": pose = ModularBoss.State.Striking; continue;
                case "recover": pose = ModularBoss.State.Recover; continue;
                case "break": pose = ModularBoss.State.Breaking; continue;
                // How many layers to have already broken off, so the stripped-down late-fight
                // body can be photographed without playing the first four bars.
                case "shed1": shed = 1; continue;
                case "shed2": shed = 2; continue;
                case "shed4": shed = 4; continue;
            }

            // The number is tested FIRST, and deliberately: Enum.TryParse happily accepts a
            // numeric string and hands back BossLineage 1234, so a seed offered after a lineage
            // silently overwrote it with a nonsense value — which is how the first capture of
            // this system came out as a COLUMN when it had asked for a STALKER.
            if (int.TryParse(part, out int n)) seed = n;
            else if (part.Equals("colossus", StringComparison.OrdinalIgnoreCase)) colossus = true;
            else if (Enum.TryParse(part, ignoreCase: true, out BossLineage l)) lineage = l;
        }

        BossGenome gene = colossus
            ? BossGen.Colossus(seed, lineage, 1f)
            : BossGen.Herald(seed, lineage, 0.5f);

        // Pinned at the far end of its band so it holds the distance it is placed at instead of
        // driving into the lens. Without this every capture of a boss is the same picture of a
        // wall of facets: they close on the player, which is what they are for.
        gene = gene with { Standoff = 1f };

        // Stood off by its own size rather than at a fixed distance. A rolled body can be a low
        // seven-unit tread hull or a twenty-five-unit standing figure, and one distance cannot
        // frame both — at 56 units the walkers looked right and the tall ones had their heads
        // out of shot, which is exactly the part you needed to see.
        var probe = new ModularBoss(gene, Vector2.Zero);
        // Height counts the cruise altitude too: a hovering DROWNER's body starts twelve units
        // off the grid before any of it exists, and framing it by its body alone put the whole
        // animal above the top of the shot.
        float span = MathF.Max(probe.Height + probe.CarriageLift * gene.Scale + probe.BodyHeight,
            probe.Radius * 2.2f);
        float back = Math.Clamp(span * 3.6f, 38f, 115f);

        var staged = new ModularBoss(gene, new Vector2(0f, back), MathF.PI);
        // Break the requested number of layers off before anyone looks at it, so the stripped
        // late-fight body is photographable without playing four bars to reach it. Struck from
        // behind, since a SEALED roll would otherwise eat every hit on its plates.
        for (int i = 0; i < shed && staged.LayersLeft > 1; i++)
            staged.Damage(gene.LayerHealth * 2f, fromFront: false);
        staged.Posed = true;
        if (pose != ModularBoss.State.Stalking) staged.PoseAs(pose, gene.Attacks[0]);

        Bosses.Add(staged);
        DynamicSpawning = false;

        // What actually rolled, printed beside the picture. Without this a capture is a body
        // with no account of why it looks like that, and a generator bug is indistinguishable
        // from a renderer bug.
        Console.WriteLine($"BOSS ROLL  {gene.FullName}  seed={gene.Seed} {gene.Lineage}");
        Console.WriteLine($"  carriage={gene.Carriage} limbs={gene.LimbCount} limbLen={gene.LimbLength:0.00} "
            + $"scale={gene.Scale:0.00} spines={gene.SpineCount} oversize={gene.OversizeLimb}");
        Console.WriteLine($"  body={gene.BodyWidth:0.0}x{gene.BodyRise:0.0}x{gene.BodyDepth:0.0} "
            + $"radius={staged.Radius:0.0} height={staged.BodyHeight:0.0} lift={staged.CarriageLift:0.0}");
        Console.WriteLine($"  paint={ClassCatalog.SwatchName(gene.ShellSwatch)}/"
            + $"{ClassCatalog.SwatchName(gene.DeepSwatch)}/{ClassCatalog.SwatchName(gene.LimbSwatch)} "
            + $"core={gene.CoreTone} quirk={gene.Quirk} layers={gene.Layers}");
        Console.WriteLine($"  attacks={string.Join(", ", gene.Attacks)}");
    }

    // --- The tick ------------------------------------------------------------------

    /// <summary>
    /// Steps the whole of DESCENT for one frame: the director, the bosses, everything they
    /// asked for, and the snares they left on people. Called from the host's path only —
    /// a client is shown the result, exactly as it is shown the hunters.
    /// </summary>
    private void StepDescent(float dt)
    {
        if (Run is null) return;

        // The run first: it may raise a boss this tick, and a boss raised now should get its
        // own step rather than standing inert for a frame.
        Run.Update(dt, this, _readyHeld);
        _readyHeld = false;

        // Solo, the run ends when the craft is out of lives. A room ends it when the room is
        // spent, which PlayerTank.Spectating already tracks for every seat.
        if (!Run.Finished && EveryoneIsSpent()) Run.Fail(this);
    }

    /// <summary>
    /// Steps whatever rolled bosses are on the field, whether or not a run put them there.
    ///
    /// <para>Deliberately <em>not</em> gated on <see cref="IsDescent"/>. A boss is an entity like
    /// any other and has to live wherever it is standing — the capture hatch drops one onto a
    /// SANDBOX world to be photographed, and a boss that is never stepped is one frozen mid-
    /// arrival, sunk halfway into the grid with its limbs unposed. Which is exactly what the
    /// first capture of this system showed.</para>
    /// </summary>
    private void StepRolledBosses(float dt)
    {
        if (Bosses.Count == 0)
        {
            // Let the rotor fade out when the last one goes — but never fight the Crab-Core for
            // the channel, since in SANDBOX that one owns it.
            if (Boss is null) Audio.SetBossHum(false, Vector2.Zero, 0f);
            AgeShards(dt);
            return;
        }

        for (int i = Bosses.Count - 1; i >= 0; i--)
        {
            ModularBoss boss = Bosses[i];
            PlayerTank quarry = NearestPlayer(boss.Position);
            boss.Update(dt, quarry.Position, quarry.Height);

            foreach (var act in boss.Acts) ApplyBossAct(boss, act);
            boss.ClearActs();

            foreach (var cue in boss.Cues) Emit(cue.Id, cue.At, cue.Param);
            boss.ClearCues();

            if (boss.LayersBrokenThisTick > 0) StageLayerBreak(boss);
            if (boss.JustDied) BossWentDown(boss);

            // The rotor. Driven off whichever boss is loudest — the nearest living one — so a
            // pair of TWINNED halves does not stack two hums on one listener.
            if (boss.Dead) Bosses.RemoveAt(i);
        }

        DriveBossHum();
        AgeShards(dt);
        ReleaseSnares(dt);
    }

    private bool EveryoneIsSpent()
    {
        foreach (var p in Players)
            if (!p.Away && p.Alive) return false;
        return Players.Count > 0;
    }

    private void DriveBossHum()
    {
        ModularBoss? loud = null;
        float best = float.MaxValue;
        foreach (var b in Bosses)
        {
            if (!b.Alive) continue;
            float d = Torus.DistanceSquared(b.Position, Eye.Position);
            if (d < best) { best = d; loud = b; }
        }
        // Never fights the Crab-Core for the channel: a descent has no ambient crab, so
        // whichever of the two is speaking is the only one that can be.
        if (loud is not null)
            Audio.SetBossHum(true, loud.Position, 0.4f + 0.6f * loud.Ruin);
        else if (Boss is null)
            Audio.SetBossHum(false, Vector2.Zero, 0f);
    }

    private void AgeShards(float dt)
    {
        for (int i = _shards.Count - 1; i >= 0; i--)
        {
            _shards[i].Age += dt;
            if (_shards[i].Spent) _shards.RemoveAt(i);
        }
    }

    private void ReleaseSnares(float dt)
    {
        for (int seat = 0; seat < Players.Count && seat < _snared.Length; seat++)
        {
            if (_snared[seat] <= 0f) continue;
            _snared[seat] -= dt;
            if (_snared[seat] <= 0f) Players[seat].Rooted = false;
        }
    }

    // --- Turning a decision into an event ------------------------------------------

    /// <summary>
    /// Carries out one thing a boss asked for. This is the whole of the coupling between the
    /// boss entity and the world: the entity knows what it wants to do and nothing at all about
    /// projectile pools, structure damage or who is standing where.
    /// </summary>
    private void ApplyBossAct(ModularBoss boss, in BossAct act)
    {
        switch (act.Kind)
        {
            case BossActKind.Bolt:
            {
                var dir = new Vector2(act.Dir.X, act.Dir.Z);
                if (dir.LengthSquared() < 1e-6f) break;
                dir = Vector2.Normalize(dir);
                Vector2 from = new Vector2(act.Origin.X, act.Origin.Z) + dir * (boss.Radius + 1f);
                SpawnProjectile(from, dir, owner: Projectile.NoOwner,
                    launchHeight: act.Origin.Y, pitch: act.Dir.Y);
                break;
            }

            case BossActKind.Mortar:
            {
                // Lobbed at a mark on the ground rather than at the player, which is what makes
                // it dodgeable: the shells land where you were, and moving is the answer.
                Vector2 from = new(act.Origin.X, act.Origin.Z);
                Vector2 to = Torus.NearestImage(act.Target, from);
                Vector2 dir = to - from;
                if (dir.LengthSquared() < 1e-6f) break;
                SpawnProjectile(from, Vector2.Normalize(dir), owner: Projectile.NoOwner,
                    grenade: true);
                break;
            }

            case BossActKind.Beam:
            {
                // Reuses the SPIDER's lance burn wholesale — the same shaft that fells towers
                // and rakes hunters, pointed the other way. A boss beam that behaved differently
                // from the one the player carries would be a second physics for the same object.
                BurnBeamAlong(act.Origin, act.Dir, BossBeamLength, act.Power,
                    BossBeamDamage * (act.Power / 2f));
                BurnPlayersAlong(act.Origin, act.Dir, BossBeamLength, act.Power);
                break;
            }

            case BossActKind.Shock:
            {
                // A ring travelling out along the grid. Everything it reaches is thrown and
                // billed, falling off with distance so the edge of it is a shove and the middle
                // of it is a mistake.
                var at = new Vector2(act.Origin.X, act.Origin.Z);
                foreach (var mark in Players)
                {
                    if (!mark.Alive || mark.Away) continue;
                    float d = Torus.Distance(at, mark.Position);
                    if (d > act.Power) continue;
                    // Off the ground is off the shock: a craft in the air genuinely steps over
                    // a ground wave, which gives every chassis that can leave the grid a real
                    // answer to it and leaves the TANK to eat it, as the TANK does.
                    if (mark.Height > 2.5f) continue;
                    float bite = ShockDamage * (1f - d / act.Power);
                    DamagePlayer(bite, mark, at);
                    JoltPlayerView(mark, 0.5f + 0.5f * (1f - d / act.Power));
                }
                Debris.FootPuff(new Vector3(at.X, 0f, at.Y));
                ShakeFromBlast(act.Power);
                break;
            }

            case BossActKind.Summon:
            {
                // It tears open and something walks out. Spawned right at the boss rather than
                // out in the fog, because the whole read of the move is that these came out of
                // it — a hunter fading in on the horizon is the sandbox's director, not this.
                var at = new Vector2(act.Origin.X, act.Origin.Z);
                Enemies.Add(new EnemyTank(Torus.Wrap(at), elite: act.Power >= 1f));
                Debris.Burst(new Vector3(at.X, 1.5f, at.Y), boss.Gene.Core, act.Power >= 1f);
                break;
            }

            case BossActKind.Tether:
            {
                // Reels the nearest craft in toward the boss. Nasty precisely because it is not
                // damage: it puts you where the boss wants you, which is inside whatever it does
                // next.
                PlayerTank mark = NearestPlayer(boss.Position);
                if (!mark.Alive || mark.Away) break;
                Vector2 pull = Torus.Delta(mark.Position, boss.Position);
                float len = pull.Length();
                if (len < 1e-3f) break;
                float reach = MathF.Min(TetherPull, MathF.Max(0f, len - boss.Radius - 3f));
                mark.Position = Torus.Wrap(mark.Position + pull / len * reach);
                Emit(Cue.CableZip, mark.Position);
                JoltPlayerView(mark, 0.4f);
                break;
            }

            case BossActKind.Snare:
            {
                PlayerTank mark = NearestPlayer(boss.Position);
                int seat = Seat(mark);
                if (seat == Projectile.NoOwner || !mark.Alive) break;
                mark.Rooted = true;
                _snared[seat] = MathF.Max(_snared[seat], act.Power);
                Emit(Cue.AnchorBite, mark.Position);
                break;
            }

            case BossActKind.Scream:
            {
                // No damage at all, and that is the point: it wrenches the view, it winds
                // everything else on the field up, and it costs the boss a long recovery to do.
                foreach (var mark in Players)
                {
                    if (!mark.Alive || mark.Away) continue;
                    float d = Torus.Distance(boss.Position, mark.Position);
                    if (d > ScreamRadius) continue;
                    JoltPlayerView(mark, 1f - d / ScreamRadius);
                }
                foreach (var e in Enemies)
                    if (e.Alive) e.PreferredRange = MathF.Max(8f, e.PreferredRange * 0.7f);
                ShakeFromBlast(ScreamRadius);
                break;
            }

            case BossActKind.Shroud:
                LaySmoke(new Vector2(act.Origin.X, act.Origin.Z));
                break;

            case BossActKind.Impact:
            {
                var at = new Vector2(act.Origin.X, act.Origin.Z);
                foreach (var mark in Players)
                {
                    if (!mark.Alive || mark.Away) continue;
                    float d = Torus.Distance(at, mark.Position);
                    if (d > act.Power) continue;
                    DamagePlayer(ImpactDamage * (1f - d / act.Power), mark, at);
                    JoltPlayerView(mark, 0.8f);
                }
                // A body this size coming down moves the floor and everything on it.
                Debris.FootPuff(new Vector3(at.X, 0f, at.Y));
                ShakeFromBlast(act.Power * 1.5f);
                // And it crushes whatever it landed on, which is the reason to bait a leap
                // into a crowd of its own hunters.
                foreach (var e in Enemies)
                    if (e.Alive && Torus.Distance(at, e.Position) < act.Power * 0.5f)
                        DamageEnemy(e, 4f);
                break;
            }
        }
    }

    /// <summary>
    /// The players' half of a beam burn. <c>BurnBeamAlong</c> only ever had hostiles to rake —
    /// it is the SPIDER's weapon — so the shaft coming the other way needs its own pass over
    /// the craft. Same geometry: within the shaft's radius on the plane, and somewhere between
    /// the craft's feet and its head where it crosses.
    /// </summary>
    private void BurnPlayersAlong(Vector3 origin, Vector3 direction, float length, float radius)
    {
        var originXZ = new Vector2(origin.X, origin.Z);
        var dirXZ = new Vector2(direction.X, direction.Z);
        float planar = dirXZ.Length();
        if (planar < 1e-4f) return;
        dirXZ /= planar;
        float slope = direction.Y / planar;

        foreach (var mark in Players)
        {
            if (!mark.Alive || mark.Away || mark.Captured) continue;
            Vector2 near = Torus.NearestImage(mark.Position, originXZ);
            float along = Math.Clamp(Vector2.Dot(near - originXZ, dirXZ), 0f, length);
            if (Vector2.Distance(near, originXZ + dirXZ * along) > radius + PlayerTank.Radius)
                continue;

            float beamY = origin.Y + slope * along;
            if (beamY < mark.Height - radius || beamY > mark.Height + PlayerTank.Radius * 2f + radius)
                continue;

            DamagePlayer(BossBeamPlayerDamage, mark, originXZ);
        }
    }

    // --- Tuning --------------------------------------------------------------------

    /// <summary>How far a boss beam reaches. Shorter than the SPIDER's, because a shaft that
    /// crosses the whole arena is not a thing you dodge, it is a thing that finds you.</summary>
    private const float BossBeamLength = 90f;

    /// <summary>What one tick of beam costs a hunter caught in it. Bosses are not careful about
    /// what else is standing in the line, which is a real tactic against them.</summary>
    private const float BossBeamDamage = 2.5f;

    /// <summary>And what it costs a craft, per tick of contact. Held low per-tick and lethal
    /// over a second: a beam is meant to be something you leave, not something that deletes you
    /// the instant it touches.</summary>
    private const float BossBeamPlayerDamage = 1.6f;

    /// <summary>The ground shock at its centre, falling to nothing at the rim.</summary>
    private const float ShockDamage = 22f;

    /// <summary>A body landing on you.</summary>
    private const float ImpactDamage = 26f;

    /// <summary>How far a scream is felt.</summary>
    private const float ScreamRadius = 55f;

    /// <summary>How far one tether hauls a craft. Enough to be genuinely relocated, not enough
    /// to be teleported into the boss's mouth.</summary>
    private const float TetherPull = 22f;

    // --- Staging the moments -------------------------------------------------------

    /// <summary>
    /// A layer just came off. This is the beat the whole fight is built around, so it gets a
    /// real staging rather than a number changing: the screen washes in the core's own colour,
    /// the plates blow off as debris in the boss's own paint, and the ground clears.
    /// </summary>
    private void StageLayerBreak(ModularBoss boss)
    {
        Vector3 core = boss.CorePoint;

        // The shed plate, thrown outward in the boss's own shell colour so the wreckage is
        // recognisably a piece of *that* monster.
        for (int i = 0; i < 3; i++)
            Debris.Burst(core + new Vector3(
                (Random.Shared.NextSingle() - 0.5f) * boss.Radius * 2f,
                (Random.Shared.NextSingle() - 0.5f) * boss.BodyHeight,
                (Random.Shared.NextSingle() - 0.5f) * boss.Radius * 2f),
                boss.Gene.Shell, elite: true);
        Debris.Burst(core, boss.Gene.Core, elite: true);

        Emit(Cue.CrabScream, boss.Position, 1f);
        ShakeFromBlast(boss.Radius * 5f);

        Announce?.Invoke(boss.LayersLeft == 1
            ? $"{boss.Gene.Name} - LAST LAYER"
            : $"{boss.Gene.Name} - {boss.LayersLeft} LAYERS LEFT");
    }

    /// <summary>
    /// It is down. The fragment goes to whoever landed the last blow, the shard rises out of
    /// the corpse, and the run is told so it can move on to the break.
    /// </summary>
    private void BossWentDown(ModularBoss boss)
    {
        if (Run is null) return;

        int seat = (uint)_lastBossHitBy < (uint)Players.Count ? _lastBossHitBy : 0;
        Fragment got = Run.AwardFragment(seat);

        _shards.Add(new FragmentShard { Position = boss.Position, Kind = got, Seat = seat });
        Run.TakePendingFragment();

        Emit(Cue.BossDeath, boss.Position);
        Emit(Cue.MawCrystal, boss.Position, 1f);
        Announce?.Invoke(
            $"{NameOrSeat(seat)} TOOK THE FRAGMENT OF THE {(got == Fragment.Sun ? "SUN" : "MOON")}");

        // A boss is worth a real pile of parts — it is the biggest machine on the field and it
        // has just been opened.
        for (int i = 0; i < 5; i++) ScatterMaterials(boss.Position);
        DropSalvage(boss.Position, PickupKind.Battery);
        DropSalvage(boss.Position, PickupKind.Ammo);
        // And a kit. Whoever just fought that has holes in them, and the boss is the one body
        // on the field that is certainly worth enough to have carried one.
        DropSalvage(boss.Position, PickupKind.RepairKit);

        // TWINNED: its death is not the end of it. Two halves, each carrying one layer, rolled
        // off the parent's own seed so they are visibly its children rather than two new
        // strangers.
        if (boss.Gene.Quirk == Quirk.Split && boss.Gene.Layers > 1)
        {
            for (int i = 0; i < 2; i++)
            {
                var half = BossGen.Herald(boss.Gene.Seed + 101 + i, boss.Gene.Lineage) with
                {
                    Scale = boss.Gene.Scale * 0.55f,
                    LayerHealth = boss.Gene.LayerHealth * 0.45f,
                };
                Vector2 at = Torus.Wrap(boss.Position
                    + new Vector2(i == 0 ? 8f : -8f, i == 0 ? 6f : -6f));
                Bosses.Add(new ModularBoss(half, at, boss.Heading));
            }
            Announce?.Invoke($"{boss.Gene.Name} COMES APART INTO TWO");
        }
    }

    // --- Damage in ------------------------------------------------------------------

    /// <summary>
    /// Puts a player's hit into whichever boss it landed on. Returns true when the round was
    /// consumed, so the projectile pass can stop looking.
    ///
    /// <para>The whole exposure rule lives inside <see cref="ModularBoss.Damage"/> rather than
    /// here — the entity knows whether it is open and whether its plates are in the way, and a
    /// second copy of that reasoning on this side is a second copy that will drift.</para>
    /// </summary>
    private bool StrikeBosses(Vector2 at, float height, float amount, int by, Vector2 from)
    {
        foreach (var boss in Bosses)
        {
            if (!boss.Hits(at, height)) continue;

            float landed = boss.Damage(amount, boss.IsFrontal(from));
            if (by != Projectile.NoOwner) _lastBossHitBy = by;

            if (landed <= 0f)
            {
                // The plates refused it. Said out loud with a hard clang and a spark, because a
                // shot that quietly does nothing reads as the game being broken rather than as
                // armour working.
                Emit(Cue.Clamp, boss.Position);
                Debris.Burst(new Vector3(at.X, height, at.Y), Palette.HudChrome, elite: false);
            }
            else
            {
                Emit(boss.Exposed ? Cue.CoreHit : Cue.Hit, boss.Position,
                    Math.Clamp(boss.Ruin, 0f, 1f));
            }
            return true;
        }
        return false;
    }

    /// <summary>Test hatch: puts damage straight into the headline boss, so a self-test can run
    /// a five-layer fight to its end in a few hundred ticks without a projectile pool.</summary>
    public float DamageBossForTest(float amount, bool fromFront = true)
        => Headline?.Damage(amount, fromFront) ?? 0f;

    /// <summary>Test hatch: raises a boss of a known seed and lineage on demand.</summary>
    public ModularBoss SpawnBossForTest(BossGenome gene, Vector2 at)
    {
        var boss = new ModularBoss(gene, at);
        Bosses.Add(boss);
        return boss;
    }

    // --- IDescentField ---------------------------------------------------------------

    void IDescentField.SpawnWaveHunter(bool elite)
    {
        // The wave's own arrivals come in further out than the sandbox's drip, so a crowd
        // resolves on the skyline and walks in rather than appearing at conversational range.
        Enemies.Add(new EnemyTank(RandomPointAroundPlayer(WaveSpawnMin, WaveSpawnMax), elite));
    }

    void IDescentField.SpawnWaveSquad() => SpawnSoldierSquad();

    void IDescentField.RaiseBoss(BossGenome gene)
    {
        // Placed at a fixed, generous distance rather than a rolled one: the arrival is the
        // only clean look a player gets at a procedurally-built silhouette, and it has to land
        // in view rather than behind a tower.
        Vector2 at = RandomPointAroundPlayer(BossSpawnRange, BossSpawnRange + 12f);
        Bosses.Add(new ModularBoss(gene, at));
        _lastBossHitBy = LocalIndex;
    }

    int IDescentField.LiveWaveUnits
    {
        get
        {
            int n = 0;
            foreach (var e in Enemies) if (e.Alive) n++;
            foreach (var s in Soldiers) if (s.Alive) n++;
            return n;
        }
    }

    bool IDescentField.BossAlive
    {
        get
        {
            foreach (var b in Bosses) if (b.Alive) return true;
            return false;
        }
    }

    void IDescentField.DropSalvage(int count)
    {
        for (int i = 0; i < count; i++)
        {
            // The salvage window between waves is where a run is repaired, and hull is the
            // thing a run loses permanently — so this is the one table where a repair kit is
            // reliably on offer rather than a lucky drop off a body.
            PickupKind kind = Random.Shared.NextSingle() switch
            {
                < 0.34f => PickupKind.Battery,
                < 0.50f => PickupKind.RepairKit,
                < 0.78f => PickupKind.Ammo,
                < 0.91f => PickupKind.ScrapMetal,
                < 0.97f => PickupKind.SpaceGunpowder,
                _ => PickupKind.CopperWire,
            };
            DropSalvage(RandomPointAroundPlayer(12f, 55f), kind);
        }
    }

    void IDescentField.SweepStragglers()
    {
        // Nothing hostile survives the end of a phase. A hunter that drifted off during a
        // herald fight and is still plinking at somebody trying to craft is not tension, it is
        // an unfinished chore — and worse, it makes the next wave bar start out of step.
        foreach (var e in Enemies)
            if (e.Alive) Debris.Burst(new Vector3(e.Position.X, 2f, e.Position.Y),
                Palette.EnemyFill, e.IsElite);
        Enemies.Clear();
        Soldiers.Clear();
        Squads.Clear();
    }

    void IDescentField.Announce(string line) => Announce?.Invoke(line);

    void IDescentField.Signal(Cue id, float param) => Emit(id, Eye.Position, param, personal: true);

    /// <summary>Where a wave's hunters fade in. Further out than the sandbox drip so a crowd
    /// arrives as a horizon full of shapes.</summary>
    private const float WaveSpawnMin = 85f;
    private const float WaveSpawnMax = 125f;

    /// <summary>And where a boss stands up. Inside the fog, so the silhouette is legible from
    /// the first frame of its arrival.</summary>
    private const float BossSpawnRange = 62f;

    /// <summary>
    /// Books one wave unit as dead. Called from the hunter and soldier death paths, and a no-op
    /// everywhere but a live wave — a hunter a boss summoned during a herald fight is not part
    /// of the crowd the bar is counting, and the director already refuses to count it.
    /// </summary>
    private void CountDescentKill() => Run?.CountKill();
}
