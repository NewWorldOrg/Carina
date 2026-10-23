using Carina.Domain.DataBroadcast;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Configurations;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

namespace Carina.Infrastructure.Tests.DataBroadcast;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class DataBroadcastSettingsRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime Noon = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-BD-016: a database where nobody has registered where the receiver is holds no row at all")]
    public async Task ADatabaseNobodyHasChangedHoldsNoRow()
    {
        await ClearAsync();

        Assert.Null(await ReadAsync());
    }

    [Fact(DisplayName = "BR-BD-016: the postal code and the prefecture are read back as they were written, none included, and writing them again keeps one row")]
    public async Task WhatIsWrittenIsReadBackAndWritingAgainKeepsOneRow()
    {
        await ClearAsync();

        await SaveAsync(DataBroadcastSettingsStanding.Unset.Set("0000000", 0, Noon)!);
        DataBroadcastSettings? first = await ReadAsync();
        await SaveAsync(DataBroadcastSettingsStanding.Over(first).Set(null, 49, Noon.AddHours(1))!);
        DataBroadcastSettings? second = await ReadAsync();
        await SaveAsync(DataBroadcastSettingsStanding.Over(second).Set("9990000", null, Noon.AddHours(2))!);

        await using CarinaDbContext reading = database.Open();
        DataBroadcastSettings? held = await new DataBroadcastSettingsRepository(reading).ReadAsync(Cancel);

        Assert.Equal(("0000000", (int?)0, Noon), (first!.PostalCode, first.Prefecture, first.ChangedAt));
        Assert.Equal(((string?)null, (int?)49), (second!.PostalCode, second.Prefecture));
        Assert.Equal(
            (DataBroadcastSettings.TheOnlyRow, "9990000", (int?)null, Noon.AddHours(2)),
            (held!.Id, held.PostalCode, held.Prefecture, held.ChangedAt));
        Assert.Equal(1, await reading.Set<DataBroadcastSettings>().CountAsync(Cancel));
    }

    [Fact(DisplayName = "BR-BD-016: a change of the settings holds its turn until its transaction ends, so a change made at the same time reads it")]
    public async Task AChangeHoldsItsTurnSoTheNextReadsIt()
    {
        await ClearAsync();
        await using CarinaDbContext first = database.Open();
        await using CarinaDbContext second = database.Open();
        await using IDbContextTransaction holding = await first.Database.BeginTransactionAsync(Cancel);
        DataBroadcastSettingsRepository earlier = new(first);
        await earlier.TakeTurnAsync(Cancel);
        DataBroadcastSettingsStanding before = DataBroadcastSettingsStanding.Over(await earlier.ReadAsync(Cancel));
        string? seenByTheLater = null;

        Task moving = Task.Run(
            async () =>
            {
                await using IDbContextTransaction waiting = await second.Database.BeginTransactionAsync(Cancel);
                DataBroadcastSettingsRepository later = new(second);
                await later.TakeTurnAsync(Cancel);
                DataBroadcastSettingsStanding seen = DataBroadcastSettingsStanding.Over(await later.ReadAsync(Cancel));
                seenByTheLater = seen.PostalCode;
                await later.SaveAsync(seen.Set(seen.PostalCode, 13, Noon.AddMinutes(1))!, Cancel);
                await waiting.CommitAsync(Cancel);
            },
            Cancel);

        Assert.NotSame(moving, await Task.WhenAny(moving, Task.Delay(TimeSpan.FromMilliseconds(300), Cancel)));

        await earlier.SaveAsync(before.Set("0000000", null, Noon)!, Cancel);
        await holding.CommitAsync(Cancel);
        await moving.WaitAsync(TimeSpan.FromSeconds(10), Cancel);

        DataBroadcastSettings? held = await ReadAsync();
        Assert.Equal("0000000", seenByTheLater);
        Assert.Equal(("0000000", (int?)13), (held!.PostalCode, held.Prefecture));
    }

    [Theory(DisplayName = "BR-BD-016: the table itself refuses a second row, a postal code that is not seven digits and a prefecture outside 0 to 49")]
    [InlineData("(2, NULL, NULL, now())", DataBroadcastSettingsConfiguration.SingleRowCheck)]
    [InlineData("(1, '000000', NULL, now())", DataBroadcastSettingsConfiguration.PostalCodeCheck)]
    [InlineData("(1, '000-000', NULL, now())", DataBroadcastSettingsConfiguration.PostalCodeCheck)]
    [InlineData("(1, '', NULL, now())", DataBroadcastSettingsConfiguration.PostalCodeCheck)]
    [InlineData("(1, NULL, 50, now())", DataBroadcastSettingsConfiguration.PrefectureCheck)]
    [InlineData("(1, NULL, -1, now())", DataBroadcastSettingsConfiguration.PrefectureCheck)]
    public async Task TheTableItselfRefusesWhatItCannotHold(string values, string check)
    {
        await ClearAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => RunAsync($"INSERT INTO {DataBroadcastSettingsConfiguration.TableName} (id, postal_code, prefecture, changed_at) VALUES {values}"));

        Assert.Equal((PostgresErrorCodes.CheckViolation, check), (refusal.SqlState, refusal.ConstraintName));
    }

    private async Task SaveAsync(DataBroadcastSettings settings)
    {
        await using CarinaDbContext writing = database.Open();
        await new DataBroadcastSettingsRepository(writing).SaveAsync(settings, Cancel);
    }

    private async Task<DataBroadcastSettings?> ReadAsync()
    {
        await using CarinaDbContext reading = database.Open();

        return await new DataBroadcastSettingsRepository(reading).ReadAsync(Cancel);
    }

    private async Task RunAsync(string sql)
    {
        await using CarinaDbContext running = database.Open();
        await running.Database.ExecuteSqlRawAsync(sql, Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<DataBroadcastSettings>().ExecuteDeleteAsync(Cancel);
    }
}
