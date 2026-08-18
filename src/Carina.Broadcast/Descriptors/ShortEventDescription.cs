using System.Diagnostics.CodeAnalysis;

using Carina.Broadcast.Text;

namespace Carina.Broadcast.Descriptors;

public sealed record ShortEventDescription(string Language, string Name, string Summary)
{
    private const int LanguageSize = 3;

    public static bool TryRead(Descriptor descriptor, [NotNullWhen(true)] out ShortEventDescription? described)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        described = null;

        if (descriptor.Tag != DescriptorTags.ShortEvent || descriptor.Payload.Length < LanguageSize + 2)
        {
            return false;
        }

        var payload = descriptor.Payload.Span;
        var nameLength = payload[LanguageSize];
        var afterName = LanguageSize + 1 + nameLength;

        if (afterName + 1 > payload.Length)
        {
            return false;
        }

        var summaryLength = payload[afterName];

        if (afterName + 1 + summaryLength > payload.Length)
        {
            return false;
        }

        described = new ShortEventDescription(
            LanguageCode.Of(payload[..LanguageSize]),
            AribText.Decode(payload.Slice(LanguageSize + 1, nameLength)),
            AribText.Decode(payload.Slice(afterName + 1, summaryLength)));

        return true;
    }
}
