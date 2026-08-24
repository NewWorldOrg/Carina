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
        Assert.Throws<ArgumentException>(() => Record(ReservationOutcomeKind.TuneFailure, null, null, []));

        ReservationOutcome outcome = Record(
            ReservationOutcomeKind.TuneFailure,
            TuneFailureKind.IncompletePsi,
            null,
            []);

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

    private static ReservationOutcome Record(
        ReservationOutcomeKind kind,
        TuneFailureKind? tuneFailure,
        RecordingOutcome? recordingOutcome,
        IReadOnlyList<Guid> recordedInstead)
        => ReservationOutcome.Record(
            ReservationOutcomeId.New(),
            ReservationFactory.Planned(),
            kind,
            tuneFailure,
            recordingOutcome,
            recordedInstead,
            ReservationFactory.Now);
}
