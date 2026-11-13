using Carina.Domain.Auth;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Infra.Auth;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class OidcSettingsRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private const string Discovery = "https://login.example.test/.well-known/openid-configuration";

    private static readonly DateTime At = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider services = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await services.DisposeAsync();

    [Fact]
    public async Task ASecretStillHeldInTheClearIsReadAndThenSealed()
    {
        await ClearAsync();
        await HeldInTheClearAsync("the-client-secret");

        await using (AsyncServiceScope sealing = services.CreateAsyncScope())
        {
            OidcSettingsRepository repository = Settings(sealing);

            Assert.Equal(new ClientSecret("the-client-secret"), (await repository.FindAsync(Cancel))!.ClientSecret);
            Assert.True(await repository.SealAnUnsealedSecretAsync(Cancel));
        }

        await using CarinaDbContext reading = database.Open();
        List<string?> inTheClear = await reading.Database
            .SqlQueryRaw<string?>("SELECT client_secret AS \"Value\" FROM oidc_config")
            .ToListAsync(Cancel);
        await using AsyncServiceScope again = services.CreateAsyncScope();
        OidcSettingsRepository repeated = Settings(again);

        Assert.Equal([null], inTheClear);
        Assert.Equal(new ClientSecret("the-client-secret"), (await repeated.FindAsync(Cancel))!.ClientSecret);
        Assert.False(await repeated.SealAnUnsealedSecretAsync(Cancel));
    }

    [Fact]
    public async Task NothingIsSealedWhereNoProviderIsHeld()
    {
        await ClearAsync();

        await using AsyncServiceScope sealing = services.CreateAsyncScope();

        Assert.False(await Settings(sealing).SealAnUnsealedSecretAsync(Cancel));
    }

    private static OidcSettingsRepository Settings(AsyncServiceScope scope)
        => Assert.IsType<OidcSettingsRepository>(scope.ServiceProvider.GetRequiredService<IOidcSettingsRepository>());

    private async Task HeldInTheClearAsync(string secret)
    {
        await using CarinaDbContext context = database.Open();

        await context.Database.ExecuteSqlAsync(
            $"INSERT INTO oidc_config (id, discovery_url, client_id, client_secret, updated_at) VALUES ({OidcSettings.TheOnlyRow}, {Discovery}, {"carina"}, {secret}, {At})",
            Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext context = database.Open();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM oidc_config");
    }
}
