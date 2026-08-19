using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class OidcRestrictionTests
{
    [Fact]
    public void AnInstallationThatNamesNobodyLetsEveryoneTheTenantCanSignInThrough()
    {
        OidcRestriction restriction = OidcRestriction.Of(null, null);

        Assert.True(restriction.AdmitsEveryone);
        Assert.Equal(OidcRefusal.None, restriction.Refuses(Claims()));
    }

    [Fact]
    public void NamingEvenOneGroupStopsTheInstallationAdmittingEveryone()
    {
        Assert.False(OidcRestriction.Of(["operators"], null).AdmitsEveryone);
        Assert.False(OidcRestriction.Of(null, ["example.test"]).AdmitsEveryone);
    }

    [Fact]
    public void AMemberOfAnAllowedGroupIsAdmitted()
    {
        OidcRestriction restriction = OidcRestriction.Of(["operators", "owners"], null);

        Assert.Equal(OidcRefusal.None, restriction.Refuses(Claims(groups: ["strangers", "owners"])));
    }

    [Fact]
    public void SomebodyInNoneOfTheAllowedGroupsIsRefused()
    {
        OidcRestriction restriction = OidcRestriction.Of(["operators"], null);

        Assert.Equal(
            OidcRefusal.OutsideEveryAllowedGroupAndDomain,
            restriction.Refuses(Claims(groups: ["strangers"])));
    }

    [Fact]
    public void GroupsThatOverflowedOutOfTheTokenAreRefusedRatherThanFetchedFromElsewhere()
    {
        OidcRestriction restriction = OidcRestriction.Of(["operators"], null);

        Assert.Equal(
            OidcRefusal.TheGroupsOverflowedOutOfTheToken,
            restriction.Refuses(Claims(groups: [], overflowed: true)));
    }

    [Fact]
    public void AnOverflowMattersOnlyWhereTheInstallationDecidesByGroup()
    {
        OidcRestriction restriction = OidcRestriction.Of(null, ["example.test"]);

        Assert.Equal(
            OidcRefusal.None,
            restriction.Refuses(Claims(overflowed: true, hostedDomain: "example.test")));
    }

    [Fact]
    public void AnAccountFromAnAllowedHostedDomainIsAdmittedWhereNoGroupsAreIssued()
    {
        OidcRestriction restriction = OidcRestriction.Of(null, ["example.test"]);

        Assert.Equal(OidcRefusal.None, restriction.Refuses(Claims(hostedDomain: "example.test")));
    }

    [Fact]
    public void AnAccountFromAnotherHostedDomainIsRefused()
    {
        OidcRestriction restriction = OidcRestriction.Of(null, ["example.test"]);

        Assert.Equal(
            OidcRefusal.OutsideEveryAllowedGroupAndDomain,
            restriction.Refuses(Claims(hostedDomain: "elsewhere.test")));
    }

    [Fact]
    public void AConsumerAccountCarryingNoHostedDomainIsRefusedWhereADomainIsNamed()
    {
        OidcRestriction restriction = OidcRestriction.Of(null, ["example.test"]);

        Assert.Equal(
            OidcRefusal.OutsideEveryAllowedGroupAndDomain,
            restriction.Refuses(Claims(hostedDomain: null)));
    }

    [Fact]
    public void ProvidersDisagreeOnCasingSoNeitherAGroupNorADomainIsComparedByIt()
    {
        OidcRestriction restriction = OidcRestriction.Of(["Operators"], ["Example.Test"]);

        Assert.Equal(OidcRefusal.None, restriction.Refuses(Claims(groups: ["operators"])));
        Assert.Equal(OidcRefusal.None, restriction.Refuses(Claims(hostedDomain: "example.test")));
    }

    [Fact]
    public void BlanksAndRepeatsTypedIntoTheFormAreNotEntriesToDecideBy()
    {
        OidcRestriction restriction = OidcRestriction.Of([" operators ", "operators", "  ", string.Empty], null);

        Assert.Equal(["operators"], restriction.Groups);
    }

    [Fact]
    public void AFormFilledWithNothingButBlanksStillAdmitsEveryone()
    {
        Assert.True(OidcRestriction.Of(["   "], [string.Empty]).AdmitsEveryone);
    }

    [Fact]
    public void AnEntryLongerThanAnyGroupNameIsRefusedRatherThanStored()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OidcRestriction.Of([new string('g', OidcRestriction.LongestEntry + 1)], null));
    }

    [Fact]
    public void MoreEntriesThanAnOperatorWouldEverTypeAreRefused()
    {
        string[] many = [.. Enumerable.Range(0, OidcRestriction.MostEntries + 1).Select(index => $"group-{index}")];

        Assert.Throws<ArgumentOutOfRangeException>(() => OidcRestriction.Of(many, null));
    }

    [Fact]
    public void AnEntryCarryingControlCharactersIsRefusedBecauseItReachesAScreen()
    {
        Assert.Throws<ArgumentException>(() => OidcRestriction.Of(["oper\u0007ators"], null));
    }

    private static OidcClaims Claims(
        IReadOnlyList<string>? groups = null,
        bool overflowed = false,
        string? hostedDomain = null)
        => new()
        {
            Issuer = "https://login.example.test",
            Audiences = ["carina"],
            Subject = "owner",
            ExpiresAt = new DateTime(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc),
            Groups = groups ?? [],
            GroupsOverflowed = overflowed,
            HostedDomain = hostedDomain,
        };
}
