namespace Carina.Domain.Recordings;

/// <summary>
/// What the sessions before the one writing a recording counted of it.
/// </summary>
public sealed record CarriedCount
{
    public CarriedCount(DropCounters counters, DropTimeline positions, long? scrambledPackets, long overflows)
    {
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentNullException.ThrowIfNull(positions);

        if (scrambledPackets is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scrambledPackets),
                scrambledPackets,
                "A count of packets left scrambled is not negative.");
        }

        if (overflows < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overflows), overflows, "An overflow count is not negative.");
        }

        Counters = counters;
        Positions = positions;
        ScrambledPackets = scrambledPackets;
        Overflows = overflows;
    }

    public static CarriedCount Nothing { get; } = new(DropCounters.Unmeasured, DropTimeline.Unlocated, null, 0);

    public DropCounters Counters { get; }

    public DropTimeline Positions { get; }

    public long? ScrambledPackets { get; }

    public long Overflows { get; }
}
