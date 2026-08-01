using System.Numerics;
using Unrendered.Core;

namespace Unrendered.Entities;

/// <summary>
/// The little bit of state that turns twenty reports a second into motion, on the client side
/// of every body this machine is only being told about — team-mates, hunters, the squad.
///
/// <para>Easing toward the last reported position is most of the job and was all of it: it
/// turns the 20 Hz step into a glide instead of a strobe. What it gets wrong is what happens
/// when a report does not arrive. The target stops moving, so the body eases onto it and
/// <em>stands still</em> until the next packet lands, then lurches. On a wire dropping one
/// packet in twenty that is a hitch about once a second, on everything on screen at once, and
/// it reads exactly like lag — which it is not: the host knows perfectly well where that
/// hunter is, and this machine has everything it needs to work out where it is going.</para>
///
/// <para>So the target carries itself forward along the speed the last two reports implied.
/// A missed packet coasts through the gap and the next one corrects whatever the guess got
/// wrong, which is what the easing was already there to do.</para>
///
/// <para>Position only, deliberately. Height and heading are eased but never extrapolated: a
/// coasting height puts a craft through a roof, a coasting heading keeps a turning body
/// spinning past where it stopped, and neither stalls in a way anybody can see for the fifty
/// milliseconds it would last.</para>
/// </summary>
public struct NetGlide
{
    /// <summary>Where the body is being eased toward — the last reported position, carried
    /// forward by <see cref="Coast"/> between reports.</summary>
    public Vector2 Target;

    /// <summary>True once the first report has landed. Until then there is nothing to ease
    /// toward and the caller snaps instead: a body first seen has to appear where it is, not
    /// slide in from wherever its placeholder opened.</summary>
    public bool Has;

    private Vector2 _velocity;
    private Vector2 _reported;   // the last position genuinely reported, never extrapolated
    private float _since;        // seconds since that report

    /// <summary>
    /// How long a body will coast for on its own. Five snapshots: long enough to ride out any
    /// loss a playable wire produces, short enough that a body which has genuinely stopped
    /// being described — it died, it went out of interest range, the wire fell over — comes to
    /// rest rather than sailing off across the map on a guess nobody is correcting.
    /// </summary>
    private const float CoastLimit = 0.25f;

    /// <summary>
    /// Above this implied speed the two reports are not one body travelling, they are one body
    /// somewhere else — a hyperspace, a seizure, a respawn, or simply the first report after a
    /// gap longer than the coast. Coasting on a velocity derived from a teleport would fire the
    /// body off the edge of the world, so the guess is thrown away and it eases across instead.
    /// </summary>
    private const float MaxCoastSpeed = 60f;

    /// <summary>Takes a fresh report from the host. Returns true if this is the first one, which
    /// the caller answers by snapping rather than easing.</summary>
    public bool Report(Vector2 pos)
    {
        if (!Has)
        {
            Target = _reported = pos;
            _velocity = Vector2.Zero;
            _since = 0f;
            Has = true;
            return true;
        }

        // Measured against the last REPORTED position rather than the current target, which has
        // been walking forward on its own since — measuring against that would subtract the
        // coast from the next velocity and wind the estimate down toward nothing.
        if (_since > 1e-4f)
        {
            Vector2 v = Torus.Delta(_reported, pos) / _since;
            _velocity = v.LengthSquared() > MaxCoastSpeed * MaxCoastSpeed ? Vector2.Zero : v;
        }

        _reported = pos;
        Target = pos;
        _since = 0f;
        return false;
    }

    /// <summary>Carries the target one frame forward along the last known velocity. Called every
    /// frame, whether or not a report arrived — on the frames one did, <see cref="Report"/> has
    /// just reset the clock and this adds a single frame to it.</summary>
    public void Coast(float dt)
    {
        if (!Has) return;
        _since += dt;
        if (_since <= CoastLimit) Target = Torus.Wrap(Target + _velocity * dt);
    }
}
