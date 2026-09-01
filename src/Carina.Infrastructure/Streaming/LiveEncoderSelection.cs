using System.ComponentModel;
using System.Diagnostics;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveEncoderSelection(
    LiveTranscodeSettings settings,
    TimeProvider clock,
    string renderNode = FfmpegLiveInvocation.RenderNode) : ILiveEncoderSelector
{
    private readonly Lock asking = new();

    private Task<LiveEncoderChoice>? asked;

    public Task<LiveEncoderChoice> ChooseAsync(CancellationToken cancellationToken)
    {
        Task<LiveEncoderChoice> answering;

        lock (asking)
        {
            answering = asked ??= DecideAsync();
        }

        return answering.WaitAsync(cancellationToken);
    }

    private async Task<LiveEncoderChoice> DecideAsync()
    {
        if (settings.Prefer is not LiveEncoder.Vaapi)
        {
            return LiveEncoderChoice.Asked(settings.Prefer);
        }

        return OutOfReach() ?? await WhatTheDriverSaysAsync();
    }

    private LiveEncoderChoice? OutOfReach()
    {
        try
        {
            using FileStream node = File.Open(renderNode, FileMode.Open, FileAccess.ReadWrite);

            return null;
        }
        catch (Exception absent) when (absent is FileNotFoundException or DirectoryNotFoundException)
        {
            return LiveEncoderChoice.FellBackToSoftware(
                EncoderRefusal.NodeMissing,
                "no render node was handed to this container");
        }
        catch (UnauthorizedAccessException)
        {
            return LiveEncoderChoice.FellBackToSoftware(
                EncoderRefusal.NodeUnreadable,
                "the render node is there and this process is not in the group that may open it");
        }
        catch (IOException failure)
        {
            return LiveEncoderChoice.FellBackToSoftware(EncoderRefusal.NodeUnreadable, failure.Message);
        }
    }

    private async Task<LiveEncoderChoice> WhatTheDriverSaysAsync()
    {
        var start = new ProcessStartInfo(settings.Programme)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in VaapiProbeInvocation.Arguments())
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
            return LiveEncoderChoice.FellBackToSoftware(
                EncoderRefusal.ProbeProgrammeMissing,
                $"'{settings.Programme}' could not be started on this machine: {failure.Message}");
        }

        if (started is null)
        {
            return LiveEncoderChoice.FellBackToSoftware(
                EncoderRefusal.ProbeProgrammeMissing,
                $"'{settings.Programme}' started no process of its own.");
        }

        using Process running = started;

        _ = running.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> complaint = running.StandardError.ReadToEndAsync(CancellationToken.None);

        using var deadline = new CancellationTokenSource(settings.LongestProbe, clock);

        try
        {
            await running.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            GiveUpOn(running);

            return LiveEncoderChoice.FellBackToSoftware(
                EncoderRefusal.ProbeTimedOut,
                $"the card was still being asked about after {settings.LongestProbe}");
        }

        return running.ExitCode is 0
            ? LiveEncoderChoice.Asked(LiveEncoder.Vaapi)
            : LiveEncoderChoice.FellBackToSoftware(EncoderRefusal.DriverUnusable, await complaint);
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
