using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;

namespace VoidTanks.Input;

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
        Settings s = InputMap.Active;
        Btn down = Btn.None;

        // Rebindable: the settings screen decides which key this is, and the answer lands
        // here already resolved.
        if (s.ForwardDown()) down |= Btn.Forward;
        if (s.BackDown()) down |= Btn.Back;
        if (s.TurnLeftDown()) down |= Btn.TurnLeft;
        if (s.TurnRightDown()) down |= Btn.TurnRight;
        if (s.JumpDown()) down |= Btn.Jump;
        if (s.FireDown()) down |= Btn.Fire;
        if (s.GrenadeDown()) down |= Btn.Grenade;
        if (s.HyperspaceDown()) down |= Btn.Hyperspace;

        // Fixed physical keys: whatever they mean is the reading chassis's business.
        if (Raylib.IsKeyDown(KeyboardKey.Q)) down |= Btn.Q;
        if (Raylib.IsKeyDown(KeyboardKey.E)) down |= Btn.E;
        if (Raylib.IsKeyDown(KeyboardKey.R)) down |= Btn.R;
        if (Raylib.IsKeyDown(KeyboardKey.T)) down |= Btn.T;
        if (Raylib.IsKeyDown(KeyboardKey.Y)) down |= Btn.Y;
        if (Raylib.IsKeyDown(KeyboardKey.U)) down |= Btn.U;
        if (Raylib.IsKeyDown(KeyboardKey.W)) down |= Btn.W;
        if (Raylib.IsKeyDown(KeyboardKey.A)) down |= Btn.A;
        if (Raylib.IsKeyDown(KeyboardKey.S)) down |= Btn.S;
        if (Raylib.IsKeyDown(KeyboardKey.D)) down |= Btn.D;
        if (Raylib.IsKeyDown(KeyboardKey.Space)) down |= Btn.Space;
        if (Raylib.IsKeyDown(KeyboardKey.Enter)) down |= Btn.Enter;
        if (Raylib.IsMouseButtonDown(MouseButton.Left)) down |= Btn.MouseL;
        if (Raylib.IsMouseButtonDown(MouseButton.Right)) down |= Btn.MouseR;

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
