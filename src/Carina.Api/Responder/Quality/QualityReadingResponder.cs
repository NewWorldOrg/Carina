using Carina.Api.Services;

using Carina.Domain.Quality;

namespace Carina.Api.Responder.Quality;

public sealed record QualityReadingResponder(
    QualityState State,
    int Subjects,
    int Measured,
    int Unmeasured,
    int BeyondThreshold)
{
    public static QualityReadingResponder Of(QualityReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return new QualityReadingResponder(
            QualityStates.Of(reading),
            reading.Subjects,
            reading.Measured,
            reading.Unmeasured,
            reading.BeyondThreshold);
    }
}

public sealed record QualityTallyResponder(
    QualityState State,
    int Subjects,
    int Measured,
    int Unmeasured,
    int BeyondThreshold,
    int Good,
    int Warning,
    int MayNotBeWatchable,
    int Unsupported,
    int Unreachable,
    double? Average,
    double? Lowest,
    double? Highest)
{
    public static QualityTallyResponder Of(QualityTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);

        return new QualityTallyResponder(
            tally.State,
            tally.Subjects,
            tally.Measured,
            tally.Unmeasured,
            tally.BeyondThreshold,
            tally.Good,
            tally.Warning,
            tally.MayNotBeWatchable,
            tally.Unsupported,
            tally.Unreachable,
            tally.Average,
            tally.Lowest,
            tally.Highest);
    }
}

public sealed record QualityMeasureResponder(QualityMetric Metric, QualityTallyResponder Reading)
{
    public static QualityMeasureResponder Of(QualityMeasure measure)
    {
        ArgumentNullException.ThrowIfNull(measure);

        return new QualityMeasureResponder(measure.Metric, QualityTallyResponder.Of(measure.Tally));
    }

    public static IReadOnlyList<QualityMeasureResponder> Over(IReadOnlyList<QualityMeasure> measures)
    {
        ArgumentNullException.ThrowIfNull(measures);

        return [.. measures.Select(Of)];
    }
}

public sealed record QualitySignalResponder(
    QualityThresholdKey Metric,
    QualityReadingResponder Reading,
    DateTime? LastTakenAt)
{
    public static QualitySignalResponder Of(QualitySignalStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);

        return new QualitySignalResponder(
            standing.Key,
            QualityReadingResponder.Of(standing.Reading),
            standing.LastTakenAt);
    }

    public static IReadOnlyList<QualitySignalResponder> Over(IReadOnlyList<QualitySignalStanding> signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        return [.. signal.Select(Of)];
    }
}
