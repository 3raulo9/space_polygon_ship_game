using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace UnrenderedPortal;

/// <summary>rlgl's primitive modes, as the draw list carries them.</summary>
public static class Draw
{
    public const int Lines = 1, Triangles = 4, Quads = 7;
}

/// <summary>
/// Captures what UNRENDERED actually draws. The game renders in raylib's immediate mode, so
/// every triangle, line and quad it emits lands in rlgl's render batch in world space, with
/// its shaded and fogged colour already worked out. For a capture, the batch is swapped for
/// a private one large enough never to flush, the game's own drawing code is run, and the
/// vertices are read straight back out — the picture as the game made it, not a description
/// of it.
///
/// The private batch is then submitted into a throwaway 4×4 target, which is the only way
/// rlgl will let go of it; nothing reaches the screen.
/// </summary>
public static unsafe class Capture
{
    private static RenderBatch* _batch;
    private static RenderTexture2D _sink;

    /// <summary>True while a capture is running, so the mod's own extra drawing (the
    /// portal) can stay out of a picture Minecraft draws its own version of.</summary>
    public static bool Active { get; private set; }

    /// <summary>Quads the private batch can hold; well past the busiest frame the game draws.</summary>
    private const int Elements = 1 << 17;

    /// <summary>Runs <paramref name="draw"/> and returns its geometry as a draw list
    /// (see PROTOCOL.md): u32 draws, then per draw u8 mode, u32 verts, verts × (f32 xyz, u8 rgba).</summary>
    public static void DrawList(Action draw, BinaryWriter into)
    {
        if (_batch == null)
        {
            _batch = (RenderBatch*)NativeMemory.AllocZeroed((nuint)sizeof(RenderBatch));
            *_batch = Rlgl.LoadRenderBatch(1, Elements);
            _sink = Raylib.LoadRenderTexture(4, 4);
        }

        Rlgl.DrawRenderBatchActive();
        Raylib.BeginTextureMode(_sink);
        Rlgl.SetRenderBatchActive(_batch);
        Active = true;
        try { draw(); }
        catch (Exception e) { Rift.Log("capture: " + e); }
        finally { Active = false; }

        Write(into);

        // Hands the batch back — rlgl draws it on the way out, into the 4x4 sink.
        Rlgl.SetRenderBatchActive(null);
        Raylib.EndTextureMode();
    }

    private static void Write(BinaryWriter w)
    {
        RenderBatch* b = _batch;
        VertexBuffer* vb = b->VertexBuffer;
        float* pos = vb->Vertices;
        byte* col = vb->Colors;
        int draws = b->DrawCounter;

        long countAt = w.BaseStream.Position;
        w.Write(0u);
        uint written = 0;
        int offset = 0;
        for (int i = 0; i < draws; i++)
        {
            DrawCall d = b->Draws[i];
            int n = d.VertexCount;
            if (n > 0 && ((int)d.Mode == Draw.Lines || (int)d.Mode == Draw.Triangles || (int)d.Mode == Draw.Quads))
            {
                w.Write((byte)d.Mode);
                w.Write((uint)n);
                for (int v = offset; v < offset + n; v++)
                {
                    w.Write(pos[v * 3]); w.Write(pos[v * 3 + 1]); w.Write(pos[v * 3 + 2]);
                    w.Write(col[v * 4]); w.Write(col[v * 4 + 1]); w.Write(col[v * 4 + 2]); w.Write(col[v * 4 + 3]);
                }
                written++;
            }
            offset += n + d.VertexAlignment;
        }
        long end = w.BaseStream.Position;
        w.BaseStream.Position = countAt;
        w.Write(written);
        w.BaseStream.Position = end;
    }

    /// <summary>How many vertices the last capture held — for the log and the self-test.</summary>
    public static int LastVertices
    {
        get
        {
            if (_batch == null) return 0;
            int n = 0;
            for (int i = 0; i < _batch->DrawCounter; i++) n += _batch->Draws[i].VertexCount;
            return n;
        }
    }
}
