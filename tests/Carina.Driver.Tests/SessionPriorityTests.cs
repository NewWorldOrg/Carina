using Carina.Contracts;
using Carina.Driver.Sessions;

namespace Carina.Driver.Tests;

public sealed class SessionPriorityTests
{
    [Fact]
    public void ARecordingOutranksEveryOtherReasonToHoldATuner()
    {
        Assert.True(SessionPriority.Recording > SessionPriority.Live);
        Assert.True(SessionPriority.Recording > SessionPriority.GuideNow);
        Assert.True(SessionPriority.Recording > SessionPriority.Scan);
        Assert.True(SessionPriority.Recording > SessionPriority.Guide);
        Assert.True(SessionPriority.Recording > SessionPriority.LogoCapture);
    }

    [Fact]
    public void SomeoneWatchingOutranksEveryCollectorButLosesToARecording()
    {
        Assert.True(SessionPriority.Live < SessionPriority.Recording);
        Assert.True(SessionPriority.Live > SessionPriority.GuideNow);
        Assert.True(SessionPriority.Live > SessionPriority.Scan);
        Assert.True(SessionPriority.Live > SessionPriority.Guide);
        Assert.True(SessionPriority.Live > SessionPriority.LogoCapture);
    }

    [Fact]
    public void TheGuidePassForWhatIsOnNowOutranksAScanButNotAViewer()
    {
        Assert.True(SessionPriority.GuideNow < SessionPriority.Live);
        Assert.True(SessionPriority.GuideNow > SessionPriority.Scan);
        Assert.True(SessionPriority.GuideNow > SessionPriority.Guide);
        Assert.True(SessionPriority.GuideNow > SessionPriority.LogoCapture);
    }

    [Fact]
    public void AScanOutranksTheBackgroundGuidePassAndLogoCapture()
    {
        Assert.True(SessionPriority.Scan < SessionPriority.GuideNow);
        Assert.True(SessionPriority.Scan > SessionPriority.Guide);
        Assert.True(SessionPriority.Scan > SessionPriority.LogoCapture);
    }

    [Fact]
    public void TheBackgroundGuidePassOutranksOnlyLogoCapture()
    {
        Assert.True(SessionPriority.Guide < SessionPriority.Scan);
        Assert.True(SessionPriority.Guide > SessionPriority.LogoCapture);
    }

    [Fact]
    public void TheLadderCarriesTheNumbersTheRequirementFixes()
    {
        Assert.Equal(10, SessionPriority.Recording);
        Assert.Equal(9, SessionPriority.Live);
        Assert.Equal(8, SessionPriority.GuideNow);
        Assert.Equal(5, SessionPriority.Scan);
        Assert.Equal(3, SessionPriority.Guide);
        Assert.Equal(1, SessionPriority.LogoCapture);
    }

    [Theory]
    [InlineData(SessionPurpose.Recording, SessionPriority.Recording)]
    [InlineData(SessionPurpose.Live, SessionPriority.Live)]
    [InlineData(SessionPurpose.Scan, SessionPriority.Scan)]
    [InlineData(SessionPurpose.Survey, SessionPriority.Guide)]
    public void EveryPurposeThisContractNamesSitsOnItsRung(SessionPurpose purpose, int rung)
    {
        Assert.Equal(rung, SessionPriority.Of(purpose));
    }

    [Fact]
    public void APurposeThisBuildDoesNotKnowSitsBelowEveryNamedOne()
    {
        var unknown = SessionPriority.Of(SessionPurpose.Unspecified);

        Assert.True(unknown < SessionPriority.LogoCapture);
        Assert.Equal(unknown, SessionPriority.Of((SessionPurpose)99));
    }

    [Fact]
    public void APurposeIsNeverRankedAboveItself()
    {
        foreach (var purpose in Enum.GetValues<SessionPurpose>())
        {
            Assert.Equal(SessionPriority.Of(purpose), SessionPriority.Of(purpose));
        }
    }
}
