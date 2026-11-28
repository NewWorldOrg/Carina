using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveTranscoder : ILiveTranscoder
{
    public const int LinesKept = 40;

    private readonly Process running;

    private readonly CaptionPipe? captions;

    private readonly TranscoderPipe? still;

    private readonly TimeSpan stopGrace;

    private readonly TimeProvider clock;

    private readonly CancellationTokenSource stopping;

    private readonly CancellationTokenSource leavingThePipe = new();

    private readonly Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

    private readonly Queue<string> complaint = new();

    private readonly TranscoderTalk talk = new();

    private readonly TaskCompletionSource probed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource<LiveStill> made = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Lock saying = new();

    private readonly Task<CaptionFlowFault?> drawing;

    private readonly Task stilling;

    private bool letGo;

    internal LiveTranscoder(
        Process running,
        LiveEncoderChoice encoder,
        CaptionPipe? captions,
        TranscoderPipe? still,
        TimeSpan stopGrace,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        this.running = running;
        this.captions = captions;
        this.still = still;
        this.stopGrace = stopGrace;
        this.clock = clock;
        Encoder = encoder;
        stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        running.ErrorDataReceived += Remember;
        running.BeginErrorReadLine();

        drawing = captions is null
            ? NothingDrawn()
            : DrawnAsync(captions);

        stilling = still is null
            ? NoStillAsked()
            : StillAsync(still);

        Completion = WatchAsync();
    }

    public LiveEncoderChoice Encoder { get; }

    public Stream Input => running.StandardInput.BaseStream;

    public Stream Output => running.StandardOutput.BaseStream;

    public ChannelReader<LiveFrame> Captions => frames.Reader;

    public Task<TranscoderExit> Completion { get; }

    public Task Probed => probed.Task;

    public Task<CaptionFlowFault?> Drawing => drawing;

    public Task<LiveStill> Still => made.Task;

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
        leavingThePipe.Dispose();
        captions?.Dispose();
        still?.Dispose();
        running.Dispose();
    }

    private async Task<CaptionFlowFault?> DrawnAsync(CaptionPipe pipe)
    {
        try
        {
            return await CaptionFrames.CarryAsync(pipe.Pictures, pipe.Canvas, frames.Writer, stopping.Token);
        }
        catch (OperationCanceledException) when (!leavingThePipe.IsCancellationRequested)
        {
            await DroppedToTheEndAsync(pipe.Pictures, leavingThePipe.Token);

            return null;
        }
    }

    private static async Task DroppedToTheEndAsync(Stream pictures, CancellationToken cancellationToken)
    {
        byte[] dropped = ArrayPool<byte>.Shared.Rent(CaptionFrames.Mouthful);

        try
        {
            int read;

            do
            {
                read = await pictures.ReadAsync(dropped, cancellationToken);
            }
            while (read > 0);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(dropped);
        }
    }

    private async Task StillAsync(TranscoderPipe pipe)
    {
        try
        {
            await StillFrames.CarryAsync(pipe.Pictures, Made, stopping.Token);
        }
        catch (OperationCanceledException) when (!leavingThePipe.IsCancellationRequested)
        {
            Made(LiveStill.Missing("the transcoder was let go before it wrote a still."));
            await DroppedToTheEndAsync(pipe.Pictures, leavingThePipe.Token);
        }
        finally
        {
            Made(LiveStill.Missing("the transcoder was let go before it wrote a still."));
        }
    }

    private void Made(LiveStill taken) => made.TrySetResult(taken);

    private Task NoStillAsked()
    {
        Made(LiveStill.Missing("no still was asked of this transcoder."));

        return Task.CompletedTask;
    }

    private Task<CaptionFlowFault?> NothingDrawn()
    {
        frames.Writer.TryComplete();

        return Task.FromResult<CaptionFlowFault?>(null);
    }

    private void Remember(object sender, DataReceivedEventArgs line)
    {
        if (line.Data is null)
        {
            return;
        }

        TranscoderLine heard;

        lock (saying)
        {
            heard = talk.Hear(line.Data);

            if (heard.Kept is { } kept)
            {
                complaint.Enqueue(kept);
            }

            while (complaint.Count > LinesKept)
            {
                complaint.Dequeue();
            }
        }

        if (heard.InputRead)
        {
            probed.TrySetResult();
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
            bool killed = await GiveUpAsync();
            await DrawnToTheEndAsync();

            return killed || running.ExitCode is 0 ? TranscoderExit.CalledOff(Complaint) : Exited();
        }

        await DrawnToTheEndAsync();

        return Exited();
    }

    private TranscoderExit Exited()
        => running.ExitCode is 0 ? TranscoderExit.Finished() : TranscoderExit.Refused(running.ExitCode, Complaint);

    private async Task DrawnToTheEndAsync()
    {
        using CancellationTokenSource deadline = new(stopGrace, clock);
        Task pipes = Task.WhenAll(drawing, stilling);

        try
        {
            await pipes.WaitAsync(deadline.Token);
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException or OperationCanceledException)
        {
            await leavingThePipe.CancelAsync();
            await stopping.CancelAsync();
            await QuietlyAsync(pipes);
        }
    }

    private static async Task QuietlyAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
    }

    private async Task<bool> GiveUpAsync()
    {
        Hush();

        if (await WaitedOutAsync(stopGrace))
        {
            return false;
        }

        Kill();

        await WaitedOutAsync(stopGrace);

        return true;
    }

    private async Task<bool> WaitedOutAsync(TimeSpan grace)
    {
        using CancellationTokenSource patience = new(grace, clock);

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
