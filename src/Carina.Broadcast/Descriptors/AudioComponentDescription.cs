using System.Diagnostics.CodeAnalysis;

using Carina.Broadcast.Text;

namespace Carina.Broadcast.Descriptors;

public sealed record AudioComponentDescription(
    int StreamContent,
    int ComponentType,
    int ComponentTag,
    int StreamType,
    int SimulcastGroupTag,
    bool IsMainComponent,
    int QualityIndicator,
    int SamplingRate,
    string Language,
    string SecondLanguage,
    string Text)
{
    private const int HeaderSize = 9;

    public static bool TryRead(Descriptor descriptor, [NotNullWhen(true)] out AudioComponentDescription? described)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        described = null;

        if (descriptor.Tag != DescriptorTags.AudioComponent || descriptor.Payload.Length < HeaderSize)
        {
            return false;
        }

        var payload = descriptor.Payload.Span;
        var multilingual = (payload[5] & 0x80) != 0;
        var afterLanguages = HeaderSize + (multilingual ? LanguageCode.Size : 0);

        if (afterLanguages > payload.Length)
        {
            return false;
        }

        described = new AudioComponentDescription(
            payload[0] & 0x0F,
            payload[1],
            payload[2],
            payload[3],
            payload[4],
            (payload[5] & 0x40) != 0,
            (payload[5] >> 4) & 0x03,
            (payload[5] >> 1) & 0x07,
            LanguageCode.Of(payload.Slice(6, LanguageCode.Size)),
            multilingual ? LanguageCode.Of(payload.Slice(HeaderSize, LanguageCode.Size)) : string.Empty,
            AribText.Decode(payload[afterLanguages..]));

        return true;
    }
}
