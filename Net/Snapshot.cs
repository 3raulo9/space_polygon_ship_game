using System.Numerics;
using VoidTanks.Core;
using VoidTanks.Entities;

namespace VoidTanks.Net;

/// <summary>
/// The host's account of the world, written for one recipient and applied on their machine.
///
/// Only the host simulates. Clients drive their own craft for the feel of it and are then
/// told what actually happened — the only arrangement that works here, because the sim leans
/// on <c>Random.Shared</c> in about thirty places and two machines stepping it independently
/// would part company inside a second.
///
/// <para><b>Two packets, not one.</b> The world is sent as a small PLAYERS packet and a
/// separate FIELD packet, every snapshot. This is not tidiness — it is the fix for craft
/// vanishing under load. One combined packet carrying twenty players plus a busy field runs
/// past the size Steam will carry in a single unreliable datagram, and a fragmented unreliable
/// message is dropped whole the instant one fragment is lost. When that happened the remote
/// craft froze or disappeared. Split, the players packet is a few hundred bytes that always
/// fits and always arrives; the field packet is allowed to be lost, and a client that misses
/// one simply keeps the enemies it already had until the next one lands.</para>
///
/// <para>Both are written per recipient and interest-culled: every player (there are at most
/// twenty and the radar has to be honest), but only the enemies and rounds within
/// <see cref="InterestRadius"/> of that player.</para>
/// </summary>
public static class Snapshot
{
    /// <summary>
    /// How far from a player the world is worth describing. Comfortably past the fog, so
    /// nothing is ever culled while it is still on screen.
    /// </summary>
    public const float InterestRadius = 170f;

    private const float PosScale = 64f;
    private const float AngScale = 65536f / MathF.Tau;

    private static short Q(float v, float scale)
        => (short)Math.Clamp(MathF.Round(v * scale), short.MinValue, short.MaxValue);

    private static short QAngle(float radians)
    {
        float a = MathF.IEEERemainder(radians, MathF.Tau);
        return (short)Math.Clamp(MathF.Round(a * AngScale), short.MinValue, short.MaxValue);
    }

    [Flags]
    private enum Mark : byte
    {
        None = 0,
        Alive = 1 << 0,
        Captured = 1 << 1,
        Planted = 1 << 2,
        Rooted = 1 << 3,
        /// <summary>The player driving this seat has dropped; the craft is held in place
        /// until they reconnect. Drawn as a dimmed, frozen ally rather than removed.</summary>
        Away = 1 << 4,
    }

    /// <summary>Bytes per craft: seat, class, flags, x, y, height, heading, pitch, shield,
    /// hyper, lives, ammo.</summary>
    private const int PlayerBytes = 1 + 1 + 1 + 2 + 2 + 2 + 2 + 2 + 2 + 1 + 1 + 1;
    private const int EnemyBytes = 2 + 2 + 2 + 1;
    private const int RoundBytes = 2 + 2 + 2 + 2 + 2 + 1 + 1;

    /// <summary>
    /// The field caps are chosen so the field packet stays inside one ~1200-byte network
    /// datagram — 40 hunters and 48 rounds is 40·7 + 48·12 = 856 bytes plus a short header,
    /// a single unreliable send with a simple loss model rather than a fragmented one that
    /// vanishes whole. The interest cull rarely reaches these near any one player anyway.
    /// </summary>
    private const int MaxEnemies = 40;
    private const int MaxRounds = 48;

    /// <summary>Worst case for a players packet: the header and every seat.</summary>
    public const int MaxPlayerSize = 8 + MatchSettings.MaxSeats * PlayerBytes;

    /// <summary>Worst case for a field packet: the header and the capped field.</summary>
    public const int MaxFieldSize = 8 + MaxEnemies * EnemyBytes + MaxRounds * RoundBytes;

    /// <summary>The larger of the two, so one buffer serves both.</summary>
    public const int MaxSize = MaxPlayerSize > MaxFieldSize ? MaxPlayerSize : MaxFieldSize;

    // --- Players ------------------------------------------------------------------

    /// <summary>
    /// Writes every craft, whatever the range. Small and sent every snapshot — this is the
    /// packet that must never be lost to the field's size, which is the whole reason it is
    /// its own packet.
    /// </summary>
    public static int WritePlayers(World.World world, uint tick, Span<byte> dst)
    {
        int at = 0;
        BitConverter.TryWriteBytes(dst.Slice(at, 4), tick); at += 4;
        dst[at++] = (byte)world.Players.Count;

        foreach (var p in world.Players)
        {
            Mark mark = Mark.None;
            if (p.Alive) mark |= Mark.Alive;
            if (p.Captured) mark |= Mark.Captured;
            if (p.Planted) mark |= Mark.Planted;
            if (p.Rooted) mark |= Mark.Rooted;
            if (p.Away) mark |= Mark.Away;

            dst[at++] = (byte)world.Seat(p);
            dst[at++] = (byte)p.Class;
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
        return at;
    }

    /// <summary>
    /// Lays the players packet over this client's world. Returns the tick, or 0 if the packet
    /// was malformed — a client must never be crashed by a bad packet, only unconvinced by
    /// one. The local craft is deliberately not overwritten: this machine drives it, and
    /// snapping it back to a position from a hundred milliseconds ago would judder in the
    /// player's hands.
    /// </summary>
    public static uint ApplyPlayers(World.World world, ReadOnlySpan<byte> src)
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

                if (seat < 0) continue;
                // Grow the roster to cover any seat the host names. A client only builds seats
                // up to its own at Welcome, so without this a player who joined later — anyone
                // with a higher seat number — is named by every snapshot and created by none,
                // and stays invisible. The placeholders become the right chassis on the class
                // line below.
                while (seat >= world.Players.Count)
                    if (world.AddPlayer() is null) break;
                if (seat >= world.Players.Count) continue;
                if (seat == world.LocalIndex) continue;   // ours to drive, not to be told

                // A client's roster starts as placeholder tanks; the first snapshot that names
                // a seat's real chassis rebuilds it as the right one. Only ever on the frame the
                // class first differs, since a craft's chassis never changes after that.
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
                p.Away = (mark & Mark.Away) != 0;
            }
            return tick;
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return 0u;
        }
    }

    // --- Field --------------------------------------------------------------------

    /// <summary>
    /// Writes the hunters and rounds near <paramref name="forSeat"/>, capped to fit one
    /// datagram. Allowed to be lost: a client that misses it keeps the field it had.
    /// </summary>
    public static int WriteField(World.World world, int forSeat, uint tick, Span<byte> dst)
    {
        Vector2 eye = world.Players[Math.Clamp(forSeat, 0, world.Players.Count - 1)].Position;
        int at = 0;
        BitConverter.TryWriteBytes(dst.Slice(at, 4), tick); at += 4;

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
    /// Rebuilds this client's field from a field packet. A client's enemy list is scenery it
    /// never reasons about, so it is replaced wholesale rather than reconciled — but only when
    /// a packet actually arrives. Miss one and the last field simply stands for another
    /// fiftieth of a second, which is the keep-last that stops the world flickering under loss.
    /// </summary>
    public static uint ApplyField(World.World world, ReadOnlySpan<byte> src)
    {
        try
        {
            int at = 0;
            uint tick = BitConverter.ToUInt32(src.Slice(at, 4)); at += 4;

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
            return 0u;
        }
    }
}
