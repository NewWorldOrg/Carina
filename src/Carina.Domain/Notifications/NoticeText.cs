using System.Globalization;
using System.Text;

namespace Carina.Domain.Notifications;

/// <summary>
/// Cleans and cuts text somebody outside wrote, such as the name of a programme, before a notice says it.
/// </summary>
public static class NoticeText
{
    public const string Ellipsis = "…";

    private const char LineBreak = '\n';

    private static readonly HashSet<char> BidirectionalControls =
    [
        '؜',
        '‎',
        '‏',
        '‪',
        '‫',
        '‬',
        '‭',
        '‮',
        '⁦',
        '⁧',
        '⁨',
        '⁩',
    ];

    private static readonly HashSet<UnicodeCategory> Dropped =
    [
        UnicodeCategory.Control,
        UnicodeCategory.LineSeparator,
        UnicodeCategory.ParagraphSeparator,
    ];

    /// <summary>
    /// One line of text without control characters, bidirectional controls, line breaks or halves of surrogate pairs.
    /// </summary>
    public static string Clean(string? written) => Kept(written, keepsLineBreaks: false);

    /// <summary>
    /// As <see cref="Clean"/>, keeping line feeds.
    /// </summary>
    public static string CleanLines(string? written) => Kept(written, keepsLineBreaks: true);

    /// <summary>
    /// The words, or as many whole graphemes of them as leave room for an ellipsis within <paramref name="longest"/>.
    /// </summary>
    public static string Cut(string said, int longest)
    {
        ArgumentNullException.ThrowIfNull(said);

        return Fitted(Graphemes(said), longest);
    }

    internal static IReadOnlyList<string> Graphemes(string said)
    {
        List<string> graphemes = [];
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(said);

        while (elements.MoveNext())
        {
            graphemes.Add(elements.GetTextElement());
        }

        return graphemes;
    }

    internal static string Fitted(IReadOnlyList<string> pieces, int longest)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(longest, 1);

        if (pieces.Sum(piece => piece.Length) <= longest)
        {
            return string.Concat(pieces);
        }

        StringBuilder kept = new();

        foreach (string piece in pieces)
        {
            if (kept.Length + piece.Length + Ellipsis.Length > longest)
            {
                break;
            }

            kept.Append(piece);
        }

        return kept.Append(Ellipsis).ToString();
    }

    private static string Kept(string? written, bool keepsLineBreaks)
    {
        if (string.IsNullOrEmpty(written))
        {
            return string.Empty;
        }

        StringBuilder kept = new(written.Length);

        for (int index = 0; index < written.Length; index++)
        {
            if (IsAWholePair(written, index))
            {
                kept.Append(written, index, 2);
                index++;
                continue;
            }

            if (IsKept(written[index], keepsLineBreaks))
            {
                kept.Append(written[index]);
            }
        }

        return kept.ToString().Trim();
    }

    private static bool IsAWholePair(string written, int index)
        => char.IsHighSurrogate(written[index]) && index + 1 < written.Length && char.IsLowSurrogate(written[index + 1]);

    private static bool IsKept(char character, bool keepsLineBreaks)
    {
        if (character is LineBreak)
        {
            return keepsLineBreaks;
        }

        return !char.IsSurrogate(character)
            && !BidirectionalControls.Contains(character)
            && !Dropped.Contains(char.GetUnicodeCategory(character));
    }
}
