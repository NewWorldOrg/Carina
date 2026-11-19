using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Carina.Domain.Programmes;

namespace Carina.Domain.Segments;

public static partial class SeriesNames
{
    public const int ShortestSharedHead = 4;

    private const char FullWidthSpace = '　';

    private const char Cut = '';

    private static readonly string[] MarkSymbols = [.. ProgrammeMarks.Symbols.Select(symbol => symbol.Symbol)];

    private static readonly string[] BracketedMarks = [.. MarkSymbols.Select(symbol => $"[{symbol.Normalize(NormalizationForm.FormKC)}]").Distinct()];

    private static readonly char[] Ends = [' ', '\t', Cut];

    public static string? Of(string programmeName)
    {
        ArgumentNullException.ThrowIfNull(programmeName);

        string folded = Folded(programmeName).Trim(Ends);
        int cut = folded.IndexOf(Cut, StringComparison.Ordinal);
        string head = cut < 0 ? folded : folded[..cut];
        string series = WithoutSubtitles(Episode().Replace(head, string.Empty).Trim(Ends));

        return series.Length is 0 ? null : series;
    }

    public static int? EpisodeOf(string programmeName)
    {
        ArgumentNullException.ThrowIfNull(programmeName);

        foreach (Match match in Episode().Matches(Folded(programmeName)))
        {
            if (Counted(match) is int episode)
            {
                return episode;
            }
        }

        return null;
    }

    public static bool Same(string? one, string? other) => one is not null && string.Equals(one, other, StringComparison.Ordinal);

    public static bool Link(string? one, string? other)
    {
        if (one is null || other is null)
        {
            return false;
        }

        (string shorter, string longer) = one.Length <= other.Length ? (one, other) : (other, one);

        return string.Equals(shorter, longer, StringComparison.Ordinal)
               || (shorter.Length >= ShortestSharedHead && longer.StartsWith(shorter, StringComparison.Ordinal));
    }

    private static string Folded(string name)
    {
        string unmarked = MarkSymbols.Aggregate(name.Replace(FullWidthSpace, Cut), (text, symbol) => text.Replace(symbol, string.Empty, StringComparison.Ordinal));
        string folded = unmarked.Normalize(NormalizationForm.FormKC);

        return BracketedMarks.Aggregate(folded, (text, mark) => text.Replace(mark, string.Empty, StringComparison.Ordinal));
    }

    private static string WithoutSubtitles(string name)
    {
        Match subtitle = Subtitle().Match(name);

        if (!subtitle.Success)
        {
            return name;
        }

        string before = name[..subtitle.Index].Trim(Ends);

        return before.Length < ShortestSharedHead ? name : WithoutSubtitles(before);
    }

    private static int? Counted(Match match)
    {
        if (match.Groups["kanji"].Success)
        {
            return Kanji(match.Groups["kanji"].Value);
        }

        return int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int episode) ? episode : null;
    }

    private static int Kanji(string number)
    {
        int ten = number.IndexOf('十', StringComparison.Ordinal);

        if (ten < 0)
        {
            return Digit(number[0]);
        }

        int tens = ten is 0 ? 1 : Digit(number[0]);
        int ones = ten == number.Length - 1 ? 0 : Digit(number[^1]);

        return (tens * 10) + ones;
    }

    private static int Digit(char kanji) => "一二三四五六七八九".IndexOf(kanji, StringComparison.Ordinal) + 1;

    [GeneratedRegex(
        @"(?:第(?<number>[0-9]+)[話回]|第(?<kanji>[一二三四五六七八九]?十[一二三四五六七八九]?|[一二三四五六七八九])話|#(?<number>[0-9]+)|(?<number>[0-9]+)話)[ \t]*(?:「[^」]*」)?",
        RegexOptions.CultureInvariant)]
    private static partial Regex Episode();

    [GeneratedRegex(@"(?:「[^「」]*」|『[^『』]*』|▽.*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex Subtitle();
}
