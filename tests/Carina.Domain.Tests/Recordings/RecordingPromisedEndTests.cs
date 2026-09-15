using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingPromisedEndTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    private static readonly DateTime Starts = Now.AddMinutes(-5);

    private static readonly DateTime Ends = Now.AddMinutes(55);

    [Fact]
    public void ARecordingIsPromisedTheEndItBeganWith()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal(Ends, recording.PromisedWindowEnd);
        Assert.Equal(recording.ExpectedWindowEnd, recording.PromisedWindowEnd);
    }

    [Fact]
    public void FollowingAProgrammeLaterMovesTheEndAndNeverThePromise()
    {
        Recording recording = RecordingFactory.Started();

        recording.Extend(Ends.AddMinutes(15));
        recording.Extend(Ends.AddMinutes(40));

        Assert.Equal(Ends.AddMinutes(40), recording.ExpectedWindowEnd);
        Assert.Equal(Ends, recording.PromisedWindowEnd);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-20)]
    public void AnEndThatIsNotLaterIsRefusedAndMovesNeitherTheEndNorThePromise(int minutes)
    {
        Recording recording = RecordingFactory.Started();
        recording.Extend(Ends.AddMinutes(10));

        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => recording.Extend(Ends.AddMinutes(10 + minutes)));

        Assert.Equal("expectedWindowEnd", refusal.ParamName);
        Assert.Equal(Ends.AddMinutes(10), recording.ExpectedWindowEnd);
        Assert.Equal(Ends, recording.PromisedWindowEnd);
    }

    [Fact]
    public void ARehydratedRecordingKeepsThePromiseItWasReadWith()
    {
        Recording recording = Rehydrated(Ends.AddMinutes(30), Ends);

        Assert.Equal(Ends.AddMinutes(30), recording.ExpectedWindowEnd);
        Assert.Equal(Ends, recording.PromisedWindowEnd);
    }

    [Fact]
    public void APromiseLaterThanTheEndARecordingHasIsRefused()
        => Assert.Equal(
            "promisedWindowEnd",
            Assert.Throws<ArgumentException>(() => Rehydrated(Ends, Ends.AddSeconds(1))).ParamName);

    [Fact]
    public void APromiseThatDoesNotComeAfterTheWindowStartsIsRefused()
        => Assert.Equal(
            "promisedWindowEnd",
            Assert.Throws<ArgumentException>(() => Rehydrated(Ends, Starts)).ParamName);

    private static Recording Rehydrated(DateTime expectedEnd, DateTime promisedEnd)
    {
        RecordingId id = RecordingId.New();

        return Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            null,
            null,
            Now,
            null,
            null,
            0,
            0,
            [],
            Starts,
            expectedEnd,
            promisedEnd,
            null,
            [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            null,
            RecordingFactory.Tuner,
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone);
    }
}
