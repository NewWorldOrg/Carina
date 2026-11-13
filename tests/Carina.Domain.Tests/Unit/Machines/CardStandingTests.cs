using Carina.Domain.Machines;

namespace Carina.Domain.Tests.Unit.Machines;

[Trait("Category", "Unit")]
public sealed class CardStandingTests
{
    [Fact]
    public void ACardStandsInOneOfSixPlacesAndOnlyOneOfThemIsUsable()
    {
        Assert.Equal(
            [
                CardStanding.Usable,
                CardStanding.NodeMissing,
                CardStanding.NodeUnreadable,
                CardStanding.DriverUnusable,
                CardStanding.ProbeTimedOut,
                CardStanding.ProbeProgrammeMissing,
            ],
            Enum.GetValues<CardStanding>());

        Assert.Single(Enum.GetValues<CardStanding>(), CardStandings.IsUsable);
    }
}
