using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;

using Carina.Domain.Streaming;
using Carina.Infrastructure.Machines;

namespace Carina.Infrastructure.Streaming;

internal sealed record ProcessLaunch(Process? Started, string Note);

internal class TranscoderPipe : IDisposable
{
    private readonly AnonymousPipeServerStream pipe = new(PipeDirection.In, HandleInheritability.Inheritable);

    public Stream Pictures => pipe;

    public int Descriptor => int.Parse(pipe.GetClientHandleAsString(), CultureInfo.InvariantCulture);

    public void HandedOver() => pipe.DisposeLocalCopyOfClientHandle();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            pipe.Dispose();
        }
    }
}

internal sealed class CaptionPipe(CaptionCanvas canvas) : TranscoderPipe
{
    public CaptionCanvas Canvas { get; } = canvas;
}

internal static class TranscoderProcess
{
    internal static LiveTranscoderStart Start(
        LiveTranscodeSettings settings,
        IReadOnlyList<string> arguments,
        LiveEncoderChoice chosen,
        TimeProvider clock,
        CancellationToken cancellationToken,
        CaptionPipe? captions = null,
        TranscoderPipe? still = null)
    {
        ProcessLaunch launched = Launch(settings.Programme, arguments);

        if (launched.Started is not { } started)
        {
            captions?.Dispose();
            still?.Dispose();

            return LiveTranscoderStart.Failed(TranscoderFault.ProgrammeMissing, launched.Note);
        }

        captions?.HandedOver();
        still?.HandedOver();

        return LiveTranscoderStart.Started(
            new LiveTranscoder(started, chosen, captions, still, settings.StopGrace, clock, cancellationToken));
    }

    internal static ProcessLaunch Launch(string programme, IReadOnlyList<string> arguments)
    {
        ProgrammeStart start = AnotherProgramme.StartFed(programme, arguments);

        return start.Process is { } started
            ? new ProcessLaunch(started, string.Empty)
            : new ProcessLaunch(null, start.Complained);
    }
}
