using Carina.Domain.Auth;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class OidcSettingsRepositoryTests(RepositoryDatabase database)
{
    private const string Discovery = "https://login.example.test/.well-known/openid-configuration";

    private static readonly DateTime At = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task AnInstallationThatNeverConfiguredAProviderHasNoRowToRead()
    {
        await ClearAsync();

        await using CarinaDbContext reading = database.Open();

        Assert.Null(await new OidcSettingsRepository(reading).FindAsync(Cancel));
    }

    [Fact]
    public async Task AConfiguredProviderComesBackWithTheSecretItWasSavedWith()
    {
        await ClearAsync();
        await SaveAsync(Configured());

        await using CarinaDbContext reading = database.Open();
        OidcSettings? read = await new OidcSettingsRepository(reading).FindAsync(Cancel);

        Assert.NotNull(read);
        Assert.Equal(Discovery, read.DiscoveryUrl);
        Assert.Equal("carina", read.ClientId);
        Assert.Equal(new ClientSecret("the-client-secret"), read.ClientSecret);
    }

    [Fact]
    public async Task WhoIsAllowedThroughComesBackAsItWasTyped()
    {
        await ClearAsync();

        OidcSettings settings = Configured();
        settings.Restrict(["operators", "owners"], ["example.test"], At);

        await SaveAsync(settings);

        await using CarinaDbContext reading = database.Open();
        OidcSettings read = (await new OidcSettingsRepository(reading).FindAsync(Cancel))!;

        Assert.Equal(["operators", "owners"], read.AllowedGroups);
        Assert.Equal(["example.test"], read.AllowedHostedDomains);
        Assert.False(read.Restriction.AdmitsEveryone);
    }

    [Fact]
    public async Task AProviderNamingNobodyComesBackAdmittingEveryone()
    {
        await ClearAsync();
        await SaveAsync(Configured());

        await using CarinaDbContext reading = database.Open();
        OidcSettings read = (await new OidcSettingsRepository(reading).FindAsync(Cancel))!;

        Assert.Empty(read.AllowedGroups);
        Assert.Empty(read.AllowedHostedDomains);
        Assert.True(read.Restriction.AdmitsEveryone);
    }

    [Fact]
    public async Task ClearingTheProviderLeavesTheRowWithNeitherSettingsNorRestriction()
    {
        await ClearAsync();

        OidcSettings settings = Configured();
        settings.Restrict(["operators"], null, At);

        await SaveAsync(settings);

        await using (CarinaDbContext changing = database.Open())
        {
            var repository = new OidcSettingsRepository(changing);
            OidcSettings held = (await repository.FindAsync(Cancel))!;

            held.Clear(At.AddDays(1));

            await repository.SaveAsync(held, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        OidcSettings read = (await new OidcSettingsRepository(reading).FindAsync(Cancel))!;

        Assert.False(read.IsConfigured);
        Assert.Null(read.ClientSecret);
        Assert.Empty(read.AllowedGroups);
    }

    private static OidcSettings Configured()
    {
        var settings = OidcSettings.Unconfigured(At);
        settings.Configure(Discovery, "carina", new ClientSecret("the-client-secret"), At);

        return settings;
    }

    private async Task SaveAsync(OidcSettings settings)
    {
        await using CarinaDbContext writing = database.Open();

        await new OidcSettingsRepository(writing).SaveAsync(settings, Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext context = database.Open();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM oidc_config");
    }
}
