using System.Numerics;
using VoidTanks.Core;
using VoidTanks.Entities;

namespace VoidTanks.Net;

/// <summary>
/// The host's account of the world, written for one recipient and applied on their machine.
///
/// Only the host simulates. Clients drive their own craft for the feel of it and are then
/// told what actually happened — which is the only arrangement that works here, because the
/// sim leans on <c>Random.Shared</c> in about thirty places and two machines stepping it
/// independently would part company inside a second.
///
/// <para><b>Written per recipient, not broadcast.</b> Twenty players on one torus is a lot of
/// state, and a naive full snapshot to nineteen peers at 20Hz is somewhere around eight
/// megabits a second off a home connection — more upstream than most hosts have. So each
/// recipient is sent what is near <em>them</em>: every player (there are at most twenty and
/// you want the radar honest), but only the enemies and rounds inside
/// <see cref="InterestRadius"/>. On a 400-wide torus that is most of the traffic gone.</para>
/// </summary>
public static class Snapshot
{
    /// <summary>
    /// How far from a player the world is worth describing. Comfortably past the fog, so
    /// nothing is ever culled while it is still on screen — the moment a player could see a
    /// thing pop in is the moment this number is too small.
    /// </summary>
    public const float InterestRadius = 170f;

    /// <summary>
    /// Positions ride as 16-bit fixed point at a 64th of a unit. The torus is 400 across, so
    /// ±200 needs 12,800 steps of that — well inside a short, with room to spare for a craft
    /// that has wandered a little past the seam before being wrapped.
    /// </summary>
    private const float PosScale = 64f;

    /// <summary>Angles as 16-bit turns: a full circle over 65,536 steps, which is finer than
    /// any player can aim and costs two bytes instead of four.</summary>
    private const float AngScale = 65536f / MathF.Tau;

    private static short Q(float v, float scale)
        => (short)Math.Clamp(MathF.Round(v * scale), short.MinValue, short.MaxValue);

    private static short QAngle(float radians)
    {
        // Fold to [-π, π) first so the quantised value wraps the same way the angle does.
        float a = MathF.IEEERemainder(radians, MathF.Tau);
        return (short)Math.Clamp(MathF.Round(a * AngScale), short.MinValue, short.MaxValue);
    }

    // --- Flags packed into one byte per craft -------------------------------------
    [Flags]
    private enum Mark : byte
    {
        None = 0,
        Alive = 1 << 0,
        Captured = 1 << 1,
        Planted = 1 << 2,
        Rooted = 1 << 3,
    }

    /// <summary>Bytes per craft on the wire: seat, class, flags, x, y, height, heading,
    /// pitch, shield, hyper, lives, ammo.</summary>
    private const int PlayerBytes = 1 + 1 + 1 + 2 + 2 + 2 + 2 + 2 + 2 + 1 + 1 + 1;

    /// <summary>Bytes per hunter: x, y, heading, flags.</summary>
    private const int EnemyBytes = 2 + 2 + 2 + 1;

    /// <summary>Bytes per round in flight: x, y, height, vx, vy, owner, flags.</summary>
    private const int RoundBytes = 2 + 2 + 2 + 2 + 2 + 1 + 1;

    /// <summary>Worst case for one packet — every seat, and as much of the field as the
    /// interest radius can hold. Sized once so the session can keep a single buffer.</summary>
    public const int MaxSize = 8
        + MatchSettings.MaxSeats * PlayerBytes
        + 64 * EnemyBytes
        + 96 * RoundBytes;

    private const int MaxEnemies = 64;
    private const int MaxRounds = 96;

    /// <summary>
    /// Writes the world as it should look to <paramref name="forSeat"/>. Returns how many
    /// bytes were used. The tick number rides along so a client can drop a packet that
    /// arrived out of order rather than snapping backwards to it.
    /// </summary>
    public static int Write(World.World world, int forSeat, uint tick, Span<byte> dst)
    {
        Vector2 eye = world.Players[Math.Clamp(forSeat, 0, world.Players.Count - 1)].Position;
        int at = 0;

        BitConverter.TryWriteBytes(dst.Slice(at, 4), tick); at += 4;
        dst[at++] = (byte)world.Players.Count;

        // Every craft, whatever the range. Twenty of them is 260 bytes and the radar would
        // lie without them — a team-mate blinking off the map at the fog line is worse than
        // the handful of bytes it saves.
        foreach (var p in world.Players)
        {
            Mark mark = Mark.None;
            if (p.Alive) mark |= Mark.Alive;
            if (p.Captured) mark |= Mark.Captured;
            if (p.Planted) mark |= Mark.Planted;
            if (p.Rooted) mark |= Mark.Rooted;

            dst[at++] = (byte)world.Seat(p);
            dst[at++] = (byte)p.Class;   // so the client knows which chassis to draw
            dst[at++] = (byte)mark;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(p.Position.X, PosScale)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(p.Position.Y, PosScale)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(p.Height, PosScale)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), QAngle(p.Heading)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), QAngle(p.Pitch)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(p.Shield, 4f)); at += 2;
            dst[at++] = (byte)Math.Clamp(p.HyperFraction * 255f, 0f, 255f);
            dst[at++] = (byte)Math.Clamp(p.Lives, 0, 255);
            dst[at++] = (byte)Math.Clamp(p.Ammo, 0, 255);
        }

        // The hunters near this player, and only those.
        int countAt = at++;
        int written = 0;
        foreach (var e in world.Enemies)
        {
            if (written >= MaxEnemies) break;
            if (!e.Alive) continue;
            if (Torus.DistanceSquared(e.Position, eye) > InterestRadius * InterestRadius) continue;

            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(e.Position.X, PosScale)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(e.Position.Y, PosScale)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), QAngle(e.Heading)); at += 2;
            dst[at++] = (byte)(e.IsElite ? 1 : 0);
            written++;
        }
        dst[countAt] = (byte)written;

        // And the rounds in the air near them. These are the most numerous thing on the
        // field and the least worth sending at range: a bolt a hundred and eighty units
        // away is a pixel nobody can see.
        countAt = at++;
        written = 0;
        foreach (var r in world.Projectiles)
        {
            if (written >= MaxRounds) break;
            if (!r.Active) continue;
            if (Torus.DistanceSquared(r.Position, eye) > InterestRadius * InterestRadius) continue;

            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(r.Position.X, PosScale)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(r.Position.Y, PosScale)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(r.Height, PosScale)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(r.Velocity.X, 8f)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(r.Velocity.Y, 8f)); at += 2;
            dst[at++] = unchecked((byte)(sbyte)Math.Clamp(r.Owner, -128, 127));
            byte f = 0;
            if (r.IsLaser) f |= 1;
            if (r.IsGrenade) f |= 2;
            if (r.IsRocket) f |= 4;
            if (r.IsTracer) f |= 8;
            dst[at++] = f;
            written++;
        }
        dst[countAt] = (byte)written;
        return at;
    }

    /// <summary>
    /// Lays the host's account over this client's world. Returns the tick it described, or
    /// zero if the packet was malformed — a client must never be crashed by a bad packet,
    /// only unconvinced by one.
    ///
    /// The local craft is deliberately <em>not</em> overwritten: this machine has been
    /// driving it every tick and snapping it back to a position from a hundred milliseconds
    /// ago would make it judder in the player's hands. It drifts from the host's truth
    /// instead, which is the trade every game of this shape makes and what proper
    /// reconciliation would tighten later.
    /// </summary>
    public static uint Apply(World.World world, ReadOnlySpan<byte> src)
    {
        try
        {
            int at = 0;
            uint tick = BitConverter.ToUInt32(src.Slice(at, 4)); at += 4;
            int players = src[at++];

            for (int i = 0; i < players; i++)
            {
                int seat = src[at++];
                var chassis = (PlayerClass)src[at++];
                var mark = (Mark)src[at++];
                float x = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float y = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float h = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float head = BitConverter.ToInt16(src.Slice(at, 2)) / AngScale; at += 2;
                float pitch = BitConverter.ToInt16(src.Slice(at, 2)) / AngScale; at += 2;
                float shield = BitConverter.ToInt16(src.Slice(at, 2)) / 4f; at += 2;
                float hyper = src[at++] / 255f;
                int lives = src[at++];
                int ammo = src[at++];

                // Seats arrive as the host numbered them; anything past the end of our roster
                // is a player we have not been told about yet, so it waits for the next one.
                if (seat < 0 || seat >= world.Players.Count) continue;
                if (seat == world.LocalIndex) continue;   // ours to drive, not to be told

                // A client's roster starts as placeholder tanks; the host is the authority on
                // who is actually driving what. The first snapshot that names a seat's real
                // chassis rebuilds that craft as the right one — a fish where a tank stood in.
                // Cheap, and only ever on the frame the class first differs, since a craft's
                // chassis never changes after that.
                if (world.Players[seat].Class != chassis)
                    world.ReplacePlayer(seat, chassis);

                PlayerTank p = world.Players[seat];
                p.Position = Torus.Wrap(new Vector2(x, y));
                p.Height = h;
                p.Heading = head;
                p.Pitch = pitch;
                p.Shield = shield;
                p.Hyper = hyper * p.MaxHyper;
                p.Lives = lives;
                p.Ammo = ammo;
                p.Captured = (mark & Mark.Captured) != 0;
            }

            // The field. Rebuilt wholesale rather than reconciled: a client's enemy list is
            // scenery it never reasons about, so there is nothing to preserve across a packet
            // and matching them up by identity would cost more than it bought.
            int enemies = src[at++];
            world.Enemies.Clear();
            for (int i = 0; i < enemies; i++)
            {
                float x = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float y = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float head = BitConverter.ToInt16(src.Slice(at, 2)) / AngScale; at += 2;
                bool elite = src[at++] != 0;
                world.Enemies.Add(new EnemyTank(Torus.Wrap(new Vector2(x, y)), elite)
                { Heading = head });
            }

            int rounds = src[at++];
            world.BeginAdoptRounds();
            for (int i = 0; i < rounds; i++)
            {
                float x = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float y = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float h = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float vx = BitConverter.ToInt16(src.Slice(at, 2)) / 8f; at += 2;
                float vy = BitConverter.ToInt16(src.Slice(at, 2)) / 8f; at += 2;
                int owner = unchecked((sbyte)src[at++]);
                byte flags = src[at++];
                world.AdoptRound(Torus.Wrap(new Vector2(x, y)), h, new Vector2(vx, vy), owner, flags);
            }
            return tick;
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            // Truncated or corrupt. Keep the world we had and wait for the next packet.
            //
            // Both exception types, because a span answers a bad read two different ways:
            // the indexer throws IndexOutOfRange and Slice throws ArgumentOutOfRange. Missing
            // one of them means a single malformed packet takes the client down, which is a
            // thing a stranger on the internet gets to decide.
            return 0u;
        }
    }
}
