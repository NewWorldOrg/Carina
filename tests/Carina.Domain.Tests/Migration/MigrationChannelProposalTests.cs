using Carina.Domain.Migration;

using static Carina.Domain.Tests.Migration.MigrationFixtures;

namespace Carina.Domain.Tests.Migration;

public sealed class MigrationChannelProposalTests
{
    [Fact]
    public void AServiceTheRescanAnsweredForGetsTheOldNameProposedAndNothingIsSettled()
    {
        IReadOnlyList<MigrationChannelProposal> proposed = MigrationChannelProposal.Over(
            Run,
            [new SourceChannelDefinition(11, "an old name", SourceBroadcastKind.Terrestrial, InReach, "21")],
            new Dictionary<ServiceKey, string> { [InReach] = "the rescanned name" });

        MigrationChannelProposal only = Assert.Single(proposed);

        Assert.Equal(MigrationChannelStanding.NameProposed, only.Standing);
        Assert.Equal("an old name", only.SourceName);
        Assert.Equal("the rescanned name", only.RescannedName);
        Assert.Equal("21", only.SourcePhysicalChannel);
        Assert.Equal(InReach.Network, only.NetworkId);
        Assert.Equal(InReach.Service, only.ServiceId);
    }

    [Fact]
    public void AServiceNothingAnsweredForIsWrittenDownWithTheChannelTheSourceSystemWasUsing()
    {
        IReadOnlyList<MigrationChannelProposal> proposed = MigrationChannelProposal.Over(
            Run,
            [new SourceChannelDefinition(11, "an old name", SourceBroadcastKind.Terrestrial, Elsewhere, "27")],
            new Dictionary<ServiceKey, string> { [InReach] = "the rescanned name" });

        MigrationChannelProposal only = Assert.Single(proposed);

        Assert.Equal(MigrationChannelStanding.NothingAnswers, only.Standing);
        Assert.Null(only.RescannedName);
        Assert.Equal("27", only.SourcePhysicalChannel);
    }

    [Fact]
    public void AKindOfBroadcastThisSystemCannotNameIsWrittenDownAsSuchEvenWhenTheRescanAnswers()
    {
        IReadOnlyList<MigrationChannelProposal> proposed = MigrationChannelProposal.Over(
            Run,
            [new SourceChannelDefinition(11, "an old name", SourceBroadcastKind.Sky, InReach, "CS8")],
            new Dictionary<ServiceKey, string> { [InReach] = "the rescanned name" });

        MigrationChannelProposal only = Assert.Single(proposed);

        Assert.Equal(MigrationChannelStanding.Inexpressible, only.Standing);
        Assert.Null(only.RescannedName);
    }

    [Fact]
    public void ANameIsProposedForAServiceTheRescanAnsweredForAndForNoOther()
        => Assert.Throws<ArgumentException>(() => MigrationChannelProposal.Rehydrate(
            Run,
            InReach.Network,
            InReach.Service,
            MigrationChannelStanding.NothingAnswers,
            "an old name",
            "21",
            "the rescanned name"));
}
