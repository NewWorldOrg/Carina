using Carina.Domain.Streaming;

namespace Carina.Api.Tests.Feature.Playback;

internal sealed class GatedViewing(GatedStream output) : IOnTheFlyViewing
{
    public OnTheFlyStanding Standing { get; } = new(
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(1),
        LiveProfile.Hd30,
        LiveEncoderChoice.Asked(LiveEncoder.Software),
        attributesWereMeasured: true,
        1,
        2);

    public Stream Output => output;

    public Task<TranscoderExit> Completion { get; } = Task.FromResult(TranscoderExit.Finished());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
