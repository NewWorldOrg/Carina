namespace Carina.Domain.Scans;

public interface IScanRunRepository
{
    Task<ScanRunStart> StartAsync(ScanRun run, CancellationToken cancellationToken);

    Task<ScanRun?> FindAsync(ScanRunId id, CancellationToken cancellationToken);

    Task<ScanRun?> FindRunningAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ScanRun>> ListRecentAsync(int limit, CancellationToken cancellationToken);

    Task SaveAsync(ScanRun run, CancellationToken cancellationToken);

    Task AddAttemptAsync(ScanRunAttempt attempt, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScanRunAttempt>> ListAttemptsAsync(ScanRunId id, CancellationToken cancellationToken);
}
