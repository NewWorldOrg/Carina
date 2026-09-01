using System.Diagnostics;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveTranscoder : ILiveTranscoder
{
    public const int LinesKept = 40;

    private readonly Process running;

    private readonly TimeSpan stopGrace;

    private readonly TimeProvider clock;

    private readonly CancellationTokenSource stopping;

    private readonly Queue<string> complaint = new();

    private readonly Lock saying = new();

    private bool letGo;

    internal LiveTranscoder(
        Process running,
        LiveEncoderChoice encoder,
        TimeSpan stopGrace,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        this.running = running;
        this.stopGrace = stopGrace;
        this.clock = clock;
        Encoder = encoder;
        stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        running.ErrorDataReceived += Remember;
        running.BeginErrorReadLine();

        Completion = WatchAsync();
    }

    public LiveEncoderChoice Encoder { get; }

    public Stream Input => running.StandardInput.BaseStream;

    public Stream Output => running.StandardOutput.BaseStream;

    public Task<TranscoderExit> Completion { get; }

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

    private void Remember(object sender, DataReceivedEventArgs line)
    {
        if (line.Data is null)
        {
            return;
        }

        lock (saying)
        {
            complaint.Enqueue(line.Data);

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

            return TranscoderExit.CalledOff(Complaint);
        }

        return running.ExitCode is 0 ? TranscoderExit.Finished() : TranscoderExit.Refused(running.ExitCode, Complaint);
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
