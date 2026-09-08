namespace Carina.Domain.Migration;

public interface IMigrationLease
{
    Task<IAsyncDisposable?> TakeAsync(CancellationToken cancellationToken);
}
