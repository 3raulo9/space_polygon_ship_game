using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;

namespace VoidTanks.Rendering;

/// <summary>
/// The VIRUS, as the hangar shows it — which is a problem no other chassis on the roster
/// poses, because the virus is not a thing that has a shape. The tank is a hull, the fish a
/// body, the soldier a person; each is a silhouette you could photograph. This one is a
/// <em>process</em>: a mote of corruption that has no body of its own and steals whatever it
/// lands on. A model of it therefore can't be a vehicle sitting on the plate. It has to read
/// as the one honest picture of the class — a naked payload, mid-air, still trailing the
/// husk of the last thing it wore.
///
/// So it is built in four registers, one per paintable part, and none of them is solid the
/// way a chassis is:
/// <list type="bullet">
/// <item><b>The mote</b> — an angular energy core at the centre, this game's octahedron
/// (its shape for a thing that is light rather than matter, same as the cannon bolt and the
/// fish's lantern), spinning on its own.</item>
/// <item><b>The payload</b> — the one genuinely bright thing on it, a lit core pulsing
/// inside the mote, which is this game's whole convention for a living heart.</item>
/// <item><b>The veins</b> — filaments reaching outward from the core and retracting, the
/// infection casting about for a body.</item>
/// <item><b>The husk</b> — a few broken plates orbiting it at a distance, the shed carapace
/// of whatever it wore last, tumbling as it is discarded.</item>
/// </list>
///
/// Everything is tinted at draw time so the paint bay repaints the whole infection for free
/// — and the point of painting <em>this</em> chassis, unlike the others, is that the tint
/// travels: the same veins crawl over every host the player takes, so a build always
/// recognises its own rot.
/// </summary>
public sealed class VirusModel
{
    private readonly PolyMesh _core = BuildCore();
    private readonly PolyMesh _plate = BuildHuskPlate();

    /// <summary>How high the mote hangs in the hangar. Like the fish it has nothing to stand
    /// on and never will, so it floats — which is itself the first thing the picture says
    /// about the class before a word of the briefing is read.</summary>
    private const float HangarHover = 1.15f;

    /// <summary>How many husk plates tumble around it, and how many veins reach off it.</summary>
    private const int Plates = 4;

    /// <summary>
    /// Draws the mote turning slowly in the hangar for <paramref name="elapsed"/>. The
    /// turntable's rotation comes in as <paramref name="heading"/>; everything else — the
    /// core's own spin, the veins casting in and out, the husk tumbling, the payload
    /// breathing — is the idle, so the specimen is never still.
    /// </summary>
    public void Draw(Loadout loadout, Vector2 pos, float heading, Vector3 cameraPos, float elapsed)
    {
        Color mote = loadout.PartColor(PlayerClass.Virus, 0);
        Color veins = loadout.PartColor(PlayerClass.Virus, 1);
        Color husk = loadout.PartColor(PlayerClass.Virus, 2);
        Color payload = loadout.PartColor(PlayerClass.Virus, 3);

        float y = HangarHover + MathF.Sin(elapsed * 0.8f) * 0.05f;
        var centre = new Vector3(pos.X, y, pos.Y);

        DrawScene(pos, heading, y, centre, cameraPos, elapsed, mote, veins, husk, payload,
            corruption: 0.5f + 0.5f * MathF.Sin(elapsed * 0.6f));
    }

    /// <summary>
    /// The same infection, posed by the caller. The in-world viewmodel drives this from the
    /// live rig so the mote the player sees at the edge of their own view — and the veins that
    /// crawl over the host they are wearing — are the same geometry the hangar showed them.
    /// <paramref name="corruption"/> (0..1) opens the veins and quickens the tumble, so a
    /// rotting host visibly comes apart as its meter empties.
    /// </summary>
    public void DrawPosed(Vector2 pos, float heading, float baseHeight, Vector3 cameraPos,
        float elapsed, Color mote, Color veins, Color husk, Color payload, float corruption)
    {
        var centre = new Vector3(pos.X, baseHeight, pos.Y);
        DrawScene(pos, heading, baseHeight, centre, cameraPos, elapsed,
            mote, veins, husk, payload, corruption);
    }

    private void DrawScene(Vector2 pos, float heading, float y, Vector3 centre, Vector3 cameraPos,
        float elapsed, Color mote, Color veins, Color husk, Color payload, float corruption)
    {
        // The veins first, so the solids sit over them. They reach out along a fixed spray of
        // directions and pulse in and out — the more corrupted, the further and hungrier.
        DrawVeins(centre, veins, elapsed, corruption);

        // The husk: broken plates tumbling around the core at a distance, faster and wider
        // apart the more the thing is coming undone.
        float orbit = elapsed * (0.5f + corruption * 0.9f);
        float radius = 1f + corruption * 0.25f;
        for (int i = 0; i < Plates; i++)
        {
            float ring = orbit + i * (MathF.Tau / Plates);
            // Baked at unit radius, so scaling the whole plate pushes it out to the orbit.
            _plate.Draw(pos, heading + ring, y, cameraPos, radius, husk,
                pitch: MathF.Sin(elapsed * 1.1f + i) * 0.5f,
                roll: i * 0.6f + elapsed * 0.4f);
        }

        // The mote itself, spinning on its own on top of the turntable.
        _core.Draw(pos, heading + elapsed * 0.9f, y, cameraPos, 1f, mote,
            pitch: elapsed * 0.5f, roll: MathF.Sin(elapsed * 0.7f) * 0.4f);

        // And the living payload, the one bright thing in the whole picture.
        DrawPayload(centre, payload, cameraPos, elapsed);
    }

    // --- The veins ---------------------------------------------------------------

    /// <summary>A fixed spray of unit directions the filaments reach along — the six axes and
    /// the eight diagonals of a cube, so the reach reads as coming off the core in every
    /// direction at once rather than in a plane.</summary>
    private static readonly Vector3[] VeinDirs = BuildVeinDirs();

    private static void DrawVeins(Vector3 centre, Color veins, float elapsed, float corruption)
    {
        // Dimmed toward the void at the root and carrying the tint out to the tip, the same
        // hair-thin treatment the fish's lantern stalk gets — the near end is centimetres
        // from nothing and a "sensible" thickness would be a pipe.
        Color root = GridRenderer.LerpColor(veins, Palette.Void, 0.55f);

        for (int i = 0; i < VeinDirs.Length; i++)
        {
            // Each filament breathes on its own phase, so the whole cast never pulses as one.
            float beat = 0.5f + 0.5f * MathF.Sin(elapsed * 2.3f + i * 1.7f);
            float len = (0.55f + 0.45f * corruption) * (0.6f + 0.7f * beat);
            Vector3 tip = centre + VeinDirs[i] * len;

            Raylib.DrawCylinderEx(centre, tip, 0.05f, 0.004f, 5, root);
            // A bright node at the tip where the infection is groping — the one warm fleck.
            Raylib.DrawSphereEx(tip, 0.03f + 0.02f * beat, 4, 4, veins);
        }
    }

    private static Vector3[] BuildVeinDirs()
    {
        var dirs = new List<Vector3>
        {
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
            new(0, 0, 1), new(0, 0, -1),
        };
        float d = 0.577f; // 1/sqrt(3), the cube diagonals
        foreach (int sx in new[] { -1, 1 })
        foreach (int sy in new[] { -1, 1 })
        foreach (int sz in new[] { -1, 1 })
            dirs.Add(new Vector3(sx * d, sy * d, sz * d));
        return dirs.ToArray();
    }

    // --- The payload -------------------------------------------------------------

    /// <summary>The living core: a hot white pip inside a breathing halo of its own colour,
    /// the single genuinely bright object on the animal — the same lantern trick the fish
    /// uses, and for the same reason (a game with no lights needs its one core to glow).</summary>
    private static void DrawPayload(Vector3 centre, Color payload, Vector3 cameraPos, float elapsed)
    {
        float pulse = 0.82f + 0.18f * MathF.Sin(elapsed * 3.1f);
        float halo = 0.26f * pulse;
        float core = 0.11f * pulse;

        Raylib.DrawSphereEx(centre, halo, 8, 8, new Color(payload.R, payload.G, payload.B, (byte)150));

        // The white core pulled toward the eye so it clears the halo's near surface rather
        // than hiding behind a wall of its own glow — the fish lantern's lesson exactly.
        Vector3 toEye = cameraPos - centre;
        if (toEye.LengthSquared() > 1e-4f) toEye = Vector3.Normalize(toEye);
        Raylib.DrawSphereEx(centre + toEye * (halo - core + 0.01f), core, 6, 6, Color.White);
    }

    // --- Geometry ----------------------------------------------------------------

    /// <summary>
    /// The mote's shell: an octahedron, elongated on its vertical axis so it reads as a shard
    /// rather than a ball. The octahedron is deliberate — it is this game's shape for a thing
    /// made of light instead of matter, and the virus, having no body, is exactly that.
    /// </summary>
    private static PolyMesh BuildCore()
    {
        var m = new PolyMesh();
        const float r = 0.42f;
        Vector3 up = new(0f, r * 1.35f, 0f), down = new(0f, -r * 1.35f, 0f);
        Vector3 px = new(r, 0f, 0f), nx = new(-r, 0f, 0f);
        Vector3 pz = new(0f, 0f, r), nz = new(0f, 0f, -r);

        m.AddFace(Color.White, up, pz, px).AddFace(Color.White, up, px, nz);
        m.AddFace(Color.White, up, nz, nx).AddFace(Color.White, up, nx, pz);
        m.AddFace(Color.White, down, px, pz).AddFace(Color.White, down, nz, px);
        m.AddFace(Color.White, down, nx, nz).AddFace(Color.White, down, pz, nx);
        return m;
    }

    /// <summary>
    /// One shard of shed husk: a tall, thin plate bent along its centre so it reads as a
    /// piece of curved carapace rather than a flat card. Built out at unit radius along +Z so
    /// the caller can scale it to whatever orbit the corruption has flung it to.
    /// </summary>
    private static PolyMesh BuildHuskPlate()
    {
        var m = new PolyMesh();
        const float R = 0.85f;     // built at ~unit radius; the draw scales it to the orbit
        const float hw = 0.24f;    // half-width across the arc
        const float hh = 0.34f;    // half-height up the shard
        const float bulge = 0.10f; // how far the middle bows outward

        Vector3 tl = new(-hw, hh, R), bl = new(-hw, -hh, R);
        Vector3 tr = new(hw, hh, R), br = new(hw, -hh, R);
        Vector3 mt = new(0f, hh, R + bulge), mb = new(0f, -hh, R + bulge);

        // Two panels meeting at the bowed spine — a crease, so the flat shading gives the
        // shard two distinct lit faces instead of one dead card.
        m.AddFace(Color.White, tl, mt, mb, bl);
        m.AddFace(Color.White, mt, tr, br, mb);
        return m;
    }
}
