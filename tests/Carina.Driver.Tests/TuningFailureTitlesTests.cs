using Carina.Contracts;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public sealed class TuningFailureTitlesTests
{
    [Theory]
    [InlineData(TuningFailure.NoLock, SessionRefusalTitles.NoLock)]
    [InlineData(TuningFailure.LockedWithoutData, SessionRefusalTitles.NoData)]
    public void TheTwoWaysReceptionFailsEachHaveAWordOfTheirOwn(TuningFailure failure, string title)
        => Assert.Equal(title, TuningFailureTitles.Of(failure));

    [Theory]
    [InlineData(TuningFailure.Unspecified)]
    [InlineData(TuningFailure.DeviceUnusable)]
    public void ADeviceThatBrokeIsNotGivenAReceptionWord(TuningFailure failure)
        => Assert.Null(TuningFailureTitles.Of(failure));

    [Fact]
    public void ACauseThatIsNotADeviceFailureNamesNothing()
    {
        Assert.Null(TuningFailureTitles.Of((Exception?)null));
        Assert.Null(TuningFailureTitles.Of(new IOException("the reader went away")));
    }

    [Fact]
    public void AFailureCarriedInsideAnotherOneIsStillFound()
        => Assert.Equal(
            SessionRefusalTitles.NoData,
            TuningFailureTitles.Of(new StreamCutException(
                SessionStopReason.Unspecified,
                "the stream this one rode on ended",
                DvbFailure.LockedWithoutData("the demux delivered nothing"))));

    [Fact]
    public void ADeviceThatCouldNotBeOpenedIsNotBlamedOnReception()
        => Assert.Null(TuningFailureTitles.Of(DvbFailure.AtDevice(
            "/dev/dvb/adapter0/frontend0",
            "opening the frontend",
            Errno.Busy,
            "Another process holds the frontend.")));
}
