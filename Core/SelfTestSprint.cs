using System.Numerics;
using Unrendered.Entities;
using Unrendered.Input;

namespace Unrendered.Core;

/// <summary>
/// The SOLDIER on foot: the walk and the sprint of the blocky-avatar game the body is built
/// after, and the controls that reach them.
///
/// <para>Driven through real input frames rather than by poking the rig, because what can go
/// wrong here is the wiring between them — a sprint bit the world never hands over, a latch
/// that lets go when the key does, a trigger that still answers to the sprint's button.</para>
/// </summary>
public static partial class SelfTest
{
    /// <summary>A soldier on open ground with nothing to walk into, facing +Z.</summary>
    private static World.World OpenGround()
    {
        var world = SoldierWorld();
        world.Structures.Clear();
        world.Player.Heading = 0f;
        return world;
    }

    /// <summary>Steps a world for <paramref name="seconds"/> with the given keys held, the
    /// first step carrying them as fresh presses. Through the world's own per-seat read, so
    /// the bits are handed to the rig exactly as a player's are.</summary>
    private static void Hold(World.World world, Btn keys, float seconds, Btn pressed = Btn.None)
    {
        int steps = (int)MathF.Round(seconds / (float)Config.FixedDt);
        for (int i = 0; i < steps; i++)
            world.Update((float)Config.FixedDt,
                new InputFrame(keys, i == 0 ? pressed : Btn.None, Vector2.Zero));
    }

    /// <summary>
    /// Forward walks at the original's walking pace; SPRINT pressed while walking forward
    /// breaks into its sprint, which then holds with SPRINT let go for as long as forward is
    /// held, and ends the moment forward is released.
    /// </summary>
    private static string? SoldierWalksAndSprintsLikeTheOriginal()
    {
        var world = OpenGround();
        var rig = world.Player.Soldier!;

        Hold(world, Btn.Forward, 1f, pressed: Btn.Forward);
        if (rig.Sprinting) return "walking forward broke into a sprint with nothing pressed";
        if (MathF.Abs(rig.PlanarSpeed - SoldierRig.WalkSpeed) > 0.3f)
            return $"walking at {rig.PlanarSpeed:0.0} m/s, not the walk's {SoldierRig.WalkSpeed:0.0}";

        Hold(world, Btn.Forward | Btn.Sprint, 1f / 60f, pressed: Btn.Sprint);
        if (!rig.Sprinting) return "SPRINT while walking forward did not start a sprint";

        // Let go of SPRINT, keep walking: still sprinting, at the sprint's speed.
        Hold(world, Btn.Forward, 1f);
        if (!rig.Sprinting) return "the sprint stopped when SPRINT was let go, with forward still held";
        if (MathF.Abs(rig.PlanarSpeed - SoldierRig.SprintSpeed) > 0.3f)
            return $"sprinting at {rig.PlanarSpeed:0.0} m/s, not the sprint's {SoldierRig.SprintSpeed:0.0}";

        // And forward let go: the sprint is over, and the body stops quickly.
        Hold(world, Btn.None, 0.5f);
        if (rig.Sprinting) return "the sprint outlived letting go of forward";
        if (rig.PlanarSpeed > 0.5f) return $"still sliding at {rig.PlanarSpeed:0.0} m/s half a second after stopping";

        // Backing off cannot be a sprint, however hard SPRINT is held.
        Hold(world, Btn.Back | Btn.Sprint, 1f, pressed: Btn.Back | Btn.Sprint);
        if (rig.Sprinting) return "backing away with SPRINT held started a sprint";
        return null;
    }

    /// <summary>The original's other way in: forward tapped twice in quick succession. Too
    /// slow a second tap is just walking again.</summary>
    private static string? ADoubleTapOfForwardSprints()
    {
        var world = OpenGround();
        var rig = world.Player.Soldier!;

        Hold(world, Btn.Forward, 0.1f, pressed: Btn.Forward);
        Hold(world, Btn.None, 0.1f);
        Hold(world, Btn.Forward, 0.5f, pressed: Btn.Forward);
        if (!rig.Sprinting) return "a quick second tap of forward did not start a sprint";

        Hold(world, Btn.None, 0.5f);
        Hold(world, Btn.Forward, 0.1f, pressed: Btn.Forward);
        Hold(world, Btn.None, 0.6f);
        Hold(world, Btn.Forward, 0.5f, pressed: Btn.Forward);
        if (rig.Sprinting) return "two taps of forward most of a second apart started a sprint";
        return null;
    }

    /// <summary>
    /// On foot, the sprint's button is not a trigger. Fire ships with Left Ctrl as its second
    /// key and the sprint ships on Left Ctrl, so the read the sampler makes for a soldier —
    /// FIRE minus whatever SPRINT is on — must exclude it, keep the mouse, and leave every
    /// other chassis's Ctrl-to-fire alone by being a separate read.
    /// </summary>
    private static string? OnFootTheSprintKeyIsNotATrigger()
    {
        var b = new Bindings();
        var ctrl = InputCode.Key(Raylib_cs.KeyboardKey.LeftControl);
        var click = InputCode.Mouse(Raylib_cs.MouseButton.Left);
        if (b[InputAction.Sprint].Primary != ctrl) return "SPRINT does not ship on Left Ctrl";
        if (!b[InputAction.Fire].Uses(ctrl))
            return "FIRE no longer ships with Left Ctrl, so this check is not testing anything";

        bool OnFoot(InputCode down) =>
            Bindings.HeldExcept(b[InputAction.Fire], b[InputAction.Sprint], c => c == down);
        if (OnFoot(ctrl)) return "on foot, Left Ctrl still pulls the trigger";
        if (!OnFoot(click)) return "on foot, the mouse no longer fires";

        // Every other chassis reads FIRE whole, Ctrl and all.
        bool Driving(InputCode down) =>
            Bindings.HeldExcept(b[InputAction.Fire], default, c => c == down);
        if (!Driving(ctrl)) return "in a tank, Left Ctrl stopped firing";

        if (b.Clashes(InputAction.Sprint) is { } clash)
            return $"SPRINT clashes with {clash} on a fresh install";
        return null;
    }
}
