using System.Numerics;
using System.Runtime.CompilerServices;
using Unrendered.Core;
using Unrendered.Entities;
using Unrendered.Rendering;
using GameWorld = Unrendered.World.World;

namespace UnrenderedPortal;

/// <summary>
/// The portal is a door for everything, the way a nether portal is. An UNRENDERED hunter that
/// drives into the water goes through: it leaves this world and comes out of Minecraft's
/// portal as itself — its body, drawn by Minecraft from the very mesh this game draws it
/// with, fighting there until Minecraft kills it or it drives back in. One that comes back is
/// handed back to the field here. (Minecraft's mobs come the other way on Minecraft's side:
/// they walk into the rift dimension, which is this world as Minecraft holds it.)
/// </summary>
public static class Crossings
{
    private sealed class Side { public float V; public bool Have; public double Cool; }
    private static readonly ConditionalWeakTable<EnemyTank, Side> _sides = new();
    private static readonly HashSet<string> _modelsSent = new();

    public static void Relink() => _modelsSent.Clear();

    internal static void Tick(GameWorld world)
    {
        if (Rift.Live is not { } live || Portal.Linked == null || world.Networked) return;
        Vector2 n = new(MathF.Sin(Portal.Facing), MathF.Cos(Portal.Facing));
        Vector2 across = new(n.Y, -n.X);
        float half = (Portal.InnerW + 2) * Portal.Block * 0.5f;   // the whole frame's width

        for (int i = world.Enemies.Count - 1; i >= 0; i--)
        {
            var e = world.Enemies[i];
            if (!e.Alive || e.Grabbed || e.Flung) continue;
            Vector2 rel = Torus.Delta(Portal.Position, e.Position);
            float side = Vector2.Dot(rel, n);
            var s = _sides.GetOrCreateValue(e);
            bool through = Raylib_cs.Raylib.GetTime() > s.Cool && s.Have && MathF.Sign(side) != MathF.Sign(s.V) && side != 0f
                        && MathF.Abs(Vector2.Dot(rel, across)) < half && MathF.Abs(side) < 6f;
            s.V = side;
            s.Have = true;
            if (!through) continue;

            string kind = e.IsElite ? "elite" : "hunter";
            SendModel(kind, live);
            live.Send("cross", "kind", kind, "hp", e.Shield, "max", e.IsElite ? 5f : 3f);
            world.Enemies.RemoveAt(i);
            CueBank.Play(Cue.PortalEnter, e.Position, 0f);
            Rift.Log($"crossings: a {kind} went through to Minecraft");
        }
    }

    internal static bool Handle(Msg m)
    {
        if (m.T != "return") return false;
        if (Portal.World is not { } w || !Portal.Open) return true;
        string kind = m.Str("kind");
        if (kind is not ("hunter" or "elite")) return true;
        Vector2 n = new(MathF.Sin(Portal.Facing), MathF.Cos(Portal.Facing));
        var e = new EnemyTank(Torus.Wrap(Portal.Position + n * 6f), elite: kind == "elite");
        e.Shield = Math.Clamp(m.F("hp", e.Shield), 0.1f, 50f);
        e.Heading = Portal.Facing;
        // Out of the water and into the field — not straight back through it.
        _sides.AddOrUpdate(e, new Side { Cool = Raylib_cs.Raylib.GetTime() + 8 });
        w.Enemies.Add(e);
        CueBank.Play(Cue.PortalEnter, e.Position, 0f);
        Rift.Notice(kind == "elite" ? "AN ELITE CAME BACK FROM MINECRAFT" : "A HUNTER CAME BACK FROM MINECRAFT");
        return true;
    }

    /// <summary>
    /// The body Minecraft draws a crossed hunter with: the game's own hunter, drawn once by the
    /// game's own renderer standing alone at the origin, facing +Z, and captured.
    /// </summary>
    private static void SendModel(string kind, Link live)
    {
        if (!_modelsSent.Add(kind)) return;
        try
        {
            var stage = new GameWorld(new Loadout(), new MatchSettings { SpawnEnemies = false }, null);
            stage.Structures.Clear();
            stage.Pickups.Clear();
            stage.GroundWeapons.Clear();
            stage.Enemies.Clear();
            stage.Players[0].Position = new Vector2(Torus.Half - 2f, Torus.Half - 2f);
            stage.Enemies.Add(new EnemyTank(Vector2.Zero, elite: kind == "elite") { Heading = 0f });
            var eye = new EntityRenderer();
            Atmosphere.Adopt(stage.Sky);
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            Capture.DrawList(() => eye.Draw(stage, new Vector3(0f, 6f, -14f)), bw);
            bw.Flush();
            live.SendBin("model", ms.GetBuffer().AsSpan(0, (int)ms.Length), droppable: false, "kind", kind);
        }
        catch (Exception ex) { Rift.Log("crossings: model: " + ex.Message); }
    }
}
