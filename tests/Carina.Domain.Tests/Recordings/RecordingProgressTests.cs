using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingProgressTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    [Fact(DisplayName = "BR-KD-030: a recording nothing has counted yet is counted as far as the moment it began")]
    public void ARecordingNothingCountedIsCountedAsFarAsItsStart()
        => Assert.Equal(Now, RecordingFactory.Started().CountedUntil);

    [Fact(DisplayName = "BR-KD-030: a recording is counted as far as the last count it took")]
    public void ARecordingIsCountedAsFarAsItsLastCount()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(0, 10), DropTimeline.Unlocated, null, 0, Now.AddMinutes(3));

        Assert.Equal(Now.AddMinutes(3), recording.CountedUntil);
    }

    [Fact(DisplayName = "BR-KD-005: a running recording takes a count from a moment later than the one it was last counted at, and not one at or before it")]
    public void ARunningRecordingTakesOnlyALaterCount()
    {
        Recording recording = RecordingFactory.Started();
        recording.Measure(DropCounters.Counted(0, 10), DropTimeline.Unlocated, null, 0, Now.AddMinutes(3));

        Assert.False(recording.KeepsCountingAt(Now.AddMinutes(3)));
        Assert.True(recording.KeepsCountingAt(Now.AddMinutes(3).AddTicks(1)));
    }

    [Fact(DisplayName = "BR-KD-005: a recording that is over takes no count, however late it comes")]
    public void ARecordingThatIsOverTakesNoCount()
    {
        Recording recording = RecordingFactory.Started();

        Assert.False(recording.KeepsCountingAt(recording.ExpectedWindowEnd));

        recording.Abort(Now.AddMinutes(1));

        Assert.False(recording.KeepsCountingAt(Now.AddMinutes(2)));
    }

    [Fact(DisplayName = "BR-KD-026: a gap is kept when it falls after the start and after every gap already kept")]
    public void AGapIsKeptWhenItFollowsEveryGapAlreadyKept()
    {
        Recording recording = RecordingFactory.Started();
        RecordingGap first = new(Now.AddMinutes(5), Now.AddMinutes(6));

        Assert.True(recording.Follows(first));

        recording.Missed(first);

        Assert.False(recording.Follows(first));
        Assert.False(recording.Follows(new RecordingGap(Now.AddMinutes(4), Now.AddMinutes(6))));
        Assert.False(recording.Follows(new RecordingGap(Now.AddMinutes(5).AddSeconds(30), Now.AddMinutes(7))));
        Assert.True(recording.Follows(new RecordingGap(Now.AddMinutes(6), Now.AddMinutes(7))));
    }

    [Fact(DisplayName = "BR-KD-026: a gap that begins before the recording began is not one of its own")]
    public void AGapBeforeTheStartIsNotKept()
        => Assert.False(RecordingFactory.Started().Follows(new RecordingGap(Now.AddTicks(-1), Now.AddMinutes(1))));

    [Fact(DisplayName = "BR-KD-003: a recording is interrupted while its last break is open, and not once it has resumed")]
    public void ARecordingIsInterruptedWhileItsLastBreakIsOpen()
    {
        Recording recording = RecordingFactory.Started();

        Assert.False(recording.IsInterrupted);

        recording.Interrupt(RecordingFault.DriverLost, Now.AddMinutes(1));

        Assert.True(recording.IsInterrupted);

        recording.Resume(Now.AddMinutes(2));

        Assert.False(recording.IsInterrupted);
    }

    [Fact(DisplayName = "BR-KD-028: a recording stopped being written at the last write to its file")]
    public void ARecordingStoppedBeingWrittenAtTheLastWrite()
        => Assert.Equal(
            Now.AddMinutes(4),
            RecordingFactory.Started().StoppedWritingAt(Now.AddMinutes(4), Now.AddMinutes(5)));

    [Fact(DisplayName = "BR-KD-028: a file that says nothing, or says it was written at or after the break was noticed, leaves the moment it was noticed")]
    public void AFileThatSaysNothingUsefulLeavesTheMomentItWasNoticed()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal(Now.AddMinutes(5), recording.StoppedWritingAt(null, Now.AddMinutes(5)));
        Assert.Equal(Now.AddMinutes(5), recording.StoppedWritingAt(Now.AddMinutes(5), Now.AddMinutes(5)));
        Assert.Equal(Now.AddMinutes(5), recording.StoppedWritingAt(Now.AddMinutes(6), Now.AddMinutes(5)));
    }

    [Fact(DisplayName = "BR-KD-028: a recording never stopped being written before it began, or before it last resumed")]
    public void ARecordingNeverStoppedBeforeItsLastMark()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal(Now, recording.StoppedWritingAt(Now.AddMinutes(-1), Now.AddMinutes(5)));

        recording.Interrupt(RecordingFault.DriverLost, Now.AddMinutes(1));
        recording.Resume(Now.AddMinutes(2));

        Assert.Equal(Now.AddMinutes(2), recording.StoppedWritingAt(Now.AddMinutes(1).AddSeconds(30), Now.AddMinutes(5)));

        recording.Interrupt(RecordingFault.DriverLost, Now.AddMinutes(3));

        Assert.Equal(Now.AddMinutes(3), recording.StoppedWritingAt(Now.AddMinutes(2).AddSeconds(30), Now.AddMinutes(5)));
    }

    [Fact(DisplayName = "BR-KD-004: a recording names the tuner it was first written on and keeps that one")]
    public void ARecordingKeepsTheFirstTunerItNames()
    {
        Recording recording = RecordingFactory.Unclaimed();

        recording.Adopt(string.Empty);

        Assert.Null(recording.TunerDeviceId);

        recording.Adopt("pt3-2");
        recording.Adopt("pt3-3");

        Assert.Equal(new TunerDeviceId("pt3-2"), recording.TunerDeviceId);
    }

    [Fact(DisplayName = "BR-KD-009: a programme whose end is still undecided is written down once, however often it is followed")]
    public void AnUndecidedEndIsWrittenDownOnce()
    {
        Recording recording = RecordingFactory.Started();

        recording.NoteEndUndecided(Now.AddMinutes(1));
        recording.NoteEndUndecided(Now.AddMinutes(2));

        OutcomeDetail noted = Assert.Single(recording.OutcomeDetail);
        Assert.Equal(RecordingFault.EndStillUndecided, noted.Fault);
        Assert.Equal(Now.AddMinutes(1), noted.NoticedAt);
    }

    [Fact(DisplayName = "BR-KD-010: a recording starts on a disk found short of room and says so")]
    public void ARecordingOnADiskFoundShortSaysSo()
    {
        Recording roomy = RecordingFactory.Started();
        Recording tight = RecordingFactory.Started();

        roomy.Prechecked(DiskPrecheckVerdict.Of(null, 1_000, 10_000, 1), Now);
        tight.Prechecked(DiskPrecheckVerdict.Of(DiskShortfall.ShortOfTheEstimate, 10_000, 1_000, 1), Now);

        Assert.Empty(roomy.OutcomeDetail);
        Assert.Equal(
            [new OutcomeDetail(RecordingFault.RefusedByDiskPrecheck, null, string.Empty, Now)],
            tight.OutcomeDetail);
    }
}
