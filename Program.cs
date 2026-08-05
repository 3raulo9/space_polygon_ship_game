using Raylib_cs;
using Unrendered.Core;

// Entry point: bootstrap the window + loop. Everything 3D renders to a small
// internal target and is upscaled nearest-neighbor inside the Renderer.

// Headless combat self-test — no window. Verifies the sim without a display.
if (args.Contains("--selftest"))
    return SelfTest.Run();

// Is multiplayer going to work on this machine? Answers without opening a window, so it
// can be run on a friend's PC before working out why the game won't connect.
if (args.Contains("--steamcheck"))
{
    bool ok = Unrendered.Net.SteamNet.Start();
    Console.WriteLine(ok
        ? $"STEAM OK — your join code is {Unrendered.Net.SteamNet.LocalCode}"
        : $"STEAM UNAVAILABLE — {Unrendered.Net.SteamNet.Trouble}");
    if (ok) Unrendered.Net.SteamNet.Stop();
    return ok ? 0 : 1;
}

// Did the soundtrack find my files? Opens the audio device but no window, rolls the
// rotation on a collapsed clock and prints what it picks, so a .wav dropped into
// Assets/Audio/Music can be confirmed in seconds rather than by playing for an hour and
// hoping. Each piece is heard for a moment and then skipped to its end.
if (args.Contains("--musiccheck"))
    return MusicCheck.Run();

// Does the world SOUND right? Drives the mixer offline against the real city and writes a
// scripted scene to a .wav you can just play: a shot walking past, a firefight receding,
// stepping behind a tower, a rotor circling, a blast in your face, being swallowed. The
// self-test proves the numbers; this is how the numbers get judged. No device, no window.
if (args.Contains("--audioscene"))
{
    int at = Array.IndexOf(args, "--audioscene");
    string path = at + 1 < args.Length && !args[at + 1].StartsWith("--") ? args[at + 1] : "scene.wav";
    return SelfTest.RenderScene(path);
}

// A capture run still needs a real GL context — it draws the actual frame and screenshots it —
// but it has no business putting a window on somebody's desktop. HiddenWindow keeps the context
// and the framebuffer and never maps the window, so the harness can grab frames while whoever is
// at the keyboard carries on with what they were doing. Only ever set when UNRENDERED_CAPTURE is,
// so a played game is unaffected.
//
// UNRENDERED_CAPTURE_SHOW=1 puts the window back, for the rare case of actually wanting to watch
// a scripted capture play out.
bool capturing = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UNRENDERED_CAPTURE"))
    && Environment.GetEnvironmentVariable("UNRENDERED_CAPTURE_SHOW") != "1";
Raylib.SetConfigFlags(capturing
    ? ConfigFlags.VSyncHint | ConfigFlags.HiddenWindow
    : ConfigFlags.VSyncHint);
Raylib.InitWindow(Config.WindowWidth, Config.WindowHeight, "UNRENDERED");
Raylib.SetExitKey(KeyboardKey.Null); // Escape is handled in the loop, not by Raylib
Raylib.SetTargetFPS(60);

// Sound bank — opens the audio device and loads the SFX. The self-test above
// returns before this, so headless runs never touch audio.
Audio.Init();

using (var game = new Game())
{
    game.Run();
}

Audio.Shutdown();
Raylib.CloseWindow();
return 0;
