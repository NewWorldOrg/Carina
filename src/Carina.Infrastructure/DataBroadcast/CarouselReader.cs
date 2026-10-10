using System.Text;

using Carina.Broadcast.DsmCc;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// Reads one service's data broadcast out of its transport stream, handed over in pieces of any length, as
/// the signals a <see cref="CarouselState"/> takes, each at the moment of the service's clock it was read at.
/// The same reading serves a live channel and a recording.
/// </summary>
public sealed class CarouselReader
{
    public const string UnnamedMediaType = "application/octet-stream";

    private readonly ServiceId service;

    private readonly DataBroadcastTap tap;

    public CarouselReader(ServiceId service)
        : this(service, CarouselLimits.Broadcast)
    {
    }

    public CarouselReader(ServiceId service, CarouselLimits limits)
    {
        ArgumentNullException.ThrowIfNull(service);

        this.service = service;
        tap = new DataBroadcastTap(service.Value, limits);
    }

    public ServiceId Service => service;

    public long UnreadablePackets => tap.UnreadablePackets;

    public long RejectedSections => tap.RejectedSections;

    public long RefusedChanges { get; private set; }

    public long ResourcesLeftOut { get; private set; }

    public long DiscardedEvents { get; private set; }

    public IReadOnlyList<CarouselSignalRead> Read(ReadOnlySpan<byte> bytes)
    {
        List<CarouselSignalRead> signals = [];

        foreach (DataBroadcastRead read in tap.Push(bytes))
        {
            if (Signal(read) is { } signal)
            {
                signals.Add(new CarouselSignalRead(signal, read.At));
            }
        }

        return signals;
    }

    private CarouselSignal? Signal(DataBroadcastRead read)
        => read switch
        {
            DataBroadcastRead.ServiceMapped mapped => Mapped(mapped.Service),
            DataBroadcastRead.CarouselChanged changed => Changed(changed.Change),
            DataBroadcastRead.EventMessageTimed timed => new CarouselSignal.EventTimed(Event(timed.Message, timed.FiresAt)),
            DataBroadcastRead.EventMessageDiscarded => Discarded(),
            _ => throw new ArgumentOutOfRangeException(nameof(read), read, "A data broadcast is read as one of the kinds named."),
        };

    private CarouselSignal Mapped(DataBroadcastService mapped)
        => mapped.Entry is { } entry
            ? new CarouselSignal.Carried(new DataBroadcastEntry(service, entry.ComponentTag, entry.Bxml?.AutoStart ?? false))
            : new CarouselSignal.NotCarried();

    private CarouselSignal? Changed(CarouselChange change)
        => change switch
        {
            CarouselChange.CatalogueUpdated updated => Listed(updated.ComponentTag, updated.Catalogue),
            CarouselChange.ModuleCompleted completed => Completed(completed.ComponentTag, completed.Module),
            CarouselChange.Dropped dropped => Dropped(dropped.ComponentTag, dropped.Defect),
            _ => Rejected(),
        };

    private CarouselSignal? Listed(int tag, DataCarouselCatalogue catalogue)
    {
        if (catalogue.Modules.Select(module => module.ModuleId).Distinct().Count() != catalogue.Modules.Count)
        {
            return Rejected();
        }

        return new CarouselSignal.CatalogUpdated(
            tag,
            catalogue.DownloadId,
            [.. catalogue.Modules.Select(module => new ListedModule(module.ModuleId, module.ModuleVersion, module.ModuleSize))],
            catalogue.Superseded);
    }

    private CarouselSignal? Completed(int tag, CompletedModule module)
    {
        List<CarouselResource> resources = [];

        foreach (ModuleResource resource in module.Resources)
        {
            if (string.IsNullOrEmpty(resource.Location) || Encoding.UTF8.GetByteCount(resource.Location) > CarouselNumbers.MostPathBytes)
            {
                ResourcesLeftOut++;

                continue;
            }

            resources.Add(new CarouselResource(
                resource.Location,
                string.IsNullOrEmpty(resource.MediaType) ? UnnamedMediaType : resource.MediaType,
                FormOf(resource.Content),
                resource.Body));
        }

        return resources.Count is 0 ? Rejected() : new CarouselSignal.ModuleCompleted(tag, module.ModuleId, module.ModuleVersion, resources);
    }

    private CarouselSignal? Dropped(int tag, CarouselDefect defect)
        => defect switch
        {
            CarouselDefect.TooManyCarousels => new CarouselSignal.Dropped(tag, CarouselDropReason.TooManyCarousels),
            CarouselDefect.TooManyModules => new CarouselSignal.Dropped(tag, CarouselDropReason.TooManyModules),
            CarouselDefect.TotalTooLarge => new CarouselSignal.Dropped(tag, CarouselDropReason.TotalTooLarge),
            _ => Rejected(),
        };

    private CarouselSignal? Rejected()
    {
        RefusedChanges++;

        return null;
    }

    private CarouselSignal? Discarded()
    {
        DiscardedEvents++;

        return null;
    }

    private static EventMessage Event(TimedEventMessage message, long firesAt)
        => new(
            message.EventMessageGroupId,
            message.EventMessageId,
            message.EventMessageType,
            message.IsImmediate ? EventTiming.Immediate : EventTiming.Npt,
            firesAt,
            message.PrivateData);

    private static ResourceForm FormOf(ResourceContent content)
        => content switch
        {
            ResourceContent.Text => ResourceForm.Text,
            ResourceContent.UndecodedText => ResourceForm.UndecodedText,
            _ => ResourceForm.Binary,
        };
}

/// <summary>
/// One signal read of a data broadcast, with the moment of the service's clock, followed through its wrap,
/// it was read at.
/// </summary>
public sealed record CarouselSignalRead(CarouselSignal Signal, long At);
