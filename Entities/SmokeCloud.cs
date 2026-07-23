using System.Numerics;

namespace VoidTanks.Entities;

/// <summary>
/// A bank of screening smoke vented from a TANK's dischargers. The heavy chassis cannot dodge
/// up and off the grid the way everything else does, so instead of leaving the enemy's line of
/// fire it deletes the enemy's line of sight: while the segment from a hunter to the player runs
/// through the cloud, that hunter can't get a shot away, and an enemy round that flies into the
/// murk dies in it. A cloud blooms to full over a breath, holds, then thins out.
///
/// Deliberately a plain sim object — position, age and a size envelope, nothing else. The world
/// owns the torus geometry of the line-of-sight test (it has to bring shooters and clouds into a
/// common image across the wrap), and the renderer reads <see cref="Radius"/> and
/// <see cref="Density"/> to draw the thing. Keeping it dumb is what lets the sim stay testable
/// without a screen, the same bargain every other entity here strikes.
/// </summary>
public sealed class SmokeCloud
{
    public Vector2 Position;
    public float Age;
    public readonly float Life;
    public readonly float MaxRadius;

    public SmokeCloud(Vector2 at, float life, float maxRadius)
    {
        Position = at;
        Life = life;
        MaxRadius = maxRadius;
    }

    public bool Active => Age < Life;

    public void Update(float dt) => Age += dt;

    /// <summary>
    /// How thick the screen is right now, 0..1: a quick bloom in over the first sixth of its
    /// life, a long hold at full, then a fade over the last third. Both the drawn opacity and
    /// the drawn radius ride this, so the cloud visibly swells up and dissipates rather than
    /// snapping into and out of existence.
    /// </summary>
    public float Density
    {
        get
        {
            float t = Age / Life;
            if (t < 0.15f) return t / 0.15f;
            if (t > 0.7f) return MathF.Max(0f, 1f - (t - 0.7f) / 0.3f);
            return 1f;
        }
    }

    /// <summary>Current planar reach of the murk. Grows from a stub as it blooms toward its
    /// full radius, so a freshly popped cloud doesn't instantly blind half the arena.</summary>
    public float Radius => MaxRadius * (0.4f + 0.6f * Density);

    /// <summary>
    /// Below this density the cloud is too thin — too newly lit or too far gone — to hide
    /// behind or to swallow a round. The world checks it before spending geometry on the
    /// line-of-sight test.
    /// </summary>
    public const float BlockingDensity = 0.35f;

    /// <summary>Whether the screen is currently thick enough to break sight lines at all.</summary>
    public bool Opaque => Density >= BlockingDensity;
}
