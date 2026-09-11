using Carina.Domain.Base;
using Carina.Domain.Programmes;

namespace Carina.Domain.Recordings;

public enum RecordingStanding
{
    InFlight = 1,

    Ended = 2,
}

public enum DropReading
{
    Dropped = 1,

    Clean = 2,

    Unmeasured = 3,
}

public enum RecordingSort
{
    StartedAt = 0,

    ProgrammeStartsAt = 1,
}

public sealed record RecordingConditions
{
    public string? Keyword { get; init; }

    public RecordingStanding? Standing { get; init; }

    public IReadOnlyList<RecordingOutcome>? Outcomes { get; init; }

    public DropReading? Drops { get; init; }

    public IReadOnlyList<ProgrammeService>? Channels { get; init; }
}

public sealed class RecordingQuery
{
    public const int MostPerPage = 200;

    public const int DefaultPerPage = 50;

    public const int MostChannels = 64;

    public static readonly TimeSpan LongestSpan = TimeSpan.FromDays(366);

    private RecordingQuery(
        RecordingKeyword keyword,
        RecordingStanding? standing,
        IReadOnlyList<RecordingOutcome> outcomes,
        DropReading? drops,
        IReadOnlyList<ProgrammeService> channels,
        DateTime? from,
        DateTime? to,
        RecordingSort sort,
        bool descending,
        int page,
        int perPage)
    {
        Keyword = keyword;
        Standing = standing;
        Outcomes = outcomes;
        Drops = drops;
        Channels = channels;
        From = from;
        To = to;
        Sort = sort;
        Descending = descending;
        Page = page;
        PerPage = perPage;
    }

    public RecordingKeyword Keyword { get; }

    public RecordingStanding? Standing { get; }

    public IReadOnlyList<RecordingOutcome> Outcomes { get; }

    public DropReading? Drops { get; }

    public IReadOnlyList<ProgrammeService> Channels { get; }

    public DateTime? From { get; }

    public DateTime? To { get; }

    public RecordingSort Sort { get; }

    public bool Descending { get; }

    public int Page { get; }

    public int PerPage { get; }

    public static RecordingQuery? For(
        DateTime? from,
        DateTime? to,
        RecordingSort sort = RecordingSort.StartedAt,
        bool descending = false,
        int? page = null,
        int? perPage = null,
        RecordingConditions? conditions = null)
    {
        RecordingConditions beside = conditions ?? new RecordingConditions();

        if (RecordingKeyword.For(beside.Keyword) is not { } keyword
            || ListingGuards.NamedIn(beside.Outcomes) is not { } outcomes
            || ListingGuards.NoMoreThan(beside.Channels, MostChannels) is not { } channels)
        {
            return null;
        }

        if (!Enum.IsDefined(sort))
        {
            return null;
        }

        if (beside.Standing is { } standing && !Enum.IsDefined(standing))
        {
            return null;
        }

        if (beside.Drops is { } drops && !Enum.IsDefined(drops))
        {
            return null;
        }

        if (beside.Standing is RecordingStanding.InFlight && outcomes.Count > 0)
        {
            return null;
        }

        if (ListingGuards.SpanIsUnusable(from, to, LongestSpan))
        {
            return null;
        }

        if (page is < 1)
        {
            return null;
        }

        return new RecordingQuery(
            keyword,
            beside.Standing,
            outcomes,
            beside.Drops,
            channels,
            from,
            to,
            sort,
            descending,
            page ?? 1,
            ListingGuards.Clamped(perPage, DefaultPerPage, MostPerPage));
    }
}
