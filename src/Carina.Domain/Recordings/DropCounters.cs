namespace Carina.Domain.Recordings;

public sealed record DropCounters
{
    private DropCounters(bool measured, long? dropped, long? total)
    {
        Measured = measured;
        Dropped = dropped;
        Total = total;
    }

    public static DropCounters Unmeasured { get; } = new(false, null, null);

    public bool Measured { get; }

    public long? Dropped { get; }

    public long? Total { get; }

    public static DropCounters Counted(long dropped, long total)
    {
        if (dropped < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dropped), dropped, "A count of lost packets is not negative.");
        }

        if (total < dropped)
        {
            throw new ArgumentOutOfRangeException(
                nameof(total),
                total,
                $"A stream cannot lose {dropped} of {total} packets.");
        }

        return new DropCounters(true, dropped, total);
    }

    public static DropCounters Rehydrate(bool measured, long? dropped, long? total)
    {
        if (!measured)
        {
            return dropped is null && total is null
                ? Unmeasured
                : throw new ArgumentException(
                    "Nothing counted these packets, so there is no number to carry.",
                    nameof(measured));
        }

        if (dropped is null || total is null)
        {
            throw new ArgumentException("Counted packets come with both numbers.", nameof(measured));
        }

        return Counted(dropped.Value, total.Value);
    }
}
