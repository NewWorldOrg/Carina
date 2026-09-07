using Carina.Domain.Quality;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

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
