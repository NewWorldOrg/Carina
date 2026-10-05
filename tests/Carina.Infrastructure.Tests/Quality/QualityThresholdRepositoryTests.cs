using Carina.Domain.Quality;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Carina.Infrastructure.Tests.Quality;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class QualityThresholdRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime At = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ALevelSavedTwiceKeepsOneRowAndTheLatestValue()
    {
        await ClearAsync();

        await SaveAsync(QualityThresholdKey.PacketsLostWarning, 0.0002);
        await SaveAsync(QualityThresholdKey.PacketsLostWarning, 0.0005);

        await using CarinaDbContext reading = database.Open();
        var repository = new QualityThresholdRepository(reading);

        QualityThreshold held = Assert.Single(await repository.ListAsync(Cancel));

        Assert.Equal(0.0005, held.Setting.Current);
        Assert.Equal(0.0002, held.Setting.Default);
        Assert.NotNull(await repository.FindAsync(QualityThresholdKey.PacketsLostWarning, Cancel));
        Assert.Null(await repository.FindAsync(QualityThresholdKey.LockRate, Cancel));
    }

    [Fact(DisplayName = "every change is kept, newest first, so the latest under a key is the first one there")]
    public async Task EveryChangeIsKeptNewestFirst()
    {
        await ClearAsync();

        await using (CarinaDbContext writing = database.Open())
        {
            var history = new QualityThresholdChangeRepository(writing);
            await history.AddAsync(Change(QualityThresholdKey.PacketsLostWarning, 0.0002, 0.0005, At), Cancel);
            await history.AddAsync(Change(QualityThresholdKey.PacketsLostWarning, 0.0005, 0.0009, At.AddHours(1)), Cancel);
            await history.AddAsync(Change(QualityThresholdKey.LockRate, 0.99, 0.95, At.AddMinutes(30)), Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        var repository = new QualityThresholdChangeRepository(reading);

        IReadOnlyList<QualityThresholdChange> all = await repository.ListAsync(Cancel);
        QualityThresholdChange latest = all.First(change => change.Key == QualityThresholdKey.PacketsLostWarning);

        Assert.Equal(3, all.Count);
        Assert.Equal(At.AddHours(1), all[0].ChangedAt);
        Assert.Equal(0.0005, latest.PreviousValue);
        Assert.Equal(0.0009, latest.NextValue);
        Assert.DoesNotContain(all, change => change.Key == QualityThresholdKey.Overflows);
    }

    [Fact(DisplayName = "a level read and then written in the same breath is written, not refused for being read")]
    public async Task ALevelReadAndThenWrittenInTheSameBreathIsWritten()
    {
        await ClearAsync();
        await SaveAsync(QualityThresholdKey.PacketsLostWarning, 0.0002);

        await using CarinaDbContext both = database.Open();
        var repository = new QualityThresholdRepository(both);

        await repository.ListAsync(Cancel);

        await repository.SaveAsync(
            QualityThreshold.Rehydrate(
                QualityThresholdKey.PacketsLostWarning,
                Threshold.Of(0.0002, 0.0007, provisional: true, 0, At),
                null),
            Cancel);

        await using CarinaDbContext reading = database.Open();

        QualityThreshold held = Assert.Single(await new QualityThresholdRepository(reading).ListAsync(Cancel));

        Assert.Equal(0.0007, held.Setting.Current);
    }

    [Fact(DisplayName = "BR-QD-023: a measured level keeps the measurement it stands on, and a level set by hand says so")]
    public async Task AMeasuredLevelKeepsTheMeasurementItStandsOn()
    {
        await ClearAsync();

        QualityThresholdMeasurement measurement =
            QualityThresholdMeasurement.Of(19_500, 240, 18, At.AddDays(-7), At, At.AddMinutes(5));

        await using (CarinaDbContext writing = database.Open())
        {
            var repository = new QualityThresholdRepository(writing);

            await repository.SaveAsync(
                QualityThreshold.Rehydrate(
                    QualityThresholdKey.CarrierToNoiseFloor,
                    Threshold.Of(15_000, 19_500, provisional: false, 240, At),
                    null,
                    byHand: false,
                    measurement),
                Cancel);
            await repository.SaveAsync(
                QualityThreshold.Rehydrate(
                    QualityThresholdKey.BitErrorRateCeiling,
                    Threshold.Of(0.0001, 0.003, provisional: true, 0, At),
                    null,
                    byHand: true,
                    null),
                Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<QualityThreshold> held = await new QualityThresholdRepository(reading).ListAsync(Cancel);
        QualityThreshold measured = held.Single(threshold => threshold.Key == QualityThresholdKey.CarrierToNoiseFloor);
        QualityThreshold byHand = held.Single(threshold => threshold.Key == QualityThresholdKey.BitErrorRateCeiling);

        Assert.Equal(measurement, measured.Measurement);
        Assert.False(measured.ByHand);
        Assert.False(measured.Setting.Provisional);
        Assert.True(byHand.ByHand);
        Assert.Null(byHand.Measurement);
    }

    [Fact(DisplayName = "BR-QV-002: a change keeps whether a hand or a measurement made it")]
    public async Task AChangeKeepsWhetherAHandOrAMeasurementMadeIt()
    {
        await ClearAsync();

        await using (CarinaDbContext writing = database.Open())
        {
            await new QualityThresholdChangeRepository(writing).AddAsync(
                QualityThresholdChange.Record(
                    QualityThresholdChangeId.New(),
                    QualityThresholdKey.CarrierToNoiseFloor,
                    15_000,
                    19_500,
                    At,
                    null,
                    QualityThresholdChangeCause.Measurement),
                Cancel);
        }

        await using CarinaDbContext reading = database.Open();

        Assert.Equal(
            QualityThresholdChangeCause.Measurement,
            Assert.Single(await new QualityThresholdChangeRepository(reading).ListAsync(Cancel)).Cause);
    }

    [Fact(DisplayName = "BR-QD-023: a writer of the levels holds its turn until its transaction ends, and the next one waits for it")]
    public async Task AWriterOfTheLevelsHoldsItsTurnUntilItsTransactionEnds()
    {
        await using CarinaDbContext first = database.Open();
        await using CarinaDbContext second = database.Open();
        await using IDbContextTransaction holding =
            await first.Database.BeginTransactionAsync(Cancel);

        await new QualityThresholdRepository(first).TakeTurnAsync(Cancel);

        await using IDbContextTransaction waiting =
            await second.Database.BeginTransactionAsync(Cancel);
        Task next = new QualityThresholdRepository(second).TakeTurnAsync(Cancel);

        Assert.NotSame(next, await Task.WhenAny(next, Task.Delay(TimeSpan.FromMilliseconds(300), Cancel)));

        await holding.CommitAsync(Cancel);
        await next.WaitAsync(TimeSpan.FromSeconds(10), Cancel);
        await waiting.CommitAsync(Cancel);
    }

    private static QualityThresholdChange Change(QualityThresholdKey key, double previous, double next, DateTime at)
        => QualityThresholdChange.Record(QualityThresholdChangeId.New(), key, previous, next, at, null);

    private async Task SaveAsync(QualityThresholdKey key, double current)
    {
        await using CarinaDbContext writing = database.Open();

        await new QualityThresholdRepository(writing).SaveAsync(
            QualityThreshold.Rehydrate(key, Threshold.Of(0.0002, current, provisional: true, 0, At), null),
            Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<QualityThreshold>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<QualityThresholdChange>().ExecuteDeleteAsync(Cancel);
    }
}
