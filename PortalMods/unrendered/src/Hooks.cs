using System.Reflection;
using System.Runtime.Loader;

namespace UnrenderedPortal;

/// <summary>
/// The one place the rift reaches past the mod API: the moment the game puts its finished
/// frame on the screen. Minecraft has to be drawn at the window's own resolution — exactly as
/// Minecraft draws it — with UNRENDERED's frame laid over it, and the API's draw hooks all
/// paint into the game's 320×240 target. So the present is wrapped at runtime (Harmony,
/// carried inside this dll); the game's code is not changed, and when there is nothing of
/// Minecraft's on screen the game presents exactly as it always does.
/// </summary>
public static class Hooks
{
    private static bool _resolver;

    public static void Install()
    {
        if (!_resolver)
        {
            _resolver = true;
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                if (name.Name != "0Harmony") return null;
                using var s = typeof(Hooks).Assembly.GetManifestResourceStream("0Harmony.dll");
                return s == null ? null : ctx.LoadFromStream(s);
            };
        }
        try { Apply(); }
        catch (Exception e) { Rift.Log("hooks: could not wrap the present — Minecraft draws at the game's own resolution: " + e); }
    }

    // Kept out of Install so 0Harmony is only resolved once the resolver above is in place.
    private static void Apply()
    {
        var h = new HarmonyLib.Harmony("unrendered.rift");
        MethodInfo? present = typeof(Unrendered.Rendering.Renderer).GetMethod(nameof(Unrendered.Rendering.Renderer.Present));
        if (present == null) { Rift.Log("hooks: Renderer.Present not found"); return; }
        h.Patch(present, prefix: new HarmonyLib.HarmonyMethod(typeof(Hooks), nameof(PresentPrefix)));
        Composite.Enabled = true;
        Rift.Log("hooks: present wrapped");
    }

    private static bool PresentPrefix(Unrendered.Rendering.Renderer __instance)
    {
        try { return !Composite.Present(__instance); }
        catch (Exception e)
        {
            Rift.Log("present: " + e);
            Composite.Enabled = false;   // never take the screen down with us
            return true;
        }
    }
}
