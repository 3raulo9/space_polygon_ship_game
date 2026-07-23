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

    // The skyline. Not an entity in any sense — it never updates and nothing in the
    // sim owns it — but it is drawn inside the same 3D pass as everything else, so it
    // is held here rather than making the Renderer juggle a second sub-renderer.
    private readonly StructureRenderer _structures = new();

    // The Crab-Core boss is a posed rig, not a single mesh — its own renderer owns
    // the parts and places them from a per-frame pose.
    private readonly CrabRenderer _crab = new();

    // ...and so is the Maw-Core, which shares two of the crab's parts outright.
    private readonly MawRenderer _maw = new();

    // --- The player's own chassis, for the hangar's turntable -------------------
    // Only ever drawn on the class-select screen: the run itself is first-person, so
    // the craft the player picked is a thing they see exactly once, before they climb
    // into it. Built white and tinted per part at draw time so the paint bay can
    // repaint them every frame for free.
    private readonly PolyMesh _playerHull = Meshes.TankHull(Color.White);
    private readonly PolyMesh _playerCap = Meshes.TankCap(Color.White);
    private readonly PolyMesh _playerBarrel = Meshes.TankBarrel(Color.White);

    // The SOLDIER: an articulated figure rather than a mesh, since it is a person and a
    // rigid person is a statue. Only ever seen on the hangar's turntable — out in the
    // run this chassis is a pair of forearms and two cables, and the person holding them
    // is never on screen.
    private readonly SoldierModel _soldierModel = new();

    /// <summary>The soldier's cables and hooks in the live world — drawn from the rig's
    /// own state, not from a mesh, because a cable's shape is decided every frame by
    /// where its anchor is and how hard it is pulling.</summary>
    private readonly SoldierRenderer _soldier = new();

    // The FISH: articulated for the same reason the soldier is and then some — a rigid
    // fish is not a statue, it is a dead fish. Seen in full on the turntable, and in
    // pieces out in the run, where the player is inside its head.
    private readonly FishModel _fishModel = new();

    // The VIRUS: not a body at all but a mote of corruption trailing a shed husk. Only ever
    // seen whole on the turntable — out in the run this chassis wears other things, and what
    // the player sees is the infection over the frame rather than a craft in front of them.
    private readonly VirusModel _virusModel = new();

    /// <summary>The parts of the fish's own body that hang in the player's view — the
    /// snout, the pectorals and the lantern. Drawn in the camera's frame rather than the
    /// world's, so they stay welded to the eye through a forty-degree carve.</summary>
    private readonly FishRenderer _fish = new();

    // The SPIDER is the boss's rig at a person's size, so it gets its own CrabRenderer
    // rather than borrowing the one above — that one is carrying the live boss's scale
    // and gunmetal, and neither belongs on the player's craft.
    private readonly CrabRenderer _spiderRig = new() { Scale = CrabRig.PlayerScale };

    /// <summary>
    /// Draws the skyline on its own, without a world. The title and settings screens
    /// drift a camera over the empty grid with no sim behind it, and the whole point of
    /// the city being a fixed feature of the torus rather than per-run scenery is that
    /// those screens can show the same one the player is about to drive into.
    /// </summary>
    public void DrawStructures(Vector3 cameraPos)
        => _structures.Draw(VoidTanks.World.StructureField.Backdrop, cameraPos);

    public void Draw(World.World world, Vector3 cameraPos)
    {
        // The world is a torus, so every world thing is drawn at its nearest image
        // across the wrap — the copy of it that sits closest to the eye. Something just
        // over the world's edge is then drawn just past the player rather than a whole
        // arena away, which is what keeps the seam invisible as they roam over it.
        var eyeXZ = new Vector2(cameraPos.X, cameraPos.Z);

        // The city first: it is the backdrop everything else is fought in front of. This
        // stage's own copy, not the shared backdrop — the towers this run has cut down
        // are down here and nowhere else.
        _structures.Draw(world.Structures, cameraPos);

        // The lone Stalker, if the stage seeded one, drawn from its live pose. Once
        // it's fully dead the rig is gone; while dying it draws mid-glitch-apart.
        if (world.Boss is { } boss && !boss.Dead)
        {
            Vector2 bossPos = Torus.NearestImage(boss.Position, eyeXZ);
            _crab.Draw(boss.Pose, bossPos, boss.Heading, cameraPos, boss.DeathProgress);
            // Its beam attack, if it is charging or burning — drawn straight after the
            // rig so the shaft leaves a crystal that has already been placed. The same
            // wrap shift is handed to it so the lance leaves the re-imaged crystal.
            if (boss.Alive) _crab.DrawLance(boss, bossPos - boss.Position);
        }

        // The hanging mouth. Its drool and its lasers are drawn after the rig so both
        // read as coming off a body that has already been placed — and the drips keep
        // falling right through the death glitch, which is exactly right: the stuff
        // was already leaking out of it before it died.
        if (world.Maw is { } maw && !maw.Dead)
        {
            Vector2 mawPos = Torus.NearestImage(maw.Position, eyeXZ);
            Vector2 mawShift = mawPos - maw.Position;
            _maw.Draw(maw.Pose, mawPos, maw.BodyY, cameraPos, maw.DeathProgress);
            _maw.DrawDrips(maw, cameraPos, mawShift);
            _maw.DrawLasers(maw, cameraPos, mawShift);
        }

        foreach (var e in world.Enemies)
        {
            if (!e.Alive) continue;
            var mesh = e.IsElite ? _eliteCone : _standardTank;
            // Scale the mesh by the same factor the hitbox uses, so the visible
            // body and the collision radius are one and the same.
            mesh.Draw(Torus.NearestImage(e.Position, eyeXZ), e.Heading, 0f, cameraPos, EnemyTank.Scale);
        }

        // Floating pickups: bob at waist height and turn slowly on the spot, so the
        // charge band and bullet tip catch the light as they drift in the fog. A CRAB
        // CORE fragment reuses the battery cell's shape flooded neon-red, so it reads as
        // a hot shard of the thing it fell out of.
        foreach (var pk in world.Pickups)
        {
            Vector2 at = Torus.NearestImage(pk.Position, eyeXZ);
            if (pk.Kind == PickupKind.CrabFragment)
                _battery.Draw(at, pk.Spin, pk.BobHeight, cameraPos, 1f, Palette.NeonRed);
            else
                (pk.Kind == PickupKind.Battery ? _battery : _bullet)
                    .Draw(at, pk.Spin, pk.BobHeight, cameraPos);
        }

        foreach (var p in world.Projectiles)
        {
            if (!p.Active) continue;
            Vector2 shotPos = Torus.NearestImage(p.Position, eyeXZ);
            // Draw each bolt at its own height, so a shot fired mid-jump visibly
            // rides high — level with the leap — and sinks toward the horizon, rather
            // than snapping back to barrel height. The heavy grenade is a fatter slug
            // in the elite/orange colour.
            if (p.IsRocket) DrawRocket(p, shotPos);
            else if (p.IsGrenade) _grenade.Draw(shotPos, 0f, p.Height, cameraPos);
            else if (p.IsLaser) DrawLaserStreak(p, shotPos);
            else if (p.IsAcid) DrawAcidBolt(p, shotPos);
            else if (p.IsTracer) DrawTracer(p, shotPos, cameraPos);
            else _bolt.Draw(shotPos, 0f, p.Height, cameraPos);
        }

        // The SPIDER's lance: the gathering flare in its core while the meter fills,
        // then the shaft itself. Drawn through the boss's own lance renderer at the
        // salvaged emitter's smaller reach — it is literally the same weapon, cut down,
        // so it should be the same light.
        if (world.Player.Spider is { } spider)
        {
            if (spider.Charging || spider.BeamActive)
            {
                // The live flare rides the craft's heading, and the shaft leaves along the
                // full look line — up or down wherever the ring is aimed.
                Vector2 look = world.Player.Forward;
                Vector2 muzzleXZ = world.Player.Position + look * SpiderWeapon.MuzzleForward;

                // While charging the flare rides the live craft; once fired the shaft
                // stays where it was loosed from, so a player who turns mid-burn sees
                // the beam hold its line rather than sweep round with them.
                Vector3 origin = spider.BeamActive
                    ? spider.BeamOrigin
                    : new Vector3(muzzleXZ.X,
                        SpiderWeapon.MuzzleHeight + world.Player.Height, muzzleXZ.Y);
                Vector3 dir = spider.BeamActive
                    ? spider.BeamDirection
                    : world.Player.Forward3;

                _crab.DrawLance(origin, dir,
                    spider.Charging ? spider.ChargeFraction : 0f,
                    spider.BeamProgress,
                    SpiderWeapon.BeamLength,
                    SpiderWeapon.BeamRadius * (0.45f + 0.55f * spider.BeamPower),
                    SpiderWeapon.FlareScale);
            }
        }

        // Thrown CRAB CORE detonations: a cinematic energy burst. A floating light core
        // throws tapering lances out in every direction at once, the whole spray churning
        // fluidly, wrapped in a breathing bubble of energy — all of it swelling in, then
        // shrinking away to nothing over three seconds (the entity's envelope).
        foreach (var blast in world.Blasts)
        {
            if (!blast.Active) continue;
            float env = blast.Envelope;
            if (env <= 0.001f) continue;

            Vector2 atXZ = Torus.NearestImage(blast.Position, eyeXZ);
            var center = new Vector3(atXZ.X, CrabCoreBlast.CoreHeight, atXZ.Y);
            float len = blast.BeamLength;

            float t = (float)Raylib.GetTime();
            float flutter = 0.9f + 0.1f * MathF.Sin(t * 33f); // the shaft boils, not sits
            float sheath = 1.15f * env * flutter;
            float core = sheath * 0.45f;
            var red = Palette.NeonRed;
            var redSheath = new Color(red.R, red.G, red.B, (byte)(150 * env));

            for (int b = 0; b < CrabCoreBlast.BeamCount; b++)
            {
                Vector3 to = center + blast.Direction(b) * len;
                // White core, red sheath over it — the boss lance's own look, tapering
                // from fat at the core to a point at the tip.
                Raylib.DrawCylinderEx(center, to, core * 1.2f, core * 0.15f, 6, Color.White);
                Raylib.DrawCylinderEx(center, to, sheath * 1.3f, sheath * 0.15f, 6, redSheath);
            }

            // The bubble of energy breathing around the core.
            float bubble = blast.BubbleRadius;
            if (bubble > 0.05f)
            {
                var mag = Palette.NeonMagenta;
                Raylib.DrawSphereEx(center, bubble, 12, 12,
                    new Color(mag.R, mag.G, mag.B, (byte)(70 * env)));
                Raylib.DrawSphereEx(center, bubble * 0.6f, 10, 10,
                    new Color((int)255, 130, 170, (int)(95 * env)));
            }

            // The blown-out core itself, pulsing at the centre of it all.
            Raylib.DrawSphereEx(center, (1.5f + 0.6f * MathF.Sin(t * 20f)) * env, 8, 8, Color.White);
        }

        // The VIRUS's stolen lance, mid-break: every live shaft of the last discharge,
        // drawn through the boss's own lance renderer because it is literally the same
        // light — one beam per direction the corrupted core threw it, each flickering on
        // its own phase, because a stable shaft would be the one thing this weapon
        // cannot produce.
        if (world.Player.Virus is { } virusRig)
        {
            float now = (float)Raylib.GetTime();
            for (int i = 0; i < virusRig.Shafts.Length; i++)
            {
                ref readonly var shaft = ref virusRig.Shafts[i];
                if (shaft.Life <= 0f) continue;

                float progress = 1f - shaft.Life / Entities.VirusRig.LanceBurnTime;
                float flicker = 0.55f + 0.45f * MathF.Sin(now * 70f + i * 2.4f);
                _crab.DrawLance(shaft.Origin, shaft.Dir, 0f, progress,
                    Entities.VirusRig.LanceLength,
                    Entities.VirusRig.LanceRadius * flicker,
                    0.4f);
            }
        }

        // The SOLDIER's rig: both cables out to wherever their hooks have got to, and
        // the forearms holding the launchers. Drawn near the end so the cables pass in
        // front of the city they are anchored to rather than through it, and so the
        // viewmodel — which sits half a metre from the eye — is over everything.
        _soldier.Draw(world, cameraPos, (float)Raylib.GetTime());

        // And the FISH's own body, for the same reason and in the same slot: it sits
        // centimetres from the eye and has to be over everything the run put behind it.
        _fish.Draw(world, cameraPos, (float)Raylib.GetTime());

        // A TANK's screening smoke: a soft bank of murk drawn as a small clutch of translucent
        // spheres, swelling and fading with the cloud's own density. Drawn late so it hangs in
        // front of the city it is hiding, and kept deliberately cheap — a few spheres, not a
        // particle field, which at this resolution is the same picture at a fraction of the cost.
        foreach (var cloud in world.Smoke)
        {
            float d = cloud.Density;
            if (d <= 0.02f) continue;
            Vector2 at = Torus.NearestImage(cloud.Position, eyeXZ);
            float r = cloud.Radius;
            var c = Palette.StructureShell;
            byte a0 = (byte)(120 * d);
            byte a1 = (byte)(96 * d);
            var centre = new Vector3(at.X, 1.8f, at.Y);
            Raylib.DrawSphereEx(centre, r, 8, 8, new Color(c.R, c.G, c.B, a0));
            Raylib.DrawSphereEx(centre + new Vector3(r * 0.5f, 0.5f, r * 0.3f), r * 0.7f, 7, 7,
                new Color(c.R, c.G, c.B, a1));
            Raylib.DrawSphereEx(centre + new Vector3(-r * 0.45f, 0.2f, -r * 0.5f), r * 0.72f, 7, 7,
                new Color(c.R, c.G, c.B, a1));
        }

        // Death debris last: chunks and sparks flung from destroyed enemies, each
        // shrinking and fading toward the void over its short life.
        foreach (var s in world.Debris.Shards)
        {
            if (!s.Active) continue;
            var posXZ = Torus.NearestImage(new Vector2(s.Position.X, s.Position.Z), eyeXZ);
            float f = s.LifeFrac;
            Color tint = GridRenderer.LerpColor(s.Color, Palette.Void, 1f - f);
            var mesh = s.IsSpark ? _spark : _shard;
            // Chunks shrink as they die; sparks stay small and just wink out.
            float size = s.IsSpark ? s.Size : s.Size * (0.4f + 0.6f * f);
            mesh.Draw(posXZ, s.Angle, s.Position.Y, cameraPos, size, tint);
        }
    }

    /// <summary>
    /// A SPIDER laser: a short neon streak lying along its own flight path rather than
    /// the cannon's tumbling octahedron. Drawn as a thin cylinder for the same reason
    /// the boss's lance is — light is the one thing out here not built out of polygons
    /// — and cored white inside red so it reads as the same emitter's work at a
    /// hundredth the size.
    /// </summary>
    private static void DrawLaserStreak(Projectile p, Vector2 at)
    {
        const float len = 1.6f;
        Vector2 dir = p.Velocity.LengthSquared() > 1e-4f
            ? Vector2.Normalize(p.Velocity) : new Vector2(0f, 1f);
        var tail = new Vector3(at.X - dir.X * len, p.Height, at.Y - dir.Y * len);
        var head = new Vector3(at.X, p.Height, at.Y);

        var red = Palette.NeonRed;
        Raylib.DrawCylinderEx(tail, head, 0.10f, 0.16f, 6,
            new Color(red.R, red.G, red.B, (byte)190));
        Raylib.DrawCylinderEx(tail, head, 0.04f, 0.07f, 6, Color.White);
    }

    /// <summary>
    /// A SOLDIER's rifle round: a long thin tracer lying along its own flight, drawn
    /// hot at the head and fading out behind. The cannon's tumbling octahedron is a
    /// shell you can watch cross the arena; this is a bullet, and what you see of a
    /// bullet is the streak it leaves.
    /// </summary>
    private static void DrawTracer(Projectile p, Vector2 at, Vector3 cameraPos)
    {
        var head = new Vector3(at.X, p.Height, at.Y);

        // A tracer is a streak lying along its own flight, which makes it the one thing
        // in the game that must not be drawn near the eye: a three-metre cylinder on a
        // round two metres out reaches back past the camera and renders as a smear
        // across the middle of the frame, pointing nowhere. Rounds leave at ninety
        // metres a second, so skipping the first few units costs two frames of a flight
        // the player was never going to see anyway.
        float range = Vector3.Distance(head, cameraPos);
        if (range < NearTracer) return;

        Vector3 dir = p.Heading3;
        // And the tail is clamped so it can never reach back to the eye either.
        float len = MathF.Min(3.2f, range - NearTracer * 0.5f);

        Vector3 tail = head - dir * len;
        var glow = Palette.Flag;
        Raylib.DrawCylinderEx(tail, head, 0.015f, 0.07f, 4,
            new Color(glow.R, glow.G, glow.B, (byte)150));
        Raylib.DrawCylinderEx(Vector3.Lerp(tail, head, 0.55f), head, 0.02f, 0.05f, 4, Color.White);
    }

    /// <summary>How close a tracer may get to the eye before it stops being drawn.</summary>
    private const float NearTracer = 4f;

    /// <summary>
    /// A worn maw's acid bolt: a slow green orb with a hot pip in it — the Maw-Core's own
    /// laser language, fired by the player wearing the mouth. Drawn as spheres rather than
    /// a streak because the monster's bolts always were: slow ordnance you watch coming is
    /// round, fast ordnance you only ever see the trail of is a line.
    /// </summary>
    private static void DrawAcidBolt(Projectile p, Vector2 at)
    {
        var centre = new Vector3(at.X, p.Height, at.Y);
        float pulse = 0.85f + 0.15f * MathF.Sin((float)Raylib.GetTime() * 21f);

        var acid = Palette.MawLaser;
        Raylib.DrawSphereEx(centre, 0.34f * pulse, 6, 6,
            new Color(acid.R, acid.G, acid.B, (byte)190));
        Raylib.DrawSphereEx(centre, 0.14f * pulse, 5, 5, Color.White);
    }

    /// <summary>
    /// A rocket: the fin-stabilised body, its motor burning behind it, and a smoke
    /// ribbon trailing off that. The ribbon is drawn as a few fading segments back along
    /// the flight rather than as a particle system — at 320×240 the difference is
    /// invisible and the cost is four cylinders instead of forty.
    /// </summary>
    private static void DrawRocket(Projectile p, Vector2 at)
    {
        Vector3 dir = p.Heading3;
        var nose = new Vector3(at.X, p.Height, at.Y);
        Vector3 tail = nose - dir * 0.9f;

        // Body, then the fins as one flared collar at the back.
        Raylib.DrawCylinderEx(tail, nose, 0.16f, 0.05f, 6, Palette.HudChrome);
        Raylib.DrawCylinderEx(tail - dir * 0.12f, tail + dir * 0.18f, 0.26f, 0.14f, 4,
            Palette.CrabChassis);

        // The motor: a bright cone blowing out of the back, flickering.
        float flicker = 0.75f + 0.25f * MathF.Sin((float)Raylib.GetTime() * 47f);
        Raylib.DrawCylinderEx(tail, tail - dir * (1.5f * flicker), 0.13f, 0.02f, 5,
            new Color(255, 232, 180, 235));

        // And the smoke ribbon behind it, thinning and fading with distance back.
        for (int i = 1; i <= 4; i++)
        {
            Vector3 a = tail - dir * (1.4f * i);
            Vector3 b = tail - dir * (1.4f * (i + 1));
            int alpha = 110 - i * 22;
            float r = 0.24f + i * 0.1f;
            Raylib.DrawCylinderEx(a, b, r, r + 0.1f, 5, new Color(150, 150, 158, alpha));
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
    /// Draws the player's chosen chassis on the hangar's turntable, painted in whatever
    /// the paint bay currently holds. An offline chassis has no model — the screen says
    /// so in words instead, so nothing is drawn here and the middle of the hangar is
    /// left as empty grid, which is the honest picture of a build the machine can't make.
    /// </summary>
    public void DrawLoadoutShowcase(Loadout loadout, Vector2 pos, float heading,
        Vector3 cameraPos, float elapsed)
    {
        var arch = ClassCatalog.Get(loadout.Class);
        if (!arch.Available) return;

        switch (loadout.Class)
        {
            case PlayerClass.Tank:
                _playerHull.Draw(pos, heading, 0f, cameraPos, 1f, loadout.PartColor(PlayerClass.Tank, 0));
                _playerCap.Draw(pos, heading, 0f, cameraPos, 1f, loadout.PartColor(PlayerClass.Tank, 1));
                _playerBarrel.Draw(pos, heading, 0f, cameraPos, 1f, loadout.PartColor(PlayerClass.Tank, 2));
                break;

            case PlayerClass.Spider:
                _spiderRig.UpperTint = loadout.PartColor(PlayerClass.Spider, 0);
                _spiderRig.LowerTint = loadout.PartColor(PlayerClass.Spider, 1);
                _spiderRig.LegTint = loadout.PartColor(PlayerClass.Spider, 2);
                // A slow idle rather than a pose lifted off the boss's protocol: the
                // gem turns, the legs breathe, and the carapace stays shut. Nothing the
                // hangar shows should look like the thing is winding up to do something.
                var pose = new CrabPose(
                    CoreSpin: elapsed * 1.3f,
                    ClawOpen: 0f,
                    LegPhase: elapsed * 1.8f,
                    CoreColor: loadout.PartColor(PlayerClass.Spider, 3),
                    SlideOffset: Vector2.Zero);
                _spiderRig.Draw(pose, pos, heading, cameraPos);
                break;

            case PlayerClass.Soldier:
                // Posed rather than merely turned: it breathes, shifts its weight, scans
                // the hangar and periodically raises a launcher to check the hook in it.
                _soldierModel.Draw(loadout, pos, heading, cameraPos, elapsed);
                break;

            case PlayerClass.Fish:
                // Swimming on the spot: the wave runs down its body, the pectorals scull
                // to hold it level, the lantern trails, and every few seconds it gulps.
                // The one specimen in the hangar that never touches the turntable.
                _fishModel.Draw(loadout, pos, heading, cameraPos, elapsed);
                break;

            case PlayerClass.Virus:
                // Hanging in the air like the fish, but nothing so settled: the mote spins,
                // its veins cast about for a body, and the husk of the last thing it wore
                // tumbles around it. The turntable heading turns the whole cloud slowly.
                _virusModel.Draw(loadout, pos, heading, cameraPos, elapsed);
                break;
        }
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
