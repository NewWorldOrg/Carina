using System.Diagnostics.CodeAnalysis;

using Carina.Broadcast.Text;

namespace Carina.Broadcast.Descriptors;

public sealed record ComponentDescription(
    int StreamContent,
    int ComponentType,
    int ComponentTag,
    string Language,
    string Text)
{
    private const int HeaderSize = 6;

    public static bool TryRead(Descriptor descriptor, [NotNullWhen(true)] out ComponentDescription? described)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        described = null;

        if (descriptor.Tag != DescriptorTags.Component || descriptor.Payload.Length < HeaderSize)
        {
            return false;
        }

        var payload = descriptor.Payload.Span;

        described = new ComponentDescription(
            payload[0] & 0x0F,
            payload[1],
            payload[2],
            LanguageCode.Of(payload.Slice(3, LanguageCode.Size)),
            AribText.Decode(payload[HeaderSize..]));

        return true;
    }
}
