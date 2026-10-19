using System.Numerics;
using Raylib_cs;
using Unrendered.Core;

namespace Unrendered.Modding;

/// <summary>
/// One box of a modded weapon's model, in the weapon's own frame: the fist is the origin,
/// +Y runs out along the blade toward the tip, +X across the edge and +Z through the flat.
/// Metres at the figure's own scale — the same units the soldier's body is built in, so a
/// part that is 0.6 long is a little longer than the figure's forearm.
/// </summary>
public readonly record struct ModPart(Vector3 From, Vector3 To, Color Color);

/// <summary>
/// A melee weapon a mod file added. Pure data: what it does when swung, where it turns up,
/// what it sounds like and what it is made of. The world decides everything else — who can
/// carry one (the SOLDIER), how a swing is aimed, and which of the game's own damage paths a
/// hit is routed through — so a mod cannot make a weapon that breaks a rule the rest of the
/// game keeps.
///
/// <para>Two damage numbers because the game has two scales. Against the field a rifle round
/// is worth 1 (a hunter has 3, an elite 5, a soldier 2); against a craft it is worth 10. A
/// weapon that only had one number would be either useless against hunters or a one-hit kill
/// on a team-mate the moment friendly fire was on.</para>
/// </summary>
public sealed class ModWeapon
{
    /// <summary>The mod file it came from, e.g. "swords".</summary>
    public required string ModId { get; init; }

    /// <summary>Its own id inside that file, e.g. "sword".</summary>
    public required string Id { get; init; }

    /// <summary>What the HUD calls it while it is in somebody's hand.</summary>
    public required string Name { get; init; }

    /// <summary>Against hunters, squads and bosses — the scale a rifle round is 1 on.</summary>
    public float Damage { get; init; }

    /// <summary>Against other craft, and only ever with friendly fire on — the scale a rifle
    /// round is 10 on.</summary>
    public float PlayerDamage { get; init; }

    /// <summary>How far past the body's edge the swing reaches, in metres.</summary>
    public float Reach { get; init; }

    /// <summary>Half the sweep, in radians, either side of where the body faces.</summary>
    public float HalfArc { get; init; }

    /// <summary>Seconds from one swing to the next. Holding FIRE swings at this pace.</summary>
    public float Cooldown { get; init; }

    /// <summary>The most bodies one swing may cut. A blade that cleaved a whole squad would
    /// make every other weapon a worse choice; one that stopped at the first body would make
    /// it useless in exactly the crowd a player is most likely to be swinging into.</summary>
    public int MaxTargets { get; init; }

    /// <summary>How many lie on the grid when a match opens.</summary>
    public int FieldCount { get; init; }

    /// <summary>The chance an enemy soldier drops one when it dies, 0..1.</summary>
    public float SoldierDrop { get; init; }

    public Cue SwingCue { get; init; } = Cue.ThrowWhoosh;
    public Cue HitCue { get; init; } = Cue.ClawSlam;

    public required ModPart[] Model { get; init; }

    /// <summary>How far the model runs out along +Y — the tip. Worked out once at load,
    /// because both the ground pose (which stands it on its point) and the viewmodel (which
    /// has to keep the tip on screen) need it every frame.</summary>
    public float Length { get; init; }

    /// <summary>The colour a hit throws sparks in: the part furthest out toward the tip,
    /// which on anything shaped like a blade is the edge that did the cutting.</summary>
    public Color Spark { get; init; }

    /// <summary>Its place in <see cref="Mods.Weapons"/>, which is also its byte on the wire.
    /// Stable for as long as both machines loaded the same files — which is exactly what the
    /// join check guarantees.</summary>
    public int Index { get; internal set; }

    /// <summary>"swords:sword" — unique across every mod loaded at once.</summary>
    public string Key => ModId + ":" + Id;
}
