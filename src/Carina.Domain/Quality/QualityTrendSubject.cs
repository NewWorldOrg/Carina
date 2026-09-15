namespace Carina.Domain.Quality;

public enum QualityTrendSubject
{
    PacketsLost = 1,

    PacketsLeftScrambled = 2,

    LockRate = 3,

    CarrierToNoise = 4,

    BitErrorRate = 5,
}

public static class QualityTrendSubjects
{
    public static readonly IReadOnlyList<QualityTrendSubject> All =
    [
        QualityTrendSubject.PacketsLost,
        QualityTrendSubject.PacketsLeftScrambled,
        QualityTrendSubject.LockRate,
        QualityTrendSubject.CarrierToNoise,
        QualityTrendSubject.BitErrorRate,
    ];

    public static QualityMetric? Metric(QualityTrendSubject subject) => subject switch
    {
        QualityTrendSubject.PacketsLost => QualityMetric.PacketsLost,
        QualityTrendSubject.PacketsLeftScrambled => QualityMetric.PacketsLeftScrambled,
        QualityTrendSubject.LockRate or QualityTrendSubject.CarrierToNoise or QualityTrendSubject.BitErrorRate => null,
        _ => throw Unnamed(subject),
    };

    public static QualityThresholdKey? SignalKey(QualityTrendSubject subject) => subject switch
    {
        QualityTrendSubject.LockRate => QualityThresholdKey.LockRate,
        QualityTrendSubject.CarrierToNoise => QualityThresholdKey.CarrierToNoiseFloor,
        QualityTrendSubject.BitErrorRate => QualityThresholdKey.BitErrorRateCeiling,
        QualityTrendSubject.PacketsLost or QualityTrendSubject.PacketsLeftScrambled => null,
        _ => throw Unnamed(subject),
    };

    public static QualityTrendStep Finest(QualityTrendSubject subject)
        => Metric(subject) is null ? QualityTrendStep.Hour : QualityTrendStep.Day;

    private static ArgumentOutOfRangeException Unnamed(QualityTrendSubject subject)
        => new(nameof(subject), subject, "A trend follows one of the subjects this domain names.");
}
