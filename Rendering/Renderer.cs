using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.Entities;
using VoidTanks.UI;

namespace VoidTanks.Rendering;

/// <summary>
/// Owns the low-res render target and the nearest-neighbor upscale (Doc 05).
/// The 3D scene is drawn into a ~320x240 texture, then blitted to the window at
/// an integer scale with no smoothing and letterboxed remainder. This single
/// decision does most of the retro heavy lifting.
/// </summary>
public sealed class Renderer : IDisposable
{
    private RenderTexture2D _target;
    // A tiny buffer used only by the pause pixel-blur: the frozen frame is
    // downsampled into this at a fraction of the resolution, then blown back up
    // over the sharp frame. Sized to the low res so every blit is a full-texture
    // read — partial-rect reads of a flipped render texture misbehave.
    private RenderTexture2D _scratch;
    private const int BlurW = Config.InternalWidth / PauseBlock;   // 40
    private const int BlurH = Config.InternalHeight / PauseBlock;  // 30
    private const int PauseBlock = 8; // world px per mosaic block at full pause

    /// <summary>Planar speed at which the SOLDIER's lens begins to stretch. Set to the
    /// same 20 m/s the vignette and the wind come in at, so the whole "this is fast now"
    /// language arrives as one change rather than three.</summary>
    private const float SoldierFovSpeed = 20f;

    /// <summary>The same for the FISH, set to the same 22 m/s its murk and its bubbles
    /// come in at, so that chassis's "this is fast now" language also arrives as one
    /// change rather than three.</summary>
    private const float FishFovSpeed = 22f;
    private Camera3D _camera;
    private readonly EntityRenderer _entities = new();
    // Renders the inventory's items as small rotating 3D models (see DrawInventory).
    private readonly ItemIconRenderer _itemIcons = new();

    public Renderer()
    {
        _target = Raylib.LoadRenderTexture(Config.InternalWidth, Config.InternalHeight);
        _scratch = Raylib.LoadRenderTexture(BlurW, BlurH);
        // Nearest-neighbor: hard, chunky pixels. No filtering, ever.
        Raylib.SetTextureFilter(_target.Texture, TextureFilter.Point);
        Raylib.SetTextureFilter(_scratch.Texture, TextureFilter.Point);

        // Draw every facet of a solid, front- and back-facing alike. The meshes
        // are hand-wound and not all consistently oriented; with culling on, the
        // "wrong" faces vanish and you see into the model (the folded-paper look).
        // PolyMesh shades two-sided, so drawing them all reads as a solid instead.
        Rlgl.DisableBackfaceCulling();

        _camera = new Camera3D
        {
            Up = new Vector3(0f, 1f, 0f),
            FovY = Config.CameraFovY,
            Projection = CameraProjection.Perspective,
        };
    }

    /// <summary>Renders the world from the player's eye into the low-res target.</summary>
    public void DrawWorld(World.World world)
    {
        PlayerTank player = world.Player;

        // First-person eye: sits at the chassis's own eye height above the craft,
        // looking down its heading. The jump lifts the eye with the craft. A soldier's
        // eye is barely half a tank's off the ground, which is most of why the same city
        // reads as something to be small inside rather than something to drive past.
        var eye = new Vector3(
            player.Position.X,
            player.EyeHeight + player.Height,
            player.Position.Y);

        var fwd = player.Forward;

        // The nearer an active Crab-Core stalks, the harder the whole rig judders —
        // a translational rumble on the eye plus an extra rotational rattle on the
        // aim point, so the closer it gets the less steady the world holds.
        float shake = world.Boss is { } b ? b.ProximityShake(player.Position) : 0f;
        // Whichever set piece owns the camera this frame — the crab's seizure or the
        // maw's digestion. Both drive the same four channels, so nothing below has to
        // know which of them is running.
        var seizure = world.Cinematic;

        // Being held in its claw dwarfs merely standing near it, so the cinematic's
        // judder replaces the proximity rumble rather than adding to it — and at an
        // order of magnitude more amplitude. Taking the larger of the two also means
        // the shake never dips as the seizure hands back control: the ring-down in
        // the recovery stage crosses the proximity level and blends straight into it.
        // The 0.28 ceiling is the loudest the view is ever thrown, reached only on the
        // frame the claw connects. At the internal 320x240 the eye's translation is
        // magnified hard by the upscale, so this is a much larger effect on screen
        // than the number suggests — the quiet stages of the cinematic sit an order of
        // magnitude below it, which is what leaves the scream and the blow room to land.
        float amp = 0.035f * shake;
        if (seizure != null) amp = MathF.Max(amp, 0.28f * seizure.Shake);
        // A rocket going off in the soldier's face, a swing that ended in a wall, or a
        // fall that nearly ended the run. Pitched between the two above: harder than
        // standing near a stalking crab, well short of being held in its claw.
        if (player.Soldier is { Shake: > 0f } jolted)
            amp = MathF.Max(amp, 0.16f * jolted.Shake);
        // A fish spearing something, meeting a wall, or being chewed on by the bloom.
        // Same band as the soldier's — these are things happening to a body, not to a hull.
        if (player.Fish is { Shake: > 0f } struck)
            amp = MathF.Max(amp, 0.16f * struck.Shake);

        Vector3 rumble = Vector3.Zero, rattle = Vector3.Zero;
        if (amp > 0f)
        {
            float t = (float)Raylib.GetTime();
            rumble = new Vector3(
                MathF.Sin(t * 47f) * MathF.Sin(t * 13f),
                MathF.Sin(t * 53f + 1.3f) * MathF.Sin(t * 17f),
                MathF.Sin(t * 43f + 0.7f) * MathF.Sin(t * 11f)) * amp;
            rattle = new Vector3(
                MathF.Sin(t * 61f + 2.1f),
                MathF.Sin(t * 67f + 4.2f), 0f) * (amp * 0.8f);

            // A slow lurch under the fast rattle, only while the cinematic is driving.
            // Fast noise alone reads as a rumble; it takes a low-frequency heave on
            // top to read as something with mass throwing the craft around.
            if (seizure != null)
                rumble += new Vector3(
                    MathF.Sin(t * 7.3f), MathF.Sin(t * 5.1f + 2f), MathF.Sin(t * 6.2f + 1f))
                    * (amp * 0.55f);
        }

        // The cinematic can also roll the horizon and drag the aim off the level —
        // the tumble through the throw, and the view being wrenched up into the core
        // or slammed down at the grid.
        float roll = seizure?.Roll ?? 0f;
        float pitch = seizure?.Pitch ?? 0f;

        // The SOLDIER owns its own camera in three ways no other chassis does: the mouse
        // aims it anywhere including straight up and straight down, an arc banks it like
        // an aircraft, and speed stretches the field of view. All three are the class —
        // the roll especially, which is the single most important thing about how a
        // swing feels — so they are applied on top of whatever a cinematic is doing
        // rather than instead of it.
        float lift = Config.CameraLookLift;
        float fov = Config.CameraFovY;

        if (player.Soldier is { } rig)
        {
            // The look is a genuine angle, and the camera target takes a slope, so this
            // is its tangent. The standing tilt every other chassis holds is dropped
            // outright: a player who can look wherever they want does not also want the
            // horizon quietly pushed down for them.
            //
            // The rifle's recoil rides on top of the aim rather than in it — the shot
            // has already gone, and shifting where the player is actually pointing at
            // six hundred rounds a minute would make the weapon unusable mid-swing.
            lift = MathF.Tan(Math.Clamp(player.Pitch + rig.Recoil,
                -PlayerTank.MaxPitch, PlayerTank.MaxPitch));
            roll += rig.Bank;

            // Knees buckling under a landing: the whole eye drops and springs back.
            eye.Y += rig.Dip;

            // Field of view stretching with speed. Nothing else in the game moves the
            // lens, and it is worth the exception: widening the frame as the player
            // accelerates is what makes thirty metres a second feel like thirty metres a
            // second rather than like a faster walk.
            float fast = Math.Clamp((rig.PlanarSpeed - SoldierFovSpeed) / 16f, 0f, 1f);
            fov *= 1f + 0.28f * fast * fast;
        }

        // The FISH owns its camera in the same three ways and pushes two of them harder.
        // The roll especially: on the soldier it is a readout of an arc the player has no
        // direct say in, whereas here it is the steering itself, so the horizon going over
        // is the player's own hand and it is allowed to go a lot further.
        if (player.Fish is { } body)
        {
            lift = MathF.Tan(Math.Clamp(player.Pitch + body.Recoil,
                -PlayerTank.MaxPitch, PlayerTank.MaxPitch));
            roll += body.Bank;

            // The surge: a beat of the tail shoves the eye forward for a third of a second.
            // Applied to the field of view rather than to the position, because at this
            // resolution translating the camera a few centimetres is invisible and
            // widening the lens for the same third of a second is unmistakable — it is
            // the one cue that makes an impulse feel like an impulse.
            float fast = Math.Clamp((body.PlanarSpeed - FishFovSpeed) / 18f, 0f, 1f);
            fov *= 1f + 0.30f * fast * fast + 0.05f * body.Surge;
        }

        _camera.FovY = fov;
        _camera.Position = eye + rumble;
        // Pitch the eye up a touch so the horizon sits low on screen: that opens
        // up a tall sky band above the floor, where the pink glow can fade all
        // the way to black below the top HUD strip.
        _camera.Target = eye + rumble
                       + new Vector3(fwd.X, lift + pitch, fwd.Y) + rattle;
        // Roll tips the whole world by turning the camera's idea of up. The axis is
        // the craft's own right on the plane, so the horizon pivots about the centre
        // of the view rather than sliding sideways.
        //
        // A chassis that can look straight up needs that axis derived from the look
        // itself rather than from the plane: with the eye near vertical, "the craft's
        // right on the plane" stops being perpendicular to where the camera is pointing
        // and the picture shears. The two agree exactly at level pitch — see the
        // derivation below — so nothing but the two mouse-aimed chassis changes.
        if (player.Soldier != null || player.Fish != null)
        {
            Vector3 look = Vector3.Normalize(new Vector3(fwd.X, lift + pitch, fwd.Y));
            Vector3 side = Vector3.Normalize(Vector3.Cross(look, new Vector3(0f, 1f, 0f)));
            Vector3 up = Vector3.Cross(side, look);
            // At zero pitch this is cos(roll)·up + sin(roll)·(cos h, 0, −sin h), which is
            // the expression below to the letter.
            _camera.Up = roll != 0f
                ? up * MathF.Cos(roll) - side * MathF.Sin(roll)
                : up;
        }
        else
        {
            _camera.Up = roll != 0f
                ? new Vector3(fwd.Y * MathF.Sin(roll), MathF.Cos(roll), -fwd.X * MathF.Sin(roll))
                : new Vector3(0f, 1f, 0f);
        }

        // Render the rotating 3D item icons into their own textures before the world's
        // texture pass opens (a 3D pass can't nest inside it) — the HUD's equip slots
        // blit them the same way the inventory panel does.
        _itemIcons.Render((float)Raylib.GetTime());

        Raylib.BeginTextureMode(_target);
        Raylib.ClearBackground(Palette.Void); // never pure black
        SkyRenderer.Draw(_camera, (float)Raylib.GetTime());

        Raylib.BeginMode3D(_camera);
        GridRenderer.Draw(player.Position);
        _entities.Draw(world, eye);
        Raylib.EndMode3D();

        // The core blazing in the player's face while it screams at them. This is a
        // first-person game, so there is no craft on screen to light up — the only
        // way to show the player caught in that glare is to flood their whole view
        // with it. Drawn over the scene but under the HUD, so the instruments stay
        // readable through the flash.
        if (seizure is { Glow: > 0f }) DrawCoreGlare(seizure.Glow);

        // The SOLDIER's speed effects: the vignette closing in, the wind streaking past.
        // Over the world and under the instruments, same as the glare.
        SoldierRenderer.DrawScreenEffects(world, (float)Raylib.GetTime());

        // And the FISH's: the murk, the bubbles, the bloom staining down from the top of
        // the frame, and the drained look of a body on the seabed. Only one of these two
        // ever does anything on a given run — each returns immediately without its own
        // chassis — so they cost nothing to have both here.
        FishRenderer.DrawScreenEffects(world, (float)Raylib.GetTime());

        // Flat instrument panel over the scene: vital bars + radar along the top, plus
        // the R/T/Y/U equip slots showing their 3D item icons.
        HudRenderer.Draw(world, _itemIcons);

        Raylib.EndTextureMode();
    }

    /// <summary>
    /// Washes the frame in the Crab-Core's core light, by <paramref name="glow"/>
    /// (0..1). The colour rides from the gem's neon magenta toward white as it
    /// intensifies, so the swell of the scream reads as heat building rather than
    /// as the screen simply getting brighter, and the blow — which spikes the glow —
    /// lands as a white flash. Deliberately kept translucent even at full: the boss
    /// should be blinding, but never actually hide itself behind its own light.
    /// </summary>
    private static void DrawCoreGlare(float glow)
    {
        glow = Math.Clamp(glow, 0f, 1f);
        Color hot = GridRenderer.LerpColor(Palette.NeonMagenta, Color.White, glow * glow);
        // Held to 90 rather than anything heavier for a specific reason: the sky in
        // this game is already magenta, so a strong wash flattens the core against its
        // own backdrop and the gem stops reading as the brightest thing in the frame —
        // which defeats the point of holding the player up in front of it.
        Raylib.DrawRectangle(0, 0, Config.InternalWidth, Config.InternalHeight,
            new Color(hot.R, hot.G, hot.B, (int)(90 * glow)));
    }

    /// <summary>
    /// Where the idling camera sits on the UI screens: a slow, aimless creep over the
    /// empty grid, with no player and no craft — just the machine ticking over.
    ///
    /// It travels a wide, very slow arc rather than the straight line it used to, and
    /// that arc is deliberately kept well inside <see cref="World.StructureField.ClearRadius"/>.
    /// Now that there is a city on this map, a drift heading off in one direction forever
    /// eventually wanders into it and parks a forty-metre slab through the middle of the
    /// title. Circling holds the skyline where it belongs — on the horizon, behind the
    /// text — however long the player leaves the menu up.
    /// </summary>
    private static Vector2 IdleDrift(float elapsed)
    {
        const float Radius = 10f;
        const float Rate = 0.07f;   // radians a second: roughly one lap a minute and a half
        return new Vector2(MathF.Sin(elapsed * Rate), MathF.Cos(elapsed * Rate)) * Radius;
    }

    /// <summary>
    /// Renders the title menu into the low-res target so it shares the world's
    /// chunky pixels. A grid drifts slowly behind it — the void is still out
    /// there, waiting — with the UI drawn flat on top. Kept in the Renderer so
    /// the Menu class stays pure state and never touches Raylib.
    /// </summary>
    public void DrawMenu(UI.Menu menu, float elapsed)
    {
        var pos = IdleDrift(elapsed);
        float pan = MathF.Sin(elapsed * 0.05f) * 0.25f;
        var eye = new Vector3(pos.X, Config.CameraHeight + 1.5f, pos.Y);
        _camera.Position = eye;
        _camera.Target = eye + new Vector3(pan, -0.16f, 1f);

        Raylib.BeginTextureMode(_target);
        Raylib.ClearBackground(Palette.Void);
        SkyRenderer.Draw(_camera, elapsed);

        Raylib.BeginMode3D(_camera);
        GridRenderer.Draw(pos);
        // The same dead city the run is fought in, drifting past behind the title. The
        // skyline is a fixed feature of the torus, so this is not a backdrop made for
        // the menu — it is the actual place, seen from wherever the idle drift has got to.
        _entities.DrawStructures(eye);
        Raylib.EndMode3D();

        MenuRenderer.Draw(menu, elapsed);

        Raylib.EndTextureMode();
    }

    /// <summary>
    /// Renders the settings screen over the same drifting-grid backdrop as the
    /// menu, so moving between them feels like one continuous cold terminal.
    /// </summary>
    public void DrawSettings(UI.SettingsScreen screen, float elapsed)
    {
        var pos = IdleDrift(elapsed);
        float pan = MathF.Sin(elapsed * 0.05f) * 0.25f;
        var eye = new Vector3(pos.X, Config.CameraHeight + 1.5f, pos.Y);
        _camera.Position = eye;
        _camera.Target = eye + new Vector3(pan, -0.16f, 1f);

        Raylib.BeginTextureMode(_target);
        Raylib.ClearBackground(Palette.Void);
        SkyRenderer.Draw(_camera, elapsed);

        Raylib.BeginMode3D(_camera);
        GridRenderer.Draw(pos);
        _entities.DrawStructures(eye);
        Raylib.EndMode3D();

        MenuRenderer.DrawSettings(screen, elapsed);

        Raylib.EndTextureMode();
    }

    /// <summary>
    /// Renders the hangar: the chosen chassis turning slowly on the spot over the same
    /// drifting grid as the menu, with the roster / budget / paint panels laid flat on
    /// top. The camera is framed so the model sits in the clear band between the two
    /// side panels rather than centred on the screen — the panels are what the player
    /// is reading, and the craft has to sit beside them, not behind them.
    /// </summary>
    public void DrawClassSelect(UI.ClassSelectScreen screen, float elapsed)
    {
        var specimen = Vector2.Zero;

        // Framing is per chassis, because they are not the same shape. The tank is a
        // compact block and wants the close view; the spider is mostly leg — nearly
        // twice as wide as it is tall — so at the tank's distance its limbs run off the
        // top and sides of the clear band. It gets pulled back and looked down on a
        // little, which also opens the well enough to see the core standing in it,
        // since the core is the part of that chassis the briefing is about.
        bool leggy = screen.Loadout.Class == PlayerClass.Spider;
        // The paint bay's list takes the right two fifths of the screen outright, so the
        // model has to slide over into what's left of it. The camera renders +X on the
        // left of the frame, so pushing the *camera* to -X puts the (stationary) model
        // at +X relative to it — which is what moves the craft leftward on screen
        // rather than dragging the whole scene with it.
        // ...and the soldier is the opposite problem. It is a person: under two metres
        // tall and a third of a tank wide, so at the tank's framing it is a smudge in
        // the middle of an empty hangar. It gets pulled right in and looked at nearly
        // level, which is also the only way the launchers on its hips — the entire point
        // of the chassis — are big enough on screen to be seen at all.
        bool small = screen.Loadout.Class == PlayerClass.Soldier;
        // ...and the fish is a third problem again. It is long and low — two and a half
        // metres nose to fluke and under one tall — so the tank's framing wastes the whole
        // upper half of the clear band on empty hangar while the tail runs off the side.
        // It gets pulled in and looked at from slightly below, which is both how you read
        // a long silhouette and the angle that keeps the lantern against the dark rather
        // than against the grid.
        bool finny = screen.Loadout.Class == PlayerClass.Fish;

        // How far it slides is per chassis for the same reason the framing is: the shift
        // is a distance in the world, and the soldier is looked at from a third of the
        // range, so the tank's 2.6 units throws it clean off the side of the frame.
        float shift = screen.Customising ? (small ? -1.15f : finny ? -0.95f : -2.6f) : 0f;
        var eye = leggy ? new Vector3(shift, 4.8f, -13f)
                : small ? new Vector3(shift, 1.75f, -4.1f)
                : finny ? new Vector3(shift, 1.5f, -5.6f)
                : new Vector3(shift, 3.6f, -9.2f);
        _camera.Position = eye;
        // Aimed below the craft's feet rather than at its middle, which lifts the whole
        // model up the frame and clear of the briefing text along the bottom. The fish
        // needs the same trick for a different reason: it hangs a body's height off the
        // plate rather than standing on it, so the aim goes under where it <em>floats</em>
        // rather than under where it would stand.
        _camera.Target = new Vector3(shift,
            leggy ? -0.15f : small ? 0.22f : finny ? 0.42f : -0.35f, 0f);

        Raylib.BeginTextureMode(_target);
        Raylib.ClearBackground(Palette.Void);
        SkyRenderer.Draw(_camera, elapsed);

        Raylib.BeginMode3D(_camera);
        GridRenderer.Draw(specimen);
        // A slow turntable, so every painted face comes round to be looked at.
        _entities.DrawLoadoutShowcase(screen.Loadout, specimen, elapsed * 0.6f, eye, elapsed);
        Raylib.EndMode3D();

        ClassSelectRenderer.Draw(screen, elapsed);

        Raylib.EndTextureMode();
    }

    /// <summary>
    /// Renders the hidden test screen: a single roster specimen turning slowly on
    /// the spot over the grid, with the 2D stat overlay on top. The camera holds
    /// still and low, a few units back, so the turntable does all the moving.
    /// </summary>
    public void DrawTest(UI.TestScreen screen, float elapsed)
    {
        // Fixed low three-quarter view onto the specimen at the origin. The
        // camera eye doubles as the fog/shading reference; sitting close keeps the
        // model unfogged and its facets lit toward the viewer.
        var specimen = Vector2.Zero;
        // The boss rig towers ~10× a tank, so frame it from far back and higher up;
        // the smaller silhouettes keep the close view.
        // The maw hangs high but is far smaller than the crab, so it gets its own
        // framing: closer in than the boss and aimed up at where it floats, since the
        // one thing a picture of it has to show is that it is off the ground.
        var eye = screen.ShowingBoss ? new Vector3(0f, 20f, -36f)
                : screen.ShowingMaw ? new Vector3(0f, 9f, -20f)
                : new Vector3(0f, 3.0f, -6.8f);
        _camera.Position = eye;
        _camera.Target = screen.ShowingBoss ? new Vector3(0f, 5.5f, 0f)
                       : screen.ShowingMaw ? new Vector3(0f, 6.5f, 0f)
                       : new Vector3(0f, 1.2f, 0f);

        Raylib.BeginTextureMode(_target);
        Raylib.ClearBackground(Palette.Void);
        SkyRenderer.Draw(_camera, elapsed);

        Raylib.BeginMode3D(_camera);
        GridRenderer.Draw(specimen);
        if (screen.ShowingBoss)
        {
            // The boss is a rig: hold it still (no turntable) and loop the chosen
            // protocol phase so its animation reads.
            _entities.DrawCrabShowcase(screen.CrabPhase, specimen, elapsed, eye);
        }
        else if (screen.ShowingMaw)
        {
            // Also a rig, also held still — the turntable would fight the tooth rings,
            // which are the whole thing worth looking at.
            _entities.DrawMawShowcase(specimen, elapsed, eye);
        }
        else
        {
            float heading = elapsed * 0.6f; // slow turntable spin
            _entities.DrawShowcase(screen.Current.Kind, specimen, heading, eye);
        }
        Raylib.EndMode3D();

        TestRenderer.Draw(screen, elapsed);

        Raylib.EndTextureMode();
    }

    /// <summary>
    /// Draws the inventory / crafting panel as a live overlay: the world is rendered
    /// normally (the sim is still running behind it — the panel does not pause anything),
    /// then the mouse-driven panel is laid over it. The panel carries its own translucent
    /// backing for contrast, so the world stays faintly visible underneath rather than
    /// being coarsened away.
    /// </summary>
    public void DrawInventory(World.World world, UI.InventoryScreen screen, float elapsed)
    {
        // DrawWorld already renders the rotating 3D item icons into their textures
        // (before its own texture pass), so they're ready for the panel to blit.
        DrawWorld(world);

        Raylib.BeginTextureMode(_target);
        InventoryRenderer.Draw(world, screen, elapsed, _itemIcons);
        Raylib.EndTextureMode();
    }

    /// <summary>
    /// Maps a window-space mouse position to a pixel in the internal 320×240 target,
    /// inverting the integer upscale + letterbox that <see cref="Present"/> applies. So
    /// the inventory's slot hit-testing lands on the same pixels the panel is drawn at,
    /// whatever the window size or fullscreen state. Points in the letterbox bars map
    /// outside the target (negative or past the edge), which the caller reads as "no
    /// slot here".
    /// </summary>
    public static Vector2 ScreenToInternal(Vector2 mouse)
    {
        int scale = Math.Min(
            Raylib.GetScreenWidth() / Config.InternalWidth,
            Raylib.GetScreenHeight() / Config.InternalHeight);
        if (scale < 1) scale = 1;

        int offX = (Raylib.GetScreenWidth() - Config.InternalWidth * scale) / 2;
        int offY = (Raylib.GetScreenHeight() - Config.InternalHeight * scale) / 2;

        return new Vector2((mouse.X - offX) / scale, (mouse.Y - offY) / scale);
    }

    /// <summary>
    /// Draws a paused run: the frozen world with a pixel-blur closing over it and
    /// the pause panel on top. <paramref name="t"/> (0..1) is how far the blur has
    /// set in — 0 is the clean frame, 1 is the fully coarsened, dimmed hold. The
    /// blur is a genuine downsample: the frame is squeezed to a fraction of its
    /// size and blown back up nearest-neighbor, so it dissolves into fat blocks
    /// rather than a soft smear — the same chunky logic as the world upscale.
    /// </summary>
    public void DrawPaused(World.World world, UI.PauseMenu menu, float elapsed, float t)
    {
        // The sim is frozen, so this redraws the same held frame into _target.
        DrawWorld(world);
        // Coarsen it, but only dim to a mid wash (not full void) so the world still
        // reads behind the panel.
        ApplyPixelDissolve(t, 150);

        Raylib.BeginTextureMode(_target);
        MenuRenderer.DrawPause(menu, elapsed, t);
        Raylib.EndTextureMode();
    }

    /// <summary>
    /// Dissolves whatever is currently in the low-res target into fat pixel blocks
    /// and dims it toward the void, by <paramref name="amount"/> (0 untouched … 1
    /// fully coarsened). It is a genuine downsample — the frame is squeezed into a
    /// fraction of the resolution and blown back up nearest-neighbor, the same
    /// chunky logic as the world upscale, not a soft smear. Shared by the pause
    /// hold and the screen-to-screen fades. Call after a Draw* has filled the
    /// target and before Present. <paramref name="maxDark"/> caps the wash alpha at
    /// full amount: 255 fades all the way to void (screen wipes), less holds an
    /// image readable underneath.
    /// </summary>
    public void ApplyPixelDissolve(float amount, int maxDark = 255)
    {
        if (amount <= 0f) return;

        // Smoothstep so the pixels swell and settle rather than ramping linearly.
        float ease = amount * amount * (3f - 2f * amount);

        // Pass 1 — downsample the whole frame into the tiny _scratch. Both reads
        // use the full-texture negative-height flip Present relies on (a render
        // texture is stored bottom-up); full reads flip cleanly where partial ones
        // do not.
        Raylib.BeginTextureMode(_scratch);
        Raylib.DrawTexturePro(
            _target.Texture,
            new Rectangle(0, 0, Config.InternalWidth, -Config.InternalHeight),
            new Rectangle(0, 0, BlurW, BlurH), Vector2.Zero, 0f, Color.White);
        Raylib.EndTextureMode();

        // Pass 2 — blow the mosaic back up over the frame, opacity rising with
        // `ease` so it visibly dissolves into fat blocks, then a cold wash deepens.
        Raylib.BeginTextureMode(_target);
        Raylib.DrawTexturePro(
            _scratch.Texture,
            new Rectangle(0, 0, BlurW, -BlurH),
            new Rectangle(0, 0, Config.InternalWidth, Config.InternalHeight),
            Vector2.Zero, 0f, new Color(255, 255, 255, (int)(255 * ease)));

        Raylib.DrawRectangle(0, 0, Config.InternalWidth, Config.InternalHeight,
            new Color(5, 7, 10, (int)(maxDark * ease)));
        Raylib.EndTextureMode();
    }

    /// <summary>
    /// Blits the low-res target to the window: integer-scaled, nearest-neighbor,
    /// centered with letterbox bars in the void colour.
    /// </summary>
    public void Present()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(Palette.Void);

        int scale = Math.Min(
            Raylib.GetScreenWidth() / Config.InternalWidth,
            Raylib.GetScreenHeight() / Config.InternalHeight);
        if (scale < 1) scale = 1;

        int destW = Config.InternalWidth * scale;
        int destH = Config.InternalHeight * scale;
        int offX = (Raylib.GetScreenWidth() - destW) / 2;
        int offY = (Raylib.GetScreenHeight() - destH) / 2;

        // Source is flipped vertically because render textures are bottom-up.
        var src = new Rectangle(0, 0, Config.InternalWidth, -Config.InternalHeight);
        var dest = new Rectangle(offX, offY, destW, destH);
        Raylib.DrawTexturePro(_target.Texture, src, dest, Vector2.Zero, 0f, Color.White);

        Raylib.EndDrawing();
    }

    public void Dispose()
    {
        Raylib.UnloadRenderTexture(_target);
        Raylib.UnloadRenderTexture(_scratch);
        _itemIcons.Dispose();
    }
}
