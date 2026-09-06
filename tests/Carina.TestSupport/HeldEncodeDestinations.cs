using Carina.Domain.Encodings;

namespace Carina.TestSupport;

public sealed class HeldEncodeDestinations : IEncodeDestinationRepository
{
    public List<EncodeDestination> Destinations { get; } = [];

    public Task<EncodeDestination?> FindAsync(EncodeDestinationId id, CancellationToken cancellationToken)
        => Task.FromResult(Destinations.FirstOrDefault(destination => destination.Id.Equals(id)));

    public Task<IReadOnlyList<EncodeDestination>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<EncodeDestination> listed = [.. Destinations.OrderBy(destination => destination.DefinedAt)];

        return Task.FromResult(listed);
    }

    public Task AddAsync(EncodeDestination destination, CancellationToken cancellationToken)
    {
        Destinations.Add(destination);

        return Task.CompletedTask;
    }

    public Task SaveAsync(EncodeDestination destination, CancellationToken cancellationToken)
    {
        if (!Destinations.Contains(destination))
        {
            Destinations.Add(destination);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(EncodeDestination destination, CancellationToken cancellationToken)
    {
        Destinations.Remove(destination);

        return Task.CompletedTask;
    }
}
