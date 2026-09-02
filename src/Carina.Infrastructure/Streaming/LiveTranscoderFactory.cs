using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveTranscoderFactory(
    LiveTranscodeSettings settings,
    ITranscodeBudget budget,
    ILiveEncoderSelector selector,
    TimeProvider clock) : ILiveTranscoderFactory
{
    public async Task<LiveTranscoderStart> StartAsync(
        ServiceId service,
        LiveProfile profile,
        StreamAttributes attributes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(attributes);

        TranscodeClaim claim = budget.Claim(TranscodePurpose.Live);

        if (claim.Seat is not { } seat)
        {
            return LiveTranscoderStart.Refused(claim.Refusal!);
        }

        bool handedOver = false;

        try
        {
            LiveTranscoderStart started = await StartedAsync(service, profile, attributes, seat, cancellationToken);

            handedOver = started.Running;

            return started;
        }
        finally
        {
            if (!handedOver)
            {
                seat.Dispose();
            }
        }
    }

    private async Task<LiveTranscoderStart> StartedAsync(
        ServiceId service,
        LiveProfile profile,
        StreamAttributes attributes,
        ITranscodeSeat seat,
        CancellationToken cancellationToken)
    {
        LiveEncoderChoice chosen = await selector.ChooseAsync(cancellationToken);

        LiveTranscoderStart started = TranscoderProcess.Start(
            settings,
            [
                .. FfmpegLiveInvocation.Arguments(service, profile, attributes, chosen.Encoder),
                .. FfmpegLiveInvocation.Delivery(),
            ],
            chosen,
            clock,
            cancellationToken);

        return started.Transcoder is { } transcoder
            ? LiveTranscoderStart.Started(new SeatedTranscoder(transcoder, seat))
            : started;
    }
}
