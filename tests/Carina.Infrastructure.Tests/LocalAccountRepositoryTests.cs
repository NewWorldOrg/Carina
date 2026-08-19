using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Tests.Auth;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class LocalAccountRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime At = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly QuickPasswordHasher hasher = new();

    [Fact]
    public async Task AnAccountThatWasNeverMadeIsNotThere()
    {
        await ClearAsync();

        await using CarinaDbContext reading = database.Open();

        Assert.Null(await new LocalAccountRepository(reading).FindAsync(Cancel));
    }

    [Fact]
    public async Task TheAccountComesBackWithAHashThatStillOpensIt()
    {
        await ClearAsync();

        await using (CarinaDbContext writing = database.Open())
        {
            await new LocalAccountRepository(writing).SaveAsync(Bootstrapped("a password long enough"), Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        LocalAccount? read = await new LocalAccountRepository(reading).FindAsync(Cancel);

        Assert.NotNull(read);
        Assert.Equal(FirstCredentials.Username, read.Username);
        Assert.True(hasher.Matches("a password long enough", read.PasswordHash));
    }

    [Fact]
    public async Task AChangedPasswordIsTheOneThatComesBack()
    {
        await ClearAsync();

        await using (CarinaDbContext writing = database.Open())
        {
            await new LocalAccountRepository(writing).SaveAsync(Bootstrapped("a password long enough"), Cancel);
        }

        await using (CarinaDbContext changing = database.Open())
        {
            var repository = new LocalAccountRepository(changing);
            LocalAccount? held = await repository.FindAsync(Cancel);

            held!.ChangePassword(hasher.Hash("a replacement password", PasswordHashPolicy.Default), At.AddHours(1));

            await repository.SaveAsync(held, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        LocalAccount? read = await new LocalAccountRepository(reading).FindAsync(Cancel);

        Assert.False(hasher.Matches("a password long enough", read!.PasswordHash));
        Assert.True(hasher.Matches("a replacement password", read.PasswordHash));
        Assert.Equal(At.AddHours(1), read.PasswordChangedAt);
    }

    [Fact]
    public async Task TheBootstrapFindsTheAccountItAlreadyMadeAndLeavesItAlone()
    {
        var log = new RecordedLog();

        await ClearAsync();

        await using (CarinaDbContext writing = database.Open())
        {
            await Bootstrap(log).EnsureAnAccountExistsAsync(new LocalAccountRepository(writing), Cancel);
        }

        string first;

        await using (CarinaDbContext reading = database.Open())
        {
            first = (await new LocalAccountRepository(reading).FindAsync(Cancel))!.PasswordHash.Value;
        }

        await using (CarinaDbContext again = database.Open())
        {
            await Bootstrap(log).EnsureAnAccountExistsAsync(new LocalAccountRepository(again), Cancel);
        }

        await using CarinaDbContext after = database.Open();
        LocalAccount? read = await new LocalAccountRepository(after).FindAsync(Cancel);

        Assert.Equal(first, read!.PasswordHash.Value);
        Assert.Single(log.Lines);
    }

    private static LocalAccountBootstrap Bootstrap(RecordedLog log)
        => new(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new QuickPasswordHasher(),
            PasswordHashPolicy.Default,
            TimeProvider.System,
            log);

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();

        await clearing.Set<LocalAccount>().ExecuteDeleteAsync(Cancel);
    }

    private LocalAccount Bootstrapped(string password)
        => LocalAccount.Bootstrap(
            FirstCredentials.Username,
            hasher.Hash(password, PasswordHashPolicy.Default),
            At);
}
