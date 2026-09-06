using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class LogoSweepSettingsTests
{
    [Fact]
    public void TheLongestAVisitMayTakeIsTenMinutes()
        => Assert.Equal(TimeSpan.FromMinutes(10), new LogoSweepSettings().LongestVisit);

    [Fact]
    public void AVisitEndsWellInsideTheGapBetweenSweepsSoTheTunerGoesBack()
    {
        LogoSweepSettings settings = new();

        Assert.True(settings.LongestVisit < settings.BetweenSweeps);
    }

    [Fact]
    public void AWakeMayWorkForTheGapBetweenSweepsLessTheVisitItHasToLeaveRoomFor()
        => Assert.Equal(TimeSpan.FromMinutes(50), new LogoSweepSettings().RoundBudget);

    [Fact]
    public void ARoundThatSpendsEveryMinuteOfItsBudgetStillEndsBeforeTheNextSweepIsDue()
    {
        LogoSweepSettings settings = new();

        Assert.True(settings.RoundBudget + settings.LongestVisit <= settings.BetweenSweeps);
    }
}
