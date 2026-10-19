using System.Numerics;
using Unrendered.Core;
using Unrendered.Entities;

namespace Unrendered.World;

/// <summary>
/// What a code mod may do to the field, through the same doors the game's own weapons use.
///
/// <para>A mod that wants to hurt a hunter does not get to subtract from its shield: it calls
/// <see cref="HurtEnemy"/>, which is <see cref="DamageEnemy"/>, so the kill is credited, the
/// salvage drops, the debris flies and a DESCENT counts it exactly as if a round had done it.
/// Everything here is a thin public face on something private, and nothing here is new rules.</para>
///
/// <para>Host and solo only, like everything that changes the field: on a client the world is
/// a picture of the host's, and whatever a mod did to it would be painted over next packet.</para>
/// </summary>
public sealed partial class World
{
    /// <summary>
    /// Seats a body a code mod moves and draws — see <see cref="PlayerTank.Puppet"/> — and
    /// hands back its craft, or null when every one of the twenty seats is taken.
    ///
    /// <para>Not bound by <see cref="MatchSettings.MaxPlayers"/>: that number is how many
    /// people the host invited, and a visitor walked in from somewhere else. It does take a
    /// real seat, so it has a pack, a name on the roster, a place on everybody's radar and a
    /// line in the kill feed, and the field hunts it like any other craft.</para>
    /// </summary>
    public PlayerTank? AddPuppet(Loadout build, string name, Vector2 at, float heading)
    {
        int seat = -1;
        foreach (int free in _vacant)
            if (seat < 0 || free < seat) seat = free;
        if (seat < 0)
        {
            if (Players.Count >= MatchSettings.MaxSeats) return null;
            seat = Players.Count;
        }
        else
        {
            _vacant.Remove(seat);
        }

        PlayerTank craft = OpenSeat(seat, build);
        craft.Puppet = true;
        craft.Position = Torus.Wrap(at);
        craft.Height = 0f;
        craft.Heading = heading;
        SeatNames[seat] = name;
        _inputs[seat] = InputFrame.Empty;
        return craft;
    }

    /// <summary>Lets a puppet's seat go — the visitor went home. The seat empties the way a
    /// player leaving does, so everything that reads the roster stops seeing it at once.</summary>
    public void RemovePuppet(PlayerTank puppet)
    {
        int seat = Players.IndexOf(puppet);
        if (seat < 0) return;
        puppet.Puppet = false;
        puppet.DamageHandler = null;
        VacateSeat(seat);
        SeatNames.Remove(seat);
    }

    /// <summary>Hurts a craft through the full player damage path: the virus's absorb, the
    /// mote's multiplier, the shield, the hull, the death and its line in the feed.</summary>
    public void HurtCraft(PlayerTank craft, float amount, Vector2? from = null)
        => DamagePlayer(amount, craft, from);

    /// <summary>Hurts a hunter as a round would. <paramref name="by"/> is the seat that gets
    /// the kill, or <see cref="Projectile.NoOwner"/> for nobody's.</summary>
    public void HurtEnemy(EnemyTank enemy, float amount, int by = Projectile.NoOwner)
        => DamageEnemy(enemy, amount, by);

    /// <summary>Hurts one of a squad. The game's own path credits nobody for a soldier — a
    /// round's owner is not carried that far — so a mod naming a seat gets the kill counted
    /// here instead.</summary>
    public void HurtSoldier(EnemySoldier soldier, float amount, Vector2? from = null,
        int by = Projectile.NoOwner)
    {
        bool wasAlive = soldier.Alive;
        DamageSoldier(soldier, amount, from);
        if (wasAlive && !soldier.Alive && by != Projectile.NoOwner) Credit(by);
    }

    /// <summary>
    /// Hurts whichever boss is up — the Crab-Core, the Maw-Core or a rolled one — in the
    /// place a well-aimed round would: the core, the crystal, the body. Returns false when
    /// there was nothing there to hit. For a weapon that struck the monster rather than a
    /// point on it, which is what a blow from outside this game is.
    /// </summary>
    public bool HurtBoss(object boss, float amount, int by = Projectile.NoOwner, Vector2? from = null)
    {
        switch (boss)
        {
            case CrabCore crab when crab.Alive:
                if (crab.DamageCore(amount)) DestroyBoss(crab);
                else Emit(Cue.CoreHit, crab.Position, 1f - crab.CoreFraction);
                return true;

            case MawCore maw when maw.Alive:
                if (maw.DamageCrystal(amount)) DestroyMaw(maw);
                else Emit(Cue.MawHurt, maw.Position, 1f - maw.CrystalFraction);
                return true;

            case ModularBoss rolled when rolled.Alive:
                return StrikeBosses(rolled.Position, rolled.Height + 1f, amount, by,
                    from ?? rolled.Position);
        }
        return false;
    }

    /// <summary>Puts one piece of salvage on the field, as if it had been thrown out of a
    /// pack there. Obeys the same ceiling as everything else lying on the grid.</summary>
    public void PlacePickup(Vector2 at, PickupKind kind) => TossSalvage(at, kind);

    /// <summary>Lays a modded weapon on the grid — <paramref name="weapon"/> is its index in
    /// <see cref="Modding.Mods.Weapons"/>. False for an index nothing is registered at.</summary>
    public bool PlaceWeapon(Vector2 at, int weapon, float height = 0f)
    {
        if (Modding.Mods.Weapon(weapon) is null) return false;
        GroundWeapons.Add(new GroundWeapon(Torus.Wrap(at), weapon, height));
        return true;
    }
}
