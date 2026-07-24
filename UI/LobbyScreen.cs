using Raylib_cs;
using VoidTanks.Core;
using VoidTanks.Input;
using VoidTanks.Net;

namespace VoidTanks.UI;

/// <summary>
/// The multiplayer front door: host a match and read out the code, or type someone else's.
///
/// Deliberately its own screen rather than a branch of the hangar. Single-player and
/// multiplayer are separate modes — a solo run is a sealed one-seat match that nobody can
/// join — and this is where the second mode begins. The flow is lobby first, hangar second:
/// you settle who is playing and under what rules, and only then go and pick a chassis.
/// </summary>
public sealed class LobbyScreen
{
    public enum Phase
    {
        /// <summary>Choosing whether to host or to join.</summary>
        Choosing,
        /// <summary>Hosting: the code is up, the rules are editable, waiting for people.</summary>
        Hosting,
        /// <summary>Typing someone else's code.</summary>
        Entering,
        /// <summary>Dialled, waiting for the host to answer.</summary>
        Joining,
    }

    /// <summary>What the screen is asking the loop to do this frame.</summary>
    public enum Action { None, Back, HostMatch, JoinMatch, Launch }

    /// <summary>The rows a host can walk while waiting for people.</summary>
    public enum Row { Seats, FriendlyFire, Revives, Launch }

    public Phase Where { get; private set; } = Phase.Choosing;
    public Row Selected { get; private set; } = Row.Seats;

    /// <summary>0 = HOST, 1 = JOIN, on the opening choice.</summary>
    public int Choice { get; private set; }

    /// <summary>The rules this host will impose. Handed to the world when the match starts.</summary>
    public MatchSettings Match { get; } = new()
    {
        MaxPlayers = MatchSettings.DefaultMaxPlayers,
        Revives = MatchSettings.DefaultRevives,
    };

    /// <summary>What the joiner has typed so far.</summary>
    public string Typed { get; private set; } = "";

    /// <summary>The code to read out, once hosting.</summary>
    public string Code => SteamNet.LocalCode;

    /// <summary>Filled in when something goes wrong, shown instead of a working screen.</summary>
    public string? Trouble { get; set; }

    /// <summary>How many are in so far, for the roster line. Set by the loop from the session.</summary>
    public int Seated { get; set; } = 1;

    public Action Update()
    {
        if (InputMap.QuitPressed)
        {
            if (Where is Phase.Choosing) return Action.Back;
            Where = Phase.Choosing;
            Typed = "";
            Trouble = null;
            return Action.None;
        }

        return Where switch
        {
            Phase.Choosing => UpdateChoosing(),
            Phase.Hosting => UpdateHosting(),
            Phase.Entering => UpdateEntering(),
            _ => Action.None,       // Joining: nothing to do but wait for the host
        };
    }

    private Action UpdateChoosing()
    {
        if (InputMap.MenuUp || InputMap.MenuDown) Choice = 1 - Choice;
        if (!InputMap.MenuConfirm) return Action.None;

        // Steam has to actually be up before either door opens. Saying so here is far kinder
        // than a host screen showing a code made of dashes that nobody can connect to.
        if (!SteamNet.Available)
        {
            Trouble = SteamNet.Trouble ?? "STEAM UNAVAILABLE";
            return Action.None;
        }

        if (Choice == 0)
        {
            Where = Phase.Hosting;
            return Action.HostMatch;
        }
        Where = Phase.Entering;
        Typed = "";
        return Action.None;
    }

    private Action UpdateHosting()
    {
        if (InputMap.MenuDown) Step(+1);
        if (InputMap.MenuUp) Step(-1);

        int nudge = (InputMap.MenuRight ? 1 : 0) - (InputMap.MenuLeft ? 1 : 0);
        if (nudge != 0)
        {
            switch (Selected)
            {
                case Row.Seats:
                    // Never below what is already in the room: a host cannot evict someone by
                    // winding the cap down past them.
                    Match.MaxPlayers = Math.Clamp(Match.MaxPlayers + nudge,
                        Math.Max(2, Seated), MatchSettings.MaxSeats);
                    break;
                case Row.FriendlyFire:
                    Match.FriendlyFire = !Match.FriendlyFire;
                    break;
                case Row.Revives:
                    Match.Revives = Math.Clamp(Match.Revives + nudge, 0, MatchSettings.MaxRevives);
                    break;
            }
        }

        if (InputMap.MenuConfirm && Selected == Row.Launch) return Action.Launch;
        return Action.None;
    }

    private void Step(int dir)
    {
        int n = System.Enum.GetValues<Row>().Length;
        Selected = (Row)(((int)Selected + dir + n) % n);
    }

    private Action UpdateEntering()
    {
        // Raylib hands back typed characters rather than key codes, which is what a text
        // field wants — it already knows about the keyboard layout underneath.
        for (int c = Raylib.GetCharPressed(); c != 0; c = Raylib.GetCharPressed())
        {
            if (Typed.Length >= 7) break;
            char ch = char.ToUpperInvariant((char)c);
            if (char.IsLetterOrDigit(ch)) Typed += ch;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && Typed.Length > 0)
            Typed = Typed[..^1];

        if (InputMap.MenuConfirm && Typed.Length == 7)
        {
            if (SteamNet.Decode(Typed) is null)
            {
                Trouble = "THAT IS NOT A CODE";
                return Action.None;
            }
            Where = Phase.Joining;
            Trouble = null;
            return Action.JoinMatch;
        }
        return Action.None;
    }

    /// <summary>The loop calls this when a dial fails or a host drops, to put the player back
    /// somewhere they can act rather than on a screen that will never change.</summary>
    public void Fail(string why)
    {
        Trouble = why;
        Where = Phase.Choosing;
        Typed = "";
    }
}
