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

        if (OutcomesIn(beside.Outcomes) is not { } outcomes || ChannelsIn(beside.Channels) is not { } channels)
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

        if (SpanIsUnusable(from, to))
        {
            return null;
        }

        if (page is < 1)
        {
            return null;
        }

        return new RecordingQuery(
            beside.Standing,
            outcomes,
            beside.Drops,
            channels,
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

    private static IReadOnlyList<RecordingOutcome>? OutcomesIn(IReadOnlyList<RecordingOutcome>? asked)
    {
        if (asked is null || asked.Count == 0)
        {
            return [];
        }

        return asked.Any(outcome => !Enum.IsDefined(outcome)) ? null : [.. asked.Distinct()];
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
}
