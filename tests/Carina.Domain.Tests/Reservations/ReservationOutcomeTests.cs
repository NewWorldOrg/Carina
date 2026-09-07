using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationOutcomeTests
{
    [Fact]
    public void AnOutcomeCarriesTheReservationItRecordsRatherThanPointingAtIt()
    {
        Reservation reservation = ReservationFactory.Planned(marginBefore: Margin.OfSeconds(10));

        ReservationOutcome outcome = ReservationOutcome.Record(
            ReservationOutcomeId.New(),
            reservation,
            ReservationOutcomeKind.Missed,
            null,
            null,
            [],
            [],
            ReservationFactory.Now);

        Assert.Equal(reservation.Id, outcome.ReservationId);
        Assert.Equal(reservation.SnapshotName, outcome.SnapshotName);
        Assert.Equal(reservation.EffectiveStartAt, outcome.EffectiveStartAt);
        Assert.Equal(reservation.EffectiveEndAt, outcome.EffectiveEndAt);
        Assert.Equal(reservation.Priority, outcome.Priority);
    }

    [Fact]
    public void ATuneFailureIsRecordedWithWhichOfTheFourItWas()
    {
        Assert.Throws<ArgumentException>(
            () => Record(ReservationOutcomeKind.TuneFailure, null, null, [], [RecordingFault.TuneFailed]));

        ReservationOutcome outcome = Record(
            ReservationOutcomeKind.TuneFailure,
            TuneFailureKind.IncompletePsi,
            null,
            [],
            [RecordingFault.TuneFailed]);

        Assert.Equal(TuneFailureKind.IncompletePsi, outcome.TuneFailure);
    }

    [Fact]
    public void OnlyAReservationThatLostAContestNamesWhatWasRecordedInstead()
    {
        Guid winner = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => Record(ReservationOutcomeKind.Missed, null, null, [winner]));

        ReservationOutcome outcome = Record(ReservationOutcomeKind.Competing, null, null, [winner]);

        Assert.Equal([winner], outcome.RecordedInstead);
    }

    [Fact]
    public void AFailureReportedByRecordingCarriesTheOutcomeRecordingWrote()
    {
        Assert.Throws<ArgumentException>(() => Record(ReservationOutcomeKind.RecordingFailure, null, null, []));

        ReservationOutcome outcome = Record(
            ReservationOutcomeKind.RecordingFailure,
            null,
            RecordingOutcome.Truncated,
            []);

        Assert.Equal(RecordingOutcome.Truncated, outcome.RecordingOutcome);
    }

    [Fact]
    public void TheFourTuneFailuresAreKeptApart()
        => Assert.Equal(
            [
                TuneFailureKind.NoLock,
                TuneFailureKind.NoData,
                TuneFailureKind.IncompletePsi,
                TuneFailureKind.StreamMismatch,
            ],
            Enum.GetValues<TuneFailureKind>());

    [Fact]
    public void AFailureReportedByRecordingKeepsEveryClassItWasGiven()
    {
        ReservationOutcome outcome = Record(
            ReservationOutcomeKind.RecordingFailure,
            null,
            RecordingOutcome.Failed,
            [],
            [RecordingFault.NothingLanded, RecordingFault.ShortOfTheWindow]);

        Assert.Equal([RecordingFault.NothingLanded, RecordingFault.ShortOfTheWindow], outcome.Faults);
        Assert.Null(outcome.TuneFailure);
    }

    [Fact]
    public void ATuneFailureAndARecordingFailureAreToldApartByWhatEachOneCarries()
    {
        ReservationOutcome tuning = Record(
            ReservationOutcomeKind.TuneFailure,
            TuneFailureKind.NoLock,
            null,
            [],
            [RecordingFault.TuneFailed]);
        ReservationOutcome recording = Record(
            ReservationOutcomeKind.RecordingFailure,
            null,
            RecordingOutcome.Truncated,
            [],
            [RecordingFault.ShortOfTheWindow]);

        Assert.Null(tuning.RecordingOutcome);
        Assert.NotNull(tuning.TuneFailure);
        Assert.NotNull(recording.RecordingOutcome);
        Assert.Null(recording.TuneFailure);
    }

    [Fact]
    public void AClassificationTheLedgerDoesNotHoldIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => Record(ReservationOutcomeKind.Missed, null, null, [], [(RecordingFault)99]));

    [Fact]
    public void ATuneFailureNamesTheClassTheRecorderGaveIt()
        => Assert.Throws<ArgumentException>(
            () => Record(
                ReservationOutcomeKind.TuneFailure,
                TuneFailureKind.NoLock,
                null,
                [],
                [RecordingFault.TunerContended]));

    private static ReservationOutcome Record(
        ReservationOutcomeKind kind,
        TuneFailureKind? tuneFailure,
        RecordingOutcome? recordingOutcome,
        IReadOnlyList<Guid> recordedInstead,
        IReadOnlyList<RecordingFault>? faults = null)
        => ReservationOutcome.Record(
            ReservationOutcomeId.New(),
            ReservationFactory.Planned(),
            kind,
            tuneFailure,
            recordingOutcome,
            faults ?? [],
            recordedInstead,
            ReservationFactory.Now);
}
