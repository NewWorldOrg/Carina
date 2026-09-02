using System.Diagnostics;
using System.Threading.Channels;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveCaptioner : ILiveCaptioner
{
    public const int LinesKept = 40;

    private readonly Process running;

    private readonly TimeSpan stopGrace;

    private readonly TimeProvider clock;

    private readonly CancellationTokenSource stopping;

    private readonly Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

    private readonly Queue<string> complaint = new();

    private readonly Lock saying = new();

    private readonly Task<CaptionFlowFault?> drawing;

    private bool letGo;

    internal LiveCaptioner(
        Process running,
        CaptionCanvas canvas,
        LiveCaptionSettings settings,
        TimeSpan stopGrace,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        this.running = running;
        this.stopGrace = stopGrace;
        this.clock = clock;
        Canvas = canvas;
        stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        drawing = CaptionFrames.CarryAsync(
            running.StandardOutput.BaseStream,
            running.StandardError,
            canvas,
            settings,
            frames.Writer,
            stopping.Token,
            Remember);

        Completion = WatchAsync();
    }

    public CaptionCanvas Canvas { get; }

    public Stream Input => running.StandardInput.BaseStream;

    public ChannelReader<LiveFrame> Frames => frames.Reader;

    public Task<TranscoderExit> Completion { get; }

    public Task<CaptionFlowFault?> Drawing => drawing;

    public string Complaint
    {
        get
        {
            lock (saying)
            {
                return TranscoderNote.Of(string.Join('\n', complaint));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (letGo)
        {
            return;
        }

        letGo = true;

        await stopping.CancelAsync();
        await Completion;

        stopping.Dispose();
        running.Dispose();
    }

    private void Remember(string line)
    {
        lock (saying)
        {
            complaint.Enqueue(line);

            while (complaint.Count > LinesKept)
            {
                complaint.Dequeue();
            }
        }
    }

    private async Task<TranscoderExit> WatchAsync()
    {
        try
        {
            await running.WaitForExitAsync(stopping.Token);
        }
        catch (OperationCanceledException)
        {
            await GiveUpAsync();
            await Quietly(drawing);

            return TranscoderExit.CalledOff(Complaint);
        }

        await Quietly(drawing);

        return running.ExitCode is 0 ? TranscoderExit.Finished() : TranscoderExit.Refused(running.ExitCode, Complaint);
    }

    private static async Task Quietly(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private async Task GiveUpAsync()
    {
        Hush();

        if (await WaitedOut(stopGrace))
        {
            return;
        }

        Kill();

        await WaitedOut(stopGrace);
    }

    private async Task<bool> WaitedOut(TimeSpan grace)
    {
        using var patience = new CancellationTokenSource(grace, clock);

        try
        {
            await running.WaitForExitAsync(patience.Token);

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void Hush()
    {
        try
        {
            running.StandardInput.Close();
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException or InvalidOperationException)
        {
            return;
        }
    }

    private void Kill()
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
