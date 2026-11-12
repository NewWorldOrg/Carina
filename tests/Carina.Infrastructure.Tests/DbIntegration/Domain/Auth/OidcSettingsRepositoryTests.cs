using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Tests.Auth;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class OidcSettingsRepositoryTests(RepositoryDatabase database)
{
    private const string Discovery = "https://login.example.test/.well-known/openid-configuration";

    private static readonly DateTime At = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly ClientSecretSeal Kept = SealingKeys.Seal();

    [Fact]
    public async Task AnInstallationThatNeverConfiguredAProviderHasNoRowToRead()
    {
        await ClearAsync();

        await using CarinaDbContext reading = database.Open();

        Assert.Null(await new OidcSettingsRepository(reading, Kept).FindAsync(Cancel));
    }

    [Fact]
    public async Task AConfiguredProviderComesBackWithTheSecretItWasSavedWith()
    {
        await ClearAsync();
        await SaveAsync(Configured());

        await using CarinaDbContext reading = database.Open();
        OidcSettings? read = await new OidcSettingsRepository(reading, Kept).FindAsync(Cancel);

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
        OidcSettings read = (await new OidcSettingsRepository(reading, Kept).FindAsync(Cancel))!;

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
        OidcSettings read = (await new OidcSettingsRepository(reading, Kept).FindAsync(Cancel))!;

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
            OidcSettingsRepository repository = new(changing, Kept);
            OidcSettings held = (await repository.FindAsync(Cancel))!;

            held.Clear(At.AddDays(1));

            await repository.SaveAsync(held, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        OidcSettings read = (await new OidcSettingsRepository(reading, Kept).FindAsync(Cancel))!;

        Assert.False(read.IsConfigured);
        Assert.Null(read.ClientSecret);
        Assert.Empty(read.AllowedGroups);
    }

    [Fact]
    public async Task TheSecretIsKeptSealedAndNeverInTheClear()
    {
        await ClearAsync();
        await SaveAsync(Configured());

        await using CarinaDbContext reading = database.Open();
        List<string> rows = await reading.Database
            .SqlQueryRaw<string>("SELECT row_to_json(held)::text AS \"Value\" FROM oidc_config AS held")
            .ToListAsync(Cancel);
        List<string?> inTheClear = await reading.Database
            .SqlQueryRaw<string?>("SELECT client_secret AS \"Value\" FROM oidc_config")
            .ToListAsync(Cancel);

        string row = Assert.Single(rows);
        Assert.DoesNotContain("the-client-secret", row, StringComparison.Ordinal);
        Assert.Equal([null], inTheClear);
    }

    [Fact]
    public async Task ASecretSealedWithKeysThatAreGoneIsLostRatherThanReadAsSomethingElse()
    {
        await ClearAsync();
        await SaveAsync(Configured());

        await using CarinaDbContext reading = database.Open();
        OidcSettings read = (await new OidcSettingsRepository(reading, SealingKeys.Seal()).FindAsync(Cancel))!;

        Assert.Null(read.ClientSecret);
        Assert.True(read.SecretLost);
        Assert.False(read.IsConfigured);
        Assert.Equal(Discovery, read.DiscoveryUrl);
        Assert.Equal("carina", read.ClientId);
    }

    [Fact]
    public async Task SavingWhileTheSecretIsLostKeepsWhatWasSealedForTheKeysToOpenOnceTheyAreBack()
    {
        await ClearAsync();
        await SaveAsync(Configured());

        await using (CarinaDbContext changing = database.Open())
        {
            OidcSettingsRepository elsewhere = new(changing, SealingKeys.Seal());
            OidcSettings held = (await elsewhere.FindAsync(Cancel))!;

            held.Restrict(["operators"], null, At.AddDays(1));

            await elsewhere.SaveAsync(held, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        OidcSettings read = (await new OidcSettingsRepository(reading, Kept).FindAsync(Cancel))!;

        Assert.Equal(new ClientSecret("the-client-secret"), read.ClientSecret);
        Assert.Equal(["operators"], read.AllowedGroups);
    }

    [Fact]
    public async Task ASecretEnteredAgainAfterItWasLostIsSealedWithTheKeysNowHeld()
    {
        await ClearAsync();
        await SaveAsync(Configured());
        ClientSecretSeal now = SealingKeys.Seal();

        await using (CarinaDbContext changing = database.Open())
        {
            OidcSettingsRepository repository = new(changing, now);
            OidcSettings held = (await repository.FindAsync(Cancel))!;

            held.Configure(Discovery, "carina", new ClientSecret("the-new-secret"), At.AddDays(1));

            await repository.SaveAsync(held, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        OidcSettings read = (await new OidcSettingsRepository(reading, now).FindAsync(Cancel))!;

        Assert.Equal(new ClientSecret("the-new-secret"), read.ClientSecret);
        Assert.True(read.IsConfigured);
    }

    [Fact]
    public async Task ASecretStillHeldInTheClearIsReadAndThenSealed()
    {
        await ClearAsync();
        await HeldInTheClearAsync("the-client-secret");

        await using (CarinaDbContext sealing = database.Open())
        {
            OidcSettingsRepository repository = new(sealing, Kept);

            Assert.Equal(new ClientSecret("the-client-secret"), (await repository.FindAsync(Cancel))!.ClientSecret);
            Assert.True(await repository.SealAnUnsealedSecretAsync(Cancel));
        }

        await using CarinaDbContext reading = database.Open();
        List<string?> inTheClear = await reading.Database
            .SqlQueryRaw<string?>("SELECT client_secret AS \"Value\" FROM oidc_config")
            .ToListAsync(Cancel);
        OidcSettingsRepository repeated = new(reading, Kept);

        Assert.Equal([null], inTheClear);
        Assert.Equal(new ClientSecret("the-client-secret"), (await repeated.FindAsync(Cancel))!.ClientSecret);
        Assert.False(await repeated.SealAnUnsealedSecretAsync(Cancel));
    }

    [Fact]
    public async Task NothingIsSealedWhereNoProviderIsHeld()
    {
        await ClearAsync();

        await using CarinaDbContext sealing = database.Open();

        Assert.False(await new OidcSettingsRepository(sealing, Kept).SealAnUnsealedSecretAsync(Cancel));
    }

    private async Task HeldInTheClearAsync(string secret)
    {
        await using CarinaDbContext context = database.Open();

        await context.Database.ExecuteSqlAsync(
            $"INSERT INTO oidc_config (id, discovery_url, client_id, client_secret, updated_at) VALUES ({OidcSettings.TheOnlyRow}, {Discovery}, {"carina"}, {secret}, {At})",
            Cancel);
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

        await new OidcSettingsRepository(writing, Kept).SaveAsync(settings, Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext context = database.Open();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM oidc_config");
    }
}
