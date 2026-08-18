using Carina.Broadcast.Descriptors;

namespace Carina.Broadcast.Tables;

public sealed class NetworkTransportStream
{
    internal NetworkTransportStream(
        int transportStreamId,
        int originalNetworkId,
        IReadOnlyList<Descriptor> descriptors)
    {
        TransportStreamId = transportStreamId;
        OriginalNetworkId = originalNetworkId;
        Descriptors = descriptors;

        Services = descriptors.WithTag(DescriptorTags.ServiceList) is { } list
            && ServiceListDescriptor.TryRead(list, out IReadOnlyList<ServiceListEntry>? services)
                ? services
                : [];

        PartiallyReceivedServices = descriptors.WithTag(DescriptorTags.PartialReception) is { } partial
            && PartialReceptionDescriptor.TryRead(partial, out IReadOnlyList<int>? partiallyReceived)
                ? partiallyReceived
                : [];

        if (descriptors.WithTag(DescriptorTags.TransportStreamInformation) is { } information
            && TransportStreamInformation.TryRead(information, out TransportStreamInformation? read))
        {
            RemoteControlKeyId = read.RemoteControlKeyId;
            Name = read.Name;
        }
    }

    public int TransportStreamId { get; }

    public int OriginalNetworkId { get; }

    public int? RemoteControlKeyId { get; }

    public string? Name { get; }

    public IReadOnlyList<ServiceListEntry> Services { get; }

    public IReadOnlyList<int> PartiallyReceivedServices { get; }

    public IReadOnlyList<Descriptor> Descriptors { get; }
}
