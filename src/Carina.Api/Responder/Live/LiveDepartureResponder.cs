using Carina.Domain.Streaming;

namespace Carina.Api.Responder.Live;

public sealed record LiveDepartureResponder(
    LiveDeparture Departure,
    long Times,
    DateTime? LastAt,
    double? ShortestSeconds,
    double? LongestSeconds)
{
    private const int Places = 3;

    public static LiveDepartureResponder Of(LiveDepartureCount counted)
    {
        ArgumentNullException.ThrowIfNull(counted);

        return new LiveDepartureResponder(
            counted.Departure,
            counted.Times,
            counted.LastAt,
            Seconds(counted.Shortest),
            Seconds(counted.Longest));
    }

    private static double? Seconds(TimeSpan? span) => span is { } some ? Math.Round(some.TotalSeconds, Places) : null;
}

public sealed record LiveDepartureTallyResponder(DateTime Since, IReadOnlyList<LiveDepartureResponder> Departures)
{
    public static LiveDepartureTallyResponder Of(LiveDepartureTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);

        return new LiveDepartureTallyResponder(tally.Since, [.. tally.Counted.Select(LiveDepartureResponder.Of)]);
    }
}
