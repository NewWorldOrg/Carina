using System.Diagnostics.CodeAnalysis;

using Carina.Broadcast.Text;

namespace Carina.Broadcast.Descriptors;

public static class NetworkNameDescriptor
{
    public static bool TryRead(Descriptor descriptor, [NotNullWhen(true)] out string? name)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.Tag != DescriptorTags.NetworkName)
        {
            name = null;

            return false;
        }

        name = AribText.Decode(descriptor.Payload);

        return true;
    }
}
