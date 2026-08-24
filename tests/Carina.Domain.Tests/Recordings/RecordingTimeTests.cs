using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingTimeTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    private static readonly DateTime Began = Now;

    [Fact]
    public void ARecordingDoesNotStopBeforeItStarted()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(
                () => recording.Settle(RecordingOutcome.Failed, 0, Began.AddSeconds(-1))).ParamName);
    }

    [Fact]
    public void ARecordingIsNotAbortedBeforeItStarted()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(
                () => RecordingFactory.Started().Abort(Began.AddSeconds(-1))).ParamName);

    [Fact]
    public void ARecordingIsNotInterruptedBeforeItStarted()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(
                () => RecordingFactory.Started().Interrupt(RecordingFault.DriverLost, Began.AddSeconds(-1)))
                .ParamName);

    [Fact]
    public void ARecordingIsNotResumedBeforeItWasInterrupted()
    {
        Recording recording = RecordingFactory.Started();
        recording.Interrupt(RecordingFault.DriverLost, Now);

        Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(() => recording.Resume(Now.AddSeconds(-1))).ParamName);
    }

    [Fact]
    public void ARecordingIsNotMeasuredBeforeItStarted()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(() => RecordingFactory.Started().Measure(
                DropCounters.Counted(0, 10),
                DropTimeline.Unlocated,
                null,
                0,
                Began.AddSeconds(-1))).ParamName);

    [Theory]
    [InlineData("stoppedAtActual")]
    [InlineData("abortedAt")]
    [InlineData("observedAt")]
    [InlineData("measuredUpdatedAt")]
    public void ARehydratedRecordingHasNothingHappeningBeforeItStarted(string column)
    {
        DateTime before = Began.AddSeconds(-1);
        RecordingId id = RecordingId.New();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            column is "observedAt" ? 12 : null,
            column is "observedAt" ? before : null,
            Began,
            column is "stoppedAtActual" ? before : null,
            column is "abortedAt" ? before : null,
            0,
            0,
            [],
            Now.AddMinutes(-5),
            Now.AddMinutes(55),
            null,
            [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            column is "measuredUpdatedAt" ? before : null,
            RecordingFactory.Tuner,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone));

        Assert.Equal(column, refusal.ParamName);
    }

    [Fact]
    public void ARecordingThatStoppedTheMomentItStartedIsStillAllowed()
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(Began);
        recording.Note(RecordingFactory.Fault());

        recording.Settle(RecordingOutcome.Failed, 0, Began);

        Assert.Equal(Began, recording.StoppedAtActual);
    }
}
