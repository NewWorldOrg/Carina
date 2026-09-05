using System.Threading.Channels;

namespace Carina.Domain.Streaming;

public interface ILiveTranscoder : IAsyncDisposable
{
    LiveEncoderChoice Encoder { get; }

    Stream Input { get; }

    Stream Output { get; }

    ChannelReader<LiveFrame> Captions { get; }

    Task<TranscoderExit> Completion { get; }
}
