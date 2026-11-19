using Carina.Domain.Channels;

namespace Carina.Domain.DataBroadcast;

public sealed record DataBroadcastEntry
{
    public DataBroadcastEntry(ServiceId service, int entryTag, bool autoStart)
    {
        ArgumentNullException.ThrowIfNull(service);

        Service = service;
        EntryTag = CarouselNumbers.Tag(entryTag, nameof(entryTag));
        AutoStart = autoStart;
    }

    public ServiceId Service { get; }

    public int EntryTag { get; }

    public bool AutoStart { get; }
}
