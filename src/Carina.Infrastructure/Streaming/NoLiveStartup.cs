using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class NoLiveStartup : ILiveStartup
{
    public LiveStartup? Current => null;
}
