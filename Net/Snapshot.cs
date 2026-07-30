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
    /// hyper, lives, ammo, virus-host, virus-decay. The last two are meaningful only on a VIRUS
    /// seat (0/0 otherwise) and are what let every other machine draw the body a mote has seized
    /// rather than a permanent naked cloud — the whole reason the class was invisible in play.</summary>
    private const int PlayerBytes = 1 + 1 + 1 + 2 + 2 + 2 + 2 + 2 + 2 + 1 + 1 + 1 + 1 + 1;
    private const int EnemyBytes = 1 + 2 + 2 + 2 + 1;   // id, x, y, heading, elite
    private const int RoundBytes = 2 + 2 + 2 + 2 + 2 + 1 + 1;

    /// <summary>
    /// The field caps are chosen so the field packet stays inside one ~1200-byte network
    /// datagram — 40 hunters and 48 rounds is 40·7 + 48·12 = 856 bytes plus a short header,
    /// a single unreliable send with a simple loss model rather than a fragmented one that
    /// vanishes whole. The interest cull rarely reaches these near any one player anyway.
    /// </summary>
    private const int MaxEnemies = 40;
    private const int MaxRounds = 48;

    /// <summary>How many squad members one bosses packet carries. A squad is small; this caps
    /// the rare pile-up so the packet stays inside a datagram (24·12 = 288 bytes).</summary>
    private const int MaxSoldiers = 24;

    /// <summary>Worst case for a players packet: the header and every seat.</summary>
    public const int MaxPlayerSize = 8 + MatchSettings.MaxSeats * PlayerBytes;

    /// <summary>Worst case for a field packet: the header, the capped field, and the salvage.</summary>
    public const int MaxFieldSize = 8 + MaxEnemies * EnemyBytes + MaxRounds * RoundBytes
                                  + 1 + MaxPickups * 5;

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

            // The virus's worn body and how far it has rotted. Zero on every other class, and on
            // an exposed mote (HostKind.None), which the reader treats as "draw the mote".
            dst[at++] = (byte)(p.Virus?.HostKind ?? VirusHost.None);
            dst[at++] = (byte)Math.Clamp((p.Virus?.Integrity ?? 0f) * 255f, 0f, 255f);
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
                var hostKind = (VirusHost)src[at++];
                float hostDecay = src[at++] / 255f;

                if (seat < 0) continue;
                // Grow the roster to cover any seat the host names. A client only builds seats
                // up to its own at Welcome, so without this a player who joined later — anyone
                // with a higher seat number — is named by every snapshot and created by none,
                // and stays invisible. The placeholders become the right chassis on the class
                // line below.
                while (seat >= world.Players.Count)
                    if (world.AddPlayer() is null) break;
                if (seat >= world.Players.Count) continue;

                // Our own craft. This machine predicts it for instant control, so its
                // TRANSFORM is reconciled (eased toward the host), not snapped — but its
                // STATUS is the host's to decide, and applying it here is what finally lets a
                // client's own craft take shield damage, lose a life, die, and be seized on
                // its own screen. Before this the local seat was skipped whole, so it ignored
                // the host entirely and was an untouchable ghost that could never be hurt or
                // grabbed. The virus's worn body is left to the client, which drains its own
                // mote locally (see World.StepForTest's client path).
                if (seat == world.LocalIndex)
                {
                    PlayerTank me = world.Players[seat];
                    me.Shield = shield;
                    me.Lives = lives;
                    me.Ammo = ammo;
                    me.Hyper = hyper * me.MaxHyper;
                    bool held = (mark & Mark.Captured) != 0;
                    me.Captured = held;
                    me.Away = (mark & Mark.Away) != 0;
                    world.ReconcileLocal(Torus.Wrap(new Vector2(x, y)), h, head, pitch,
                        follow: held || me.Away);
                    continue;
                }

                // A client's roster starts as placeholder tanks; the first snapshot that names
                // a seat's real chassis rebuilds it as the right one. Only ever on the frame the
                // class first differs, since a craft's chassis never changes after that.
                if (world.Players[seat].Class != chassis)
                    world.ReplacePlayer(seat, chassis);

                PlayerTank p = world.Players[seat];
                // Transform is eased, not snapped: hand it to the craft as a target the client's
                // step glides toward (snapped on first sight), so a team-mate updated 20 times a
                // second moves smoothly instead of strobing. Status below is taken outright.
                p.NetTarget(Torus.Wrap(new Vector2(x, y)), h, head, pitch);
                p.Shield = shield;
                p.Hyper = hyper * p.MaxHyper;
                p.Lives = lives;
                p.Ammo = ammo;
                p.Captured = (mark & Mark.Captured) != 0;
                p.Away = (mark & Mark.Away) != 0;

                // Drive the remote virus's worn body and rot so this machine draws the host it
                // has seized — a hunter, a soldier, a boss — rather than the naked mote it drew
                // for every virus before. Enum.IsDefined guards a malformed byte from becoming a
                // bogus host. Null on any non-virus seat, where the two bytes are just zeroes.
                if (p.Virus is { } mote && Enum.IsDefined(hostKind))
                    mote.NetSet(hostKind, hostDecay);
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

            // A stable id so the client can match this same hunter across packets and interpolate
            // it. Assigned the first time it is ever written; a byte, safe because only a handful
            // are alive and in range at once, so two live ones never share the low byte.
            if (e.NetId == 0) e.NetId = world.NextNetId();
            dst[at++] = (byte)e.NetId;
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

        // The salvage on the grid — few, static, and never culled, so a client sees every cell
        // and shard the host has out. Position and kind only; the bob and spin animate locally.
        int pkAt = at++;
        int pk = 0;
        foreach (var p in world.Pickups)
        {
            if (pk >= MaxPickups) break;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(p.Position.X, PosScale)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(p.Position.Y, PosScale)); at += 2;
            dst[at++] = (byte)p.Kind;
            pk++;
        }
        dst[pkAt] = (byte)pk;
        return at;
    }

    /// <summary>How many pickups a field packet carries — the world's own cap.</summary>
    private const int MaxPickups = 8;

    // --- Structures ---------------------------------------------------------------

    /// <summary>
    /// How many damaged buildings one packet describes. The interest cull rarely reaches
    /// this — a player has to have levelled twenty towers inside one fog radius — and it keeps
    /// the packet inside a datagram (20·9 = 180 bytes plus a header).
    /// </summary>
    private const int MaxStructures = 20;

    /// <summary>Bytes per building: index, flags, and the standing-cell mask.</summary>
    private const int StructureBytes = 2 + 1 + World.Fracture.MaskBytes;

    /// <summary>Worst case for a structures packet.</summary>
    public const int MaxStructureSize = 8 + MaxStructures * StructureBytes;

    [Flags]
    private enum StructMark : byte
    {
        None = 0,
        /// <summary>Cut, and on its way down. Stops blocking; an arch starts its topple.</summary>
        Falling = 1 << 0,
        /// <summary>Finished. The lot is cleared and the field should let it go.</summary>
        Gone = 1 << 1,
        /// <summary>A tower with a chunk model — the mask that follows is meaningful.</summary>
        Fractured = 1 << 2,
    }

    /// <summary>
    /// Writes the damaged buildings near <paramref name="forSeat"/>: which cells of each cut
    /// tower are still standing, and whether it is coming down or already gone.
    ///
    /// This is the one thing about the world two players genuinely disagreed about. The city's
    /// <em>layout</em> has always been identical everywhere — it is generated from a fixed seed
    /// — but nothing carried the <em>damage</em>, so a tower one player cut down with a beam
    /// still stood on everybody else's screen, complete with collision. Undamaged buildings are
    /// never sent: every machine already generated them.
    ///
    /// Keep-last and re-sent every snapshot while in range, exactly like the field, so a lost
    /// packet heals itself and a client that walks up on a ruin it has never heard about is
    /// told the moment it comes into range.
    /// </summary>
    /// <returns>Bytes written, or 0 when there is nothing damaged near this client at all —
    /// which is most of a match, and is not worth a packet.</returns>
    public static int WriteStructures(World.World world, int forSeat, uint tick, Span<byte> dst)
    {
        Vector2 eye = world.Players[Math.Clamp(forSeat, 0, world.Players.Count - 1)].Position;
        int at = 0;
        BitConverter.TryWriteBytes(dst.Slice(at, 4), tick); at += 4;
        int countAt = at++;
        int n = 0;

        // The damaged buildings still standing, then the lots already cleared. Two lists
        // rather than one iterator: this runs per client per snapshot, and a walk of seventy
        // structures should not also be an allocation.
        WriteSome(world.Structures, ref at, ref n, eye, dst);
        WriteSome(world.RazedStructures, ref at, ref n, eye, dst);

        dst[countAt] = (byte)n;
        return n == 0 ? 0 : at;
    }

    private static void WriteSome(IReadOnlyList<World.Structure> from, ref int at, ref int n,
        Vector2 eye, Span<byte> dst)
    {
        for (int i = 0; i < from.Count; i++)
        {
            if (n >= MaxStructures) return;
            World.Structure s = from[i];
            if (!s.Damaged) continue;
            if (Torus.DistanceSquared(s.Position, eye) > InterestRadius * InterestRadius) continue;

            StructMark mark = StructMark.None;
            if (s.Falling) mark |= StructMark.Falling;
            if (s.Gone) mark |= StructMark.Gone;
            if (s.Fracture != null) mark |= StructMark.Fractured;

            BitConverter.TryWriteBytes(dst.Slice(at, 2), (ushort)s.Index); at += 2;
            dst[at++] = (byte)mark;
            Span<byte> mask = dst.Slice(at, World.Fracture.MaskBytes); at += World.Fracture.MaskBytes;
            if (s.Fracture is { } f) f.WriteMask(mask);
            else mask.Clear();
            n++;
        }
    }

    /// <summary>Lays a structures packet over this client's skyline. A malformed one leaves the
    /// city as it was rather than throwing — same contract as every other apply here.</summary>
    public static void ApplyStructures(World.World world, ReadOnlySpan<byte> src)
    {
        try
        {
            int at = 0;
            at += 4;   // tick — keep-last, so no ordering guard
            int n = src[at++];
            for (int i = 0; i < n; i++)
            {
                int index = BitConverter.ToUInt16(src.Slice(at, 2)); at += 2;
                var mark = (StructMark)src[at++];
                ReadOnlySpan<byte> mask = src.Slice(at, World.Fracture.MaskBytes);
                at += World.Fracture.MaskBytes;
                world.AdoptStructure(index,
                    falling: (mark & StructMark.Falling) != 0,
                    gone: (mark & StructMark.Gone) != 0,
                    fractured: (mark & StructMark.Fractured) != 0,
                    mask);
            }
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
        }
    }

    // --- Rigs: the transient combat light -----------------------------------------

    /// <summary>How many seats one rigs packet describes. Interest-culled, and these are the
    /// craft close enough to see the light off — well under this in any real fight.</summary>
    private const int MaxRigSeats = 8;

    /// <summary>Worst case for a rigs packet: header, then every described seat's cables,
    /// stolen lance and charged beam.</summary>
    public const int MaxRigSize = 8 + MaxRigSeats * (2 + 14 + 1 + VirusRig.MaxShafts * 10 + 12);

    [Flags]
    private enum RigMark : byte
    {
        None = 0,
        /// <summary>Two grapple cables out — a SOLDIER's, or the rig a VIRUS is wearing.</summary>
        Cables = 1 << 0,
        /// <summary>The VIRUS's stolen lance, mid-break.</summary>
        Shafts = 1 << 1,
        /// <summary>The SPIDER's lance: gathering, or burning.</summary>
        Beam = 1 << 2,
    }

    /// <summary>
    /// Writes the transient combat light around <paramref name="forSeat"/> — cables, the
    /// virus's stolen lance shafts, the spider's charged beam. None of it is state anything
    /// else reads; it is purely what a fight <em>looks</em> like, and it was all host-local, so
    /// a team-mate cutting a street in half with a lance did it invisibly on everyone else's
    /// screen. Unreliable and keep-nothing: a shaft burns for half a second and the next packet
    /// supersedes this one.
    /// </summary>
    /// <returns>Bytes written, or 0 when nothing near this client is throwing any light —
    /// which is most of the time, and is not worth a packet.</returns>
    public static int WriteRigs(World.World world, int forSeat, uint tick, Span<byte> dst)
    {
        Vector2 eye = world.Players[Math.Clamp(forSeat, 0, world.Players.Count - 1)].Position;
        int at = 0;
        BitConverter.TryWriteBytes(dst.Slice(at, 4), tick); at += 4;
        int countAt = at++;
        int n = 0;

        for (int seat = 0; seat < world.Players.Count; seat++)
        {
            if (n >= MaxRigSeats) break;
            PlayerTank p = world.Players[seat];
            if (Torus.DistanceSquared(p.Position, eye) > InterestRadius * InterestRadius) continue;

            SoldierRig? rig = p.Rig;
            bool cables = rig is { } r && (r.Left.Out || r.Right.Out);
            bool shafts = p.Virus is { } v && AnyShaft(v);
            bool beam = p.Spider is { } sp && (sp.Charging || sp.BeamActive);
            if (!cables && !shafts && !beam) continue;

            RigMark mark = RigMark.None;
            if (cables) mark |= RigMark.Cables;
            if (shafts) mark |= RigMark.Shafts;
            if (beam) mark |= RigMark.Beam;

            dst[at++] = (byte)seat;
            dst[at++] = (byte)mark;

            if (cables)
            {
                WriteHook(rig!.Left, dst, ref at);
                WriteHook(rig.Right, dst, ref at);
            }

            if (shafts)
            {
                VirusRig mote = p.Virus!;
                int shaftCountAt = at++;
                int ns = 0;
                for (int i = 0; i < mote.Shafts.Length; i++)
                {
                    ref readonly var sh = ref mote.Shafts[i];
                    if (sh.Life <= 0f) continue;
                    WriteVec(dst, ref at, sh.Origin);
                    WriteDir(dst, ref at, sh.Dir);
                    dst[at++] = (byte)Math.Clamp(sh.Life / VirusRig.LanceBurnTime * 255f, 0f, 255f);
                    ns++;
                }
                dst[shaftCountAt] = (byte)ns;
            }

            if (beam)
            {
                SpiderWeapon lance = p.Spider!;
                // Where the shaft was loosed from while it burns, or the live muzzle while the
                // meter fills — the same choice the renderer makes for the local craft, made
                // once here so a client does not have to reason about it.
                Vector3 origin = lance.BeamActive ? lance.BeamOrigin : MuzzleOf(p);
                Vector3 dir = lance.BeamActive ? lance.BeamDirection : p.Forward3;
                WriteVec(dst, ref at, origin);
                WriteDir(dst, ref at, dir);
                dst[at++] = (byte)Math.Clamp(
                    (lance.BeamActive ? lance.BeamPower : lance.ChargeFraction) * 255f, 0f, 255f);
                dst[at++] = (byte)Math.Clamp(lance.ChargeFraction * 255f, 0f, 255f);
                // -1 while merely charging, which the reader turns back into "no shaft yet".
                dst[at++] = lance.BeamActive
                    ? (byte)Math.Clamp(lance.BeamProgress * 254f, 0f, 254f) : (byte)255;
            }

            n++;
        }

        dst[countAt] = (byte)n;
        return n == 0 ? 0 : at;
    }

    private static bool AnyShaft(VirusRig v)
    {
        for (int i = 0; i < v.Shafts.Length; i++)
            if (v.Shafts[i].Life > 0f) return true;
        return false;
    }

    private static Vector3 MuzzleOf(PlayerTank p)
    {
        Vector2 muzzle = p.Position + p.Forward * SpiderWeapon.MuzzleForward;
        return new Vector3(muzzle.X, SpiderWeapon.MuzzleHeight + p.Height, muzzle.Y);
    }

    private static void WriteHook(GrappleHook h, Span<byte> dst, ref int at)
    {
        dst[at++] = (byte)h.State;
        BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(h.Tip.X, PosScale)); at += 2;
        BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(h.Tip.Y, PosScale)); at += 2;
        BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(h.TipY, PosScale)); at += 2;
    }

    private static void WriteVec(Span<byte> dst, ref int at, Vector3 v)
    {
        BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(v.X, PosScale)); at += 2;
        BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(v.Y, PosScale)); at += 2;
        BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(v.Z, PosScale)); at += 2;
    }

    /// <summary>A unit direction as three signed bytes. A beam a hundred metres long lands
    /// within a few centimetres of where it was aimed at this resolution, which is far below
    /// what a 320×240 frame can show.</summary>
    private static void WriteDir(Span<byte> dst, ref int at, Vector3 d)
    {
        if (d.LengthSquared() > 1e-6f) d = Vector3.Normalize(d);
        dst[at++] = unchecked((byte)(sbyte)Math.Clamp(MathF.Round(d.X * 127f), -127f, 127f));
        dst[at++] = unchecked((byte)(sbyte)Math.Clamp(MathF.Round(d.Y * 127f), -127f, 127f));
        dst[at++] = unchecked((byte)(sbyte)Math.Clamp(MathF.Round(d.Z * 127f), -127f, 127f));
    }

    private static Vector3 ReadVec(ReadOnlySpan<byte> src, ref int at)
    {
        float x = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
        float y = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
        float z = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
        return new Vector3(x, y, z);
    }

    private static Vector3 ReadDir(ReadOnlySpan<byte> src, ref int at)
    {
        float x = unchecked((sbyte)src[at++]) / 127f;
        float y = unchecked((sbyte)src[at++]) / 127f;
        float z = unchecked((sbyte)src[at++]) / 127f;
        var d = new Vector3(x, y, z);
        return d.LengthSquared() > 1e-6f ? Vector3.Normalize(d) : new Vector3(0f, 0f, 1f);
    }

    /// <summary>Lays a rigs packet over this client's remote craft. Purely cosmetic — nothing
    /// read here decides anything — so a malformed packet costs a frame of light and no more.</summary>
    public static void ApplyRigs(World.World world, ReadOnlySpan<byte> src)
    {
        try
        {
            int at = 0;
            at += 4;   // tick — keep-nothing, so no ordering guard
            int n = src[at++];
            world.BeginAdoptRigs();
            for (int i = 0; i < n; i++)
            {
                int seat = src[at++];
                var mark = (RigMark)src[at++];

                if ((mark & RigMark.Cables) != 0)
                {
                    for (int h = 0; h < 2; h++)
                    {
                        var state = (HookState)src[at++];
                        float tx = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                        float ty = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                        float th = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                        world.AdoptHook(seat, right: h == 1, state,
                            Torus.Wrap(new Vector2(tx, ty)), th);
                    }
                }

                if ((mark & RigMark.Shafts) != 0)
                {
                    int ns = src[at++];
                    for (int s = 0; s < ns; s++)
                    {
                        Vector3 origin = ReadVec(src, ref at);
                        Vector3 dir = ReadDir(src, ref at);
                        float life = src[at++] / 255f * VirusRig.LanceBurnTime;
                        world.AdoptShaft(seat, s, origin, dir, life);
                    }
                    world.EndAdoptShafts(seat, ns);
                }

                if ((mark & RigMark.Beam) != 0)
                {
                    Vector3 origin = ReadVec(src, ref at);
                    Vector3 dir = ReadDir(src, ref at);
                    float power = src[at++] / 255f;
                    float charge = src[at++] / 255f;
                    byte pb = src[at++];
                    world.AdoptBeam(seat, origin, dir, power, charge,
                        progress: pb == 255 ? -1f : pb / 254f);
                }
            }
            world.EndAdoptRigs();
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            world.EndAdoptRigs();
        }
    }

    // --- Bosses -------------------------------------------------------------------

    /// <summary>
    /// Writes the Crab-Core and Maw-Core near <paramref name="forSeat"/>, if any. Bosses are
    /// singletons, so this is tiny — a flags byte and, for each present-and-in-range boss, its
    /// position, phase and integrity — but it is its own packet so a client can be shown a
    /// boss the field packet's interest cull would otherwise have no room to mention.
    /// </summary>
    public static int WriteBosses(World.World world, int forSeat, uint tick, Span<byte> dst)
    {
        Vector2 eye = world.Players[Math.Clamp(forSeat, 0, world.Players.Count - 1)].Position;
        int at = 0;
        BitConverter.TryWriteBytes(dst.Slice(at, 4), tick); at += 4;
        int flagsAt = at++;
        byte flags = 0;

        if (world.Boss is { Alive: true } b
            && Torus.DistanceSquared(b.Position, eye) <= InterestRadius * InterestRadius)
        {
            flags |= 1;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(b.Position.X, PosScale)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(b.Position.Y, PosScale)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), QAngle(b.Heading)); at += 2;
            dst[at++] = (byte)b.Phase;
            dst[at++] = (byte)Math.Clamp(b.CoreFraction * 255f, 0f, 255f);
            // The seizure it is playing out, if any, so an onlooker sees it reach out and club a
            // held player. Zero the rest of the time, which the puppet reads as "no hold".
            dst[at++] = (byte)Math.Clamp(b.GrabArm * 255f, 0f, 255f);
            dst[at++] = (byte)Math.Clamp(b.StrikeArm * 255f, 0f, 255f);
            dst[at++] = (byte)Math.Clamp(b.SeizureGlow * 255f, 0f, 255f);
        }

        if (world.Maw is { Alive: true } m
            && Torus.DistanceSquared(m.Position, eye) <= InterestRadius * InterestRadius)
        {
            flags |= 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(m.Position.X, PosScale)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(m.Position.Y, PosScale)); at += 2;
            dst[at++] = (byte)m.Phase;
            dst[at++] = (byte)Math.Clamp(m.CrystalFraction * 255f, 0f, 255f);
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(m.BodyY, PosScale)); at += 2;
            // The jaw, so a digestion's clamp is seen and not just the mouth hanging open.
            dst[at++] = (byte)Math.Clamp(m.JawOpen * 255f, 0f, 255f);
        }

        dst[flagsAt] = flags;

        // The squad near this client, so its members are drawn and not only heard. Always
        // present (a count of zero when there is no squad), after the two bosses.
        int soldierCountAt = at++;
        int ns = 0;
        foreach (var s in world.Soldiers)
        {
            if (ns >= MaxSoldiers) break;
            if (!s.Alive) continue;
            if (Torus.DistanceSquared(s.Position, eye) > InterestRadius * InterestRadius) continue;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(s.Position.X, PosScale)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(s.Position.Y, PosScale)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), Q(s.Height, PosScale)); at += 2;
            BitConverter.TryWriteBytes(dst.Slice(at, 2), QAngle(s.Heading)); at += 2;
            dst[at++] = (byte)s.Move;
            dst[at++] = unchecked((byte)(sbyte)Math.Clamp(MathF.Round(s.Bank * 40f), -127f, 127f));
            dst[at++] = (byte)Math.Clamp(s.PlanarSpeed, 0f, 255f);
            byte sf = 0;
            if (s.IsLeader) sf |= 1;
            if (s.Allied) sf |= 2;
            dst[at++] = sf;
            ns++;
        }
        dst[soldierCountAt] = (byte)ns;
        return at;
    }

    /// <summary>Lays a bosses packet over this client's world, installing or dropping the
    /// render-only puppets. Never throws on a short packet — a bad one just leaves the last.</summary>
    public static void ApplyBosses(World.World world, ReadOnlySpan<byte> src)
    {
        try
        {
            int at = 0;
            at += 4;   // tick — bosses are keep-last like the field, so no ordering guard
            byte flags = src[at++];

            if ((flags & 1) != 0)
            {
                float x = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float y = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float head = BitConverter.ToInt16(src.Slice(at, 2)) / AngScale; at += 2;
                var phase = (Entities.CrabCore.State)src[at++];
                float core = src[at++] / 255f;
                float grab = src[at++] / 255f;
                float strike = src[at++] / 255f;
                float glow = src[at++] / 255f;
                world.AdoptBoss(Torus.Wrap(new Vector2(x, y)), head, phase, core, grab, strike, glow);
            }
            else world.ClearBossPuppet();

            if ((flags & 2) != 0)
            {
                float x = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float y = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                var phase = (Entities.MawCore.State)src[at++];
                float crystal = src[at++] / 255f;
                float bodyY = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float jaw = src[at++] / 255f;
                world.AdoptMaw(Torus.Wrap(new Vector2(x, y)), phase, crystal, bodyY, jaw);
            }
            else world.ClearMawPuppet();

            // The squad, rebuilt wholesale like the enemy list — a count then that many members.
            int soldiers = src[at++];
            world.BeginAdoptSoldiers();
            for (int i = 0; i < soldiers; i++)
            {
                float x = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float y = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float h = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float head = BitConverter.ToInt16(src.Slice(at, 2)) / AngScale; at += 2;
                var move = (Entities.SoldierMove)src[at++];
                float bank = unchecked((sbyte)src[at++]) / 40f;
                float speed = src[at++];
                byte sf = src[at++];
                world.AdoptSoldier(Torus.Wrap(new Vector2(x, y)), h, head, move, bank, speed,
                    leader: (sf & 1) != 0, allied: (sf & 2) != 0, slot: i);
            }
            world.EndAdoptSoldiers();
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
        }
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
            // The hunters are kept between packets and matched on the host's id, so each can be
            // eased toward its new spot rather than the whole list being torn down and rebuilt
            // (which was twenty steps of stutter a second). The sweep drops any this packet omits.
            world.BeginAdoptEnemies();
            for (int i = 0; i < enemies; i++)
            {
                int id = src[at++];
                float x = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float y = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float head = BitConverter.ToInt16(src.Slice(at, 2)) / AngScale; at += 2;
                bool elite = src[at++] != 0;
                world.AdoptEnemy(id, Torus.Wrap(new Vector2(x, y)), head, elite);
            }
            world.EndAdoptEnemies();

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
                // The client predicts its own rounds locally for an instant shot, so it must not
                // also adopt the host's copy of them — that would draw each of its own bolts
                // twice. Everyone else's rounds it takes as sent.
                if (owner == world.LocalIndex) continue;
                world.AdoptRound(Torus.Wrap(new Vector2(x, y)), h, new Vector2(vx, vy), owner, flags);
            }

            // The salvage. Static, so the list is only rebuilt when its size changes (a spawn or
            // a pickup) — otherwise the existing cells keep their age and bob smoothly rather
            // than resetting their phase twenty times a second.
            int pk = src[at++];
            bool rebuild = pk != world.Pickups.Count;
            if (rebuild) world.Pickups.Clear();
            for (int i = 0; i < pk; i++)
            {
                float px = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                float py = BitConverter.ToInt16(src.Slice(at, 2)) / PosScale; at += 2;
                var kind = (Entities.PickupKind)src[at++];
                var pos = Torus.Wrap(new Vector2(px, py));
                if (rebuild) world.Pickups.Add(new Entities.Pickup(pos, kind));
                else world.Pickups[i].Position = pos;
            }
            return tick;
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return 0u;
        }
    }
}
