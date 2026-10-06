using Carina.Domain.Auth;

namespace Carina.Domain.Quality;

public sealed class QualityThreshold
{
    public const int UpdatedByMaxLength = AuthSession.LongestDisplayName;

    private QualityThreshold()
    {
    }

    public QualityThresholdKey Key { get; private set; }

    public Threshold Setting { get; private set; } = null!;

    public string? UpdatedBy { get; private set; }

    public bool ByHand { get; private set; }

    public double? MeasuredValue { get; private set; }

    public long? MeasuredSessions { get; private set; }

    public long? MeasuredSessionsDropped { get; private set; }

    public DateTime? MeasuredFrom { get; private set; }

    public DateTime? MeasuredUntil { get; private set; }

    public DateTime? MeasuredAt { get; private set; }

    /// <summary>
    /// The last measurement that decided this level, or null when none has.
    /// </summary>
    public QualityThresholdMeasurement? Measurement
        => MeasuredValue is { } value
            ? QualityThresholdMeasurement.Of(
                value,
                MeasuredSessions!.Value,
                MeasuredSessionsDropped!.Value,
                MeasuredFrom!.Value,
                MeasuredUntil!.Value,
                MeasuredAt!.Value)
            : null;

    public static QualityThreshold Declare(QualityThresholdKey key, Threshold setting)
        => Rehydrate(key, setting, null);

    public static QualityThreshold Rehydrate(
        QualityThresholdKey key,
        Threshold setting,
        string? updatedBy,
        bool byHand = false,
        QualityThresholdMeasurement? measurement = null)
    {
        if (!Enum.IsDefined(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "A threshold is kept under one of the keys this domain names.");
        }

        ArgumentNullException.ThrowIfNull(setting);

        if (updatedBy is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(updatedBy);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(updatedBy.Length, UpdatedByMaxLength, nameof(updatedBy));
        }

        StandsOn(key, setting, byHand, measurement);

        return new QualityThreshold
        {
            Key = key,
            Setting = setting,
            UpdatedBy = updatedBy,
            ByHand = byHand,
            MeasuredValue = measurement?.Value,
            MeasuredSessions = measurement?.Sessions,
            MeasuredSessionsDropped = measurement?.SessionsDropped,
            MeasuredFrom = measurement?.From,
            MeasuredUntil = measurement?.Until,
            MeasuredAt = measurement?.MeasuredAt,
        };
    }

    private static void StandsOn(QualityThresholdKey key, Threshold setting, bool byHand, QualityThresholdMeasurement? measurement)
    {
        if (measurement is not null && !SignalThresholdMeasure.Keys.Contains(key))
        {
            throw new ArgumentException($"Only the signal levels are measured, and {key} is not one of them.", nameof(measurement));
        }

        if (byHand && !setting.Provisional)
        {
            throw new ArgumentException("A level set by hand does not stand on a measurement.", nameof(byHand));
        }

        if (!setting.Provisional && (measurement is null || !setting.Current.Equals(measurement.Value)))
        {
            throw new ArgumentException(
                "A level that is not provisional is the value the measurement it stands on decided.",
                nameof(measurement));
        }
    }
}
