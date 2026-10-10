using Carina.Domain.Captions;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Infrastructure.Recordings;

/// <summary>
/// Reads whether any recording is being written, whether anybody is watching through a transcoder, and the
/// earliest start, margin included, of the reservations still to be recorded that reach the next
/// <see cref="Idleness.NoReservationWithin"/>.
/// </summary>
public sealed class BusynessReader(
    IRecordingRepository recordings,
    IReservationRepository reservations,
    IWatching watching) : IBusynessReader
{
    public async Task<Busyness> ReadAsync(DateTime now, CancellationToken cancellationToken)
    {
        bool recording = (await recordings.ListInFlightAsync(cancellationToken)).Count > 0;
        IReadOnlyList<Reservation> pending = await reservations.ListPendingAsync(
            new ReservationWindow(now, now + Idleness.NoReservationWithin + Margin.Longest),
            cancellationToken);
        DateTime? next = pending.Count > 0 ? pending.Min(reservation => reservation.EffectiveStartAt) : null;

        return new Busyness(recording, watching.Anyone, next);
    }
}
