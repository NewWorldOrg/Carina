using Carina.Domain.Captions;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// Reads whether learning is on, whether any recording is being written, whether anybody is watching
/// through a transcoder, and the earliest start, margin included, of the reservations still to be
/// recorded that reach the next <see cref="SpareTime.NoReservationWithin"/>.
/// </summary>
public sealed class OccupancyReader(
    ILearningSwitch learning,
    IRecordingRepository recordings,
    IReservationRepository reservations,
    IWatching watching) : IOccupancyReader
{
    public async Task<Occupancy> ReadAsync(DateTime now, CancellationToken cancellationToken)
    {
        bool on = await learning.IsOnAsync(cancellationToken);
        bool recording = (await recordings.ListInFlightAsync(cancellationToken)).Count > 0;
        IReadOnlyList<Reservation> pending = await reservations.ListPendingAsync(
            new ReservationWindow(now, now + SpareTime.NoReservationWithin + Margin.Longest),
            cancellationToken);
        DateTime? next = pending.Count > 0 ? pending.Min(reservation => reservation.EffectiveStartAt) : null;

        return new Occupancy(on, recording, watching.Anyone, next);
    }
}
