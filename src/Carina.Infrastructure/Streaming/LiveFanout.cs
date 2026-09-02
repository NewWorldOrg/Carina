using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveFanout(LiveFanoutSettings settings, ILiveStartup? startup = null) : ILiveWireSource
{
    private readonly Lock gate = new();

    private readonly ILiveStartup? startup = startup;

    private readonly List<Viewing> viewers = [];

    private readonly SortedDictionary<LiveChannel, LiveFrame> headers = [];

    private bool ended;

    private LiveFragmentFault? fault;

    public int Viewers
    {
        get
        {
            lock (gate)
            {
                return viewers.Count;
            }
        }
    }

    public IReadOnlyList<LiveFrame> Headers
    {
        get
        {
            lock (gate)
            {
                return [.. headers.Values];
            }
        }
    }

    public bool Ended
    {
        get
        {
            lock (gate)
            {
                return ended;
            }
        }
    }

    public LiveFragmentFault? Fault
    {
        get
        {
            lock (gate)
            {
                return fault;
            }
        }
    }

    public ValueTask<ILiveViewing?> JoinAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            if (ended)
            {
                return ValueTask.FromResult<ILiveViewing?>(null);
            }

            Viewing viewing = new(this, settings.LongestBacklog);

            foreach (LiveFrame header in headers.Values)
            {
                viewing.Offer(header);
            }

            viewers.Add(viewing);

            return ValueTask.FromResult<ILiveViewing?>(viewing);
        }
    }

    public void Publish(LiveFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (gate)
        {
            if (ended)
            {
                return;
            }

            if (LiveChannels.Headers.Contains(frame.Channel))
            {
                headers[frame.Channel] = frame;
            }

            foreach (Viewing viewing in viewers)
            {
                viewing.Offer(frame);
            }
        }
    }

    public void End() => Close(null);

    public void Break(LiveFragmentFault why)
    {
        if (!Enum.IsDefined(why))
        {
            throw new ArgumentOutOfRangeException(
                nameof(why),
                why,
                "What is being sent live breaks in one of the ways the fragmenter names.");
        }

        Close(why);
    }

    private static bool Expendable(LiveFrame frame) => LiveChannels.Expendable.Contains(frame.Channel);

    private void Close(LiveFragmentFault? why)
    {
        lock (gate)
        {
            if (ended)
            {
                return;
            }

            ended = true;
            fault = why;
            headers.Clear();

            foreach (Viewing viewing in viewers)
            {
                viewing.Close(why);
            }
        }
    }

    private void Leave(Viewing viewing)
    {
        lock (gate)
        {
            viewers.Remove(viewing);
        }
    }

    private sealed class Viewing : ILiveViewing
    {
        private readonly LiveFanout fanout;

        private readonly int longestBacklog;

        private readonly Channel<LiveFrame> frames = Channel.CreateUnbounded<LiveFrame>();

        private readonly Lock counting = new();

        private int queued;

        private long dropped;

        private bool left;

        internal Viewing(LiveFanout fanout, int longestBacklog)
        {
            this.fanout = fanout;
            this.longestBacklog = longestBacklog;
            Frames = new CountedReader(frames.Reader, Took);
        }

        public ChannelReader<LiveFrame> Frames { get; }

        public LiveBacklog Backlog
        {
            get
            {
                lock (counting)
                {
                    return new LiveBacklog(queued, dropped);
                }
            }
        }

        public ILiveStartup? Startup => fanout.startup;

        public ValueTask DisposeAsync()
        {
            if (left)
            {
                return ValueTask.CompletedTask;
            }

            left = true;
            fanout.Leave(this);
            frames.Writer.TryComplete();

            lock (counting)
            {
                while (frames.Reader.TryRead(out _))
                {
                }

                queued = 0;
            }

            return ValueTask.CompletedTask;
        }

        internal void Offer(LiveFrame frame)
        {
            bool expendable = Expendable(frame);

            lock (counting)
            {
                if (expendable && queued >= longestBacklog)
                {
                    dropped++;

                    return;
                }

                if (frames.Writer.TryWrite(frame) && expendable)
                {
                    queued++;
                }
            }
        }

        internal void Close(LiveFragmentFault? why)
            => frames.Writer.TryComplete(
                why is { } broke
                    ? new InvalidOperationException($"What was being sent live broke: {broke}.")
                    : null);

        private void Took(LiveFrame frame)
        {
            if (!Expendable(frame))
            {
                return;
            }

            lock (counting)
            {
                queued--;
            }
        }
    }

    private sealed class CountedReader(ChannelReader<LiveFrame> inner, Action<LiveFrame> took) : ChannelReader<LiveFrame>
    {
        public override Task Completion => inner.Completion;

        public override bool CanCount => inner.CanCount;

        public override int Count => inner.Count;

        public override bool CanPeek => inner.CanPeek;

        public override bool TryPeek([MaybeNullWhen(false)] out LiveFrame item) => inner.TryPeek(out item);

        public override bool TryRead([MaybeNullWhen(false)] out LiveFrame item)
        {
            if (!inner.TryRead(out item))
            {
                return false;
            }

            took(item);

            return true;
        }

        public override ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken = default)
            => inner.WaitToReadAsync(cancellationToken);
    }
}
