using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;

namespace Carina.Infrastructure.Recordings;

public sealed class DiskPrecheckService(StorageMonitor storage)
{
    public async Task<DiskPrecheckVerdict> WeighAsync(
        OutputRoot root,
        RecordingDemand starting,
        IReadOnlyList<RecordingDemand> alreadyRunning,
        DateTime asOf,
        CancellationToken cancellationToken)
    {
        DriverCall<IReadOnlyList<StorageRootDto>> answer = await storage.ReadAsync(cancellationToken);

        return DiskPrecheck.Weigh(
            root,
            answer.TryGetValue(out IReadOnlyList<StorageRootDto>? roots) ? roots : null,
            starting,
            alreadyRunning,
            asOf);
    }
}
