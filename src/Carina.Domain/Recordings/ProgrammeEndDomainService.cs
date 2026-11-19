using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Recordings;

public sealed class ProgrammeEndDomainService(IAnnouncedProgrammes programmes, IReservationRepository reservations)
{
    public async Task<DateTime?> EndsAtAsync(Recording recording, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        Programme? announced = await programmes.FindAsync(
            new ProgrammeId(recording.NetworkId, recording.ServiceId, recording.EventId),
            cancellationToken);
        Reservation? reservation = recording.ReservationId is { } reserved
            ? await reservations.FindAsync(reserved, cancellationToken)
            : null;

        return ProgrammeEnd.Of(
            recording.ProgrammeStartsAt,
            announced?.EndsAt,
            reservation?.EndAt,
            reservation?.EndAtConfirmed is true,
            recording.PromisedWindowEnd);
    }
}
