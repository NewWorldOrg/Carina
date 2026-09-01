using Carina.Domain.Base;

namespace Carina.Domain.Streaming;

public sealed class LivePts : CommonValueObject<ulong>
{
    public const int Hertz = 90_000;

    public const ulong ComesAroundAt = 1UL << 33;

    private LivePts(ulong ticks)
        : base(ticks)
    {
    }

    public static LivePts Start { get; } = new(0UL);

    public static LivePts Furthest { get; } = new(ulong.MaxValue);

    public static LivePts Of(ulong ticks) => new(ticks);

    public static LivePts Rescaled(ulong ticks, uint timescale)
    {
        if (timescale is 0U)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timescale),
                timescale,
                "A clock that ticks no times a second says nothing about when a picture is shown.");
        }

        UInt128 rescaled = (UInt128)ticks * Hertz / timescale;

        return new LivePts(rescaled > ulong.MaxValue ? ulong.MaxValue : (ulong)rescaled);
    }
}
