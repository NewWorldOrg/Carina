using Carina.Domain.Captions;
using Carina.Domain.Recordings;
using Carina.Domain.Tests.Recordings;

namespace Carina.Domain.Tests.Captions;

public sealed class CaptionLedgerTests
{
    private static readonly DateTime Later = RecordingFactory.Now.AddHours(2);

    [Fact]
    public void BrPd017CaptionsStillWaitingAreComing()
        => Assert.Equal(CaptionStanding.Coming, CaptionLedger.StandingOf(Ended(), kept: false));

    [Fact]
    public void ARecordingStillBeingWrittenHasNoCaptionsComingYet()
        => Assert.Equal(CaptionStanding.None, CaptionLedger.StandingOf(RecordingFactory.Started(), kept: false));

    [Fact]
    public void BrPd017CaptionsReadyAndKeptAreReady()
    {
        Recording recording = Ended();
        recording.Caption(CaptionState.Ready, 3, Later);

        Assert.Equal(CaptionStanding.Ready, CaptionLedger.StandingOf(recording, kept: true));
    }

    [Fact]
    public void BrPd016CaptionsTheRowSaysAreReadyWithNoRecordKeptAreComingBecauseTheyAreTakenAgain()
    {
        Recording recording = Ended();
        recording.Caption(CaptionState.Ready, 3, Later);

        Assert.Equal(CaptionStanding.Coming, CaptionLedger.StandingOf(recording, kept: false));
    }

    [Fact]
    public void ARecordingWithNoCaptionsHasNone()
    {
        Recording recording = Ended();
        recording.Caption(CaptionState.Absent, null, Later);

        Assert.Equal(CaptionStanding.None, CaptionLedger.StandingOf(recording, kept: false));
    }

    [Fact]
    public void BrPd016AFailureIsComingUntilItHasFailedAsManyTimesAsAreTried()
    {
        Recording recording = Ended();

        for (int failed = 1; failed < CaptionSettings.TriesAtMost; failed++)
        {
            recording.Caption(CaptionState.Failed, null, Later.AddMinutes(failed));
            Assert.Equal(CaptionStanding.Coming, CaptionLedger.StandingOf(recording, kept: false));
        }

        recording.Caption(CaptionState.Failed, null, Later.AddHours(1));

        Assert.Equal(CaptionStanding.None, CaptionLedger.StandingOf(recording, kept: false));
    }

    [Fact]
    public void BrPd016CaptionsTakenBeforeTheFileWasDescrambledAreComingAgain()
    {
        Recording recording = Ended(scrambled: true);
        recording.Caption(CaptionState.Ready, 3, Later);

        recording.Descrambled(Later.AddHours(1));

        Assert.True(CaptionLedger.Awaited(recording));
        Assert.Equal(CaptionStanding.Coming, CaptionLedger.StandingOf(recording, kept: true));
    }

    [Fact]
    public void CaptionsTakenAfterTheFileWasDescrambledAreNotTakenAgain()
    {
        Recording recording = Ended(scrambled: true);
        recording.Descrambled(Later);

        recording.Caption(CaptionState.Ready, 3, Later.AddHours(1));

        Assert.False(CaptionLedger.Awaited(recording));
    }

    private static Recording Ended(bool scrambled = false)
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(RecordingFactory.Now.AddHours(1));
        recording.Note(RecordingFactory.Fault(scrambled ? RecordingFault.ScramblingUnresolved : RecordingFault.DriverLost));
        recording.Settle(RecordingOutcome.Truncated, 1_000, RecordingFactory.Now.AddHours(1));

        return recording;
    }
}
