using System.Numerics;

namespace VoidTanks.Core;

/// <summary>
/// Every physical control the simulation can read, as one bit each.
///
/// These are <em>keys</em>, not actions. The game has always let one physical key mean a
/// different thing on every chassis — Q is the tank's lurch and the soldier's left hook,
/// E is the smoke vent and the right hook, the left mouse button is a rifle or a spit or
/// a virus round depending on who is holding it — and that arrangement is deliberate.
/// Naming the actions here instead would need thirty bits where twenty-two do, and would
/// quietly move the "which chassis am I?" decision out of the world and into the sampler,
/// which is the one place that must not know.
///
/// The four drive bits and the four trigger bits below them are the exception: those are
/// rebindable from the settings screen, so the sampler resolves them through
/// <see cref="Settings"/> and what lands here is already the answer, not the key.
/// </summary>
[Flags]
public enum Btn : uint
{
    None = 0,

    // --- Rebindable: resolved through Settings at sample time --------------------
    Forward    = 1u << 0,
    Back       = 1u << 1,
    TurnLeft   = 1u << 2,
    TurnRight  = 1u << 3,
    Jump       = 1u << 4,   // the tank's plant rides this too — see InputFrame.TankPlant
    Fire       = 1u << 5,
    Grenade    = 1u << 6,
    Hyperspace = 1u << 7,

    // --- Fixed physical keys -----------------------------------------------------
    Q = 1u << 8,
    E = 1u << 9,
    R = 1u << 10,
    T = 1u << 11,
    Y = 1u << 12,
    U = 1u << 13,

    W = 1u << 14,
    A = 1u << 15,
    S = 1u << 16,
    D = 1u << 17,

    Space = 1u << 18,
    Enter = 1u << 19,

    MouseL = 1u << 20,
    MouseR = 1u << 21,
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
    /// frame stays a fixed twelve bytes on the wire; a sixteenth of a pixel is far below
    /// what the look sensitivity can express, so nothing is lost that anyone can feel.
    /// Only meaningful while the cursor is captured.</summary>
    public readonly short LookX, LookY;

    public InputFrame(Btn down, Btn pressed, Vector2 look)
    {
        Down = down;
        Pressed = pressed;
        LookX = Quantise(look.X);
        LookY = Quantise(look.Y);
    }

    private InputFrame(Btn down, Btn pressed, short lx, short ly)
    {
        Down = down; Pressed = pressed; LookX = lx; LookY = ly;
    }

    private static short Quantise(float px)
        => (short)Math.Clamp(MathF.Round(px * 16f), short.MinValue, short.MaxValue);

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
    /// </summary>
    public InputFrame Repeat() => new(Down, Btn.None, 0, 0);

    /// <summary>
    /// The same frame with every trigger cleared, but the movement left alone. What a client
    /// sends while its own crafting panel is open: the mouse belongs to the panel there, so a
    /// click on a stack must not also loose a round — and clearing it here, on the machine
    /// that knows the panel is up, means the host never has to reason about a UI it cannot
    /// see. The craft keeps drifting under the overlay, exactly as it does single-player.
    /// </summary>
    public InputFrame WithoutCombat()
    {
        const Btn combat = Btn.Fire | Btn.Grenade | Btn.Hyperspace | Btn.MouseL | Btn.MouseR
                         | Btn.Q | Btn.E | Btn.R | Btn.T | Btn.Y | Btn.U;
        return new(Down & ~combat, Pressed & ~combat, LookX, LookY);
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
    public bool Grenade => this[Btn.Grenade];
    public bool HyperspacePressed => Hit(Btn.Hyperspace);

    /// <summary>Dig in / stand up: the freed jump key. The treads never leave the grid now.</summary>
    public bool TankPlantPressed => Hit(Btn.Jump);
    /// <summary>Q: the lurch — a track-boost dodge, paid out of the Hyper reserve.</summary>
    public bool TankLurchPressed => Hit(Btn.Q);
    /// <summary>E: vent the smoke dischargers to blind the field.</summary>
    public bool TankSmokePressed => Hit(Btn.E);
    /// <summary>R: the AP slug — a heavy round that punches through a line and through cover.</summary>
    public bool TankSlugPressed => Hit(Btn.R);
    /// <summary>Q: the pounce — a kick off a wall, on the key the heavy chassis dodges with.</summary>
    public bool SpiderPouncePressed => Hit(Btn.Q);

    // The SOLDIER: a person in first person.
    public bool RightHookPressed => Hit(Btn.E);
    public bool LeftHookPressed => Hit(Btn.Q);
    public bool HighJumpPressed => Hit(Btn.Enter) || Hit(Btn.Space);
    public bool RifleDown => this[Btn.MouseL];
    public bool RocketPressed => Hit(Btn.MouseR);

    /// <summary>Raw WASD as (strafe, forward), each -1..1. A body, not a vehicle.</summary>
    public Vector2 SoldierMove => new(
        (this[Btn.D] ? 1f : 0f) - (this[Btn.A] ? 1f : 0f),
        (this[Btn.W] ? 1f : 0f) - (this[Btn.S] ? 1f : 0f));

    // The FISH: no held movement key at all. W is an event, not a state.
    public bool BeatPressed => Hit(Btn.W) || Hit(Btn.Space);
    public float RollInput => (this[Btn.D] ? 1f : 0f) - (this[Btn.A] ? 1f : 0f);
    public bool BrakeDown => this[Btn.S];
    public bool SpitDown => this[Btn.MouseL];
    public bool StrikePressed => Hit(Btn.MouseR);

    // The VIRUS: the soldier's hand, flying.
    public Vector2 VirusMove => SoldierMove;
    public bool VirusFireDown => this[Btn.MouseL];
    public bool VirusOverloadPressed => Hit(Btn.MouseR);

    /// <summary>The four equip slots on the R/T/Y/U row. Which one was just pressed, or -1.</summary>
    public int WeaponSlotPressed()
    {
        if (Hit(Btn.R)) return 0;
        if (Hit(Btn.T)) return 1;
        if (Hit(Btn.Y)) return 2;
        if (Hit(Btn.U)) return 3;
        return -1;
    }

    // --- Wire format --------------------------------------------------------------
    // Fixed twelve bytes, little-endian, no version tag. The tag belongs on the packet
    // that carries a run of these, not on every frame inside it.

    public const int Size = 12;

    public void Write(Span<byte> dst)
    {
        BitConverter.TryWriteBytes(dst[..4], (uint)Down);
        BitConverter.TryWriteBytes(dst.Slice(4, 4), (uint)Pressed);
        BitConverter.TryWriteBytes(dst.Slice(8, 2), LookX);
        BitConverter.TryWriteBytes(dst.Slice(10, 2), LookY);
    }

    public static InputFrame Read(ReadOnlySpan<byte> src) => new(
        (Btn)BitConverter.ToUInt32(src[..4]),
        (Btn)BitConverter.ToUInt32(src.Slice(4, 4)),
        BitConverter.ToInt16(src.Slice(8, 2)),
        BitConverter.ToInt16(src.Slice(10, 2)));
}
