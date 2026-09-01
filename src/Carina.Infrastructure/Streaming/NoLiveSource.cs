using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class NoLiveSource : ILiveWireSource
{
    public ValueTask<ILiveViewing?> JoinAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult<ILiveViewing?>(null);
}
