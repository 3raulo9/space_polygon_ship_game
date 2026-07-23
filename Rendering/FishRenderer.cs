using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.Entities;

namespace VoidTanks.Rendering;

/// <summary>
/// Everything the FISH puts on screen: the parts of its own body that hang permanently in
/// view, and the screen effects that carry speed and depth.
///
/// The viewmodel matters more here than on any other chassis, for a specific reason. The
/// soldier needed two forearms because a person swinging through a city with nothing in
/// frame reads as a floating camera. A fish needs more than that: the entire pitch of the
/// class is <em>you are an animal, and the animal is the vehicle</em>, and there is no
/// cockpit, no gun and no hands to say so. What says it is a snout in the lower frame, two
/// pectoral fins at the edges rolling with every carve, and a lantern on a stalk swinging
/// out ahead of the eyes — which is also, not incidentally, the only light source anywhere
/// in this game.
///
/// Drawn in the camera's own frame rather than the world's, so the body stays welded to
/// the view through a bank that tips the horizon forty degrees.
/// </summary>
public sealed class FishRenderer
{
    /// <summary>
    /// The world half: the body in view. Called from inside the 3D pass, after the city
    /// and before the screen effects, so the snout and the fins sit over everything.
    /// </summary>
    public void Draw(World.World world, Vector3 cameraPos, float elapsed)
    {
        PlayerTank p = world.Player;
        if (p.Fish is not { } body) return;

        // The camera's own basis, rebuilt here rather than passed down: the viewmodel has
        // to hang off exactly the basis the Renderer aimed along, roll included, or the
        // fins swim about as the horizon goes over.
        Vector3 fwd = p.Forward3;
        var worldUp = new Vector3(0f, 1f, 0f);
        Vector3 right = Vector3.Normalize(Vector3.Cross(fwd, worldUp));
        Vector3 up = Vector3.Cross(right, fwd);

        float roll = body.Bank;
        if (roll != 0f)
        {
            float c = MathF.Cos(roll), s = MathF.Sin(roll);
            Vector3 r0 = right, u0 = up;
            right = r0 * c + u0 * s;
            up = u0 * c - r0 * s;
        }

        var view = new ViewFrame(cameraPos, fwd, right, up);
        Color hide = world.Loadout.PartColor(PlayerClass.Fish, 0);
        Color fin = world.Loadout.PartColor(PlayerClass.Fish, 1);
        Color belly = world.Loadout.PartColor(PlayerClass.Fish, 2);
        Color lure = world.Loadout.PartColor(PlayerClass.Fish, 3);

        DrawPectorals(view, body, fin, elapsed);
        DrawSnout(view, body, hide, belly);
        DrawLantern(view, body, lure, elapsed);
        DrawSpitFlash(view, body);
    }

    // --- The body in view ---------------------------------------------------------
    // Everything is placed in metres from the eye along the view's right / up / forward.
    // The numbers are small because the eye is genuinely inside the animal's head: the
    // snout is thirty centimetres away, which is why it fills the bottom of the frame the
    // way a nose does rather than sitting in the middle of it like a held weapon.

    /// <summary>
    /// The face: an upper and a lower jaw closing to a point in the lower centre of the
    /// frame, with a row of teeth along the bite.
    ///
    /// Two jaws rather than one wedge, and the extra four cylinders are the whole reason
    /// this reads as an animal. A single tapered shape at the bottom of the screen is a
    /// ramp — it has no scale, no direction and nothing to tell you which way is up. Split
    /// it along a bite line, put bone on that line, and the same silhouette is unmistakably
    /// a mouth pointed at whatever the crosshair is on.
    ///
    /// It is also the one thing on screen that never moves relative to the view, which is
    /// exactly what makes everything that <em>does</em> move read as movement.
    /// </summary>
    private static void DrawSnout(ViewFrame view, FishRig body, Color hide, Color belly)
    {
        // A strike stretches the head forward along its own line, and a coil pulls it back
        // into the frame as the animal gathers. The two together are most of what makes a
        // lunge feel like something the body did rather than a speed change.
        float reach = body.Strike switch
        {
            StrikeState.Coil => -0.12f * body.StrikePhase,
            StrikeState.Lunge => 0.20f,
            _ => 0f,
        };

        // A gulp on the frame the spit goes out, so the weapon has a mouth behind it.
        float gape = body.Flash * 0.06f;

        // Upper jaw: from just under the eye, out and down to the point. Kept narrow — a
        // fat one fills the bottom third of the frame and stops reading as a snout at all,
        // which is what a first draft of this did.
        Vector3 upRoot = view.At(0f, -0.32f, 0.44f + reach * 0.4f);
        Vector3 upTip = view.At(0f, -0.235f, 1.06f + reach);
        Raylib.DrawCylinderEx(upRoot, Vector3.Lerp(upRoot, upTip, 0.6f), 0.075f, 0.040f, 6, hide);
        Raylib.DrawCylinderEx(Vector3.Lerp(upRoot, upTip, 0.6f), upTip, 0.040f, 0.010f, 6, hide);

        // Lower jaw, undershot and in the pale belly tone so the bite line reads as a
        // line rather than as a crease in one solid.
        Vector3 loRoot = view.At(0f, -0.45f - gape, 0.44f + reach * 0.4f);
        Vector3 loTip = view.At(0f, -0.325f - gape * 2f, 0.94f + reach);
        Raylib.DrawCylinderEx(loRoot, Vector3.Lerp(loRoot, loTip, 0.6f), 0.062f, 0.034f, 6, belly);
        Raylib.DrawCylinderEx(Vector3.Lerp(loRoot, loTip, 0.6f), loTip, 0.034f, 0.008f, 6, belly);

        // Teeth hanging off the upper jaw into the gap. Placed by walking the jaw's own
        // line rather than by writing out view-space coordinates for each one, so they stay
        // welded to it through the stretch of a lunge instead of drifting off the front of
        // the face at the moment the player is most likely to be looking at it.
        for (int i = 0; i < 6; i++)
        {
            float t = 0.22f + i * 0.13f;
            float side = (i % 2 == 0 ? 1f : -1f) * (0.040f + 0.007f * i);
            Vector3 gum = Vector3.Lerp(upRoot, upTip, t)
                        + view.Right * side + view.Up * -0.030f;
            // Shorter nearer the point, the way a jaw's teeth are.
            Vector3 point = gum + view.Up * (-0.045f + 0.022f * t);
            Raylib.DrawCylinderEx(gum, point, 0.011f, 0.002f, 4, belly);
        }
    }

    /// <summary>
    /// The pectorals, one at each lower corner of the frame. These carry the entire
    /// handling model visually: they flare wide when the body is braking, sweep back and
    /// pin flat through a beat or a strike, and — the important one — the roll drags them
    /// through the frame, so a carve is something the player watches their own body do
    /// rather than only feeling through the horizon.
    /// </summary>
    private static void DrawPectorals(ViewFrame view, FishRig body, Color fin, float elapsed)
    {
        // How far back they are swept, 0 (flared out, braking) .. 1 (pinned, striking).
        float pinned = body.Strike == StrikeState.Lunge ? 1f
                     : body.Strike == StrikeState.Coil ? 0.7f
                     : Math.Clamp(body.PlanarSpeed / FishRig.MaxSpeed, 0f, 1f) * 0.55f
                       + body.Surge * 0.35f;
        float brake = Math.Clamp(body.MoveInput.Y, 0f, 1f);
        pinned = Math.Clamp(pinned - brake * 0.6f, 0f, 1f);

        for (int i = 0; i < 2; i++)
        {
            bool right = i == 0;
            float side = right ? 1f : -1f;

            // The scull: the two fins beat out of phase with each other, and slow right
            // down as the body picks up speed — a fish cruising holds its pectorals still
            // and only sculls when it is loitering.
            float idle = 1f - Math.Clamp(body.PlanarSpeed / 18f, 0f, 1f);
            float scull = MathF.Sin(elapsed * 2.4f + (right ? 0f : MathF.PI)) * 0.09f * idle;

            // Beached, they thrash: the one place on this chassis the animation is
            // deliberately ugly, because being on the deck is deliberately ugly.
            if (body.Beached) scull = MathF.Sin(elapsed * 17f + (right ? 0f : 2f)) * 0.16f;

            // Set well out and well down, so they live in the bottom corners of the frame
            // where peripheral motion belongs and never crowd the crosshair. Flared, they
            // reach almost to the edges; pinned, they fold back toward the body and mostly
            // leave the picture, which is exactly the readout wanted — a fish at speed is a
            // fish with its fins in.
            Vector3 root = view.At(side * 0.26f, -0.30f + scull, 0.52f);
            Vector3 tip = view.At(
                side * (0.86f - 0.30f * pinned),
                -0.40f + scull * 2.2f - 0.06f * pinned,
                0.34f - 0.30f * pinned);
            Vector3 heel = view.At(
                side * (0.44f - 0.10f * pinned),
                -0.36f + scull * 1.4f,
                0.10f - 0.22f * pinned);

            // Drawn as three thin cylinders round the fin's outline — a membrane read as
            // its own edges, which is all a fin can be at 320 across.
            Raylib.DrawCylinderEx(root, tip, 0.028f, 0.012f, 5, fin);
            Raylib.DrawCylinderEx(root, heel, 0.026f, 0.010f, 5, fin);
            Raylib.DrawCylinderEx(heel, tip, 0.012f, 0.010f, 4, fin);
        }
    }

    /// <summary>
    /// The lantern. A stalk off the crown arcing out over the snout with a lit bulb on the
    /// end, trailing a beat behind wherever the head has just been.
    ///
    /// It is doing three jobs at once and each of them alone would justify it: it says
    /// "fish" faster than any silhouette could, it gives a game with no lights anywhere a
    /// single moving highlight to look at, and — because it swings with the body's own lag
    /// rather than with the camera — it is a live readout of the gap between where the
    /// player is looking and where they are actually going, which is the one thing this
    /// chassis most needs to communicate.
    /// </summary>
    private static void DrawLantern(ViewFrame view, FishRig body, Color lure, float elapsed)
    {
        // The trail. Sideways lag comes from the roll (the stalk swings out of a carve),
        // and the backward lag from acceleration — a beat throws the lantern behind the
        // head and it catches up over the next half second.
        float sway = -body.Bank / FishRig.MaxBank * 0.42f
                   + MathF.Sin(elapsed * 1.7f) * 0.05f;
        float drag = body.Surge * 0.34f
                   + (body.Strike == StrikeState.Lunge ? 0.45f : 0f);

        // Carried out and — the part that actually matters — carried <em>off to one
        // side</em>. An angler wears its lure dead ahead of its own face, and copying that
        // here puts a bright magenta ball directly over the crosshair for the entire run,
        // which is unplayable. So the stalk is raked up and to the left: still visibly
        // growing out of the player's own head, still the only light in the frame, and
        // sitting in the corner of the eye where a real one would be at the edge of
        // attention rather than in the middle of it.
        const float Lean = 0.34f;   // how far off the centre line it is carried

        Vector3 crown = view.At(-0.05f, -0.14f, 0.40f);
        Vector3 knee = view.At(-Lean * 0.55f + sway * 0.35f, 0.28f, 0.92f - drag * 0.5f);
        Vector3 bulb = view.At(-Lean + sway, 0.46f, 1.52f - drag);

        // Hair-thin, for the same reason the soldier's cables are: the near end of this is
        // forty centimetres from the eye, so anything that looks a sensible thickness out
        // at the lantern is a pipe across a third of the screen down here.
        Color stalk = GridRenderer.LerpColor(lure, Palette.Void, 0.6f);
        Raylib.DrawCylinderEx(crown, knee, 0.005f, 0.007f, 5, stalk);
        Raylib.DrawCylinderEx(knee, bulb, 0.007f, 0.012f, 5, stalk);

        // The bulb: a hot white core inside a dimmer halo of the lure's own colour,
        // breathing slowly. The one genuinely bright thing the player sees for a whole run.
        float pulse = 0.84f + 0.16f * MathF.Sin(elapsed * 2.3f);
        float halo = 0.048f * pulse;
        float core = 0.020f * pulse;

        Raylib.DrawSphereEx(bulb, halo, 6, 6, new Color(lure.R, lure.G, lure.B, (byte)160));
        // The core is pulled far enough toward the eye to clear the halo's *front surface*
        // rather than merely its centre. Nesting one sphere inside another looks right in
        // the arithmetic and draws nothing: the outer shell writes depth on the way past,
        // so a core placed at the same centre is behind a wall of its own glow and the
        // lantern renders as a flat disc with no light in it.
        Raylib.DrawSphereEx(bulb - view.Forward * (halo - core + 0.006f), core, 5, 5,
            Color.White);
    }

    /// <summary>The spit going out: a short bright flare off the snout. Gone within a
    /// couple of frames — a flash that lasts long enough to look at is not a flash.</summary>
    private static void DrawSpitFlash(ViewFrame view, FishRig body)
    {
        if (body.Flash <= 0f) return;

        float f = body.Flash;
        Vector3 muzzle = view.At(0f, -0.19f, 0.66f);
        Raylib.DrawSphereEx(muzzle, 0.08f * f, 5, 5,
            new Color(190, 240, 220, (int)(200 * f)));
    }

    /// <summary>The camera's own basis for this frame, so a point in view space becomes a
    /// point in the world with one multiply-add.</summary>
    private readonly record struct ViewFrame(Vector3 Eye, Vector3 Forward, Vector3 Right, Vector3 Up)
    {
        /// <summary>Right / up / forward, in metres from the eye.</summary>
        public Vector3 At(float x, float y, float z) => Eye + Right * x + Up * y + Forward * z;
    }

    // --- Screen effects ------------------------------------------------------------

    /// <summary>Planar speed at which the water starts to show. Comfortably above what a
    /// single beat produces, so the whole effect is a reward for stringing them.</summary>
    private const float FastSpeed = 22f;

    /// <summary>
    /// The flat pass over the finished 3D frame: the murk closing in at speed, bubbles
    /// streaming past, and — the one screen state on this chassis that is a genuine alarm
    /// — the dry, drained look of a body on the seabed.
    ///
    /// There are no shaders anywhere in this game, so every one of these is built out of
    /// rectangles. At 320×240 that is not a compromise: a real underwater caustic here
    /// would be four pixels wide.
    /// </summary>
    public static void DrawScreenEffects(World.World world, float elapsed)
    {
        if (world.Player.Fish is not { } body) return;

        const int w = Config.InternalWidth;
        const int h = Config.InternalHeight;

        // Beached first and hardest. Everything else on this chassis is a gradient; this
        // is a state, and it has to be unmistakable from across the room.
        if (body.Beached) DrawBeachedWash(w, h, elapsed);

        // And the other end of the same axis. Drawn before the speed effects so the murk
        // and the bubbles sit over it rather than under it — a player sprinting into the
        // ceiling should see both, not one instead of the other.
        if (body.BloomNotice > 0f) DrawBloom(w, h, body, elapsed);

        float fast = Math.Clamp((body.PlanarSpeed - FastSpeed) / 16f, 0f, 1f);
        // A strike is always fast enough to show, whatever the body was doing before it.
        if (body.Strike == StrikeState.Lunge) fast = MathF.Max(fast, 0.85f);

        if (fast > 0.01f)
        {
            DrawMurk(fast);
            DrawBubbles(w, h, fast, elapsed);
        }

        // The gather before a strike: the frame draws in from all four sides for a fifth
        // of a second. The only moment in a run where the picture itself tells the player
        // they have committed to something.
        if (body.Strike == StrikeState.Coil)
        {
            int inset = (int)(body.StrikePhase * 14f);
            for (int i = 0; i < inset; i += 2)
                Raylib.DrawRectangleLines(i, i, w - i * 2, h - i * 2,
                    new Color(10, 26, 30, 120));
        }

        // Grazing a wall at speed: hard lines off the edge it was on, exactly as the
        // soldier's crash does. Keyed off the stagger, which is the only thing that knows
        // the body just met geometry.
        if (body.Stagger > 0f && !body.Beached)
        {
            int lines = (int)(body.Stagger * 20f);
            for (int i = 0; i < lines; i++)
            {
                int y = (i * 97 + (int)(elapsed * 240f)) % h;
                int len = 16 + (i * 13) % 38;
                var streak = new Color(200, 240, 250, (int)(90 * body.Stagger));
                Raylib.DrawRectangle(i % 2 == 0 ? 0 : w - len, y, len, 1, streak);
            }
        }
    }

    /// <summary>
    /// The water thickening at the edges as speed builds. Concentric frames of rising
    /// alpha rather than a gradient — both the only thing available here and exactly the
    /// right look, since the picture is already made of chunky pixels. Tinted toward the
    /// fog's cold green rather than the soldier's near-black, because this is water
    /// closing in and not darkness.
    /// </summary>
    private static void DrawMurk(float amount)
    {
        const int w = Config.InternalWidth;
        const int h = Config.InternalHeight;
        const int bands = 10;

        for (int i = 0; i < bands; i++)
        {
            int inset = i * 5;
            int alpha = (int)(amount * (bands - i) * 3.4f);
            if (alpha <= 0) continue;
            var col = new Color(8, 30, 34, Math.Min(alpha, 255));
            Raylib.DrawRectangleLines(inset, inset, w - inset * 2, h - inset * 2, col);
        }
    }

    /// <summary>
    /// Bubbles streaming past. Deterministic — a cheap hash off the index — so the field
    /// scrolls instead of boiling, which is what makes it read as water going past rather
    /// than as noise. Two pixels rather than the soldier's long streaks: air in water rises
    /// and tumbles, it doesn't draw lines.
    /// </summary>
    private static void DrawBubbles(int w, int h, float amount, float elapsed)
    {
        int count = (int)(14 + amount * 30);
        float scroll = elapsed * (120f + amount * 380f);

        for (int i = 0; i < count; i++)
        {
            int hash = i * 2654435761u.GetHashCode();
            int y = (int)(((hash >> 3) & 0x7FFF) % h);
            float band = (hash & 1) == 0 ? 1f : -1f;

            // Kept out toward the sides, where peripheral motion belongs, so the middle of
            // the frame — where the player is aiming — stays clear.
            float edge = ((hash >> 7) & 0xFF) / 255f;
            int x = band > 0 ? (int)(edge * w * 0.34f) : (int)(w - edge * w * 0.34f);

            int size = 1 + ((hash >> 11) & 1);
            int drift = (int)(scroll * (0.6f + edge)) % (w + 160) - 80;
            int at = band > 0 ? x - drift : x + drift;
            if (at < -size || at > w) continue;

            // Rising as they pass, which is the whole tell that this is a fluid.
            int rise = (int)(scroll * 0.35f) % h;
            var col = new Color(190, 235, 240, (int)(40 + 110 * amount));
            Raylib.DrawRectangle(at, (y - rise + h * 2) % h, size, size, col);
        }
    }

    /// <summary>
    /// The bloom, seen. It comes down from the <em>top</em> of the frame and nowhere else,
    /// which is the entire design of the effect: every other warning in this game is a
    /// gauge or a border, and a hazard that has a direction should be shown as having one.
    /// A player who has never read a word of the briefing learns from this alone that the
    /// bad thing is above them and the answer is to go down.
    ///
    /// Two stages, matching the two the sim runs. In the warned band it is a thin stain
    /// creeping over the horizon — free, and only unsettling. Inside the bloom proper it
    /// floods most of the frame and pulses at the rate the damage is being taken, so the
    /// picture and the shield bar are counting the same clock.
    /// </summary>
    private static void DrawBloom(int w, int h, FishRig body, float elapsed)
    {
        float notice = body.BloomNotice;
        float toxic = body.Toxicity;

        // How far down the frame it has reached. A third of the screen at the moment of
        // first warning, most of it once the bloom is genuinely biting.
        int depth = (int)(h * (0.10f + 0.30f * notice + 0.42f * toxic));
        float pulse = toxic > 0f ? 0.72f + 0.28f * MathF.Sin(elapsed * 7f) : 1f;

        for (int y = 0; y < depth; y++)
        {
            // Densest at the very top and fading out to nothing at its lower edge, so it
            // reads as something the player is swimming up into rather than as a filter
            // laid over the whole picture.
            float f = 1f - (float)y / depth;
            int alpha = (int)(f * f * (34f * notice + 90f * toxic) * pulse);
            if (alpha <= 0) continue;

            // The sky's own magenta, gone sick. Reusing the horizon hue is deliberate:
            // this is not a new colour arriving in the world, it is the colour that has
            // been glowing up there since the first frame of the game finally being
            // identified as the thing that it is.
            Raylib.DrawRectangle(0, y, w, 1,
                new Color(146, 40, 96, Math.Min(alpha, 235)));
        }

        // Inside it, flecks of the stuff drift down through the frame — the bloom as
        // particulate rather than as a tint, which is what stops it looking like a
        // post-process and starts it looking like water full of something.
        if (toxic <= 0f) return;

        int motes = (int)(10 + toxic * 26);
        for (int i = 0; i < motes; i++)
        {
            int hash = i * 2246822519u.GetHashCode();
            int x = (int)(((uint)hash >> 5) % (uint)w);
            int fall = (int)(elapsed * (18f + (hash & 15))) % h;
            int y = (int)((((uint)hash >> 17) % (uint)h + fall) % h);
            if (y > depth) continue;

            Raylib.DrawRectangle(x, y, 1, 1,
                new Color(225, 130, 190, (int)(120 + 100 * toxic)));
        }
    }

    /// <summary>
    /// Beached. The colour drains out of the edges of the frame and a dry band pumps in
    /// and out at the rate of a body working for air it cannot get. This is the one effect
    /// in the game that exists to make the player uncomfortable enough to fix it, and it
    /// is deliberately unpleasant to sit in.
    /// </summary>
    private static void DrawBeachedWash(int w, int h, float elapsed)
    {
        float gasp = 0.55f + 0.45f * MathF.Sin(elapsed * 4.4f);

        for (int i = 0; i < 12; i++)
        {
            int inset = i * 4;
            int alpha = (int)((12 - i) * 4.5f * gasp);
            if (alpha <= 0) continue;
            Raylib.DrawRectangleLines(inset, inset, w - inset * 2, h - inset * 2,
                new Color(96, 84, 60, Math.Min(alpha, 255)));
        }
    }
}
