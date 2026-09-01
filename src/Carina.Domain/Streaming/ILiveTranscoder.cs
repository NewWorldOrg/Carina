namespace Carina.Domain.Streaming;

public interface ILiveTranscoder : IAsyncDisposable
{
    LiveEncoderChoice Encoder { get; }

    Stream Input { get; }

    Stream Output { get; }

    Task<TranscoderExit> Completion { get; }
}
