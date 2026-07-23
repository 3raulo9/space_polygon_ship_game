using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.Entities;

namespace VoidTanks.Rendering;

/// <summary>
/// Everything the SOLDIER puts on screen: the two cables and their hooks out in the
/// world, the forearms and launcher grips held permanently in view, and the screen
/// effects that carry speed — the vignette closing in, the wind streaks, the burst of
/// speed lines off a surface grazed at pace.
///
/// This is the only chassis with a viewmodel, and it needs one for a specific reason:
/// the game is first person and every other class is a vehicle, so there has never been
/// anything on screen belonging to the player. A person swinging through a city with
/// nothing in frame but the city reads as a floating camera. Two forearms fix that
/// entirely, and they cost eight cylinders.
///
/// Drawn as raw cylinders rather than as a <see cref="PolyMesh"/> because none of it is
/// a rigid model: a cable's shape is decided every frame by where its anchor is and how
/// much slack is in it, and the arms are placed in the camera's own frame rather than
/// in the world's.
/// </summary>
public sealed class SoldierRenderer
{
    // --- The cables --------------------------------------------------------------

    /// <summary>How many straight segments a cable is drawn as. Enough that the sag
    /// reads as a curve at the internal resolution, few enough that two of them cost
    /// nothing.</summary>
    private const int CableSegments = 9;

    /// <summary>
    /// How thick a cable draws. Deliberately hair-thin: its near end is half a metre
    /// from the eye, so anything that looks a sensible thickness out at the anchor is a
    /// white pipe across a third of the screen at this end. A steel line should be
    /// something the player looks past, not the thing they are looking at.
    /// </summary>
    private const float CableRadius = 0.022f;

    /// <summary>
    /// The world half: both cables, both hooks, and the arms holding them. Called from
    /// inside the 3D pass, after the city and before the screen effects.
    /// </summary>
    public void Draw(World.World world, Vector3 cameraPos, float elapsed)
    {
        PlayerTank p = world.Player;
        if (p.Soldier is not { } rig) return;

        // The camera's own frame, rebuilt here rather than passed down: the viewmodel
        // has to hang off exactly the basis the Renderer aimed the camera along,
        // including the bank, or the arms swim as the horizon rolls.
        Vector3 fwd = p.Forward3;
        var worldUp = new Vector3(0f, 1f, 0f);
        Vector3 right = Vector3.Normalize(Vector3.Cross(fwd, worldUp));
        Vector3 up = Vector3.Cross(right, fwd);

        // Roll the basis by the same amount, and in the same direction, that the camera
        // rolls its own up vector (see Renderer.DrawWorld).
        float roll = rig.Bank;
        if (roll != 0f)
        {
            float c = MathF.Cos(roll), s = MathF.Sin(roll);
            Vector3 r0 = right, u0 = up;
            right = r0 * c + u0 * s;
            up = u0 * c - r0 * s;
        }

        var view = new ViewFrame(cameraPos, fwd, right, up);

        // Where the launchers sit in view. The cables leave from here rather than from
        // the body's own hips, so the line the player watches pay out is visibly coming
        // out of the thing in their hands.
        Vector3 muzzleR = LauncherMuzzle(view, rig, p, elapsed, right: true);
        Vector3 muzzleL = LauncherMuzzle(view, rig, p, elapsed, right: false);

        var eyeXZ = new Vector2(cameraPos.X, cameraPos.Z);
        DrawCable(rig.Right, muzzleR, eyeXZ);
        DrawCable(rig.Left, muzzleL, eyeXZ);

        // The targeting ghost: a bracket standing on whatever the crosshair is resting
        // on, out in the world rather than on the glass. It is what lets the player read
        // an anchor at a glance mid-flight — the crosshair alone tells you *that* there
        // is something there, and this tells you *where*, which at forty metres and
        // thirty metres a second are very different pieces of information.
        //
        // Only while there is actually a hook left to fire. With both cables committed
        // it is marking a shot that cannot be taken, and a marker for an impossible
        // action is worse than no marker at all.
        bool spare = rig.Left.State == HookState.Stowed || rig.Right.State == HookState.Stowed;
        if (spare && world.AnchorInSight is { } ghost)
            DrawAnchorGhost(ghost, eyeXZ, world.AnchorIsWeak, elapsed);

        DrawArms(view, rig, p, elapsed);
        DrawMuzzleFlash(view, rig, p, elapsed);
    }

    /// <summary>
    /// The hook itself — the thing the whole class throws, so it is drawn as an actual
    /// piece of steel rather than a dot: a shank with three flukes splayed off it, big
    /// enough to be read against a tower at sixty metres.
    ///
    /// <paramref name="along"/> is the direction it is travelling or has bitten into;
    /// the flukes fan off the back of that, which is what makes a flying hook look
    /// thrown and an anchored one look driven in.
    /// </summary>
    private static void DrawHook(Vector3 at, Vector3 along, bool anchored, float scale = 1f)
    {
        if (along.LengthSquared() < 1e-6f) along = new Vector3(0f, 0f, 1f);
        along = Vector3.Normalize(along);

        // A frame to splay the flukes around.
        Vector3 side = MathF.Abs(along.Y) > 0.95f
            ? Vector3.Normalize(Vector3.Cross(along, new Vector3(1f, 0f, 0f)))
            : Vector3.Normalize(Vector3.Cross(along, new Vector3(0f, 1f, 0f)));
        Vector3 other = Vector3.Cross(along, side);

        Color steel = anchored ? Color.White : Palette.HudChrome;
        Vector3 tip = at + along * 0.30f * scale;
        Vector3 heel = at - along * 0.55f * scale;

        // The shank, tapering to the point that goes in.
        Raylib.DrawCylinderEx(heel, tip, 0.10f * scale, 0.03f * scale, 5, steel);

        // Three flukes, raked back off the heel like a grapnel's.
        for (int i = 0; i < 3; i++)
        {
            float a = i * MathF.Tau / 3f;
            Vector3 outward = side * MathF.Cos(a) + other * MathF.Sin(a);
            Vector3 knee = heel + outward * 0.20f * scale + along * 0.10f * scale;
            Vector3 barb = knee + outward * 0.16f * scale - along * 0.24f * scale;
            Raylib.DrawCylinderEx(heel, knee, 0.07f * scale, 0.05f * scale, 4, steel);
            Raylib.DrawCylinderEx(knee, barb, 0.05f * scale, 0.02f * scale, 4, steel);
        }

        // Bitten in, the head sits in a small burst of white — the visual half of the
        // clank, and what makes a successful anchor unmistakable at any range.
        if (anchored)
            Raylib.DrawSphereEx(tip, 0.16f * scale, 5, 5, new Color(255, 255, 255, 190));
    }

    /// <summary>
    /// The bracket standing on a valid anchor: four corner posts around the point the
    /// hook would bite, turning slowly so it reads as a marker rather than as geometry.
    /// A weak anchor — one that will tear out from under the player after a second under
    /// load — is drawn in the warning colour and with a broken bracket, so the city can
    /// be read for trustworthiness without ever stopping to look at it.
    /// </summary>
    private static void DrawAnchorGhost(Vector3 at, Vector2 eyeXZ, bool weak, float elapsed)
    {
        float dist = Vector3.Distance(at, new Vector3(eyeXZ.X, at.Y, eyeXZ.Y));
        // Scaled with range so the bracket keeps roughly the same size on screen —
        // otherwise it is a smudge at sixty metres and a wall at six.
        float s = Math.Clamp(dist * 0.045f, 0.5f, 3.2f);

        Color col = weak ? new Color(220, 90, 70, 210) : new Color(150, 240, 235, 200);
        float spin = elapsed * (weak ? 2.6f : 1.1f);

        for (int i = 0; i < 4; i++)
        {
            float a = spin + i * MathF.Tau / 4f;
            // A weak anchor's bracket stutters rather than turning smoothly.
            if (weak && ((int)(elapsed * 12f) + i) % 3 == 0) continue;

            var offset = new Vector3(MathF.Cos(a) * s, 0f, MathF.Sin(a) * s);
            Vector3 corner = at + offset;
            // Short posts set well out from the point: the crosshair has to sit inside
            // the bracket with clear air around it, or the two marks read as one blur
            // and the player can't tell which of them is the aim.
            Raylib.DrawCylinderEx(corner + new Vector3(0f, -s * 0.22f, 0f),
                corner + new Vector3(0f, s * 0.22f, 0f), 0.025f * s, 0.025f * s, 4, col);
        }
    }

    /// <summary>
    /// The muzzle flash, lighting the launcher that just fired. Drawn as a short bright
    /// cone out of the barrel plus a wash of light on the grip — at this resolution a
    /// real light would be four pixels of gradient, whereas a white cone in the corner
    /// of the frame reads instantly as a weapon going off.
    /// </summary>
    private static void DrawMuzzleFlash(ViewFrame view, SoldierRig rig, PlayerTank p, float elapsed)
    {
        if (rig.Flash <= 0f) return;

        Vector3 muzzle = LauncherMuzzle(view, rig, p, elapsed, rig.FlashOnRight);
        float f = rig.Flash;

        Raylib.DrawCylinderEx(muzzle, muzzle + view.Forward * (0.45f * f),
            0.14f * f, 0.02f, 5, new Color(255, 244, 210, (int)(230 * f)));
        Raylib.DrawSphereEx(muzzle, 0.1f * f, 5, 5, Color.White);
    }

    /// <summary>
    /// One cable, from the launcher in view to wherever its hook has got to, with the
    /// sag its slack earns and the whitening its tension does.
    ///
    /// The sag is the whole difference between a cable and a laser: a rope hanging
    /// between two points is a curve, and a taut one is a straight line, so how bent it
    /// is <em>is</em> the readout of whether the player is currently being pulled. It
    /// costs one sine per segment and tells the player more than any HUD element could.
    /// </summary>
    private static void DrawCable(GrappleHook h, Vector3 from, Vector2 eyeXZ)
    {
        if (h.State == HookState.Stowed) return;

        // Where the far end is. A flying hook's tip is already absolute near the player;
        // an anchor is a canonical point on the torus and has to be re-imaged, or a
        // cable across the world's seam is drawn four hundred units the wrong way.
        Vector3 to;
        if (h.Anchored)
        {
            Vector2 near = Torus.NearestImage(h.Tip, eyeXZ);
            to = new Vector3(near.X, h.TipY, near.Y);
        }
        else
        {
            to = new Vector3(h.Tip.X, h.TipY, h.Tip.Y);
        }

        // A retracting cable is drawn shortened toward the launcher rather than
        // vanishing, so the zip home is something the player sees happen.
        if (h.State == HookState.Returning)
        {
            float left = Math.Clamp(h.Flown / MathF.Max(1f, h.Reach), 0f, 1f);
            to = from + (to - from) * left;
        }

        float span = Vector3.Distance(from, to);
        if (span < 0.2f) return;

        // Slack: how much more rope there is than there is gap. A taut cable has none
        // and hangs dead straight; a paid-out one bellies down under its own weight.
        float slack = h.Anchored ? MathF.Max(0f, h.Length - span) : span * 0.06f;
        float sag = MathF.Min(span * 0.35f, slack * 0.75f + 0.15f);

        Color steel = Palette.HudChrome;
        // Under load the line whitens toward the anchor — the visible tension the spec
        // asks for, and the one place the cable stops looking like dead metal.
        Color hot = GridRenderer.LerpColor(steel, Color.White, Math.Clamp(h.Tension, 0f, 1f));

        Vector3 prev = from;
        for (int i = 1; i <= CableSegments; i++)
        {
            float t = (float)i / CableSegments;
            Vector3 point = Vector3.Lerp(from, to, t);
            point.Y -= sag * MathF.Sin(t * MathF.PI);

            // The near end stays plain steel and the far end takes the tension colour,
            // so the whitening reads as being concentrated at the bite.
            Color col = GridRenderer.LerpColor(steel, hot, t * t);
            Raylib.DrawCylinderEx(prev, point, CableRadius, CableRadius, 4, col);
            prev = point;
        }

        // And the hook on the end of it, pointed the way it flew.
        Vector3 along = h.Anchored ? Vector3.Normalize(to - prev) : h.Dir;
        DrawHook(to, along, h.Anchored);
    }

    // --- The viewmodel -----------------------------------------------------------

    /// <summary>The camera's own basis for this frame, so a point in view space becomes
    /// a point in the world with one multiply-add.</summary>
    private readonly record struct ViewFrame(Vector3 Eye, Vector3 Forward, Vector3 Right, Vector3 Up)
    {
        /// <summary>Right / up / forward, in metres from the eye.</summary>
        public Vector3 At(float x, float y, float z) => Eye + Right * x + Up * y + Forward * z;
    }

    // Where the kit hangs in view. Both forearms and both launcher grips stay on screen
    // at all times, low and to the sides, framing the picture without eating it.
    private const float HandX = 0.42f;     // out from the centre line
    private const float HandY = -0.34f;    // below the eye line
    private const float HandZ = 0.66f;     // out in front

    /// <summary>
    /// Both forearms and the launchers they hold. Their motion is the entire
    /// difference between a first-person game and a camera on a stick: they sway when
    /// the player idles, bob when they run, kick back on a jump and get dragged wide by
    /// a cable that is pulling.
    /// </summary>
    private static void DrawArms(ViewFrame view, SoldierRig rig, PlayerTank p, float elapsed)
    {
        for (int i = 0; i < 2; i++)
        {
            bool right = i == 0;
            Vector3 wrist = HandAt(view, rig, p, elapsed, right, forward: 0f);
            Vector3 muzzle = LauncherMuzzle(view, rig, p, elapsed, right);

            // The forearm, running back from the wrist toward the shoulder — off the
            // bottom of the frame, which is where an arm attached to a body goes.
            Vector3 elbow = HandAt(view, rig, p, elapsed, right, forward: -0.30f)
                          + view.Up * -0.16f + view.Right * (right ? 0.10f : -0.10f);
            Raylib.DrawCylinderEx(elbow, wrist, 0.062f, 0.048f, 6, Palette.PlayerFill);

            // The launcher housing in the fist, and the muzzle the cable leaves through.
            Raylib.DrawCylinderEx(wrist, muzzle, 0.07f, 0.04f, 6, Palette.CrabChassis);
            // A hard chrome collar at the muzzle, so the business end catches the light
            // and the two grips read as machined rather than as blocks.
            Raylib.DrawCylinderEx(
                Vector3.Lerp(wrist, muzzle, 0.82f), muzzle, 0.05f, 0.042f, 6, Palette.HudChrome);

            // The seated hook, when this launcher is holding one — the same piece of
            // steel that gets thrown, at a size that fits in the frame. Its absence is
            // the most direct readout the game has of a cable being out: the player can
            // see, without a glance at any gauge, which of their two hooks they still
            // have in hand.
            if (rig.Hook(right).State == HookState.Stowed)
                DrawHook(muzzle + view.Forward * 0.05f, view.Forward, anchored: false, scale: 0.2f);
        }
    }

    /// <summary>
    /// Where one wrist is this frame. Everything that makes the view feel inhabited
    /// lives in this function: the idle sway, the run bob, the recoil of a landing, and
    /// the pull of a cable dragging the arm out toward its anchor.
    /// </summary>
    private static Vector3 HandAt(ViewFrame view, SoldierRig rig, PlayerTank p,
        float elapsed, bool right, float forward)
    {
        float side = right ? HandX : -HandX;
        float x = side, y = HandY, z = HandZ + forward;

        // Idle sway on foot — a slow figure-of-eight, barely there, so a player standing
        // still is still visibly a person breathing rather than a tripod.
        float sway = rig.Grounded ? 1f : 0.35f;
        x += MathF.Sin(elapsed * 0.9f + (right ? 0f : 1.7f)) * 0.012f * sway;
        y += MathF.Sin(elapsed * 1.3f + (right ? 0.6f : 2.2f)) * 0.010f * sway;

        // Footstep bob while actually running: twice a stride, and only on the ground.
        if (rig.Grounded)
        {
            float pace = Math.Clamp(rig.PlanarSpeed / SoldierRig.GroundSpeed, 0f, 1f);
            y += MathF.Sin(elapsed * 9.5f) * 0.028f * pace;
            x += MathF.Sin(elapsed * 4.75f) * 0.018f * pace * (right ? 1f : -1f);
        }

        // A stagger throws both arms down and out — the knee the spec drops the
        // character to, expressed in the only two limbs that are on screen.
        y -= rig.Stagger * 0.22f;

        // And a cable under tension hauls its own arm up and outward toward the anchor,
        // so the player can see which of the two hooks is currently carrying them.
        GrappleHook h = rig.Hook(right);
        if (h.Anchored)
        {
            float pull = Math.Clamp(h.Tension, 0f, 1f);
            x += (right ? 0.07f : -0.07f) * pull;
            y += 0.09f * pull;
        }

        return view.At(x, y, z);
    }

    /// <summary>The muzzle of one launcher: out past the wrist along the view's forward,
    /// which is where the cable is drawn from and where the seated hook sits.</summary>
    private static Vector3 LauncherMuzzle(ViewFrame view, SoldierRig rig, PlayerTank p,
        float elapsed, bool right)
        => HandAt(view, rig, p, elapsed, right, forward: 0.34f)
           + view.Right * (right ? 0.05f : -0.05f);

    // --- Screen effects ----------------------------------------------------------

    /// <summary>Planar speed at which the vignette and the wind streaks begin. The spec's
    /// 20 m/s — comfortably faster than anything the class can do without a cable, so
    /// the whole effect only ever appears as a reward for swinging well.</summary>
    private const float FastSpeed = 20f;

    /// <summary>
    /// The flat pass over the finished 3D frame: the vignette closing in at speed, the
    /// wind streaking past, and the red wash of damage. Drawn after the world and under
    /// the HUD, so the instruments stay readable through all of it.
    ///
    /// There are no shaders anywhere in this game — the whole picture is a 320×240
    /// target blown up with nearest-neighbour — so every one of these is built out of
    /// rectangles. At this resolution that is not a compromise: a "motion blur" here
    /// would be four pixels wide.
    /// </summary>
    public static void DrawScreenEffects(World.World world, float elapsed)
    {
        if (world.Player.Soldier is not { } rig) return;

        const int w = Config.InternalWidth;
        const int h = Config.InternalHeight;

        float fast = Math.Clamp((rig.PlanarSpeed - FastSpeed) / 14f, 0f, 1f);

        if (fast > 0.01f)
        {
            DrawVignette(fast);
            DrawWindStreaks(w, h, fast, elapsed);
        }

        // Grazing a surface at speed: a burst of hard lines from the edge the wall is
        // on. Keyed off the crash/stagger state, which is the only thing that knows the
        // player just met geometry.
        if (rig.Stagger > 0f)
        {
            int lines = (int)(rig.Stagger * 22f);
            for (int i = 0; i < lines; i++)
            {
                int y = (i * 97 + (int)(elapsed * 240f)) % h;
                int len = 18 + (i * 13) % 40;
                var streak = new Color(235, 245, 255, (int)(90 * rig.Stagger));
                Raylib.DrawRectangle(i % 2 == 0 ? 0 : w - len, y, len, 1, streak);
            }
        }
    }

    /// <summary>
    /// The edges darkening as speed builds. Drawn as concentric frames of rising alpha
    /// rather than a gradient, which is both the only thing available here and exactly
    /// the right look: the picture is made of chunky pixels, so the vignette should be
    /// made of chunky bands.
    /// </summary>
    private static void DrawVignette(float amount)
    {
        const int w = Config.InternalWidth;
        const int h = Config.InternalHeight;
        const int bands = 9;

        for (int i = 0; i < bands; i++)
        {
            // Each band a little further in and a little more opaque, so the darkness
            // gathers at the corners rather than sitting as a border.
            int inset = i * 5;
            int alpha = (int)(amount * (bands - i) * 3.2f);
            if (alpha <= 0) continue;
            var col = new Color(5, 7, 10, Math.Min(alpha, 255));
            Raylib.DrawRectangleLines(inset, inset, w - inset * 2, h - inset * 2, col);
        }
    }

    /// <summary>
    /// Wind streaking past the edges of the view. Deterministic rather than random — a
    /// cheap hash off the streak's index — so the field doesn't boil between frames; it
    /// scrolls, which is what makes it read as air going past rather than as noise.
    /// </summary>
    private static void DrawWindStreaks(int w, int h, float amount, float elapsed)
    {
        int count = (int)(10 + amount * 22);
        float scroll = elapsed * (140f + amount * 420f);

        for (int i = 0; i < count; i++)
        {
            int hash = i * 2654435761u.GetHashCode();
            int y = (int)(((hash >> 3) & 0x7FFF) % h);
            float band = (hash & 1) == 0 ? 1f : -1f;

            // Streaks live near the sides, where peripheral motion belongs, and leave
            // the middle of the frame — where the player is aiming — clear.
            float edge = ((hash >> 7) & 0xFF) / 255f;
            int x = band > 0
                ? (int)(edge * w * 0.32f)
                : (int)(w - edge * w * 0.32f);

            int len = 6 + (int)(amount * 26) + ((hash >> 11) & 0x0F);
            int drift = (int)(scroll * (0.6f + edge)) % (w + 200) - 100;
            int at = band > 0 ? x - drift : x + drift;
            if (at < -len || at > w) continue;

            var col = new Color(200, 225, 240, (int)(30 + 90 * amount));
            Raylib.DrawRectangle(at, y, len, 1, col);
        }
    }
}
