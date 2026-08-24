using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingGuardTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    [Fact]
    public void TheLengthsAreTheOnesTheFileSystemAndTheWireGive()
    {
        Assert.Equal(255, RecordingFileName.MaxLength);
        Assert.Equal(64, OutputRoot.MaxLength);
        Assert.Equal(64, TunerDeviceId.MaxLength);

        Assert.Equal(255, new RecordingFileName(new string('a', 255)).Value.Length);
        Assert.Throws<ArgumentException>(() => new RecordingFileName(new string('a', 256)));

        Assert.Equal(64, new OutputRoot(new string('a', 64)).Value.Length);
        Assert.Throws<ArgumentException>(() => new OutputRoot(new string('a', 65)));

        Assert.Equal(64, new TunerDeviceId(new string('a', 64)).Value.Length);
        Assert.Throws<ArgumentException>(() => new TunerDeviceId(new string('a', 65)));
    }

    [Fact]
    public void ASettledRecordingIsNotMeasuredAgainNineDaysLater()
    {
        Recording recording = Settled();

        Assert.Throws<InvalidOperationException>(() => recording.Measure(
            DropCounters.Counted(4, 900),
            DropTimeline.Unlocated,
            null,
            0,
            Now.AddDays(9)));
    }

    [Fact]
    public void ASettledRecordingTakesNoFurtherReason()
    {
        Recording recording = Settled();

        Assert.Throws<InvalidOperationException>(() => recording.Note(RecordingFactory.Fault()));
        Assert.Single(recording.OutcomeDetail);
    }

    [Fact]
    public void NothingCountsBackwardsWhenARecordingIsMeasured()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal(
            "scrambledPackets",
            Assert.Throws<ArgumentOutOfRangeException>(() => recording.Measure(
                DropCounters.Unmeasured,
                DropTimeline.Unlocated,
                -1,
                0,
                Now)).ParamName);
        Assert.Equal(
            "eovfCount",
            Assert.Throws<ArgumentOutOfRangeException>(() => recording.Measure(
                DropCounters.Unmeasured,
                DropTimeline.Unlocated,
                null,
                -1,
                Now)).ParamName);
    }

    [Fact]
    public void AFileIsNotSmallerThanEmptyWhenARecordingSettles()
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(Now);

        Assert.Equal(
            "fileSizeObserved",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => recording.Settle(RecordingOutcome.Complete, -1, Now)).ParamName);
    }

    [Theory]
    [InlineData("writtenDurationMs")]
    [InlineData("resumeCount")]
    [InlineData("fileSizeObserved")]
    [InlineData("scrambledPackets")]
    [InlineData("eovfCount")]
    public void NothingCountsBackwardsOnARehydratedRecording(string parameter)
        => Assert.Equal(parameter, Assert.Throws<ArgumentOutOfRangeException>(() => Rehydrated(parameter)).ParamName);

    [Fact]
    public void ARoleTheLedgerDoesNotHoldIsRefused()
        => Assert.Equal(
            "broadcastGroupRole",
            Assert.Throws<ArgumentOutOfRangeException>(() => Rehydrated("broadcastGroupRole")).ParamName);

    private static Recording Settled()
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault());
        recording.Settle(RecordingOutcome.Truncated, 1_200_000, Now.AddHours(1));

        return recording;
    }

    private static Recording Rehydrated(string parameter)
    {
        RecordingId id = RecordingId.New();

        return Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            parameter is "fileSizeObserved" ? -1 : null,
            parameter is "fileSizeObserved" ? Now : null,
            Now,
            null,
            null,
            parameter is "writtenDurationMs" ? -1 : 0,
            parameter is "resumeCount" ? -1 : 0,
            [],
            Now.AddMinutes(-5),
            Now.AddMinutes(55),
            null,
            [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            parameter is "scrambledPackets" ? -1 : null,
            parameter is "eovfCount" ? -1 : 0,
            null,
            RecordingFactory.Tuner,
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            parameter is "broadcastGroupRole" ? (BroadcastGroupRole)99 : BroadcastGroupRole.Standalone);
    }
}
