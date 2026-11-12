using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Tests.Recordings;

using static Carina.Domain.Tests.Reservations.ReservationFactory;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationOutcomeDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly HeldReservationOutcomes outcomes = new();

    private readonly HeldRecordingLedger recordings = new();

    private ReservationOutcomeDomainService Ledger => new(outcomes, recordings);

    [Fact(DisplayName = "BR-RD-012: a reservation is written into the ledger once for each kind of outcome")]
    public async Task AReservationIsWrittenOnceForEachKind()
    {
        Reservation reservation = Planned();

        Assert.True(await Ledger.RecordOnceAsync(reservation, ReservationOutcomeKind.ProgrammeMoved, Now, Cancel));
        Assert.False(await Ledger.RecordOnceAsync(reservation, ReservationOutcomeKind.ProgrammeMoved, Now.AddMinutes(1), Cancel));
        Assert.True(await Ledger.RecordOnceAsync(reservation, ReservationOutcomeKind.ProgrammeGone, Now, Cancel));

        Assert.Equal(
            [ReservationOutcomeKind.ProgrammeMoved, ReservationOutcomeKind.ProgrammeGone],
            outcomes.Outcomes.Select(outcome => outcome.Kind));
        Assert.Equal(Now, outcomes.Outcomes[0].OccurredAt);
    }

    [Fact(DisplayName = "BR-RD-012: an outcome carrying its own detail is written once for its kind, as it was handed")]
    public async Task AnOutcomeCarryingItsDetailIsWrittenOnceAsHanded()
    {
        Reservation reservation = Planned();
        Guid instead = Guid.NewGuid();
        ReservationOutcome refused = ReservationOutcome.Record(
            ReservationOutcomeId.New(),
            reservation,
            ReservationOutcomeKind.Competing,
            null,
            null,
            [RecordingFault.TunerContended],
            [instead],
            Now);

        Assert.True(await Ledger.RecordOnceAsync(refused, Cancel));
        Assert.False(await Ledger.RecordOnceAsync(
            ReservationOutcome.Record(
                ReservationOutcomeId.New(),
                reservation,
                ReservationOutcomeKind.Competing,
                null,
                null,
                [],
                [],
                Now),
            Cancel));

        ReservationOutcome written = Assert.Single(outcomes.Outcomes);
        Assert.Same(refused, written);
        Assert.Equal([instead], written.RecordedInstead);
    }

    [Fact(DisplayName = "BR-RD-012: a failed recording's outcome names every fault of every recording made of the reservation, each once")]
    public async Task AFailedOutcomeNamesEveryFaultOnce()
    {
        var reservation = ReservationId.New();
        Recording first = RecordingFactory.Started(reservationId: reservation);
        first.Note(RecordingFactory.Fault(RecordingFault.DriverLost));
        Recording second = RecordingFactory.Started(reservationId: reservation);
        second.Note(RecordingFactory.Fault(RecordingFault.DriverLost));
        second.Note(RecordingFactory.Fault(RecordingFault.DiskExhausted));
        Recording another = RecordingFactory.Started(reservationId: ReservationId.New());
        another.Note(RecordingFactory.Fault(RecordingFault.TunerContended));
        recordings.Recordings.AddRange([first, second, another]);

        Assert.Equal([RecordingFault.DriverLost, RecordingFault.DiskExhausted], await Ledger.WhyItFailedAsync(reservation, Cancel));
        Assert.Empty(await Ledger.WhyItFailedAsync(ReservationId.New(), Cancel));
    }
}
