namespace Carina.Domain.Quality;

public interface IQualitySupplyReader
{
    Task<IReadOnlyList<SupplyReading>> ReadAsync(CancellationToken cancellationToken);
}
