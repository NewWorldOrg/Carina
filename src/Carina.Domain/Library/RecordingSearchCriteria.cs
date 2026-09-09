using Carina.Domain.Programmes;
using Carina.Domain.Recordings;

namespace Carina.Domain.Library;

public enum RecordingSortKey
{
    NewestFirst = 1,
}

public sealed record RecordingCursor(DateTime StartedAt, RecordingId Id);

public sealed record RecordingSearchConditions
{
    public IReadOnlyList<ProgrammeService>? Channels { get; init; }

    public int? Genre { get; init; }

    public IReadOnlyList<RecordingOutcome>? Outcomes { get; init; }

    public QualityLevel? Quality { get; init; }
}

public sealed class RecordingSearchCriteria
{
    public const int MostPerPage = 200;

    public const int DefaultPerPage = 50;

    public const int LongestKeyword = RecordingKeyword.LongestKeyword;

    public const int MostWords = RecordingKeyword.MostWords;

    public const int MostChannels = 64;

    public const int HighestGenre = 15;

    public static readonly TimeSpan LongestSpan = TimeSpan.FromDays(366);

    private RecordingSearchCriteria(
        string keyword,
        IReadOnlyList<string> words,
        IReadOnlyList<ProgrammeService> channels,
        int? genre,
        IReadOnlyList<RecordingOutcome> outcomes,
        QualityLevel? quality,
        DateTime? from,
        DateTime? to,
        RecordingSortKey sort,
        RecordingCursor? after,
        int perPage)
    {
        Keyword = keyword;
        Words = words;
        Channels = channels;
        Genre = genre;
        Outcomes = outcomes;
        Quality = quality;
        From = from;
        To = to;
        Sort = sort;
        After = after;
        PerPage = perPage;
    }

    public string Keyword { get; }

    public IReadOnlyList<string> Words { get; }

    public IReadOnlyList<ProgrammeService> Channels { get; }

    public int? Genre { get; }

    public IReadOnlyList<RecordingOutcome> Outcomes { get; }

    public QualityLevel? Quality { get; }

    public DateTime? From { get; }

    public DateTime? To { get; }

    public RecordingSortKey Sort { get; }

    public RecordingCursor? After { get; }

    public int PerPage { get; }

    public static RecordingSearchCriteria? For(
        string? keyword,
        DateTime? from,
        DateTime? to,
        RecordingSortKey sort = RecordingSortKey.NewestFirst,
        RecordingCursor? after = null,
        int? perPage = null,
        RecordingSearchConditions? conditions = null)
    {
        RecordingSearchConditions beside = conditions ?? new RecordingSearchConditions();

        if (RecordingKeyword.For(keyword) is not { } asked
            || ChannelsIn(beside.Channels) is not { } channels
            || OutcomesIn(beside.Outcomes) is not { } outcomes)
        {
            return null;
        }

        if (!Enum.IsDefined(sort))
        {
            return null;
        }

        if (beside.Genre is { } genre && genre is < 0 or > HighestGenre)
        {
            return null;
        }

        if (beside.Quality is { } quality && !Enum.IsDefined(quality))
        {
            return null;
        }

        if (SpanIsUnusable(from, to) || after is { StartedAt.Kind: not DateTimeKind.Utc })
        {
            return null;
        }

        return new RecordingSearchCriteria(
            asked.Asked,
            asked.Words,
            channels,
            beside.Genre,
            outcomes,
            beside.Quality,
            from,
            to,
            sort,
            after,
            Clamped(perPage));
    }

    private static IReadOnlyList<ProgrammeService>? ChannelsIn(IReadOnlyList<ProgrammeService>? asked)
    {
        if (asked is null || asked.Count is 0)
        {
            return [];
        }

        ProgrammeService[] apart = [.. asked.Distinct()];

        return apart.Length > MostChannels ? null : apart;
    }

    private static IReadOnlyList<RecordingOutcome>? OutcomesIn(IReadOnlyList<RecordingOutcome>? asked)
    {
        if (asked is null || asked.Count is 0)
        {
            return [];
        }

        return asked.Any(outcome => !Enum.IsDefined(outcome)) ? null : [.. asked.Distinct()];
    }

    private static bool SpanIsUnusable(DateTime? from, DateTime? to)
    {
        if (from is { Kind: not DateTimeKind.Utc } || to is { Kind: not DateTimeKind.Utc })
        {
            return true;
        }

        return from is { } began && to is { } finished && (finished <= began || finished - began > LongestSpan);
    }

    private static int Clamped(int? perPage)
        => perPage switch
        {
            null or < 1 => DefaultPerPage,
            > MostPerPage => MostPerPage,
            { } asked => asked,
        };
}
