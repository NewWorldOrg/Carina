using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

internal sealed class OnTheFlyViewing : IOnTheFlyViewing
{
    private readonly ILiveTranscoder transcoder;

    private readonly Action letGo;

    private bool released;

    internal OnTheFlyViewing(
        ILiveTranscoder transcoder,
        OnTheFlyStanding standing,
        ReadOnlyMemory<byte> first,
        Action letGo)
    {
        this.transcoder = transcoder;
        this.letGo = letGo;
        Standing = standing;
        Output = new FirstBytesThenTheRest(first, transcoder.Output);
    }

    public OnTheFlyStanding Standing { get; }

    public Stream Output { get; }

    public Task<TranscoderExit> Completion => transcoder.Completion;

    public async ValueTask DisposeAsync()
    {
        if (released)
        {
            return;
        }

        released = true;

        await transcoder.DisposeAsync();

        letGo();
    }
}
