using System.Diagnostics.CodeAnalysis;

namespace Carina.Broadcast.Descriptors;

public static class PartialReceptionDescriptor
{
    public const int EntrySize = 2;

    public static bool TryRead(Descriptor descriptor, [NotNullWhen(true)] out IReadOnlyList<int>? services)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        services = null;

        if (descriptor.Tag != DescriptorTags.PartialReception || descriptor.Payload.Length % EntrySize != 0)
        {
            return false;
        }

        ReadOnlySpan<byte> payload = descriptor.Payload.Span;
        var found = new List<int>(payload.Length / EntrySize);

        for (int at = 0; at < payload.Length; at += EntrySize)
        {
            found.Add((payload[at] << 8) | payload[at + 1]);
        }

        services = found;

        return true;
    }
}
