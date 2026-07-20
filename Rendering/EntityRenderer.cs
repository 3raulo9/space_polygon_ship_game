using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.Entities;

namespace VoidTanks.Rendering;

/// <summary>
/// Draws the flat-shaded solids (enemies, projectiles) inside the 3D pass.
/// Meshes are built once and shared. Everything fades into fog and pops in at
/// the boundary via <see cref="PolyMesh"/>; nothing casts a shadow or is grounded
/// to the grid — the rootless float is part of the wrongness (Doc 02).
/// </summary>
public sealed class EntityRenderer
{
    private readonly PolyMesh _standardTank = Meshes.Tank(Palette.EnemyFill);
    private readonly PolyMesh _eliteCone = Meshes.EliteCone(Palette.EliteFill);
    private readonly PolyMesh _bolt = Meshes.Bolt(Palette.Flag);
    private readonly PolyMesh _grenade = Meshes.Grenade(Palette.EliteFill);

    // Floating salvage: a battery cell (shield + hyper) and a stray round (ammo).
    private readonly PolyMesh _battery = Meshes.Battery(Palette.BatteryFill, Palette.BatteryCore);
    private readonly PolyMesh _bullet = Meshes.Bullet(Palette.Flag, Palette.HudChrome);

    // Death debris: a jagged chunk and a bright spark. Both are drawn white and
    // tinted per-instance so each piece can carry its own (fading) colour.
    private readonly PolyMesh _shard = Meshes.Shard(Color.White);
    private readonly PolyMesh _spark = Meshes.Bolt(Color.White);

    // Placeholder polygon ships — built only for the test screen's turntable.
    private readonly PolyMesh _shipInterceptor = Meshes.ShipInterceptor(Palette.EnemyFill);
    private readonly PolyMesh _shipGunship = Meshes.ShipGunship(Palette.EliteFill);
    private readonly PolyMesh _shipScout = Meshes.ShipScout(Palette.PlayerFill);

    // The Crab-Core boss is a posed rig, not a single mesh — its own renderer owns
    // the parts and places them from a per-frame pose.
    private readonly CrabRenderer _crab = new();

    // ...and so is the Maw-Core, which shares two of the crab's parts outright.
    private readonly MawRenderer _maw = new();

    public void Draw(World.World world, Vector3 cameraPos)
    {
        // The lone Stalker, if the stage seeded one, drawn from its live pose. Once
        // it's fully dead the rig is gone; while dying it draws mid-glitch-apart.
        if (world.Boss is { } boss && !boss.Dead)
        {
            _crab.Draw(boss.Pose, boss.Position, boss.Heading, cameraPos, boss.DeathProgress);
            // Its beam attack, if it is charging or burning — drawn straight after the
            // rig so the shaft leaves a crystal that has already been placed.
            if (boss.Alive) _crab.DrawLance(boss);
        }

        // The hanging mouth. Its drool and its lasers are drawn after the rig so both
        // read as coming off a body that has already been placed — and the drips keep
        // falling right through the death glitch, which is exactly right: the stuff
        // was already leaking out of it before it died.
        if (world.Maw is { } maw && !maw.Dead)
        {
            _maw.Draw(maw.Pose, maw.Position, maw.BodyY, cameraPos, maw.DeathProgress);
            _maw.DrawDrips(maw, cameraPos);
            _maw.DrawLasers(maw, cameraPos);
        }

        foreach (var e in world.Enemies)
        {
            if (!e.Alive) continue;
            var mesh = e.IsElite ? _eliteCone : _standardTank;
            // Scale the mesh by the same factor the hitbox uses, so the visible
            // body and the collision radius are one and the same.
            mesh.Draw(e.Position, e.Heading, 0f, cameraPos, EnemyTank.Scale);
        }

        // Floating pickups: bob at waist height and turn slowly on the spot, so the
        // charge band and bullet tip catch the light as they drift in the fog.
        foreach (var pk in world.Pickups)
        {
            var mesh = pk.Kind == PickupKind.Battery ? _battery : _bullet;
            mesh.Draw(pk.Position, pk.Spin, pk.BobHeight, cameraPos);
        }

        foreach (var p in world.Projectiles)
        {
            if (!p.Active) continue;
            // Draw each bolt at its own height, so a shot fired mid-jump visibly
            // rides high — level with the leap — and sinks toward the horizon, rather
            // than snapping back to barrel height. The heavy grenade is a fatter slug
            // in the elite/orange colour.
            if (p.IsGrenade) _grenade.Draw(p.Position, 0f, p.Height, cameraPos);
            else _bolt.Draw(p.Position, 0f, p.Height, cameraPos);
        }

        // Death debris last: chunks and sparks flung from destroyed enemies, each
        // shrinking and fading toward the void over its short life.
        foreach (var s in world.Debris.Shards)
        {
            if (!s.Active) continue;
            var posXZ = new Vector2(s.Position.X, s.Position.Z);
            float f = s.LifeFrac;
            Color tint = GridRenderer.LerpColor(s.Color, Palette.Void, 1f - f);
            var mesh = s.IsSpark ? _spark : _shard;
            // Chunks shrink as they die; sparks stay small and just wink out.
            float size = s.IsSpark ? s.Size : s.Size * (0.4f + 0.6f * f);
            mesh.Draw(posXZ, s.Angle, s.Position.Y, cameraPos, size, tint);
        }
    }

    /// <summary>
    /// Draws a single roster entry as a rotating turntable specimen for the test
    /// screen. Tanks sit on the grid; the ships float, matching the rootless drift
    /// the enemies already have. Returns nothing — purely a display pass.
    /// </summary>
    public void DrawShowcase(EnemyKind kind, Vector2 pos, float heading, Vector3 cameraPos)
    {
        (PolyMesh mesh, float height) = kind switch
        {
            EnemyKind.StandardTank    => (_standardTank, 0f),
            EnemyKind.EliteTank       => (_eliteCone, 0f),
            EnemyKind.ShipInterceptor => (_shipInterceptor, 1.5f),
            EnemyKind.ShipGunship     => (_shipGunship, 1.4f),
            EnemyKind.ShipScout       => (_shipScout, 1.6f),
            _                         => (_standardTank, 0f),
        };
        mesh.Draw(pos, heading, height, cameraPos);
    }

    /// <summary>
    /// Draws the Crab-Core boss for the test screen, looping a single protocol
    /// phase so the tester can study each animation in isolation. Held at a fixed
    /// three-quarter heading (no turntable spin) so the mechanical slides, clamps
    /// and skitter read cleanly.
    /// </summary>
    public void DrawCrabShowcase(CrabCore.State phase, Vector2 pos, float elapsed, Vector3 cameraPos)
    {
        const float showHeading = 0.6f; // a slight turn off head-on
        CrabPose pose = CrabCore.ShowcasePose(phase, elapsed);
        _crab.Draw(pose, pos, showHeading, cameraPos);

        // The lance phases get their light too, aimed off the pose's own lean, so the
        // turntable shows where the tilt is actually pointing the crystal.
        if (phase is not (CrabCore.State.Aiming or CrabCore.State.Firing)) return;

        var origin = new Vector3(pos.X, CrabRig.CoreWorldY + 3.0f, pos.Y);
        float cp = MathF.Cos(pose.BodyPitch);
        var dir = new Vector3(MathF.Sin(showHeading) * cp, -MathF.Sin(pose.BodyPitch),
                              MathF.Cos(showHeading) * cp);

        if (phase == CrabCore.State.Aiming)
        {
            // Re-derive the charge from the same loop the pose runs on.
            float lt = elapsed % (CrabCore.ChargeTime + 0.6f);
            _crab.DrawLance(origin, dir, Math.Clamp(lt / CrabCore.ChargeTime, 0f, 1f), -1f);
        }
        else
        {
            _crab.DrawLance(origin, dir, 0f, elapsed % CrabCore.BeamTime / CrabCore.BeamTime);
        }
    }

    /// <summary>
    /// Draws the Maw-Core for the test screen: hanging at its real world height, with
    /// the crystal and both tooth rings turning. Held still rather than spun on the
    /// turntable — the rings are already rotating, and adding a third rotation on top
    /// of them makes the grind impossible to read.
    ///
    /// Drawn at its true <see cref="MawRig.BodyWorldY"/> rather than at a framing
    /// height chosen for the picture, so the bestiary shows exactly how high off the
    /// grid the thing actually floats — which is the single fact about it a tester
    /// most needs to be able to check.
    /// </summary>
    public void DrawMawShowcase(Vector2 pos, float elapsed, Vector3 cameraPos)
    {
        MawPose pose = MawCore.ShowcasePose(elapsed);
        _maw.Draw(pose, pos, MawRig.BodyWorldY, cameraPos);
    }
}
