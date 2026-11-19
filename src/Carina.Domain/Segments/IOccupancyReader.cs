namespace Carina.Domain.Segments;

public interface IOccupancyReader
{
    Task<Occupancy> ReadAsync(DateTime now, CancellationToken cancellationToken);
}
