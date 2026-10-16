using System.Threading.Channels;

namespace Carina.Domain.Streaming;

public interface ILiveTranscoder : IAsyncDisposable
{
    LiveEncoderChoice Encoder { get; }

    Stream Input { get; }

    Stream Output { get; }

    ChannelReader<LiveFrame> Captions { get; }

    Task<TranscoderExit> Completion { get; }

    /// <summary>
    /// Completes when the transcoder has read enough of its input to know what it carries, and never for one that
    /// does not say so.
    /// </summary>
    Task Probed { get; }
}
