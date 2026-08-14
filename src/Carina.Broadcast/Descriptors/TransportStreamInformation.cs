using System.Diagnostics.CodeAnalysis;

using Carina.Broadcast.Text;

namespace Carina.Broadcast.Descriptors;

public sealed record TransportStreamInformation(int RemoteControlKeyId, string Name)
{
    public static bool TryRead(Descriptor descriptor, [NotNullWhen(true)] out TransportStreamInformation? information)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        information = null;

        if (descriptor.Tag != DescriptorTags.TransportStreamInformation || descriptor.Payload.Length < 2)
        {
            return false;
        }

        var payload = descriptor.Payload.Span;
        var nameLength = payload[1] >> 2;

        if (2 + nameLength > payload.Length)
        {
            return false;
        }

        information = new TransportStreamInformation(payload[0], AribText.Decode(payload.Slice(2, nameLength)));

        return true;
    }
}
