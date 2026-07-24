using System.Numerics;
using VoidTanks.Core;

namespace VoidTanks.Entities;

/// <summary>
/// The SPIDER's front limbs used as what they are on the thing this chassis was cut
/// out of: hands. The Crab-Core's whole identity is that it walks up to you, closes a
/// claw around your craft, squeezes, and throws you across the arena — and until now
/// the salvaged version could do none of it. It had the boss's gun and none of the
/// boss's <em>threat</em>. This is the other half.
///
/// Three beats on one trigger, which is the same grammar the lance already uses (hold,
/// then let go):
///
///   press with a hunter inside <see cref="Reach"/> — the claw closes and the machine
///   comes off the grid;
///   hold — it squeezes, and the body's integrity drains while it is in there;
///   release — it is hurled down the look line, and whatever it lands among wears the
///   impact along with it.
///
/// The reason this belongs on the SPIDER rather than on any other chassis is the core.
/// This is the one craft in the game whose weak point faces <em>forward</em> (see
/// <see cref="PlayerTank.ArmorMultiplierFromShot"/>), and a body held out in front of
/// that core is the only cover it will ever have. So the grab is not a damage tool —
/// crushing is deliberately slower than simply shooting the thing — it is a way to pick
/// up a piece of the field and stand behind it. What you are holding is armour that
/// screams, and it is armour you can throw.
///
/// Pure state and geometry: the world applies every point of damage and steps the
/// thrown body, so all of this runs headless.
/// </summary>
public sealed class SpiderClaw
{
    /// <summary>
    /// How far out in front the claw closes, solved off the rig rather than chosen —
    /// the point the front limb's tip reaches when it swings onto the chassis centre
    /// line, exactly as the boss's own grip is solved (see
    /// <see cref="CrabRig.CentreGripReach"/>), plus the bulk of the thing being held.
    ///
    /// The standoff on the end is not padding. A hunter is nearly seven units tall and
    /// three times the player's own width, and this is a first-person game: held at the
    /// claw itself it is not a captive, it is a wall, and the player fights the rest of
    /// the engagement blind. Out at arm's length it sits in the upper half of the frame
    /// — plainly caught, plainly between you and whatever is shooting — and the grid
    /// ahead stays visible underneath it. This is the number to turn if it ever crowds
    /// the screen.
    /// </summary>
    public static readonly float HoldReach =
        CrabRig.PlayerCentreGripReach(CrabRig.Legs[CrabRig.GrabLeg]) + EnemyTank.Radius + 4f;

    /// <summary>How high off the grid the catch is carried. Low: a machine held barely
    /// clear of the floor reads as hoisted by something that has to strain to do it,
    /// which is the right note for a chassis this size lifting a hunter. It also keeps
    /// the mass of the hull under the eye line rather than over it.</summary>
    public const float HoldHeight = 1.2f;

    /// <summary>
    /// How close a hunter has to be before the claw will take it. A shade past the hold
    /// point, so the grab has a little bite to it — a machine caught at the edge of reach
    /// is yanked in toward the chassis rather than being seized where it stood.
    /// </summary>
    public static readonly float Reach = HoldReach + 2f;

    /// <summary>
    /// Integrity a squeezing claw drains per second. Deliberately slower than simply
    /// shooting the thing — the emitter does roughly three a second at the same range —
    /// because a grab that also out-damaged the gun would make the gun pointless. What
    /// the hold is worth is the cover and the throw; the crush is the clock running on
    /// a body you are already using for something else.
    /// </summary>
    public const float CrushRate = 1.6f;

    /// <summary>
    /// What the held body loses each time it eats a round meant for the player. A flat
    /// bite of its own integrity rather than the round's damage, because the two numbers
    /// live on completely different scales in this game — an enemy shell is billed
    /// against a hundred-point shield, and a hunter has three. Passing one through raw
    /// would vaporise the catch on the first hit. At this figure a standard hunter soaks
    /// four rounds before it comes apart, which is a real shield and an honest clock.
    /// </summary>
    public const float ShieldBite = 0.75f;

    /// <summary>How wide the sheltered arc is, as a dot against the craft's forward.
    /// Matches the front plate the armour model uses, so "the core is facing this" and
    /// "the body is covering this" are the same question asked twice.</summary>
    public const float ShieldArc = 0.5f;

    /// <summary>How hard the body is thrown, and how much of that is lift. Fast and
    /// fairly flat: this is a hurled machine, not a lob, and it should arrive roughly
    /// where the crosshair was rather than somewhere over it.</summary>
    public const float ThrowSpeed = 52f;
    public const float ThrowLift = 5f;

    /// <summary>The pull on a body in flight. Heavy — it is several tons of hunter.</summary>
    public const float ThrowGravity = 26f;

    /// <summary>What the impact costs the thing that was thrown, and what it costs
    /// everything else standing where it lands. The thrown body always comes off worse,
    /// which is what makes a throw a way of spending a hostage rather than a way of
    /// farming one.</summary>
    public const float ImpactDamage = 2f;
    public const float ThrownBodyDamage = 3f;

    /// <summary>How far the landing carries. About a hunter's own width — you have to
    /// actually put it on something.</summary>
    public const float ImpactRadius = 5f;

    /// <summary>
    /// What carrying one costs in mobility. A SPIDER with a machine in its hands has two
    /// fewer legs on the grid and several tons out in front of its centre of gravity, and
    /// it should feel like it. This is most of what stops the grab from being strictly
    /// free: you are slower for as long as you are behind the shield.
    /// </summary>
    public const float CarryScale = 0.62f;

    /// <summary>What is in the claw, or null. Only ever a hunter: the claw takes
    /// machines, which is what the limb it is copied from was built to do.</summary>
    public EnemyTank? Victim { get; private set; }

    /// <summary>True while something is actually in the hand.</summary>
    public bool Holding => Victim != null;

    /// <summary>Seconds the current catch has been in the claw — what the renderer
    /// pulses the grip on and what the HUD reads for its hold readout.</summary>
    public float Squeeze { get; private set; }

    /// <summary>How many rounds the held body has eaten on the player's behalf. Reset
    /// with each catch; exists so the HUD can show the shield being used up.</summary>
    public int Soaked { get; private set; }

    /// <summary>
    /// Closes on a hunter. The caller has already decided it is close enough and that
    /// the hand is empty — this only performs the catch. Returns false rather than
    /// throwing if either turns out not to hold, so a trigger read that raced the AI
    /// (a hunter killed on the same tick the claw reached it) simply misses.
    /// </summary>
    public bool TryGrab(EnemyTank prey)
    {
        if (Holding || !prey.Alive || prey.Grabbed || prey.Flung) return false;
        Victim = prey;
        prey.Grabbed = true;
        Squeeze = 0f;
        Soaked = 0;
        return true;
    }

    /// <summary>
    /// Parks the catch in the grip for this tick and hands back what the squeeze has
    /// earned, which the world bills through its ordinary damage path so a hunter that
    /// dies in the hand is scored, salvaged and mourned exactly like one that was shot.
    ///
    /// Writing the victim's transform here is the same trick the Crab-Core's seizure
    /// plays on the player: the body stays in the world's own list and goes on being
    /// drawn by the ordinary renderer, it simply stops driving itself. Nothing new has
    /// to be drawn for a held hunter to appear hanging in front of the player's face.
    /// </summary>
    public float Hold(float dt, Vector2 gripXZ, float gripY, float facing)
    {
        if (Victim is not { } prey) return 0f;

        Squeeze += dt;
        prey.Position = Torus.Wrap(gripXZ);
        prey.Height = gripY;
        // Turned to face the same way the player is. A machine held nose-out is a shield;
        // one held sideways is a diorama.
        prey.Heading = facing;
        return CrushRate * dt;
    }

    /// <summary>
    /// True when a round travelling along <paramref name="shotVelocity"/> would have to
    /// come through the held body to reach the player. The same front arc the armour
    /// model uses, because it is the same piece of the craft being talked about: the
    /// core is on the front, and the catch is held over it.
    /// </summary>
    public bool ShieldsFrom(Vector2 shotVelocity, Vector2 facing)
    {
        if (!Holding) return false;
        if (shotVelocity.LengthSquared() < 1e-6f) return false;
        Vector2 srcDir = Vector2.Normalize(-shotVelocity);
        return Vector2.Dot(srcDir, facing) > ShieldArc;
    }

    /// <summary>Bills the held body for a round it just ate. Returns what to take off its
    /// integrity, so the world's own damage path scores the kill if this is the one that
    /// finishes it.</summary>
    public float Soak()
    {
        Soaked++;
        return ShieldBite;
    }

    /// <summary>
    /// Hurls the catch along <paramref name="direction"/> — the full look line, so the
    /// same trigger can spike a hunter into the grid at the player's feet or throw it
    /// onto a roof. The body leaves the claw carrying its own ballistic velocity and the
    /// world steps it from there; this hand is empty again the moment it lets go.
    ///
    /// Returns the thing thrown, or null if the hand was empty.
    /// </summary>
    public EnemyTank? Throw(Vector3 direction)
    {
        if (Victim is not { } prey) return null;

        Vector3 d = direction.LengthSquared() > 1e-6f
            ? Vector3.Normalize(direction)
            : new Vector3(0f, 0f, 1f);

        prey.Grabbed = false;
        prey.Flung = true;
        prey.Toss = new Vector3(d.X * ThrowSpeed, d.Y * ThrowSpeed + ThrowLift, d.Z * ThrowSpeed);

        Victim = null;
        Squeeze = 0f;
        Soaked = 0;
        return prey;
    }

    /// <summary>
    /// Opens the hand without throwing — what a crushed body, a cinematic, or the
    /// crafting panel does. The catch drops where it is and, if it is still alive, goes
    /// straight back to driving itself.
    /// </summary>
    public EnemyTank? Drop()
    {
        if (Victim is not { } prey) return null;
        prey.Grabbed = false;
        prey.Height = 0f;
        Victim = null;
        Squeeze = 0f;
        Soaked = 0;
        return prey;
    }
}
