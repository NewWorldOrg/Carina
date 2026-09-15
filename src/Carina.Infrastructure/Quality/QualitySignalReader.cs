using Carina.Domain.Quality;

namespace Carina.Infrastructure.Quality;

public sealed class QualitySignalReader(
    IQualitySignalRollupRepository rollups,
    IQualitySignalSampleRepository samples) : IQualitySignalReader
{
    public async Task<IReadOnlyList<SignalFigures>> FiguresAsync(
        QualityPeriod period,
        CancellationToken cancellationToken)
        => QualitySignalSurvey.Figures(await WindowsAsync(period, cancellationToken));

    public async Task<IReadOnlyList<QualitySignalWindow>> WindowsAsync(
        QualityPeriod period,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(period);

        DateTime boundary = await RolledThroughAsync(period, cancellationToken);

        IReadOnlyList<QualitySignalRollup> rolled = boundary > period.From
            ? await rollups.ListAsync(QualityWindow.Hour, period.From, boundary, cancellationToken)
            : [];

        IReadOnlyList<QualitySignalSample> raw = await TailAsync(period, boundary, cancellationToken);

        return [.. rolled.Select(QualitySignalWindow.Of), .. raw.Select(QualitySignalWindow.Of)];
    }

    public async Task<IReadOnlyList<QualitySignalWindow>> WindowsAsync(
        QualityTrendFrame frame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);

        QualityPeriod period = frame.Period;
        DateTime boundary = await RolledThroughAsync(period, cancellationToken);

        IReadOnlyList<QualitySignalWindow> rolled = boundary > period.From
            ? await rollups.ListFoldedAsync(
                QualityWindow.Hour,
                period.From,
                boundary,
                frame.Length,
                QualityTrendFrame.Grid,
                cancellationToken)
            : [];

        IReadOnlyList<QualitySignalSample> raw = await TailAsync(period, boundary, cancellationToken);

        return [.. rolled, .. raw.Select(QualitySignalWindow.Of)];
    }

    private async Task<DateTime> RolledThroughAsync(QualityPeriod period, CancellationToken cancellationToken)
    {
        DateTime? latest = await rollups.LatestWindowStartAsync(QualityWindow.Hour, cancellationToken);

        DateTime rolledThrough = latest is { } window
            ? QualityWindows.EndOf(window, QualityWindow.Hour)
            : period.From;

        return rolledThrough < period.From
            ? period.From
            : rolledThrough > period.Until ? period.Until : rolledThrough;
    }

    private async Task<IReadOnlyList<QualitySignalSample>> TailAsync(
        QualityPeriod period,
        DateTime boundary,
        CancellationToken cancellationToken)
        => boundary < period.Until
            ? await samples.ListTakenBetweenAsync(boundary, period.Until, cancellationToken)
            : [];
}
