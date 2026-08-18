using System.Diagnostics.CodeAnalysis;

using Carina.Broadcast.Text;

namespace Carina.Broadcast.Descriptors;

public sealed record ServiceDescription(byte ServiceType, string ProviderName, string Name)
{
    public ServiceKind Kind => ServiceKinds.Of(ServiceType);

    public static bool TryRead(Descriptor descriptor, [NotNullWhen(true)] out ServiceDescription? description)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        description = null;

        if (descriptor.Tag != DescriptorTags.Service || descriptor.Payload.Length < 3)
        {
            return false;
        }

        ReadOnlySpan<byte> payload = descriptor.Payload.Span;
        byte providerLength = payload[1];

        if (2 + providerLength + 1 > payload.Length)
        {
            return false;
        }

        byte nameLength = payload[2 + providerLength];

        if (3 + providerLength + nameLength > payload.Length)
        {
            return false;
        }

        description = new ServiceDescription(
            payload[0],
            AribText.Decode(payload.Slice(2, providerLength)),
            AribText.Decode(payload.Slice(3 + providerLength, nameLength)));

        return true;
    }
}
