using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

internal sealed class SeatedTranscoder(ILiveTranscoder transcoder, ITranscodeSeat seat) : ILiveTranscoder
{
    public LiveEncoderChoice Encoder => transcoder.Encoder;

    public Stream Input => transcoder.Input;

    public Stream Output => transcoder.Output;

    public Task<TranscoderExit> Completion => transcoder.Completion;

    public async ValueTask DisposeAsync()
    {
        await transcoder.DisposeAsync();

        seat.Dispose();
    }
}
