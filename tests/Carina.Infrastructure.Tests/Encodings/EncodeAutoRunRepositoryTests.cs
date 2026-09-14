using Carina.Domain.Encodings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Configurations;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Infrastructure.Tests.Encodings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeAutoRunRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime Settled = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "a machine nobody has settled holds no row at all")]
    public async Task AMachineNobodyHasSettledHoldsNoRowAtAll()
    {
        await ClearAsync();

        await using CarinaDbContext reading = database.Open();

        Assert.Null(await new EncodeAutoRunRepository(reading).ReadAsync(Cancel));
    }

    [Fact(DisplayName = "what was settled is read back as it was written, and settling again keeps one row")]
    public async Task WhatWasSettledIsReadBackAndSettlingAgainKeepsOneRow()
    {
        await ClearAsync();

        await SaveAsync(EncodeAutoRun.Settled(false, 4, Settled));
        await SaveAsync(EncodeAutoRun.Settled(true, 1, Settled.AddHours(3)));

        await using CarinaDbContext reading = database.Open();
        EncodeAutoRun? held = await new EncodeAutoRunRepository(reading).ReadAsync(Cancel);

        Assert.NotNull(held);
        Assert.Equal(EncodeAutoRun.TheOnlyRow, held.Id);
        Assert.True(held.Automatically);
        Assert.Equal(1, held.MostCores);
        Assert.Equal(Settled.AddHours(3), held.UpdatedAt);
        Assert.Equal(1, await reading.Set<EncodeAutoRun>().CountAsync(Cancel));
    }

    [Fact(DisplayName = "the table itself refuses a second row and a cap the domain would not allow")]
    public async Task TheTableItselfRefusesASecondRowAndACapTheDomainWouldNotAllow()
    {
        await ClearAsync();
        await SaveAsync(EncodeAutoRun.Settled(true, 2, Settled));

        Assert.Equal(
            EncodeAutoRunConfiguration.SingleRowCheck,
            (await Assert.ThrowsAsync<PostgresException>(
                () => RunAsync($"INSERT INTO {EncodeAutoRunConfiguration.TableName} "
                    + "(id, automatically, most_cores, updated_at) VALUES (2, true, 2, now())"))).ConstraintName);

        Assert.Equal(
            EncodeAutoRunConfiguration.CoresCheck,
            (await Assert.ThrowsAsync<PostgresException>(
                () => RunAsync($"UPDATE {EncodeAutoRunConfiguration.TableName} SET most_cores = 0"))).ConstraintName);
    }

    [Fact(DisplayName = "hands settling it at once on a table that holds no row all get through, and one of them stands")]
    public async Task HandsSettlingItAtOnceAllGetThroughAndOneOfThemStands()
    {
        await ClearAsync();

        const int Hands = 8;
        using var gate = new Barrier(Hands);
        EncodeAutoRun[] settling =
        [
            .. Enumerable.Range(0, Hands).Select(hand => EncodeAutoRun.Settled(hand % 2 is 0, hand + 1, Settled.AddHours(hand))),
        ];

        await Task.WhenAll(settling.Select(autoRun => Task.Run(async () =>
        {
            await using CarinaDbContext writing = database.Open();
            await writing.Database.OpenConnectionAsync(Cancel);
            gate.SignalAndWait(Cancel);

            await new EncodeAutoRunRepository(writing).SaveAsync(autoRun, Cancel);
        })));

        await using CarinaDbContext reading = database.Open();
        EncodeAutoRun? held = await new EncodeAutoRunRepository(reading).ReadAsync(Cancel);

        Assert.NotNull(held);
        Assert.Equal(1, await reading.Set<EncodeAutoRun>().CountAsync(Cancel));
        Assert.Contains(
            (held.Automatically, held.MostCores, held.UpdatedAt),
            settling.Select(autoRun => (autoRun.Automatically, autoRun.MostCores, autoRun.UpdatedAt)));
    }

    private async Task SaveAsync(EncodeAutoRun autoRun)
    {
        await using CarinaDbContext writing = database.Open();
        await new EncodeAutoRunRepository(writing).SaveAsync(autoRun, Cancel);
    }

    private async Task RunAsync(string sql)
    {
        await using CarinaDbContext running = database.Open();
        await running.Database.ExecuteSqlRawAsync(sql, Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<EncodeAutoRun>().ExecuteDeleteAsync(Cancel);
    }
}
