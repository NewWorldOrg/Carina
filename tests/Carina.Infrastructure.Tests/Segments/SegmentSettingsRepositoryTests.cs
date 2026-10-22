using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Configurations;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

namespace Carina.Infrastructure.Tests.Segments;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SegmentSettingsRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime Noon = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "a database where nobody has changed the threshold holds no row at all")]
    public async Task ADatabaseNobodyHasChangedHoldsNoRow()
    {
        await ClearAsync();

        await using CarinaDbContext reading = database.Open();

        Assert.Null(await new SegmentSettingsRepository(reading).ReadAsync(Cancel));
    }

    [Fact(DisplayName = "BR-CV-002: a threshold is read back as it was written, one never changed included, and setting it again keeps one row")]
    public async Task AThresholdIsReadBackAndSettingItAgainKeepsOneRow()
    {
        await ClearAsync();

        await SaveAsync(SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, 0.70, null));
        SegmentSettings? unchanged = await ReadAsync();
        await SaveAsync(SegmentSettingsStanding.Over(unchanged).Set(0.65, Noon)!);
        await SaveAsync(SegmentSettingsStanding.Over(await ReadAsync()).Set(0.8, Noon.AddHours(3))!);

        await using CarinaDbContext reading = database.Open();
        SegmentSettings? held = await new SegmentSettingsRepository(reading).ReadAsync(Cancel);

        Assert.Equal((0.70, (DateTime?)null), (unchanged!.SkipThreshold, unchanged.SkipThresholdChangedAt));
        Assert.Equal((SegmentSettings.TheOnlyRow, 0.8, (DateTime?)Noon.AddHours(3)), (held!.Id, held.SkipThreshold, held.SkipThresholdChangedAt));
        Assert.Equal(1, await reading.Set<SegmentSettings>().CountAsync(Cancel));
    }

    [Fact(DisplayName = "BR-CV-002: a change of the settings holds its turn until its transaction ends, so a change made at the same time reads it")]
    public async Task AChangeHoldsItsTurnSoTheNextReadsIt()
    {
        await ClearAsync();
        await using CarinaDbContext first = database.Open();
        await using CarinaDbContext second = database.Open();
        await using IDbContextTransaction holding = await first.Database.BeginTransactionAsync(Cancel);
        SegmentSettingsRepository earlier = new(first);
        await earlier.TakeTurnAsync(Cancel);
        SegmentSettingsStanding before = SegmentSettingsStanding.Over(await earlier.ReadAsync(Cancel));
        double? seenByTheLater = null;

        Task moving = Task.Run(
            async () =>
            {
                await using IDbContextTransaction waiting = await second.Database.BeginTransactionAsync(Cancel);
                SegmentSettingsRepository later = new(second);
                await later.TakeTurnAsync(Cancel);
                SegmentSettingsStanding seen = SegmentSettingsStanding.Over(await later.ReadAsync(Cancel));
                seenByTheLater = seen.SkipThreshold;
                await later.SaveAsync(seen.Set(0.8, Noon.AddMinutes(1))!, Cancel);
                await waiting.CommitAsync(Cancel);
            },
            Cancel);

        Assert.NotSame(moving, await Task.WhenAny(moving, Task.Delay(TimeSpan.FromMilliseconds(300), Cancel)));

        await earlier.SaveAsync(before.Set(0.65, Noon)!, Cancel);
        await holding.CommitAsync(Cancel);
        await moving.WaitAsync(TimeSpan.FromSeconds(10), Cancel);

        SegmentSettings? held = await ReadAsync();
        Assert.Equal(0.65, seenByTheLater);
        Assert.Equal((0.8, (DateTime?)Noon.AddMinutes(1)), (held!.SkipThreshold, held.SkipThresholdChangedAt));
    }

    [Fact(DisplayName = "the table itself refuses a second row")]
    public async Task TheTableItselfRefusesASecondRow()
    {
        await ClearAsync();
        await SaveAsync(SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, 0.65, Noon));

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => RunAsync($"INSERT INTO {SegmentSettingsConfiguration.TableName} (id, skip_threshold) VALUES (2, 0.70)"));

        Assert.Equal(SegmentSettingsConfiguration.SingleRowCheck, refusal.ConstraintName);
    }

    [Fact(DisplayName = "hands setting the threshold at once on a table that holds no row all get through, and one of them stands")]
    public async Task HandsSettingAtOnceAllGetThroughAndOneOfThemStands()
    {
        await ClearAsync();

        const int Hands = 8;
        using var gate = new Barrier(Hands);
        SegmentSettings[] setting =
        [
            .. Enumerable.Range(0, Hands).Select(hand => SegmentSettings.Rehydrate(SegmentSettings.TheOnlyRow, 0.50 + (0.05 * hand), Noon.AddMinutes(hand))),
        ];

        await Task.WhenAll(setting.Select(settings => Task.Run(async () =>
        {
            await using CarinaDbContext writing = database.Open();
            await writing.Database.OpenConnectionAsync(Cancel);
            gate.SignalAndWait(Cancel);

            await new SegmentSettingsRepository(writing).SaveAsync(settings, Cancel);
        })));

        await using CarinaDbContext reading = database.Open();
        SegmentSettings? held = await new SegmentSettingsRepository(reading).ReadAsync(Cancel);

        Assert.NotNull(held);
        Assert.Equal(1, await reading.Set<SegmentSettings>().CountAsync(Cancel));
        Assert.Contains(
            (Math.Round(held.SkipThreshold, 2), held.SkipThresholdChangedAt),
            setting.Select(settings => (Math.Round(settings.SkipThreshold, 2), settings.SkipThresholdChangedAt)));
    }

    private async Task SaveAsync(SegmentSettings settings)
    {
        await using CarinaDbContext writing = database.Open();
        await new SegmentSettingsRepository(writing).SaveAsync(settings, Cancel);
    }

    private async Task<SegmentSettings?> ReadAsync()
    {
        await using CarinaDbContext reading = database.Open();

        return await new SegmentSettingsRepository(reading).ReadAsync(Cancel);
    }

    private async Task RunAsync(string sql)
    {
        await using CarinaDbContext running = database.Open();
        await running.Database.ExecuteSqlRawAsync(sql, Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<SegmentSettings>().ExecuteDeleteAsync(Cancel);
    }
}
