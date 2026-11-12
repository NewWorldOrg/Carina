using Carina.Domain.Recordings;

namespace Carina.Domain.Reservations;

/// <summary>
/// Writes the reservation outcome ledger: at most one line for each reservation and kind, and, for a reservation whose
/// recording failed, the faults of every recording made of it.
/// </summary>
public sealed class ReservationOutcomeDomainService(
    IReservationOutcomeRepository outcomes,
    IRecordingRepository recordings)
{
    /// <summary>
    /// Writes <paramref name="outcome"/> unless the reservation it is about already has a line of the same kind, and
    /// says whether it was written.
    /// </summary>
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

    /// <summary>
    /// Writes a line of <paramref name="kind"/> carrying nothing beyond the reservation, unless one is already there.
    /// </summary>
    public Task<bool> RecordOnceAsync(
        Reservation reservation,
        ReservationOutcomeKind kind,
        DateTime at,
        CancellationToken cancellationToken)
        => RecordOnceAsync(
            ReservationOutcome.Record(ReservationOutcomeId.New(), reservation, kind, null, null, [], [], at),
            cancellationToken);

    /// <summary>
    /// Every fault the recordings made of <paramref name="reservation"/> were left with, each once, in the order the
    /// recordings were made.
    /// </summary>
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
