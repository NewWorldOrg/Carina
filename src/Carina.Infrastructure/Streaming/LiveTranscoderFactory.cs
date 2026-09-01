using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveTranscoderFactory(
    LiveTranscodeSettings settings,
    ILiveEncoderSelector selector,
    TimeProvider clock) : ILiveTranscoderFactory
{
    public async Task<LiveTranscoderStart> StartAsync(
        LiveProfile profile,
        StreamAttributes attributes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(attributes);

        LiveEncoderChoice chosen = await selector.ChooseAsync(cancellationToken);

        return TranscoderProcess.Start(
            settings,
            [
                .. FfmpegLiveInvocation.Arguments(profile, attributes, chosen.Encoder),
                .. FfmpegLiveInvocation.Delivery(),
            ],
            chosen,
            clock,
            cancellationToken);
    }
}
