using Carina.Contracts;
using Carina.Driver.Descrambling;

namespace Carina.Driver.Tests;

public sealed class HeadHoldTests
{
    [Fact]
    public void ALiveViewerWaitsForTheServiceItAskedFor()
    {
        Assert.Equal(HeadHold.ForProgramme(1024), HeadHold.For(SessionPurpose.Live, service: 1024));
    }

    [Fact]
    public void ALiveViewerThatNamesNoServiceWaitsForEveryProgramme()
    {
        Assert.Equal(HeadHold.EveryProgramme, HeadHold.For(SessionPurpose.Live, service: null));
    }

    [Theory]
    [InlineData(SessionPurpose.Recording)]
    [InlineData(SessionPurpose.Survey)]
    [InlineData(SessionPurpose.SurveyNow)]
    [InlineData(SessionPurpose.Scan)]
    [InlineData(SessionPurpose.Logo)]
    public void EveryOtherPurposeWaitsForEveryProgrammeWhateverServiceItNames(SessionPurpose purpose)
    {
        Assert.Equal(HeadHold.EveryProgramme, HeadHold.For(purpose, service: 1024));
    }
}
