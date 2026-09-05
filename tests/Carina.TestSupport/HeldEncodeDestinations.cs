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
}
