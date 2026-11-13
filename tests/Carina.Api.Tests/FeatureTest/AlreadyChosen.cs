using Carina.Domain.Streaming;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class AlreadyChosen(LiveEncoder encoder, IReadOnlyList<VideoCodec>? formats = null) : ILiveEncoderSelector
{
    public Task<LiveEncoderChoice> ChooseAsync(CancellationToken cancellationToken)
        => Task.FromResult(LiveEncoderChoice.Asked(encoder));

    public Task<IReadOnlyList<VideoCodec>> FormatsAsync(CancellationToken cancellationToken)
        => Task.FromResult(formats ?? [VideoCodec.H264]);
}
