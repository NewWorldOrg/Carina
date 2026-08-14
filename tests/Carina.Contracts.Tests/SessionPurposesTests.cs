namespace Carina.Contracts.Tests;

public sealed class SessionPurposesTests
{
    [Fact]
    public void ASurveyKeepsTheBackpressureItsNameAlreadyCarried()
    {
        Assert.True(SessionPurposes.ReadsEveryPacket(SessionPurpose.Survey));
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
}
