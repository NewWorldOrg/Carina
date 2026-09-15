using Carina.Domain.Base;

namespace Carina.Domain.Channels;

public sealed record CandidateScore
{
    private CandidateScore(
        double lockRate,
        int? carrierToNoiseLowestMilliDecibels,
        double? bitErrorRateHighest,
        long samples,
        DateTime measuredFrom,
        DateTime measuredUntil,
        DateTime evaluatedAt)
    {
        LockRate = lockRate;
        CarrierToNoiseLowestMilliDecibels = carrierToNoiseLowestMilliDecibels;
        BitErrorRateHighest = bitErrorRateHighest;
        Samples = samples;
        MeasuredFrom = measuredFrom;
        MeasuredUntil = measuredUntil;
        EvaluatedAt = evaluatedAt;
    }

    public double LockRate { get; }

    public int? CarrierToNoiseLowestMilliDecibels { get; }

    public double? BitErrorRateHighest { get; }

    public long Samples { get; }

    public DateTime MeasuredFrom { get; }

    public DateTime MeasuredUntil { get; }

    public DateTime EvaluatedAt { get; }

    public static CandidateScore Of(
        long samples,
        long locked,
        int? carrierToNoiseLowestMilliDecibels,
        double? bitErrorRateHighest,
        DateTime measuredFrom,
        DateTime measuredUntil,
        DateTime evaluatedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samples);
        ArgumentOutOfRangeException.ThrowIfNegative(locked);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(locked, samples);

        if (locked is 0 && (carrierToNoiseLowestMilliDecibels is not null || bitErrorRateHighest is not null))
        {
            throw new ArgumentException(
                "A candidate that never held a lock read no carrier to noise figure and no error rate.",
                nameof(carrierToNoiseLowestMilliDecibels));
        }

        if (bitErrorRateHighest is { } rate)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(rate, nameof(bitErrorRateHighest));
        }

        UtcTimes.Required(measuredFrom, nameof(measuredFrom));
        UtcTimes.Required(measuredUntil, nameof(measuredUntil));
        UtcTimes.Required(evaluatedAt, nameof(evaluatedAt));

        if (measuredUntil <= measuredFrom)
        {
            throw new ArgumentException("A period ends after it begins.", nameof(measuredUntil));
        }

        if (evaluatedAt < measuredUntil)
        {
            throw new ArgumentException(
                "A score is evaluated once the period it covers has been read, not before.",
                nameof(evaluatedAt));
        }

        return new CandidateScore(
            (double)locked / samples,
            carrierToNoiseLowestMilliDecibels,
            bitErrorRateHighest,
            samples,
            measuredFrom,
            measuredUntil,
            evaluatedAt);
    }
}
