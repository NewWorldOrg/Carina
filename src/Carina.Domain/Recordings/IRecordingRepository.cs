using Carina.Domain.Reservations;

namespace Carina.Domain.Recordings;

public interface IRecordingRepository
{
    Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Recording>> ListInFlightAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Recording>> ListForReservationAsync(
        ReservationId reservationId,
        CancellationToken cancellationToken);

    Task AddAsync(Recording recording, CancellationToken cancellationToken);

    Task SaveAsync(Recording recording, CancellationToken cancellationToken);
}
