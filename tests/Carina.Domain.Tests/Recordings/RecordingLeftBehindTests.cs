using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingLeftBehindTests
{
    private static readonly DateTime Later = RecordingFactory.Now.AddHours(1);

    [Fact]
    public void AnErasureThatLeftFilesBehindIsKeptWithWhenItWasAskedForAndHowManyStayed()
    {
        Recording recording = Settled();

        recording.Erased(LeftBehind(2), Later.AddMinutes(5));

        Assert.Equal(Later.AddMinutes(5), recording.LeftBehindAt);
        Assert.Equal(2, recording.FilesLeftBehind);
    }

    [Fact]
    public void AnErasureThatCouldNotCountWhatItLeftStillSaysItLeftSomething()
    {
        Recording recording = Settled();

        recording.Erased(RecordingErasure.Refused(ErasureFault.FileLeftBehind, "permission denied"), Later);

        Assert.Equal(Later, recording.LeftBehindAt);
        Assert.Null(recording.FilesLeftBehind);
    }

    [Fact]
    public void ALaterErasureThatLeftFilesBehindIsTheOneKept()
    {
        Recording recording = Settled();

        recording.Erased(LeftBehind(2), Later);
        recording.Erased(LeftBehind(1), Later.AddMinutes(10));

        Assert.Equal(Later.AddMinutes(10), recording.LeftBehindAt);
        Assert.Equal(1, recording.FilesLeftBehind);
    }

    [Fact]
    public void AnErasureThatTookEverythingClearsWhatAnEarlierOneLeftBehind()
    {
        Recording recording = Settled();
        recording.Erased(LeftBehind(2), Later);

        recording.Erased(RecordingErasure.Erased(2), Later.AddMinutes(10));

        Assert.Null(recording.LeftBehindAt);
        Assert.Null(recording.FilesLeftBehind);
    }

    [Theory]
    [InlineData(ErasureFault.RootOutOfReach)]
    [InlineData(ErasureFault.DriverUnreachable)]
    [InlineData(ErasureFault.DriverRefused)]
    public void AnErasureThatTouchedNothingLeavesWhatAnEarlierOneLeftBehindAsItWas(ErasureFault fault)
    {
        Recording recording = Settled();
        recording.Erased(LeftBehind(2), Later);

        recording.Erased(RecordingErasure.Refused(fault, "nothing was touched"), Later.AddMinutes(10));

        Assert.Equal(Later, recording.LeftBehindAt);
        Assert.Equal(2, recording.FilesLeftBehind);
    }

    [Theory]
    [InlineData(ErasureFault.RootOutOfReach)]
    [InlineData(ErasureFault.DriverUnreachable)]
    [InlineData(ErasureFault.DriverRefused)]
    public void AnErasureThatTouchedNothingSaysNothingWasLeftBehindOnARecordingNobodyTriedToThrowAway(
        ErasureFault fault)
    {
        Recording recording = Settled();

        recording.Erased(RecordingErasure.Refused(fault, "nothing was touched"), Later);

        Assert.Null(recording.LeftBehindAt);
        Assert.Null(recording.FilesLeftBehind);
    }

    [Fact]
    public void ARecordingStillBeingWrittenKeepsNoErasure()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Throws<InvalidOperationException>(() => recording.Erased(LeftBehind(1), Later));
        Assert.Null(recording.LeftBehindAt);
    }

    [Fact]
    public void AnErasureIsNotKeptAtATimeBeforeTheRecordingBegan()
    {
        Recording recording = Settled();

        Assert.Throws<ArgumentException>(
            () => recording.Erased(LeftBehind(1), RecordingFactory.Now.AddMinutes(-1)));
    }

    [Fact]
    public void OnlyAnErasureThatLeftAFileBehindCountsWhatItLeft()
    {
        Assert.Throws<ArgumentException>(
            () => RecordingErasure.Refused(ErasureFault.RootOutOfReach, "the mount has gone", 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RecordingErasure.Refused(ErasureFault.FileLeftBehind, "permission denied", 0));
        Assert.True(LeftBehind(1).LeftFilesBehind);
        Assert.False(RecordingErasure.Erased(1).LeftFilesBehind);
    }

    [Fact]
    public void ARowCannotCountFilesLeftBehindWithoutSayingWhenOrOnARecordingStillBeingWritten()
    {
        Recording settled = Settled();

        Assert.Throws<ArgumentException>(() => Again(settled, leftBehindAt: null, filesLeftBehind: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Again(settled, Later, filesLeftBehind: 0));
        Assert.Throws<ArgumentException>(() => Again(RecordingFactory.Started(), Later, filesLeftBehind: 1));
        Assert.Equal(3, Again(settled, Later, filesLeftBehind: 3).FilesLeftBehind);
        Assert.Null(Again(settled, Later, filesLeftBehind: null).FilesLeftBehind);
    }

    private static RecordingErasure LeftBehind(int files)
        => RecordingErasure.Refused(ErasureFault.FileLeftBehind, "permission denied", files);

    private static Recording Settled()
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault());
        recording.Settle(RecordingOutcome.Truncated, 1_200_000, Later);

        return recording;
    }

    private static Recording Again(Recording held, DateTime? leftBehindAt, int? filesLeftBehind)
        => Recording.Rehydrate(
            held.Id,
            held.ReservationId,
            held.Programme,
            held.OutputRoot,
            held.FileName,
            held.FileSizeObserved,
            held.ObservedAt,
            held.StartedAtActual,
            held.StoppedAtActual,
            held.AbortedAt,
            held.WrittenDurationMs,
            held.ResumeCount,
            held.Interruptions,
            held.ExpectedWindowStart,
            held.ExpectedWindowEnd,
            held.PromisedWindowEnd,
            held.Outcome,
            held.OutcomeDetail,
            held.Counters,
            held.Positions,
            held.ScrambledPackets,
            held.EovfCount,
            held.MeasuredUpdatedAt,
            held.TunerDeviceId,
            held.ThumbnailState,
            RecordingFactory.Snapshot(),
            held.BroadcastGroupKey,
            held.BroadcastGroupRole,
            held.ThumbnailFault,
            leftBehindAt,
            filesLeftBehind);
}
