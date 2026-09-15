using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

namespace Carina.TestSupport;

public sealed class HeldQualitySessionMeasurements : IQualitySessionMeasurementRepository
{
    private readonly List<QualitySessionMeasurement> kept = [];

    public IReadOnlyList<QualitySessionMeasurement> Measurements => [.. kept.Select(Copy)];

    public Task<QualitySessionMeasurement?> FindAsync(
        string driverInstanceId,
        SessionId session,
        CancellationToken cancellationToken)
        => Task.FromResult(kept
            .Where(held => held.DriverInstanceId == driverInstanceId && held.Session.Equals(session))
            .Select(Copy)
            .FirstOrDefault());

    public Task<IReadOnlyList<QualitySessionMeasurement>> ListOpenAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<QualitySessionMeasurement>>(
            [.. kept.Where(held => !held.HasEnded).Select(Copy)]);

    public Task<IReadOnlyList<QualitySessionMeasurement>> ListStartedBetweenAsync(
        DateTime from,
        DateTime until,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<QualitySessionMeasurement>>(
            [.. kept.Where(held => held.StartedAt >= from && held.StartedAt < until).OrderBy(held => held.StartedAt).Select(Copy)]);

    public Task SaveAsync(QualitySessionMeasurement measurement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(measurement);

        kept.RemoveAll(held =>
            held.DriverInstanceId == measurement.DriverInstanceId && held.Session.Equals(measurement.Session));
        kept.Add(Copy(measurement));

        return Task.CompletedTask;
    }

    private static QualitySessionMeasurement Copy(QualitySessionMeasurement measurement)
        => QualitySessionMeasurement.Rehydrate(
            measurement.DriverInstanceId,
            measurement.Session,
            measurement.Purpose,
            measurement.Tuner,
            measurement.Network,
            measurement.Service,
            measurement.StartedAt,
            measurement.EndedAt,
            measurement.CcMeasured,
            measurement.CcDroppedPackets,
            measurement.CcTotalPackets,
            measurement.EovfCount,
            measurement.MeasuredUpdatedAt);
}

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

    public List<QualitySignalWindow> Windows { get; } = [];

    public List<QualityPeriod> Asked { get; } = [];

    public Task<IReadOnlyList<SignalFigures>> FiguresAsync(QualityPeriod period, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(period);

        Asked.Add(period);

        return Task.FromResult<IReadOnlyList<SignalFigures>>([.. Figures]);
    }

    public Task<IReadOnlyList<QualitySignalWindow>> WindowsAsync(
        QualityTrendFrame frame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);

        return Task.FromResult<IReadOnlyList<QualitySignalWindow>>(
            [.. Windows.Where(window => frame.Period.Holds(window.Start))]);
    }

    public Task<IReadOnlyList<QualitySignalWindow>> WindowsAsync(
        QualityPeriod period,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(period);

        Asked.Add(period);

        return Task.FromResult<IReadOnlyList<QualitySignalWindow>>(
            [.. Windows.Where(window => period.Holds(window.Start))]);
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

    public Task<IReadOnlyDictionary<TunerDeviceId, DateTime>> ListLastTakenAsync(
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<TunerDeviceId, DateTime>>(
            Samples
                .GroupBy(sample => sample.Tuner)
                .ToDictionary(held => held.Key, held => held.Max(sample => sample.TakenAt)));

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

    public Task<IReadOnlyList<QualitySignalWindow>> ListFoldedAsync(
        QualityWindow granularity,
        DateTime from,
        DateTime until,
        TimeSpan step,
        DateTime grid,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<QualitySignalWindow>>(
        [
            .. Rollups
                .Where(rollup => rollup.Granularity == granularity
                                 && rollup.WindowStart >= from
                                 && rollup.WindowStart < until)
                .GroupBy(rollup => (
                    Start: Binned(rollup.WindowStart, step, grid),
                    Tuner: rollup.Tuner.Value,
                    Network: rollup.Network.Value,
                    Service: rollup.Service.Value))
                .OrderBy(group => group.Key.Start)
                .ThenBy(group => group.Key.Tuner, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Network)
                .ThenBy(group => group.Key.Service)
                .Select(group => new QualitySignalWindow(
                    group.Key.Start,
                    new TunerDeviceId(group.Key.Tuner),
                    new NetworkId(group.Key.Network),
                    new ServiceId(group.Key.Service),
                    group.Sum(rollup => rollup.Samples),
                    group.Sum(rollup => rollup.Locked),
                    group.Sum(rollup => rollup.Unmeasured),
                    group.Sum(rollup => rollup.Unreachable),
                    group.Min(rollup => rollup.CarrierToNoiseLowest),
                    [
                        .. group
                            .SelectMany(rollup => rollup.BitErrors)
                            .GroupBy(rate => rate.Layer)
                            .OrderBy(layer => layer.Key)
                            .Select(layer => new LayerErrorPeak(layer.Key, layer.Max(rate => rate.Highest))),
                    ],
                    [],
                    group
                        .Where(rollup => rollup.CarrierToNoiseLowest is not null || rollup.BitErrors.Count > 0)
                        .Max(rollup => (DateTime?)rollup.WindowStart))),
        ]);

    private static DateTime Binned(DateTime at, TimeSpan step, DateTime grid)
    {
        long offset = (((at.Ticks - grid.Ticks) % step.Ticks) + step.Ticks) % step.Ticks;

        return new DateTime(at.Ticks - offset, DateTimeKind.Utc);
    }
}
