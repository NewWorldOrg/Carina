using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// Reads what <see cref="IBusynessReader"/> reads for every pass over ended recordings, for the heavy work of
/// learning to judge spare time by.
/// </summary>
public sealed class OccupancyReader(IBusynessReader busyness) : IOccupancyReader
{
    public async Task<Occupancy> ReadAsync(DateTime now, CancellationToken cancellationToken)
    {
        Busyness busy = await busyness.ReadAsync(now, cancellationToken);

        return new Occupancy(busy.Recording, busy.Watching, busy.NextReservationStartsAt);
    }
}
