using Carina.Contracts;

namespace Carina.Domain.Recordings;

public sealed record ExpectedBitrate
{
    public static readonly ExpectedBitrate Terrestrial = new(14_300_000, 16_500_000);

    public static readonly ExpectedBitrate Satellite = new(11_100_000, 12_200_000);

    public ExpectedBitrate(long leastBitsPerSecond, long mostBitsPerSecond)
    {
        if (leastBitsPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leastBitsPerSecond),
                leastBitsPerSecond,
                "A broadcast that carries no bits per second weighs nothing, so nothing can be weighed against it.");
        }

        if (mostBitsPerSecond <= leastBitsPerSecond)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mostBitsPerSecond),
                mostBitsPerSecond,
                $"A measured range has width, so the top of this one is above {leastBitsPerSecond}. "
                + "A single rate would narrow the weight a recording may have to the slack alone, "
                + "which is tighter than broadcasts are observed to vary.");
        }

        LeastBitsPerSecond = leastBitsPerSecond;
        MostBitsPerSecond = mostBitsPerSecond;
    }

    public long LeastBitsPerSecond { get; }

    public long MostBitsPerSecond { get; }

    public static ExpectedBitrate Of(TunerKind kind)
        => kind switch
        {
            TunerKind.Terrestrial => Terrestrial,
            TunerKind.Satellite => Satellite,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "A recording was weighed against the rates measured off a tuner of a named kind."),
        };

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
