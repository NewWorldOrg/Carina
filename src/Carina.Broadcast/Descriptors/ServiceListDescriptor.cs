using System.Diagnostics.CodeAnalysis;

namespace Carina.Broadcast.Descriptors;

public sealed record ServiceListEntry(int ServiceId, byte ServiceType)
{
    public ServiceKind Kind => ServiceKinds.Of(ServiceType);
}

public static class ServiceListDescriptor
{
    public const int EntrySize = 3;

    public static bool TryRead(
        Descriptor descriptor,
        [NotNullWhen(true)] out IReadOnlyList<ServiceListEntry>? services)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        services = null;

        if (descriptor.Tag != DescriptorTags.ServiceList || descriptor.Payload.Length % EntrySize != 0)
        {
            return false;
        }

        ReadOnlySpan<byte> payload = descriptor.Payload.Span;
        var found = new List<ServiceListEntry>(payload.Length / EntrySize);

        for (int at = 0; at < payload.Length; at += EntrySize)
        {
            found.Add(new ServiceListEntry((payload[at] << 8) | payload[at + 1], payload[at + 2]));
        }

        services = found;

        return true;
    }
}
