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
public sealed class QualitySignalStoreTests(RepositoryDatabase database)
{
    private static readonly DateTime Noon = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-QD-004: a sample comes back with every time and every layer it went in with")]
    public async Task ASampleComesBackWithEveryTimeAndEveryLayerItWentInWith()
    {
        await ClearAsync();

        await AddAsync(
        [
            Sample(
                Noon,
                SignalSample.WithLock(
                    Noon,
                    34779,
                    Noon.AddSeconds(-1),
                    [new LayerBitErrorCounts(0, 0, 1671168), new LayerBitErrorCounts(1, 2, 67682304)],
                    Noon.AddSeconds(-1))),
        ]);

        await using CarinaDbContext reading = database.Open();

        QualitySignalSample held = Assert.Single(
            await new QualitySignalSampleRepository(reading)
                .ListTakenBetweenAsync(Noon.AddHours(-1), Noon.AddHours(1), Cancel));

        Assert.Equal(Noon, held.Signal.LockReadAt);
        Assert.Equal(Noon.AddSeconds(-1), held.Signal.CarrierToNoiseReadAt);
        Assert.Equal(34779, held.Signal.CarrierToNoiseMilliDecibels);
        Assert.Equal([0, 1], held.Signal.BitErrors.Select(counts => counts.Layer));
        Assert.True(held.Signal.WasTaken);
    }

    [Fact(DisplayName = "BR-QV-003: a reading that could not be taken comes back saying which way it could not be")]
    public async Task AReadingThatCouldNotBeTakenComesBackSayingWhichWayItCouldNotBe()
    {
        await ClearAsync();

        await AddAsync([Sample(Noon, SignalSample.NotTaken(Noon, SignalNotTaken.NoTimeGiven))]);

        await using CarinaDbContext reading = database.Open();

        QualitySignalSample held = Assert.Single(
            await new QualitySignalSampleRepository(reading)
                .ListTakenBetweenAsync(Noon.AddHours(-1), Noon.AddHours(1), Cancel));

        Assert.False(held.Signal.WasTaken);
        Assert.Equal(SignalNotTaken.NoTimeGiven, held.Signal.NotTakenBecause);
    }

    [Fact(DisplayName = "BR-QS-003: a sweep lets go only of the samples taken before its cutoff")]
    public async Task ASweepLetsGoOnlyOfTheSamplesTakenBeforeItsCutoff()
    {
        await ClearAsync();

        await AddAsync(
        [
            Sample(Noon.AddDays(-8), SignalSample.WithoutLock(Noon.AddDays(-8)), "live-1"),
            Sample(Noon, SignalSample.WithoutLock(Noon), "live-2"),
        ]);

        await using CarinaDbContext sweeping = database.Open();

        var repository = new QualitySignalSampleRepository(sweeping);

        Assert.Equal(1, await repository.ForgetTakenBeforeAsync(Noon.AddDays(-7), Cancel));
        Assert.Single(await repository.ListTakenBetweenAsync(Noon.AddDays(-30), Noon.AddDays(1), Cancel));
    }

    [Fact(DisplayName = "BR-QD-006: a window saved twice keeps one row and the latest counts")]
    public async Task AWindowSavedTwiceKeepsOneRowAndTheLatestCounts()
    {
        await ClearAsync();

        await SaveAsync([Rollup(Noon, samples: 6, locked: 6)]);
        await SaveAsync([Rollup(Noon, samples: 360, locked: 359)]);

        await using CarinaDbContext reading = database.Open();

        var repository = new QualitySignalRollupRepository(reading);

        QualitySignalRollup held = Assert.Single(
            await repository.ListAsync(QualityWindow.Hour, Noon.AddHours(-1), Noon.AddHours(1), Cancel));

        Assert.Equal(360, held.Samples);
        Assert.Equal(359, held.Locked);
        Assert.Equal(Noon, await repository.LatestWindowStartAsync(QualityWindow.Hour, Cancel));
        Assert.Null(await repository.LatestWindowStartAsync(QualityWindow.Minute, Cancel));
    }

    [Fact(DisplayName = "BR-QD-006: the windows of one layer are swept without touching the other's")]
    public async Task TheWindowsOfOneLayerAreSweptWithoutTouchingTheOthers()
    {
        await ClearAsync();

        await SaveAsync(
        [
            Rollup(Noon.AddDays(-100), samples: 6, locked: 6),
            Rollup(Noon.AddDays(-100), samples: 6, locked: 6, granularity: QualityWindow.Minute),
        ]);

        await using CarinaDbContext sweeping = database.Open();

        var repository = new QualitySignalRollupRepository(sweeping);

        Assert.Equal(1, await repository.ForgetStartedBeforeAsync(QualityWindow.Minute, Noon.AddDays(-90), Cancel));
        Assert.Equal(
            Noon.AddDays(-100),
            await repository.LatestWindowStartAsync(QualityWindow.Hour, Cancel));
    }

    [Fact(DisplayName = "BR-QS-003: what the reader answers with is the windows first and the samples they have not reached")]
    public async Task WhatTheReaderAnswersWithIsTheWindowsFirstAndTheSamplesTheyHaveNotReached()
    {
        await ClearAsync();

        await SaveAsync([Rollup(Noon.AddHours(-2), samples: 360, locked: 360)]);
        await AddAsync(
        [
            Sample(Noon.AddHours(-3), SignalSample.WithoutLock(Noon.AddHours(-3)), "live-0"),
            Sample(Noon.AddMinutes(-30), SignalSample.WithLock(Noon.AddMinutes(-30), 12000, Noon.AddMinutes(-30)), "live-2"),
        ]);

        await using CarinaDbContext reading = database.Open();

        IReadOnlyList<SignalFigures> figures = await new QualitySignalReader(
                new QualitySignalRollupRepository(reading),
                new QualitySignalSampleRepository(reading))
            .FiguresAsync(QualityPeriod.Of(Noon.AddDays(-1), Noon, Noon)!, Cancel);

        SignalFigures figure = Assert.Single(figures);

        Assert.Equal(361, figure.Samples);
        Assert.Equal(12000, figure.CarrierToNoiseLowest);
    }

    private static QualitySignalSample Sample(DateTime at, SignalSample signal, string session = "live-1")
        => QualitySignalSample.Rehydrate(
            "instance-a",
            SessionId.Parse(session),
            at,
            SessionPurpose.Live,
            new TunerDeviceId("adapter3.frontend0"),
            new NetworkId(32736),
            new ServiceId(1024),
            signal);

    private static QualitySignalRollup Rollup(
        DateTime windowStart,
        long samples,
        long locked,
        QualityWindow granularity = QualityWindow.Hour)
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
            34779,
            34779,
            34779,
            [new LayerErrorRate(0, 0, 0)]);

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
