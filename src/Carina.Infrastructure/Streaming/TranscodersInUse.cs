using Carina.Domain.Captions;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class TranscodersInUse(ITranscodeBudget transcoders) : IWatching
{
    public bool Anyone => transcoders.Running > 0;
}
