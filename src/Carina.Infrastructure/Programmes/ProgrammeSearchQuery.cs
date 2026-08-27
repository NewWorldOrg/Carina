using System.Globalization;

using Carina.Contracts;
using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Programmes;

public sealed record ProgrammeSearchTerm(string Name, Type Shape, bool Repeated);

public static class ProgrammeSearchQuery
{
    public const string Keyword = "keyword";

    public const string Exclude = "exclude";

    public const string Fields = "fields";

    public const string Genre = "genre";

    public const string Type = "type";

    public const string Channel = "channel";

    public const string From = "from";

    public const string To = "to";

    public const string Sort = "sort";

    public const string Descending = "descending";

    public const string Page = "page";

    public const string PerPage = "perPage";

    public static IReadOnlyList<ProgrammeSearchTerm> Vocabulary { get; } =
    [
        new(Keyword, typeof(string), false),
        new(Exclude, typeof(string), false),
        new(Fields, typeof(ProgrammeField), true),
        new(Genre, typeof(int), true),
        new(Type, typeof(TuneSystem), false),
        new(Channel, typeof(string), true),
        new(From, typeof(DateTimeOffset), false),
        new(To, typeof(DateTimeOffset), false),
        new(Sort, typeof(ProgrammeSort), false),
        new(Descending, typeof(bool), false),
        new(Page, typeof(int), false),
        new(PerPage, typeof(int), false),
    ];

    public static ProgrammeSearch? Read(string? query)
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> asked = Apart(query);

        if (Every<ProgrammeField>(All(asked, Fields)) is not { } fields
            || Numbers(All(asked, Genre)) is not { } genres
            || ProgrammeServiceText.Every(All(asked, Channel)) is not { } channels)
        {
            return null;
        }

        if (!Named(One(asked, Type), out TuneSystem? system)
            || !Named(One(asked, Sort), out ProgrammeSort? sort)
            || !Truth(One(asked, Descending), out bool? descending)
            || !Number(One(asked, Page), out int? page)
            || !Number(One(asked, PerPage), out int? perPage)
            || !Instant(One(asked, From), out DateTimeOffset? from)
            || !Instant(One(asked, To), out DateTimeOffset? to))
        {
            return null;
        }

        return ProgrammeSearch.For(
            One(asked, Keyword),
            from?.UtcDateTime,
            to?.UtcDateTime,
            sort ?? ProgrammeSort.StartsAt,
            descending ?? false,
            page,
            perPage,
            new ProgrammeConditions
            {
                Exclude = One(asked, Exclude),
                Fields = fields,
                Genres = genres,
                System = system,
                Channels = channels,
            });
    }

    private static IReadOnlyList<TKind>? Every<TKind>(IReadOnlyList<string> texts)
        where TKind : struct, Enum
    {
        var carried = new List<TKind>(texts.Count);

        foreach (string text in texts)
        {
            if (!Enum.TryParse(text, ignoreCase: true, out TKind read))
            {
                return null;
            }

            carried.Add(read);
        }

        return carried;
    }

    private static IReadOnlyList<int>? Numbers(IReadOnlyList<string> texts)
    {
        var carried = new List<int>(texts.Count);

        foreach (string text in texts)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int read))
            {
                return null;
            }

            carried.Add(read);
        }

        return carried;
    }

    private static bool Named<TKind>(string? text, out TKind? read)
        where TKind : struct, Enum
    {
        read = null;

        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        if (!Enum.TryParse(text, ignoreCase: true, out TKind found))
        {
            return false;
        }

        read = found;

        return true;
    }

    private static bool Truth(string? text, out bool? read)
    {
        read = null;

        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        if (!bool.TryParse(text, out bool found))
        {
            return false;
        }

        read = found;

        return true;
    }

    private static bool Number(string? text, out int? read)
    {
        read = null;

        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int found))
        {
            return false;
        }

        read = found;

        return true;
    }

    private static bool Instant(string? text, out DateTimeOffset? read)
    {
        read = null;

        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset found))
        {
            return false;
        }

        read = found;

        return true;
    }

    private static IReadOnlyList<string> All(
        IReadOnlyDictionary<string, IReadOnlyList<string>> asked,
        string name)
        => asked.TryGetValue(name, out IReadOnlyList<string>? found) ? found : [];

    private static string? One(IReadOnlyDictionary<string, IReadOnlyList<string>> asked, string name)
        => All(asked, name) is [string first, ..] ? first : null;

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Apart(string? query)
    {
        var read = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        string carried = query ?? string.Empty;

        if (carried.StartsWith('?'))
        {
            carried = carried[1..];
        }

        foreach (string pair in carried.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=', StringComparison.Ordinal);
            string name = Plain(equals < 0 ? pair : pair[..equals]);

            if (name.Length == 0)
            {
                continue;
            }

            string value = equals < 0 ? string.Empty : Plain(pair[(equals + 1)..]);

            if (read.TryGetValue(name, out IReadOnlyList<string>? found))
            {
                ((List<string>)found).Add(value);
                continue;
            }

            read[name] = new List<string> { value };
        }

        return read;
    }

    private static string Plain(string text)
    {
        try
        {
            return Uri.UnescapeDataString(text.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return text;
        }
    }
}
