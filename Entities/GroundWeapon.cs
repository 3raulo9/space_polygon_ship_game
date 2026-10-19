using System.Numerics;

namespace Unrendered.Entities;

/// <summary>
/// A modded weapon nobody is holding: stood on its point in the grid where it came to rest,
/// or still falling there from wherever it was let go — off a soldier killed thirty metres up
/// a tower, or out of a player's hand mid-swing.
///
/// <para>Not a <see cref="Pickup"/>, deliberately. Salvage is the run's economy and comes with
/// rules a sword must not obey: it can be evicted to make room, it respawns out in the fog,
/// any chassis that drives over it takes it, and it goes into a pack. A sword is none of
/// those — only a SOLDIER can lift one, it goes into a hand rather than a slot, and it stays
/// exactly where it was dropped until somebody comes for it.</para>
/// </summary>
public sealed class GroundWeapon
{
    public Vector2 Position;

    /// <summary>How far the point is off the grid: zero once it has landed and stuck.</summary>
    public float Height;

    /// <summary>Downward speed while it falls, m/s.</summary>
    private float _fall;

    /// <summary>Index into <see cref="Modding.Mods.Weapons"/>.</summary>
    public int Weapon;

    /// <summary>Seconds it has existed, for the glint and the tumble. A local clock on every
    /// machine; never sent.</summary>
    public float Age;

    /// <summary>The seat that just let go of it, which may not take it straight back. Without
    /// this a player who drops a sword at their own feet picks it up again on the next tick,
    /// and the drop key is a key that does nothing.</summary>
    public int RefusedSeat = -1;
    public float RefuseFor;

    /// <summary>Host-side: taken this tick, swept at the end of the walk — the same pattern
    /// salvage uses, so two players reaching one sword on one tick cannot both have it.</summary>
    public bool Taken;

    /// <summary>Client-side: where the host last said it was, eased toward rather than taken
    /// outright so a falling sword glides between snapshots instead of stepping.</summary>
    private float _netHeight;
    private bool _netSeen;

    /// <summary>How close a soldier has to come, on the plane.</summary>
    public const float Radius = 1.4f;

    /// <summary>And how close in height: a soldier hanging off a tower above one has not
    /// walked over it.</summary>
    public const float ReachUp = 3.5f;

    /// <summary>How fast a dropped weapon comes down. Slower than a body falls — it is drawn
    /// tumbling, and a tumble that is over in a third of a second is never seen.</summary>
    public const float Gravity = 22f;

    public GroundWeapon(Vector2 position, int weapon, float height = 0f)
    {
        Position = position;
        Weapon = weapon;
        Height = MathF.Max(0f, height);
        _netHeight = Height;
    }

    public bool Landed => Height <= 0f;

    /// <summary>Host-side: falls, and counts down the refusal.</summary>
    public void Update(float dt)
    {
        Age += dt;
        if (RefuseFor > 0f) RefuseFor = MathF.Max(0f, RefuseFor - dt);
        if (Height <= 0f) return;

        _fall += Gravity * dt;
        Height = MathF.Max(0f, Height - _fall * dt);
        if (Height <= 0f) _fall = 0f;
    }

    /// <summary>Client-side: lays the host's account over this cell.</summary>
    public void NetSet(Vector2 position, float height, int weapon)
    {
        Position = position;
        Weapon = weapon;
        _netHeight = height;
        if (!_netSeen) { Height = height; _netSeen = true; }
    }

    /// <summary>Client-side: the glint's clock, and the ease down onto the host's height.</summary>
    public void Animate(float dt)
    {
        Age += dt;
        float k = 1f - MathF.Exp(-18f * dt);
        Height += (_netHeight - Height) * k;
        if (MathF.Abs(Height - _netHeight) < 0.01f) Height = _netHeight;
    }

    // --- How it stands ------------------------------------------------------------------
    // Worked out of the position rather than rolled and stored, so every machine stands the
    // same sword at the same angle without a byte of it crossing the wire.

    /// <summary>Which way it leans, radians about the vertical.</summary>
    public float LeanHeading => Hash(Position.X * 12.9898f + Position.Y * 78.233f) * MathF.Tau;

    /// <summary>How far off upright, radians — a sword that fell rather than one placed.</summary>
    public float Lean => 0.10f + Hash(Position.X * 39.346f + Position.Y * 11.135f) * 0.22f;

    private static float Hash(float v)
    {
        float s = MathF.Sin(v) * 43758.5453f;
        return s - MathF.Floor(s);
    }
}
