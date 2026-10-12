using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Creator;
using Unrendered.Rendering;

namespace Unrendered.UI;

/// <summary>
/// The creature and vehicle creator, opened from the title menu by pressing F4 three times.
/// Built to be worked the way Spore's creator is worked — with the mouse, by dragging, and
/// never by typing a number:
///
/// <list type="bullet">
/// <item>Drag a shape off the shapes bar onto the body. It snaps to the surface under the
/// cursor; off the centre line a mirrored twin appears on the other side; near the centre
/// line the two merge into one. Hold A to place it on one side only.</item>
/// <item>Drag a placed part to move it over the body; drag it off the body, or back onto the
/// bar, to throw it away. Alt-drag copies it.</item>
/// <item>Roll the wheel over a part to grow or shrink it. Drag its handles to stretch it,
/// its ring to turn it — Tab for the other two rings.</item>
/// <item>The spine: drag an end vertebra out to grow the body, in to shorten it; drag a middle
/// one to bend it; roll the wheel over one to fatten or thin that stretch.</item>
/// <item>Drag on empty space (or with the right button anywhere) to turn the camera round the
/// creature; the wheel over empty space zooms; the middle button pans.</item>
/// <item>PAINT: drag a colour onto a part. ANIMATE: pick a clip, scrub the timeline, and drag
/// parts to pose them — every pose is a key. PLAY: walk it round with WASD.</item>
/// </list>
///
/// <para>Owns all the state and reads all the input; <see cref="CreatorRenderer"/> only
/// draws. Layout constants live here so click targets and drawn things cannot disagree.</para>
/// </summary>
public sealed class CreatorScreen
{
    public enum Action { None, Back }
    public enum Page { Library, Build, Paint, Animate, Play }

    public sealed record LibraryEntry(string Label, string Note, Func<Design>? Make, string? Path, bool Template);

    // ---- layout, shared with the renderer -----------------------------------------------
    public const int W = Config.InternalWidth, H = Config.InternalHeight;
    public const int TopH = 13;
    public const int PalW = 60;
    public const int PalTop = 26;
    public const int Icon = 26;
    public const int PalCols = 2;
    public const int BottomY = H - 12;
    public const int TimelineY = BottomY - 12;
    public const int TimelineX0 = PalW + 34, TimelineX1 = W - 22;
    public const int LibX = 12, LibY = 44, LibW = 150, LibRows = 15, RowH = 9;
    public static readonly string[] PageNames = { "BUILD", "PAINT", "ANIMATE", "PLAY" };
    public static readonly string[] TopButtons = { "UNDO", "REDO", "SAVE" };

    // ---- library --------------------------------------------------------------------------
    public List<LibraryEntry> Library { get; } = new();
    public int LibIndex { get; private set; }
    public int LibScroll { get; private set; }

    // ---- the design on the bench -------------------------------------------------------------
    public Page Current { get; private set; } = Page.Library;
    public Design? Design { get; private set; }
    public string? SourcePath { get; private set; }
    public bool Dirty { get; private set; }

    /// <summary>The part clicked last, whose rings and handles are showing.</summary>
    public int Selected { get; private set; } = -1;
    /// <summary>The part under the cursor.</summary>
    public int Hover { get; private set; } = -1;
    /// <summary>Show all three rings rather than just the spin ring (Tab, as in Spore).</summary>
    public bool AllRings { get; private set; }

    // ---- palette --------------------------------------------------------------------------
    public int Category { get; private set; }
    public int PaletteScroll { get; private set; }
    public List<PartCatalog.Item> PaletteItems => PartCatalog.In(PartCatalog.Categories[Category]);
    /// <summary>PAINT: the colour last picked up, which a plain click on a part applies.</summary>
    public int Brush { get; private set; } = 1;

    // ---- animation -----------------------------------------------------------------------------
    public int ClipIndex { get; private set; }
    public float Time { get; private set; }
    public bool Playing { get; private set; } = true;

    // ---- play mode -------------------------------------------------------------------------------
    public Vector2 PlayPos { get; private set; }
    public float PlayHeading { get; private set; }
    private string? _oneShot;
    private float _oneShotTime;

    // ---- camera (eased toward its targets so every move glides) ----------------------------
    private float _yaw = 0.6f, _pitch = 0.3f, _dist = 7f;
    private float _yawT = 0.6f, _pitchT = 0.3f, _distT = 7f;
    private Vector3 _target = new(0, 1, 0), _targetT = new(0, 1, 0);

    // ---- text entry and messages -----------------------------------------------------------------
    public string? TextEntry { get; private set; }
    public string Status { get; private set; } = "";
    private float _statusTime;
    private bool _leaveArmed;

    // ---- undo ------------------------------------------------------------------------------------
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();

    // ---- dragging --------------------------------------------------------------------------------
    private enum DragKind { None, Orbit, Pan, Part, Vertebra, Ring, Handle, Swatch, Scrub, KeyDot, Duration, Slider, Pose }
    private DragKind _drag;
    private MouseButton _dragButton;
    private Vector2 _pressAt;
    private bool _moved;
    private int _dragPart = -1;
    private bool _dragIsNew;
    private bool _reachGround;
    private int _ringAxis;
    private int _handleAxis;
    private float _ringAngle;
    private float _grabDepth = 4f;
    private int _dragSwatch;
    private float _keyDotT;
    private (Track Track, int Field) _slider;
    private string _undoAtPress = "";

    /// <summary>The dragged part is hanging in the air, over nothing: letting go deletes it.</summary>
    public bool Detached { get; private set; }
    /// <summary>The part being carried, or -1.</summary>
    public int Carried => _drag == DragKind.Part ? _dragPart : -1;
    /// <summary>The swatch being carried in PAINT, or -2 for none.</summary>
    public int CarriedSwatch => _drag == DragKind.Swatch ? _dragSwatch : -2;
    /// <summary>Which ring is being turned, or -1.</summary>
    public int ActiveRing => _drag == DragKind.Ring ? _ringAxis : -1;
    public Vector2 Mouse { get; private set; }

    /// <summary>World transform of every part this frame, as drawn and as clicked.</summary>
    public Dictionary<int, Matrix4x4> Pose { get; private set; } = new();

    private (LibraryEntry Entry, string Page, string Clip)? _captureOpen;

    public CreatorScreen()
    {
        RefreshLibrary();
        // Capture harness: UNRENDERED_CREATOR=<id>[:page[:clip]] opens straight onto a game
        // character (or "blank"/"vehicle"), so the bench can be photographed without input.
        // Deferred to the first update, because copying a game character needs the renderer.
        if (Environment.GetEnvironmentVariable("UNRENDERED_CREATOR") is { } open)
        {
            var bits = open.Split(':');
            var entry = Library.FirstOrDefault(e => string.Equals(e.Note, bits[0], StringComparison.OrdinalIgnoreCase));
            if (entry != null) _captureOpen = (entry, bits.Length > 1 ? bits[1] : "", bits.Length > 2 ? bits[2] : "");
        }
    }

    public void Reset()
    {
        RefreshLibrary();
        if (Design == null) Current = Page.Library;
        _leaveArmed = false;
    }

    public Part? SelectedPart => Design?.Find(Selected);
    public Clip? SelectedClip => Design != null && ClipIndex >= 0 && ClipIndex < Design.Clips.Count ? Design.Clips[ClipIndex] : null;

    /// <summary>Normalised time in the clip being shown, or -1 when holding the rest pose.</summary>
    public float ClipT
    {
        get
        {
            if (Current == Page.Play) return PlayClipT;
            var c = SelectedClip;
            if (c == null || Current != Page.Animate) return -1f;
            return c.Loop ? (Time / c.Duration) % 1f : Math.Clamp(Time / c.Duration, 0f, 1f);
        }
    }

    public Camera3D Camera
    {
        get
        {
            var dir = new Vector3(MathF.Sin(_yaw) * MathF.Cos(_pitch), MathF.Sin(_pitch), MathF.Cos(_yaw) * MathF.Cos(_pitch));
            return new Camera3D
            {
                Position = _target + dir * _dist,
                Target = _target,
                Up = Vector3.UnitY,
                FovY = Config.CameraFovY,
                Projection = CameraProjection.Perspective,
            };
        }
    }

    // =====================================================================================
    //  Update
    // =====================================================================================

    public Action Update(float dt)
    {
        if (_captureOpen is { } co)
        {
            _captureOpen = null;
            Open(co.Entry);
            if (co.Page == "demo")
            {
                DemoBuild();
                if (co.Clip.Length > 0) Current = co.Clip == "PLAY" ? Page.Play : Page.Animate;
            }
            Current = co.Page switch { "paint" => Page.Paint, "animate" => Page.Animate, "play" => Page.Play, _ => Current };
            if (co.Clip.Length > 0 && Design != null)
                ClipIndex = Math.Max(0, Design.Clips.FindIndex(c => c.Name == co.Clip.ToUpperInvariant()));
            if (co.Page is "select" or "animate" && Design != null)
                Selected = Design.Parts.FirstOrDefault(p => p.Parent >= 0 && p.Role != PartRole.Spine)?.Id ?? -1;
        }

        if (_statusTime > 0f) _statusTime -= dt;
        Mouse = Renderer.ScreenToInternal(Raylib.GetMousePosition());

        if (TextEntry != null) { UpdateTextEntry(); UpdatePose(); return Action.None; }
        if (Current == Page.Library) return UpdateLibrary();

        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (_drag != DragKind.None) { CancelDrag(); return Action.None; }
            if (Dirty && !_leaveArmed)
            {
                _leaveArmed = true;
                Say("UNSAVED - ESC AGAIN TO LEAVE");
            }
            else
            {
                _leaveArmed = false;
                Design = null;
                Current = Page.Library;
                RefreshLibrary();
            }
            return Action.None;
        }

        bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        if (ctrl && Raylib.IsKeyPressed(KeyboardKey.S)) Save(asNew: shift);
        if (ctrl && Raylib.IsKeyPressed(KeyboardKey.Z)) Undo();
        if (ctrl && Raylib.IsKeyPressed(KeyboardKey.Y)) Redo();
        if (Raylib.IsKeyPressed(KeyboardKey.Tab)) AllRings = !AllRings;
        if (Raylib.IsKeyPressed(KeyboardKey.F)) FrameCamera();
        if (Raylib.IsKeyPressed(KeyboardKey.Space) && Current == Page.Animate) Playing = !Playing;
        if ((Raylib.IsKeyPressed(KeyboardKey.Delete) || Raylib.IsKeyPressed(KeyboardKey.Backspace)) && Current == Page.Build
            && _drag == DragKind.None)
            DeleteSelected();

        UpdateClock(dt);
        if (Current == Page.Play) UpdatePlay(dt);
        UpdatePose();
        UpdateMouse(shift);
        EaseCamera(dt);
        UpdatePose();
        return Action.None;
    }

    private void UpdatePose()
    {
        if (Design == null) { Pose = new(); return; }
        float t = ClipT;
        var root = Current == Page.Play
            ? Matrix4x4.CreateRotationY(PlayHeading) * Matrix4x4.CreateTranslation(PlayPos.X, 0, PlayPos.Y)
            : Matrix4x4.Identity;
        var clip = Current == Page.Play ? PlayClip : SelectedClip;
        Pose = DesignPose.Evaluate(Design, t < 0f ? null : clip, Math.Max(0f, t), root);
    }

    private void UpdateClock(float dt)
    {
        var c = SelectedClip;
        if (Current != Page.Animate || c == null || !Playing || _drag is DragKind.Scrub or DragKind.Pose) return;
        Time += dt;
        if (!c.Loop && Time > c.Duration + 0.5f) Time = 0f;
        if (c.Loop && Time > c.Duration * 64f) Time %= c.Duration;
    }

    private void EaseCamera(float dt)
    {
        float k = 1f - MathF.Exp(-dt * 14f);
        _yaw += (_yawT - _yaw) * k;
        _pitch += (_pitchT - _pitch) * k;
        _dist += (_distT - _dist) * k;
        if (Current == Page.Play) _targetT = new Vector3(PlayPos.X, _targetT.Y, PlayPos.Y);
        _target += (_targetT - _target) * k;
    }

    // =====================================================================================
    //  The mouse
    // =====================================================================================

    private static bool OverPalette(Vector2 m) => m.X < PalW && m.Y > TopH && m.Y < BottomY;
    private static bool OverTop(Vector2 m) => m.Y < TopH;
    private bool OverTimeline(Vector2 m) => Current == Page.Animate && m.Y >= TimelineY - 6 && m.Y < BottomY && m.X >= PalW;

    private void UpdateMouse(bool shift)
    {
        var d = Design!;
        Vector2 m = Mouse;
        Vector2 delta = Raylib.GetMouseDelta() / Math.Max(1, Raylib.GetScreenWidth() / W);

        // Hover: what is under the cursor.
        Hover = -1;
        if (_drag == DragKind.None && !OverPalette(m) && !OverTop(m) && !OverTimeline(m) && m.Y < BottomY)
        {
            var (o, dir) = Ray(m);
            if (DesignPose.Raycast(d, Pose, o, dir) is { } h) Hover = h.Part;
        }

        // ---- the wheel ----------------------------------------------------------------
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0f && _drag == DragKind.None)
        {
            if (OverPalette(m)) PaletteScroll = Math.Max(0, PaletteScroll - (int)wheel);
            else if (Current == Page.Build && Hover >= 0 && d.Find(Hover) is { } hp && (hp.Parent >= 0 || hp.Role == PartRole.Spine))
                WheelPart(hp, wheel);
            else _distT = Math.Clamp(_distT * MathF.Pow(0.87f, wheel), 1.5f, 40f);
        }

        // ---- presses ------------------------------------------------------------------
        if (_drag == DragKind.None)
        {
            if (Raylib.IsMouseButtonPressed(MouseButton.Left)) Press(m, shift);
            else if (Raylib.IsMouseButtonPressed(MouseButton.Right))
            {
                if (OverTimeline(m) && KeyDotAt(m) is { } kt) DeleteKeysAt(kt);
                else Begin(DragKind.Orbit, MouseButton.Right);
            }
            else if (Raylib.IsMouseButtonPressed(MouseButton.Middle)) Begin(DragKind.Pan, MouseButton.Middle);
            return;
        }

        if (Vector2.Distance(m, _pressAt) > 1.5f) _moved = true;

        // ---- held -------------------------------------------------------------------------
        switch (_drag)
        {
            case DragKind.Orbit:
                _yawT -= delta.X * 0.025f;
                _pitchT = Math.Clamp(_pitchT + delta.Y * 0.02f, -0.35f, 1.45f);
                break;
            case DragKind.Pan:
            {
                var cam = Camera;
                Vector3 fwd = Vector3.Normalize(cam.Target - cam.Position);
                Vector3 right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
                Vector3 up = Vector3.Cross(right, fwd);
                float s = _dist * 0.004f;
                _targetT += (-right * delta.X + up * delta.Y) * s;
                break;
            }
            case DragKind.Part: if (_moved) DragPartTo(m); break;
            case DragKind.Vertebra: if (_moved) DragVertebraTo(m); break;
            case DragKind.Ring: DragRing(m); break;
            case DragKind.Handle: DragHandle(delta); break;
            case DragKind.Scrub: Scrub(m); break;
            case DragKind.KeyDot: DragKeyDot(m); break;
            case DragKind.Duration: DragDuration(delta); break;
            case DragKind.Slider: DragSlider(m); break;
            case DragKind.Pose: DragPose(delta); break;
        }

        if (Raylib.IsMouseButtonReleased(_dragButton)) Release(m, shift);
    }

    private void Begin(DragKind kind, MouseButton button)
    {
        _drag = kind;
        _dragButton = button;
        _pressAt = Mouse;
        _moved = false;
    }

    private void Press(Vector2 m, bool shift)
    {
        var d = Design!;
        if (OverTop(m)) { ClickTop(m); return; }

        if (OverPalette(m))
        {
            if (Current == Page.Build) PressPalette(m);
            else if (Current == Page.Paint) PressSwatches(m);
            else if (Current == Page.Animate) PressClipPanel(m);
            else if (Current == Page.Play) PressPlayPanel(m);
            return;
        }

        if (m.Y >= BottomY) { ClickBottom(m); return; }
        if (OverTimeline(m)) { PressTimeline(m); return; }

        // Rings and handles of the selected part sit in front of everything.
        if (Current is Page.Build or Page.Animate && SelectedPart is { } sel)
        {
            if (RingAt(m, sel) is { } axis)
            {
                SnapshotForDrag();
                _ringAxis = axis;
                _ringAngle = RingAngle(m, sel);
                Begin(DragKind.Ring, MouseButton.Left);
                return;
            }
            if (Current == Page.Build && HandleAt(m, sel) is { } h)
            {
                SnapshotForDrag();
                _handleAxis = h;
                Begin(DragKind.Handle, MouseButton.Left);
                return;
            }
        }

        var (o, dir) = Ray(m);
        if (DesignPose.Raycast(d, Pose, o, dir) is not { } hit)
        {
            if (Current != Page.Animate) Selected = -1;
            Begin(DragKind.Orbit, MouseButton.Left);
            return;
        }
        var part = d.Find(hit.Part)!;

        switch (Current)
        {
            case Page.Paint:
                Paint(part, Brush, shift);
                return;
            case Page.Animate:
                Selected = part.Id;
                SnapshotForDrag();
                _dragPart = part.Id;
                Begin(DragKind.Pose, MouseButton.Left);
                return;
            case Page.Play:
                Begin(DragKind.Orbit, MouseButton.Left);
                return;
        }

        // BUILD.
        Selected = part.Id;
        if (part.Role == PartRole.Spine)
        {
            SnapshotForDrag();
            _dragPart = part.Id;
            Begin(DragKind.Vertebra, MouseButton.Left);
            return;
        }
        if (part.Parent < 0)
        {
            // The body itself stays put — dragging it turns the camera.
            Begin(DragKind.Orbit, MouseButton.Left);
            return;
        }

        SnapshotForDrag();
        bool alt = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);
        if (alt) part = Duplicate(part);
        _dragPart = part.Id;
        _dragIsNew = alt;
        _reachGround = false;
        _grabDepth = hit.Distance;
        Selected = part.Id;
        Begin(DragKind.Part, MouseButton.Left);
    }

    private void Release(Vector2 m, bool shift)
    {
        var d = Design!;
        switch (_drag)
        {
            case DragKind.Part:
            {
                var p = d.Find(_dragPart);
                if (p != null && (Detached || OverPalette(m)))
                {
                    // Dropped on nothing, or back on the bar: gone, twin and all.
                    if (p.Twin >= 0) d.DeleteSubtree(p.Twin);
                    d.DeleteSubtree(p.Id);
                    Selected = -1;
                    if (_dragIsNew) { Design = DesignStore.FromJson(_undoAtPress); DropUndo(); }
                    else { Committed(); Say("REMOVED"); }
                }
                else if (_moved || _dragIsNew) Committed();
                else DropUndo();
                Detached = false;
                break;
            }
            case DragKind.Vertebra:
            case DragKind.Ring:
            case DragKind.Handle:
                if (_moved) Committed(); else DropUndo();
                break;
            case DragKind.Pose:
            case DragKind.KeyDot:
            case DragKind.Duration:
            case DragKind.Slider:
                if (_moved) { MarkClipEdited(); Committed(); } else DropUndo();
                break;
            case DragKind.Swatch:
                if (!OverPalette(m) && m.Y < BottomY)
                {
                    var (o, dir) = Ray(m);
                    if (DesignPose.Raycast(d, Pose, o, dir) is { } h) Paint(d.Find(h.Part)!, _dragSwatch, shift);
                }
                break;
        }
        _drag = DragKind.None;
        _dragPart = -1;
        _dragIsNew = false;
    }

    private void CancelDrag()
    {
        if (_undoAtPress.Length > 0)
        {
            Design = DesignStore.FromJson(_undoAtPress);
            DropUndo();
        }
        Detached = false;
        _drag = DragKind.None;
    }

    // =====================================================================================
    //  BUILD: parts
    // =====================================================================================

    private void PressPalette(Vector2 m)
    {
        if (m.Y < PalTop)
        {
            int n = PartCatalog.Categories.Length;
            Category = (Category + (m.X < PalW / 2 ? n - 1 : 1)) % n;
            PaletteScroll = 0;
            Audio.PlayBlip();
            return;
        }
        int idx = PaletteIndexAt(m);
        var items = PaletteItems;
        if (idx < 0 || idx >= items.Count) return;

        // Pick the shape up. It joins the design at once and is carried under the cursor;
        // dropped over nothing it is simply not added.
        SnapshotForDrag();
        var item = items[idx];
        var root = PartCatalog.Instantiate(Design!, item);
        root.Side = 1;
        _dragPart = root.Id;
        _dragIsNew = true;
        _reachGround = item.ReachGround && Design!.Kind == DesignKind.Creature;
        _grabDepth = _dist * 0.8f;
        Selected = root.Id;
        Begin(DragKind.Part, MouseButton.Left);
        _moved = true;
        DragPartTo(m);
    }

    /// <summary>
    /// Capture harness only: builds a creature on the fresh spine by dropping shapes at points
    /// on it, through exactly the path a mouse drag takes — the same ray, the same snapping,
    /// the same mirroring — so a screenshot shows what dragging really does.
    /// </summary>
    private void DemoBuild()
    {
        var v = Spine.Vertebrae(Design!);
        if (v.Count == 0) return;
        Vector3 At(Part p, Vector3 offset) { UpdatePose(); return Vector3.Transform(offset, Pose[p.Id]); }

        Drop("LEG", At(v[0], new(0.28f, -0.2f, 0f)));
        Drop("LEG", At(v[^1], new(0.28f, -0.2f, 0f)));
        Drop("HEAD", At(v[^1], new(0f, 0.05f, 0.25f)));
        Drop("WING", At(v[1], new(0.25f, 0.3f, 0f)));
        Drop("SPIKE", At(v[2], new(0f, 0.33f, 0f)));
        Selected = Design!.Parts.FirstOrDefault(p => p.Role == PartRole.Leg && p.Side > 0)?.Id ?? -1;

        void Drop(string label, Vector3 world)
        {
            UpdatePose();
            if (Project(world) is not { } m) return;
            var item = PartCatalog.All.First(i => i.Label == label);
            SnapshotForDrag();
            var root = PartCatalog.Instantiate(Design!, item);
            root.Side = 1;
            _dragPart = root.Id;
            _dragIsNew = true;
            _reachGround = item.ReachGround;
            _grabDepth = _dist;
            DragPartTo(m);
            if (Detached) { Design = DesignStore.FromJson(_undoAtPress); DropUndo(); }
            else Committed();
            _dragPart = -1;
            _dragIsNew = false;
            Detached = false;
        }
    }

    public int PaletteIndexAt(Vector2 m, int top = PalTop)
    {
        if (m.X < 2 || m.X >= 2 + PalCols * Icon || m.Y < top) return -1;
        int col = (int)((m.X - 2) / Icon), row = (int)((m.Y - top) / Icon);
        return (row + PaletteScroll) * PalCols + col;
    }

    /// <summary>
    /// Carries the dragged part to whatever surface is under the cursor, keeping its twin
    /// mirrored on the far side — or, over nothing, lets it hang in the air ready to be thrown
    /// away. Everything is live; nothing waits for the drop.
    /// </summary>
    private void DragPartTo(Vector2 m)
    {
        var d = Design!;
        var p = d.Find(_dragPart);
        if (p == null) return;
        var (o, dir) = Ray(m);

        var ignore = d.Subtree(p.Id).Select(x => x.Id).ToHashSet();
        if (p.Twin >= 0) foreach (var x in d.Subtree(p.Twin)) ignore.Add(x.Id);

        var pose = DesignPose.Evaluate(d, null, 0f, Matrix4x4.Identity);
        var hit = OverPalette(m) ? null : DesignPose.Raycast(d, pose, o, dir, ignore);

        if (hit is not { } h)
        {
            Detached = true;
            if (p.Twin >= 0) { d.DeleteSubtree(p.Twin); p.Twin = -1; }
            p.Parent = -1;
            p.Pivot = o + dir * _grabDepth;
            if (p.Stick) p.Mount = Vector3.UnitY;
            return;
        }
        Detached = false;
        _grabDepth = h.Distance;
        Place(d, p, h, pose);
        if (_reachGround) ReachGround(d, p);
    }

    private static void Place(Design d, Part p, DesignPose.Hit h, Dictionary<int, Matrix4x4> pose)
    {
        var q = d.Find(h.Part)!;
        bool asym = Raylib.IsKeyDown(KeyboardKey.A);
        Vector3 point = h.Point, normal = h.Normal;

        // Near the centre line the part snaps onto it and is one part, not two.
        bool centred = !asym && MathF.Abs(point.X) < 0.12f;
        if (centred)
        {
            point.X = 0f;
            normal.X = 0f;
            normal = normal.LengthSquared() < 1e-6f ? Vector3.UnitY : Vector3.Normalize(normal);
        }

        int newSide = centred ? 0 : MathF.Sign(point.X);
        if (newSide != 0 && p.Side != 0 && newSide != p.Side) ReflectInPlace(d, p);
        p.Side = newSide;

        Matrix4x4.Invert(pose[q.Id], out var inv);
        p.Parent = q.Id;
        p.Pivot = Vector3.Transform(point, inv) / q.Stretch;
        if (p.Stick)
        {
            var n = Vector3.TransformNormal(normal, inv);
            p.Mount = n.LengthSquared() < 1e-8f ? Vector3.UnitY : Vector3.Normalize(n);
        }
        else p.Mount = Vector3.UnitY;

        if (asym)
        {
            // One side only: the twin, if there is one, stays where it was, on its own.
            if (d.Find(p.Twin) is { } t) t.Twin = -1;
            p.Twin = -1;
            return;
        }
        if (centred)
        {
            if (p.Twin >= 0) { d.DeleteSubtree(p.Twin); p.Twin = -1; }
            return;
        }

        var twin = d.Find(p.Twin) ?? d.Find(d.MirrorSubtree(p.Id))!;
        twin.Parent = q.Twin >= 0 ? q.Twin : q.Id;
        foreach (var part in d.Subtree(p.Id)) d.SyncTwin(part);
        twin.Side = -p.Side;
    }

    /// <summary>Turns a part's assembly into its own mirror image where it stands — a fin
    /// built to reach right, carried across to the left, has to reach left.</summary>
    private static void ReflectInPlace(Design d, Part root)
    {
        foreach (var part in d.Subtree(root.Id))
        {
            if (part != root) part.Pivot = part.Pivot with { X = -part.Pivot.X };
            part.Rest = new Vector3(part.Rest.X, -part.Rest.Y, -part.Rest.Z);
            part.Mount = part.Mount with { X = -part.Mount.X };
            foreach (var b in part.Blocks)
            {
                b.Center = b.Center with { X = -b.Center.X };
                if (b.Tris != null)
                {
                    var t = (float[])b.Tris.Clone();
                    for (int i = 0; i < t.Length; i += 3) t[i] = -t[i];
                    b.Tris = t;
                }
            }
            part.Name = Creator.Design.SwapSide(part.Name);
        }
    }

    /// <summary>A leg dropped on a body is sized so its foot just reaches the ground, the way
    /// Spore's legs stand a creature up.</summary>
    private static void ReachGround(Design d, Part leg)
    {
        var pose = DesignPose.Evaluate(d, null, 0f, Matrix4x4.Identity);
        float top = pose[leg.Id].Translation.Y;
        float low = float.MaxValue;
        foreach (var part in d.Subtree(leg.Id))
            foreach (var b in part.Blocks)
            {
                var bm = DesignPose.BlockMatrix(part, b, pose[part.Id]);
                foreach (var (a, bb, c, _) in BlockGeometry.Triangles(b))
                    low = MathF.Min(low, MathF.Min(Vector3.Transform(a, bm).Y, MathF.Min(Vector3.Transform(bb, bm).Y, Vector3.Transform(c, bm).Y)));
            }
        float reach = top - low;
        if (top <= 0.05f || reach <= 0.01f) return;
        leg.Scale = Math.Clamp(leg.Scale * top / reach, 0.25f, 4f);
        d.SyncTwin(leg);
    }

    private Part Duplicate(Part part)
    {
        var d = Design!;
        int copyId = d.DuplicateSubtree(part.Id);
        var copy = d.Find(copyId)!;
        if (part.Twin >= 0)
        {
            int twinCopy = d.DuplicateSubtree(part.Twin);
            copy.Twin = twinCopy;
            d.Find(twinCopy)!.Twin = copyId;
        }
        Say("COPY");
        return copy;
    }

    private void WheelPart(Part p, float wheel)
    {
        var d = Design!;
        Edit();
        float f = MathF.Pow(1.08f, wheel);
        if (p.Role == PartRole.Spine) Spine.Girth(d, p, f);
        else
        {
            p.Scale = Math.Clamp(p.Scale * f, 0.1f, 8f);
            d.SyncTwin(p);
        }
        Committed();
    }

    private void DeleteSelected()
    {
        var d = Design!;
        var p = SelectedPart;
        if (p == null || p.Parent < 0) return;
        if (p.Role == PartRole.Spine && Spine.Vertebrae(d).Count <= 2) { Say("A SPINE NEEDS TWO"); return; }
        Edit();
        if (p.Twin >= 0) d.DeleteSubtree(p.Twin);
        d.DeleteSubtree(p.Id);
        if (p.Role == PartRole.Spine) Spine.Layout(d);
        Selected = -1;
        Committed();
    }

    // ---- the spine -------------------------------------------------------------------------

    private void DragVertebraTo(Vector2 m)
    {
        var d = Design!;
        var v = d.Find(_dragPart);
        if (v == null) return;
        var (o, dir) = Ray(m);
        // On the centre plane — the spine never leaves it. Seen edge-on, fall back to a plane
        // facing the camera through the vertebra.
        Vector3 point;
        if (MathF.Abs(dir.X) > 0.08f) point = o + dir * (-o.X / dir.X);
        else
        {
            var at = Pose[v.Id].Translation;
            var n = Vector3.Normalize(Camera.Position - at);
            point = o + dir * (Vector3.Dot(at - o, n) / Vector3.Dot(dir, n));
        }
        var rootM = v.Parent >= 0 && Pose.TryGetValue(v.Parent, out var rm) ? rm : Matrix4x4.Identity;
        Matrix4x4.Invert(rootM, out var inv);
        var local = Vector3.Transform(point, inv);
        local.Y = MathF.Max(0.15f, local.Y);
        Spine.Drag(d, v, local);
    }

    // ---- rings and handles ---------------------------------------------------------------

    /// <summary>The part's mounting frame in the world: its joint, and its three axes after it
    /// has been stood on its surface. Rings turn about these.</summary>
    public (Vector3 Origin, Vector3 X, Vector3 Y, Vector3 Z, float Radius) Frame(Part p)
    {
        var d = Design!;
        var parentM = p.Parent >= 0 && Pose.TryGetValue(p.Parent, out var pm) ? pm : Matrix4x4.Identity;
        var parent = d.Find(p.Parent);
        var mount = DesignPose.Align(p.Mount) * Matrix4x4.CreateTranslation(p.Pivot * (parent?.Stretch ?? Vector3.One)) * parentM;
        Vector3 x = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, mount));
        Vector3 y = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, mount));
        Vector3 z = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, mount));
        var (lo, hi) = DesignPose.PartBounds(p);
        float scale = new Vector3(parentM.M11, parentM.M12, parentM.M13).Length();
        float r = Math.Clamp((hi - lo).Length() * 0.45f * p.Scale * scale, 0.22f, 3f);
        return (Pose.TryGetValue(p.Id, out var own) ? own.Translation : mount.Translation, x, y, z, r);
    }

    /// <summary>The rings showing: just the spin ring, or all three with Tab (and always all
    /// three when posing).</summary>
    public int[] RingAxes => AllRings || Current == Page.Animate ? new[] { 1, 0, 2 } : new[] { 1 };

    public bool ShowsRings(Part p) => p.Role != PartRole.Spine && p.Parent >= 0;

    public Vector3 RingAxis(Part p, int axis)
    {
        var f = Frame(p);
        return axis == 0 ? f.X : axis == 1 ? f.Y : f.Z;
    }

    /// <summary>The ring as points in the world, for drawing and hit-testing alike.</summary>
    public Vector3[] RingPoints(Part p, int axis, int count = 32)
    {
        var f = Frame(p);
        Vector3 a = RingAxis(p, axis);
        Vector3 u = Vector3.Normalize(Vector3.Cross(a, MathF.Abs(a.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY));
        Vector3 v = Vector3.Cross(a, u);
        var pts = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float t = MathF.Tau * i / count;
            pts[i] = f.Origin + (u * MathF.Cos(t) + v * MathF.Sin(t)) * f.Radius;
        }
        return pts;
    }

    private int? RingAt(Vector2 m, Part p)
    {
        if (!ShowsRings(p)) return null;
        foreach (int axis in RingAxes)
            foreach (var pt in RingPoints(p, axis))
                if (Project(pt) is { } sp && Vector2.Distance(sp, m) < 3.5f) return axis;
        return null;
    }

    private float RingAngle(Vector2 m, Part p)
    {
        var c = Project(Frame(p).Origin) ?? m;
        return MathF.Atan2(m.Y - c.Y, m.X - c.X);
    }

    private void DragRing(Vector2 m)
    {
        var p = SelectedPart;
        if (p == null) return;
        float a = RingAngle(m, p);
        float delta = a - _ringAngle;
        while (delta > MathF.PI) delta -= MathF.Tau;
        while (delta < -MathF.PI) delta += MathF.Tau;
        _ringAngle = a;
        if (MathF.Abs(delta) < 1e-5f) return;
        _moved = true;

        // Screen y runs down, so a turn that looks anticlockwise has a falling angle; whether
        // that is a positive turn depends on which way the axis faces.
        Vector3 axisW = RingAxis(p, _ringAxis);
        bool facing = Vector3.Dot(axisW, Camera.Position - Frame(p).Origin) > 0f;
        float turn = facing ? -delta : delta;

        if (Current == Page.Animate)
        {
            NudgeKey(p, _ringAxis == 0 ? Channel.Pitch : _ringAxis == 1 ? Channel.Yaw : Channel.Roll, turn);
            return;
        }
        Vector3 axisL = _ringAxis == 0 ? Vector3.UnitX : _ringAxis == 1 ? Vector3.UnitY : Vector3.UnitZ;
        var rot = Matrix4x4.CreateFromYawPitchRoll(p.Rest.Y, p.Rest.X, p.Rest.Z) * Matrix4x4.CreateFromAxisAngle(axisL, turn);
        p.Rest = DesignPose.ToEuler(rot);
        Design!.SyncTwin(p);
    }

    /// <summary>The stretch handles: just past the far face of the part along each of its own
    /// axes.</summary>
    public Vector3[] HandlePoints(Part p)
    {
        if (!Pose.TryGetValue(p.Id, out var pm)) return Array.Empty<Vector3>();
        var (lo, hi) = DesignPose.PartBounds(p);
        var c = (lo + hi) * 0.5f;
        float pad = 0.1f / MathF.Max(0.1f, p.Scale);
        return new[]
        {
            Vector3.Transform(c with { X = hi.X + pad }, pm),
            Vector3.Transform(c with { Y = hi.Y + pad }, pm),
            Vector3.Transform(c with { Z = hi.Z + pad }, pm),
        };
    }

    public Vector3 HandleCentre(Part p)
    {
        var (lo, hi) = DesignPose.PartBounds(p);
        return Vector3.Transform((lo + hi) * 0.5f, Pose.GetValueOrDefault(p.Id, Matrix4x4.Identity));
    }

    public bool ShowsHandles(Part p) => Current == Page.Build && p.Role != PartRole.Spine;

    private int? HandleAt(Vector2 m, Part p)
    {
        if (!ShowsHandles(p)) return null;
        var hs = HandlePoints(p);
        for (int i = 0; i < hs.Length; i++)
            if (Project(hs[i]) is { } s && Vector2.Distance(s, m) < 4f) return i;
        return null;
    }

    private void DragHandle(Vector2 delta)
    {
        var p = SelectedPart;
        if (p == null) return;
        var hs = HandlePoints(p);
        if (Project(hs[_handleAxis]) is not { } tip || Project(HandleCentre(p)) is not { } mid) return;
        Vector2 along = tip - mid;
        float len = along.Length();
        if (len < 2f) return;
        float pull = Vector2.Dot(delta, along / len);
        if (MathF.Abs(pull) < 1e-3f) return;
        _moved = true;
        float f = Math.Clamp(1f + pull / MathF.Max(12f, len), 0.8f, 1.25f);
        var s = p.Stretch;
        s = _handleAxis switch { 0 => s with { X = s.X * f }, 1 => s with { Y = s.Y * f }, _ => s with { Z = s.Z * f } };
        p.Stretch = Vector3.Clamp(s, new Vector3(0.1f), new Vector3(10f));
        Design!.SyncTwin(p);
    }

    // =====================================================================================
    //  PAINT
    // =====================================================================================

    /// <summary>Swatch tiles: the palette, then ORIGINAL, which gives a copied game mesh its
    /// own colours back.</summary>
    public static int SwatchCount => ClassCatalog.Swatches.Length + 1;
    public const int SwatchTop = TopH + 14;

    private void PressSwatches(Vector2 m)
    {
        int idx = PaletteIndexAt(m, SwatchTop);
        if (idx < 0 || idx >= SwatchCount) return;
        int swatch = idx == ClassCatalog.Swatches.Length ? -1 : idx;
        Brush = swatch;
        _dragSwatch = swatch;
        Begin(DragKind.Swatch, MouseButton.Left);
    }

    private void Paint(Part p, int swatch, bool all)
    {
        var d = Design!;
        Edit();
        var targets = all ? d.Parts : new[] { p }.Concat(d.Find(p.Twin) is { } t ? new[] { t } : Array.Empty<Part>()).ToList();
        foreach (var part in targets)
            foreach (var b in part.Blocks)
                b.Swatch = swatch < 0 && b.Shape != BlockShape.Mesh ? b.Swatch : swatch;
        Committed();
    }

    // =====================================================================================
    //  ANIMATE
    // =====================================================================================

    public static readonly string[] ClipButtons = { "+ NEW", "COPY", "REBUILD", "DELETE" };
    public const int ClipRowsY = TopH + 4;

    public int ClipButtonsY => ClipRowsY + (Design?.Clips.Count ?? 0) * RowH + 3;
    public int TrackRowsY => ClipButtonsY + ClipButtons.Length * RowH + 6;

    private void PressClipPanel(Vector2 m)
    {
        var d = Design!;
        int row = (int)((m.Y - ClipRowsY) / RowH);
        if (m.Y >= ClipRowsY && row < d.Clips.Count) { SelectClip(row); return; }

        if (m.Y >= ClipButtonsY && m.Y < ClipButtonsY + ClipButtons.Length * RowH)
        {
            int b = (int)((m.Y - ClipButtonsY) / RowH);
            var clip = SelectedClip;
            Edit();
            switch (b)
            {
                case 0:
                    d.Clips.Add(new Clip { Name = $"CLIP {d.Clips.Count + 1}", Duration = 1.5f });
                    SelectClip(d.Clips.Count - 1);
                    Say("NEW CLIP - DRAG PARTS TO POSE THEM");
                    break;
                case 1 when clip != null:
                    var copy = clip.Clone();
                    copy.Name = Clip(clip.Name + " 2", 12);
                    copy.BuiltIn = false;
                    d.Clips.Add(copy);
                    SelectClip(d.Clips.Count - 1);
                    break;
                case 2 when clip != null:
                    if (AutoAnim.Build(d, clip.Name) is { } fresh) { d.Clips[ClipIndex] = fresh; Say("REBUILT FROM THE BODY"); }
                    else Say("ONLY THE BUILT-IN CLIPS REBUILD");
                    break;
                case 3 when clip != null && d.Clips.Count > 1:
                    d.Clips.RemoveAt(ClipIndex);
                    SelectClip(ClipIndex);
                    break;
            }
            Dirty = true;
            return;
        }

        if (SliderAt(m) is { } s)
        {
            SnapshotForDrag();
            _slider = s;
            Begin(DragKind.Slider, MouseButton.Left);
            DragSlider(m);
        }
    }

    /// <summary>The wave tracks of the selected part, each shown as three sliders.</summary>
    public List<Track> SliderTracks
        => SelectedClip?.Tracks.Where(t => t.Part == Selected && t.Mode != TrackMode.Keys).ToList() ?? new();

    public const int SliderX = 4, SliderW = PalW - 8;
    public static readonly string[] SliderNames = { "SWING", "SPEED", "TIMING" };

    public int SliderRowY(int track) => TrackRowsY + 9 + track * 30;

    private (Track, int)? SliderAt(Vector2 m)
    {
        var tracks = SliderTracks;
        for (int i = 0; i < tracks.Count; i++)
            for (int f = 0; f < 3; f++)
            {
                int y = SliderRowY(i) + 8 + f * 7;
                if (m.Y >= y - 2 && m.Y < y + 4 && m.X >= SliderX && m.X <= SliderX + SliderW) return (tracks[i], f);
            }
        return null;
    }

    /// <summary>0..1 position of a wave slider.</summary>
    public static float SliderValue(Track t, int field) => field switch
    {
        0 => Math.Clamp(MathF.Abs(t.Amp) / (CreatorChannels.IsMove(t.Channel) ? 0.5f : 1.5f), 0f, 1f),
        1 => (t.Cycles - 1) / 7f,
        _ => t.Phase,
    };

    private void DragSlider(Vector2 m)
    {
        var (t, field) = _slider;
        float v = Math.Clamp((m.X - SliderX) / SliderW, 0f, 1f);
        _moved = true;
        switch (field)
        {
            case 0: t.Amp = (t.Amp < 0 ? -1 : 1) * v * (CreatorChannels.IsMove(t.Channel) ? 0.5f : 1.5f); break;
            case 1: t.Cycles = 1 + (int)MathF.Round(v * 7f); break;
            default: t.Phase = v; break;
        }
    }

    private void PressTimeline(Vector2 m)
    {
        var c = SelectedClip;
        if (c == null) return;
        if (m.X < TimelineX0)
        {
            // The two buttons left of the strip: play/pause and loop.
            if (m.X < PalW + 16) Playing = !Playing;
            else { Edit(); c.Loop = !c.Loop; MarkClipEdited(); Dirty = true; }
            return;
        }
        if (m.X > TimelineX1 - 2)
        {
            SnapshotForDrag();
            Begin(DragKind.Duration, MouseButton.Left);
            return;
        }
        if (KeyDotAt(m) is { } kt)
        {
            SnapshotForDrag();
            _keyDotT = kt;
            Begin(DragKind.KeyDot, MouseButton.Left);
            return;
        }
        Begin(DragKind.Scrub, MouseButton.Left);
        Scrub(m);
    }

    /// <summary>Times of the selected part's keys, merged across its channels: one dot per
    /// moment the part was posed.</summary>
    public List<float> KeyTimes
    {
        get
        {
            var c = SelectedClip;
            if (c == null || Selected < 0) return new();
            var times = new List<float>();
            foreach (var t in c.Tracks.Where(t => t.Part == Selected && t.Mode == TrackMode.Keys))
                foreach (var k in t.Keys)
                    if (!times.Any(x => MathF.Abs(x - k.T) < 0.006f)) times.Add(k.T);
            times.Sort();
            return times;
        }
    }

    public static int TimeToX(float t) => TimelineX0 + (int)(t * (TimelineX1 - TimelineX0));

    private float? KeyDotAt(Vector2 m)
    {
        if (MathF.Abs(m.Y - TimelineY) > 4f) return null;
        foreach (var t in KeyTimes)
            if (MathF.Abs(TimeToX(t) - m.X) <= 2.5f) return t;
        return null;
    }

    private void Scrub(Vector2 m)
    {
        var c = SelectedClip;
        if (c == null) return;
        Playing = false;
        Time = Math.Clamp((m.X - TimelineX0) / (TimelineX1 - TimelineX0), 0f, 0.999f) * c.Duration;
    }

    private void DragKeyDot(Vector2 m)
    {
        var c = SelectedClip;
        if (c == null) return;
        float t = Math.Clamp((m.X - TimelineX0) / (TimelineX1 - TimelineX0), 0f, 1f);
        if (MathF.Abs(t - _keyDotT) < 1e-4f) return;
        _moved = true;
        foreach (var tr in c.Tracks.Where(tr => tr.Part == Selected && tr.Mode == TrackMode.Keys))
            foreach (var k in tr.Keys.Where(k => MathF.Abs(k.T - _keyDotT) < 0.006f))
                k.T = t;
        _keyDotT = t;
        Time = t * c.Duration;
    }

    private void DeleteKeysAt(float time)
    {
        var c = SelectedClip;
        if (c == null) return;
        Edit();
        foreach (var tr in c.Tracks.Where(tr => tr.Part == Selected && tr.Mode == TrackMode.Keys))
            tr.Keys.RemoveAll(k => MathF.Abs(k.T - time) < 0.006f);
        c.Tracks.RemoveAll(tr => tr.Mode == TrackMode.Keys && tr.Keys.Count == 0);
        MarkClipEdited();
        Dirty = true;
    }

    private void DragDuration(Vector2 delta)
    {
        var c = SelectedClip;
        if (c == null || MathF.Abs(delta.X) < 1e-3f) return;
        _moved = true;
        c.Duration = Math.Clamp(c.Duration * (1f + delta.X / 120f), 0.1f, 30f);
    }

    /// <summary>Dragging a part in ANIMATE turns it at its joint — sideways is yaw, up and down
    /// is pitch — and keys the pose at the playhead.</summary>
    private void DragPose(Vector2 delta)
    {
        var p = Design!.Find(_dragPart);
        if (p == null || delta.LengthSquared() < 1e-6f) return;
        _moved = true;
        Playing = false;
        if (MathF.Abs(delta.X) > 1e-3f) NudgeKey(p, Channel.Yaw, delta.X * 0.02f);
        if (MathF.Abs(delta.Y) > 1e-3f) NudgeKey(p, Channel.Pitch, delta.Y * 0.02f);
    }

    /// <summary>Adds <paramref name="by"/> to a part's channel at the playhead, as a key. A wave
    /// already on that channel is turned into keys first, so the motion already there stays.</summary>
    private void NudgeKey(Part p, Channel ch, float by)
    {
        var c = SelectedClip;
        if (c == null) return;
        float now = Math.Max(0f, ClipT);
        var tr = c.Tracks.FirstOrDefault(t => t.Part == p.Id && t.Channel == ch && t.Mode == TrackMode.Keys);
        if (tr == null)
        {
            var wave = c.Tracks.FirstOrDefault(t => t.Part == p.Id && t.Channel == ch);
            tr = new Track { Part = p.Id, Channel = ch, Mode = TrackMode.Keys };
            if (wave != null)
            {
                for (int i = 0; i < 8; i++) tr.Keys.Add(new Key { T = i / 8f, V = wave.Sample(i / 8f, c.Loop) });
                c.Tracks.Remove(wave);
            }
            else
            {
                tr.Keys.Add(new Key { T = 0f, V = 0f });
                if (!c.Loop) tr.Keys.Add(new Key { T = 1f, V = 0f });
            }
            c.Tracks.Add(tr);
        }
        var key = tr.Keys.FirstOrDefault(k => MathF.Abs(k.T - now) < 0.006f);
        if (key == null)
        {
            key = new Key { T = now, V = tr.Sample(now, c.Loop) - tr.Offset };
            tr.Keys.Add(key);
            tr.Keys.Sort((a, b) => a.T.CompareTo(b.T));
        }
        key.V += by;
        MarkClipEdited();
    }

    private void MarkClipEdited()
    {
        if (SelectedClip is { } c) c.Edited = true;
    }

    private void SelectClip(int i)
    {
        if (Design == null || Design.Clips.Count == 0) return;
        ClipIndex = Math.Clamp(i, 0, Design.Clips.Count - 1);
        Time = 0f;
        Playing = true;
    }

    // =====================================================================================
    //  PLAY: a test drive
    // =====================================================================================

    private Clip? PlayClip
    {
        get
        {
            var d = Design;
            if (d == null) return null;
            if (_oneShot != null) return d.Clips.FirstOrDefault(c => c.Name == _oneShot);
            bool moving = Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.S);
            return d.Clips.FirstOrDefault(c => c.Name == (moving ? "MOVE" : "IDLE")) ?? d.Clips.FirstOrDefault();
        }
    }

    private float PlayClipT
    {
        get
        {
            var c = PlayClip;
            if (c == null) return -1f;
            if (_oneShot != null) return Math.Clamp(_oneShotTime / c.Duration, 0f, 1f);
            return (Time / c.Duration) % 1f;
        }
    }

    private void UpdatePlay(float dt)
    {
        Time += dt;
        float turn = (Raylib.IsKeyDown(KeyboardKey.A) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.D) ? 1 : 0);
        float go = (Raylib.IsKeyDown(KeyboardKey.W) ? 1 : 0) - (Raylib.IsKeyDown(KeyboardKey.S) ? 0.5f : 0);
        PlayHeading += turn * 2.2f * dt;
        PlayPos += new Vector2(MathF.Sin(PlayHeading), MathF.Cos(PlayHeading)) * go * 3f * dt;
        if (Raylib.IsKeyPressed(KeyboardKey.Space) && Design!.Clips.Any(c => c.Name == "JUMP")) PlayOnce("JUMP");
        if (_oneShot != null)
        {
            _oneShotTime += dt;
            var c = Design!.Clips.FirstOrDefault(x => x.Name == _oneShot);
            if (c == null || _oneShotTime > c.Duration) _oneShot = null;
        }
    }

    private void PlayOnce(string name)
    {
        _oneShot = name;
        _oneShotTime = 0f;
    }

    private void PressPlayPanel(Vector2 m)
    {
        int row = (int)((m.Y - ClipRowsY) / RowH);
        if (row >= 0 && row < Design!.Clips.Count) PlayOnce(Design.Clips[row].Name);
    }

    // =====================================================================================
    //  Top and bottom bars
    // =====================================================================================

    public static int TabX(int i) => 2 + i * 44;
    public static int ButtonX(int i) => W - 2 - (TopButtons.Length - i) * 30;

    private void ClickTop(Vector2 m)
    {
        for (int i = 0; i < PageNames.Length; i++)
            if (m.X >= TabX(i) && m.X < TabX(i) + 42)
            {
                var was = Current;
                Current = (Page)(i + 1);
                if (Current == Page.Play) { PlayPos = Vector2.Zero; PlayHeading = 0f; Time = 0f; }
                else if (was == Page.Play) { _targetT = _targetT with { X = 0, Z = 0 }; }
                Audio.PlayBlip();
                return;
            }
        for (int i = 0; i < TopButtons.Length; i++)
            if (m.X >= ButtonX(i) && m.X < ButtonX(i) + 28)
            {
                if (i == 0) Undo();
                else if (i == 1) Redo();
                else Save(asNew: Raylib.IsKeyDown(KeyboardKey.LeftShift));
                return;
            }
    }

    /// <summary>The bottom bar: the design's name (click to rename), what it is, and which
    /// chassis it borrows. Each word is its own button.</summary>
    public static (int X0, int X1)[] BottomFields(Design d)
    {
        int nameW = PixelFont.Measure(d.Name, 1) + 4;
        int kindW = PixelFont.Measure(d.Kind.ToString(), 1) + 4;
        int baseW = PixelFont.Measure("BASE " + d.BaseClass, 1) + 4;
        int x = 2;
        var a = (x, x + nameW); x += nameW + 6;
        var b = (x, x + kindW); x += kindW + 6;
        var c = (x, x + baseW);
        return new[] { a, b, c };
    }

    private void ClickBottom(Vector2 m)
    {
        var d = Design!;
        var f = BottomFields(d);
        if (m.X >= f[0].X0 && m.X < f[0].X1) { TextEntry = d.Name; return; }
        if (m.X >= f[1].X0 && m.X < f[1].X1)
        {
            Edit();
            d.Kind = d.Kind == DesignKind.Creature ? DesignKind.Vehicle : DesignKind.Creature;
            Committed();
            return;
        }
        if (m.X >= f[2].X0 && m.X < f[2].X1)
        {
            Edit();
            int n = Enum.GetValues<PlayerClass>().Length;
            d.BaseClass = (PlayerClass)(((int)d.BaseClass + 1) % n);
            Dirty = true;
        }
    }

    // =====================================================================================
    //  Library
    // =====================================================================================

    private readonly Dictionary<string, Design> _captured = new();

    private Design CaptureCached(GameCapture.Entry e)
    {
        if (!_captured.TryGetValue(e.Id, out var d)) _captured[e.Id] = d = GameCapture.Capture(e);
        return d.Clone();
    }

    private void RefreshLibrary()
    {
        Library.Clear();
        Library.Add(new("+ NEW CREATURE", "blank", () => DesignTemplates.Blank(DesignKind.Creature), null, false));
        Library.Add(new("+ NEW VEHICLE", "vehicle", () => DesignTemplates.Blank(DesignKind.Vehicle), null, false));
        foreach (var e in GameCapture.All)
            Library.Add(new(e.Label, e.Id, () => CaptureCached(e), null, true));
        foreach (var (path, design, error) in DesignStore.List())
        {
            string file = System.IO.Path.GetFileName(path);
            Library.Add(design != null
                ? new(design.Name, file, null, path, false)
                : new("! " + file, error ?? "unreadable", null, path, false));
        }
        LibIndex = Math.Clamp(LibIndex, 0, Library.Count - 1);
        _previewFor = -1;
    }

    private int _previewFor = -1;
    private Design? _preview;

    public Design? LibraryPreview
    {
        get
        {
            if (_previewFor == LibIndex) return _preview;
            _previewFor = LibIndex;
            var e = Library[LibIndex];
            try { _preview = e.Make != null ? e.Make() : DesignStore.FromJson(File.ReadAllText(e.Path!)); }
            catch { _preview = null; }
            return _preview;
        }
    }

    private Action UpdateLibrary()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) return Action.Back;
        if (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressedRepeat(KeyboardKey.Up)) MoveLib(-1);
        if (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressedRepeat(KeyboardKey.Down)) MoveLib(1);
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)) Open(Library[LibIndex]);

        Vector2 m = Mouse;
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0f) LibScroll = Math.Clamp(LibScroll - (int)wheel, 0, Math.Max(0, Library.Count - LibRows));
        if (m.X >= LibX && m.X < LibX + LibW && m.Y >= LibY)
        {
            int row = (int)((m.Y - LibY) / RowH) + LibScroll;
            if (row >= 0 && row < Library.Count && row - LibScroll < LibRows)
            {
                // Hovering a row shows it turning on the right, as Spore's sporepedia does.
                if (Raylib.GetMouseDelta() != Vector2.Zero) LibIndex = row;
                if (Raylib.IsMouseButtonPressed(MouseButton.Left)) Open(Library[row]);
            }
        }
        return Action.None;
    }

    private void MoveLib(int step)
    {
        LibIndex = Math.Clamp(LibIndex + step, 0, Library.Count - 1);
        if (LibIndex < LibScroll) LibScroll = LibIndex;
        if (LibIndex >= LibScroll + LibRows) LibScroll = LibIndex - LibRows + 1;
        Audio.PlayBlip();
    }

    private void Open(LibraryEntry e)
    {
        try { Design = e.Make != null ? e.Make() : DesignStore.FromJson(File.ReadAllText(e.Path!)); }
        catch (Exception ex) { Say("CANNOT OPEN: " + ex.Message.ToUpperInvariant()); return; }
        SourcePath = e.Path;
        Dirty = false;
        _undo.Clear(); _redo.Clear();
        Current = Page.Build;
        Selected = -1;
        ClipIndex = 0; Time = 0f;
        FrameCamera(snap: true);
        Say(e.Template ? "A COPY - THE ORIGINAL IS NEVER CHANGED" : "OPENED " + e.Label);
    }

    private void FrameCamera(bool snap = false)
    {
        if (Design == null) return;
        var pose = DesignPose.Evaluate(Design, null, 0f, Matrix4x4.Identity);
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        foreach (var p in Design.Parts)
        {
            if (!pose.TryGetValue(p.Id, out var pm)) continue;
            var (bl, bh) = DesignPose.PartBounds(p);
            if (bl == bh) continue;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? bl.X : bh.X, (i & 2) == 0 ? bl.Y : bh.Y, (i & 4) == 0 ? bl.Z : bh.Z);
                var w = Vector3.Transform(c, pm);
                lo = Vector3.Min(lo, w);
                hi = Vector3.Max(hi, w);
            }
        }
        if (lo.X > hi.X) { lo = Vector3.Zero; hi = Vector3.One; }
        _targetT = (lo + hi) * 0.5f;
        _distT = Math.Clamp((hi - lo).Length() * 1.6f, 5f, 30f);
        if (snap) { _target = _targetT; _dist = _distT; _yaw = _yawT; _pitch = _pitchT; }
    }

    // =====================================================================================
    //  Projection
    // =====================================================================================

    /// <summary>The camera ray through a point of the 320×240 frame.</summary>
    public (Vector3 Origin, Vector3 Dir) Ray(Vector2 m)
    {
        var (pos, fwd, right, up, tan, aspect) = Basis();
        float nx = (m.X / W) * 2f - 1f;
        float ny = 1f - (m.Y / H) * 2f;
        return (pos, Vector3.Normalize(fwd + right * nx * tan * aspect + up * ny * tan));
    }

    /// <summary>Where a world point lands in the 320×240 frame, or null behind the camera.</summary>
    public Vector2? Project(Vector3 p)
    {
        var (pos, fwd, right, up, tan, aspect) = Basis();
        Vector3 v = p - pos;
        float z = Vector3.Dot(v, fwd);
        if (z < 0.05f) return null;
        float x = Vector3.Dot(v, right) / (z * tan * aspect);
        float y = Vector3.Dot(v, up) / (z * tan);
        return new Vector2((x + 1f) * 0.5f * W, (1f - y) * 0.5f * H);
    }

    private (Vector3, Vector3, Vector3, Vector3, float, float) Basis()
    {
        var cam = Camera;
        Vector3 fwd = Vector3.Normalize(cam.Target - cam.Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(fwd, Vector3.UnitY));
        Vector3 up = Vector3.Cross(right, fwd);
        return (cam.Position, fwd, right, up, MathF.Tan(cam.FovY * MathF.PI / 360f), (float)W / H);
    }

    // =====================================================================================
    //  Names, saving, undo
    // =====================================================================================

    private void UpdateTextEntry()
    {
        for (int ch = Raylib.GetCharPressed(); ch != 0; ch = Raylib.GetCharPressed())
        {
            char c = char.ToUpperInvariant((char)ch);
            if ((c is >= 'A' and <= 'Z' or >= '0' and <= '9' or ' ' or '-') && TextEntry!.Length < 20) TextEntry += c;
        }
        if ((Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace)) && TextEntry!.Length > 0)
            TextEntry = TextEntry[..^1];
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { TextEntry = null; return; }
        if (!Raylib.IsKeyPressed(KeyboardKey.Enter) && !Raylib.IsKeyPressed(KeyboardKey.KpEnter)) return;
        string name = TextEntry!.Trim();
        TextEntry = null;
        if (name.Length == 0 || Design == null) return;
        Edit();
        Design.Name = name;
        Dirty = true;
    }

    private void Save(bool asNew)
    {
        if (Design == null) return;
        try
        {
            string path = DesignStore.Save(Design, asNew ? null : SourcePath);
            SourcePath = path;
            Dirty = false;
            _leaveArmed = false;
            Say("SAVED CREATIONS/" + System.IO.Path.GetFileName(path).ToUpperInvariant());
        }
        catch (Exception e) { Say("SAVE FAILED: " + e.Message.ToUpperInvariant()); }
    }

    /// <summary>Records the design before a one-shot change.</summary>
    private void Edit()
    {
        if (Design == null) return;
        _undo.Add(DesignStore.ToJson(Design));
        if (_undo.Count > 60) _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>Records the design at the start of a drag, so the whole drag undoes in one.</summary>
    private void SnapshotForDrag()
    {
        _undoAtPress = DesignStore.ToJson(Design!);
        _undo.Add(_undoAtPress);
        if (_undo.Count > 60) _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>A drag that changed nothing leaves no undo step behind.</summary>
    private void DropUndo()
    {
        if (_undo.Count > 0 && ReferenceEquals(_undo[^1], _undoAtPress)) _undo.RemoveAt(_undo.Count - 1);
        _undoAtPress = "";
    }

    /// <summary>After any change to the body: mark it unsaved and let the built-in animations
    /// catch up with the new shape. Hand-edited ones are left alone.</summary>
    private void Committed()
    {
        Dirty = true;
        _leaveArmed = false;
        _undoAtPress = "";
        var d = Design;
        if (d == null) return;
        string keep = SelectedClip?.Name ?? "";
        AutoAnim.Rebuild(d);
        ClipIndex = Math.Max(0, d.Clips.FindIndex(c => c.Name == keep));
    }

    private void Undo()
    {
        if (_undo.Count == 0 || Design == null) { Say("NOTHING TO UNDO"); return; }
        _redo.Add(DesignStore.ToJson(Design));
        Design = DesignStore.FromJson(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        AfterHistory();
    }

    private void Redo()
    {
        if (_redo.Count == 0 || Design == null) { Say("NOTHING TO REDO"); return; }
        _undo.Add(DesignStore.ToJson(Design));
        Design = DesignStore.FromJson(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        AfterHistory();
    }

    private void AfterHistory()
    {
        Dirty = true;
        if (Design!.Find(Selected) == null) Selected = -1;
        ClipIndex = Math.Clamp(ClipIndex, 0, Math.Max(0, Design.Clips.Count - 1));
    }

    private void Say(string message)
    {
        Status = message;
        _statusTime = 3.5f;
    }

    public bool StatusVisible => _statusTime > 0f && Status.Length > 0;

    /// <summary>What the bottom bar suggests, given what the cursor is over.</summary>
    public string Hint
    {
        get
        {
            if (_drag == DragKind.Part) return Detached ? "LET GO TO THROW IT AWAY" : "HOLD A FOR ONE SIDE ONLY";
            if (_drag == DragKind.Vertebra) return "PULL AN END TO GROW THE SPINE";
            return Current switch
            {
                Page.Build => Hover >= 0 && Design?.Find(Hover)?.Role == PartRole.Spine
                    ? "DRAG TO BEND - WHEEL TO FATTEN"
                    : Hover >= 0 ? "DRAG MOVES - WHEEL SIZES - ALT COPIES" : "DRAG SHAPES ONTO THE BODY",
                Page.Paint => "DRAG A COLOUR ONTO A PART - SHIFT: ALL",
                Page.Animate => "DRAG PARTS TO POSE - RIGHT-CLICK CUTS A KEY",
                Page.Play => "WASD WALK - SPACE JUMP - CLICK A CLIP",
                _ => "",
            };
        }
    }

    public static string Clip(string s, int max) => s.Length <= max ? s : s[..Math.Max(1, max - 1)] + ".";
}

/// <summary>Small channel facts the screen and renderer share.</summary>
public static class CreatorChannels
{
    public static bool IsMove(Channel c) => c is Channel.MoveX or Channel.MoveY or Channel.MoveZ;
}
