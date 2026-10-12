using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Creator;
using Unrendered.UI;

namespace Unrendered.Rendering;

/// <summary>
/// Draws the creator: the design in 3D on the grid, with whatever the cursor is over lit up,
/// the selected part's rings and stretch handles, and a carried part turning red when letting
/// go would throw it away — then the shapes bar, the bars and the timeline over it, in the
/// same 320×240 cold chrome as the rest of the game's screens. Reads the screen, changes
/// nothing.
/// </summary>
internal static class CreatorRenderer
{
    private const int W = Config.InternalWidth;
    private const int H = Config.InternalHeight;

    private static readonly Color PanelBg = new(5, 7, 10, 215);
    private static readonly Color Select = Palette.NeonMagenta;
    private static Color Dim => Scale(Palette.HudChrome, 0.45f);
    private static Color Mid => Scale(Palette.HudChrome, 0.7f);
    private static Color Bright => Palette.HudChrome;
    private static readonly Color[] RingColors = { new(230, 70, 70, 255), new(90, 230, 200, 255), new(90, 140, 255, 255) };

    // =====================================================================================
    //  Meshes
    // =====================================================================================

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Block, MeshCache> _meshes = new();

    private sealed class MeshCache
    {
        public List<(Vector3, Vector3, Vector3, Color)>? Source;
        public PolyMesh? Mesh;
    }

    /// <summary>A block as a flat-shaded mesh, rebuilt only when its shape changes.</summary>
    private static PolyMesh MeshFor(Block b)
    {
        var tris = BlockGeometry.Triangles(b);
        var cache = _meshes.GetOrCreateValue(b);
        if (cache.Mesh != null && ReferenceEquals(cache.Source, tris)) return cache.Mesh;
        var m = new PolyMesh();
        foreach (var (a, bb, c, col) in tris) m.AddFace(col, a, bb, c);
        cache.Source = tris;
        cache.Mesh = m;
        return m;
    }

    public static void DrawDesign(Design d, Dictionary<int, Matrix4x4> pose, Vector3 eye,
        ISet<int>? tinted = null, Color? tint = null)
    {
        foreach (var p in d.Parts)
        {
            if (!pose.TryGetValue(p.Id, out var pm)) continue;
            Color? t = tinted != null && tinted.Contains(p.Id) ? tint : null;
            foreach (var b in p.Blocks)
                MeshFor(b).Draw(DesignPose.BlockMatrix(p, b, pm), eye, t);
        }
    }

    // =====================================================================================
    //  3D
    // =====================================================================================

    public static Camera3D LibraryCamera(Design? d, float elapsed)
    {
        float height = 1f, extent = 3f;
        if (d != null) (height, extent) = Bounds(d);
        var target = new Vector3(0, height, 0);
        float dist = Math.Clamp(extent * 1.5f, 4.5f, 30f);
        var eye = target + new Vector3(0, extent * 0.35f, dist);
        var aim = target + new Vector3(-dist * 0.32f, 0, 0);
        return new Camera3D { Position = eye, Target = aim, Up = Vector3.UnitY, FovY = Config.CameraFovY, Projection = CameraProjection.Perspective };
    }

    public static void DrawLibraryModel(Design d, float elapsed, Vector3 eye)
    {
        var idle = d.Clips.FirstOrDefault(c => c.Name == "IDLE") ?? d.Clips.FirstOrDefault();
        float t = idle != null ? (elapsed / idle.Duration) % 1f : 0f;
        var pose = DesignPose.Evaluate(d, idle, t, Matrix4x4.CreateRotationY(elapsed * 0.5f));
        DrawDesign(d, pose, eye);
    }

    private static (float Height, float Extent) Bounds(Design d)
    {
        var pose = DesignPose.Evaluate(d, null, 0f, Matrix4x4.Identity);
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        foreach (var p in d.Parts)
        {
            var (bl, bh) = DesignPose.PartBounds(p);
            if (bl == bh || !pose.TryGetValue(p.Id, out var pm)) continue;
            for (int i = 0; i < 8; i++)
            {
                var w = Vector3.Transform(new Vector3((i & 1) == 0 ? bl.X : bh.X, (i & 2) == 0 ? bl.Y : bh.Y, (i & 4) == 0 ? bl.Z : bh.Z), pm);
                lo = Vector3.Min(lo, w);
                hi = Vector3.Max(hi, w);
            }
        }
        if (lo.X > hi.X) return (1f, 3f);
        return ((lo.Y + hi.Y) * 0.5f, (hi - lo).Length());
    }

    public static void DrawScene(CreatorScreen s, Vector3 eye, float elapsed)
    {
        var d = s.Design;
        if (d == null) return;

        // A carried part over nothing goes red: letting go throws it away.
        HashSet<int>? doomed = null;
        if (s.Carried >= 0 && s.Detached) doomed = d.Subtree(s.Carried).Select(p => p.Id).ToHashSet();
        DrawDesign(d, s.Pose, eye, doomed, Palette.NeonRed);

        float pulse = 0.6f + 0.4f * MathF.Abs(MathF.Sin(elapsed * 4f));

        // What the cursor is over glows faintly; in PAINT it shows the colour about to land.
        if (s.Hover >= 0 && d.Find(s.Hover) is { } hover && s.Hover != s.Selected)
        {
            Color c = s.Current == CreatorScreen.Page.Paint
                ? ClassCatalog.SwatchColor(s.CarriedSwatch >= 0 ? s.CarriedSwatch : Math.Max(0, s.Brush))
                : Scale(Palette.HudChrome, 0.55f);
            Outline(d, hover, s.Pose, c);
            if (hover.Twin >= 0 && d.Find(hover.Twin) is { } ht) Outline(d, ht, s.Pose, Scale(c, 0.5f));
        }

        // The spine's handles, as Spore shows them when the cursor is on the torso: a line down
        // the vertebrae with a grip at each one, so it is obvious what can be pulled.
        if (s.Current == CreatorScreen.Page.Build && Spine.Has(d)
            && (d.Find(s.Hover)?.Role == PartRole.Spine || s.SelectedPart?.Role == PartRole.Spine))
        {
            var v = Spine.Vertebrae(d);
            for (int i = 0; i < v.Count; i++)
            {
                if (!s.Pose.TryGetValue(v[i].Id, out var vm)) continue;
                var at = vm.Translation;
                bool end = i == 0 || i == v.Count - 1;
                bool on = v[i].Id == s.Hover || v[i].Id == s.Selected;
                float g = MathF.Max(0.05f, Vector3.Distance(eye, at) * (end ? 0.014f : 0.01f));
                Raylib.DrawCube(at, g, g, g, on ? Palette.HudChrome : end ? Select : Scale(Palette.HudChrome, 0.6f));
                if (i > 0 && s.Pose.TryGetValue(v[i - 1].Id, out var pm))
                    Raylib.DrawLine3D(pm.Translation, at, Scale(Palette.HudChrome, 0.7f));
            }
        }

        var sel = s.SelectedPart;
        if (sel == null || s.Current is CreatorScreen.Page.Paint or CreatorScreen.Page.Play) return;
        Outline(d, sel, s.Pose, Scale(Select, pulse));
        if (sel.Twin >= 0 && d.Find(sel.Twin) is { } twin) Outline(d, twin, s.Pose, Scale(Select, 0.35f));

        if (s.Carried >= 0) return;

        // The rings, as Spore draws them round a selected part.
        if (s.ShowsRings(sel))
            foreach (int axis in s.RingAxes)
            {
                var pts = s.RingPoints(sel, axis, 40);
                Color rc = s.ActiveRing == axis ? Palette.HudChrome : RingColors[axis];
                for (int i = 0; i < pts.Length; i++) Raylib.DrawLine3D(pts[i], pts[(i + 1) % pts.Length], rc);
            }

        // The stretch handles: a little block at the end of each axis, on a line from the
        // part's middle.
        if (s.ShowsHandles(sel))
        {
            var mid = s.HandleCentre(sel);
            var hs = s.HandlePoints(sel);
            float size = MathF.Max(0.06f, Vector3.Distance(eye, mid) * 0.012f);
            for (int i = 0; i < hs.Length; i++)
            {
                Raylib.DrawLine3D(mid, hs[i], Scale(RingColors[i], 0.6f));
                Raylib.DrawCube(hs[i], size, size, size, RingColors[i]);
            }
        }
    }

    /// <summary>A part's outline: the box round its blocks, in its own frame.</summary>
    private static void Outline(Design d, Part p, Dictionary<int, Matrix4x4> pose, Color color)
    {
        if (!pose.TryGetValue(p.Id, out var pm)) return;
        var (lo, hi) = DesignPose.PartBounds(p);
        if (lo == hi) return;
        var pad = new Vector3(0.015f);
        lo -= pad; hi += pad;
        Span<Vector3> v = stackalloc Vector3[8];
        for (int i = 0; i < 8; i++)
            v[i] = Vector3.Transform(new Vector3((i & 1) == 0 ? lo.X : hi.X, (i & 2) == 0 ? lo.Y : hi.Y, (i & 4) == 0 ? lo.Z : hi.Z), pm);
        for (int i = 0; i < 8; i++)
            for (int bit = 1; bit < 8; bit <<= 1)
                if ((i & bit) == 0) Raylib.DrawLine3D(v[i], v[i | bit], color);
    }

    // =====================================================================================
    //  Palette icons
    // =====================================================================================

    private const int IconPx = 24;
    private static readonly Dictionary<PartCatalog.Item, RenderTexture2D> _icons = new();

    /// <summary>Renders the shapes bar's icons, a few a frame so opening the creator never
    /// hitches. Each is the part itself, turned three-quarters to the camera.</summary>
    public static void PrepareIcons()
    {
        int budget = 6;
        foreach (var item in PartCatalog.All)
        {
            if (_icons.ContainsKey(item)) continue;
            if (budget-- <= 0) return;
            _icons[item] = RenderIcon(item);
        }
    }

    private static RenderTexture2D RenderIcon(PartCatalog.Item item)
    {
        var tex = Raylib.LoadRenderTexture(IconPx, IconPx);
        var d = new Design { Parts = item.Build() };
        var pose = DesignPose.Evaluate(d, null, 0f, Matrix4x4.Identity);
        var (h, extent) = Bounds(d);
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        foreach (var p in d.Parts)
        {
            var (bl, bh) = DesignPose.PartBounds(p);
            if (bl == bh) continue;
            lo = Vector3.Min(lo, Vector3.Transform(bl, pose[p.Id]));
            hi = Vector3.Max(hi, Vector3.Transform(bh, pose[p.Id]));
            lo = Vector3.Min(lo, Vector3.Transform(bh, pose[p.Id]));
            hi = Vector3.Max(hi, Vector3.Transform(bl, pose[p.Id]));
        }
        var centre = lo.X <= hi.X ? (lo + hi) * 0.5f : new Vector3(0, h, 0);
        var eye = centre + Vector3.Normalize(new Vector3(1f, 0.8f, 1.4f)) * MathF.Max(0.6f, extent) * 1.25f;
        var cam = new Camera3D { Position = eye, Target = centre, Up = Vector3.UnitY, FovY = 40f, Projection = CameraProjection.Perspective };

        Raylib.BeginTextureMode(tex);
        Raylib.ClearBackground(new Color(0, 0, 0, 0));
        Raylib.BeginMode3D(cam);
        DrawDesign(d, pose, eye);
        Raylib.EndMode3D();
        Raylib.EndTextureMode();
        return tex;
    }

    private static void DrawIcon(PartCatalog.Item item, int x, int y)
    {
        if (!_icons.TryGetValue(item, out var tex)) return;
        // Render textures come out upside down.
        Raylib.DrawTextureRec(tex.Texture, new Rectangle(0, 0, IconPx, -IconPx), new Vector2(x, y), Color.White);
    }

    // =====================================================================================
    //  2D
    // =====================================================================================

    public static void DrawHud(CreatorScreen s, float elapsed)
    {
        if (s.Current == CreatorScreen.Page.Library) { DrawLibrary(s, elapsed); return; }

        DrawTopBar(s);
        Raylib.DrawRectangle(0, CreatorScreen.TopH, CreatorScreen.PalW, CreatorScreen.BottomY - CreatorScreen.TopH, PanelBg);
        switch (s.Current)
        {
            case CreatorScreen.Page.Build: DrawShapes(s); break;
            case CreatorScreen.Page.Paint: DrawSwatches(s); break;
            case CreatorScreen.Page.Animate: DrawClipPanel(s, play: false); DrawTimeline(s); break;
            case CreatorScreen.Page.Play: DrawClipPanel(s, play: true); break;
        }
        DrawBottomBar(s);
        DrawStatus(s);
        if (s.CarriedSwatch > -2) DrawCarriedSwatch(s);
        if (s.TextEntry != null) DrawTextEntry(s, elapsed);
    }

    private static void DrawTopBar(CreatorScreen s)
    {
        Raylib.DrawRectangle(0, 0, W, CreatorScreen.TopH, PanelBg);
        for (int i = 0; i < CreatorScreen.PageNames.Length; i++)
        {
            bool on = (int)s.Current == i + 1;
            int x = CreatorScreen.TabX(i);
            bool hot = s.Mouse.Y < CreatorScreen.TopH && s.Mouse.X >= x && s.Mouse.X < x + 42;
            if (on) Raylib.DrawRectangle(x, 1, 42, CreatorScreen.TopH - 2, Scale(Select, 0.35f));
            else if (hot) Raylib.DrawRectangle(x, 1, 42, CreatorScreen.TopH - 2, Scale(Palette.HudChrome, 0.15f));
            Centered(CreatorScreen.PageNames[i], x + 21, 3, on ? Bright : hot ? Mid : Dim);
        }
        for (int i = 0; i < CreatorScreen.TopButtons.Length; i++)
        {
            int x = CreatorScreen.ButtonX(i);
            bool hot = s.Mouse.Y < CreatorScreen.TopH && s.Mouse.X >= x && s.Mouse.X < x + 28;
            bool save = i == 2;
            Raylib.DrawRectangleLines(x, 1, 28, CreatorScreen.TopH - 2, hot ? Bright : save && s.Dirty ? Select : Dim);
            Centered(CreatorScreen.TopButtons[i], x + 14, 3, hot ? Bright : save && s.Dirty ? Select : Mid);
        }
    }

    private static void DrawShapes(CreatorScreen s)
    {
        string cat = PartCatalog.Categories[s.Category];
        Text("<", 3, CreatorScreen.TopH + 4, Mid);
        Text(">", CreatorScreen.PalW - 8, CreatorScreen.TopH + 4, Mid);
        Centered(cat, CreatorScreen.PalW / 2, CreatorScreen.TopH + 4, Bright);

        var items = s.PaletteItems;
        int hot = CreatorScreen.PalW > s.Mouse.X ? s.PaletteIndexAt(s.Mouse) : -1;
        for (int i = s.PaletteScroll * CreatorScreen.PalCols; i < items.Count; i++)
        {
            int slot = i - s.PaletteScroll * CreatorScreen.PalCols;
            int x = 2 + (slot % CreatorScreen.PalCols) * CreatorScreen.Icon;
            int y = CreatorScreen.PalTop + (slot / CreatorScreen.PalCols) * CreatorScreen.Icon;
            if (y + CreatorScreen.Icon > CreatorScreen.BottomY) break;
            Raylib.DrawRectangle(x + 1, y + 1, CreatorScreen.Icon - 2, CreatorScreen.Icon - 2, new Color(14, 18, 24, 255));
            if (i == hot) Raylib.DrawRectangleLines(x, y, CreatorScreen.Icon, CreatorScreen.Icon, Select);
            DrawIcon(items[i], x + 1, y + 1);
        }
        // The hovered shape's name, so a tile is never a mystery.
        if (hot >= 0 && hot < items.Count)
        {
            string label = items[hot].Label;
            int w = Measure(label) + 6;
            int y = (int)s.Mouse.Y - 12;
            Raylib.DrawRectangle(CreatorScreen.PalW + 2, y, w, 10, new Color(5, 7, 10, 235));
            Text(label, CreatorScreen.PalW + 5, y + 2, Bright);
        }
    }

    private static void DrawSwatches(CreatorScreen s)
    {
        Centered("COLOURS", CreatorScreen.PalW / 2, CreatorScreen.TopH + 4, Bright);
        for (int i = 0; i < CreatorScreen.SwatchCount; i++)
        {
            int x = 2 + (i % CreatorScreen.PalCols) * CreatorScreen.Icon;
            int y = CreatorScreen.SwatchTop + (i / CreatorScreen.PalCols) * CreatorScreen.Icon;
            bool original = i == ClassCatalog.Swatches.Length;
            int swatch = original ? -1 : i;
            if (original)
            {
                // The game's own colours: a little split tile.
                Raylib.DrawRectangle(x + 2, y + 2, 11, 22, Palette.PlayerFill);
                Raylib.DrawRectangle(x + 13, y + 2, 11, 11, Palette.NeonRed);
                Raylib.DrawRectangle(x + 13, y + 13, 11, 11, Palette.Flag);
            }
            else Raylib.DrawRectangle(x + 2, y + 2, CreatorScreen.Icon - 4, CreatorScreen.Icon - 4, ClassCatalog.SwatchColor(i));
            if (s.Brush == swatch) Raylib.DrawRectangleLines(x, y, CreatorScreen.Icon, CreatorScreen.Icon, Bright);
        }
    }

    private static void DrawCarriedSwatch(CreatorScreen s)
    {
        int x = (int)s.Mouse.X + 3, y = (int)s.Mouse.Y + 3;
        Raylib.DrawRectangle(x, y, 9, 9, s.CarriedSwatch >= 0 ? ClassCatalog.SwatchColor(s.CarriedSwatch) : Palette.PlayerFill);
        Raylib.DrawRectangleLines(x, y, 9, 9, Bright);
    }

    private static void DrawClipPanel(CreatorScreen s, bool play)
    {
        var d = s.Design!;
        int y = CreatorScreen.ClipRowsY;
        for (int i = 0; i < d.Clips.Count; i++)
        {
            var c = d.Clips[i];
            bool on = !play && i == s.ClipIndex;
            bool hot = s.Mouse.X < CreatorScreen.PalW && s.Mouse.Y >= y && s.Mouse.Y < y + CreatorScreen.RowH;
            if (on) Raylib.DrawRectangle(1, y - 1, CreatorScreen.PalW - 2, CreatorScreen.RowH, Scale(Select, 0.35f));
            else if (hot) Raylib.DrawRectangle(1, y - 1, CreatorScreen.PalW - 2, CreatorScreen.RowH, Scale(Palette.HudChrome, 0.15f));
            Text(CreatorScreen.Clip(c.Name, 9), 3, y, on ? Bright : Mid);
            y += CreatorScreen.RowH;
        }
        if (play) return;

        y = s.ClipButtonsY;
        for (int b = 0; b < CreatorScreen.ClipButtons.Length; b++)
        {
            int by = y + b * CreatorScreen.RowH;
            bool hot = s.Mouse.X < CreatorScreen.PalW && s.Mouse.Y >= by - 1 && s.Mouse.Y < by - 1 + CreatorScreen.RowH;
            Raylib.DrawRectangleLines(2, by - 1, CreatorScreen.PalW - 4, CreatorScreen.RowH, hot ? Mid : Dim);
            Centered(CreatorScreen.ClipButtons[b], CreatorScreen.PalW / 2, by + 1, hot ? Bright : Mid);
        }

        // The selected part's wave tracks, as sliders: how far it swings, how often, and when.
        var part = s.SelectedPart;
        y = s.TrackRowsY;
        Raylib.DrawRectangle(2, y - 2, CreatorScreen.PalW - 4, 1, Dim);
        Text(part == null ? "PICK A PART" : CreatorScreen.Clip(part.Name, 9), 3, y, part == null ? Dim : Bright);
        var tracks = s.SliderTracks;
        for (int i = 0; i < tracks.Count; i++)
        {
            int ty = s.SliderRowY(i);
            if (ty + 26 > CreatorScreen.TimelineY - 8) break;
            Text(tracks[i].Channel.ToString().ToUpperInvariant(), 3, ty, Mid);
            for (int f = 0; f < 3; f++)
            {
                int sy = ty + 8 + f * 7;
                Raylib.DrawRectangle(CreatorScreen.SliderX, sy + 1, CreatorScreen.SliderW, 1, Dim);
                int kx = CreatorScreen.SliderX + (int)(CreatorScreen.SliderValue(tracks[i], f) * CreatorScreen.SliderW);
                Raylib.DrawRectangle(kx - 1, sy - 1, 3, 5, RingColors[f]);
                // Its name, while the cursor is on it.
                if (s.Mouse.X < CreatorScreen.PalW && MathF.Abs(s.Mouse.Y - (sy + 1)) < 3.5f)
                {
                    string label = CreatorScreen.SliderNames[f];
                    Raylib.DrawRectangle(CreatorScreen.PalW + 2, sy - 3, Measure(label) + 6, 10, new Color(5, 7, 10, 235));
                    Text(label, CreatorScreen.PalW + 5, sy - 1, RingColors[f]);
                }
            }
        }
    }

    /// <summary>The strip across the bottom of the view: play and loop buttons, the playhead,
    /// the selected part's keys as dots, and the end of the clip, which drags to retime it.</summary>
    private static void DrawTimeline(CreatorScreen s)
    {
        var clip = s.SelectedClip;
        if (clip == null) return;
        int y = CreatorScreen.TimelineY;
        Raylib.DrawRectangle(CreatorScreen.PalW, y - 7, W - CreatorScreen.PalW, 14, PanelBg);

        // Play / pause.
        int px = CreatorScreen.PalW + 4;
        if (s.Playing)
        {
            Raylib.DrawRectangle(px, y - 3, 2, 7, Bright);
            Raylib.DrawRectangle(px + 4, y - 3, 2, 7, Bright);
        }
        else Raylib.DrawTriangle(new Vector2(px, y - 3), new Vector2(px, y + 4), new Vector2(px + 6, (float)y + 0.5f), Bright);
        // Loop: a ring when it loops, an arrow to a wall when it plays once.
        int lx = CreatorScreen.PalW + 18;
        if (clip.Loop) Raylib.DrawCircleLines(lx + 4, y + 1, 3.5f, Mid);
        else { Raylib.DrawRectangle(lx, y, 6, 1, Mid); Raylib.DrawRectangle(lx + 7, y - 3, 1, 7, Mid); }

        int x0 = CreatorScreen.TimelineX0, x1 = CreatorScreen.TimelineX1;
        Raylib.DrawRectangle(x0, y, x1 - x0, 2, Dim);
        // The clip's end, as a grip.
        Raylib.DrawRectangle(x1, y - 3, 3, 8, Mid);

        var times = s.KeyTimes;
        foreach (var t in times)
        {
            int kx = CreatorScreen.TimeToX(t);
            Raylib.DrawRectangle(kx - 1, y - 2, 3, 6, Select);
        }
        float ct = Math.Max(0f, s.ClipT);
        int hx = CreatorScreen.TimeToX(ct);
        Raylib.DrawRectangle(hx, y - 5, 1, 11, Palette.NeonRed);
    }

    private static void DrawBottomBar(CreatorScreen s)
    {
        var d = s.Design!;
        Raylib.DrawRectangle(0, CreatorScreen.BottomY, W, H - CreatorScreen.BottomY, PanelBg);
        var f = CreatorScreen.BottomFields(d);
        string[] words = { d.Name, d.Kind.ToString().ToUpperInvariant(), "BASE " + d.BaseClass.ToString().ToUpperInvariant() };
        for (int i = 0; i < 3; i++)
        {
            bool hot = s.Mouse.Y >= CreatorScreen.BottomY && s.Mouse.X >= f[i].X0 && s.Mouse.X < f[i].X1;
            if (hot) Raylib.DrawRectangle(f[i].X0, CreatorScreen.BottomY + 1, f[i].X1 - f[i].X0, 10, Scale(Palette.HudChrome, 0.15f));
            Text(words[i], f[i].X0 + 2, CreatorScreen.BottomY + 3, i == 0 ? (s.Dirty ? Select : Bright) : hot ? Bright : Mid);
        }
        // The hint sits just above the bar, in the view, so it never runs into the name.
        string hint = s.Hint;
        int hy = s.Current == CreatorScreen.Page.Animate ? CreatorScreen.TimelineY - 17 : CreatorScreen.BottomY - 10;
        Text(hint, CreatorScreen.PalW + (W - CreatorScreen.PalW - Measure(hint)) / 2, hy, Scale(Palette.HudChrome, 0.6f));
    }

    private static void DrawLibrary(CreatorScreen s, float elapsed)
    {
        Raylib.DrawRectangle(0, 0, CreatorScreen.LibX + CreatorScreen.LibW + 8, H, PanelBg);
        PixelFont.Draw("CREATOR", CreatorScreen.LibX, 12, 2, Scale(Palette.HudChrome, 0.8f + 0.2f * MathF.Abs(MathF.Sin(elapsed * 0.9f))));
        Text("OPEN A CHARACTER OR START BLANK", CreatorScreen.LibX, 32, Dim);

        int y = CreatorScreen.LibY;
        for (int r = 0; r < CreatorScreen.LibRows; r++)
        {
            int i = r + s.LibScroll;
            if (i >= s.Library.Count) break;
            var e = s.Library[i];
            bool on = i == s.LibIndex;
            if (on) Raylib.DrawRectangle(CreatorScreen.LibX - 2, y - 1, CreatorScreen.LibW, CreatorScreen.RowH, Scale(Select, 0.25f));
            Text(on ? "> " + e.Label : "  " + e.Label, CreatorScreen.LibX, y, on ? Bright : Mid);
            string note = e.Template ? "IN GAME" : e.Path != null ? "SAVED" : "";
            if (note.Length > 0)
                Text(note, CreatorScreen.LibX + CreatorScreen.LibW - 4 - Measure(note), y, e.Template ? Dim : Scale(Palette.GridNear, 0.9f));
            y += CreatorScreen.RowH;
        }
        var cur = s.Library[s.LibIndex];
        string detail = cur.Path != null ? System.IO.Path.GetFileName(cur.Path).ToUpperInvariant()
                      : cur.Template ? "OPENS A COPY - THE GAME IS NEVER CHANGED" : "";
        Text(detail, CreatorScreen.LibX, 190, Dim);
        if (s.LibraryPreview is { } d)
            Text($"{d.Kind.ToString().ToUpperInvariant()} - {d.Parts.Count} PARTS - {d.Clips.Count} ANIMS", CreatorScreen.LibX, 200, Dim);
        Text("CLICK TO OPEN   ESC BACK", CreatorScreen.LibX, H - 14, Dim);
        DrawStatus(s);
    }

    private static void DrawStatus(CreatorScreen s)
    {
        if (!s.StatusVisible) return;
        int w = Measure(s.Status) + 8;
        int x = CreatorScreen.PalW + (W - CreatorScreen.PalW - w) / 2;
        int y = CreatorScreen.TopH + 4;
        if (s.Current == CreatorScreen.Page.Library) { x = (W - w) / 2; y = H - 30; }
        Raylib.DrawRectangle(x, y, w, 12, new Color(5, 7, 10, 235));
        Raylib.DrawRectangleLines(x, y, w, 12, Scale(Select, 0.7f));
        Text(s.Status, x + 4, y + 2, Bright);
    }

    private static void DrawTextEntry(CreatorScreen s, float elapsed)
    {
        const int w = 180, h = 36;
        int x = (W - w) / 2, y = (H - h) / 2;
        Raylib.DrawRectangle(x, y, w, h, new Color(5, 7, 10, 245));
        Raylib.DrawRectangleLines(x, y, w, h, Select);
        Text("NAME", x + 6, y + 5, Dim);
        Text(s.TextEntry!, x + 6, y + 16, Bright);
        if ((int)(elapsed * 2.5f) % 2 == 0)
            Raylib.DrawRectangle(x + 6 + Measure(s.TextEntry!) + 2, y + 22, 5, 1, Bright);
        string ok = "ENTER OK  ESC CANCEL";
        Text(ok, x + w - 6 - Measure(ok), y + 26, Dim);
    }

    // =====================================================================================

    private static int Measure(string s) => Math.Max(0, PixelFont.Measure(s, 1) - 1);
    private static void Text(string s, int x, int y, Color c) => PixelFont.Draw(s, x, y, 1, c);
    private static void Centered(string s, int cx, int y, Color c) => Text(s, cx - Measure(s) / 2, y, c);

    private static Color Scale(Color c, float t)
        => new((byte)Math.Clamp(c.R * t, 0, 255), (byte)Math.Clamp(c.G * t, 0, 255), (byte)Math.Clamp(c.B * t, 0, 255), c.A);
}
