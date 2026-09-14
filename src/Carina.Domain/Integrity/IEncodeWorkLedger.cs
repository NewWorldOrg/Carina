namespace Carina.Domain.Integrity;

public interface IEncodeWorkLedger
{
    Task<IReadOnlyList<DeclaredFile>> ListAsync(CancellationToken cancellationToken);
}
