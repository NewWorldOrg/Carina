using System.Threading.Channels;

namespace Carina.Domain.Streaming;

public interface ILiveCaptioner : IAsyncDisposable
{
    Stream Input { get; }

    ChannelReader<LiveFrame> Frames { get; }

    Task<TranscoderExit> Completion { get; }
}
