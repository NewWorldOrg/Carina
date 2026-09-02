using System.ComponentModel;
using System.Diagnostics;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

internal sealed record ProcessLaunch(Process? Started, string Note);

internal static class TranscoderProcess
{
    internal static LiveTranscoderStart Start(
        LiveTranscodeSettings settings,
        IReadOnlyList<string> arguments,
        LiveEncoderChoice chosen,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ProcessLaunch launched = Launch(settings.Programme, arguments);

        return launched.Started is { } started
            ? LiveTranscoderStart.Started(new LiveTranscoder(started, chosen, settings.StopGrace, clock, cancellationToken))
            : LiveTranscoderStart.Failed(TranscoderFault.ProgrammeMissing, launched.Note);
    }

    internal static ProcessLaunch Launch(string programme, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(programme)
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
            return new ProcessLaunch(null, $"'{programme}' could not be started on this machine: {failure.Message}");
        }

        return started is null
            ? new ProcessLaunch(null, $"'{programme}' started no process of its own.")
            : new ProcessLaunch(started, string.Empty);
    }
}
