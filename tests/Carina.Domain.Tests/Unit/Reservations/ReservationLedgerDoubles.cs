using Carina.Domain.Base;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Domain.Tests.Unit.Reservations;

internal sealed class HeldReservationOutcomes : IReservationOutcomeRepository
{
    public List<ReservationOutcome> Outcomes { get; } = [];

    public Task AddAsync(ReservationOutcome outcome, CancellationToken cancellationToken)
    {
        Outcomes.Add(outcome);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReservationOutcome>> ListAsync(OutcomeSpan span, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ReservationOutcome>>([.. Outcomes]);

    public Task<PaginatedList<ReservationOutcome>> ListAsync(
        ReservationOutcomeQuery query,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("The ledger is read by reservation here.");

    public Task<IReadOnlyList<ReservationOutcome>> ListForReservationAsync(
        ReservationId reservationId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ReservationOutcome>>(
            [.. Outcomes.Where(outcome => outcome.ReservationId.Equals(reservationId))]);
}

internal sealed class HeldRecordingLedger : IRecordingRepository
{
    public List<Recording> Recordings { get; } = [];

    public Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
        => Task.FromResult(Recordings.FirstOrDefault(recording => recording.Id.Equals(id)));

    public Task<IReadOnlyList<Recording>> ListInFlightAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Recording>>([.. Recordings.Where(recording => recording.Outcome is null)]);

    public Task<IReadOnlyList<Recording>> ListForReservationAsync(
        ReservationId reservationId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Recording>>(
            [.. Recordings.Where(recording => reservationId.Equals(recording.ReservationId))]);

    public Task AddAsync(Recording recording, CancellationToken cancellationToken)
    {
        Recordings.Add(recording);

        return Task.CompletedTask;
    }

    public Task SaveAsync(Recording recording, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class HeldReservationLedger : IReservationRepository
{
    public List<Reservation> Reservations { get; } = [];

    public Task<PaginatedList<Reservation>> ListAsync(ReservationQuery query, CancellationToken cancellationToken)
        => throw new NotSupportedException("The ledger is not paged here.");

    public Task<ReservationHealth> HealthAsync(DateTime at, CancellationToken cancellationToken)
        => throw new NotSupportedException("The ledger's health is not read here.");

    public Task<Reservation?> FindAsync(ReservationId id, CancellationToken cancellationToken)
        => Task.FromResult(Reservations.FirstOrDefault(reservation => reservation.Id.Equals(id)));

    public Task<Reservation?> FindByProgrammeAsync(ProgrammeRef programme, CancellationToken cancellationToken)
        => Task.FromResult(
            Reservations.FirstOrDefault(reservation => reservation.Programme.Equals(programme))
            ?? Reservations.FirstOrDefault(reservation => reservation.Programme.Id.Equals(programme.Id)
                                                          && reservation.IsPendingWithin(new ReservationWindow(DateTime.MinValue, DateTime.MaxValue))
                                                          && !reservation.IsPinned));

    public Task<IReadOnlyList<Reservation>> ListPendingAsync(ReservationWindow window, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Reservation>>(
            [.. Reservations.Where(reservation => reservation.IsPendingWithin(window)).OrderBy(reservation => reservation.StartAt)]);

    public Task<IReadOnlyList<ReservationAwaitingOutcome>> ListAwaitingOutcomeAsync(
        DateTime through,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Outcomes are not judged here.");

    public Task<IReadOnlyList<Reservation>> ListClaimedOverAsync(
        ReservationWindow window,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Claims are not read here.");

    public Task<IReadOnlyList<Reservation>> ListForRuleAsync(RuleId ruleId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Reservation>>(
            [.. Reservations.Where(reservation => ruleId.Equals(reservation.RuleId))]);

    public Task<IReadOnlyList<Reservation>> ListForBroadcastGroupAsync(
        BroadcastGroupKey key,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Reservation>>(
            [.. Reservations.Where(reservation => key.Equals(reservation.BroadcastGroupKey))]);

    public Task AddAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        Reservations.Add(reservation);

        return Task.CompletedTask;
    }

    public Task SaveAsync(Reservation reservation, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SaveAllAsync(IReadOnlyList<Reservation> reservations, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task WithdrawAsync(IReadOnlyList<Reservation> reservations, CancellationToken cancellationToken)
    {
        Reservations.RemoveAll(reservations.Contains);

        return Task.CompletedTask;
    }

    public Task<ReservationDiscard> DiscardAsync(ReservationId id, DateTime at, CancellationToken cancellationToken)
        => throw new NotSupportedException("Nothing is thrown away here.");
}
