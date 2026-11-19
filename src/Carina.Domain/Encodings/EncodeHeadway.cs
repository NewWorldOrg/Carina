using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

public sealed record EncodeHeadway
{
    public EncodeHeadway(double? portion, TimeSpan? left, DateTime at)
    {
        if (portion is { } done && (done < 0 || done > 1 || double.IsNaN(done)))
        {
            throw new ArgumentOutOfRangeException(nameof(portion), portion, "A portion is between none of it and all of it.");
        }

        if (left is { } more && more < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(left), left, "Nothing left cannot be less than none.");
        }

        Portion = portion;
        Left = left;
        At = UtcTimes.Required(at, nameof(at));
    }

    public double? Portion { get; }

    public TimeSpan? Left { get; }

    public DateTime At { get; }

    public static EncodeHeadway Of(EncodeProgress progress, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return new EncodeHeadway(progress.Portion, progress.Left, at);
    }
}
