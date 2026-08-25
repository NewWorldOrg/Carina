namespace Carina.Domain.Recordings;

public sealed record ExpectedBitrate
{
    public ExpectedBitrate(long leastBitsPerSecond, long mostBitsPerSecond)
    {
        if (leastBitsPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leastBitsPerSecond),
                leastBitsPerSecond,
                "A broadcast that carries no bits per second weighs nothing, so nothing can be weighed against it.");
        }

        if (mostBitsPerSecond < leastBitsPerSecond)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mostBitsPerSecond),
                mostBitsPerSecond,
                $"A range reaches upwards, so the top of this one is at least {leastBitsPerSecond}.");
        }

        LeastBitsPerSecond = leastBitsPerSecond;
        MostBitsPerSecond = mostBitsPerSecond;
    }

    public long LeastBitsPerSecond { get; }

    public long MostBitsPerSecond { get; }

    public long LeastBytesOver(TimeSpan span) => BytesOver(LeastBitsPerSecond, span);

    public long MostBytesOver(TimeSpan span) => BytesOver(MostBitsPerSecond, span);

    private static long BytesOver(long bitsPerSecond, TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(span), span, "A stream runs forwards.");
        }

        return (long)(bitsPerSecond * span.TotalSeconds / 8.0);
    }
}
