using Carina.Domain.Quality;

namespace Carina.Infrastructure.Quality;

public sealed class QualitySignalReader(
    IQualitySignalRollupRepository rollups,
    IQualitySignalSampleRepository samples) : IQualitySignalReader
{
    public async Task<IReadOnlyList<SignalFigures>> FiguresAsync(
        QualityPeriod period,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(period);

        DateTime? latest = await rollups.LatestWindowStartAsync(QualityWindow.Hour, cancellationToken);

        DateTime rolledThrough = latest is { } window
            ? QualityWindows.EndOf(window, QualityWindow.Hour)
            : period.From;

        DateTime boundary = rolledThrough < period.From
            ? period.From
            : rolledThrough > period.Until ? period.Until : rolledThrough;

        IReadOnlyList<QualitySignalRollup> rolled = boundary > period.From
            ? await rollups.ListAsync(QualityWindow.Hour, period.From, boundary, cancellationToken)
            : [];

        IReadOnlyList<QualitySignalSample> raw = boundary < period.Until
            ? await samples.ListTakenBetweenAsync(boundary, period.Until, cancellationToken)
            : [];

        return QualitySignalSurvey.Figures(rolled, raw);
    }
}
