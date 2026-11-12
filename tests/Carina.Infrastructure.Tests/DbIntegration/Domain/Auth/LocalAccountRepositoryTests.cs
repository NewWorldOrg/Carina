using Carina.Domain.Auth;
using Carina.Infrastructure.Persistence;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Auth;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class LocalAccountRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime At = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly QuickPasswordHasher hasher = new();

    private readonly ServiceProvider services = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await services.DisposeAsync();

    [Fact]
    public async Task AnAccountThatWasNeverMadeIsNotThere()
    {
        await ClearAsync();

        await using AsyncServiceScope reading = services.CreateAsyncScope();

        Assert.Null(await Accounts(reading).FindAsync(Cancel));
    }

    [Fact]
    public async Task TheAccountComesBackWithAHashThatStillOpensIt()
    {
        await ClearAsync();

        await using (AsyncServiceScope writing = services.CreateAsyncScope())
        {
            await Accounts(writing).SaveAsync(Bootstrapped("a password long enough"), Cancel);
        }

        await using AsyncServiceScope reading = services.CreateAsyncScope();
        LocalAccount? read = await Accounts(reading).FindAsync(Cancel);

        Assert.NotNull(read);
        Assert.Equal(FirstCredentials.Username, read.Username);
        Assert.True(hasher.Matches("a password long enough", read.PasswordHash));
    }

    [Fact]
    public async Task AChangedPasswordIsTheOneThatComesBack()
    {
        await ClearAsync();

        await using (AsyncServiceScope writing = services.CreateAsyncScope())
        {
            await Accounts(writing).SaveAsync(Bootstrapped("a password long enough"), Cancel);
        }

        await using (AsyncServiceScope changing = services.CreateAsyncScope())
        {
            ILocalAccountRepository repository = Accounts(changing);
            LocalAccount? held = await repository.FindAsync(Cancel);

            held!.ChangePassword(hasher.Hash("a replacement password", PasswordHashPolicy.Default), At.AddHours(1));

            await repository.SaveAsync(held, Cancel);
        }

        await using AsyncServiceScope reading = services.CreateAsyncScope();
        LocalAccount? read = await Accounts(reading).FindAsync(Cancel);

        Assert.False(hasher.Matches("a password long enough", read!.PasswordHash));
        Assert.True(hasher.Matches("a replacement password", read.PasswordHash));
        Assert.Equal(At.AddHours(1), read.PasswordChangedAt);
    }

    private static ILocalAccountRepository Accounts(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ILocalAccountRepository>();

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
