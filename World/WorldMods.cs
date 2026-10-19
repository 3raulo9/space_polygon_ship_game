using System.Numerics;
using Unrendered.Core;
using Unrendered.Entities;
using Unrendered.Modding;

namespace Unrendered.World;

/// <summary>
/// Everything the world does with content out of the mods folder. Today that is melee weapons
/// for the SOLDIER: the few lying on the grid when a match opens, the ones a dead soldier
/// lets fall, picking one up by walking over it, swinging it on FIRE, and letting go of it.
///
/// <para><b>A mod supplies numbers, never rules.</b> A weapon file says how hard, how far and
/// how wide; this file decides how a swing is aimed and routes every hit through the same
/// damage paths a round takes — <see cref="DamageEnemy"/>, <see cref="DamageSoldier"/>,
/// <see cref="StrikeBosses"/>, <see cref="BiteCraft"/>. So a modded blade gets kill credit,
/// salvage, the boss's armour refusal, the spider's hostage and friendly fire exactly as a
/// rifle does, and nothing a file can say gets around any of them.</para>
///
/// <para>Host-authoritative throughout, like salvage. A client predicts its own swing — the
/// sound and the arm — for an instant feel, and is told by the host what it hit, what it is
/// holding and what is lying on the grid.</para>
/// </summary>
public sealed partial class World
{
    /// <summary>Modded weapons nobody is holding.</summary>
    public readonly List<GroundWeapon> GroundWeapons = new();

    /// <summary>
    /// The most the field will put down on its own — the opening scatter and what dead
    /// soldiers drop. A weapon a <em>player</em> lets go of ignores this: it was in somebody's
    /// hand, and the field deleting it to make room would be the field stealing it.
    /// </summary>
    public const int MaxGroundWeapons = 12;

    /// <summary>How far out the opening scatter lands: past arm's length, inside the fog, so
    /// the first one is something seen and walked to rather than something stood on.</summary>
    private const float FieldWeaponMin = 22f;
    private const float FieldWeaponMax = 70f;

    /// <summary>How far ahead a let-go weapon is tossed: where it can be seen, and just past
    /// the reach that would put it straight back in a hand that stood still. A player who
    /// dropped a blade to get their rifle back has to walk onto it to take it again.</summary>
    public const float DropToss = 3.4f;

    /// <summary>How long the player who let go of a weapon must wait before the same one
    /// will go back into their hand.</summary>
    private const float DropRefusal = 1.5f;

    /// <summary>Same, after a weapon was knocked out of a dying hand — longer, because a
    /// craft rebuilt in place is still standing on it.</summary>
    private const float DeathRefusal = 3f;

    /// <summary>A beat between taking a weapon and its first swing, so a player who walks over
    /// one with FIRE held does not swing it on the frame it arrives.</summary>
    private const float DrawTime = 0.25f;

    /// <summary>Each seat's lives as of the last tick, so a life being spent can be noticed.
    /// A spent life rebuilds the craft on the spot, so there is no single tick on which the
    /// hull reads zero to catch it by.</summary>
    private readonly Dictionary<PlayerTank, int> _livesSeen = new();

    // --- Seeding ---------------------------------------------------------------------------

    /// <summary>
    /// The opening scatter, once, as the world is built. <c>UNRENDERED_SWORD_NEAR</c> is the
    /// capture harness's: "1" stands the first weapon seven metres ahead, "held" puts it in the
    /// SOLDIER's hand, so both the ground pose and the viewmodel can be photographed.
    /// </summary>
    private void SeedModWeapons(bool capture)
    {
        if (Mods.Weapons.Count == 0) return;

        string? hatch = Environment.GetEnvironmentVariable("UNRENDERED_SWORD_NEAR");
        if (hatch is "1" or "held")
        {
            if (hatch == "held" && Player.Soldier != null) Player.HeldWeapon = 0;
            else GroundWeapons.Add(new GroundWeapon(
                Torus.Wrap(Player.Position + Player.Forward * 7f), 0));
            return;
        }
        if (capture) return;

        foreach (var w in Mods.Weapons)
            for (int i = 0; i < w.FieldCount && GroundWeapons.Count < MaxGroundWeapons; i++)
                GroundWeapons.Add(new GroundWeapon(
                    ReachablePoint(PointAround(Player.Position, FieldWeaponMin, FieldWeaponMax)),
                    w.Index));
    }

    /// <summary>
    /// A soldier's body letting go of its blade. Rolled per weapon a mod offers, starting from
    /// a random one so a second mod's blade is not always second in line, and one at most per
    /// body. It falls from where they were hanging, which is usually a long way up.
    /// </summary>
    private void DropBladeFrom(EnemySoldier s)
    {
        int count = Mods.Weapons.Count;
        if (count == 0 || GroundWeapons.Count >= MaxGroundWeapons) return;

        int start = Random.Shared.Next(count);
        for (int k = 0; k < count; k++)
        {
            ModWeapon w = Mods.Weapons[(start + k) % count];
            if (w.SoldierDrop <= 0f || Random.Shared.NextSingle() >= w.SoldierDrop) continue;
            GroundWeapons.Add(new GroundWeapon(ReachablePoint(s.Position), w.Index,
                s.Height + EnemySoldier.AimHeight));
            return;
        }
    }

    // --- The host's tick ---------------------------------------------------------------------

    /// <summary>Falling, landing, and being picked up. Host only — see the client's
    /// <see cref="AnimateGroundWeapons"/>.</summary>
    private void UpdateGroundWeapons(float dt)
    {
        TickSwings(dt);
        DropFromTheFallen();
        if (GroundWeapons.Count == 0) return;

        float reach = PlayerTank.Radius + GroundWeapon.Radius;
        float reachSq = reach * reach;
        bool anyTaken = false;

        foreach (var g in GroundWeapons)
        {
            bool falling = !g.Landed;
            g.Update(dt);
            if (g.Taken) continue;

            // A weapon whose mod is no longer installed has nothing to be. Only reachable if
            // the registry changed under a live world, which nothing should do — but if it
            // ever does, the field drops it rather than handing somebody an index to nothing.
            if (Mods.Weapon(g.Weapon) is not { } w) { g.Taken = anyTaken = true; continue; }

            // It hit the grid this tick: a hard clang where it stuck, heard by anybody near.
            if (falling && g.Landed) Emit(Cue.Clamp, g.Position);

            for (int seat = 0; seat < Players.Count; seat++)
            {
                PlayerTank who = Players[seat];
                if (!CanTakeWeapon(who)) continue;
                if (seat == g.RefusedSeat && g.RefuseFor > 0f) continue;
                if (Torus.DistanceSquared(g.Position, who.Position) > reachSq) continue;
                if (MathF.Abs(who.Height - g.Height) > GroundWeapon.ReachUp) continue;

                who.HeldWeapon = g.Weapon;
                who.SwingCooldown = DrawTime;
                g.Taken = anyTaken = true;
                Emit(Cue.Pickup, g.Position, owner: seat, personal: true);
                Debris.Burst(new Vector3(g.Position.X, g.Height + 1.2f, g.Position.Y),
                    w.Spark, elite: false);
                break;
            }
        }

        if (anyTaken) GroundWeapons.RemoveAll(g => g.Taken);
    }

    /// <summary>Only a SOLDIER lifts a weapon, only one at a time, and only while it is in a
    /// state to be holding anything.</summary>
    private static bool CanTakeWeapon(PlayerTank who)
        => who.Soldier != null && who.HeldWeapon < 0
           && who.Alive && !who.Away && !who.Captured;

    /// <summary>A client's tick for the same list: the glint's clock and the ease down onto
    /// the host's account. It never picks anything up.</summary>
    private void AnimateGroundWeapons(float dt)
    {
        TickSwings(dt);
        foreach (var g in GroundWeapons) g.Animate(dt);
    }

    /// <summary>The swing clock, on every seat this machine simulates.</summary>
    private void TickSwings(float dt)
    {
        for (int i = 0; i < Players.Count; i++)
        {
            if (!Authoritative && i != LocalIndex) continue;
            PlayerTank p = Players[i];
            if (p.SwingCooldown > 0f) p.SwingCooldown = MathF.Max(0f, p.SwingCooldown - dt);
        }
    }

    /// <summary>
    /// A weapon goes down with the hand holding it: on a spent life, on a final death, and on
    /// a craft that is somehow holding one without being a SOLDIER at all.
    /// </summary>
    private void DropFromTheFallen()
    {
        foreach (var who in Players)
        {
            int lives = who.Lives;
            bool known = _livesSeen.TryGetValue(who, out int seen);
            _livesSeen[who] = lives;
            if (who.HeldWeapon < 0) continue;

            bool spent = known && lives < seen;
            if (spent || !who.Alive || who.Soldier == null)
                LetGo(who, toss: false);
        }

        // A replaced craft is a new object, so the old one's entry would otherwise sit here
        // for the rest of the match.
        if (_livesSeen.Count > Players.Count * 2)
            foreach (var gone in _livesSeen.Keys.Where(k => !Players.Contains(k)).ToList())
                _livesSeen.Remove(gone);
    }

    /// <summary>Puts the held weapon back on the grid, falling from the hand.</summary>
    private void LetGo(PlayerTank who, bool toss)
    {
        int weapon = who.HeldWeapon;
        who.HeldWeapon = -1;
        if (Mods.Weapon(weapon) == null) return;

        Vector2 at = toss ? who.Position + who.Forward * DropToss : who.Position;
        var g = new GroundWeapon(ReachablePoint(Torus.Wrap(at)), weapon, who.Height + 1.2f)
        {
            RefusedSeat = Seat(who),
            RefuseFor = toss ? DropRefusal : DeathRefusal,
        };
        GroundWeapons.Add(g);
    }

    /// <summary>The drop key. The host's to perform: a client is told it let go by the next
    /// snapshot, the same way it is told it picked something up.</summary>
    private void DropHeldWeapon(PlayerTank who)
    {
        if (!Authoritative || who.HeldWeapon < 0) return;
        LetGo(who, toss: true);
    }

    // --- The swing -----------------------------------------------------------------------------

    /// <summary>
    /// One swing of the held weapon. Runs on the client too, for the local seat, so the arm
    /// and the sound are instant; only the host goes on to cut anything.
    /// </summary>
    private void SwingHeldWeapon(PlayerTank who)
    {
        if (who.SwingCooldown > 0f) return;
        if (Mods.Weapon(who.HeldWeapon) is not { } w) return;
        if (who.Captured || HeldInClaw(who)) return;

        who.SwingCooldown = w.Cooldown;
        who.SwingSeq++;
        int seat = Seat(who);
        Emit(w.SwingCue, who.Position, owner: seat);

        if (!Authoritative) return;
        if (Cut(who, w, seat) > 0)
            // No owner: what it met is settled here, and the swinger never predicted it.
            Emit(w.HitCue, Torus.Wrap(who.Position + who.Forward * (w.Reach * 0.5f)));
    }

    /// <summary>
    /// What one swing meets: hunters, then the squads, then a boss, then — with friendly fire
    /// on — other craft, until the weapon's <see cref="ModWeapon.MaxTargets"/> is spent. Each
    /// is billed through its own ordinary damage path. Returns how many it cut.
    /// </summary>
    private int Cut(PlayerTank who, ModWeapon w, int seat)
    {
        Vector2 fwd = who.Forward;
        float centre = who.HitCentre;
        // How far up and down the column the sweep reaches. A blade is swung through an arc
        // with an arm behind it, not held out level, so it covers a band and not a line.
        float band = w.Reach * 0.6f;
        int hits = 0;

        foreach (var e in Enemies)
        {
            if (hits >= w.MaxTargets) break;
            if (!e.Alive) continue;
            Vector2 at = Rewound(e.HitId, seat, e.Position);
            if (!InSweep(who.Position, fwd, at, EnemyTank.Radius, w)) continue;
            if (!Overlaps(centre, band, e.Height, e.Height + EnemyTank.BodyHeight)) continue;

            Sparks(at, MathF.Min(centre, e.Height + EnemyTank.BodyHeight), w);
            DamageEnemy(e, w.Damage, seat);
            hits++;
        }

        foreach (var s in Soldiers)
        {
            if (hits >= w.MaxTargets) break;
            // Somebody flying cover for you, or a body a VIRUS is wearing, is not cut by a
            // blade swung in a crowd. A round can be aimed past them; a sweep cannot.
            if (!s.Alive || s.Carrier || s.Allied) continue;
            Vector2 at = Rewound(s.HitId, seat, s.Position);
            if (!InSweep(who.Position, fwd, at, EnemySoldier.Radius, w)) continue;
            if (!Overlaps(centre, band, s.Height, s.Height + EnemySoldier.BodyHeight)) continue;

            BlowCover();
            Sparks(at, s.Height + EnemySoldier.AimHeight, w);
            DamageSoldier(s, w.Damage, who.Position);
            hits++;
        }

        if (hits < w.MaxTargets && CutBosses(who, w, seat)) hits++;

        for (int i = 0; i < Players.Count && hits < w.MaxTargets; i++)
        {
            PlayerTank mark = Players[i];
            if (!Harmable(who, mark)) continue;
            Vector2 at = RewoundSeat(i, seat, mark.Position);
            if (!InSweep(who.Position, fwd, at, PlayerTank.Radius, w)) continue;
            if (!Overlaps(centre, band, mark.HitCentre - mark.HitHalfHeight,
                    mark.HitCentre + mark.HitHalfHeight)) continue;

            Sparks(at, mark.HitCentre, w);
            // Through BiteCraft like any round, so the plate a blow meets, the hostage held
            // up in a claw and the lance it spoils all answer a blade the way they answer a bolt.
            if (!BiteCraft(mark, w.PlayerDamage, fwd * 40f, who.Position))
                JoltPlayerView(mark, 0.35f);
            hits++;
        }

        return hits;
    }

    /// <summary>
    /// A blade against the monsters, which answer at a <em>point</em> — the crab's core, the
    /// maw's crystal, a rolled boss's body — rather than at a radius. So the sweep is sampled:
    /// five bearings across the arc, two distances along it, three heights up the body, and the
    /// first sample to land is the hit. One boss per swing.
    /// </summary>
    private bool CutBosses(PlayerTank who, ModWeapon w, int seat)
    {
        if (Boss is not { Alive: true } && Maw is not { Alive: true } && Bosses.Count == 0)
            return false;

        Vector2 fwd = who.Forward;
        float heading = MathF.Atan2(fwd.X, fwd.Y);
        float eye = who.Eye.Y;
        Span<float> heights = stackalloc float[] { who.HitCentre, eye, eye + w.Reach * 0.5f };

        for (int a = -2; a <= 2; a++)
        {
            float bearing = heading + w.HalfArc * a * 0.5f;
            var dir = new Vector2(MathF.Sin(bearing), MathF.Cos(bearing));
            for (int r = 1; r <= 2; r++)
            {
                Vector2 at = Torus.Wrap(who.Position + dir * (PlayerTank.Radius + w.Reach * r * 0.5f));
                foreach (float h in heights)
                {
                    if (Boss is { Alive: true } crab && crab.HitsCore(at, h))
                    {
                        Sparks(at, h, w);
                        if (crab.DamageCore(w.Damage)) DestroyBoss(crab);
                        else Emit(Cue.CoreHit, crab.Position, 1f - crab.CoreFraction);
                        return true;
                    }
                    if (Maw is { Alive: true } maw && maw.HitsCrystal(at, h))
                    {
                        Sparks(at, h, w);
                        if (maw.DamageCrystal(w.Damage)) DestroyMaw(maw);
                        else Emit(Cue.MawHurt, maw.Position, 1f - maw.CrystalFraction);
                        return true;
                    }
                    if (Bosses.Count > 0 && StrikeBosses(at, h, w.Damage, seat, who.Position))
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Whether something of <paramref name="radius"/> at <paramref name="target"/> is inside a
    /// sweep from <paramref name="from"/> facing <paramref name="fwd"/>. The arc is widened by
    /// the angle the target itself subtends, so a hunter twice the width of the swing is not
    /// missed because its centre was a degree outside it.
    /// </summary>
    private static bool InSweep(Vector2 from, Vector2 fwd, Vector2 target, float radius, ModWeapon w)
    {
        Vector2 d = Torus.Delta(from, target);
        float dist = d.Length();
        if (dist - radius > PlayerTank.Radius + w.Reach) return false;
        // Standing inside it: there is no direction left to miss by.
        if (dist <= radius + PlayerTank.Radius * 0.5f) return true;

        float cos = Math.Clamp(Vector2.Dot(d / dist, fwd), -1f, 1f);
        float widen = MathF.Asin(MathF.Min(1f, radius / dist));
        return MathF.Acos(cos) <= w.HalfArc + widen;
    }

    private static bool Overlaps(float centre, float band, float lo, float hi)
        => centre + band >= lo && centre - band <= hi;

    private void Sparks(Vector2 at, float height, ModWeapon w)
        => Debris.Burst(new Vector3(at.X, height, at.Y), w.Spark, elite: false);

    // --- Test hatches -----------------------------------------------------------------------

    /// <summary>Test hatch: one swing from the given seat (the local one by default).</summary>
    public void SwingForTest(PlayerTank? who = null) => SwingHeldWeapon(who ?? Player);

    /// <summary>Test hatch: a dead soldier's roll for a blade, without having to kill one.</summary>
    public void DropBladeForTest(EnemySoldier s) => DropBladeFrom(s);

    /// <summary>Test hatch: the opening scatter again, against whatever mods are installed now.</summary>
    public void SeedModWeaponsForTest() => SeedModWeapons(capture: false);
}
