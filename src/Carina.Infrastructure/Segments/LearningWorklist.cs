using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

public sealed class LearningWorklist(
    IRecordingRepository recordings,
    IAnnouncedProgrammes programmes,
    IReservationRepository reservations) : ILearningWorklist
{
    public async Task<IReadOnlyList<Recording>> BeingRecordedAsync(
        IReadOnlyList<OutputRoot> withinReach,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(withinReach);

        return
        [
            .. (await recordings.ListInFlightAsync(cancellationToken))
                .Where(recording => recording.IsInFlight && withinReach.Contains(recording.OutputRoot))
                .OrderBy(recording => recording.StartedAtActual)
                .ThenBy(recording => recording.Id.Value),
        ];
    }

    public async Task<RecordingStanding?> StandingAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        Recording? found = await recordings.FindAsync(id, cancellationToken);

        if (found is null)
        {
            return null;
        }

        return found.IsInFlight ? RecordingStanding.InFlight : RecordingStanding.Ended;
    }

    public async Task<DateTime?> ProgrammeEndsAtAsync(Recording recording, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        Programme? announced = await programmes.FindAsync(
            new ProgrammeId(recording.NetworkId, recording.ServiceId, recording.EventId),
            cancellationToken);

        if (After(announced?.EndsAt, recording) is { } guided)
        {
            return guided;
        }

        if (recording.ReservationId is not { } reserved)
        {
            return null;
        }

        Reservation? reservation = await reservations.FindAsync(reserved, cancellationToken);

        return reservation is { EndAtConfirmed: true } ? After(reservation.EndAt, recording) : null;
    }

    private static DateTime? After(DateTime? endsAt, Recording recording)
        => endsAt > recording.ProgrammeStartsAt ? endsAt : null;
}
