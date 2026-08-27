using System.Text;

using Carina.Domain.Base;

namespace Carina.Domain.Programmes;

public static class ProgrammeSearchMatching
{
    private const int Anything = '%';

    private const int OneCharacter = '_';

    private const int Escape = '\\';

    public static IReadOnlyList<ProgrammeMatch> Layered(
        IEnumerable<Programme> held,
        IEnumerable<ArchivedProgramme> archived)
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(archived);

        ProgrammeMatch[] carried = [.. held.Select(ProgrammeMatch.Of)];
        var already = carried.Select(Key).ToHashSet();

        return
        [
            .. carried,
            .. archived.Select(ProgrammeMatch.Of).Where(match => !already.Contains(Key(match))),
        ];
    }

    public static PaginatedList<ProgrammeMatch> Search(
        IEnumerable<ProgrammeMatch> matches,
        ProgrammeSearch search,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(search);

        ProgrammeMatch[] found =
        [
            .. Ordered(matches.Where(match => Matches(match, search, now)), search)
                .ThenBy(match => match.EventId.Value),
        ];

        return new PaginatedList<ProgrammeMatch>(
            [.. found.Skip((search.Page - 1) * search.PerPage).Take(search.PerPage)],
            found.Length,
            search.Page,
            search.PerPage);
    }

    public static bool Matches(ProgrammeMatch match, ProgrammeSearch search, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(search);

        ProgrammeReach reach = search.ReachAt(now);

        return !match.IsShadow
            && (reach.History || !match.IsArchived)
            && (reach.NotOverBy is not { } instant || match.EndsAt is null || match.EndsAt > instant)
            && search.Words.All(word => Carries(match, word, search.Fields))
            && !search.ExcludedWords.Any(word => Leaves(match, word, search.Fields))
            && (search.Genres.Count == 0 || match.Genres.Any(genre => search.Genres.Contains(genre.Kind)))
            && (search.Channels.Count == 0 || On(match, search.Channels))
            && (search.Services is not { } within || On(match, within))
            && !On(match, search.Withheld)
            && (search.From is not { } from || match.EndsAt is null || match.EndsAt > from)
            && (search.To is not { } to || match.StartsAt < to);
    }

    private static (int, int, int, DateTime) Key(ProgrammeMatch match)
        => (match.NetworkId.Value, match.ServiceId.Value, match.EventId.Value, match.StartsAt);

    private static IOrderedEnumerable<ProgrammeMatch> Ordered(
        IEnumerable<ProgrammeMatch> found,
        ProgrammeSearch search)
        => (search.Sort, search.Descending) switch
        {
            (ProgrammeSort.Name, false) => found.OrderBy(match => match.Name, ByCodePoint.Reading),
            (ProgrammeSort.Name, true) => found.OrderByDescending(match => match.Name, ByCodePoint.Reading),
            (_, true) => found.OrderByDescending(match => match.StartsAt),
            _ => found.OrderBy(match => match.StartsAt),
        };

    private static bool Carries(ProgrammeMatch match, string word, IReadOnlyList<ProgrammeField> fields)
    {
        string looked = ProgrammeSearchText.Folded(word);

        if (!Like(ProgrammeSearchText.Searchable(match.Name, match.Summary), looked))
        {
            return false;
        }

        return (fields.Contains(ProgrammeField.Title), fields.Contains(ProgrammeField.Description)) switch
        {
            (true, false) => Like(ProgrammeSearchText.Folded(match.Name), looked),
            (false, true) => Like(ProgrammeSearchText.Folded(match.Summary), looked),
            _ => true,
        };
    }

    private static bool Leaves(ProgrammeMatch match, string word, IReadOnlyList<ProgrammeField> fields)
    {
        string looked = ProgrammeSearchText.Folded(word);

        return (fields.Contains(ProgrammeField.Title), fields.Contains(ProgrammeField.Description)) switch
        {
            (true, false) => Like(ProgrammeSearchText.Folded(match.Name), looked),
            (false, true) => Like(ProgrammeSearchText.Folded(match.Summary), looked),
            _ => Like(ProgrammeSearchText.Searchable(match.Name, match.Summary), looked),
        };
    }

    private static bool On(ProgrammeMatch match, IReadOnlyList<ProgrammeService> services)
        => services.Any(service => service.NetworkId == match.NetworkId.Value
            && service.ServiceId == match.ServiceId.Value);

    private static bool Like(string subject, string word)
    {
        int[] text = CodePoints(subject);
        (int Value, bool Literal)[] pattern = Read(CodePoints($"{(char)Anything}{word}{(char)Anything}"));
        int taken = 0;
        int placed = 0;
        int marked = -1;
        int resumed = 0;

        while (taken < text.Length)
        {
            if (placed < pattern.Length && !Star(pattern[placed]) && Accepts(pattern[placed], text[taken]))
            {
                placed++;
                taken++;
            }
            else if (placed < pattern.Length && Star(pattern[placed]))
            {
                marked = placed;
                resumed = taken;
                placed++;
            }
            else if (marked >= 0)
            {
                placed = marked + 1;
                taken = ++resumed;
            }
            else
            {
                return false;
            }
        }

        while (placed < pattern.Length && Star(pattern[placed]))
        {
            placed++;
        }

        return placed == pattern.Length;
    }

    private static bool Star((int Value, bool Literal) part) => !part.Literal && part.Value == Anything;

    private static bool Accepts((int Value, bool Literal) part, int point)
        => part.Literal ? part.Value == point : part.Value == OneCharacter;

    private static (int Value, bool Literal)[] Read(int[] spelt)
    {
        var carried = new List<(int, bool)>(spelt.Length);

        for (int index = 0; index < spelt.Length; index++)
        {
            if (spelt[index] == Escape && index + 1 < spelt.Length)
            {
                carried.Add((spelt[index + 1], true));
                index++;
                continue;
            }

            carried.Add((spelt[index], spelt[index] is not Anything and not OneCharacter));
        }

        return [.. carried];
    }

    private static int[] CodePoints(string text)
    {
        var carried = new List<int>(text.Length);
        int index = 0;

        while (index < text.Length)
        {
            if (Rune.TryGetRuneAt(text, index, out Rune rune))
            {
                carried.Add(rune.Value);
                index += rune.Utf16SequenceLength;
                continue;
            }

            carried.Add(text[index]);
            index++;
        }

        return [.. carried];
    }

    private sealed class ByCodePoint : IComparer<string>
    {
        public static readonly ByCodePoint Reading = new();

        public int Compare(string? x, string? y)
        {
            int[] left = CodePoints(x ?? string.Empty);
            int[] right = CodePoints(y ?? string.Empty);

            for (int index = 0; index < left.Length && index < right.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return left[index] < right[index] ? -1 : 1;
                }
            }

            return left.Length.CompareTo(right.Length);
        }
    }
}
