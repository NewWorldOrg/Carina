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
            return Missing();
        }

        if (Path.GetDirectoryName(request.Destination) is { Length: > 0 } room)
        {
            Directory.CreateDirectory(room);
        }

        ThumbnailRender ran = await RunAsync(
            FfmpegInvocation.Arguments(request, settings.Width),
            keepingWhatItWrote: false,
            cancellationToken);

        if (!ran.Drew)
        {
            return ran;
        }

        return Weighed(request.Destination)
            ? ThumbnailRender.Drawn()
            : ThumbnailRender.Failed(
                ThumbnailFault.NothingWasWritten,
                "the programme reported success and left no picture behind");
    }

    public async Task<ThumbnailRender> FrameAsync(
        ThumbnailFrameRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(request.Source))
        {
            return Missing();
        }

        return await RunAsync(
            FfmpegInvocation.FrameArguments(request, settings.Width),
            keepingWhatItWrote: true,
            cancellationToken);
    }

    private static ThumbnailRender Missing()
        => ThumbnailRender.Failed(
            ThumbnailFault.SourceOutOfReach,
            "the recording is not where the ledger says it is");

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

    private async Task<ThumbnailRender> RunAsync(
        IReadOnlyList<string> arguments,
        bool keepingWhatItWrote,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(settings.Programme)
        {
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
        using var written = new MemoryStream();

        Task read = running.StandardOutput.BaseStream.CopyToAsync(written, CancellationToken.None);
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

        await read;

        if (running.ExitCode is not 0)
        {
            return ThumbnailRender.Refused(running.ExitCode, await complaint);
        }

        if (!keepingWhatItWrote)
        {
            return ThumbnailRender.Drawn();
        }

        return written.Length > 0
            ? ThumbnailRender.Drawn(written.ToArray())
            : ThumbnailRender.Failed(
                ThumbnailFault.NothingWasWritten,
                "the programme reported success and handed over no picture");
    }
}
