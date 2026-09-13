using Carina.Domain.Base;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class DatabaseBoundedReadTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

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

    private static async Task<string> Shown(CarinaDbContext context, CancellationToken cancellationToken)
        => (await context.Database
            .SqlQueryRaw<string>("SHOW statement_timeout")
            .ToListAsync(cancellationToken))[0];
}
