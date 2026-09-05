using Carina.Domain.Auth;
using Carina.Domain.Base;
using Carina.Infrastructure.Auth;
using Carina.Infrastructure.Persistence;

namespace Carina.Conventions.Tests;

public sealed class PlaybackTargetRuleTests
{
    private static readonly IReadOnlyList<Type> ProductionTypes =
    [
        .. typeof(Program).Assembly.GetTypes(),
        .. typeof(CommonValueObject<>).Assembly.GetTypes(),
        .. typeof(CarinaDbContext).Assembly.GetTypes(),
    ];

    [Fact]
    public void BrLa004EveryWayACarrierIsHandedOutOrHonouredNamesTheRecordingItOpens()
    {
        Assert.Empty(ConventionRules.CarrierHandoutsNamingNoTarget(ProductionTypes));
    }

    [Fact]
    public void BrLa004TheWaysACarrierIsHandedOutOrHonouredAreTheseAndNoOthers()
    {
        Assert.Equal(
            [
                "Carina.Domain.Auth.IPlaybackGrantStore.Admit(String, PlaybackTarget)",
                "Carina.Domain.Auth.IPlaybackGrantStore.Open(String, Subject, PlaybackTarget)",
                "Carina.Domain.Auth.IPlaybackTicketStore.Issue(Subject, PlaybackTarget)",
                "Carina.Domain.Auth.IPlaybackTicketStore.Spend(String, PlaybackTarget)",
                "Carina.Domain.Auth.PlaybackGrant.OpenedBy(String, Subject, PlaybackTarget, DateTime)",
                "Carina.Domain.Auth.PlaybackTicket.Issue(Subject, PlaybackTarget, DateTime, String&)",
                "Carina.Infrastructure.Auth.PlaybackGrantStore.Admit(String, PlaybackTarget)",
                "Carina.Infrastructure.Auth.PlaybackGrantStore.Open(String, Subject, PlaybackTarget)",
                "Carina.Infrastructure.Auth.PlaybackTicketStore.Issue(Subject, PlaybackTarget)",
                "Carina.Infrastructure.Auth.PlaybackTicketStore.Spend(String, PlaybackTarget)",
            ],
            ConventionRules.CarrierHandouts(ProductionTypes));
    }

    [Fact]
    public void TheRuleHasProductionInstancesToBiteOn()
    {
        Assert.Contains(typeof(PlaybackTicket), ProductionTypes);
        Assert.Contains(typeof(PlaybackGrant), ProductionTypes);
        Assert.Contains(typeof(PlaybackTicketStore), ProductionTypes);
        Assert.Contains(typeof(PlaybackGrantStore), ProductionTypes);
    }

    [Fact]
    public void TheRuleIsATripWireAndAStoreThatSkipsTheComparisonWalksPastIt()
    {
        Assert.Empty(ConventionRules.CarrierHandoutsNamingNoTarget([typeof(PlaybackTicketStore)]));
        Assert.Contains(
            "Carina.Infrastructure.Auth.PlaybackTicketStore.Spend(String, PlaybackTarget)",
            ConventionRules.CarrierHandouts([typeof(PlaybackTicketStore)]));
    }
}
