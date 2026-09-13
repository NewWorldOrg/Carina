using System.Data.Common;

using Carina.Domain.Base;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class DatabaseBoundedReadTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = new(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TheConnectionCarriesTheTimeTheStatementWasGivenWhileTheReadRuns()
    {
        await using CarinaDbContext context = database.Open();
        var bounded = new DatabaseBoundedRead(context);

        string shown = await bounded.NoLongerThanAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken => Shown(context, cancellationToken),
            Cancel);

        Assert.Equal("30s", shown);
    }

    [Fact]
    public async Task TheTimeItWasGivenIsGoneAgainOnceTheReadIsOver()
    {
        await using CarinaDbContext context = database.Open();
        var bounded = new DatabaseBoundedRead(context);

        await bounded.NoLongerThanAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken => Shown(context, cancellationToken),
            Cancel);

        Assert.NotEqual("30s", await Shown(context, Cancel));
    }

    [Fact]
    public async Task AReadThatRunsPastTheTimeItWasGivenIsStopped()
    {
        await using CarinaDbContext context = database.Open();
        var bounded = new DatabaseBoundedRead(context);

        ReadTookTooLongException stopped = await Assert.ThrowsAsync<ReadTookTooLongException>(
            () => bounded.NoLongerThanAsync(
                TimeSpan.FromMilliseconds(200),
                cancellationToken => context.Database.ExecuteSqlRawAsync("SELECT pg_sleep(5)", cancellationToken),
                Cancel));

        Assert.Equal(TimeSpan.FromMilliseconds(200), stopped.Patience);
    }

    [Fact]
    public async Task TheConnectionIsUsableAgainAfterAReadWasStopped()
    {
        await using CarinaDbContext context = database.Open();
        var bounded = new DatabaseBoundedRead(context);

        await Assert.ThrowsAsync<ReadTookTooLongException>(
            () => bounded.NoLongerThanAsync(
                TimeSpan.FromMilliseconds(200),
                cancellationToken => context.Database.ExecuteSqlRawAsync("SELECT pg_sleep(5)", cancellationToken),
                Cancel));

        Assert.Equal(
            "30s",
            await bounded.NoLongerThanAsync(
                TimeSpan.FromSeconds(30),
                cancellationToken => Shown(context, cancellationToken),
                Cancel));
    }

    [Fact]
    public async Task WhatTheReadFoundIsWhatComesBack()
    {
        await using CarinaDbContext context = database.Open();

        Assert.Equal(
            7,
            await new DatabaseBoundedRead(context).NoLongerThanAsync(
                TimeSpan.FromSeconds(30),
                _ => Task.FromResult(7),
                Cancel));
    }

    [Fact]
    public async Task AReadGivenNoTimeAtAllIsRefusedBeforeTheStoreIsTouched()
    {
        await using CarinaDbContext context = database.Open();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => new DatabaseBoundedRead(context).NoLongerThanAsync(
                TimeSpan.Zero,
                _ => Task.FromResult(0),
                Cancel));
    }

    [Fact]
    public async Task AWriteInsideTheReadThatRanOutOfTimeIsStoppedTheSameWay()
    {
        await using CarinaDbContext context = database.Open(new SlowedInserts());

        ReadTookTooLongException stopped = await Assert.ThrowsAsync<ReadTookTooLongException>(
            () => new DatabaseBoundedRead(context).NoLongerThanAsync(
                TimeSpan.FromMilliseconds(200),
                cancellationToken => Begun(context, cancellationToken),
                Cancel));

        Assert.IsType<DbUpdateException>(stopped.InnerException);
        Assert.Equal(TimeSpan.FromMilliseconds(200), stopped.Patience);
    }

    [Fact]
    public async Task AReaderWhoWentAwayIsNotReadAsAStoreThatTookTooLong()
    {
        await using CarinaDbContext context = database.Open();
        using var goingAway = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new DatabaseBoundedRead(context).NoLongerThanAsync(
                TimeSpan.FromSeconds(30),
                cancellationToken => context.Database.ExecuteSqlRawAsync("SELECT pg_sleep(5)", cancellationToken),
                goingAway.Token));
    }

    private static async Task<int> Begun(CarinaDbContext context, CancellationToken cancellationToken)
    {
        await context.Set<CollectionEpoch>().AddAsync(CollectionEpoch.Begin(At), cancellationToken);

        return await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task<string> Shown(CarinaDbContext context, CancellationToken cancellationToken)
        => (await context.Database
            .SqlQueryRaw<string>("SHOW statement_timeout")
            .ToListAsync(cancellationToken))[0];

    private sealed class SlowedInserts : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Dawdle(command);

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Dawdle(command);

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static void Dawdle(DbCommand command)
        {
            if (command.CommandText.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase))
            {
                command.CommandText = "SELECT pg_sleep(5); " + command.CommandText;
            }
        }
    }
}
