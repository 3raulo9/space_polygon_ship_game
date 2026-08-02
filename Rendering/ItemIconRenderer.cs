using System.Numerics;
using Raylib_cs;
using Unrendered.Core;

namespace Unrendered.Rendering;

/// <summary>
/// Renders each inventory item as a small rotating 3D model into its own little render
/// texture, so the crafting panel shows the real polygon salvage turning slowly on the
/// spot rather than flat coloured squares. One texture per item kind, redrawn each frame at
/// a shared slow spin; the panel then blits the right one into every slot holding that kind.
/// A dozen 28×28 targets is nothing, and it means a slot, a bench arrow's ghosted promise and
/// the stack riding the cursor are all the same picture. Kept apart from the flat 2D panel
/// drawing because
/// a 3D pass needs its own render target and camera, which can't be nested inside the
/// panel's own texture pass.
/// </summary>
public sealed class ItemIconRenderer : IDisposable
{
    /// <summary>Resolution each icon is rendered at. A little above the ~14px slots it
    /// lands in, so the silhouette has some detail to spare when it's blitted down.</summary>
    public const int Size = 28;

    private readonly Dictionary<ItemKind, RenderTexture2D> _tex = new();
    private readonly Dictionary<ItemKind, PolyMesh> _mesh = new();
    private readonly Dictionary<ItemKind, float> _scale = new();
    // Each mesh is built base-at-origin and extends upward; this is the model-space Y to
    // lift it by (negated) so it turns about its own centre instead of its feet.
    private readonly Dictionary<ItemKind, float> _centerY = new();
    private Camera3D _cam;

    public ItemIconRenderer()
    {
        // The salvage meshes, reusing the same shapes the world uses for pickups and the
        // boss's gem so a fragment and a crafted core read as what they are.
        _mesh[ItemKind.Battery] = Meshes.Battery(Palette.BatteryFill, Palette.BatteryCore);
        _mesh[ItemKind.Bullet] = Meshes.Bullet(Palette.Flag, Palette.HudChrome);
        _mesh[ItemKind.CrabFragment] = Meshes.Shard(Palette.NeonRed);
        _mesh[ItemKind.CrabCore] = Meshes.CrabCoreGem(Palette.NeonMagenta);

        // The materials. Told apart by silhouette before colour, because at this size that is
        // all a player really has: a flat torn plate, a round coil, a heap, a brick, a block,
        // a crystal, a rod.
        _mesh[ItemKind.ScrapMetal] = Meshes.ScrapPlate(Palette.ScrapSteel);
        _mesh[ItemKind.CopperWire] = Meshes.WireCoil(Palette.CopperWire);
        _mesh[ItemKind.SpaceGunpowder] = Meshes.PowderPile(Palette.PowderDark, Palette.PowderGrain);
        _mesh[ItemKind.DenseAlloy] = Meshes.Ingot(Palette.AlloyIngot, Palette.HudChrome);
        _mesh[ItemKind.Lead] = Meshes.MetalBlock(Palette.LeadGrey, Palette.ScrapSteel);
        _mesh[ItemKind.Zinc] = Meshes.Crystal(Palette.ZincPale);
        _mesh[ItemKind.Lithium] = Meshes.Rod(Palette.HudChrome, Palette.LithiumRose);

        // Per-kind scale (to frame each silhouette at a similar size) and the model-space
        // Y centre to spin about (these meshes sit base-at-origin, so most are lifted).
        _scale[ItemKind.Battery] = 1.15f;      _centerY[ItemKind.Battery] = 0.89f;
        _scale[ItemKind.Bullet] = 1.25f;       _centerY[ItemKind.Bullet] = 0.85f;
        _scale[ItemKind.CrabFragment] = 2.4f;  _centerY[ItemKind.CrabFragment] = 0.12f;
        _scale[ItemKind.CrabCore] = 0.85f;     _centerY[ItemKind.CrabCore] = 1.25f;

        _scale[ItemKind.ScrapMetal] = 1.30f;     _centerY[ItemKind.ScrapMetal] = 0.16f;
        _scale[ItemKind.CopperWire] = 1.30f;     _centerY[ItemKind.CopperWire] = 0.40f;
        _scale[ItemKind.SpaceGunpowder] = 1.35f; _centerY[ItemKind.SpaceGunpowder] = 0.34f;
        _scale[ItemKind.DenseAlloy] = 1.35f;     _centerY[ItemKind.DenseAlloy] = 0.23f;
        _scale[ItemKind.Lead] = 1.55f;           _centerY[ItemKind.Lead] = 0.45f;
        _scale[ItemKind.Zinc] = 1.30f;           _centerY[ItemKind.Zinc] = 0.58f;
        _scale[ItemKind.Lithium] = 1.20f;        _centerY[ItemKind.Lithium] = 0.60f;

        foreach (var kind in _mesh.Keys)
        {
            var rt = Raylib.LoadRenderTexture(Size, Size);
            Raylib.SetTextureFilter(rt.Texture, TextureFilter.Point);
            _tex[kind] = rt;
        }

        // A fixed close three-quarter view onto the item centred at the origin — near
        // enough that the fog never touches it, tipped down a little so it reads as a
        // solid turning in the light rather than a flat face-on card.
        _cam = new Camera3D
        {
            Position = new Vector3(0f, 0.9f, -3.6f),
            Target = new Vector3(0f, 0f, 0f),
            Up = new Vector3(0f, 1f, 0f),
            FovY = 46f,
            Projection = CameraProjection.Perspective,
        };
    }

    /// <summary>Redraws every item icon at the current slow spin. Call once per frame,
    /// before the panel pass blits the textures in.</summary>
    public void Render(float elapsed)
    {
        float heading = elapsed * 0.8f;   // the slow idle turn
        foreach (var (kind, mesh) in _mesh)
        {
            Raylib.BeginTextureMode(_tex[kind]);
            Raylib.ClearBackground(new Color(0, 0, 0, 0)); // transparent — only the model shows
            Raylib.BeginMode3D(_cam);
            // Lift by -centre so the model spins about its middle, centred on the origin.
            mesh.Draw(Vector2.Zero, heading, -_centerY[kind] * _scale[kind], _cam.Position, _scale[kind]);
            Raylib.EndMode3D();
            Raylib.EndTextureMode();
        }
    }

    /// <summary>The rendered icon for a kind. Render textures are stored bottom-up, so
    /// draw it with a negative-height source rectangle (as the panel does).</summary>
    public Texture2D Texture(ItemKind kind) => _tex[kind].Texture;

    public void Dispose()
    {
        foreach (var rt in _tex.Values) Raylib.UnloadRenderTexture(rt);
    }
}
