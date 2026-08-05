using System.Numerics;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Entities;

namespace Unrendered.Rendering;

/// <summary>
/// Draws the DESCENT bosses. One renderer for all five lineages and every rolled variant,
/// because they are one construction (see <see cref="BossModel"/>) posed by one set of
/// animation channels the entity already advances.
///
/// <para><b>The pose is the telegraph.</b> Every phase of <see cref="ModularBoss.State"/> has a
/// silhouette you can read from across the arena with the HUD switched off: it rears back to
/// wind, throws itself forward to strike, and visibly sags in the beat afterward — which is the
/// beat you are meant to shoot it in. A boss whose only tell is a bar filling somewhere is a
/// boss nobody watches.</para>
///
/// <para><b>The model changes as it dies.</b> <see cref="ModularBoss.ShedLayers"/> plates stop
/// being drawn as they break, so a five-bar Colossus is a different object at each stage of the
/// fight — armoured slab, then plated frame, then a bare core walking on legs.</para>
/// </summary>
public sealed class BossRenderer
{
    /// <summary>Baked bodies, kept between frames. A genome is rolled once and drawn tens of
    /// thousands of times; rebuilding the stack per frame would be the most expensive thing in
    /// the renderer by a wide margin.</summary>
    private readonly Dictionary<BossGenome, BossModel> _models = new();

    /// <summary>How many bodies to keep before the cache is dropped wholesale. A run only ever
    /// meets five, so this is never reached in play — it exists so the bestiary screen, which
    /// can roll a fresh boss on every keypress, cannot leak.</summary>
    private const int CacheLimit = 32;

    private BossModel ModelFor(BossGenome gene)
    {
        if (_models.TryGetValue(gene, out var m)) return m;
        if (_models.Count >= CacheLimit) _models.Clear();
        m = new BossModel(gene);
        _models[gene] = m;
        return m;
    }

    /// <summary>
    /// Draws one boss. <paramref name="wrapShift"/> is the torus offset the world applies to
    /// re-image a distant object near the camera, exactly as the other entity draws take it.
    /// </summary>
    public void Draw(ModularBoss boss, Vector3 cameraPos, Vector2 wrapShift = default)
    {
        BossModel model = ModelFor(boss.Gene);
        BossGenome g = boss.Gene;

        Vector2 pos = boss.Position + wrapShift;
        float scale = g.Scale;
        float death = boss.DeathProgress;
        float fling = death * death;

        // A boss that is not there is not drawn — but it is not simply absent either: PHASED
        // leaves a flickering suggestion of the body so a player can still track where it went,
        // which is what makes the quirk a rhythm to learn rather than a coin toss.
        if (boss.Untouchable && boss.Phase is not (ModularBoss.State.Arriving
            or ModularBoss.State.Breaking) && Random.Shared.NextSingle() < 0.72f) return;

        // --- The body's attitude ----------------------------------------------------
        // One pitch and one roll, and between them they carry the whole read of what the thing
        // is doing. Everything bolted to the body rides them; the limbs do not, because their
        // feet are on the floor holding it up.
        (float pitch, float roll, float surge) = Attitude(boss);

        float baseY = boss.Height + boss.CarriageLift * scale;

        // Arrival: it comes up out of the grid and settles, rather than existing all at once.
        // The one long look a player gets at a silhouette nobody designed by hand.
        float arrive = boss.ArrivalProgress;
        float arriveDrop = (1f - arrive) * (1f - arrive) * boss.BodyHeight * 0.8f;
        baseY -= arriveDrop;

        Vector2 bodyPos = pos + Facing(boss.Heading) * surge;

        // --- Carriage ---------------------------------------------------------------
        if (model.Stand is { } stand && !Flicker(death))
        {
            Vector3 j = Jitter(death, 1.2f) + new Vector3(0f, -fling * 3f, 0f);
            stand.Draw(new Vector2(bodyPos.X + j.X, bodyPos.Y + j.Z), boss.Heading,
                boss.Height + j.Y, cameraPos, scale, DeathTint(g.Deep, death), pitch, roll);
        }

        // --- Limbs ------------------------------------------------------------------
        // Drawn before the body so a limb crossing the near side of the chassis sits behind it,
        // which at this resolution is the difference between a leg and a stripe.
        DrawLimbs(boss, model, bodyPos, baseY, cameraPos, death, fling);

        // --- The body ---------------------------------------------------------------
        if (!Flicker(death))
        {
            Vector3 j = Jitter(death, 1.6f) + new Vector3(0f, fling * 10f, 0f);
            model.Body.Draw(new Vector2(bodyPos.X + j.X, bodyPos.Y + j.Z),
                boss.Heading + fling * 5f, baseY + j.Y, cameraPos, scale,
                DeathTint(g.Shell, death), pitch, roll);
        }

        // --- The plates that are still on it ----------------------------------------
        // This is the shed-layer read. Rings the boss has already lost are simply not drawn,
        // and the outermost surviving one flares open while it winds — armour opening to let
        // something out is the oldest telegraph there is.
        for (int i = boss.ShedLayers; i < model.Plates.Length; i++)
        {
            if (Flicker(death)) continue;
            bool outermost = i == boss.ShedLayers;
            float flare = outermost ? boss.Wind * 0.5f : 0f;
            Vector3 j = Jitter(death, 2.0f);
            if (death > 0f) j += new Vector3(
                (Random.Shared.NextSingle() - 0.5f) * fling * 20f,
                fling * 9f,
                (Random.Shared.NextSingle() - 0.5f) * fling * 20f);

            model.Plates[i].Draw(new Vector2(bodyPos.X + j.X, bodyPos.Y + j.Z),
                boss.Heading + fling * 7f, baseY + j.Y + flare * scale, cameraPos,
                scale * (1f + flare * 0.28f),
                DeathTint(GridRenderer.LerpColor(g.Shell, g.Deep, 0.3f), death), pitch, roll);
        }

        // --- Spines -----------------------------------------------------------------
        if (model.Spine is { } spine && !Flicker(death))
        {
            for (int i = 0; i < g.SpineCount; i++)
            {
                float t = g.SpineCount <= 1 ? 0.5f : i / (g.SpineCount - 1f);
                var mount = new Vector3(0f, g.BodyRise * 1.45f, (t - 0.5f) * g.BodyDepth * 1.6f);
                // They bristle when it winds, which is a whole extra silhouette for free.
                float bristle = 1f + boss.Wind * 0.5f;
                DrawPart(spine, mount, 0f, bodyPos, boss.Heading, baseY, cameraPos, scale * bristle,
                    DeathTint(g.Deep, death), Jitter(death, 1.4f), pitch, roll, tiltPart: true);
            }
        }

        // --- The core ---------------------------------------------------------------
        // The one bright wrong thing, spun and heated by the entity's own channels. It burns
        // toward white as the wind-up fills, which is the tell that reads at any distance and
        // through any amount of fog.
        if (!Flicker(death))
        {
            Color core = death > 0f
                ? DeathTint(Palette.NeonRed, death)
                : GridRenderer.LerpColor(g.Core, Color.White, boss.CoreHeat * boss.CoreHeat);
            // Placed off the genome's own CoreLocalY, which the entity's CorePoint also reads —
            // so the gem you can see and the point a beam leaves are the same place.
            var mount = new Vector3(0f, g.CoreLocalY, 0f);
            Vector3 j = Jitter(death, 2.4f) + new Vector3(0f, fling * 14f, 0f);
            // Big enough to be the landmark on a body that may be thirty units across, and it
            // swells as it heats — the wind-up is legible from anywhere in the arena.
            DrawPart(model.Core, mount, boss.CoreSpin + fling * 22f, bodyPos, boss.Heading,
                baseY, cameraPos, scale * (0.95f + 0.35f * boss.CoreHeat), core, j,
                pitch, roll, tiltPart: true);
        }

        // The gathering light of whatever it is winding. Reuses the Crab-Core's lance flare so
        // a boss charging a beam looks like this game rather than like a new game.
        if (boss.Phase == ModularBoss.State.Winding && WindGlows(boss.Current))
            DrawCharge(boss, g, scale);
    }

    /// <summary>Which wind-ups gather visible light in the core. The ones that do not — a burrow,
    /// a shroud — are telegraphed by the body instead, and lighting them up would be a promise
    /// of a beam that never arrives.</summary>
    private static bool WindGlows(AttackModule m) => m is not
        (AttackModule.Burrow or AttackModule.Shroud or AttackModule.Bulwarks);

    private static void DrawCharge(ModularBoss boss, BossGenome g, float scale)
    {
        float t = boss.PhaseProgress;
        Vector3 at = boss.CorePoint;
        float pulse = 0.75f + 0.25f * MathF.Sin((float)Raylib.GetTime() * (12f + 24f * t));
        float r = (0.5f + 2.6f * t * t) * pulse * scale * 0.6f;
        Color hot = GridRenderer.LerpColor(g.Core, Color.White, t * t);
        Raylib.DrawSphereEx(at, r * 1.8f, 8, 8, new Color(g.Core.R, g.Core.G, g.Core.B, (int)(110 * t)));
        Raylib.DrawSphereEx(at, r, 8, 8, hot);
    }

    // --- The pose ------------------------------------------------------------------

    /// <summary>
    /// What the body is doing this frame, as a pitch, a roll and a surge along its own facing.
    /// This one function is the entire animation language of the fight — every phase reads
    /// differently from any angle and at any distance, with no HUD involved.
    /// </summary>
    private static (float Pitch, float Roll, float Surge) Attitude(ModularBoss boss)
    {
        float t = boss.PhaseProgress;
        float sway = MathF.Sin(boss.GaitPhase * 1.3f);

        switch (boss.Phase)
        {
            case ModularBoss.State.Arriving:
                // Straightening up as it comes out of the floor: it arrives hunched and
                // unfolds, so the last thing the arrival does is make it look bigger.
                return (-0.5f * (1f - t) * (1f - t), sway * 0.05f, 0f);

            case ModularBoss.State.Winding:
                // Rearing back and loading onto one side. The surge is negative — it physically
                // withdraws before it comes through, which is the read that makes a wind-up
                // legible even when the core is hidden behind a tower.
                return (0.34f * boss.Wind, 0.16f * boss.Wind * MathF.Sign(sway),
                    -2.2f * boss.Wind);

            case ModularBoss.State.Striking:
                // Through and down onto it, hard, then holding the follow-through.
                return (-0.42f * MathF.Sin(t * MathF.PI * 0.8f), -0.1f * sway,
                    3.4f * MathF.Sin(t * MathF.PI * 0.6f));

            case ModularBoss.State.Recover:
                // Slumped. The single most important pose in the fight: this is the window
                // that is worth double, and it has to look like an opening, not a pause.
                return (0.28f * (1f - t * 0.6f), 0.2f * (1f - t) * MathF.Sign(sway), -0.8f);

            case ModularBoss.State.Breaking:
                // Convulsing — a fast shudder on both axes, so a layer coming off is visibly
                // something happening to it rather than a number changing.
                return (MathF.Sin(t * 34f) * 0.18f, MathF.Sin(t * 27f) * 0.22f, 0f);

            case ModularBoss.State.Dying:
                return (t * 0.8f, t * 0.5f, 0f);

            default:
                // Walking. A slow breathing sway, faster and heavier the more broken it is.
                float ruin = 1f + boss.Ruin;
                return (sway * 0.035f * ruin, MathF.Sin(boss.GaitPhase * 0.8f) * 0.05f * ruin, 0f);
        }
    }

    /// <summary>
    /// The limbs. Each carries the gait, and each is pulled off it by whatever the body is
    /// doing: a winding boss plants wide and braces, a striking one throws its limbs back, a
    /// recovering one lets them hang.
    /// </summary>
    private static void DrawLimbs(ModularBoss boss, BossModel model, Vector2 bodyPos,
        float baseY, Vector3 cameraPos, float death, float fling)
    {
        BossGenome g = boss.Gene;
        bool walks = g.Carriage is Carriage.Legs or Carriage.Biped or Carriage.Husks;

        for (int i = 0; i < model.Limbs.Length; i++)
        {
            if (Flicker(death)) continue;
            var (mount, yaw, phaseOff) = model.Limbs[i];

            float phase = boss.GaitPhase + phaseOff;
            float lift = walks ? MathF.Max(0f, MathF.Sin(phase)) * 0.8f : 0f;
            float sweep = MathF.Cos(phase) * (walks ? 0.16f : 0.30f);

            // The brace: winding spreads the limbs out and drops the body onto them; striking
            // throws them back behind the line of the blow.
            float spread = boss.Wind * 0.42f;
            if (boss.Phase == ModularBoss.State.Striking) spread = -0.5f * boss.PhaseProgress;
            // A recovering boss is not holding itself up properly, and its limbs say so.
            if (boss.Phase == ModularBoss.State.Recover) lift *= 0.25f;

            float sign = MathF.Cos(yaw) >= 0f ? 1f : -1f;
            float finalYaw = yaw + sweep + spread * sign;

            // The one huge limb. Drawn at up to twice the length of the others, which is the
            // single most memorable thing a rolled silhouette can have.
            float limbScale = i == g.OversizeLimb ? 1.95f : 1f;

            Vector3 offset = Jitter(death, 1.4f);
            if (death > 0f)
            {
                Vector2 outward = Outward(mount, boss.Heading);
                offset += new Vector3(outward.X * fling * 18f, fling * 7f, outward.Y * fling * 18f);
            }

            var at = new Vector3(mount.X, mount.Y + lift, mount.Z);
            DrawPart(model.Limb, at, finalYaw + fling * 4f, bodyPos, boss.Heading, baseY,
                cameraPos, g.Scale * limbScale, DeathTint(g.Limb, death), offset,
                // Limbs stay upright while the chassis leans: their feet are on the floor
                // holding the thing up, and tipping them would swing the feet through it.
                pitch: 0f, roll: 0f, tiltPart: false);
        }
    }

    // --- Placement -----------------------------------------------------------------

    /// <summary>
    /// Draws one rigged part: its mount is scaled and rotated onto the body, the part then turns
    /// on its own hinge, and a world-space offset shoves it off the rig for the death glitch.
    /// Mirrors <see cref="CrabRenderer"/>'s own placement exactly — one rig convention for every
    /// multi-part thing in this game.
    /// </summary>
    private static void DrawPart(PolyMesh mesh, Vector3 localMount, float localYaw,
        Vector2 bodyPos, float heading, float baseY, Vector3 cameraPos, float scale,
        Color tint, Vector3 offset, float pitch, float roll, bool tiltPart)
    {
        Vector3 o = Tilt(localMount * scale, pitch, roll);
        float cos = MathF.Cos(heading), sin = MathF.Sin(heading);
        float rx = o.X * cos + o.Z * sin;
        float rz = -o.X * sin + o.Z * cos;
        mesh.Draw(new Vector2(bodyPos.X + rx + offset.X, bodyPos.Y + rz + offset.Z),
            heading + localYaw, baseY + o.Y + offset.Y, cameraPos, scale, tint,
            tiltPart ? pitch : 0f, tiltPart ? roll : 0f);
    }

    private static Vector3 Tilt(Vector3 v, float pitch, float roll)
    {
        if (roll != 0f)
        {
            float c = MathF.Cos(roll), s = MathF.Sin(roll);
            v = new Vector3(v.X * c - v.Y * s, v.X * s + v.Y * c, v.Z);
        }
        if (pitch != 0f)
        {
            float c = MathF.Cos(pitch), s = MathF.Sin(pitch);
            v = new Vector3(v.X, v.Y * c - v.Z * s, v.Y * s + v.Z * c);
        }
        return v;
    }

    private static Vector2 Facing(float heading) => new(MathF.Sin(heading), MathF.Cos(heading));

    private static Vector2 Outward(Vector3 mount, float heading)
    {
        float cos = MathF.Cos(heading), sin = MathF.Sin(heading);
        var v = new Vector2(mount.X * cos + mount.Z * sin, -mount.X * sin + mount.Z * cos);
        float len = v.Length();
        return len > 1e-4f ? v / len : new Vector2(1f, 0f);
    }

    // --- The death glitch ----------------------------------------------------------
    // Lifted wholesale from the Crab-Core, and deliberately: every big thing in this game comes
    // apart the same way — parts flung outward, colours slamming between hot glitch tones, whole
    // frames dropping out. It is one of the game's signatures and a rolled boss does not get its
    // own version of it.

    private static Vector3 Jitter(float death, float mag)
    {
        if (death <= 0f) return Vector3.Zero;
        var r = Random.Shared;
        float a = death * mag;
        return new Vector3(
            (r.NextSingle() * 2f - 1f) * a,
            (r.NextSingle() * 2f - 1f) * a,
            (r.NextSingle() * 2f - 1f) * a);
    }

    private static bool Flicker(float death)
        => death > 0f && Random.Shared.NextSingle() < death * 0.33f;

    private static Color DeathTint(Color baseColor, float death)
    {
        if (death <= 0f) return baseColor;
        if (Random.Shared.NextSingle() < death * 0.6f)
            return Random.Shared.Next(3) switch
            {
                0 => Palette.NeonRed,
                1 => Color.White,
                _ => Palette.NeonMagenta,
            };
        return baseColor;
    }

    // --- The fragment shard --------------------------------------------------------

    /// <summary>
    /// The shard rising out of a corpse. Purely ceremony — the fragment was awarded the instant
    /// the boss died — but it is the moment that connects "that thing is dead" to "there is now
    /// a tag under somebody's name", and without it the two are unrelated events.
    ///
    /// <para>A sun is a spinning wedge in the jaundiced flag-yellow; a moon is the same shape in
    /// cold chrome. Two colours the palette already holds, and the pair is legible at a glance
    /// from anywhere in the arena, which is the whole job.</para>
    /// </summary>
    public void DrawShard(World.World.FragmentShard shard, Vector3 cameraPos, Vector2 wrapShift = default)
    {
        _shardMesh ??= Meshes.CrabCoreGem(Color.White);

        float t = shard.Rise;
        float fade = 1f - MathF.Max(0f, (shard.Age - FragmentShardHold) / 1.6f);
        if (fade <= 0f) return;

        Color col = shard.Kind == Fragment.Sun ? Palette.Flag : Palette.HudChrome;
        col = GridRenderer.LerpColor(col, Color.White,
            0.35f + 0.35f * MathF.Sin(shard.Age * 6f));

        Vector2 at = shard.Position + wrapShift;
        float y = 2f + t * 9f;

        _shardMesh.Draw(at, shard.Age * 2.4f, y, cameraPos, 1.1f + 0.4f * t, col);
        // A halo, so a shard reads as light rather than as a small rock hanging in the air.
        Raylib.DrawSphereEx(new Vector3(at.X, y + 1.2f, at.Y), 1.4f + 0.5f * t, 8, 8,
            new Color(col.R, col.G, col.B, (int)(70 * fade)));
    }

    /// <summary>How long the shard hangs at full before it starts fading out.</summary>
    private const float FragmentShardHold = 3.6f;

    private PolyMesh? _shardMesh;
}
