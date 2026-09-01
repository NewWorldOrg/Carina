using System.ComponentModel;
using System.Diagnostics;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

internal static class TranscoderProcess
{
    internal static LiveTranscoderStart Start(
        LiveTranscodeSettings settings,
        IReadOnlyList<string> arguments,
        LiveEncoderChoice chosen,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(settings.Programme)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
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
