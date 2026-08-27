using Carina.Domain.Rules;

namespace Carina.Domain.Reservations;

public sealed record ReservationWindow(DateTime From, DateTime To);

public interface IReservationRepository
{
    Task<Reservation?> FindAsync(ReservationId id, CancellationToken cancellationToken);

    Task<Reservation?> FindByProgrammeAsync(ProgrammeRef programme, CancellationToken cancellationToken);

    Task<IReadOnlyList<Reservation>> ListPendingAsync(ReservationWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<Reservation>> ListForRuleAsync(RuleId ruleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Reservation>> ListForBroadcastGroupAsync(
        BroadcastGroupKey key,
        CancellationToken cancellationToken);

    Task AddAsync(Reservation reservation, CancellationToken cancellationToken);

    Task SaveAsync(Reservation reservation, CancellationToken cancellationToken);

    Task SaveAllAsync(IReadOnlyList<Reservation> reservations, CancellationToken cancellationToken);

    Task WithdrawAsync(IReadOnlyList<Reservation> reservations, CancellationToken cancellationToken);
}
