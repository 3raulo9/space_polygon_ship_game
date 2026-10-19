using System.Reflection;
using Unrendered.Core;
using GameWorld = Unrendered.World.World;

namespace UnrenderedPortal;

/// <summary>
/// The world the pilot left keeps going while they are in Minecraft — but only while a
/// Minecraft player is standing in it. Their fight is real: the hunters still have to move,
/// the rounds still have to fly. With nobody in it, it waits, the way a solo run waits
/// behind the pause panel.
///
/// The pilot's ears are in Minecraft, so after the step the stand-in world is asked to place
/// the listener again — the last word on where the player is hearing from is always theirs.
/// </summary>
public static class Background
{
    private static readonly MethodInfo? DriveListener =
        typeof(GameWorld).GetMethod("DriveListener", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static void Tick(float dt)
    {
        if (!Realm.Active || Realm.HomeWorld is not { } home || Realm.RealmWorld is not { } realm) return;
        if (!Visitors.AnyIn(home)) return;
        try
        {
            home.Update(dt, InputFrame.Empty, acceptCombatInput: false);
            Visitors.Tick(home, dt);
        }
        catch (Exception e) { Rift.Log("background: " + e); }
        try { DriveListener?.Invoke(realm, new object[] { 0f }); }
        catch (Exception e) { Rift.Log("listener: " + e.Message); }
    }
}
