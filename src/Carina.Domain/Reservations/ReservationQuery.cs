using Carina.Domain.Programmes;

namespace Carina.Domain.Reservations;

public enum ReservationStanding
{
    Scheduled = 1,

    Conflict = 2,

    Cancelled = 3,

    Missed = 4,

    Recording = 5,

    Complete = 6,

    Truncated = 7,

    Failed = 8,
}

public enum ReservationOrigin
{
    ByHand = 1,

    ByRule = 2,
}

public enum ReservationSort
{
    StartAt = 0,

    Priority = 1,
}

public sealed record ReservationConditions
{
    public IReadOnlyList<ReservationStanding>? Standings { get; init; }

    public ReservationOrigin? Origin { get; init; }

    public IReadOnlyList<ProgrammeService>? Channels { get; init; }

    public string? Keyword { get; init; }
}

public sealed class ReservationQuery
{
    public const int MostPerPage = 200;

    public const int DefaultPerPage = 50;

    public const int MostChannels = 64;

    public const int ShortestKeyword = 2;

    public static readonly TimeSpan LongestSpan = TimeSpan.FromDays(366);

    private ReservationQuery(
        IReadOnlyList<ReservationStanding> standings,
        ReservationOrigin? origin,
        IReadOnlyList<ProgrammeService> channels,
        string? keyword,
        DateTime? from,
        DateTime? to,
        ReservationSort sort,
        bool descending,
        int page,
        int perPage)
    {
        Standings = standings;
        Origin = origin;
        Channels = channels;
        Keyword = keyword;
        From = from;
        To = to;
        Sort = sort;
        Descending = descending;
        Page = page;
        PerPage = perPage;
    }

    public IReadOnlyList<ReservationStanding> Standings { get; }

    public ReservationOrigin? Origin { get; }

    public IReadOnlyList<ProgrammeService> Channels { get; }

    public string? Keyword { get; }

    public DateTime? From { get; }

    public DateTime? To { get; }

    public ReservationSort Sort { get; }

    public bool Descending { get; }

    public int Page { get; }

    public int PerPage { get; }

    public static ReservationQuery? For(
        DateTime? from,
        DateTime? to,
        ReservationSort sort = ReservationSort.StartAt,
        bool descending = false,
        int? page = null,
        int? perPage = null,
        ReservationConditions? conditions = null)
    {
        ReservationConditions beside = conditions ?? new ReservationConditions();

        if (StandingsIn(beside.Standings) is not { } standings || ChannelsIn(beside.Channels) is not { } channels)
        {
            return null;
        }

        if (KeywordIn(beside.Keyword) is not { } keyword)
        {
            return null;
        }

        if (!Enum.IsDefined(sort))
        {
            return null;
        }

        if (beside.Origin is { } origin && !Enum.IsDefined(origin))
        {
            return null;
        }

        if (SpanIsUnusable(from, to) || page is < 1)
        {
            return null;
        }

        return new ReservationQuery(
            standings,
            beside.Origin,
            channels,
            keyword.Length is 0 ? null : keyword,
            from,
            to,
            sort,
            descending,
            page ?? 1,
            Clamped(perPage));
    }

    private static int Clamped(int? perPage)
        => perPage switch
        {
            null or < 1 => DefaultPerPage,
            > MostPerPage => MostPerPage,
            { } asked => asked,
        };

    private static bool SpanIsUnusable(DateTime? from, DateTime? to)
    {
        if (from is { } start && start.Kind is not DateTimeKind.Utc)
        {
            return true;
        }

        if (to is { } end && end.Kind is not DateTimeKind.Utc)
        {
            return true;
        }

        return from is { } began && to is { } finished && (finished <= began || finished - began > LongestSpan);
    }

    private static IReadOnlyList<ReservationStanding>? StandingsIn(IReadOnlyList<ReservationStanding>? asked)
    {
        if (asked is null || asked.Count == 0)
        {
            return [];
        }

        return asked.Any(standing => !Enum.IsDefined(standing)) ? null : [.. asked.Distinct()];
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

    private static string? KeywordIn(string? asked)
    {
        if (asked is null)
        {
            return string.Empty;
        }

        string trimmed = asked.Trim();

        return trimmed.Length is 0 || trimmed.Length >= ShortestKeyword ? trimmed : null;
    }
}
