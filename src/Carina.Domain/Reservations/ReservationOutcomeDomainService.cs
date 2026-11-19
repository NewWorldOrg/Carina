using Carina.Domain.Recordings;

namespace Carina.Domain.Reservations;

public sealed class ReservationOutcomeDomainService(
    IReservationOutcomeRepository outcomes,
    IRecordingRepository recordings)
{
    public async Task<bool> RecordOnceAsync(ReservationOutcome outcome, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        IReadOnlyList<ReservationOutcome> already =
            await outcomes.ListForReservationAsync(outcome.ReservationId, cancellationToken);

        if (already.Any(written => written.Kind == outcome.Kind))
        {
            return false;
        }

        await outcomes.AddAsync(outcome, cancellationToken);

        return true;
    }

    public Task<bool> RecordOnceAsync(
        Reservation reservation,
        ReservationOutcomeKind kind,
        DateTime at,
        CancellationToken cancellationToken)
        => RecordOnceAsync(
            ReservationOutcome.Record(ReservationOutcomeId.New(), reservation, kind, null, null, [], [], at),
            cancellationToken);

    public async Task<IReadOnlyList<RecordingFault>> WhyItFailedAsync(
        ReservationId reservation,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Recording> written = await recordings.ListForReservationAsync(reservation, cancellationToken);

        return
        [
            .. written
                .SelectMany(recording => recording.OutcomeDetail)
                .Select(detail => detail.Fault)
                .Distinct(),
        ];
    }
}
