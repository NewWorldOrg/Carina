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
