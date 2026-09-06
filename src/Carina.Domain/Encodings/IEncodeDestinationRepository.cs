namespace Carina.Domain.Encodings;

public interface IEncodeDestinationRepository
{
    Task<EncodeDestination?> FindAsync(EncodeDestinationId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<EncodeDestination>> ListAsync(CancellationToken cancellationToken);

    Task AddAsync(EncodeDestination destination, CancellationToken cancellationToken);

    Task SaveAsync(EncodeDestination destination, CancellationToken cancellationToken);

    Task RemoveAsync(EncodeDestination destination, CancellationToken cancellationToken);
}
