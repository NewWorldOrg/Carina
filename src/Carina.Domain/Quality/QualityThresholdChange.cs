using Carina.Domain.Base;

namespace Carina.Domain.Quality;

public sealed class QualityThresholdChange
{
    public const int ChangedByMaxLength = 128;

    private QualityThresholdChange()
    {
    }

    public QualityThresholdChangeId Id { get; private set; } = null!;

    public QualityThresholdKey Key { get; private set; }

    public double PreviousValue { get; private set; }

    public double NextValue { get; private set; }

    public DateTime ChangedAt { get; private set; }

    public string? ChangedBy { get; private set; }

    public static QualityThresholdChange Record(
        QualityThresholdChangeId id,
        QualityThresholdKey key,
        double previousValue,
        double nextValue,
        DateTime changedAt,
        string? changedBy)
        => Rehydrate(id, key, previousValue, nextValue, changedAt, changedBy);

    public static QualityThresholdChange Rehydrate(
        QualityThresholdChangeId id,
        QualityThresholdKey key,
        double previousValue,
        double nextValue,
        DateTime changedAt,
        string? changedBy)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (!Enum.IsDefined(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "A threshold is kept under one of the keys this domain names.");
        }

        Measured(previousValue, nameof(previousValue));
        Measured(nextValue, nameof(nextValue));

        if (changedBy is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(changedBy);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(changedBy.Length, ChangedByMaxLength, nameof(changedBy));
        }

        return new QualityThresholdChange
        {
            Id = id,
            Key = key,
            PreviousValue = previousValue,
            NextValue = nextValue,
            ChangedAt = UtcTimes.Required(changedAt, nameof(changedAt)),
            ChangedBy = changedBy,
        };
    }

    private static void Measured(double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A threshold is a number a reading can be compared against.");
        }
    }
}
