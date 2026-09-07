namespace Carina.Domain.Migration;

public interface IMigrationRecordRepository
{
    Task SaveAsync(MigrationReport report, CancellationToken cancellationToken);

    Task<MigrationRun?> LatestAsync(CancellationToken cancellationToken);

    Task<MigrationReport?> ReadAsync(MigrationRunId runId, CancellationToken cancellationToken);
}
