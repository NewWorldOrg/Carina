using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// Reads whether learning is on, beside what <see cref="IBusynessReader"/> reads for every pass over ended
/// recordings.
/// </summary>
public sealed class OccupancyReader(ILearningSwitch learning, IBusynessReader busyness) : IOccupancyReader
{
    public async Task<Occupancy> ReadAsync(DateTime now, CancellationToken cancellationToken)
    {
        bool on = await learning.IsOnAsync(cancellationToken);
        Busyness busy = await busyness.ReadAsync(now, cancellationToken);

        return new Occupancy(on, busy.Recording, busy.Watching, busy.NextReservationStartsAt);
    }
}
