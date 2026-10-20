using System.Numerics;
using System.Text.Json.Nodes;
using Raylib_cs;
using Unrendered.Core;
using Unrendered.Entities;
using Unrendered.Rendering;

namespace UnrenderedPortal;

/// <summary>
/// UNRENDERED's items as UNRENDERED models them, for Minecraft to draw: every kind's own
/// polygons with their own colours, framed the way the pack's icons frame them, plus the
/// thrown CRAB CORE's slug and the colours and numbers of its blast. The Fabric mod ships
/// this file (assets/unrendered_portal/unrendered/items.json), so a crab core in a Minecraft
/// inventory is the very gem UNRENDERED's pack shows, shaded the way UNRENDERED shades it.
///
/// <para>Written by <c>RIFT_EXPORT=&lt;file&gt;</c>; PortalMods/tools/export-items.sh runs it
/// against the game the mods are built for.</para>
/// </summary>
public static class ItemModels
{
    public static JsonObject Build()
    {
        var items = new JsonObject();
        foreach (ItemKind kind in Enum.GetValues<ItemKind>())
        {
            if (ItemIconRenderer.Model(kind) is not { } m) continue;
            var o = Mesh(m.Mesh);
            o["scale"] = m.Scale;
            o["centerY"] = m.CenterY;
            o["name"] = ItemNames.Of(kind);
            o["stack"] = Inventory.MaxStack(kind);
            items[Items.Id(kind)] = o;
        }

        var light = Atmosphere.LightDir;
        // Thrown the way the game throws one, and measured: the slug's speed, life and height.
        var slug = new Projectile();
        slug.FireCrabBomb(Vector2.Zero, Vector2.UnitY, Projectile.NoOwner);
        return new JsonObject
        {
            ["light"] = new JsonArray(light.X, light.Y, light.Z),
            ["items"] = items,
            // Projectile.FireCrabBomb and the renderer's grenade: what a thrown core is in flight.
            ["slug"] = Mesh(Meshes.Grenade(Palette.EliteFill)),
            ["throw"] = new JsonObject
            {
                ["speed"] = slug.Velocity.Length(), ["life"] = slug.Life, ["height"] = slug.Height,
            },
            ["blast"] = new JsonObject
            {
                ["life"] = CrabCoreBlast.Life, ["beams"] = CrabCoreBlast.BeamCount,
                ["length"] = CrabCoreBlast.MaxBeamLength, ["coreHeight"] = CrabCoreBlast.CoreHeight,
                ["radius"] = CrabCoreBlast.DamageRadius, ["tick"] = 0.35f, ["damage"] = 9f,
                ["red"] = Rgba(Palette.NeonRed), ["magenta"] = Rgba(Palette.NeonMagenta),
            },
        };
    }

    private static JsonObject Mesh(PolyMesh mesh)
    {
        var v = new JsonArray();
        var c = new JsonArray();
        foreach (var (a, b, cc, col) in mesh.Triangles)
        {
            foreach (var p in new[] { a, b, cc }) { v.Add(Round(p.X)); v.Add(Round(p.Y)); v.Add(Round(p.Z)); }
            c.Add(Rgba(col));
        }
        return new JsonObject { ["v"] = v, ["c"] = c };
    }

    private static float Round(float f) => MathF.Round(f, 4);

    private static int Rgba(Color c) => c.R << 24 | c.G << 16 | c.B << 8 | c.A;

    /// <summary>RIFT_EXPORT: writes the file and says so in the log. RIFT_EXPORT_SOUNDS: a
    /// folder, into which go the throw and the blast as UNRENDERED synthesises them, as .wav.</summary>
    internal static void ExportIfAsked()
    {
        string? path = Environment.GetEnvironmentVariable("RIFT_EXPORT");
        if (!string.IsNullOrEmpty(path))
            try
            {
                File.WriteAllText(path, Build().ToJsonString());
                Rift.Log($"item models written to {path}");
            }
            catch (Exception e) { Rift.Log("item models: " + e.Message); }

        string? sounds = Environment.GetEnvironmentVariable("RIFT_EXPORT_SOUNDS");
        if (!string.IsNullOrEmpty(sounds))
            try
            {
                Directory.CreateDirectory(sounds);
                // The same recipes Audio plays for these cues, mixed at the same gains.
                var rng = new Random(61);
                Wav(Path.Combine(sounds, "crab_core_blast.wav"),
                    (SfxSynth.BeamAngelic(rng), 0.7f), (SfxSynth.BeamChoir(rng), 0.6f),
                    (SfxSynth.CrabBlastGrind(rng), 1f), (SfxSynth.CrabBlastMetal(rng), 1f),
                    (SfxSynth.CreepyPowerUp(rng, 0), 1f));
                Wav(Path.Combine(sounds, "crab_core_throw.wav"), (SfxSynth.ThrowWhoosh(rng), 1f));
                Rift.Log($"sounds written to {sounds}");
            }
            catch (Exception e) { Rift.Log("sounds: " + e.Message); }
    }

    private static void Wav(string path, params (SfxSynth.Params Recipe, float Gain)[] layers)
    {
        var tracks = layers.Select(l => (Samples: SfxSynth.Render(l.Recipe), l.Gain)).ToList();
        var mix = new float[tracks.Max(t => t.Samples.Length)];
        foreach (var (samples, gain) in tracks)
            for (int i = 0; i < samples.Length; i++) mix[i] += samples[i] * gain;
        float peak = mix.Max(MathF.Abs);
        if (peak > 1f) for (int i = 0; i < mix.Length; i++) mix[i] /= peak;
        using var f = new BinaryWriter(File.Create(path));
        f.Write("RIFF"u8); f.Write(36 + mix.Length * 2); f.Write("WAVEfmt "u8);
        f.Write(16); f.Write((short)1); f.Write((short)1); f.Write(SfxSynth.SampleRate); f.Write(SfxSynth.SampleRate * 2);
        f.Write((short)2); f.Write((short)16); f.Write("data"u8); f.Write(mix.Length * 2);
        foreach (float x in mix) f.Write((short)(Math.Clamp(x, -1f, 1f) * 32767f));
    }
}
