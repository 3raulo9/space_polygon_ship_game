namespace VoidTanks.Core;

/// <summary>
/// Global tunables. The resolution numbers here are load-bearing for the look
/// (Doc 02): render the 3D world to a tiny target, upscale nearest-neighbor.
/// </summary>
public static class Config
{
    // --- Internal render resolution (the chunky-pixel source) ---
    // ~320x240 is the target. Everything 3D is drawn here, then upscaled.
    public const int InternalWidth = 320;
    public const int InternalHeight = 240;

    // --- Window (integer-multiple upscale of the internal target) ---
    // 320x240 * 3 = 960x720. Letterbox any remainder; never stretch/warp.
    public const int WindowScale = 3;
    public const int WindowWidth = InternalWidth * WindowScale;   // 960
    public const int WindowHeight = InternalHeight * WindowScale; // 720

    // --- Simulation ---
    // Fixed timestep so movement/collision are deterministic (Doc 05).
    public const double FixedDt = 1.0 / 60.0;

    // --- Fog / draw distance (Doc 02) ---
    // Geometry fades into the fog colour over a short distance; things pop in
    // at the boundary. Short draw distance = more dread, cheaper to render.
    public const float FogStart = 24f;
    public const float FogEnd = 130f;

    // --- Camera ---
    // Horizon sits slightly low so the player feels small under a vast dark
    // ceiling of nothing (Doc 02).
    public const float CameraFovY = 62f;
    public const float CameraHeight = 3.2f; // eye height above the grid plane

    /// <summary>
    /// The standing upward tilt of the eye, as a slope on the look direction — this is
    /// what drops the horizon and opens the sky band above it. Anything that wants to
    /// aim the camera at a specific thing has to work <em>relative</em> to this rather
    /// than from level, or it ends up pointing a good fifteen degrees over the top of
    /// whatever it meant to frame.
    /// </summary>
    public const float CameraLookLift = 0.15f;
}
