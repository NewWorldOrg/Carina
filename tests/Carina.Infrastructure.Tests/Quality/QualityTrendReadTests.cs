using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Quality;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Quality;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class QualityTrendReadTests(RepositoryDatabase database)
{
    private static readonly DateTime DayOne = new(2026, 9, 6, 19, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-QV-001: two days of hourly windows come back as one row a day, not one an hour")]
    public async Task TwoDaysOfHourlyWindowsComeBackAsOneRowADay()
    {
        await ClearAsync();
        await SaveAsync([.. Enumerable.Range(0, 48).Select(hour => Rollup(DayOne.AddHours(hour), cnr: 30_000 - hour))]);

        IReadOnlyList<QualitySignalWindow> folded = await FoldAsync(DayOne, DayOne.AddDays(2), TimeSpan.FromDays(1));

        Assert.Equal([DayOne, DayOne.AddDays(1)], folded.Select(window => window.Start));
        Assert.All(folded, window => Assert.Equal(24 * 360, window.Samples));
        Assert.Equal(30_000 - 23, folded[0].CarrierToNoiseLowest);
        Assert.Equal(30_000 - 47, folded[1].CarrierToNoiseLowest);
    }

    [Fact(DisplayName = "BR-QD-009: the fold keeps the worst of each layer apart, and names the last window that carried a value")]
    public async Task TheFoldKeepsTheWorstOfEachLayerApart()
    {
        await ClearAsync();
        await SaveAsync(
        [
            Rollup(DayOne, layers: [new LayerErrorRate(0, 0.00001, 0.00002), new LayerErrorRate(1, 0.0001, 0.0004)]),
            Rollup(DayOne.AddHours(1), layers: [new LayerErrorRate(1, 0.0001, 0.0002)]),
            Rollup(DayOne.AddHours(2), cnr: null),
        ]);

        QualitySignalWindow window = Assert.Single(await FoldAsync(DayOne, DayOne.AddDays(1), TimeSpan.FromDays(1)));

        Assert.Equal([new LayerErrorPeak(0, 0.00002), new LayerErrorPeak(1, 0.0004)], window.BitErrors);
        Assert.Equal(DayOne.AddHours(1), window.LastCarriedAt);
        Assert.Equal(3 * 360, window.Samples);
    }

    [Fact]
    public async Task TunersAndMultiplexesAreFoldedApart()
    {
        await ClearAsync();
        await SaveAsync(
        [
            Rollup(DayOne),
            Rollup(DayOne, tuner: "adapter1"),
            Rollup(DayOne, service: 102),
            Rollup(DayOne.AddHours(1), service: 102),
        ]);

        IReadOnlyList<QualitySignalWindow> folded = await FoldAsync(DayOne, DayOne.AddDays(1), TimeSpan.FromDays(1));

        Assert.Equal(3, folded.Count);
        Assert.Equal(
            2 * 360,
            folded.Single(window => window.Tuner.Value == "adapter0" && window.Service.Value == 102).Samples);
    }

    [Fact(DisplayName = "BR-QS-003: the reader takes raw samples only past the last hour rolled up, so none is counted twice")]
    public async Task TheReaderTakesRawSamplesOnlyPastTheLastHourRolledUp()
    {
        DateTime rolledThrough = DayOne.AddDays(1);
        QualityTrendFrame frame = QualityTrendFrame.Over(1, rolledThrough.AddMinutes(30), QualityTrendStep.Hour)!;

        await ClearAsync();
        await SaveAsync([.. Enumerable.Range(0, 24).Select(hour => Rollup(frame.Period.From.AddHours(hour), samples: 6, locked: 6))]);
        await AddAsync(
        [
            Sample(rolledThrough.AddMinutes(-30)),
            Sample(rolledThrough.AddMinutes(5)),
            Sample(rolledThrough.AddMinutes(15)),
        ]);

        await using CarinaDbContext reading = database.Open();

        IReadOnlyList<QualitySignalWindow> windows = await new QualitySignalReader(
                new QualitySignalRollupRepository(reading),
                new QualitySignalSampleRepository(reading))
            .WindowsAsync(frame, Cancel);

        Assert.Equal(24, windows.Count(window => window.Samples == 6));
        Assert.Equal(
            [rolledThrough.AddMinutes(5), rolledThrough.AddMinutes(15)],
            windows.Where(window => window.Samples == 1).Select(window => window.Start));
    }

    private static QualitySignalRollup Rollup(
        DateTime windowStart,
        string tuner = "adapter0",
        int service = 101,
        long samples = 360,
        long locked = 360,
        int? cnr = 30_000,
        IReadOnlyList<LayerErrorRate>? layers = null)
        => QualitySignalRollup.Rehydrate(
            QualityWindow.Hour,
            windowStart,
            new TunerDeviceId(tuner),
            new NetworkId(1),
            new ServiceId(service),
            samples,
            locked,
            0,
            0,
            cnr,
            cnr,
            cnr,
            cnr is null ? [] : layers ?? [new LayerErrorRate(0, 0, 0)]);

    private static QualitySignalSample Sample(DateTime at)
        => QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse("live-1"),
            at,
            SessionPurpose.Live,
            new TunerDeviceId("adapter0"),
            new NetworkId(1),
            new ServiceId(101),
            SignalSample.WithLock(at, 30_000, at));

    private async Task<IReadOnlyList<QualitySignalWindow>> FoldAsync(DateTime from, DateTime until, TimeSpan step)
    {
        await using CarinaDbContext reading = database.Open();

        return await new QualitySignalRollupRepository(reading)
            .ListFoldedAsync(QualityWindow.Hour, from, until, step, QualityTrendFrame.Grid, Cancel);
    }

    private async Task AddAsync(IReadOnlyList<QualitySignalSample> samples)
    {
        await using CarinaDbContext writing = database.Open();

        await new QualitySignalSampleRepository(writing).AddAsync(samples, Cancel);
    }

    private async Task SaveAsync(IReadOnlyList<QualitySignalRollup> rollups)
    {
        await using CarinaDbContext writing = database.Open();

        await new QualitySignalRollupRepository(writing).SaveAsync(rollups, Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<QualitySignalSample>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<QualitySignalRollup>().ExecuteDeleteAsync(Cancel);
    }
}
