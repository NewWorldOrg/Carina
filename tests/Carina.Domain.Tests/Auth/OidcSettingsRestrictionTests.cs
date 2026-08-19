using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class OidcSettingsRestrictionTests
{
    private const string Discovery = "https://login.example.test/.well-known/openid-configuration";

    private static readonly DateTime At = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AFreshInstallationNamesNobodyAndThereforeAdmitsEveryone()
    {
        OidcSettings settings = OidcSettings.Unconfigured(At);

        Assert.Empty(settings.AllowedGroups);
        Assert.Empty(settings.AllowedHostedDomains);
        Assert.True(settings.Restriction.AdmitsEveryone);
    }

    [Fact]
    public void NamingGroupsAndDomainsMovesTheMomentTheSettingsWereLastTouched()
    {
        OidcSettings settings = Configured();

        settings.Restrict(["operators"], ["example.test"], At.AddDays(1));

        Assert.Equal(["operators"], settings.AllowedGroups);
        Assert.Equal(["example.test"], settings.AllowedHostedDomains);
        Assert.False(settings.Restriction.AdmitsEveryone);
        Assert.Equal(At.AddDays(1), settings.UpdatedAt);
    }

    [Fact]
    public void ClearingTheIdentityProviderTakesItsRestrictionWithIt()
    {
        OidcSettings settings = Configured();
        settings.Restrict(["operators"], null, At);

        settings.Clear(At.AddDays(1));

        Assert.Empty(settings.AllowedGroups);
        Assert.True(settings.Restriction.AdmitsEveryone);
    }

    [Fact]
    public void ChangingTheClientLeavesTheRestrictionWhereItWas()
    {
        OidcSettings settings = Configured();
        settings.Restrict(["operators"], null, At);

        settings.Configure(Discovery, "carina-renamed", null, At.AddDays(1));

        Assert.Equal(["operators"], settings.AllowedGroups);
    }

    [Fact]
    public void ARehydratedRowCarriesBackWhoWasAllowedThrough()
    {
        OidcSettings settings = OidcSettings.Rehydrate(
            OidcSettings.TheOnlyRow,
            Discovery,
            "carina",
            new ClientSecret("s3cr3t"),
            At,
            ["operators"],
            ["example.test"]);

        Assert.Equal(["operators"], settings.AllowedGroups);
        Assert.Equal(["example.test"], settings.AllowedHostedDomains);
    }

    [Fact]
    public void ARestrictionCannotBeTouchedBeforeTheSettingsExisted()
    {
        OidcSettings settings = Configured();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => settings.Restrict(["operators"], null, At.AddSeconds(-1)));
    }

    private static OidcSettings Configured()
    {
        OidcSettings settings = OidcSettings.Unconfigured(At);
        settings.Configure(Discovery, "carina", new ClientSecret("s3cr3t"), At);

        return settings;
    }
}
