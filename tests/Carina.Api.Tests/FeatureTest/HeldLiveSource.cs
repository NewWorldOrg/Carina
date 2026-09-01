using System.Threading.Channels;

using Carina.Domain.Streaming;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class HeldLiveSource : ILiveWireSource, ILiveViewing
{
    private readonly Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

    public ChannelReader<LiveFrame> Frames => frames.Reader;

    public bool LetGo { get; private set; }

    public void Send(LiveFrame frame) => frames.Writer.TryWrite(frame);

    public void NoMore() => frames.Writer.TryComplete();

    public ValueTask<ILiveViewing?> JoinAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult<ILiveViewing?>(this);

    public ValueTask DisposeAsync()
    {
        LetGo = true;

        return ValueTask.CompletedTask;
    }
}
