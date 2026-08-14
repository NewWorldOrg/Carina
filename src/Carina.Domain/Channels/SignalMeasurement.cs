namespace Carina.Domain.Channels;

public sealed record SignalMeasurement
{
    private SignalMeasurement(
        DateTime measuredAt,
        bool locked,
        int? cnrMilliDecibels,
        long? postViterbiErrorBits,
        long? postViterbiTotalBits)
    {
        MeasuredAt = measuredAt;
        Locked = locked;
        CnrMilliDecibels = cnrMilliDecibels;
        PostViterbiErrorBits = postViterbiErrorBits;
        PostViterbiTotalBits = postViterbiTotalBits;
    }

    public DateTime MeasuredAt { get; }

    public bool Locked { get; }

    public int? CnrMilliDecibels { get; }

    public long? PostViterbiErrorBits { get; }

    public long? PostViterbiTotalBits { get; }

    public static SignalMeasurement WithLock(
        DateTime measuredAt,
        int? cnrMilliDecibels = null,
        long? postViterbiErrorBits = null,
        long? postViterbiTotalBits = null)
        => new(
            UtcTimes.Required(measuredAt, nameof(measuredAt)),
            true,
            cnrMilliDecibels,
            postViterbiErrorBits,
            postViterbiTotalBits);

    public static SignalMeasurement WithoutLock(DateTime measuredAt)
        => new(UtcTimes.Required(measuredAt, nameof(measuredAt)), false, null, null, null);
}
