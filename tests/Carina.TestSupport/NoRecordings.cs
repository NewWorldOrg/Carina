using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.TestSupport;

/// <summary>
/// A recording ledger with nothing running in it, for the tests that weigh a plan without any
/// recording under way.
/// </summary>
public sealed class NoRecordings : IRecordingRepository
{
    public Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
        => Task.FromResult<Recording?>(null);

    public Task<IReadOnlyList<Recording>> ListInFlightAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Recording>>([]);

    public Task<IReadOnlyList<Recording>> ListForReservationAsync(
        ReservationId reservationId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Recording>>([]);

    public Task AddAsync(Recording recording, CancellationToken cancellationToken)
        => throw new NotSupportedException("Nothing is recording in this ledger.");

    public Task SaveAsync(Recording recording, CancellationToken cancellationToken)
        => throw new NotSupportedException("Nothing is recording in this ledger.");
}
