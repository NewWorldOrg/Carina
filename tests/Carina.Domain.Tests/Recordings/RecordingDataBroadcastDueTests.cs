using Carina.Domain.DataBroadcast;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingDataBroadcastDueTests
{
    private static readonly DateTime Ending = RecordingFactory.Now.AddHours(1);

    private static readonly DateTime Later = RecordingFactory.Now.AddHours(2);

    [Fact(DisplayName = "BR-BS-001: a recording still being written has no record of its data broadcast due in any way")]
    public void ARecordingStillBeingWrittenHasNothingDue()
    {
        Recording recording = RecordingFactory.Started();

        Assert.False(recording.AwaitsItsDataBroadcast);
        Assert.False(recording.DataBroadcastNeverCameDue);
        Assert.False(recording.DataBroadcastIsDueAgain);
    }

    [Theory(DisplayName = "BR-BS-001: the data broadcast of a recording that has ended is awaited")]
    [InlineData(RecordingOutcome.Truncated)]
    [InlineData(RecordingOutcome.Failed)]
    public void TheDataBroadcastOfARecordingThatHasEndedIsAwaited(RecordingOutcome outcome)
    {
        Recording recording = Ended(outcome);

        Assert.True(recording.AwaitsItsDataBroadcast);
        Assert.False(recording.DataBroadcastNeverCameDue);
        Assert.False(recording.DataBroadcastIsDueAgain);
    }

    [Theory(DisplayName = "BR-BS-001: a record taken, made or missing, is awaited no longer")]
    [InlineData(0)]
    [InlineData(36)]
    public void ARecordTakenIsNoLongerAwaited(int modules)
    {
        Recording recording = Ended(RecordingOutcome.Truncated);

        recording.DataBroadcastTaken(modules, Later);

        Assert.False(recording.AwaitsItsDataBroadcast);
        Assert.False(recording.DataBroadcastIsDueAgain);
    }

    [Fact(DisplayName = "BR-BS-001: a record that failed is due again until it has failed three times in all")]
    public void ARecordThatFailedIsDueAgainUntilTheTriesRunOut()
    {
        Recording recording = Ended(RecordingOutcome.Truncated);

        for (int failed = 1; failed < DataBroadcastProgress.TriesAtMost; failed++)
        {
            recording.DataBroadcastFailed(Later.AddMinutes(failed));

            Assert.False(recording.AwaitsItsDataBroadcast);
            Assert.True(recording.DataBroadcastIsDueAgain);

            recording.DataBroadcastAgain();

            Assert.True(recording.AwaitsItsDataBroadcast);
        }

        recording.DataBroadcastFailed(Later.AddHours(1));

        Assert.False(recording.DataBroadcastIsDueAgain);
        Assert.False(recording.AwaitsItsDataBroadcast);
    }

    [Fact(DisplayName = "BR-BS-001: a recording that ended under a process that did not know the record has it never come due, until it is put to coming")]
    public void ARecordingThatEndedWithNoRecordDueNeverCameDue()
    {
        Recording recording = EndedWithNoRecordDue();

        Assert.True(recording.DataBroadcastNeverCameDue);
        Assert.False(recording.AwaitsItsDataBroadcast);

        recording.DataBroadcastAgain();

        Assert.False(recording.DataBroadcastNeverCameDue);
        Assert.True(recording.AwaitsItsDataBroadcast);
    }

    private static Recording Ended(RecordingOutcome outcome)
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(Ending);
        recording.Note(RecordingFactory.Fault());
        recording.Settle(outcome, outcome is RecordingOutcome.Failed ? 0 : 1_000, Ending);

        return recording;
    }

    private static Recording EndedWithNoRecordDue()
    {
        RecordingId id = RecordingId.New();

        return Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".ts"),
            1_000,
            Ending,
            RecordingFactory.Now,
            Ending,
            Ending,
            0,
            0,
            [],
            RecordingFactory.Now,
            Ending,
            Ending,
            RecordingOutcome.Truncated,
            [RecordingFactory.Fault()],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            null,
            new TunerDeviceId("pt3-0"),
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            Carina.Domain.Reservations.BroadcastGroupRole.Standalone,
            dataBroadcast: DataBroadcastProgress.NotYet);
    }
}
