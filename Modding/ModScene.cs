using System.Numerics;
using Raylib_cs;
using Unrendered.Core;

namespace Unrendered.Modding;

/// <summary>
/// One radar contact a code mod adds. <see cref="Offset"/> is in world units from the craft
/// whose instruments are up, in that craft's own frame: X to its right, Y straight ahead —
/// the mod knows which way its own camera faces, and the radar draws "ahead" as up.
/// </summary>
public readonly record struct RadarBlip(Vector2 Offset, Color Color, int Size = 2);

/// <summary>
/// The frame about to be drawn, as far as code mods are concerned. Handed to every mod's
/// <see cref="IGameMod.BeforeDraw"/> and then on to the draw hooks, rebuilt fresh each frame
/// — a mod that wants a change to last makes it every frame.
/// </summary>
public sealed class ModScene
{
    /// <summary>The camera the frame will be drawn with. Built from the player's eye before
    /// any mod sees it; move it in <see cref="IGameMod.BeforeDraw"/>.</summary>
    public Camera3D Camera;

    /// <summary>
    /// True to have the game draw none of its own world — no sky, no floor, no buildings, no
    /// bodies — so that the mod can put a different place around the camera in
    /// <see cref="IGameMod.Draw3D"/>. The instruments, the feed and the screen effects are
    /// still the game's.
    /// </summary>
    public bool OwnsWorld;

    /// <summary>The colour the frame is cleared to before anything is drawn.</summary>
    public Color Clear;

    /// <summary>False to leave the field's own contacts off the radar — the hunters, the
    /// squads, the bosses — when the place on screen is not the field.</summary>
    public bool WorldContacts;

    /// <summary>Contacts of the mod's own, drawn on the radar over the field's.</summary>
    public readonly List<RadarBlip> Blips = new();

    /// <summary>Where the eye is this frame.</summary>
    public Vector3 Eye => Camera.Position;

    internal void Reset(Camera3D camera)
    {
        Camera = camera;
        OwnsWorld = false;
        Clear = Palette.Void;
        WorldContacts = true;
        Blips.Clear();
    }
}
