using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class CollectionSettingsTests
{
    [Fact]
    public void TheCoverageAimedForIsTheEightDaysBroadcastCarries()
        => Assert.Equal(TimeSpan.FromDays(8), new CollectionSettings().WantedCoverage);

    [Fact]
    public void TheThresholdThatSendsACollectorBackEarlyIsThreeDays()
        => Assert.Equal(TimeSpan.FromDays(3), new CollectionSettings().RevisitsBelow);

    [Fact]
    public void TheLongestAVisitMayTakeIsThreeMinutes()
        => Assert.Equal(TimeSpan.FromMinutes(3), new CollectionSettings().LongestVisit);

    [Fact]
    public void TheThresholdSitsInsideTheGoalRatherThanBeyondIt()
    {
        CollectionSettings settings = new();

        Assert.True(settings.RevisitsBelow < settings.WantedCoverage);
    }
}
