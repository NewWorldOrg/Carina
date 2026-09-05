using Carina.Conventions.Tests.Fixtures.Playback;

namespace Carina.Conventions.Tests;

public sealed class PlaybackTargetRuleSelfCheckTests
{
    [Fact]
    public void DetectsATicketIssuedWithoutSayingWhatItOpens()
    {
        Assert.Equal(
            [
                $"{typeof(UnboundTicketStore).FullName}.Issue(Subject)",
                $"{typeof(UnboundTicketStore).FullName}.Spend(String)",
            ],
            ConventionRules.CarrierHandoutsNamingNoTarget([typeof(UnboundTicketStore), typeof(BoundTicketStore)]));
    }

    [Fact]
    public void DetectsAGrantOpenedWithoutSayingWhatItOpens()
    {
        Assert.Equal(
            [$"{typeof(PassThatOpensAnything).FullName}.Open(String, Subject)"],
            ConventionRules.CarrierHandoutsNamingNoTarget([typeof(PassThatOpensAnything)]));
    }

    [Fact]
    public void AStoreWhoseEveryHandoutNamesATargetPasses()
    {
        Assert.Empty(ConventionRules.CarrierHandoutsNamingNoTarget([typeof(BoundTicketStore)]));
    }

    [Fact]
    public void TypesThatNeitherIssueNorHonourACarrierAreNotLookedAt()
    {
        Assert.Empty(ConventionRules.CarrierHandouts([typeof(string), typeof(PlaybackTargetRuleSelfCheckTests)]));
    }
}
