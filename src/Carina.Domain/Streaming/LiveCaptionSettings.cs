namespace Carina.Domain.Streaming;

public sealed record LiveCaptionSettings
{
    public static readonly TimeSpan FurthestCorrection = TimeSpan.FromSeconds(10);

    private readonly TimeSpan encoderDelay = TimeSpan.Zero;

    public TimeSpan EncoderDelay
    {
        get => encoderDelay;

        init => encoderDelay = value >= -FurthestCorrection && value <= FurthestCorrection
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A caption is moved by at most ten seconds either way to meet the picture it belongs to; further than that is not a correction.");
    }

    public LivePts Corrected(LivePts stamped)
    {
        ArgumentNullException.ThrowIfNull(stamped);

        long moved = encoderDelay.Ticks * LivePts.Hertz / TimeSpan.TicksPerSecond;

        if (moved >= 0)
        {
            return LivePts.Of(stamped.Value + (ulong)moved);
        }

        ulong earlier = (ulong)(-moved);

        return stamped.Value > earlier ? LivePts.Of(stamped.Value - earlier) : LivePts.Start;
    }
}
