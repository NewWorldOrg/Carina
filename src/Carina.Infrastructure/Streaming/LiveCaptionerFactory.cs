using System.Diagnostics;

using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveCaptionerFactory(
    LiveTranscodeSettings settings,
    LiveCaptionSettings captions,
    TimeProvider clock) : ILiveCaptionerFactory
{
    public Task<LiveCaptionerStart> StartAsync(
        ServiceId service,
        StreamAttributes attributes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(attributes);

        CaptionCanvas canvas = new(attributes.Size);

        ProcessLaunch launched = TranscoderProcess.Launch(
            settings.Programme,
            [
                .. FfmpegCaptionInvocation.Arguments(service, attributes.Size),
                .. FfmpegCaptionInvocation.Delivery(),
            ]);

        if (launched.Started is not { } started)
        {
            return Task.FromResult(LiveCaptionerStart.Failed(TranscoderFault.ProgrammeMissing, launched.Note));
        }

        return Task.FromResult(LiveCaptionerStart.Started(
            new LiveCaptioner(started, canvas, captions, settings.StopGrace, clock, cancellationToken)));
    }
}
