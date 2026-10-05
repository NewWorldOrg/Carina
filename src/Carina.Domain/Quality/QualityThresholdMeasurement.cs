using Carina.Domain.Base;

namespace Carina.Domain.Quality;

/// <summary>
/// A level measured from the sessions that dropped packets and the ones that did not, with how many of each it
/// stood on and the span they were taken over.
/// </summary>
public sealed record QualityThresholdMeasurement
{
    private QualityThresholdMeasurement(
        double value,
        long sessions,
        long sessionsDropped,
        DateTime from,
        DateTime until,
        DateTime measuredAt)
    {
        Value = value;
        Sessions = sessions;
        SessionsDropped = sessionsDropped;
        From = from;
        Until = until;
        MeasuredAt = measuredAt;
    }

    public double Value { get; }

    public long Sessions { get; }

    public long SessionsDropped { get; }

    public DateTime From { get; }

    public DateTime Until { get; }

    public DateTime MeasuredAt { get; }

    public static QualityThresholdMeasurement Of(
        double value,
        long sessions,
        long sessionsDropped,
        DateTime from,
        DateTime until,
        DateTime measuredAt)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A measured level is a number a reading can be compared against.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessions);
        ArgumentOutOfRangeException.ThrowIfNegative(sessionsDropped);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sessionsDropped, sessions);

        DateTime start = UtcTimes.Required(from, nameof(from));
        DateTime end = UtcTimes.Required(until, nameof(until));

        if (end < start)
        {
            throw new ArgumentException("The sessions a level was measured from do not end before they start.", nameof(until));
        }

        return new QualityThresholdMeasurement(
            value,
            sessions,
            sessionsDropped,
            start,
            end,
            UtcTimes.Required(measuredAt, nameof(measuredAt)));
    }
}
