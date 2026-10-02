using Carina.Domain.Channels;
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
    public void TheLongestBetweenAttemptsIsASweepTheWaitsOnFullTunersAndOneVisitListeningAndWriting()
    {
        CollectionSettings settings = new()
        {
            BetweenSweeps = TimeSpan.FromMinutes(10),
            LongestVisit = TimeSpan.FromMinutes(2),
            WhenTunersAreFull = new RotationBackoff(TimeSpan.FromSeconds(10), 2, TimeSpan.FromMinutes(1), 3),
        };

        Assert.Equal(
            TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(10 + 20) + TimeSpan.FromMinutes(2 + 2),
            settings.LongestBetweenAttempts());
    }

    [Fact]
    public void TheLongestBetweenAttemptsIsLongerThanTheWaitBetweenSweeps()
    {
        CollectionSettings settings = new();

        Assert.True(settings.LongestBetweenAttempts() > settings.BetweenSweeps);
    }

    [Fact]
    public void TheThresholdSitsInsideTheGoalRatherThanBeyondIt()
    {
        CollectionSettings settings = new();

        Assert.True(settings.RevisitsBelow < settings.WantedCoverage);
    }
}
