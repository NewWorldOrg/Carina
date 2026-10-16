using Carina.Contracts;
using Carina.Driver.Descrambling;

namespace Carina.Driver.Tests;

public sealed class HeadHoldTests
{
    [Fact]
    public void ALiveViewerWaitsForNoMoreThanWhatIsHeld()
    {
        Assert.Equal(HeadHold.WhatIsHeld, HeadHolds.For(SessionPurpose.Live));
    }

    [Theory]
    [InlineData(SessionPurpose.Recording)]
    [InlineData(SessionPurpose.Survey)]
    [InlineData(SessionPurpose.SurveyNow)]
    [InlineData(SessionPurpose.Scan)]
    [InlineData(SessionPurpose.Logo)]
    public void EveryOtherPurposeWaitsForEveryProgramme(SessionPurpose purpose)
    {
        Assert.Equal(HeadHold.EveryProgramme, HeadHolds.For(purpose));
    }

    [Fact]
    public void ALiveViewerIsReadFromTheLock()
    {
        Assert.True(HeadHolds.ReadsFromTheLock(HeadHolds.For(SessionPurpose.Live)));
    }

    [Theory]
    [InlineData(SessionPurpose.Recording)]
    [InlineData(SessionPurpose.Survey)]
    [InlineData(SessionPurpose.SurveyNow)]
    [InlineData(SessionPurpose.Scan)]
    [InlineData(SessionPurpose.Logo)]
    public void EveryOtherPurposeIsReadOnlyOnceTheFrontendHasLocked(SessionPurpose purpose)
    {
        Assert.False(HeadHolds.ReadsFromTheLock(HeadHolds.For(purpose)));
    }
}
