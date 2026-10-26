using System.Threading.Channels;

using Carina.Domain.Auth;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// One viewer's seat on a live session, read as it comes and cut into segments, held for as long as a player goes on
/// asking for it.
/// </summary>
internal sealed class LiveHlsRendition : ILiveHlsRendition
{
    private readonly Lock gate = new();

    private readonly ILiveViewing seat;

    private readonly HlsSegmenter segmenter;

    private readonly LiveHlsSettings settings;

    private readonly TimeProvider clock;

    private readonly Action<LiveHlsRendition> lapsed;

    private readonly CancellationTokenSource stopping = new();

    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly ITimer expiry;

    private DateTimeOffset askedAt;

    private bool closed;

    internal LiveHlsRendition(
        string digest,
        ILiveViewing seat,
        Subject watcher,
        LiveProfile profile,
        LiveHlsSettings settings,
        TimeProvider clock,
        Action<LiveHlsRendition> lapsed)
    {
        Digest = digest;
        Watcher = watcher;
        Profile = profile;
        this.seat = seat;
        this.settings = settings;
        this.clock = clock;
        this.lapsed = lapsed;
        segmenter = new HlsSegmenter(settings);
        askedAt = clock.GetUtcNow();
        expiry = clock.CreateTimer(_ => Ring(), null, settings.Idle, Timeout.InfiniteTimeSpan);
    }

    public LiveProfile Profile { get; }

    public LiveHlsWindow Window
    {
        get
        {
            lock (gate)
            {
                return segmenter.Window;
            }
        }
    }

    public ReadOnlyMemory<byte>? Initialisation
    {
        get
        {
            lock (gate)
            {
                return segmenter.Initialisation;
            }
        }
    }

    internal string Digest { get; }

    internal Subject Watcher { get; }

    internal DateTimeOffset AskedAt
    {
        get
        {
            lock (gate)
            {
                return askedAt;
            }
        }
    }

    /// <summary>
    /// Ends once the seat has been let go of.
    /// </summary>
    internal Task Life { get; private set; } = Task.CompletedTask;

    public ReadOnlyMemory<byte>? Segment(long number)
    {
        lock (gate)
        {
            return segmenter.Segment(number);
        }
    }

    /// <remarks>
    /// Holds its own deadline, disposed of with the wait.
    /// </remarks>
    public async Task<bool> ReadyAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = new(settings.LongestWait, clock);
        using CancellationTokenSource leash =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            await ready.Task.WaitAsync(leash.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        return Window.Segments.Count > 0;
    }

    internal void Start() => Life = ReadAsync();

    internal bool HasLapsed(DateTimeOffset now) => now - AskedAt >= settings.Idle;

    internal void Asked()
    {
        lock (gate)
        {
            askedAt = clock.GetUtcNow();
        }
    }

    /// <summary>
    /// Stops reading the seat, which lets go of it.
    /// </summary>
    internal void Close()
    {
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            closed = true;
        }

        expiry.Dispose();
        stopping.Cancel();
    }

    private void Ring()
    {
        TimeSpan quiet = clock.GetUtcNow() - AskedAt;

        if (quiet >= settings.Idle)
        {
            lapsed(this);

            return;
        }

        lock (gate)
        {
            if (!closed)
            {
                expiry.Change(settings.Idle - quiet, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private async Task ReadAsync()
    {
        ChannelReader<LiveFrame> frames = seat.Frames;

        try
        {
            while (await frames.WaitToReadAsync(stopping.Token))
            {
                Cut(frames);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            lock (gate)
            {
                segmenter.End();
            }

            ready.TrySetResult();
            await seat.DisposeAsync();
        }
    }

    private void Cut(ChannelReader<LiveFrame> frames)
    {
        bool cut;

        lock (gate)
        {
            while (frames.TryRead(out LiveFrame? frame))
            {
                segmenter.Take(frame);
            }

            cut = segmenter.Window.Segments.Count > 0;
        }

        if (cut)
        {
            ready.TrySetResult();
        }
    }
}
