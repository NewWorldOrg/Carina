using System.ComponentModel;
using System.Diagnostics;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class FfprobeStreamAttributeReader(StreamAttributeSettings settings, TimeProvider clock)
    : IStreamAttributeReader
{
    public async Task<StreamAttributeReading> ReadAsync(StreamSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var start = new ProcessStartInfo(settings.Programme)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in FfprobeInvocation.Arguments(source))
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
            return StreamAttributeReading.Unanswered(
                StreamProbeFault.ProgrammeMissing,
                $"'{settings.Programme}' could not be started on this machine: {failure.Message}");
        }

        if (started is null)
        {
            return StreamAttributeReading.Unanswered(
                StreamProbeFault.ProgrammeMissing,
                $"'{settings.Programme}' started no process of its own.");
        }

        using Process running = started;

        Task<string> answer = running.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> complaint = running.StandardError.ReadToEndAsync(CancellationToken.None);

        using var deadline = new CancellationTokenSource(settings.LongestRead, clock);
        using CancellationTokenSource waiting =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            await running.WaitForExitAsync(waiting.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            GiveUpOn(running);

            return StreamAttributeReading.Unanswered(
                StreamProbeFault.TimedOut,
                $"the programme was still reading the stream after {settings.LongestRead}");
        }
        catch (OperationCanceledException)
        {
            GiveUpOn(running);

            throw;
        }

        if (running.ExitCode is not 0)
        {
            return StreamAttributeReading.Refused(running.ExitCode, await complaint);
        }

        return FfprobeAttributes.Read(await answer);
    }

    private static void GiveUpOn(Process running)
    {
        try
        {
            running.Kill(entireProcessTree: true);
        }
        catch (Exception gone) when (gone is InvalidOperationException or NotSupportedException)
        {
            return;
        }
    }
}
