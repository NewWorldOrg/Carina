using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class OidcSettingsTests
{
    private const string Discovery = "https://login.example.test/.well-known/openid-configuration";

    private static readonly DateTime At = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AnInstallationStartsWithNoIdentityProviderAndSaysSo()
    {
        OidcSettings settings = OidcSettings.Unconfigured(At);

        Assert.Equal(OidcSettings.TheOnlyRow, settings.Id);
        Assert.False(settings.IsConfigured);
        Assert.Null(settings.DiscoveryUrl);
        Assert.Null(settings.ClientId);
        Assert.Null(settings.ClientSecret);
    }

    [Fact]
    public void ConfiguringAnIdentityProviderNeedsAllThreeOfItsPieces()
    {
        OidcSettings settings = Configured();

        Assert.True(settings.IsConfigured);
        Assert.Equal(Discovery, settings.DiscoveryUrl);
        Assert.Equal("carina", settings.ClientId);
        Assert.Equal(new ClientSecret("s3cr3t"), settings.ClientSecret);
    }

    [Fact]
    public void SavingTheFormAgainWithoutTheSecretKeepsTheSecretAlreadyHeld()
    {
        OidcSettings settings = Configured();

        settings.Configure(Discovery, "carina-renamed", null, At.AddDays(1));

        Assert.Equal("carina-renamed", settings.ClientId);
        Assert.Equal(new ClientSecret("s3cr3t"), settings.ClientSecret);
    }

    [Fact]
    public void SavingTheFormWithANewSecretReplacesTheOneHeld()
    {
        OidcSettings settings = Configured();

        settings.Configure(Discovery, "carina", new ClientSecret("rotated"), At.AddDays(1));

        Assert.Equal(new ClientSecret("rotated"), settings.ClientSecret);
    }

    [Fact]
    public void AnIdentityProviderCannotBeConfiguredWithoutASecretItNeverHad()
    {
        OidcSettings settings = OidcSettings.Unconfigured(At);

        Assert.Throws<InvalidOperationException>(
            () => settings.Configure(Discovery, "carina", null, At.AddDays(1)));
    }

    [Fact]
    public void ConfiguringMovesTheMomentTheSettingsWereLastTouched()
    {
        OidcSettings settings = Configured();

        settings.Configure(Discovery, "carina", null, At.AddDays(2));

        Assert.Equal(At.AddDays(2), settings.UpdatedAt);
    }

    [Fact]
    public void ClearingTheIdentityProviderLeavesTheInstallationOnItsLocalAccount()
    {
        OidcSettings settings = Configured();

        settings.Clear(At.AddDays(3));

        Assert.False(settings.IsConfigured);
        Assert.Null(settings.ClientSecret);
        Assert.Equal(At.AddDays(3), settings.UpdatedAt);
    }

    [Theory]
    [InlineData("http://login.example.test/.well-known/openid-configuration")]
    [InlineData("login.example.test")]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("javascript:alert(1)")]
    public void ADiscoveryUrlThatIsNotAnHttpsUrlIsRefused(string discovery)
    {
        OidcSettings settings = OidcSettings.Unconfigured(At);

        Assert.Throws<ArgumentException>(
            () => settings.Configure(discovery, "carina", new ClientSecret("s3cr3t"), At));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AClientIdThatIsNotAnIdentifierIsRefused(string clientId)
    {
        OidcSettings settings = OidcSettings.Unconfigured(At);

        Assert.Throws<ArgumentException>(
            () => settings.Configure(Discovery, clientId, new ClientSecret("s3cr3t"), At));
    }

    [Fact]
    public void SettingsCannotBeTouchedBeforeTheyExisted()
    {
        OidcSettings settings = Configured();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => settings.Configure(Discovery, "carina", null, At.AddSeconds(-1)));
    }

    [Fact]
    public void ARehydratedRowCarriesBackWhatWasConfigured()
    {
        OidcSettings settings = OidcSettings.Rehydrate(
            OidcSettings.TheOnlyRow,
            Discovery,
            "carina",
            new ClientSecret("s3cr3t"),
            At);

        Assert.True(settings.IsConfigured);
        Assert.Equal("carina", settings.ClientId);
    }

    [Fact]
    public void AHalfConfiguredRowIsNotAStateTheSettingsCanBeIn()
    {
        Assert.Throws<ArgumentException>(
            () => OidcSettings.Rehydrate(OidcSettings.TheOnlyRow, Discovery, "carina", null, At));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ASecretThatIsNotASecretIsRefused(string secret)
    {
        Assert.Throws<ArgumentException>(() => new ClientSecret(secret));
    }

    [Fact]
    public void ASecretIsNeverNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ClientSecret(null!));
    }

    [Fact]
    public void ASecretKeepsItselfOutOfWhateverPrintsIt()
    {
        var secret = new ClientSecret("s3cr3t");

        Assert.DoesNotContain("s3cr3t", secret.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t", $"{secret}", StringComparison.Ordinal);
    }

    private static OidcSettings Configured()
    {
        OidcSettings settings = OidcSettings.Unconfigured(At);
        settings.Configure(Discovery, "carina", new ClientSecret("s3cr3t"), At);

        return settings;
    }
}
