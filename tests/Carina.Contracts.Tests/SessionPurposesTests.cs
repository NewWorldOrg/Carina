namespace Carina.Contracts.Tests;

public sealed class SessionPurposesTests
{
    [Fact]
    public void ASurveyKeepsTheBackpressureItsNameAlreadyCarried()
    {
        Assert.True(SessionPurposes.ReadsEveryPacket(SessionPurpose.Survey));
    }

    [Fact]
    public void AHurriedSurveyIsFedTheSameWayAsAnOrdinaryOne()
    {
        Assert.True(SessionPurposes.ReadsEveryPacket(SessionPurpose.SurveyNow));
    }

    [Fact]
    public void AScanWaitsForItsReaderBecauseATableSectionArrivesOncePerCycle()
    {
        Assert.True(SessionPurposes.ReadsEveryPacket(SessionPurpose.Scan));
    }

    [Theory]
    [InlineData(SessionPurpose.Recording)]
    [InlineData(SessionPurpose.Live)]
    [InlineData(SessionPurpose.Unspecified)]
    public void ARecordingOrAViewerIsNeverHeldUpByASlowReader(SessionPurpose purpose)
    {
        Assert.False(SessionPurposes.ReadsEveryPacket(purpose));
    }

    [Fact]
    public void AScanIsNamedApartFromASurveyThoughTheyAreFedTheSameWay()
    {
        Assert.NotEqual(SessionPurpose.Survey, SessionPurpose.Scan);
        Assert.Equal(
            SessionPurposes.ReadsEveryPacket(SessionPurpose.Survey),
            SessionPurposes.ReadsEveryPacket(SessionPurpose.Scan)
        );
    }

    [Fact]
    public void CollectingALogoWaitsForItsReaderTheWayEveryOtherWalkOfTheAirDoes()
    {
        Assert.True(SessionPurposes.ReadsEveryPacket(SessionPurpose.Logo));
    }

    [Fact]
    public void ADriverThatHasNeverHeardOfCollectingLogosIsNotTalkedIntoSomethingElseInstead()
    {
        var older = new DriverHello(
            1,
            "older",
            [.. SessionPurposes.Capabilities.Where(named => !named.EndsWith("logo", StringComparison.Ordinal))]);

        Assert.Equal(SessionPurpose.Unspecified, SessionPurposes.AgreedWith(older, SessionPurpose.Logo));
    }

    [Fact]
    public void ADriverThatSaysItCollectsLogosIsAskedForExactlyThat()
    {
        var current = new DriverHello(1, "current", [.. SessionPurposes.Capabilities]);

        Assert.Equal(SessionPurpose.Logo, SessionPurposes.AgreedWith(current, SessionPurpose.Logo));
    }
}
