using Carina.Domain.Channels;

namespace Carina.Domain.DataBroadcast;

/// <summary>
/// What a service's programme map says of its data broadcast: the carousel it is entered from and whether
/// the broadcaster asks for it to open by itself.
/// </summary>
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
