using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.Entities;

namespace VoidTanks.Rendering;

/// <summary>
/// The enemy squads, drawn out in the world: the figures themselves, the cables they are
/// hanging from, and the one bright mark that says which of the four is currently coming
/// for you.
///
/// This is the only enemy in the game that is drawn as an <em>articulated body</em> rather
/// than as a rigid mesh turned on the spot, and it has to be. Everything else out here is
/// a machine, and a machine reads perfectly well as one shape at one angle; a person does
/// not, and a person moving like this one moves reads as nothing at all unless the limbs
/// go where the physics says they should. The whole threat of the squads is legibility at
/// range — which one is about to arrive, from where, and how fast — and every part of that
/// is carried by the silhouette.
///
/// The figure and the cables both come from the player's own chassis: the same
/// <see cref="SoldierModel"/> the hangar turntable poses, and the same cable and hook
/// drawing the player's rig uses. They are wearing the same kit. It should look like it.
/// </summary>
public sealed class EnemySoldierRenderer
{
    private readonly SoldierModel _figure = new();

    public void Draw(World.World world, Vector3 cameraPos, float elapsed)
    {
        if (world.Soldiers.Count == 0) return;

        var eyeXZ = new Vector2(cameraPos.X, cameraPos.Z);

        foreach (var s in world.Soldiers)
        {
            if (!s.Alive) continue;

            // Nearest image across the wrap, like everything else in the world pass: a
            // squad working the seam is drawn beside the player rather than an arena away.
            Vector2 at = Torus.NearestImage(s.Position, eyeXZ);

            // Cables first, so the body draws over the near end of its own lines rather
            // than having two steel threads laid across its chest.
            SoldierRenderer.DrawCable(s.Right, Launcher(at, s, right: true), eyeXZ);
            SoldierRenderer.DrawCable(s.Left, Launcher(at, s, right: false), eyeXZ);

            bool perched = s.Move == SoldierMove.Perched;
            var flight = new SoldierModel.FlightPose(
                Speed: s.PlanarSpeed,
                Bank: s.Bank,
                Grounded: s.Move == SoldierMove.Running,
                Perched: perched,
                Blades: s.BladesOut,
                Stagger: s.Stagger,
                // Offset per soldier so four of them never breathe or stride in unison —
                // the cheapest possible fix for the single thing that most makes a group
                // of figures read as one thing drawn four times.
                Time: elapsed + s.Slot * 1.37f);

            // The corruption, worn on the outside. A seeded body is going magenta at the
            // edges and a turned one is nothing else — which is the only readout this
            // mechanic gets and the only one it needs: at a glance across a fight the player
            // can see which of the four is theirs to take and which is already theirs.
            float rot = s.Corruption;
            Color cloth = GridRenderer.LerpColor(Palette.SoldierCloth, Palette.NeonMagenta, rot);
            Color webbing = GridRenderer.LerpColor(
                s.IsLeader ? Palette.SoldierMark : Palette.SoldierWebbing,
                Palette.NeonMagenta, rot * 0.8f);
            Color steel = GridRenderer.LerpColor(Palette.SoldierSteel, Palette.NeonRed, rot * 0.6f);

            // An escort is not corrupted — they have not changed their minds about anything,
            // they are simply flying beside somebody they take for one of their own. So they
            // are marked in the charged teal this game has always used for things that are on
            // your side, on the kit rather than through the cloth: the same soldiers, wearing
            // a colour that says do not shoot this one.
            if (s.Allied)
            {
                webbing = Palette.BatteryCore;
                steel = GridRenderer.LerpColor(Palette.SoldierSteel, Palette.BatteryCore, 0.6f);
            }

            _figure.DrawFlier(at, s.Height, s.Heading, cameraPos,
                cloth, webbing, steel, steel,
                GridRenderer.LerpColor(Palette.SoldierBlade, Palette.NeonMagenta, rot),
                flight,
                EnemySoldier.Scale);

            // The tell. A soldier who has been given the turn carries a hard white spark at
            // the chest for the whole of their run — the one piece of information the player
            // cannot afford to have to work out from four silhouettes at once. It is drawn
            // rather than HUD-marked on purpose: it lives in the world, so it is occluded by
            // the buildings they are swinging behind, exactly as they are.
            var chest = new Vector3(at.X, s.Height + EnemySoldier.AimHeight, at.Y);

            if (s.BladesOut)
            {
                float pulse = 0.9f + 0.1f * MathF.Sin(elapsed * 24f);
                Raylib.DrawSphereEx(chest, 0.16f * pulse, 5, 5, new Color(255, 255, 255, 200));
            }

            // A seeded body carries the corruption visibly working on it: a hot mote of the
            // stuff at the chest, stuttering rather than pulsing, so the window the player
            // has to fly into them is something they can see closing.
            if (s.Tagged > 0f)
            {
                float flicker = 0.55f + 0.45f * MathF.Sin(elapsed * 31f + s.Slot);
                var seed = Palette.NeonMagenta;
                Raylib.DrawSphereEx(chest, (0.3f + 0.25f * flicker) * EnemySoldier.Scale * 0.5f,
                    6, 6, new Color(seed.R, seed.G, seed.B, (byte)(190 * flicker)));
            }
        }
    }

    /// <summary>
    /// Where one of their cables leaves the body: out from the hip on its own side, at belt
    /// height. Mirrors <c>World.SoldierMuzzle</c>, which is where the player's cables leave
    /// from — the same rig, so the same geometry.
    /// </summary>
    private static Vector3 Launcher(Vector2 at, EnemySoldier s, bool right)
    {
        var fwd = new Vector2(MathF.Sin(s.Heading), MathF.Cos(s.Heading));
        var side = new Vector2(-fwd.Y, fwd.X) * (right ? 0.5f : -0.5f) * EnemySoldier.Scale;
        return new Vector3(
            at.X + side.X + fwd.X * 0.35f * EnemySoldier.Scale,
            s.Height + EnemySoldier.HarnessHeight,
            at.Y + side.Y + fwd.Y * 0.35f * EnemySoldier.Scale);
    }
}
