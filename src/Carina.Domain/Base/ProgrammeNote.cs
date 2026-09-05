using System.Text;

namespace Carina.Domain.Base;

/// <summary>
/// What another programme said, kept for a person to read: the paths on this machine are taken
/// out first, and only then is the tail kept, because the tail is where the failure is.
/// </summary>
public static class ProgrammeNote
{
    public const string InsteadOfAPath = "…";

    public const int Longest = 1000;

    public static string Of(string said, int longest)
    {
        ArgumentNullException.ThrowIfNull(said);
        ArgumentOutOfRangeException.ThrowIfLessThan(longest, 1);

        string kept = WithoutPaths(said).Trim();

        return kept.Length <= longest ? kept : kept[^longest..];
    }

    private static string WithoutPaths(string said)
    {
        var kept = new StringBuilder(said.Length);
        int at = 0;

        while (at < said.Length)
        {
            if (said[at] is '/' && (at is 0 || Breaks(said[at - 1])))
            {
                while (at < said.Length && !Breaks(said[at]))
                {
                    at++;
                }

                kept.Append(InsteadOfAPath);

                continue;
            }

            kept.Append(said[at]);
            at++;
        }

        return kept.ToString();
    }

    private static bool Breaks(char letter)
        => char.IsWhiteSpace(letter) || letter is '\'' or '"' or '(' or '[' or ')' or ']' or ',';
}
