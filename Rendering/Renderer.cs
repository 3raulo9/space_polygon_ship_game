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
    private Camera3D _camera;
    private readonly EntityRenderer _entities = new();

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

        // First-person eye: sits at camera height above the craft, looking down
        // its heading. The jump lifts the eye with the craft.
        var eye = new Vector3(
            player.Position.X,
            Config.CameraHeight + player.Height,
            player.Position.Y);

        var fwd = player.Forward;

        // The nearer an active Crab-Core stalks, the harder the whole rig judders —
        // a translational rumble on the eye plus an extra rotational rattle on the
        // aim point, so the closer it gets the less steady the world holds.
        float shake = world.Boss is { } b ? b.ProximityShake(player.Position) : 0f;
        var seizure = world.Seizure;

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

        _camera.Position = eye + rumble;
        // Pitch the eye up a touch so the horizon sits low on screen: that opens
        // up a tall sky band above the floor, where the pink glow can fade all
        // the way to black below the top HUD strip.
        _camera.Target = eye + rumble
                       + new Vector3(fwd.X, Config.CameraLookLift + pitch, fwd.Y) + rattle;
        // Roll tips the whole world by turning the camera's idea of up. The axis is
        // the craft's own right on the plane, so the horizon pivots about the centre
        // of the view rather than sliding sideways.
        _camera.Up = roll != 0f
            ? new Vector3(fwd.Y * MathF.Sin(roll), MathF.Cos(roll), -fwd.X * MathF.Sin(roll))
            : new Vector3(0f, 1f, 0f);

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

        // Flat instrument panel over the scene: vital bars + radar along the top.
        HudRenderer.Draw(world);

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
    /// Renders the title menu into the low-res target so it shares the world's
    /// chunky pixels. A grid drifts slowly behind it — the void is still out
    /// there, waiting — with the UI drawn flat on top. Kept in the Renderer so
    /// the Menu class stays pure state and never touches Raylib.
    /// </summary>
    public void DrawMenu(UI.Menu menu, float elapsed)
    {
        // A slow, aimless drift over the empty grid. No player, no craft — just
        // the machine idling. The eye creeps forward and pans a hair so the void
        // reads as alive-but-indifferent rather than a frozen backdrop.
        var pos = new Vector2(elapsed * 0.6f, elapsed * 1.4f);
        float pan = MathF.Sin(elapsed * 0.05f) * 0.25f;
        var eye = new Vector3(pos.X, Config.CameraHeight + 1.5f, pos.Y);
        _camera.Position = eye;
        _camera.Target = eye + new Vector3(pan, -0.16f, 1f);

        Raylib.BeginTextureMode(_target);
        Raylib.ClearBackground(Palette.Void);
        SkyRenderer.Draw(_camera, elapsed);

        Raylib.BeginMode3D(_camera);
        GridRenderer.Draw(pos);
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
        var pos = new Vector2(elapsed * 0.6f, elapsed * 1.4f);
        float pan = MathF.Sin(elapsed * 0.05f) * 0.25f;
        var eye = new Vector3(pos.X, Config.CameraHeight + 1.5f, pos.Y);
        _camera.Position = eye;
        _camera.Target = eye + new Vector3(pan, -0.16f, 1f);

        Raylib.BeginTextureMode(_target);
        Raylib.ClearBackground(Palette.Void);
        SkyRenderer.Draw(_camera, elapsed);

        Raylib.BeginMode3D(_camera);
        GridRenderer.Draw(pos);
        Raylib.EndMode3D();

        MenuRenderer.DrawSettings(screen, elapsed);

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
        var eye = screen.ShowingBoss ? new Vector3(0f, 20f, -36f) : new Vector3(0f, 3.0f, -6.8f);
        _camera.Position = eye;
        _camera.Target = screen.ShowingBoss ? new Vector3(0f, 5.5f, 0f) : new Vector3(0f, 1.2f, 0f);

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
    }
}
