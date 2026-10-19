namespace Unrendered.Modding;

/// <summary>
/// A code mod's handle on the game, given to it once in <see cref="IGameMod.Load"/>. One per
/// mod: what it says is said in its name, and the folder it is given is its own.
/// </summary>
public sealed class ModHost
{
    internal ModHost(string id, string file)
    {
        Id = id;
        File = file;
    }

    /// <summary>The mod's id, as it gave it.</summary>
    public string Id { get; }

    /// <summary>The .dll it was loaded from, e.g. "glowstone-rift.dll".</summary>
    public string File { get; }

    /// <summary>
    /// A folder for the mod's own files — settings, saves, caches — at Mods/&lt;id&gt;/,
    /// created on first ask. Beside the mod rather than in some system folder, so deleting a
    /// mod's file and its folder removes every trace of it.
    /// </summary>
    public string DataFolder
    {
        get
        {
            string dir = Path.Combine(Mods.Folder, Id);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>The world on screen, or null on a menu.</summary>
    public World.World? World => CodeMods.CurrentWorld;

    /// <summary>A line in the feed, in the yellow of a notice — something that happened.</summary>
    public void Notice(string text) => CodeMods.Post?.Invoke(text.ToUpperInvariant(), Net.NoticeFeed.Kind.Notice);

    /// <summary>A line in the feed, in the teal of the console — an answer, a status.</summary>
    public void Say(string text) => CodeMods.Post?.Invoke(text.ToUpperInvariant(), Net.NoticeFeed.Kind.Console);

    /// <summary>A line in the game's log, tagged with the mod's id. Never on screen.</summary>
    public void Log(string text) => System.Console.WriteLine($"[{Id}] {text}");

    /// <summary>
    /// True while the mod has an overlay of its own up that the mouse drives. The pointer is
    /// let go (draw one with <see cref="ModText.Pointer"/>) and this machine's combat triggers
    /// are held back, exactly as while the pack is open — so a click meant for the overlay
    /// does not also fire the gun. Movement carries on.
    /// </summary>
    public bool HoldsMouse { get; set; }
}
