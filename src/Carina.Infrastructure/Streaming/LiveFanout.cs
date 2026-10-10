using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public sealed class LiveFanout(
    LiveFanoutSettings settings,
    ILiveStartup? startup = null,
    LiveEndingRecord? ending = null) : ILiveWireSource
{
    private const string WasLetGoOf = "this session was let go of.";

    private readonly Lock gate = new();

    private readonly ILiveStartup? startup = startup;

    private readonly LiveEndingRecord? ending = ending;

    private readonly List<Viewing> viewers = [];

    private readonly SortedDictionary<LiveChannel, IReadOnlyList<LiveFrame>> kept = [];

    private bool ended;

    private LiveFragmentFault? fault;

    private long droppedByThoseWhoLeft;

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

    public long Dropped
    {
        get
        {
            lock (gate)
            {
                return droppedByThoseWhoLeft + viewers.Sum(viewing => viewing.Backlog.Dropped);
            }
        }
    }

    public int Queued
    {
        get
        {
            lock (gate)
            {
                return viewers.Count is 0 ? 0 : viewers.Max(viewing => viewing.Backlog.Queued);
            }
        }
    }

    public IReadOnlyList<LiveBacklog> Watching
    {
        get
        {
            lock (gate)
            {
                return [.. viewers.Select(viewing => viewing.Backlog)];
            }
        }
    }

    public IReadOnlyList<LiveFrame> Kept
    {
        get
        {
            lock (gate)
            {
                return [.. kept.Values.SelectMany(frames => frames)];
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

            foreach (LiveFrame held in kept.Values.SelectMany(frames => frames))
            {
                viewing.Offer(held);
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

            Keep(frame);
            Offer(frame);
        }
    }

    /// <summary>
    /// Hands the frame to every viewer and keeps <paramref name="standing"/>, all of it on the frame's channel
    /// and in the order given, as what a viewer joining later is handed of that channel.
    /// </summary>
    public void Publish(LiveFrame frame, IReadOnlyList<LiveFrame> standing)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(standing);

        if (!LiveChannels.Kept.Contains(frame.Channel) || standing.Any(held => held.Channel != frame.Channel))
        {
            throw new ArgumentException("What is kept of a channel is kept of a channel that is kept, and of that channel only.", nameof(standing));
        }

        lock (gate)
        {
            if (ended)
            {
                return;
            }

            kept[frame.Channel] = [.. standing];
            Offer(frame);
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

    private static bool CutWhenBehind(LiveFrame frame) => LiveChannels.CutWhenBehind.Contains(frame.Channel);

    private void Offer(LiveFrame frame)
    {
        foreach (Viewing viewing in viewers)
        {
            viewing.Offer(frame);
        }
    }

    private void Keep(LiveFrame frame)
    {
        if (!LiveChannels.Kept.Contains(frame.Channel))
        {
            return;
        }

        if (frame.Payload.IsEmpty)
        {
            kept.Remove(frame.Channel);

            return;
        }

        kept[frame.Channel] = [frame];
    }

    private void Close(LiveFragmentFault? why)
    {
        lock (gate)
        {
            if (ended)
            {
                return;
            }

            if (why is null)
            {
                ending?.Note(LiveSupplyEnding.Of(LiveSupplyEnd.LetGo, WasLetGoOf));
            }

            ended = true;
            fault = why;
            kept.Clear();

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
            if (viewers.Remove(viewing))
            {
                droppedByThoseWhoLeft += viewing.Backlog.Dropped;
            }
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

        public ILiveEnding? Ending => fanout.ending;

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
            bool cut = CutWhenBehind(frame);

            lock (counting)
            {
                if (cut && queued >= longestBacklog)
                {
                    dropped += expendable ? 1 : 0;

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
