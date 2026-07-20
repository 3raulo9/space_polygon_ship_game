using System.Numerics;

namespace VoidTanks.Entities;

/// <summary>
/// The shared physical layout of the Maw-Core — how big it is, how high it hangs,
/// where its teeth sit and where the drool falls from. The same role
/// <see cref="CrabRig"/> plays for the Crab-Core, and for the same reason: the
/// renderer draws from it, the entity hit-tests against it, and the digestion
/// cinematic parks the player inside it, so none of the three can drift apart.
///
/// The load-bearing decision in this file is <see cref="CrystalWorldY"/>. The whole
/// enemy exists to make the jump matter: it hangs with its crystal exactly where a
/// bolt fired at the top of a leap rides, so a grounded shot passes harmlessly
/// beneath it and the only way to hurt it is to be in the air. Every other height
/// here is measured off that one, upward to the shell or downward to the teeth, so
/// the model is hung from its own weak spot rather than positioned by eye.
/// </summary>
public static class MawRig
{
    /// <summary>Overall size multiplier. Smaller than the Crab-Core: this thing has
    /// to hang at head height without its shell filling the whole sky.</summary>
    public const float Scale = 1.5f;

    /// <summary>
    /// World height of the crystal's strike band — the exact height a bolt fired at
    /// the apex of a jump is travelling at. <see cref="PlayerTank.JumpApex"/> is where
    /// the craft gets to and <see cref="Projectile.BoltHeight"/> is how far over the
    /// craft the barrel sits, so their sum is where the shot is. Nothing about this
    /// number is chosen; it falls out of the jump.
    /// </summary>
    public static float CrystalWorldY => PlayerTank.JumpApex + Projectile.BoltHeight;

    /// <summary>Planar reach of the crystal's strike zone.</summary>
    public const float HitRadius = 3.4f;

    /// <summary>
    /// Half-height of the strike band. Wide enough to forgive a shot loosed a little
    /// before or after the peak — the timing should be readable, not frame-perfect —
    /// and still far narrower than the gap up from barrel height, so a shot from the
    /// grid can never sneak into it.
    /// </summary>
    public const float HitVertical = 2.2f;

    // --- Where the parts hang, in the body's own local frame (pre-Scale) -------
    // The body pivot is the underside of the carapace: the seam where the Crab-Core's
    // middle tier was cut away. Everything above it is inherited shell, everything
    // below it is the new throat.

    /// <summary>The gem's base above the pivot, matching the crab's own well.</summary>
    public const float CrystalLocalY = 0.5f;

    /// <summary>Where the ring of teeth is socketed, below the pivot.</summary>
    public const float ToothLocalY = -0.85f;

    /// <summary>Radius the teeth stand at — just inside the jaw's flared lip, so they
    /// ring the throat rather than sticking out past the shell.</summary>
    public const float ToothRadius = 1.55f;

    /// <summary>How many teeth are in each ring.</summary>
    public const int ToothCount = 9;

    /// <summary>
    /// World height of the body pivot. Solved backwards off the strike band so the
    /// crystal's base lands on it: the shell hangs wherever it has to for the weak
    /// spot to sit at the top of a jump.
    /// </summary>
    public static float BodyWorldY => CrystalWorldY - CrystalLocalY * Scale;

    /// <summary>World height of the tooth ring — a little under standing eye height,
    /// so walking beneath it puts the teeth right in front of the player's face.</summary>
    public static float ToothWorldY => BodyWorldY + ToothLocalY * Scale;

    /// <summary>World height the drips let go from: the lowest points of the teeth.</summary>
    public static float DripWorldY => ToothWorldY - 1.15f * Scale;

    /// <summary>
    /// Where the swallowed player's <em>eye</em> rides inside the throat, relative to
    /// the body pivot (pre-scale, so negative is below the shell).
    ///
    /// The eye rather than the craft, and that distinction is the whole trick. The
    /// camera sits <see cref="Core.Config.CameraHeight"/> above the craft, so placing
    /// the <em>craft</em> in the gullet puts the eye three units higher — up through
    /// the shell and inside the crystal, where a camera with backface culling off sees
    /// nothing but the gem's interior facets filling the screen as a flat wash. The
    /// hold has to be solved backwards from where the view should end up.
    ///
    /// Set just under the ring of teeth, so looking up gives the throat narrowing away
    /// overhead and the teeth turning around the edge of the frame — which is the shot
    /// the whole set piece exists for.
    /// </summary>
    public const float GulletEyeLocal = -0.6f;

    /// <summary>That eye offset in world units, off the body pivot.</summary>
    public static float GulletEyeOffset => GulletEyeLocal * Scale;

    /// <summary>
    /// How far off the vertical the player has to be for the thing to drop on them.
    /// Tight on purpose: "directly underneath" has to be a place the player can tell
    /// they are standing in and can step out of, not a wide bubble that reads as the
    /// monster simply grabbing whoever is nearby.
    /// </summary>
    public const float StrikeColumn = 3.2f;

    /// <summary>The pose of one tooth in a ring: its bearing round the throat.</summary>
    public static float ToothAngle(int index, float spin)
        => spin + MathF.Tau * index / ToothCount;

    /// <summary>Where a tooth's root sits in the body's local frame, for a ring turned
    /// to <paramref name="spin"/>. Shared so the renderer's teeth and the drips that
    /// run off them come from the same circle.</summary>
    public static Vector3 ToothMount(int index, float spin, float radius)
    {
        float a = ToothAngle(index, spin);
        return new Vector3(MathF.Cos(a) * radius, ToothLocalY, MathF.Sin(a) * radius);
    }
}
