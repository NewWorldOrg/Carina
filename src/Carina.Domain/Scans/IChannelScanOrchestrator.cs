namespace Carina.Domain.Scans;

public interface IChannelScanOrchestrator
{
    Task<ScanOutcome> RunAsync(ScanScope scope, CancellationToken cancellationToken);

    Task<ScanOutcome> RunAsync(
        ScanScope scope,
        IScanRunObserver observer,
        CancellationToken cancellationToken);
}
