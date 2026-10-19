using Unrendered.Core;

namespace Unrendered.Modding;

/// <summary>
/// A code mod: a <c>.dll</c> in the Mods folder holding a public class that implements this,
/// with a constructor that takes nothing. One file, like a <c>.json</c> mod — dropped in beside
/// them, listed on the same MODS page, folded into the same multiplayer fingerprint.
///
/// <para>Where a <c>.json</c> mod is data the game knows how to read, a code mod is code the
/// game calls: at boot, when a world opens and closes, every step of the sim, every frame
/// of input, and three times while a frame is drawn. It may read anything public and change
/// anything public — that is the bargain of code, and it is why the join check refuses a
/// guest whose code mods are not byte-for-byte the host's.</para>
///
/// <para>Every hook but <see cref="Load"/> has an empty default, so a mod implements only the
/// ones it needs. A hook that throws is logged and skipped, never fatal; one that keeps
/// throwing is switched off for the rest of the session, and the feed says so.</para>
///
/// <para>Compile against <c>Unrendered.dll</c> and <c>Raylib-cs.dll</c> from the game's own
/// folder and do not ship copies of either — the game's are already loaded, and a mod that
/// brought its own would be talking to a different game.</para>
/// </summary>
public interface IGameMod
{
    /// <summary>a-z 0-9 _ - and unique across everything installed, .json mods included.</summary>
    string Id { get; }

    /// <summary>What the MODS page calls it.</summary>
    string Name { get; }

    string Version { get; }
    string Author => "";
    string Description => "";

    /// <summary>Once, at boot, before any world exists. Keep the handle: it is how the mod
    /// talks back to the game.</summary>
    void Load(ModHost host);

    /// <summary>A world came into being: a run opened, a crossing landed on the next planet, a
    /// match was joined. Called before that world's first step.</summary>
    void WorldOpened(World.World world) { }

    /// <summary>The world the game was running is gone — back to a menu, or replaced by the
    /// next planet's. Anything the mod put into it went with it.</summary>
    void WorldClosed(World.World world) { }

    /// <summary>
    /// Every fixed step of the sim (sixty a second), straight after the world's own. The
    /// <paramref name="input"/> is this machine's frame for the step — the one the local
    /// craft just drove on — and is empty while the chat is open, with its combat bits
    /// cleared while a panel has the mouse.
    /// </summary>
    void Step(World.World world, float dt, in InputFrame input) { }

    /// <summary>
    /// Once per rendered frame of live play, while the keyboard is the game's — no chat, no
    /// panel, no pause. The place to read keys of the mod's own with Raylib: an edge read here
    /// is read once, where a fixed step might see the same press twice or not at all.
    /// </summary>
    void Frame(World.World world, float dt) { }

    /// <summary>
    /// Before the world is drawn. The camera in <paramref name="scene"/> is already built
    /// from the player's eye and may be moved; the scene may be claimed outright
    /// (<see cref="ModScene.OwnsWorld"/>). Any off-screen texture pass the mod needs goes here
    /// — passes cannot nest, and once the frame's own pass is open it is too late.
    /// </summary>
    void BeforeDraw(World.World world, ModScene scene) { }

    /// <summary>Inside the frame's 3D pass, with its camera, after everything the game drew.</summary>
    void Draw3D(World.World world, ModScene scene) { }

    /// <summary>Over the finished picture, in the 320×240 target, after the instruments.
    /// <see cref="ModText"/> writes in the game's own lettering.</summary>
    void DrawHud(World.World world, ModScene scene) { }

    /// <summary>
    /// A chat line in braces the game itself does not know — <c>{rift}</c>, say. Return the
    /// reply for the console, or null when the line is not this mod's. Solo only: in a match
    /// the host's session reads commands, and it does not ask mods.
    /// </summary>
    string? Command(World.World? world, string line) => null;
}
