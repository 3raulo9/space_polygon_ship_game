namespace VoidTanks.Entities;

/// <summary>
/// What a cinematic does to the camera while it owns the player.
///
/// This game has no camera rig: the eye is built every frame from the player's own
/// position, height and heading, so a set piece that wants to throw the view around
/// has to hand the renderer these four channels and let it fold them in. Both of the
/// game's set pieces — the Crab-Core's seizure and the Maw-Core's digestion — drive
/// exactly the same four, so the renderer takes whichever one is running through
/// this interface rather than knowing about either class.
/// </summary>
public interface ICinematicView
{
    /// <summary>How hard the view should judder, 0..1.</summary>
    float Shake { get; }

    /// <summary>Camera roll in radians — the world tipping off its axis.</summary>
    float Roll { get; }

    /// <summary>Extra vertical aim on the eye's look direction: positive drags the
    /// view up (into a crystal, or up a throat), negative down at the grid.</summary>
    float Pitch { get; }

    /// <summary>0..1 full-screen wash in the monster's own light — the only way to
    /// show a first-person player lit up by something is to flood their view.</summary>
    float Glow { get; }
}
