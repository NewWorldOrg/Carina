namespace Carina.Domain.Scans;

public interface IChannelScanOrchestrator
{
    Task<ScanOutcome> RunAsync(ScanScope scope, CancellationToken cancellationToken);
}
