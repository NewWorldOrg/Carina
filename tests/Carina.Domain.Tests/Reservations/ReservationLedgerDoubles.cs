using Carina.Domain.Base;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

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
