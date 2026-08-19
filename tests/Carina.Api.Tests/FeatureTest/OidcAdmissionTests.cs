using Carina.Api.Authentication;
using Carina.Domain.Auth;
using Carina.TestSupport;

namespace Carina.Api.Tests.FeatureTest;

public sealed class OidcAdmissionTests
{
    [Fact]
    public async Task WithNobodyNamedEveryoneTheTenantCanSignInThroughIsLetIn()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured();

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("a-stranger"));

        Assert.Single(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task AMemberOfAnAllowedGroupIsLetIn()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedGroups: ["operators"]);

        using HttpResponseMessage arrived = await probe.SignInAsync(
            new MockIdentityUser("owner") { Groups = ["strangers", "operators"] });

        Assert.Single(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task SomebodyInNoneOfTheAllowedGroupsIsKeptOut()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedGroups: ["operators"]);

        using HttpResponseMessage arrived = await probe.SignInAsync(
            new MockIdentityUser("a-stranger") { Groups = ["strangers"] });

        Assert.Empty(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task SomebodyWhoseGroupsOverflowedOutOfTheTokenIsKeptOutRatherThanFetchedForElsewhere()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedGroups: ["operators"]);

        using HttpResponseMessage arrived = await probe.SignInAsync(
            new MockIdentityUser("owner") { GroupsOverflowed = true });

        Assert.Empty(probe.Sessions.Sessions);
        Assert.DoesNotContain(probe.Idp.Visits, visit => visit.EndsWith("/groups", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnOverflowIsNoObstacleWhereTheInstallationDecidesByHostedDomain()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedHostedDomains: ["example.test"]);

        using HttpResponseMessage arrived = await probe.SignInAsync(
            new MockIdentityUser("owner") { GroupsOverflowed = true, HostedDomain = "example.test" });

        Assert.Single(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task AnAccountFromTheAllowedHostedDomainIsLetInWhereNoGroupsAreEverIssued()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedHostedDomains: ["example.test"]);

        using HttpResponseMessage arrived = await probe.SignInAsync(
            new MockIdentityUser("owner") { HostedDomain = "example.test" });

        Assert.Single(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task AnAccountFromAnotherHostedDomainIsKeptOut()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedHostedDomains: ["example.test"]);

        using HttpResponseMessage arrived = await probe.SignInAsync(
            new MockIdentityUser("a-stranger") { HostedDomain = "elsewhere.test" });

        Assert.Empty(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task AConsumerAccountCarryingNoHostedDomainIsKeptOut()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedHostedDomains: ["example.test"]);

        using HttpResponseMessage arrived = await probe.SignInAsync(new MockIdentityUser("a-stranger"));

        Assert.Empty(probe.Sessions.Sessions);
    }

    [Fact]
    public async Task SomebodyKeptOutIsToldNoMoreThanSomebodyWhoseTokenNeverVerified()
    {
        await using OidcProbe probe = OidcProbe.OverHttp().Configured(allowedGroups: ["operators"]);

        using HttpResponseMessage kept = await probe.SignInAsync(
            new MockIdentityUser("a-stranger") { Groups = ["strangers"] });

        Assert.Contains(
            LoginRedirect.TheIdentityProviderFailed,
            kept.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }
}
