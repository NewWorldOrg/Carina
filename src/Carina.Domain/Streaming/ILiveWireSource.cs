using System.Threading.Channels;

namespace Carina.Domain.Streaming;

public interface ILiveWireSource
{
    ValueTask<ILiveViewing?> JoinAsync(CancellationToken cancellationToken);
}

public interface ILiveViewing : IAsyncDisposable
{
    ChannelReader<LiveFrame> Frames { get; }

    LiveBacklog Backlog { get; }

    ILiveStartup? Startup { get; }

    ILiveEnding? Ending { get; }
}
