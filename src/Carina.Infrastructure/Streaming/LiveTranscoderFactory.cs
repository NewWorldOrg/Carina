using System.ComponentModel;
using System.Diagnostics;

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

        var start = new ProcessStartInfo(settings.Programme)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in FfmpegLiveInvocation.Arguments(profile, attributes, chosen.Encoder))
        {
            start.ArgumentList.Add(argument);
        }

        foreach (string argument in FfmpegLiveInvocation.Delivery())
        {
            start.ArgumentList.Add(argument);
        }

        Process? started;

        try
        {
            started = Process.Start(start);
        }
        catch (Win32Exception failure)
        {
            return LiveTranscoderStart.Failed(
                TranscoderFault.ProgrammeMissing,
                $"'{settings.Programme}' could not be started on this machine: {failure.Message}");
        }

        if (started is null)
        {
            return LiveTranscoderStart.Failed(
                TranscoderFault.ProgrammeMissing,
                $"'{settings.Programme}' started no process of its own.");
        }

        return LiveTranscoderStart.Started(
            new LiveTranscoder(started, chosen, settings.StopGrace, clock, cancellationToken));
    }
}
