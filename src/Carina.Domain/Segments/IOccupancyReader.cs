namespace Carina.Domain.Segments;

/// <summary>
/// Reads what decides whether it is spare time for the heavy work of learning.
/// </summary>
public interface IOccupancyReader
{
    Task<Occupancy> ReadAsync(DateTime now, CancellationToken cancellationToken);
}
