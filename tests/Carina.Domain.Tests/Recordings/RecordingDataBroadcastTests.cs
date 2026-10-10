using Carina.Domain.DataBroadcast;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingDataBroadcastTests
{
    private static readonly DateTime Ends = RecordingFactory.Now.AddHours(1);

    private static readonly DateTime Later = RecordingFactory.Now.AddHours(2);

    [Fact(DisplayName = "BR-BS-001: a recording still being written has no record of its data broadcast due")]
    public void ARecordingStillBeingWrittenHasNoRecordDue()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal((DataBroadcastState.None, (DateTime?)null, (int?)null, 0), Of(recording));
        Assert.Throws<InvalidOperationException>(() => recording.DataBroadcastTaken(3, Later));
        Assert.Throws<InvalidOperationException>(() => recording.DataBroadcastFailed(Later));
        Assert.Throws<InvalidOperationException>(() => recording.DataBroadcastAgain());
    }

    [Theory(DisplayName = "BR-BS-001: a recording that ends has the record of its data broadcast coming, however it ended")]
    [InlineData(RecordingOutcome.Complete)]
    [InlineData(RecordingOutcome.Truncated)]
    [InlineData(RecordingOutcome.Failed)]
    public void ARecordingThatEndsHasItsRecordComing(RecordingOutcome outcome)
    {
        Recording recording = Ended(outcome);

        Assert.Equal((DataBroadcastState.Coming, (DateTime?)null, (int?)null, 0), Of(recording));
    }

    [Fact(DisplayName = "BR-BS-001: a record taken with modules is made and says how many and when")]
    public void ARecordTakenWithModulesIsMadeAndSaysHowManyAndWhen()
    {
        Recording recording = Ended();

        recording.DataBroadcastTaken(36, Later);

        Assert.Equal((DataBroadcastState.Made, (DateTime?)Later, (int?)36, 0), Of(recording));
        Assert.Equal(recording.DataBroadcast, new DataBroadcastProgress(DataBroadcastState.Made, 0, 36));
    }

    [Fact(DisplayName = "BR-BS-001: a record taken with no modules is missing")]
    public void ARecordTakenWithNoModulesIsMissing()
    {
        Recording recording = Ended();

        recording.DataBroadcastTaken(0, Later);

        Assert.Equal((DataBroadcastState.Missing, (DateTime?)Later, (int?)null, 0), Of(recording));
    }

    [Fact(DisplayName = "BR-BS-001: failures are counted in all, three tries and no more, and a record made forgets them")]
    public void FailuresAreCountedInAllThreeTriesAndNoMore()
    {
        Recording recording = Ended();

        recording.DataBroadcastFailed(Later);
        recording.DataBroadcastAgain();
        recording.DataBroadcastFailed(Later.AddMinutes(5));

        Assert.Equal((DataBroadcastState.Failed, (DateTime?)Later.AddMinutes(5), (int?)null, 2), Of(recording));

        recording.DataBroadcastAgain();

        Assert.Equal((DataBroadcastState.Coming, (DateTime?)null, (int?)null, 2), Of(recording));

        recording.DataBroadcastFailed(Later.AddMinutes(10));

        Assert.Throws<InvalidOperationException>(() => recording.DataBroadcastAgain());
        Assert.Equal(3, recording.DataBroadcastAttempts);
    }

    [Fact(DisplayName = "BR-BS-001: a record that is made and no longer kept comes again")]
    public void ARecordMadeAndNoLongerKeptComesAgain()
    {
        Recording recording = Ended();
        recording.DataBroadcastTaken(2, Later);

        recording.DataBroadcastAgain();

        Assert.Equal((DataBroadcastState.Coming, (DateTime?)null, (int?)null, 0), Of(recording));
    }

    [Fact(DisplayName = "BR-BS-001: a recording that ended with no record due, under a process that did not know it, has it coming")]
    public void ARecordingThatEndedWithNoRecordDueHasItComing()
    {
        Recording recording = Rehydrated(RecordingOutcome.Failed, DataBroadcastProgress.NotYet, null);

        recording.DataBroadcastAgain();

        Assert.Equal((DataBroadcastState.Coming, (DateTime?)null, (int?)null, 0), Of(recording));
    }

    [Fact]
    public void ARecordThatIsMissingDoesNotComeAgainUnlessTheRecordingIsDescrambled()
    {
        Recording recording = Ended();
        recording.DataBroadcastTaken(0, Later);

        Assert.Throws<InvalidOperationException>(() => recording.DataBroadcastAgain());
    }

    [Theory(DisplayName = "BR-BS-001: a record taken, found missing or failed is taken again once the recording is descrambled")]
    [InlineData(5)]
    [InlineData(0)]
    [InlineData(-1)]
    public void ARecordIsTakenAgainOnceTheRecordingIsDescrambled(int modules)
    {
        Recording recording = Ended(RecordingOutcome.Truncated, scrambled: true);
        Settle(recording, modules);

        recording.Descrambled(Later.AddDays(1));

        Assert.Equal(DataBroadcastState.Coming, recording.DataBroadcastState);
        Assert.Null(recording.DataBroadcastMadeAt);
        Assert.Null(recording.DataBroadcastModules);
    }

    [Fact]
    public void ARecordStillComingStaysComingWhenTheRecordingIsDescrambled()
    {
        Recording recording = Ended(RecordingOutcome.Truncated, scrambled: true);

        recording.Descrambled(Later);

        Assert.Equal((DataBroadcastState.Coming, (DateTime?)null, (int?)null, 0), Of(recording));
    }

    [Fact]
    public void AMomentBeforeTheRecordingBeganIsRefused()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(() => Ended().DataBroadcastTaken(1, RecordingFactory.Now.AddDays(-1))).ParamName);

    [Fact]
    public void ARecordingRehydratedWithNothingSaidAboutItsDataBroadcastHasItComingOnceItHasEnded()
    {
        Assert.Equal(DataBroadcastState.None, Rehydrated(null, null, null).DataBroadcastState);
        Assert.Equal(DataBroadcastState.Coming, Rehydrated(RecordingOutcome.Failed, null, null).DataBroadcastState);
    }

    [Fact(DisplayName = "BR-BS-001: a recording rehydrated with a record taken while it was being written is refused")]
    public void ARecordingRehydratedWithARecordTakenWhileItWasBeingWrittenIsRefused()
        => Assert.Equal(
            "outcome",
            Assert.Throws<ArgumentException>(() => Rehydrated(null, new DataBroadcastProgress(DataBroadcastState.Coming, 0, null), null)).ParamName);

    [Theory]
    [InlineData(DataBroadcastState.Made, false)]
    [InlineData(DataBroadcastState.Coming, true)]
    public void ARecordingRehydratedWithAMomentThatDoesNotFitItsRecordIsRefused(DataBroadcastState state, bool saysWhen)
        => Assert.Equal(
            "madeAt",
            Assert.Throws<ArgumentException>(() => Rehydrated(
                RecordingOutcome.Failed,
                new DataBroadcastProgress(state, 0, state is DataBroadcastState.Made ? 2 : null),
                saysWhen ? Later : null)).ParamName);

    private static void Settle(Recording recording, int modules)
    {
        if (modules < 0)
        {
            recording.DataBroadcastFailed(Later);

            return;
        }

        recording.DataBroadcastTaken(modules, Later);
    }

    private static Recording Ended(RecordingOutcome outcome = RecordingOutcome.Truncated, bool scrambled = false)
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(Ends);
        recording.Note(RecordingFactory.Fault(scrambled ? RecordingFault.ScramblingUnresolved : RecordingFault.DriverLost));
        recording.Settle(outcome, outcome is RecordingOutcome.Failed ? 0 : 1_000, Ends);

        return recording;
    }

    private static Recording Rehydrated(RecordingOutcome? outcome, DataBroadcastProgress? progress, DateTime? madeAt)
    {
        RecordingId id = RecordingId.New();
        bool ended = outcome is not null;

        return Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".ts"),
            ended ? 0 : null,
            ended ? Ends : null,
            RecordingFactory.Now,
            ended ? Ends : null,
            null,
            0,
            0,
            [],
            RecordingFactory.Now,
            Ends,
            Ends,
            outcome,
            ended ? [RecordingFactory.Fault()] : [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            null,
            RecordingFactory.Tuner,
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            dataBroadcast: progress,
            dataBroadcastMadeAt: madeAt);
    }

    private static (DataBroadcastState, DateTime?, int?, int) Of(Recording recording)
        => (recording.DataBroadcastState, recording.DataBroadcastMadeAt, recording.DataBroadcastModules, recording.DataBroadcastAttempts);
}
