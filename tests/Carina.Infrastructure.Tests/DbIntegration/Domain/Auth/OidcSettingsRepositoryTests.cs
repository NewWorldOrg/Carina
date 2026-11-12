using Carina.Domain.Auth;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Auth;

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
    public async Task AnInstallationThatNeverConfiguredAProviderHasNoRowToRead()
    {
        await ClearAsync();

        await using AsyncServiceScope reading = services.CreateAsyncScope();

        Assert.Null(await Settings(reading).FindAsync(Cancel));
    }

    [Fact]
    public async Task AConfiguredProviderComesBackWithTheSecretItWasSavedWith()
    {
        await ClearAsync();
        await SaveAsync(Configured());

        await using AsyncServiceScope reading = services.CreateAsyncScope();
        OidcSettings? read = await Settings(reading).FindAsync(Cancel);

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

        await using AsyncServiceScope reading = services.CreateAsyncScope();
        OidcSettings read = (await Settings(reading).FindAsync(Cancel))!;

        Assert.Equal(["operators", "owners"], read.AllowedGroups);
        Assert.Equal(["example.test"], read.AllowedHostedDomains);
        Assert.False(read.Restriction.AdmitsEveryone);
    }

    [Fact]
    public async Task AProviderNamingNobodyComesBackAdmittingEveryone()
    {
        await ClearAsync();
        await SaveAsync(Configured());

        await using AsyncServiceScope reading = services.CreateAsyncScope();
        OidcSettings read = (await Settings(reading).FindAsync(Cancel))!;

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

        await using (AsyncServiceScope changing = services.CreateAsyncScope())
        {
            IOidcSettingsRepository repository = Settings(changing);
            OidcSettings held = (await repository.FindAsync(Cancel))!;

            held.Clear(At.AddDays(1));

            await repository.SaveAsync(held, Cancel);
        }

        await using AsyncServiceScope reading = services.CreateAsyncScope();
        OidcSettings read = (await Settings(reading).FindAsync(Cancel))!;

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

        await using ServiceProvider elsewhere = database.Infrastructure();
        await using AsyncServiceScope reading = elsewhere.CreateAsyncScope();
        OidcSettings read = (await Settings(reading).FindAsync(Cancel))!;

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

        await using (ServiceProvider elsewhere = database.Infrastructure())
        await using (AsyncServiceScope changing = elsewhere.CreateAsyncScope())
        {
            IOidcSettingsRepository repository = Settings(changing);
            OidcSettings held = (await repository.FindAsync(Cancel))!;

            held.Restrict(["operators"], null, At.AddDays(1));

            await repository.SaveAsync(held, Cancel);
        }

        await using AsyncServiceScope reading = services.CreateAsyncScope();
        OidcSettings read = (await Settings(reading).FindAsync(Cancel))!;

        Assert.Equal(new ClientSecret("the-client-secret"), read.ClientSecret);
        Assert.Equal(["operators"], read.AllowedGroups);
    }

    [Fact]
    public async Task ASecretEnteredAgainAfterItWasLostIsSealedWithTheKeysNowHeld()
    {
        await ClearAsync();
        await SaveAsync(Configured());
        await using ServiceProvider now = database.Infrastructure();

        await using (AsyncServiceScope changing = now.CreateAsyncScope())
        {
            IOidcSettingsRepository repository = Settings(changing);
            OidcSettings held = (await repository.FindAsync(Cancel))!;

            held.Configure(Discovery, "carina", new ClientSecret("the-new-secret"), At.AddDays(1));

            await repository.SaveAsync(held, Cancel);
        }

        await using AsyncServiceScope reading = now.CreateAsyncScope();
        OidcSettings read = (await Settings(reading).FindAsync(Cancel))!;

        Assert.Equal(new ClientSecret("the-new-secret"), read.ClientSecret);
        Assert.True(read.IsConfigured);
    }

    private static OidcSettings Configured()
    {
        var settings = OidcSettings.Unconfigured(At);
        settings.Configure(Discovery, "carina", new ClientSecret("the-client-secret"), At);

        return settings;
    }

    private async Task SaveAsync(OidcSettings settings)
    {
        await using AsyncServiceScope writing = services.CreateAsyncScope();

        await Settings(writing).SaveAsync(settings, Cancel);
    }

    private static IOidcSettingsRepository Settings(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IOidcSettingsRepository>();

    private async Task ClearAsync()
    {
        await using CarinaDbContext context = database.Open();

        await context.Database.ExecuteSqlRawAsync("DELETE FROM oidc_config");
    }
}
