using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

public sealed class OccupancyReader(IBusynessReader busyness) : IOccupancyReader
{
    public async Task<Occupancy> ReadAsync(DateTime now, CancellationToken cancellationToken)
    {
        Busyness busy = await busyness.ReadAsync(now, cancellationToken);

        return new Occupancy(busy.Recording, busy.Watching, busy.NextReservationStartsAt);
    }
}
