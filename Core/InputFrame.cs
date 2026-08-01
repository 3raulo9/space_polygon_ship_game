using System.Numerics;

namespace Unrendered.Core;

/// <summary>
/// Every control the simulation can read, as one bit each.
///
/// <para>These are <em>actions</em>, not keys. They used to be keys — Q and E and the left
/// mouse button arrived here raw, and each chassis decided what they meant — on the argument
/// that one physical key legitimately means three different things depending on what you are
/// driving, and that naming actions here would drag the "which chassis am I?" question into
/// the sampler. The first half of that is still true and is still how the game feels. The
/// second half turned out to be backwards: the sampler does not have to know which chassis is
/// reading, because <em>every</em> action is resolved every tick and the chassis picks the
/// ones it cares about, exactly as it always did with the keys. What changed is that the
/// player can now say which button raises which bit, and a bit named <c>Q</c> cannot be
/// rebound to anything without becoming a lie.</para>
///
/// <para>Twenty-two bits, same as the keys they replace, and the frame is the same twelve
/// bytes on the wire. Bit positions are the wire vocabulary: append at the end, never
/// renumber.</para>
/// </summary>
[Flags]
public enum Btn : uint
{
    None = 0,

    // --- Shared: read on every chassis -------------------------------------------
    Forward    = 1u << 0,   // and, on a body, walking forward
    Back       = 1u << 1,
    TurnLeft   = 1u << 2,   // and, on a body, stepping left; on the fish, rolling left
    TurnRight  = 1u << 3,
    Jump       = 1u << 4,   // the tank's plant rides this too — see InputFrame.TankPlant
    Fire       = 1u << 5,   // cannon, rifle, spit, claw, virus round — the primary trigger
    Secondary  = 1u << 6,   // grenade, rocket, strike, emitter, overload
    Hyperspace = 1u << 7,

    // --- The TANK's siege kit ------------------------------------------------------
    TankLurch  = 1u << 8,
    TankSmoke  = 1u << 9,
    TankSlug   = 1u << 10,

    // --- The SPIDER ----------------------------------------------------------------
    SpiderPounce = 1u << 11,

    // --- The SOLDIER ---------------------------------------------------------------
    LeftHook   = 1u << 12,
    RightHook  = 1u << 13,  // the virus's lunge rides this too
    HighJump   = 1u << 14,

    // --- The FISH -------------------------------------------------------------------
    Beat       = 1u << 15,
    Brake      = 1u << 16,

    // --- Equip slots ------------------------------------------------------------------
    Slot1      = 1u << 17,
    Slot2      = 1u << 18,
    Slot3      = 1u << 19,
    Slot4      = 1u << 20,

    /// <summary>Use the thing in front of you. Only the multiplayer room reads it.</summary>
    Interact   = 1u << 21,
}

/// <summary>
/// One tick's worth of a single player's intent — everything the simulation is allowed
/// to know about what a human is doing, and nothing about which human or which machine.
///
/// This exists so the world can be driven by something other than the keyboard in front
/// of it. The headless self-test already wanted that (it has been faking it with the
/// ScriptedSoldierMove / ScriptedFishMove hooks on World); a second player over a wire
/// needs it properly. Twelve bytes, so a tick of it costs nothing to send.
///
/// <para><b>Edges are counted once.</b> <see cref="Down"/> is the live held state;
/// <see cref="Pressed"/> is the keys that went down <em>since the last tick the world
/// actually ran</em>. That distinction matters: the loop steps the sim in fixed 1/60
/// increments off an accumulator, so a slow display can run two steps in one render
/// frame. Raylib's own IsKeyPressed would answer true in both of them and the tank would
/// fire twice for one click. <see cref="InputSampler"/> hands the edge to the first step
/// and a clean frame to the second.</para>
/// </summary>
public readonly struct InputFrame
{
    /// <summary>Keys held this tick.</summary>
    public readonly Btn Down;

    /// <summary>Keys that went down on this tick and no other.</summary>
    public readonly Btn Pressed;

    /// <summary>Mouse movement for this tick in sixteenths of a pixel. Quantised so the
    /// frame stays small; a sixteenth of a pixel is far below what the look sensitivity can
    /// express, so nothing is lost that anyone can feel. Only meaningful while the cursor is
    /// captured — and, since <see cref="Aim"/> arrived, only meaningful <em>locally</em>: it
    /// is deliberately not on the wire (see the format note at the bottom).</summary>
    public readonly short LookX, LookY;

    /// <summary>
    /// Where this craft is actually pointing, in radians, quantised the same way the snapshot
    /// quantises a heading. Zero and meaningless unless <see cref="HasAim"/>.
    ///
    /// <para>This is the half of the frame the host <em>takes at its word</em>. Aim used to
    /// cross as <see cref="LookX"/>/<see cref="LookY"/> — a mouse <em>delta</em> the host
    /// integrated — and that cannot work over a lossy channel: every dropped input packet
    /// deducted a permanent, unrecoverable amount from the host's idea of where the client was
    /// looking, and the only thing that ever corrected the disagreement was the client's own
    /// reconciliation yanking its camera backwards onto a heading it had never chosen. An
    /// absolute angle is self-healing: a lost packet costs one tick of freshness and the next
    /// one puts the host back exactly on the player's line.</para>
    /// </summary>
    public readonly short AimYaw, AimPitch;

    /// <summary>True when <see cref="AimYaw"/>/<see cref="AimPitch"/> mean anything — set on
    /// every frame a networked client sends, and never on a locally-sampled one (the machine
    /// doing the sampling turns its own craft with the mouse delta, as it always did).</summary>
    public readonly bool HasAim;

    /// <summary>Radians per quantised unit, matching <c>Snapshot</c>'s heading scale so an
    /// angle costs the same two bytes and the same precision everywhere it is written.</summary>
    private const float AimScale = 65536f / MathF.Tau;

    public InputFrame(Btn down, Btn pressed, Vector2 look)
    {
        Down = down;
        Pressed = pressed;
        LookX = Quantise(look.X);
        LookY = Quantise(look.Y);
        AimYaw = 0; AimPitch = 0; HasAim = false;
    }

    private InputFrame(Btn down, Btn pressed, short lx, short ly,
                       short aimYaw, short aimPitch, bool hasAim)
    {
        Down = down; Pressed = pressed; LookX = lx; LookY = ly;
        AimYaw = aimYaw; AimPitch = aimPitch; HasAim = hasAim;
    }

    private static short Quantise(float px)
        => (short)Math.Clamp(MathF.Round(px * 16f), short.MinValue, short.MaxValue);

    private static short QuantiseAngle(float radians)
        => (short)Math.Clamp(MathF.Round(MathF.IEEERemainder(radians, MathF.Tau) * AimScale),
                             short.MinValue, short.MaxValue);

    /// <summary>
    /// The same frame, stamped with the absolute direction the craft is pointing. What a
    /// client puts on every input packet just before it goes out, so the host drives that seat
    /// along the line the player is really looking down rather than along a running total of
    /// mouse deltas it may have holes in.
    /// </summary>
    public InputFrame WithAim(float heading, float pitch)
        => new(Down, Pressed, LookX, LookY, QuantiseAngle(heading), QuantiseAngle(pitch), true);

    /// <summary>The stamped direction, in radians, as (yaw, pitch). Meaningless unless
    /// <see cref="HasAim"/>.</summary>
    public Vector2 Aim => new(AimYaw / AimScale, AimPitch / AimScale);

    /// <summary>A tick with nothing held and nothing pressed — a player whose hands are off
    /// the keys, which is also what a disconnected peer looks like until it says otherwise.</summary>
    public static readonly InputFrame Empty = default;

    public bool this[Btn b] => (Down & b) != 0;
    public bool Hit(Btn b) => (Pressed & b) != 0;

    /// <summary>Mouse movement this tick, back in whole pixels. Only meaningful while the
    /// cursor is captured, which the loop does for exactly as long as a craft is driving.</summary>
    public Vector2 LookDelta => new(LookX / 16f, LookY / 16f);

    /// <summary>
    /// The same frame with every edge stripped and the look zeroed, but the held keys
    /// kept. This is what the second and later sim steps inside one render frame get:
    /// a craft carries on driving under a held W, but one click stays one shot and the
    /// frame's mouse movement is applied exactly once.
    ///
    /// <para>The aim is <em>kept</em>. This frame is also what the host falls back to for a
    /// seat whose input packet did not arrive this tick, and a player who is still holding W
    /// is also still pointing where they were pointing — zeroing it would swing their craft to
    /// face north for one tick every time the wire hiccupped.</para>
    /// </summary>
    public InputFrame Repeat() => new(Down, Btn.None, 0, 0, AimYaw, AimPitch, HasAim);

    /// <summary>
    /// This frame, plus the edges of an <paramref name="earlier"/> one being skipped over.
    ///
    /// The host eats two ticks of input in one step when a client's packets have arrived in a
    /// clump and it has fallen behind. Held keys and aim come from this frame — it is the newer
    /// and therefore the truer account of where the hands are — but the edges are OR'd, because
    /// the tick being caught up on is still a tick on which the player pressed something, and
    /// silently dropping it is the same lost action the redundancy exists to prevent.
    /// </summary>
    public InputFrame Absorbing(in InputFrame earlier)
        => new(Down, Pressed | earlier.Pressed, LookX, LookY, AimYaw, AimPitch, HasAim);

    /// <summary>
    /// The same frame with every trigger cleared, but the movement left alone. What a client
    /// sends while its own crafting panel is open: the mouse belongs to the panel there, so a
    /// click on a stack must not also loose a round — and clearing it here, on the machine
    /// that knows the panel is up, means the host never has to reason about a UI it cannot
    /// see. The craft keeps drifting under the overlay, exactly as it does single-player.
    /// </summary>
    public InputFrame WithoutCombat()
    {
        const Btn combat = Btn.Fire | Btn.Secondary | Btn.Hyperspace
                         | Btn.TankLurch | Btn.TankSmoke | Btn.TankSlug | Btn.SpiderPounce
                         | Btn.LeftHook | Btn.RightHook
                         | Btn.Slot1 | Btn.Slot2 | Btn.Slot3 | Btn.Slot4;
        return new(Down & ~combat, Pressed & ~combat, LookX, LookY, AimYaw, AimPitch, HasAim);
    }

    // --- What the chassis actually ask for ---------------------------------------
    // Named reads, so the world keeps saying what it means rather than testing bits. Each
    // one is the same expression that used to live in InputMap; the only thing that has
    // changed is where the answer comes from.

    // The machines: TANK and SPIDER.
    public bool Forward => this[Btn.Forward];
    public bool Back => this[Btn.Back];
    public bool TurnLeft => this[Btn.TurnLeft];
    public bool TurnRight => this[Btn.TurnRight];
    public bool JumpPressed => Hit(Btn.Jump);
    public bool Fire => this[Btn.Fire];
    public bool Grenade => this[Btn.Secondary];
    public bool HyperspacePressed => Hit(Btn.Hyperspace);

    /// <summary>Dig in / stand up: the jump binding. The treads never leave the grid now.</summary>
    public bool TankPlantPressed => Hit(Btn.Jump);
    /// <summary>The lurch — a track-boost dodge, paid out of the Hyper reserve.</summary>
    public bool TankLurchPressed => Hit(Btn.TankLurch);
    /// <summary>Vent the smoke dischargers to blind the field.</summary>
    public bool TankSmokePressed => Hit(Btn.TankSmoke);
    /// <summary>The AP slug — a heavy round that punches through a line and through cover.</summary>
    public bool TankSlugPressed => Hit(Btn.TankSlug);
    /// <summary>The pounce — a kick off a wall.</summary>
    public bool SpiderPouncePressed => Hit(Btn.SpiderPounce);

    // The SOLDIER: a person in first person. Its rifle and its rocket are the primary and
    // secondary triggers rather than bits of their own — they are what this chassis has in
    // the same two hands the tank puts on a cannon and a grenade, and giving them separate
    // bindings would mean a player who moves FIRE off the mouse discovers the soldier alone
    // never got the message.
    public bool RightHookPressed => Hit(Btn.RightHook);
    public bool LeftHookPressed => Hit(Btn.LeftHook);
    public bool HighJumpPressed => Hit(Btn.HighJump);
    public bool RifleDown => this[Btn.Fire];
    public bool RocketPressed => Hit(Btn.Secondary);

    /// <summary>Movement as (strafe, forward), each -1..1. A body, not a vehicle: the turn
    /// bindings step sideways here, since the mouse is already doing the turning.</summary>
    public Vector2 SoldierMove => new(
        (this[Btn.TurnRight] ? 1f : 0f) - (this[Btn.TurnLeft] ? 1f : 0f),
        (this[Btn.Forward] ? 1f : 0f) - (this[Btn.Back] ? 1f : 0f));

    // The FISH: no held movement key at all. The beat is an event, not a state.
    public bool BeatPressed => Hit(Btn.Beat);
    public float RollInput => (this[Btn.TurnRight] ? 1f : 0f) - (this[Btn.TurnLeft] ? 1f : 0f);
    public bool BrakeDown => this[Btn.Brake];
    public bool SpitDown => this[Btn.Fire];
    public bool StrikePressed => Hit(Btn.Secondary);

    // The VIRUS: the soldier's hand, flying.
    public Vector2 VirusMove => SoldierMove;
    public bool VirusFireDown => this[Btn.Fire];
    public bool VirusOverloadPressed => Hit(Btn.Secondary);

    /// <summary>The four equip slots. Which one was just pressed, or -1.</summary>
    public int WeaponSlotPressed()
    {
        if (Hit(Btn.Slot1)) return 0;
        if (Hit(Btn.Slot2)) return 1;
        if (Hit(Btn.Slot3)) return 2;
        if (Hit(Btn.Slot4)) return 3;
        return -1;
    }

    /// <summary>Use the thing in front of you. Only the multiplayer room reads it.</summary>
    public bool InteractPressed => Hit(Btn.Interact);

    // --- Wire format --------------------------------------------------------------
    // Fixed thirteen bytes, little-endian, no version tag. The tag belongs on the packet
    // that carries a run of these, not on every frame inside it — and a run is exactly what
    // carries them now, so the size is worth a sentence: a client sends the last dozen frames
    // in every packet, which makes thirteen bytes into a hundred and sixty. That is still
    // nothing on an uplink, and it is what turns a lost input packet from a lost action into
    // no event at all.
    //
    // The mouse delta is deliberately NOT here. It was, and it was the wrong thing to send:
    // the host integrated it, so a dropped packet cost the host a piece of the player's turn
    // for ever. What crosses instead is the absolute angle the delta produced, which the host
    // takes outright — see WithAim. LookX/LookY stay on the struct because the machine that
    // sampled them still turns its own craft with them; they simply stop at the socket.

    public const int Size = 13;

    public void Write(Span<byte> dst)
    {
        BitConverter.TryWriteBytes(dst[..4], (uint)Down);
        BitConverter.TryWriteBytes(dst.Slice(4, 4), (uint)Pressed);
        BitConverter.TryWriteBytes(dst.Slice(8, 2), AimYaw);
        BitConverter.TryWriteBytes(dst.Slice(10, 2), AimPitch);
        dst[12] = HasAim ? (byte)1 : (byte)0;
    }

    public static InputFrame Read(ReadOnlySpan<byte> src) => new(
        (Btn)BitConverter.ToUInt32(src[..4]),
        (Btn)BitConverter.ToUInt32(src.Slice(4, 4)),
        0, 0,
        BitConverter.ToInt16(src.Slice(8, 2)),
        BitConverter.ToInt16(src.Slice(10, 2)),
        src[12] != 0);
}
