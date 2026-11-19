namespace Carina.Domain.Quality;

public sealed record QualityThresholdSettled(QualityThreshold Threshold, QualityThresholdChange? Change);

public static class QualityThresholdSettling
{
    public static QualityThresholdSettled ByHand(QualityThresholdStanding standing, double value, DateTime at, string? by)
    {
        ArgumentNullException.ThrowIfNull(standing);

        QualityThreshold revised = QualityThreshold.Rehydrate(
            standing.Key,
            Threshold.Of(standing.Setting.Default, value, provisional: true, 0, at),
            by,
            byHand: true,
            standing.Measurement);

        return new QualityThresholdSettled(
            revised,
            Change(standing, value, at, by, QualityThresholdChangeCause.Hand));
    }

    public static QualityThresholdSettled Released(QualityThresholdStanding standing, DateTime at, string? by)
    {
        ArgumentNullException.ThrowIfNull(standing);

        if (!standing.ByHand)
        {
            return new QualityThresholdSettled(Kept(standing, standing.Setting), null);
        }

        Threshold setting = standing.Measurement is { } measured
            ? Threshold.Of(standing.Setting.Default, measured.Value, provisional: false, measured.Sessions, at)
            : Threshold.Of(standing.Setting.Default, standing.Setting.Default, provisional: true, 0, at);

        return new QualityThresholdSettled(
            QualityThreshold.Rehydrate(standing.Key, setting, by, byHand: false, standing.Measurement),
            Change(standing, setting.Current, at, by, QualityThresholdChangeCause.Hand));
    }

    public static QualityThresholdSettled Measured(QualityThresholdStanding standing, QualityThresholdMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(standing);
        ArgumentNullException.ThrowIfNull(measurement);

        if (standing.ByHand)
        {
            return new QualityThresholdSettled(
                QualityThreshold.Rehydrate(standing.Key, standing.Setting, standing.UpdatedBy, byHand: true, measurement),
                null);
        }

        Threshold setting = Threshold.Of(
            standing.Setting.Default,
            measurement.Value,
            provisional: false,
            measurement.Sessions,
            measurement.MeasuredAt);
        QualityThresholdChange? change = standing.Setting.Current.Equals(measurement.Value)
            ? null
            : Change(standing, measurement.Value, measurement.MeasuredAt, null, QualityThresholdChangeCause.Measurement);

        return new QualityThresholdSettled(
            QualityThreshold.Rehydrate(standing.Key, setting, null, byHand: false, measurement),
            change);
    }

    private static QualityThreshold Kept(QualityThresholdStanding standing, Threshold setting)
        => QualityThreshold.Rehydrate(standing.Key, setting, standing.UpdatedBy, standing.ByHand, standing.Measurement);

    private static QualityThresholdChange Change(
        QualityThresholdStanding standing,
        double next,
        DateTime at,
        string? by,
        QualityThresholdChangeCause cause)
        => QualityThresholdChange.Record(
            QualityThresholdChangeId.New(),
            standing.Key,
            standing.Setting.Current,
            next,
            at,
            by,
            cause);
}
