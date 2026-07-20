using System.Numerics;
using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.Entities;

namespace VoidTanks.Rendering;

/// <summary>
/// Draws the Maw-Core from its posed parts. Like the Crab-Core it is a rig rather
/// than a baked mesh, and deliberately built out of the crab's <em>own</em> parts:
/// the carapace lid and the neon gem are the same two meshes the Stalker wears,
/// placed with nothing underneath them. Only the bottom half is new — the jaw, two
/// counter-rotating rings of teeth, and the black stuff running off them.
///
/// The counter-rotation is the whole visual idea and it is worth being explicit
/// about. One ring of teeth turning is a machine part. Two rings turning against
/// each other, at rates that do not divide evenly, never repeat a configuration the
/// eye can lock onto — so the mouth reads as working rather than as spinning, and
/// there is no frame at which it looks like it has stopped.
/// </summary>
public sealed class MawRenderer
{
    // Inherited from the Crab-Core, on purpose: same species, same mould.
    private readonly PolyMesh _shell = Meshes.CrabBodyUpper(Palette.MawShell);
    private readonly PolyMesh _crystal = Meshes.CrabCoreGem(Color.White); // tinted per frame

    private readonly PolyMesh _jaw = Meshes.MawJaw(Palette.MawShell);
    private readonly PolyMesh _tooth = Meshes.MawTooth(Palette.MawTooth);
    private readonly PolyMesh _ichor = Meshes.Ichor(Palette.MawIchor);
    private readonly PolyMesh _laser = Meshes.Bolt(Palette.MawLaser);

    public float Scale = MawRig.Scale;

    /// <summary>
    /// Draws the whole rig at a body height. <paramref name="death"/> ramps 0→1 as it
    /// comes apart, tearing the shell off the jaw and flinging the teeth outward —
    /// the same glitch language the Crab-Core dies in, because they are the same
    /// machine failing the same way.
    /// </summary>
    public void Draw(MawPose pose, Vector2 position, float bodyY, Vector3 cameraPos,
        float death = 0f)
    {
        float fling = death * death;   // ease-in: a beat of shudder, then it lets go

        // The shell, hanging with nothing under it. On death it blows upward and off.
        if (!Flicker(death))
        {
            Vector3 j = Jitter(death, 1.5f) + new Vector3(0f, fling * 10f, 0f);
            _shell.Draw(new Vector2(position.X + j.X, position.Y + j.Z), fling * 5f,
                bodyY + j.Y, cameraPos, Scale, DeathTint(Palette.MawShell, death));
        }

        // The jaw, slung under it. The gape rides the pose: it drops away from the
        // shell as the mouth opens and pulls up flush as it clamps around something,
        // so a closed jaw visibly has the shell sitting on it.
        float gape = (1f - pose.JawOpen) * 0.35f;
        if (!Flicker(death))
        {
            Vector3 j = Jitter(death, 1.3f) + new Vector3(0f, -fling * 5f, 0f);
            _jaw.Draw(new Vector2(position.X + j.X, position.Y + j.Z), -fling * 3f,
                bodyY + gape * Scale + j.Y, cameraPos, Scale,
                DeathTint(Palette.MawShell, death));
        }

        // The crystal in the well — the one thing on the model worth shooting, and the
        // only bright thing on it. Placed at exactly the height the entity hit-tests
        // against, so what the player aims at is what the sim checks.
        if (!Flicker(death))
        {
            Vector3 j = Jitter(death, 2.2f) + new Vector3(0f, fling * 7f, 0f);
            Color tint = death > 0f ? DeathTint(Palette.NeonRed, death) : pose.CrystalColor;
            _crystal.Draw(new Vector2(position.X + j.X, position.Y + j.Z),
                pose.CrystalSpin + fling * 18f,
                bodyY + MawRig.CrystalLocalY * Scale + j.Y, cameraPos, Scale, tint);
        }

        DrawTeeth(pose, position, bodyY, cameraPos, death, fling);
    }

    /// <summary>
    /// The two rings of teeth. The outer ring stands where the jaw's lip is and turns
    /// one way; the inner ring sits a little higher and tighter and turns the other,
    /// which is what makes the mouth grind instead of merely revolve.
    ///
    /// Each tooth is also given a slow cant of its own that rides its bearing round
    /// the ring, so a tooth passing the near side of the mouth leans differently from
    /// the one opposite it. Without that the ring reads as a rigid cog; with it the
    /// teeth read as sockets working loose in something soft.
    /// </summary>
    private void DrawTeeth(MawPose pose, Vector2 position, float bodyY, Vector3 cameraPos,
        float death, float fling)
    {
        // A clamped jaw draws its teeth in tighter — that closing is what a player
        // being swallowed sees coming down around them.
        float squeeze = 0.72f + 0.28f * pose.JawOpen;

        for (int ring = 0; ring < 2; ring++)
        {
            bool inner = ring == 1;
            float spin = inner ? pose.ToothSpinInner : pose.ToothSpin;
            float radius = (inner ? MawRig.ToothRadius * 0.62f : MawRig.ToothRadius) * squeeze;
            float lift = inner ? 0.55f : 0f;   // the inner ring sits up the throat

            for (int i = 0; i < MawRig.ToothCount; i++)
            {
                if (Flicker(death)) continue;

                float a = MawRig.ToothAngle(i, spin);
                var local = new Vector3(MathF.Cos(a) * radius,
                                        MawRig.ToothLocalY + lift, MathF.Sin(a) * radius);
                Vector3 o = local * Scale;

                Vector3 offset = Jitter(death, 1.6f);
                if (death > 0f)
                {
                    // Teeth are the lightest parts and go first, thrown outward off the
                    // ring they were turning on.
                    offset += new Vector3(MathF.Cos(a) * fling * 14f, -fling * 4f,
                                          MathF.Sin(a) * fling * 14f);
                }

                // A tooth points at the throat's centre: its own yaw is its bearing,
                // turned a quarter so the mesh's inward hook faces the middle.
                float yaw = -a + MathF.PI * 0.5f;

                _tooth.Draw(
                    new Vector2(position.X + o.X + offset.X, position.Y + o.Z + offset.Z),
                    yaw + fling * 8f, bodyY + o.Y + offset.Y, cameraPos, Scale,
                    DeathTint(Palette.MawTooth, death));
            }
        }
    }

    /// <summary>
    /// The black drool: beads running off the teeth and falling to the grid, each
    /// stretching as it accelerates. The stretch is done by drawing the bead at a
    /// growing scale rather than by deforming the mesh — at this resolution a
    /// lengthening dark blob and a genuinely stretched droplet are the same handful
    /// of pixels, and one of them costs nothing.
    /// </summary>
    public void DrawDrips(MawCore maw, Vector3 cameraPos, Vector2 wrapShift = default)
    {
        foreach (var d in maw.Drips)
        {
            if (!d.Active) continue;
            // Older beads are further into their fall and therefore longer and thinner.
            float stretch = 0.75f + (1f - d.LifeFrac) * 0.9f;
            // Shifted by the same wrap offset the body was, so the drool stays under the
            // re-imaged mouth when it is being drawn across the world's seam.
            var xz = new Vector2(d.Position.X + wrapShift.X, d.Position.Z + wrapShift.Y);
            _ichor.Draw(xz, d.Spin * d.LifeFrac, d.Position.Y, cameraPos, stretch,
                Palette.MawIchor);
        }
    }

    /// <summary>
    /// The little lasers, drawn as hot acid points with a soft halo. The halo is a
    /// plain sphere rather than a mesh for the same reason the Crab-Core's beam is:
    /// light is the one thing out here that is not built out of polygons, so anything
    /// glowing should visibly break the model's own rules.
    /// </summary>
    public void DrawLasers(MawCore maw, Vector3 cameraPos, Vector2 wrapShift = default)
    {
        foreach (var l in maw.Lasers)
        {
            if (!l.Active) continue;
            // Re-imaged by the same wrap offset as the mouth that spat them.
            Vector3 pos = l.Position + new Vector3(wrapShift.X, 0f, wrapShift.Y);
            var xz = new Vector2(pos.X, pos.Z);
            // Both kept small on purpose. These are meant to read as spat sparks, not
            // as cannon rounds: a halo wide enough to be impressive also reads as a
            // projectile worth respecting, and the whole design of the lasers is that
            // they are a nuisance keeping you moving while the mouth does the killing.
            Raylib.DrawSphereEx(pos, 0.22f, 6, 6,
                new Color(Palette.MawLaser.R, Palette.MawLaser.G, Palette.MawLaser.B, (byte)70));
            _laser.Draw(xz, 0f, pos.Y, cameraPos, 0.65f, Palette.MawLaser);
        }
    }

    // --- Death-glitch helpers -------------------------------------------------
    // All no-ops at death<=0, so a living monster draws exactly as it always does.
    // Fresh randomness each call, so the tearing shifts every frame.

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

    /// <summary>True on frames a part should blink out entirely — the hard on/off
    /// stutter of a corrupted signal.</summary>
    private static bool Flicker(float death)
        => death > 0f && Random.Shared.NextSingle() < death * 0.33f;

    /// <summary>Slams a part's colour between hot glitch tones as it tears apart.</summary>
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
}
