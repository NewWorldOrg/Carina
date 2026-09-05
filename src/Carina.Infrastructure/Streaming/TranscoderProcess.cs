using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

internal sealed record ProcessLaunch(Process? Started, string Note);

internal sealed class CaptionPipe : IDisposable
{
    private readonly AnonymousPipeServerStream pipe = new(PipeDirection.In, HandleInheritability.Inheritable);

    public CaptionPipe(CaptionCanvas canvas)
    {
        Canvas = canvas;
    }

    public CaptionCanvas Canvas { get; }

    public Stream Pictures => pipe;

    public int Descriptor => int.Parse(pipe.GetClientHandleAsString(), CultureInfo.InvariantCulture);

    public void HandedOver() => pipe.DisposeLocalCopyOfClientHandle();

    public void Dispose() => pipe.Dispose();
}

internal static class TranscoderProcess
{
    internal static LiveTranscoderStart Start(
        LiveTranscodeSettings settings,
        IReadOnlyList<string> arguments,
        LiveEncoderChoice chosen,
        TimeProvider clock,
        CancellationToken cancellationToken,
        CaptionPipe? captions = null)
    {
        ProcessLaunch launched = Launch(settings.Programme, arguments);

        if (launched.Started is not { } started)
        {
            captions?.Dispose();

            return LiveTranscoderStart.Failed(TranscoderFault.ProgrammeMissing, launched.Note);
        }

        captions?.HandedOver();

        return LiveTranscoderStart.Started(new LiveTranscoder(started, chosen, captions, settings.StopGrace, clock, cancellationToken));
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
