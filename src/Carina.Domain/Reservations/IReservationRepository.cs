using Carina.Domain.Base;
using Carina.Domain.Rules;

namespace Carina.Domain.Reservations;

public sealed record ReservationWindow(DateTime From, DateTime To);

/// <summary>
/// A reservation whose outcome is not written down yet, together with whether a recording came of it.
/// The claim and the outcome on the reservation are the recording ledger's to write, so a claim that
/// never became a recording is only visible by looking for the recording that is not there.
/// </summary>
public sealed record ReservationAwaitingOutcome(Reservation Reservation, bool Recorded);

public enum ReservationDiscard
{
    Discarded = 1,

    NoSuchReservation = 2,

    TurningIntoARecording = 3,

    RecordingCameOfIt = 4,

    StillToBeRecorded = 5,
}

public interface IReservationRepository
{
    Task<PaginatedList<Reservation>> ListAsync(ReservationQuery query, CancellationToken cancellationToken);

    Task<ReservationHealth> HealthAsync(DateTime at, CancellationToken cancellationToken);

    Task<Reservation?> FindAsync(ReservationId id, CancellationToken cancellationToken);

    Task<Reservation?> FindByProgrammeAsync(ProgrammeRef programme, CancellationToken cancellationToken);

    Task<IReadOnlyList<Reservation>> ListPendingAsync(ReservationWindow window, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReservationAwaitingOutcome>> ListAwaitingOutcomeAsync(
        DateTime through,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Reservation>> ListClaimedOverAsync(
        ReservationWindow window,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Reservation>> ListForRuleAsync(RuleId ruleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Reservation>> ListForBroadcastGroupAsync(
        BroadcastGroupKey key,
        CancellationToken cancellationToken);

    Task AddAsync(Reservation reservation, CancellationToken cancellationToken);

    Task SaveAsync(Reservation reservation, CancellationToken cancellationToken);

    Task SaveAllAsync(IReadOnlyList<Reservation> reservations, CancellationToken cancellationToken);

    Task WithdrawAsync(IReadOnlyList<Reservation> reservations, CancellationToken cancellationToken);

    Task<ReservationDiscard> DiscardAsync(
        ReservationId id,
        DateTime at,
        CancellationToken cancellationToken);
}
