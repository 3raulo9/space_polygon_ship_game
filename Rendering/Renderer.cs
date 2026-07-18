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
    private Camera3D _camera;
    private readonly EntityRenderer _entities = new();

    public Renderer()
    {
        _target = Raylib.LoadRenderTexture(Config.InternalWidth, Config.InternalHeight);
        // Nearest-neighbor: hard, chunky pixels. No filtering, ever.
        Raylib.SetTextureFilter(_target.Texture, TextureFilter.Point);

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
        _camera.Position = eye;
        _camera.Target = eye + new Vector3(fwd.X, -0.12f, fwd.Y); // slightly low horizon

        Raylib.BeginTextureMode(_target);
        Raylib.ClearBackground(Palette.Void); // never pure black

        Raylib.BeginMode3D(_camera);
        GridRenderer.Draw(player.Position);
        _entities.Draw(world, player.Position);
        Raylib.EndMode3D();

        Raylib.EndTextureMode();
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

        Raylib.BeginMode3D(_camera);
        GridRenderer.Draw(pos);
        Raylib.EndMode3D();

        MenuRenderer.DrawSettings(screen, elapsed);

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
    }
}
