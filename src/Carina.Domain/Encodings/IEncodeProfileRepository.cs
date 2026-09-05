namespace Carina.Domain.Encodings;

public interface IEncodeProfileRepository
{
    Task<EncodeProfile?> FindAsync(EncodeProfileId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<EncodeProfile>> ListAsync(CancellationToken cancellationToken);

    Task AddAsync(EncodeProfile profile, CancellationToken cancellationToken);
}
