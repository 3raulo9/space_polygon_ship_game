using System.Numerics;

namespace VoidTanks.Core;

/// <summary>
/// The world is a torus: drive far enough in any direction and you come back to
/// where you started. Rather than an infinite plane, play happens on a fixed
/// <see cref="Size"/>×<see cref="Size"/> square whose edges are stitched to their
/// opposite edge — go off the top and you reappear at the bottom, off the right and
/// you reappear at the left.
///
/// Nothing in the game world holds a fixed landmark and the fog swallows everything
/// past <see cref="Config.FogEnd"/>, so as long as the world is comfortably wider than
/// twice that distance the seam is never visible: an entity is only ever drawn once,
/// at its <see cref="NearestImage"/> — the copy of it that sits closest to the viewer
/// across the wrap. All the game has to do is (a) keep positions wrapped into the
/// canonical [-<see cref="Half"/>, <see cref="Half"/>) window and (b) measure
/// distance and direction the short way round via <see cref="Delta"/>.
/// </summary>
public static class Torus
{
    /// <summary>
    /// Side length of the world square, in world units. A multiple of twice the grid
    /// spacing (16) so the checkerboard's parity matches across the seam and the floor
    /// reads as continuous where the world wraps, and well over twice
    /// <see cref="Config.FogEnd"/> (130) so an entity's far copy is always lost in the
    /// fog and can never be seen alongside its near one.
    /// </summary>
    public const float Size = 400f;

    /// <summary>Half the world — the wrap window runs [-Half, Half).</summary>
    public const float Half = Size * 0.5f;

    /// <summary>Folds a single coordinate into the canonical [-Half, Half) window.</summary>
    public static float WrapCoord(float v) => v - Size * MathF.Floor(v / Size + 0.5f);

    /// <summary>Folds a world point into the canonical [-Half, Half)² window.</summary>
    public static Vector2 Wrap(Vector2 p) => new(WrapCoord(p.X), WrapCoord(p.Y));

    /// <summary>The shortest signed difference b−a on the wrapped axis: the way round
    /// that never travels more than <see cref="Half"/>.</summary>
    public static float DeltaCoord(float a, float b)
    {
        float d = b - a;
        return d - Size * MathF.Floor(d / Size + 0.5f);
    }

    /// <summary>The shortest vector from <paramref name="from"/> to <paramref name="to"/>
    /// across the torus — the one everything targeting, aiming and colliding should use
    /// instead of a plain subtraction, so a chase or a shot goes the short way over the
    /// seam rather than the long way across the whole arena.</summary>
    public static Vector2 Delta(Vector2 from, Vector2 to)
        => new(DeltaCoord(from.X, to.X), DeltaCoord(from.Y, to.Y));

    /// <summary>Wrapped distance between two points.</summary>
    public static float Distance(Vector2 a, Vector2 b) => Delta(a, b).Length();

    /// <summary>Wrapped squared distance — for the many checks that compare against a
    /// radius and never need the square root.</summary>
    public static float DistanceSquared(Vector2 a, Vector2 b) => Delta(a, b).LengthSquared();

    /// <summary>
    /// The copy of <paramref name="pos"/> that lies nearest <paramref name="viewer"/>
    /// across the wrap, in absolute coordinates around the viewer. Rendering places
    /// every world thing at its nearest image relative to the player's eye, which is
    /// what makes the seam invisible: something just over the top edge is drawn just
    /// above the player rather than a whole world-width away.
    /// </summary>
    public static Vector2 NearestImage(Vector2 pos, Vector2 viewer)
        => viewer + Delta(viewer, pos);
}
