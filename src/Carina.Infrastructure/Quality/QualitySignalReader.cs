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

        if (await RolledWithinAsync(period, cancellationToken) is not { } rolled)
        {
            return [.. (await RawAsync(period.From, period.Until, cancellationToken)).Select(QualitySignalWindow.Of)];
        }

        IReadOnlyList<QualitySignalSample> head = await RawAsync(period.From, rolled.From, cancellationToken);
        IReadOnlyList<QualitySignalRollup> windows =
            await rollups.ListAsync(QualityWindow.Hour, rolled.From, rolled.Until, cancellationToken);
        IReadOnlyList<QualitySignalSample> tail = await RawAsync(rolled.Until, period.Until, cancellationToken);

        return
        [
            .. head.Select(QualitySignalWindow.Of),
            .. windows.Select(QualitySignalWindow.Of),
            .. tail.Select(QualitySignalWindow.Of),
        ];
    }

    public async Task<IReadOnlyList<QualitySignalWindow>> WindowsAsync(
        QualityTrendFrame frame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);

        QualityPeriod period = frame.Period;

        if (await RolledWithinAsync(period, cancellationToken) is not { } rolled)
        {
            return [.. (await RawAsync(period.From, period.Until, cancellationToken)).Select(QualitySignalWindow.Of)];
        }

        IReadOnlyList<QualitySignalSample> head = await RawAsync(period.From, rolled.From, cancellationToken);
        IReadOnlyList<QualitySignalWindow> windows = await rollups.ListFoldedAsync(
            QualityWindow.Hour,
            rolled.From,
            rolled.Until,
            frame.Length,
            QualityTrendFrame.Grid,
            cancellationToken);
        IReadOnlyList<QualitySignalSample> tail = await RawAsync(rolled.Until, period.Until, cancellationToken);

        return [.. head.Select(QualitySignalWindow.Of), .. windows, .. tail.Select(QualitySignalWindow.Of)];
    }

    private static DateTime FirstWholeHourFrom(DateTime at)
    {
        DateTime start = QualityWindows.StartOf(at, QualityWindow.Hour);

        return start == at ? start : QualityWindows.EndOf(start, QualityWindow.Hour);
    }

    private async Task<QualityRollupSpan?> RolledWithinAsync(QualityPeriod period, CancellationToken cancellationToken)
    {
        if (await rollups.LatestWindowStartAsync(QualityWindow.Hour, cancellationToken) is not { } latest)
        {
            return null;
        }

        DateTime from = FirstWholeHourFrom(period.From);
        DateTime rolledThrough = QualityWindows.EndOf(latest, QualityWindow.Hour);
        DateTime lastWholeHourEnds = QualityWindows.StartOf(period.Until, QualityWindow.Hour);
        DateTime until = rolledThrough < lastWholeHourEnds ? rolledThrough : lastWholeHourEnds;

        return from < until ? new QualityRollupSpan(from, until) : null;
    }

    private async Task<IReadOnlyList<QualitySignalSample>> RawAsync(
        DateTime from,
        DateTime until,
        CancellationToken cancellationToken)
        => from < until
            ? await samples.ListTakenBetweenAsync(from, until, cancellationToken)
            : [];
}
