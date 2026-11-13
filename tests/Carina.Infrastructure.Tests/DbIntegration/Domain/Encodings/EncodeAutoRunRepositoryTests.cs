using Carina.Domain.Encodings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Encodings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeAutoRunRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Settled = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact(DisplayName = "a machine nobody has settled holds no row at all")]
    public async Task AMachineNobodyHasSettledHoldsNoRowAtAll()
    {
        await ClearAsync();

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Null(await AutoRuns(reading).ReadAsync(Cancel));
    }

    [Fact(DisplayName = "what was settled is read back as it was written, and settling again keeps one row")]
    public async Task WhatWasSettledIsReadBackAndSettlingAgainKeepsOneRow()
    {
        await ClearAsync();

        await SaveAsync(EncodeAutoRun.Settled(false, 4, Settled));
        await SaveAsync(EncodeAutoRun.Settled(true, 1, Settled.AddHours(3)));

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeAutoRun? held = await AutoRuns(reading).ReadAsync(Cancel);

        Assert.NotNull(held);
        Assert.Equal(EncodeAutoRun.TheOnlyRow, held.Id);
        Assert.True(held.Automatically);
        Assert.Equal(1, held.MostCores);
        Assert.Equal(Settled.AddHours(3), held.UpdatedAt);
        Assert.Equal(1, await Context(reading).Set<EncodeAutoRun>().CountAsync(Cancel));
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
            await using AsyncServiceScope writing = container.CreateAsyncScope();
            await Context(writing).Database.OpenConnectionAsync(Cancel);
            gate.SignalAndWait(Cancel);

            await AutoRuns(writing).SaveAsync(autoRun, Cancel);
        })));

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeAutoRun? held = await AutoRuns(reading).ReadAsync(Cancel);

        Assert.NotNull(held);
        Assert.Equal(1, await Context(reading).Set<EncodeAutoRun>().CountAsync(Cancel));
        Assert.Contains(
            (held.Automatically, held.MostCores, held.UpdatedAt),
            settling.Select(autoRun => (autoRun.Automatically, autoRun.MostCores, autoRun.UpdatedAt)));
    }

    private async Task SaveAsync(EncodeAutoRun autoRun)
    {
        await using AsyncServiceScope writing = container.CreateAsyncScope();
        await AutoRuns(writing).SaveAsync(autoRun, Cancel);
    }

    private async Task RunAsync(string sql)
    {
        await using AsyncServiceScope running = container.CreateAsyncScope();
        await Context(running).Database.ExecuteSqlRawAsync(sql, Cancel);
    }

    private async Task ClearAsync()
    {
        await using AsyncServiceScope clearing = container.CreateAsyncScope();
        await Context(clearing).Set<EncodeAutoRun>().ExecuteDeleteAsync(Cancel);
    }

    private static IEncodeAutoRunRepository AutoRuns(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeAutoRunRepository>();

    private static CarinaDbContext Context(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
}
