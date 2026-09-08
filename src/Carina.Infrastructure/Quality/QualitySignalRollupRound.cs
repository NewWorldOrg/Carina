using Carina.Domain.Quality;

namespace Carina.Infrastructure.Quality;

public sealed record QualitySignalSweep(int Rolled, int SamplesForgotten, int WindowsForgotten);

public sealed class QualitySignalRollupRound(
    IQualitySignalSampleRepository samples,
    IQualitySignalRollupRepository rollups,
    QualitySignalSettings settings,
    TimeProvider clock)
{
    public async Task<QualitySignalSweep> RunAsync(CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        int rolled = 0;

        foreach (QualityWindow granularity in QualityWindows.All)
        {
            rolled += await RollAsync(granularity, now, cancellationToken);
        }

        return new QualitySignalSweep(
            rolled,
            await ForgetSamplesAsync(now, cancellationToken),
            await ForgetWindowsAsync(now, cancellationToken));
    }

    private async Task<int> RollAsync(QualityWindow granularity, DateTime now, CancellationToken cancellationToken)
    {
        DateTime? latest = await rollups.LatestWindowStartAsync(granularity, cancellationToken);

        if (QualitySignalRollupPlan.Span(now, granularity, latest, settings.KeepSamplesFor) is not { } span)
        {
            return 0;
        }

        IReadOnlyList<QualitySignalSample> taken =
            await samples.ListTakenBetweenAsync(span.From, span.Until, cancellationToken);

        IReadOnlyList<QualitySignalRollup> made = QualitySignalRollupPlan.Over(taken, granularity);

        await rollups.SaveAsync(made, cancellationToken);

        return made.Count;
    }

    private async Task<int> ForgetSamplesAsync(DateTime now, CancellationToken cancellationToken)
    {
        Dictionary<QualityWindow, DateTime?> through = [];

        foreach (QualityWindow granularity in QualityWindows.All)
        {
            DateTime? latest = await rollups.LatestWindowStartAsync(granularity, cancellationToken);

            through[granularity] = latest is { } window ? QualityWindows.EndOf(window, granularity) : null;
        }

        return QualitySignalRetention.SamplesTakenBefore(now, settings.KeepSamplesFor, through) is { } cutoff
            ? await samples.ForgetTakenBeforeAsync(cutoff, cancellationToken)
            : 0;
    }

    private async Task<int> ForgetWindowsAsync(DateTime now, CancellationToken cancellationToken)
    {
        int forgotten = 0;

        foreach (QualityWindow granularity in QualityWindows.All)
        {
            if (QualitySignalRetention.WindowsStartedBefore(now, settings.KeptFor(granularity)) is { } cutoff)
            {
                forgotten += await rollups.ForgetStartedBeforeAsync(granularity, cutoff, cancellationToken);
            }
        }

        return forgotten;
    }
}
