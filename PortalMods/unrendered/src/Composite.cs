using System.Numerics;
using System.Reflection;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Modding;
using Unrendered.Rendering;
using GameWorld = Unrendered.World.World;

namespace UnrenderedPortal;

/// <summary>
/// Puts Minecraft on the screen at Minecraft's own resolution, under UNRENDERED's HUD.
///
/// <para>UNRENDERED draws its world into a 320×240 target and blows it up. Minecraft's models
/// must not go through that: the only thing of UNRENDERED's that may sit over them is its
/// HUD. So every frame with anything of Minecraft's in view is finished in two layers:</para>
/// <list type="bullet">
/// <item><b>In Minecraft</b> (the pilot crossed): Minecraft — sky, blocks, mobs — is drawn at
/// full resolution, and the game's frame goes over it. The game drew no world of its own
/// that frame, only the craft's own share (its arms, its rounds, its sparks) and the HUD, on
/// a clear background; Minecraft's terrain was written into its depth so a round behind a
/// hill is behind the hill.</item>
/// <item><b>In UNRENDERED</b> with Minecraft things in it (visitors, mobs that walked through,
/// blocks somebody built): inside the game's own pass each one is drawn as a flat key colour,
/// so the city in front of it hides it and the HUD drawn afterwards covers it. Then the same
/// things are drawn at full resolution and shown exactly where the key survived.</item>
/// </list>
/// </summary>
public static class Composite
{
    /// <summary>True once the present is wrapped (see <see cref="Hooks"/>). Without it the rift
    /// still works, just drawn inside the game's own pass.</summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// Marks Minecraft's pixels in the game's own frame: inside the game's pass, with its depth,
    /// a Minecraft thing is drawn into the frame's <em>alpha</em> only, as zero. The colour
    /// underneath — the city behind it — is left as it was, which is what shows through water
    /// and glass; anything of the game's in front hides it by depth; and the HUD, drawn
    /// afterwards, writes its alpha back and so stays on top.
    /// </summary>
    public static void BeginMask()
    {
        Rlgl.DrawRenderBatchActive();
        Rlgl.ColorMask(false, false, false, true);
        Rlgl.DisableColorBlend();
    }

    public static void EndMask()
    {
        Rlgl.DrawRenderBatchActive();
        Rlgl.ColorMask(true, true, true, true);
        Rlgl.EnableColorBlend();
    }

    private enum Mode { None, Realm, Overlay }
    private static Mode _mode;
    private static GameWorld? _world;
    private static Camera3D _cam;

    private static RenderTexture2D _full;
    private static int _fw, _fh;
    private static Shader _shader;
    private static bool _ready;
    private static int _locFull;

    /// <summary>Before the frame (the mod's BeforeDraw): decides how this frame will be finished.</summary>
    public static void Begin(GameWorld world, ModScene scene)
    {
        _world = world;
        _cam = scene.Camera;
        _mode = !Enabled ? Mode.None
            : Realm.Owns(world) ? Mode.Realm
            : Overlays.Any(world, scene.Camera.Position) ? Mode.Overlay
            : Mode.None;
    }

    public static bool RealmLayered => _mode == Mode.Realm;
    public static bool OverlayKeyed => _mode == Mode.Overlay;

    /// <summary>The game's present, replaced when this frame has Minecraft in it. False hands the
    /// frame back to the game untouched.</summary>
    internal static bool Present(Renderer renderer)
    {
        if (_mode == Mode.None || _world == null || !ReferenceEquals(_world, Rift.World)) return false;
        var target = TargetOf(renderer);
        if (target == null) return false;
        Ready();

        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        int scale = Math.Max(1, Math.Min(sw / Config.InternalWidth, sh / Config.InternalHeight));
        int dw = Config.InternalWidth * scale, dh = Config.InternalHeight * scale;
        Size(dw, dh);

        // Minecraft, at the size it will be shown.
        Raylib.BeginTextureMode(_full);
        if (_mode == Mode.Realm)
        {
            Raylib.ClearBackground(Realm.Fog);
            Raylib.BeginMode3D(_cam);
            Realm.DrawFull(_cam);
            Raylib.EndMode3D();
        }
        else
        {
            Raylib.ClearBackground(new Color(0, 0, 0, 0));
            Raylib.BeginMode3D(_cam);
            Overlays.Draw(_world, _cam.Position, keyed: false);
            Raylib.EndMode3D();
        }
        Raylib.EndTextureMode();

        // And the two together.
        Raylib.BeginDrawing();
        Raylib.ClearBackground(Palette.Void);
        Raylib.BeginShaderMode(_shader);
        Raylib.SetShaderValueTexture(_shader, _locFull, _full.Texture);
        var src = new Rectangle(0, 0, Config.InternalWidth, -Config.InternalHeight);
        var dst = new Rectangle((sw - dw) / 2, (sh - dh) / 2, dw, dh);
        Raylib.DrawTexturePro(target.Value.Texture, src, dst, Vector2.Zero, 0f, Color.White);
        Raylib.EndShaderMode();
        Raylib.EndDrawing();
        return true;
    }

    private static readonly FieldInfo? TargetField =
        typeof(Renderer).GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic);

    private static RenderTexture2D? TargetOf(Renderer r) => TargetField?.GetValue(r) as RenderTexture2D?;

    private static void Size(int w, int h)
    {
        if (w == _fw && h == _fh) return;
        if (_fw > 0) Raylib.UnloadRenderTexture(_full);
        _full = Raylib.LoadRenderTexture(w, h);
        Raylib.SetTextureFilter(_full.Texture, TextureFilter.Point);
        _fw = w; _fh = h;
    }

    private static void Ready()
    {
        if (_ready) return;
        _ready = true;
        _shader = Raylib.LoadShaderFromMemory(null, Fs);
        _locFull = Raylib.GetShaderLocation(_shader, "fullTex");
    }

    // The game's frame, and Minecraft where the frame's alpha says so: alpha 0 is Minecraft's
    // (the whole frame, in Minecraft, bar what the game drew; in UNRENDERED, the masked things),
    // 1 is the game's own, and in between is the HUD's translucency over Minecraft. Under a
    // Minecraft pixel that is itself see-through (water, glass) is what the game drew there.
    private const string Fs = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        uniform sampler2D texture0;
        uniform sampler2D fullTex;
        out vec4 finalColor;
        void main() {
            vec4 t = texture(texture0, fragTexCoord);
            vec4 m = texture(fullTex, fragTexCoord);
            vec3 mc = mix(t.rgb, m.rgb, m.a);
            finalColor = vec4(mix(mc, t.rgb, t.a), 1.0);
        }
        """;
}
