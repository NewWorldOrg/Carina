using Carina.Domain.Base;

namespace Carina.Domain.Reservations;

public sealed record OutcomeSpan(DateTime From, DateTime To, ReservationOutcomeKind? Kind);

public interface IReservationOutcomeRepository
{
    Task AddAsync(ReservationOutcome outcome, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReservationOutcome>> ListAsync(OutcomeSpan span, CancellationToken cancellationToken);

    Task<PaginatedList<ReservationOutcome>> ListAsync(
        ReservationOutcomeQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReservationOutcome>> ListForReservationAsync(
        ReservationId reservationId,
        CancellationToken cancellationToken);
}
