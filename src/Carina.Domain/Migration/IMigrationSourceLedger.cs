namespace Carina.Domain.Migration;

public interface IMigrationSourceLedger
{
    Task<SourceLedger> ReadAsync(CancellationToken cancellationToken);
}
