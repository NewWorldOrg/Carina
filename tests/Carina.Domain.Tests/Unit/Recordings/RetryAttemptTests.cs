using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Unit.Recordings;

[Trait("Category", "Unit")]
public sealed class RetryAttemptTests
{
    [Fact(DisplayName = "BR-KS-003: an attempt that began a recording counts as started")]
    public void AnAttemptThatBeganARecordingCountsAsStarted()
        => Assert.Same(RetryAttempt.Started, RetryAttempt.Of(true, true, Named()));

    [Fact(DisplayName = "BR-KS-003: an attempt the driver refused carries the class it named, or none")]
    public void AnAttemptTheDriverRefusedCarriesTheClassItNamed()
    {
        RetryAttempt named = RetryAttempt.Of(false, true, Named());
        RetryAttempt unnamed = RetryAttempt.Of(false, true, null);

        Assert.Equal(RetryResult.RefusedAgain, named.Result);
        Assert.Equal(TuneFailureKind.NoLock, named.TuneFailure);
        Assert.Equal([RecordingFault.TuneFailed], named.Faults);
        Assert.Equal(RetryResult.RefusedAgain, unnamed.Result);
        Assert.Empty(unnamed.Faults);
    }

    [Fact(DisplayName = "BR-KS-003: an attempt nothing answered, or one given up on this side, is unanswered")]
    public void AnAttemptNothingAnsweredIsUnanswered()
        => Assert.Same(RetryAttempt.Unanswered, RetryAttempt.Of(false, false, Named()));

    private static RecordingStartFailure Named()
        => RecordingStartFailure.TheTunerWouldNotTune(TuneFailureKind.NoLock);
}
