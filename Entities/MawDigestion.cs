using System.Numerics;
using VoidTanks.Core;

namespace VoidTanks.Entities;

/// <summary>
/// The Maw-Core's set piece: what happens when the hanging mouth comes down on a
/// player who stood still too long. It closes over them, hauls them up into its
/// throat, and starts grinding — and unlike the Crab-Core's seizure, which is a
/// scripted length you sit through, this one does not end on its own. It ends when
/// the player shoots their way out of it.
///
/// That is the whole design. The Crab-Core's execution is a cutscene: control is
/// taken, a fixed sequence plays, you are thrown clear. This is a <em>trap</em>:
/// control over movement is taken but the trigger is left live, the camera is
/// wrenched up the throat so the only thing in frame is the thing eating you, and it
/// bites off <see cref="BiteFraction"/> of your shield every
/// <see cref="BiteInterval"/> seconds until you land <see cref="EscapeHits"/> shots
/// into the roof of its mouth. A player who freezes dies in it.
///
/// Built the same way <see cref="CrabSeizure"/> is, and for the same reasons: pure
/// simulation, no Raylib, no wall-clock, no randomness, so the headless self-test can
/// run the whole trap end to end. It sets <see cref="PlayerTank.Captured"/> and
/// writes the craft's transform directly, and the camera follows from that.
/// </summary>
public sealed class MawDigestion : ICinematicView
{
    /// <summary>The beats, in order.</summary>
    public enum Stage
    {
        Swallow,  // the throat closes and drags the craft up off the grid
        Digest,   // held in the gullet, being ground — the part with no timer
        Spit,     // three hits landed: the jaw springs and it throws them out
        Fall,     // dropped, coming back down to the grid
        Recover,  // grounded and driving again, still ringing
        Done,
    }

    /// <summary>What happened this tick that the world has to act on. Damage is
    /// reported rather than applied, because the world owns what a hit to the player
    /// means — the shield, the lives, the low-health alarm.</summary>
    public enum Event { None, Bitten, Landed }

    // --- Trigger ---------------------------------------------------------------

    /// <summary>
    /// True when this monster is in a position to swallow this player. The entity's
    /// own lunge does the work of getting there — it only sets
    /// <see cref="MawCore.JustCaught"/> on the tick its throat actually closed over
    /// somebody — so all this adds is the standing rule that a dead monster catches
    /// nothing.
    /// </summary>
    public static bool CanSwallow(MawCore maw, PlayerTank player)
        => maw.Alive && maw.JustCaught && !player.Captured;

    // --- Escape ----------------------------------------------------------------

    /// <summary>
    /// How many shots into the roof of its mouth it takes to make it let go. Three:
    /// enough that the player has to hold their nerve and keep firing while being
    /// chewed, few enough that it never becomes a fight in itself.
    /// </summary>
    public const int EscapeHits = 3;

    /// <summary>Shots landed from inside so far, 0..<see cref="EscapeHits"/>.</summary>
    public int Hits { get; private set; }

    /// <summary>
    /// Puts one shot into the inside of the throat. Called by the world when the
    /// player pulls the trigger mid-digestion — at point-blank inside a mouth there
    /// is nothing to aim at and nothing to miss, so every shot the craft can pay for
    /// counts. Returns true if this was the one that broke the hold.
    ///
    /// Only meaningful during <see cref="Stage.Digest"/>: shots fired while it is
    /// still swallowing you, or after it has already decided to spit, do nothing, so
    /// the count can never run past the escape and trigger a second release.
    /// </summary>
    public bool RegisterShot()
    {
        if (_stage != Stage.Digest) return false;

        Hits++;
        _flinch = 1f;
        Audio.PlayMawHurt((float)Hits / EscapeHits);

        if (Hits < EscapeHits) return false;
        BeginSpit();
        return true;
    }

    // --- Damage ----------------------------------------------------------------

    /// <summary>
    /// What one bite costs, as a fraction of the shield's maximum. Taken as a
    /// fraction rather than a flat number so being eaten costs the same share of your
    /// life whatever state you were caught in — and so the arithmetic the player does
    /// under pressure is simple: about six bites is all of you.
    /// </summary>
    public const float BiteFraction = 0.15f;

    /// <summary>Seconds between bites. Slow enough to get three shots off between the
    /// first and the third, tight enough that hesitating is expensive.</summary>
    public const float BiteInterval = 1.25f;

    /// <summary>What hitting the grid at the end of the fall costs, as a fraction of
    /// the maximum. Small — the drop is punctuation, not a second punishment.</summary>
    public const float LandingFraction = 0.04f;

    // --- Timings (seconds) -----------------------------------------------------

    private const float SwallowTime = 0.55f;
    private const float SpitTime = 0.35f;
    private const float RecoverTime = 1.5f;
    private const float FallGravity = 24f;

    // --- Framing ---------------------------------------------------------------

    /// <summary>
    /// How far the view is dragged upward while the player is in the throat.
    ///
    /// Solved against the camera's standing tilt rather than dialled in, for the same
    /// reason the seizure's gaze is: <see cref="Config.CameraLookLift"/> already aims
    /// the eye up, so "look up" is not a number you can guess at. This is a hard,
    /// unambiguous pitch on top of that — the horizon leaves the frame entirely and
    /// the only thing on screen is the throat closing over the top of the view, which
    /// is precisely what a player being swallowed should be able to see and nothing
    /// else.
    /// </summary>
    private const float ThroatGaze = 1.35f - Config.CameraLookLift;

    /// <summary>
    /// How far the craft is canted in the gullet. Larger than the seizure's hold: the
    /// crab carries you at arm's length in a claw, where this has you wedged sideways
    /// in a tube that is actively trying to swallow you.
    /// </summary>
    private const float GulletRoll = 0.26f;

    private readonly MawCore _maw;
    private readonly PlayerTank _player;

    private Stage _stage = Stage.Swallow;
    private float _t;        // seconds inside the current stage
    private float _clock;    // seconds since it closed — drives the trembles
    private float _biteClock;
    private float _flinch;   // 0..1 spike when a shot lands inside, decays

    private Vector2 _fromPos;
    private float _fromHeight;

    private float _fallSpeed;

    public MawDigestion(MawCore maw, PlayerTank player)
    {
        _maw = maw;
        _player = player;

        _fromPos = player.Position;
        _fromHeight = player.Height;

        player.Captured = true;
        player.ResetMomentum();

        // The first bite is due a full interval in, so the swallow itself is free —
        // the player gets a beat to understand what has happened to them before the
        // shield starts going.
        _biteClock = BiteInterval;

        Audio.PlayMawSwallow();
    }

    /// <summary>The beat currently playing.</summary>
    public Stage Phase => _stage;

    /// <summary>True until it has fully played out, recovery included.</summary>
    public bool Active => _stage != Stage.Done;

    /// <summary>True while the thing actually has the craft inside it.</summary>
    public bool Held => _stage is Stage.Swallow or Stage.Digest;

    // --- What the renderer reads (ICinematicView) -----------------------------

    public float Shake { get; private set; }
    public float Roll { get; private set; }
    public float Pitch { get; private set; }
    public float Glow { get; private set; }

    /// <summary>
    /// Advances one fixed step and writes the player's transform. Returns whichever
    /// damage moment fell on this tick, if any.
    /// </summary>
    public Event Update(float dt)
    {
        if (_stage == Stage.Done) return Event.None;

        _t += dt;
        _clock += dt;
        if (_flinch > 0f) _flinch = MathF.Max(0f, _flinch - dt * 3f);

        // A crystal destroyed while it is eating you drops you at once. Waiting out a
        // digestion inside something that is already tearing itself apart would be
        // absurd, and there is only ever one way out of the throat, so a kill mid-meal
        // takes the same exit the escape does.
        if (Held && !_maw.Alive)
        {
            BeginSpit();
            return Event.None;
        }

        Event ev = Event.None;
        switch (_stage)
        {
            case Stage.Swallow: UpdateSwallow(); break;
            case Stage.Digest: ev = UpdateDigest(dt); break;
            case Stage.Spit: UpdateSpit(); break;
            case Stage.Fall: ev = UpdateFall(dt); break;
            case Stage.Recover: UpdateRecover(); break;
        }
        return ev;
    }

    // --- Stage 1: dragged up into the throat ----------------------------------

    private void UpdateSwallow()
    {
        float f = Ease(_t / SwallowTime);

        // Hauled straight up off the grid into the gullet. No lateral drag: it came
        // down on the column the player was standing in, so up is the only direction
        // that makes sense — and a vertical yank with the camera pinned to the ceiling
        // is unmistakably being swallowed rather than being carried.
        _player.Position = Vector2.Lerp(_fromPos, _maw.Position, f);
        _player.Height = Lerp(_fromHeight, GulletHeight, f);

        // The view is wrenched up the throat across the swallow, and it happens fast:
        // the whole read of the moment is that the sky is replaced by the inside of
        // something before the player can react to it.
        Shake = 0.35f + 0.35f * f;
        Pitch = Lerp(0f, ThroatGaze, f);
        Roll = GulletRoll * f;
        Glow = 0.25f * f;

        if (_t >= SwallowTime) Enter(Stage.Digest);
    }

    // --- Stage 2: held in the gullet, being ground ----------------------------

    private Event UpdateDigest(float dt)
    {
        // Wedged in the throat, shaken by the teeth working around them. The three
        // axes run at unrelated rates so the motion never settles into a pattern, and
        // it stays a pure function of the clock so the sim is unchanged.
        _player.Position = _maw.Position + new Vector2(
            MathF.Sin(_clock * 29f) * 0.22f,
            MathF.Sin(_clock * 34f + 1.3f) * 0.22f);
        _player.Height = GulletHeight + MathF.Sin(_clock * 25f + 0.7f) * 0.3f;

        // Held hard up the throat the whole time. This is the shot the set piece
        // exists for and it never cuts away from it.
        Pitch = ThroatGaze;
        Roll = GulletRoll + 0.05f * MathF.Sin(_clock * 3.7f);

        // The grind: a steady judder that surges as each bite comes due, so the player
        // can feel the next one arriving before it lands. A shot landing inside spikes
        // it — the thing flinches, and that flinch is the only feedback the player gets
        // that shooting is working.
        float toBite = 1f - Math.Clamp(_biteClock / BiteInterval, 0f, 1f);
        Shake = 0.4f + 0.25f * toBite * toBite + 0.5f * _flinch;
        Glow = 0.3f + 0.25f * toBite + 0.45f * _flinch;

        _biteClock -= dt;
        if (_biteClock > 0f) return Event.None;

        _biteClock = BiteInterval;
        Audio.PlayMawDigest();
        Shake = 1f;
        return Event.Bitten;
    }

    // --- Stage 3: it lets go --------------------------------------------------

    /// <summary>
    /// The jaw springs and the craft is thrown clear. Reached by the escape and by a
    /// crystal dying mid-meal, so there is exactly one way out of the throat and it
    /// always looks the same.
    /// </summary>
    private void BeginSpit()
    {
        _maw.ReleasePrey();
        Audio.PlayMawRelease();
        Enter(Stage.Spit);
    }

    private void UpdateSpit()
    {
        float f = Ease(_t / SpitTime);

        // Shoved down and out of the mouth. The view is dragged back off the ceiling
        // across the same beat, so the sky comes back as the player is expelled.
        _player.Height = Lerp(GulletHeight, GulletHeight - 1.2f, f);
        Pitch = Lerp(ThroatGaze, 0.25f, f);
        Roll = GulletRoll * (1f - f);
        Shake = Lerp(1f, 0.4f, f);
        Glow = 0.5f * (1f - f);

        if (_t >= SpitTime)
        {
            _fallSpeed = -3f;    // pushed downward, not merely dropped
            Enter(Stage.Fall);
        }
    }

    // --- Stage 4: back down to the grid ---------------------------------------

    private Event UpdateFall(float dt)
    {
        _fallSpeed -= FallGravity * dt;
        _player.Height += _fallSpeed * dt;

        Shake = 0.25f;
        Pitch = 0.25f - 0.5f * Math.Clamp(_t * 1.6f, 0f, 1f);   // down at the oncoming floor
        Roll = 0f;
        Glow = 0f;

        if (_player.Height > 0f) return Event.None;

        _player.Height = 0f;
        Audio.PlayCrashLanding();
        Enter(Stage.Recover);
        return Event.Landed;
    }

    // --- Stage 5: driving again, still ringing --------------------------------

    private void UpdateRecover()
    {
        // Control comes back the instant the craft touches down; the rest of this is
        // only the view settling. The monster's own recovery is what stops it simply
        // swallowing them again — it is up in its Spent phase for several seconds.
        if (_player.Captured)
        {
            _player.Captured = false;
            _player.ResetMomentum();
        }

        float f = 1f - Math.Clamp(_t / RecoverTime, 0f, 1f);

        Shake = 0.45f * f * f;
        Roll = 0.14f * f * f * MathF.Sin(_clock * 12f);
        Pitch = -0.35f * f * f;
        Glow = 0f;

        if (_t >= RecoverTime)
        {
            Shake = 0f;
            Roll = 0f;
            Pitch = 0f;
            Enter(Stage.Done);
        }
    }

    // --- Helpers --------------------------------------------------------------

    /// <summary>
    /// Where the craft rides while held. Solved backwards from where the <em>eye</em>
    /// has to end up — see <see cref="MawRig.GulletEyeLocal"/> — because the camera
    /// sits above the craft and aiming the craft at the throat puts the view inside
    /// the crystal.
    ///
    /// Recomputed every tick off the monster's live height rather than frozen, which
    /// is what lets it haul the player upward as it climbs back to its hover: the
    /// craft simply follows the body it is inside. Floored just off the grid so the
    /// early part of that climb, while the thing is still bottomed out from its lunge,
    /// never buries the player under the floor.
    /// </summary>
    private float GulletHeight
        => MathF.Max(0.2f, _maw.BodyY + MawRig.GulletEyeOffset - Config.CameraHeight);

    private void Enter(Stage next)
    {
        _stage = next;
        _t = 0f;
    }

    /// <summary>Smoothstep on a 0..1 fraction.</summary>
    private static float Ease(float f)
    {
        f = Math.Clamp(f, 0f, 1f);
        return f * f * (3f - 2f * f);
    }

    private static float Lerp(float a, float b, float f) => a + (b - a) * Math.Clamp(f, 0f, 1f);
}
