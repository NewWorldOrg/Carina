using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.DataBroadcast;

public sealed class DataBroadcastSession
{
    private const long Lap = (long)LivePts.ComesAroundAt;

    private readonly Lock gate = new();

    private readonly CarouselReader reader;

    private readonly CarouselState state = new();

    private readonly ILogger logger;

    private readonly Action<DataBroadcastSession> stopped;

    private readonly List<Shown> showing = [];

    private readonly Dictionary<(int Tag, int ModuleId, int Version), LiveFrame> modules = [];

    private readonly Dictionary<int, LiveFrame> references = [];

    private LiveFrame? catalog;

    private LiveFrame? absent;

    private IReadOnlyList<LiveFrame> standing = [];

    private long latest;

    private bool broken;

    public DataBroadcastSession(ServiceId service, ILogger logger)
        : this(service, logger, static _ => { })
    {
    }

    public DataBroadcastSession(ServiceId service, ILogger logger, Action<DataBroadcastSession> stopped)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(stopped);

        reader = new CarouselReader(service);
        this.logger = logger;
        this.stopped = stopped;
        Seat = new DataBroadcastSeat(this);
    }

    public Stream Seat { get; }

    public IReadOnlyList<LiveFrame> Standing
    {
        get
        {
            lock (gate)
            {
                return standing;
            }
        }
    }

    public bool HasStopped
    {
        get
        {
            lock (gate)
            {
                return broken;
            }
        }
    }

    public void Show(LiveFanout fanout)
    {
        ArgumentNullException.ThrowIfNull(fanout);

        lock (gate)
        {
            Shown shown = new(fanout, Laps(reader.Now ?? 0) * Lap);

            if (broken)
            {
                fanout.Publish(shown.Moved(DataBroadcastFrames.Absent(latest)), standing);

                return;
            }

            showing.Add(shown);

            if (standing.Count > 0)
            {
                IReadOnlyList<LiveFrame> held = shown.Held(standing);

                fanout.Publish(held, held);
            }
        }
    }

    public void StopShowing(LiveFanout fanout)
    {
        ArgumentNullException.ThrowIfNull(fanout);

        lock (gate)
        {
            showing.RemoveAll(shown => ReferenceEquals(shown.Fanout, fanout));
        }
    }

    public void Read(ReadOnlySpan<byte> bytes)
    {
        Exception? failed = null;

        lock (gate)
        {
            if (broken)
            {
                return;
            }

            try
            {
                Apply(reader.Read(bytes));
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                failed = Halt() ? failure : null;
            }
        }

        if (failed is not null)
        {
            logger.LogWarning(failed, "The data broadcast of service {Service} stopped being read: reading it failed.", reader.Service.Value);
            stopped(this);
        }
    }

    public void Stop()
    {
        bool halted;

        lock (gate)
        {
            halted = Halt();
        }

        if (halted)
        {
            stopped(this);
        }
    }

    internal void Ended()
        => logger.LogInformation(
            "The data broadcast of service {Service} was read to its end, leaving out {Packets} unreadable packets, {Sections} sections, {Changes} carousel changes, {Resources} resources and {Events} event messages.",
            reader.Service.Value,
            reader.UnreadablePackets,
            reader.RejectedSections,
            reader.RefusedChanges,
            reader.ResourcesLeftOut,
            reader.DiscardedEvents);

    private void Apply(IReadOnlyList<CarouselSignalRead> signals)
    {
        foreach (CarouselSignalRead read in signals)
        {
            latest = read.At;

            foreach (CarouselDelta delta in state.Apply(read.Signal, read.At))
            {
                if (FrameOf(delta, read.At) is { } frame)
                {
                    Publish(frame);
                }
            }
        }
    }

    private LiveFrame? FrameOf(CarouselDelta delta, long at)
    {
        switch (delta)
        {
            case CarouselDelta.CatalogChanged changed:
                catalog = DataBroadcastFrames.Catalog(changed.Catalog, at);
                absent = null;
                Stand();

                return catalog;
            case CarouselDelta.ModuleArrived arrived:
                LiveFrame module = DataBroadcastFrames.Module(arrived.Module, at);
                modules[Key(arrived.Module)] = module;
                catalog = state.Catalog is { } held ? DataBroadcastFrames.Catalog(held, at) : catalog;
                Stand();

                return module;
            case CarouselDelta.EventCame came:
                return DataBroadcastFrames.Event(came.Message, at);
            case CarouselDelta.NptReferred referred:
                LiveFrame reference = DataBroadcastFrames.NptReference(referred.Reference, at);
                references[referred.Reference.Tag] = reference;
                Stand();

                return reference;
            case CarouselDelta.Absent:
                catalog = null;
                absent = DataBroadcastFrames.Absent(at);
                Stand();

                return absent;
            case CarouselDelta.CarouselDropped dropped:
                logger.LogWarning(
                    "A carousel of the data broadcast of service {Service} was left out: tag {Tag}, {Reason}.",
                    reader.Service.Value,
                    dropped.Tag,
                    dropped.Reason);

                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(delta), delta, "A change of the carousels is one of the kinds named.");
        }
    }

    private void Stand()
    {
        IReadOnlyList<ModuleVersion> valid = state.Modules;
        HashSet<(int Tag, int ModuleId, int Version)> held = [.. valid.Select(Key)];

        foreach ((int Tag, int ModuleId, int Version) gone in modules.Keys.Where(key => !held.Contains(key)).ToArray())
        {
            modules.Remove(gone);
        }

        HashSet<int> referred = [.. state.NptReferences.Select(reference => reference.Tag)];

        foreach (int gone in references.Keys.Where(tag => !referred.Contains(tag)).ToArray())
        {
            references.Remove(gone);
        }

        standing = (catalog, absent) switch
        {
            ({ } latest, _) =>
            [
                latest,
                .. valid.Select(Key).Where(modules.ContainsKey).Select(key => modules[key]),
                .. references.OrderBy(reference => reference.Key).Select(reference => reference.Value),
            ],
            (null, { } nothing) => [nothing],
            _ => [],
        };
    }

    private bool Halt()
    {
        if (broken)
        {
            return false;
        }

        broken = true;
        catalog = null;
        absent = null;
        modules.Clear();
        references.Clear();
        standing = [];

        LiveFrame none = DataBroadcastFrames.Absent(latest);

        foreach (Shown shown in showing)
        {
            shown.Fanout.Publish(shown.Moved(none), standing);
        }

        return true;
    }

    private void Publish(LiveFrame frame)
    {
        foreach (Shown shown in showing)
        {
            shown.Fanout.Publish(shown.Moved(frame), shown.Held(standing));
        }
    }

    private static long Laps(long at) => at < 0 ? 0 : at / Lap;

    private static (int Tag, int ModuleId, int Version) Key(ModuleVersion module) => (module.Tag, module.ModuleId, module.Version);

    private sealed record Shown(LiveFanout Fanout, long Behind)
    {
        public LiveFrame Moved(LiveFrame frame) => DataBroadcastFrames.Shifted(frame, Behind);

        public IReadOnlyList<LiveFrame> Held(IReadOnlyList<LiveFrame> frames)
            => Behind is 0 ? frames : [.. frames.Select(Moved)];
    }
}
