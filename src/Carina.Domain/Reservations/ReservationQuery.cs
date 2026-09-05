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
    public const int MostPerPage = ListingGuards.MostPerPage;

    public const int DefaultPerPage = ListingGuards.DefaultPerPage;

    public const int MostChannels = ListingGuards.MostChannels;

    public const int ShortestKeyword = 2;

    public static readonly TimeSpan LongestSpan = ListingGuards.LongestSpan;

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

        if (ListingGuards.NamedIn(beside.Standings) is not { } standings
            || ListingGuards.ChannelsIn(beside.Channels) is not { } channels)
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

        if (ListingGuards.SpanIsUnusable(from, to) || page is < 1)
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
            ListingGuards.Clamped(perPage));
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
