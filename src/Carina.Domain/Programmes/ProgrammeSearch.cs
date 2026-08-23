using Carina.Contracts;

namespace Carina.Domain.Programmes;

public enum ProgrammeSort
{
    StartsAt = 0,

    Name = 1,
}

public enum ProgrammeField
{
    Title = 1,

    Description = 2,
}

public sealed record ProgrammeConditions
{
    public string? Exclude { get; init; }

    public IReadOnlyList<ProgrammeField>? Fields { get; init; }

    public IReadOnlyList<int>? Genres { get; init; }

    public TuneSystem? System { get; init; }

    public IReadOnlyList<ProgrammeService>? Channels { get; init; }
}

public sealed class ProgrammeSearch
{
    public const int ShortestKeyword = 2;

    public const int MostPerPage = 200;

    public const int DefaultPerPage = 50;

    public const int MostWords = 8;

    public const int MostChannels = 64;

    public const int HighestGenre = 15;

    public static readonly TimeSpan LongestSpan = TimeSpan.FromDays(31);

    private static readonly IReadOnlyList<ProgrammeField> BothFields =
        [ProgrammeField.Title, ProgrammeField.Description];

    private ProgrammeSearch(
        string keyword,
        IReadOnlyList<string> words,
        IReadOnlyList<string> excludedWords,
        IReadOnlyList<ProgrammeField> fields,
        IReadOnlyList<int> genres,
        TuneSystem? system,
        IReadOnlyList<ProgrammeService> channels,
        IReadOnlyList<ProgrammeService>? services,
        IReadOnlyList<ProgrammeService> withheld,
        DateTime? from,
        DateTime? to,
        ProgrammeSort sort,
        bool descending,
        int page,
        int perPage)
    {
        Keyword = keyword;
        Words = words;
        ExcludedWords = excludedWords;
        Fields = fields;
        Genres = genres;
        System = system;
        Channels = channels;
        Services = services;
        Withheld = withheld;
        From = from;
        To = to;
        Sort = sort;
        Descending = descending;
        Page = page;
        PerPage = perPage;
    }

    public string Keyword { get; }

    public IReadOnlyList<string> Words { get; }

    public IReadOnlyList<string> ExcludedWords { get; }

    public IReadOnlyList<ProgrammeField> Fields { get; }

    public IReadOnlyList<int> Genres { get; }

    public TuneSystem? System { get; }

    public IReadOnlyList<ProgrammeService> Channels { get; }

    public IReadOnlyList<ProgrammeService>? Services { get; }

    public IReadOnlyList<ProgrammeService> Withheld { get; }

    public DateTime? From { get; }

    public DateTime? To { get; }

    public ProgrammeSort Sort { get; }

    public bool Descending { get; }

    public int Page { get; }

    public int PerPage { get; }

    private bool NarrowsNothing
        => Words.Count is 0
            && ExcludedWords.Count is 0
            && Genres.Count is 0
            && Channels.Count is 0
            && System is null
            && From is null
            && To is null;

    public static ProgrammeSearch? For(
        string? keyword,
        DateTime? from,
        DateTime? to,
        ProgrammeSort sort = ProgrammeSort.StartsAt,
        bool descending = false,
        int? page = null,
        int? perPage = null,
        ProgrammeConditions? conditions = null)
    {
        string asked = (keyword ?? string.Empty).Trim();
        ProgrammeConditions beside = conditions ?? new ProgrammeConditions();

        if (WordsIn(asked) is not { } words
            || ExcludedIn(beside.Exclude) is not { } excluded
            || FieldsIn(beside.Fields) is not { } fields
            || GenresIn(beside.Genres) is not { } genres
            || ChannelsIn(beside.Channels) is not { } channels)
        {
            return null;
        }

        if (!Enum.IsDefined(sort))
        {
            return null;
        }

        if (beside.System is { } named && named is not TuneSystem.Unspecified && !Enum.IsDefined(named))
        {
            return null;
        }

        if (from is { } start && start.Kind is not DateTimeKind.Utc)
        {
            return null;
        }

        if (to is { } end && end.Kind is not DateTimeKind.Utc)
        {
            return null;
        }

        if (from is { } began && to is { } finished && (finished <= began || finished - began > LongestSpan))
        {
            return null;
        }

        var looking = new ProgrammeSearch(
            asked,
            words,
            excluded,
            fields,
            genres,
            beside.System is TuneSystem.Unspecified ? null : beside.System,
            channels,
            null,
            [],
            from,
            to,
            sort,
            descending,
            page is { } asking && asking > 1 ? asking : 1,
            Clamped(perPage));

        return looking.NarrowsNothing ? null : looking;
    }

    public ProgrammeSearch Over(IReadOnlyList<ProgrammeService>? services)
        => new(
            Keyword,
            Words,
            ExcludedWords,
            Fields,
            Genres,
            System,
            Channels,
            services,
            Withheld,
            From,
            To,
            Sort,
            Descending,
            Page,
            PerPage);

    public ProgrammeSearch Except(IReadOnlyList<ProgrammeService> withheld)
    {
        ArgumentNullException.ThrowIfNull(withheld);

        return new(
            Keyword,
            Words,
            ExcludedWords,
            Fields,
            Genres,
            System,
            Channels,
            Services,
            withheld,
            From,
            To,
            Sort,
            Descending,
            Page,
            PerPage);
    }

    private static IReadOnlyList<string>? WordsIn(string asked)
    {
        if (asked.Length is 0)
        {
            return [];
        }

        string[] apart = asked.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (apart.Length is 0 or > MostWords || !apart.Any(word => word.Length >= ShortestKeyword))
        {
            return null;
        }

        return [.. apart.Select(word => word.ToLowerInvariant())];
    }

    private static IReadOnlyList<string>? ExcludedIn(string? asked)
        => string.IsNullOrWhiteSpace(asked) ? [] : WordsIn(asked.Trim());

    private static IReadOnlyList<ProgrammeField>? FieldsIn(IReadOnlyList<ProgrammeField>? asked)
    {
        if (asked is null || asked.Count == 0)
        {
            return BothFields;
        }

        return asked.Any(field => !Enum.IsDefined(field)) ? null : [.. asked.Distinct()];
    }

    private static IReadOnlyList<int>? GenresIn(IReadOnlyList<int>? asked)
    {
        if (asked is null || asked.Count == 0)
        {
            return [];
        }

        return asked.Any(genre => genre is < 0 or > HighestGenre) ? null : [.. asked.Distinct()];
    }

    private static IReadOnlyList<ProgrammeService>? ChannelsIn(IReadOnlyList<ProgrammeService>? asked)
    {
        if (asked is null || asked.Count == 0)
        {
            return [];
        }

        ProgrammeService[] apart = [.. asked.Distinct()];

        return apart.Length > MostChannels ? null : apart;
    }

    private static int Clamped(int? perPage)
        => perPage switch
        {
            null or < 1 => DefaultPerPage,
            > MostPerPage => MostPerPage,
            { } asked => asked,
        };
}
