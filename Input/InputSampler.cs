using System.Numerics;
using Raylib_cs;
using Unrendered.Core;

namespace Unrendered.Input;

/// <summary>
/// Turns the keyboard in front of the machine into <see cref="InputFrame"/>s.
///
/// This is the <em>only</em> place in the game where a live device becomes simulation
/// input. Everything downstream — the world, the rigs, the tank — reads a frame it was
/// handed and cannot tell whether a human, the self-test, or a peer on the other end of
/// a wire produced it. That is the whole point of the exercise: a second player is just
/// a second frame arriving from somewhere else.
///
/// It also owns the edge bookkeeping. The loop steps the sim in fixed 1/60 increments off
/// an accumulator, so one render frame can produce two steps, or none. Ask for
/// <see cref="Sample"/> once per render frame, then <see cref="Next"/> once per step:
/// the first step gets the edges, the rest get the same held keys with the edges spent.
/// </summary>
public sealed class InputSampler
{
    private Btn _prevDown;
    private InputFrame _frame;
    private bool _fresh;

    /// <summary>Reads the devices. Call once per render frame, before the fixed-step loop.</summary>
    public void Sample()
    {
        Bindings b = InputMap.Active.Bindings;
        Btn down = Btn.None;

        // Every action, every tick, whichever chassis is being driven. Resolving only the
        // ones the current craft reads would be cheaper and wrong: the sampler is the one
        // place in the game that must not know what is being driven, and a frame is twelve
        // bytes whether eight bits are set or twenty.
        //
        // Held state only — the edges are worked out below against the last tick that
        // actually ran, which is not the same question as "did raylib see a press this
        // render frame". See InputFrame's remarks.
        if (b.Down(InputAction.Forward)) down |= Btn.Forward;
        if (b.Down(InputAction.Back)) down |= Btn.Back;
        if (b.Down(InputAction.TurnLeft)) down |= Btn.TurnLeft;
        if (b.Down(InputAction.TurnRight)) down |= Btn.TurnRight;
        if (b.Down(InputAction.Jump)) down |= Btn.Jump;
        if (b.Down(InputAction.Fire)) down |= Btn.Fire;
        if (b.Down(InputAction.Secondary)) down |= Btn.Secondary;
        if (b.Down(InputAction.Hyperspace)) down |= Btn.Hyperspace;

        if (b.Down(InputAction.TankLurch)) down |= Btn.TankLurch;
        if (b.Down(InputAction.TankSmoke)) down |= Btn.TankSmoke;
        if (b.Down(InputAction.TankSlug)) down |= Btn.TankSlug;

        if (b.Down(InputAction.SpiderPounce)) down |= Btn.SpiderPounce;

        if (b.Down(InputAction.LeftHook)) down |= Btn.LeftHook;
        if (b.Down(InputAction.RightHook)) down |= Btn.RightHook;
        if (b.Down(InputAction.HighJump)) down |= Btn.HighJump;

        if (b.Down(InputAction.Beat)) down |= Btn.Beat;
        if (b.Down(InputAction.Brake)) down |= Btn.Brake;

        if (b.Down(InputAction.Slot1)) down |= Btn.Slot1;
        if (b.Down(InputAction.Slot2)) down |= Btn.Slot2;
        if (b.Down(InputAction.Slot3)) down |= Btn.Slot3;
        if (b.Down(InputAction.Slot4)) down |= Btn.Slot4;

        if (b.Down(InputAction.Interact)) down |= Btn.Interact;

        _frame = new InputFrame(down, down & ~_prevDown, Raylib.GetMouseDelta());
        _prevDown = down;
        _fresh = true;
    }

    /// <summary>
    /// The frame for the next simulation step. The first call after each
    /// <see cref="Sample"/> carries that frame's edges and mouse movement; every call
    /// after it repeats the held keys only, so one click is one shot however many fixed
    /// steps the accumulator decides to run.
    /// </summary>
    public InputFrame Next()
    {
        if (_fresh)
        {
            _fresh = false;
            return _frame;
        }
        return _frame.Repeat();
    }

    /// <summary>
    /// The frame as last sampled, edges and all, without spending them. For the code
    /// outside the fixed step that still has to look at live input — cursor handling and
    /// the debug hatches in the loop.
    /// </summary>
    public InputFrame Current => _frame;

    /// <summary>
    /// Drops the held state, so the next sample reports every key still down as freshly
    /// pressed. Called when the world stops reading input for a while — a pause, the
    /// crafting panel, a menu — so that a key held down across the gap doesn't come back
    /// as an edge nobody meant, and one released behind the overlay doesn't strand a bit.
    /// </summary>
    public void Forget()
    {
        _prevDown = Btn.None;
        _frame = InputFrame.Empty;
        _fresh = false;
    }
}
