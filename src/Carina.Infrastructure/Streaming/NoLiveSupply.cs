using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class NoLiveSupply : ILiveSupply
{
    public Task<LiveSupplyStart> OpenAsync(NetworkId network, ServiceId service, CancellationToken cancellationToken)
        => Task.FromResult(LiveSupplyStart.Refused(
            LiveRefusal.DriverUnavailable,
            "nothing on this app supplies a transport stream to live viewing."));
}
