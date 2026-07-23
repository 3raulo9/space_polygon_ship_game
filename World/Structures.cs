using System.Numerics;
using VoidTanks.Core;

namespace VoidTanks.World;

/// <summary>What a piece of the skyline is. The kind picks which family of meshes the
/// renderer pulls from; <see cref="Structure.Variant"/> picks one inside it.</summary>
public enum StructureKind
{
    /// <summary>A tower: a spire, a slab, a bundle of shafts, a leaning stack.</summary>
    Tower,

    /// <summary>An arc thrown clean across the grid on two legs, tall enough to
    /// drive under and wide enough that you meet one leg long before the other.</summary>
    Arch,
}

/// <summary>
/// One dead alien building standing on the grid, and — once something cuts it — the
/// state of it coming down.
///
/// Placement is fixed at construction and never changes. What <em>is</em> live is the
/// collapse: a struck structure runs a short cinematic (a shudder, a topple, a sink into
/// the grid) and is then dropped from the field. That is the only thing on a structure
/// that ticks; nothing here hunts, aims, or reacts to the player in any other way.
///
/// The shape itself lives in the renderer's prebuilt meshes, which are turned and scaled
/// per instance — see <see cref="Rendering.StructureMeshes"/>.
/// </summary>
public sealed class Structure
{
    /// <summary>Where it stands, in canonical wrapped torus coordinates.</summary>
    public readonly Vector2 Position;

    /// <summary>Which way it faces, radians about Y. For an arch this is the bearing of
    /// its span, so the two legs sit off either side of <see cref="Position"/>.</summary>
    public readonly float Heading;

    public readonly StructureKind Kind;

    /// <summary>Which mesh inside the kind's family. The renderer folds this into its
    /// array length, so adding shapes there never invalidates a laid-out field.</summary>
    public readonly int Variant;

    /// <summary>Uniform scale on the mesh — the one knob that makes a scatter of four
    /// shapes read as a skyline rather than as a repeated prop.</summary>
    public readonly float Scale;

    public Structure(Vector2 position, float heading, StructureKind kind, int variant, float scale)
    {
        Position = position;
        Heading = heading;
        Kind = kind;
        Variant = variant;
        Scale = scale;
        // Which way it goes over. Taken from the variant rather than rolled, so a given
        // building always falls the same way — the skyline is deterministic and its
        // demolition should be too.
        _tip = (variant & 1) == 0 ? 1f : -1f;
    }

    // Footprint radii at scale 1, matching the meshes the renderer builds. These are
    // what the craft is pushed out of and what rounds stop against, so they are
    // deliberately a little tighter than the geometry: brushing a corner should scrape,
    // not stop you dead.
    private const float TowerFootprint = 3.6f;
    private const float ArchLegFootprint = 2.0f;

    /// <summary>Half the span of an arch at scale 1 — the distance from its centre out
    /// to each leg. Shared with the mesh builder so the legs you drive into are the legs
    /// you can see.</summary>
    public const float ArchHalfSpan = 18f;

    // How tall each kind stands at scale 1: a tower to the tip of its spire, an arch leg
    // to the springing point where the curve takes over. Used to decide what a shot or a
    // beam passing overhead actually clears — the one place the sim needs a number about
    // geometry it otherwise leaves entirely to the renderer.
    private const float TowerHeight = 45f;
    private const float ArchLegHeight = 7f;

    /// <summary>How high this thing reaches — a beam or a round above this passes clean
    /// over it. An arch's answer is its <em>legs</em>: the span is up in the air and
    /// nothing is tested against it, so shots fly under an arc as freely as the craft
    /// drives under it.</summary>
    public float BlockHeight => (Kind == StructureKind.Tower ? TowerHeight : ArchLegHeight) * Scale;

    /// <summary>
    /// The solid parts of this structure on the plane, as circles nothing can pass
    /// through: one for a tower, one per leg for an arch. Written into
    /// <paramref name="into"/> and returns how many were written, so the collision passes
    /// allocate nothing per tick. A structure already coming down blocks nothing — the
    /// collapse is over as far as the sim is concerned, and only the picture is left.
    /// </summary>
    public int Blockers(Span<(Vector2 At, float Radius)> into)
    {
        if (Falling) return 0;

        if (Kind == StructureKind.Tower)
        {
            into[0] = (Position, TowerFootprint * Scale);
            return 1;
        }

        // The span runs along the structure's own right, so the legs land either side.
        var right = new Vector2(MathF.Cos(Heading), -MathF.Sin(Heading));
        Vector2 offset = right * (ArchHalfSpan * Scale);
        float r = ArchLegFootprint * Scale;
        into[0] = (Torus.Wrap(Position + offset), r);
        into[1] = (Torus.Wrap(Position - offset), r);
        return 2;
    }

    /// <summary>The most blockers any one structure contributes — the size the callers'
    /// stack buffers have to be.</summary>
    public const int MaxBlockers = 2;

    // --- Coming down --------------------------------------------------------------

    // The three beats of the collapse, in seconds. The shudder is what sells the cut as
    // a cut: the thing is hit, hangs for a fifth of a second doing nothing but shaking,
    // and only then admits it is falling. Skipping it makes a struck tower look like it
    // was already falling before the beam arrived.
    private const float ShudderTime = 0.22f;
    private const float ToppleTime = 1.6f;
    private const float SinkTime = 0.7f;

    /// <summary>How far over it goes: past horizontal, so the last thing you see is the
    /// spire sweeping down through the grid rather than a building lying flat on it.</summary>
    private const float FallAngle = MathF.PI * 0.56f;

    private readonly float _tip;
    private float _age;

    /// <summary>True from the moment something cuts it until it is dropped from the
    /// field. A falling structure blocks nothing and cannot be struck again.</summary>
    public bool Falling { get; private set; }

    /// <summary>True once the collapse has finished and the field should let it go.</summary>
    public bool Gone { get; private set; }

    /// <summary>The topple, in radians about the model's own nose/tail axis. Read by the
    /// renderer; zero on anything still standing.</summary>
    public float Roll { get; private set; }

    /// <summary>How far the base has sunk below the grid, as a negative height. The last
    /// beat of the collapse, and how the wreck leaves the world without a fade.</summary>
    public float Sink { get; private set; }

    /// <summary>
    /// Cuts it down. Returns false if it was already falling — which is what keeps a
    /// five-second beam burning through a tower from re-staging its death sixty times a
    /// second, and lets the caller sound the collapse exactly once.
    /// </summary>
    public bool Strike()
    {
        if (Falling) return false;
        Falling = true;
        _age = 0f;
        return true;
    }

    /// <summary>
    /// Steps a collapse. Returns true on the single tick the mass hits the grid — the
    /// beat the caller throws dust and sounds the impact. A no-op on anything standing.
    /// </summary>
    public bool Update(float dt)
    {
        if (!Falling || Gone) return false;

        float was = _age;
        _age += dt;

        if (_age < ShudderTime)
        {
            // Hanging there, shaking, not yet going anywhere.
            Roll = MathF.Sin(_age * 90f) * 0.012f;
            return false;
        }

        float t = Math.Clamp((_age - ShudderTime) / ToppleTime, 0f, 1f);
        // Squared, so it lets go slowly and arrives fast — a mass falling, rather than a
        // model being rotated at a constant rate.
        Roll = _tip * FallAngle * t * t;

        if (_age >= ShudderTime + ToppleTime)
        {
            float s = Math.Clamp((_age - ShudderTime - ToppleTime) / SinkTime, 0f, 1f);
            Sink = -s * BlockHeight * 0.6f;
            if (s >= 1f) Gone = true;
        }

        // The tick the topple completes: the mass is down.
        return was < ShudderTime + ToppleTime && _age >= ShudderTime + ToppleTime;
    }
}

/// <summary>
/// The skyline's layout: where the alien towers and arcs stand on this torus.
///
/// It is generated from a <em>constant seed</em> rather than from the ambient RNG, which
/// is the single decision that makes the rest of it work. The world is a fixed 400×400
/// square that wraps, so a skyline built once is a real place: the same tower stands on
/// the same patch of grid in every run, the capture harness photographs the same city
/// twice, and a player who drives a straight line long enough genuinely comes back round
/// to the arch they started under. Randomising it per run would buy nothing but a
/// different arrangement of the same shapes, at the cost of all of that.
///
/// Each caller gets its own <em>instances</em> of that layout via <see cref="Create"/>,
/// because structures can now be cut down and a run that levels three towers must not
/// level them in the title screen's backdrop as well.
/// </summary>
public static class StructureField
{
    // The city's seed. Change it and the whole world is re-laid-out.
    private const int Seed = 90210;

    // How many the layout asks for. Deliberately sparse: a building is worth something
    // as cover and as a landmark, and both of those need the grid between them to be
    // mostly empty. Ask for more and the void stops being a void.
    private const int TowerCount = 60;
    private const int ArchCount = 9;

    /// <summary>
    /// Nothing may stand within this of the origin. Three separate things want it: the
    /// hangar's turntable and the headless self-test both sit at (0,0), a run that opens
    /// with the player's nose inside a wall is nobody's idea of an opening, and the title
    /// screen idles its camera in here — so the radius has to be wide enough that the
    /// nearest building sits on the horizon behind the menu rather than through it.
    /// </summary>
    public const float ClearRadius = 55f;

    // How much room each kind takes up, at scale 1: roughly the radius of the ground it
    // actually covers. A tower is a few units across; an arc is its whole span, which is
    // why one of them is worth four times what the other is.
    private const float TowerExtent = 5f;
    private const float ArchExtent = 21f;

    /// <summary>
    /// The clear ground insisted on between any two structures, on top of both their
    /// extents. This is the only spacing knob, and it is deliberately the *only* one:
    /// separation is extent + extent + gap, measured pairwise, so a big arc keeps small
    /// towers off its span without also forbidding them from the next block over.
    ///
    /// Worth knowing before turning it up: the ground each structure needs grows as the
    /// square of the separation, so a modest increase here quietly starves the rejection
    /// sampler and the field comes up with a fraction of the buildings it asked for
    /// rather than telling you it failed. Change it and check the counts — the self-test
    /// puts a floor under them for exactly this reason.
    /// </summary>
    private const float Gap = 22f;

    private static List<Structure>? _backdrop;

    /// <summary>
    /// A shared, never-damaged copy of the city for the screens that have no world behind
    /// them — the title and settings backdrops. Built on first use.
    /// </summary>
    public static IReadOnlyList<Structure> Backdrop => _backdrop ??= Create();

    /// <summary>
    /// Set VOIDTANKS_STRUCTURES=0 to raze the city. The capture harness and anything
    /// wanting a bare grid to photograph a single entity against use this.
    /// </summary>
    private static bool Enabled
        => Environment.GetEnvironmentVariable("VOIDTANKS_STRUCTURES") != "0";

    /// <summary>
    /// Builds a fresh set of structures on the fixed layout — same buildings in the same
    /// places every time, but a new set of objects, so one world knocking a tower down
    /// leaves every other copy of the city standing.
    /// </summary>
    public static List<Structure> Create()
    {
        var placed = new List<Structure>(TowerCount + ArchCount);
        if (!Enabled) return placed;

        var rng = new Random(Seed);
        // Kept alongside so the spacing test can ask how much room each already-placed
        // structure takes without re-deriving it from the kind on every comparison.
        var claim = new List<float>(TowerCount + ArchCount);

        // Arcs first. They are the rarest and the hungriest for space, so letting them
        // pick their spots before the towers have filled the map is the difference
        // between nine arcs and three.
        Place(rng, placed, claim, StructureKind.Arch, ArchCount, ArchExtent, 0.75f, 1.6f);
        Place(rng, placed, claim, StructureKind.Tower, TowerCount, TowerExtent, 0.6f, 1.7f);

        return placed;
    }

    /// <summary>
    /// Rejection-samples <paramref name="count"/> structures into the field: roll a spot,
    /// keep it if it clears the origin and everything already standing, else roll again.
    /// Attempts are capped, so a field that has genuinely run out of room comes up short
    /// rather than looping forever — a slightly emptier skyline is a fine failure mode.
    /// </summary>
    private static void Place(Random rng, List<Structure> placed, List<float> claim,
        StructureKind kind, int count, float extent, float minScale, float maxScale)
    {
        int attempts = count * 150;
        int made = 0;

        while (made < count && attempts-- > 0)
        {
            var at = new Vector2(
                (rng.NextSingle() - 0.5f) * Torus.Size,
                (rng.NextSingle() - 0.5f) * Torus.Size);

            if (at.LengthSquared() < ClearRadius * ClearRadius) continue;

            // Scale is rolled before the spacing test, since how much ground this one
            // covers is most of what decides whether it fits where it landed.
            float scale = minScale + rng.NextSingle() * (maxScale - minScale);
            float mine = extent * scale;

            // Clear of everything already standing: both extents plus the gap, so the
            // requirement is set by the actual pair rather than by whichever of the two
            // happens to be the bigger. That distinction is what keeps arcs from vetoing
            // towers across half a map they have no business reaching.
            bool clear = true;
            for (int i = 0; i < placed.Count && clear; i++)
            {
                float want = mine + claim[i] + Gap;
                clear = Torus.DistanceSquared(at, placed[i].Position) > want * want;
            }
            if (!clear) continue;

            placed.Add(new Structure(at, rng.NextSingle() * MathF.Tau, kind, rng.Next(64), scale));
            claim.Add(mine);
            made++;
        }
    }
}
