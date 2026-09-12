using Carina.Domain.Streaming;

namespace Carina.Api.Responder.Live;

public sealed record LiveDepartureResponder(LiveDeparture Departure, long Times, DateTime? LastAt)
{
    public static LiveDepartureResponder Of(LiveDepartureCount counted)
    {
        ArgumentNullException.ThrowIfNull(counted);

        return new LiveDepartureResponder(counted.Departure, counted.Times, counted.LastAt);
    }
}

public sealed record LiveDepartureTallyResponder(DateTime Since, IReadOnlyList<LiveDepartureResponder> Departures)
{
    public static LiveDepartureTallyResponder Of(LiveDepartureTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);

        return new LiveDepartureTallyResponder(tally.Since, [.. tally.Counted.Select(LiveDepartureResponder.Of)]);
    }
}
