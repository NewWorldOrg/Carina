using System.ComponentModel;
using System.Diagnostics;

using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;

namespace Carina.Infrastructure.Thumbnails;

public sealed class FfmpegThumbnailRenderer(ThumbnailSettings settings, TimeProvider clock) : IThumbnailRenderer
{
    public async Task<ThumbnailRender> RenderAsync(ThumbnailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.Source))
        {
            return ThumbnailRender.Failed(
                ThumbnailFault.SourceOutOfReach,
                "the recording is not where the ledger says it is");
        }

        if (Path.GetDirectoryName(request.Destination) is { Length: > 0 } room)
        {
            Directory.CreateDirectory(room);
        }

        var start = new ProcessStartInfo(settings.Programme)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in FfmpegInvocation.Arguments(request, settings.Width))
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
            return ThumbnailRender.Failed(
                ThumbnailFault.ProgrammeMissing,
                $"'{settings.Programme}' could not be started on this machine: {failure.Message}");
        }

        if (started is null)
        {
            return ThumbnailRender.Failed(
                ThumbnailFault.ProgrammeMissing,
                $"'{settings.Programme}' started no process of its own.");
        }

        using Process running = started;

        _ = running.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> complaint = running.StandardError.ReadToEndAsync(CancellationToken.None);

        using var deadline = new CancellationTokenSource(settings.LongestRender, clock);
        using CancellationTokenSource waiting =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            await running.WaitForExitAsync(waiting.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            GiveUpOn(running);

            return ThumbnailRender.Failed(
                ThumbnailFault.TimedOut,
                $"the programme was still running after {settings.LongestRender}");
        }
        catch (OperationCanceledException)
        {
            GiveUpOn(running);

            throw;
        }

        if (running.ExitCode is not 0)
        {
            return ThumbnailRender.Refused(running.ExitCode, await complaint);
        }

        return Weighed(request.Destination)
            ? ThumbnailRender.Drawn()
            : ThumbnailRender.Failed(
                ThumbnailFault.NothingWasWritten,
                "the programme reported success and left no picture behind");
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

    private static bool Weighed(string destination)
    {
        var drawn = new FileInfo(destination);

        return drawn.Exists && drawn.Length > 0;
    }
}
