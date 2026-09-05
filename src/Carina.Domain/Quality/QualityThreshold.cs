namespace Carina.Domain.Quality;

public sealed class QualityThreshold
{
    public const int UpdatedByMaxLength = 128;

    private QualityThreshold()
    {
    }

    public QualityThresholdKey Key { get; private set; }

    public Threshold Setting { get; private set; } = null!;

    public string? UpdatedBy { get; private set; }

    public static QualityThreshold Declare(QualityThresholdKey key, Threshold setting)
        => Rehydrate(key, setting, null);

    public static QualityThreshold Rehydrate(QualityThresholdKey key, Threshold setting, string? updatedBy)
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

        return new QualityThreshold
        {
            Key = key,
            Setting = setting,
            UpdatedBy = updatedBy,
        };
    }
}
