using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

/// <summary>
/// The last thing a running job reported and when: how much of the source is done and how long
/// the rest should take. The time is the part a reader cannot do without — it is what tells a
/// job that is getting on from one that has stopped and still says "running" (BR-ED2-014).
/// </summary>
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
