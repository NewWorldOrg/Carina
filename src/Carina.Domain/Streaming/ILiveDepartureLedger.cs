using Carina.Domain.Base;

namespace Carina.Domain.Streaming;

public interface ILiveDepartureLedger
{
    void Note(LiveSessionKey key, LiveDeparture departure, TimeSpan carried);

    LiveDepartureTally Read();
}

public sealed record LiveDepartureCount
{
    public LiveDepartureCount(
        LiveDeparture departure,
        long times,
        DateTime? lastAt,
        TimeSpan? shortest = null,
        TimeSpan? longest = null)
    {
        if (!Enum.IsDefined(departure))
        {
            throw new ArgumentOutOfRangeException(
                nameof(departure),
                departure,
                "A wire ends in one of the ways named here.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(times);
        UtcTimes.Optional(lastAt, nameof(lastAt));

        if ((times is 0) != (lastAt is null))
        {
            throw new ArgumentException(
                "A way a wire has ended says when it last did, and one no wire has ended in says nothing.",
                nameof(lastAt));
        }

        if ((times is 0) != (shortest is null) || (times is 0) != (longest is null))
        {
            throw new ArgumentException(
                "A way a wire has ended says how long the shortest and the longest of them lasted.",
                nameof(shortest));
        }

        if (shortest is { } least && longest is { } most)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(least, TimeSpan.Zero, nameof(shortest));

            if (most < least)
            {
                throw new ArgumentException(
                    "The longest a wire lasted is not shorter than the shortest.",
                    nameof(longest));
            }
        }

        Departure = departure;
        Times = times;
        LastAt = lastAt;
        Shortest = shortest;
        Longest = longest;
    }

    public LiveDeparture Departure { get; }

    public long Times { get; }

    public DateTime? LastAt { get; }

    public TimeSpan? Shortest { get; }

    public TimeSpan? Longest { get; }
}

public sealed record LiveDepartureTally
{
    public LiveDepartureTally(DateTime since, IReadOnlyList<LiveDepartureCount> counted)
    {
        ArgumentNullException.ThrowIfNull(counted);
        UtcTimes.Required(since, nameof(since));

        if (!counted.Select(count => count.Departure).SequenceEqual(Enum.GetValues<LiveDeparture>()))
        {
            throw new ArgumentException(
                "Every way a wire can end is answered for, in the order they are named.",
                nameof(counted));
        }

        Since = since;
        Counted = counted;
    }

    public DateTime Since { get; }

    public IReadOnlyList<LiveDepartureCount> Counted { get; }
}
