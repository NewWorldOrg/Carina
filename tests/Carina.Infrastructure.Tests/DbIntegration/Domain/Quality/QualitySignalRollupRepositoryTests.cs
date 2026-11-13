using Carina.Domain.Channels;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Quality;

[Trait("Category", "DbIntegration")]
public sealed class QualitySignalRollupRepositoryTests(MigratedRepositoryDatabase database)
    : IClassFixture<MigratedRepositoryDatabase>, IAsyncLifetime
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime DayOne = new(2026, 9, 6, 19, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact(DisplayName = "a window comes back on the physical channel it was rolled up on, and loses it when it is saved again as taken on more than one")]
    public async Task AWindowComesBackOnThePhysicalChannelItWasRolledUpOn()
    {
        await ClearAsync();

        await SaveAsync([Rolled(Noon, samples: 6, locked: 6, channel: 27)]);

        Assert.Equal(27, Assert.Single(await WindowsAsync()).PhysicalChannel);

        await SaveAsync([Rolled(Noon, samples: 12, locked: 12)]);

        Assert.Null(Assert.Single(await WindowsAsync()).PhysicalChannel);
    }

    [Fact(DisplayName = "a window saved twice keeps one row and the latest counts")]
    public async Task AWindowSavedTwiceKeepsOneRowAndTheLatestCounts()
    {
        await ClearAsync();

        await SaveAsync([Rolled(Noon, samples: 6, locked: 6)]);
        await SaveAsync([Rolled(Noon, samples: 360, locked: 359)]);

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        IQualitySignalRollupRepository repository = Rollups(reading);

        QualitySignalRollup held = Assert.Single(
            await repository.ListAsync(QualityWindow.Hour, Noon.AddHours(-1), Noon.AddHours(1), Cancel));

        Assert.Equal(360, held.Samples);
        Assert.Equal(359, held.Locked);
        Assert.Equal(Noon, await repository.LatestWindowStartAsync(QualityWindow.Hour, Cancel));
        Assert.Null(await repository.LatestWindowStartAsync(QualityWindow.Minute, Cancel));
    }

    [Fact(DisplayName = "the windows of one layer are swept without touching the other's")]
    public async Task TheWindowsOfOneLayerAreSweptWithoutTouchingTheOthers()
    {
        await ClearAsync();

        await SaveAsync(
        [
            Rolled(Noon.AddDays(-100), samples: 6, locked: 6),
            Rolled(Noon.AddDays(-100), samples: 6, locked: 6, granularity: QualityWindow.Minute),
        ]);

        await using AsyncServiceScope sweeping = container.CreateAsyncScope();

        IQualitySignalRollupRepository repository = Rollups(sweeping);

        Assert.Equal(1, await repository.ForgetStartedBeforeAsync(QualityWindow.Minute, Noon.AddDays(-90), Cancel));
        Assert.Equal(
            Noon.AddDays(-100),
            await repository.LatestWindowStartAsync(QualityWindow.Hour, Cancel));
    }

    [Fact(DisplayName = "two days of hourly windows come back as one row a day, not one an hour")]
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

    [Fact(DisplayName = "the fold keeps the worst of each layer apart, and names the last window that carried a value")]
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

    private static IQualitySignalRollupRepository Rollups(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IQualitySignalRollupRepository>();

    private static QualitySignalRollup Rolled(
        DateTime windowStart,
        long samples,
        long locked,
        QualityWindow granularity = QualityWindow.Hour,
        int? channel = null)
        => QualitySignalRollup.Rehydrate(
            granularity,
            windowStart,
            new TunerDeviceId("adapter3.frontend0"),
            new NetworkId(32736),
            new ServiceId(1024),
            samples,
            locked,
            0,
            0,
            30000,
            30000,
            30000,
            [new LayerErrorRate(0, 0, 0)],
            channel);

    private static QualitySignalRollup Rollup(
        DateTime windowStart,
        string tuner = "adapter0",
        int service = 101,
        int? cnr = 30_000,
        IReadOnlyList<LayerErrorRate>? layers = null)
        => QualitySignalRollup.Rehydrate(
            QualityWindow.Hour,
            windowStart,
            new TunerDeviceId(tuner),
            new NetworkId(1),
            new ServiceId(service),
            360,
            360,
            0,
            0,
            cnr,
            cnr,
            cnr,
            cnr is null ? [] : layers ?? [new LayerErrorRate(0, 0, 0)]);

    private async Task SaveAsync(IReadOnlyList<QualitySignalRollup> rollups)
    {
        await using AsyncServiceScope writing = container.CreateAsyncScope();

        await Rollups(writing).SaveAsync(rollups, Cancel);
    }

    private async Task<IReadOnlyList<QualitySignalRollup>> WindowsAsync()
    {
        await using AsyncServiceScope reading = container.CreateAsyncScope();

        return await Rollups(reading).ListAsync(QualityWindow.Hour, Noon.AddHours(-1), Noon.AddHours(1), Cancel);
    }

    private async Task<IReadOnlyList<QualitySignalWindow>> FoldAsync(DateTime from, DateTime until, TimeSpan step)
    {
        await using AsyncServiceScope reading = container.CreateAsyncScope();

        return await Rollups(reading).ListFoldedAsync(QualityWindow.Hour, from, until, step, QualityTrendFrame.Grid, Cancel);
    }

    private async Task ClearAsync()
    {
        await using AsyncServiceScope clearing = container.CreateAsyncScope();
        await clearing.ServiceProvider.GetRequiredService<CarinaDbContext>()
            .Set<QualitySignalRollup>()
            .ExecuteDeleteAsync(Cancel);
    }
}
