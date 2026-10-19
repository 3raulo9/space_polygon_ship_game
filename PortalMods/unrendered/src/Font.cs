using Raylib_cs;
using Unrendered.Modding;

namespace UnrenderedPortal;

/// <summary>
/// UNRENDERED's own lettering (through <see cref="ModText"/>), so nothing the rift says looks
/// like it came from somewhere else.
/// </summary>
public static class Font
{
    private const string Known = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-/:+.<>!,?{} ";

    /// <summary>The font is capitals, digits and a little punctuation. Everything the rift says
    /// is folded into that: dashes for em-dashes, three dots for an ellipsis, braces for
    /// brackets, and anything else dropped rather than drawn as a gap.</summary>
    public static string Clean(string s)
    {
        var b = new System.Text.StringBuilder(s.Length);
        foreach (char ch in s.ToUpperInvariant()
                     .Replace("—", "-").Replace("–", "-").Replace("…", "...")
                     .Replace('(', '{').Replace(')', '}').Replace('[', '{').Replace(']', '}')
                     .Replace('_', '-').Replace('\'', ' '))
            if (Known.IndexOf(ch) >= 0) b.Append(ch);
        return b.ToString();
    }

    public static int Draw(string s, int x, int y, int px, Color c) => ModText.Draw(Clean(s), x, y, c, px);

    public static void Centered(string s, int cx, int y, int px, Color c) => ModText.DrawCentered(Clean(s), cx, y, c, px);

    public static int Measure(string s, int px) => ModText.Measure(Clean(s), px);
}
