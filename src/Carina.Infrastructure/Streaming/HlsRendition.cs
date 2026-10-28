using Carina.Domain.Auth;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// What a key holds: pictures and sound read as they come and cut into segments, for as long as a player goes on
/// asking for them.
/// </summary>
internal abstract class HlsRendition : IHlsRendition
{
    private readonly TimeProvider clock;

    private readonly Action<HlsRendition> lapsed;

    private readonly CancellationTokenSource stopping = new();

    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly ITimer expiry;

    private DateTimeOffset askedAt;

    private bool closed;

    private bool broke;

    protected HlsRendition(
        HlsHolding holding,
        LiveProfile profile,
        HlsSegmenter segmenter,
        HlsSettings settings,
        TimeProvider clock,
        Action<HlsRendition> lapsed)
    {
        Digest = holding.Digest;
        Watcher = holding.Watcher;
        Profile = profile;
        Segmenter = segmenter;
        Settings = settings;
        this.clock = clock;
        this.lapsed = lapsed;
        askedAt = clock.GetUtcNow();
        expiry = clock.CreateTimer(_ => Ring(), null, settings.Idle, Timeout.InfiniteTimeSpan);
    }

    public LiveProfile Profile { get; }

    public HlsWindow Window
    {
        get
        {
            lock (Gate)
            {
                return Segmenter.Window;
            }
        }
    }

    public ReadOnlyMemory<byte>? Initialisation
    {
        get
        {
            lock (Gate)
            {
                return Segmenter.Initialisation;
            }
        }
    }

    public bool Broke
    {
        get
        {
            lock (Gate)
            {
                return broke;
            }
        }
    }

    internal string Digest { get; }

    internal Subject Watcher { get; }

    internal DateTimeOffset AskedAt
    {
        get
        {
            lock (Gate)
            {
                return askedAt;
            }
        }
    }

    /// <summary>
    /// Ends once what the key holds has been let go of.
    /// </summary>
    internal Task Life { get; private set; } = Task.CompletedTask;

    protected Lock Gate { get; } = new();

    protected HlsSegmenter Segmenter { get; }

    protected HlsSettings Settings { get; }

    protected CancellationToken Stopping => stopping.Token;

    public virtual ReadOnlyMemory<byte>? Segment(long number)
    {
        lock (Gate)
        {
            return Segmenter.Segment(number);
        }
    }

    /// <remarks>
    /// Holds its own deadline, disposed of with the wait.
    /// </remarks>
    public async Task<bool> ReadyAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = new(Settings.LongestWait, clock);
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

        lock (Gate)
        {
            return Segmenter.HoldsASegment;
        }
    }

    internal void Start() => Life = ReadAsync();

    internal bool HasLapsed(DateTimeOffset now) => now - AskedAt >= Settings.Idle;

    internal void Asked()
    {
        lock (Gate)
        {
            askedAt = clock.GetUtcNow();
        }
    }

    /// <summary>
    /// Stops reading, which lets go of what the key holds.
    /// </summary>
    internal void Close()
    {
        lock (Gate)
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

    /// <summary>
    /// Reads what the key holds until it ends or the key is let go of, and lets go of it then.
    /// </summary>
    protected abstract Task ReadAsync();

    protected void SayTheEndCame() => ready.TrySetResult();

    protected void SayItBroke()
    {
        lock (Gate)
        {
            broke = true;
        }
    }

    protected void SayWhetherASegmentIsCut()
    {
        bool cut;

        lock (Gate)
        {
            cut = Segmenter.HoldsASegment;
        }

        if (cut)
        {
            ready.TrySetResult();
        }
    }

    private void Ring()
    {
        TimeSpan quiet = clock.GetUtcNow() - AskedAt;

        if (quiet >= Settings.Idle)
        {
            lapsed(this);

            return;
        }

        lock (Gate)
        {
            if (!closed)
            {
                expiry.Change(Settings.Idle - quiet, Timeout.InfiniteTimeSpan);
            }
        }
    }
}

/// <summary>
/// The digest a key is held by and the watcher it was issued to.
/// </summary>
internal readonly record struct HlsHolding(string Digest, Subject Watcher);
