using Carina.Domain.Quality;

namespace Carina.TestSupport;

public sealed class HeldQualityLedger : IQualityLedgerReader
{
    public List<QualityLedgerRow> Rows { get; } = [];

    public List<QualityPeriod> Asked { get; } = [];

    public Task<IReadOnlyList<QualityLedgerRow>> ReadAsync(QualityPeriod period, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(period);

        Asked.Add(period);

        return Task.FromResult<IReadOnlyList<QualityLedgerRow>>(
            [.. Rows.Where(row => period.Holds(row.StartedAt)).OrderBy(row => row.StartedAt)]);
    }
}

public sealed class HeldQualityThresholds : IQualityThresholdRepository
{
    public List<QualityThreshold> Thresholds { get; } = [];

    public Task<IReadOnlyList<QualityThreshold>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<QualityThreshold>>([.. Thresholds.OrderBy(threshold => threshold.Key)]);

    public Task<QualityThreshold?> FindAsync(QualityThresholdKey key, CancellationToken cancellationToken)
        => Task.FromResult(Thresholds.FirstOrDefault(threshold => threshold.Key == key));

    public Task SaveAsync(QualityThreshold threshold, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(threshold);

        Thresholds.RemoveAll(held => held.Key == threshold.Key);
        Thresholds.Add(threshold);

        return Task.CompletedTask;
    }
}

public sealed class HeldQualityThresholdChanges : IQualityThresholdChangeRepository
{
    public List<QualityThresholdChange> Changes { get; } = [];

    public Task AddAsync(QualityThresholdChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        Changes.Add(change);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QualityThresholdChange>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<QualityThresholdChange>>(
            [.. Changes.OrderByDescending(change => change.ChangedAt)]);
}

public sealed class HeldQualitySignals : IQualitySignalReader
{
    public List<SignalFigures> Figures { get; } = [];

    public List<QualityPeriod> Asked { get; } = [];

    public Task<IReadOnlyList<SignalFigures>> FiguresAsync(QualityPeriod period, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(period);

        Asked.Add(period);

        return Task.FromResult<IReadOnlyList<SignalFigures>>([.. Figures]);
    }
}

public sealed class HeldQualitySignalSamples : IQualitySignalSampleRepository
{
    public List<QualitySignalSample> Samples { get; } = [];

    public Task AddAsync(IReadOnlyList<QualitySignalSample> samples, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(samples);

        Samples.AddRange(samples);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QualitySignalSample>> ListTakenBetweenAsync(
        DateTime from,
        DateTime until,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<QualitySignalSample>>(
            [.. Samples.Where(sample => sample.TakenAt >= from && sample.TakenAt < until).OrderBy(sample => sample.TakenAt)]);

    public Task<int> ForgetTakenBeforeAsync(DateTime cutoff, CancellationToken cancellationToken)
        => Task.FromResult(Samples.RemoveAll(sample => sample.TakenAt < cutoff));
}

public sealed class HeldQualitySignalRollups : IQualitySignalRollupRepository
{
    public List<QualitySignalRollup> Rollups { get; } = [];

    public Task SaveAsync(IReadOnlyList<QualitySignalRollup> rollups, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rollups);

        foreach (QualitySignalRollup rollup in rollups)
        {
            Rollups.RemoveAll(held => held.Granularity == rollup.Granularity
                                      && held.WindowStart == rollup.WindowStart
                                      && held.Tuner == rollup.Tuner
                                      && held.Network == rollup.Network
                                      && held.Service == rollup.Service);

            Rollups.Add(rollup);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QualitySignalRollup>> ListAsync(
        QualityWindow granularity,
        DateTime from,
        DateTime until,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<QualitySignalRollup>>(
        [
            .. Rollups
                .Where(rollup => rollup.Granularity == granularity
                                 && rollup.WindowStart >= from
                                 && rollup.WindowStart < until)
                .OrderBy(rollup => rollup.WindowStart),
        ]);

    public Task<DateTime?> LatestWindowStartAsync(QualityWindow granularity, CancellationToken cancellationToken)
        => Task.FromResult(Rollups
            .Where(rollup => rollup.Granularity == granularity)
            .Select(rollup => (DateTime?)rollup.WindowStart)
            .OrderByDescending(start => start)
            .FirstOrDefault());

    public Task<int> ForgetStartedBeforeAsync(
        QualityWindow granularity,
        DateTime cutoff,
        CancellationToken cancellationToken)
        => Task.FromResult(
            Rollups.RemoveAll(rollup => rollup.Granularity == granularity && rollup.WindowStart < cutoff));
}
