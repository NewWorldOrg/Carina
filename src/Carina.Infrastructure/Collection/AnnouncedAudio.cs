using Carina.Broadcast.Descriptors;
using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Collection;

public static class AnnouncedAudio
{
    private const int Sound = 2;

    public static AudioMode Of(IReadOnlyList<AudioComponentDescription> announced)
    {
        ArgumentNullException.ThrowIfNull(announced);

        return Mode(Main(announced)?.ComponentType);
    }

    private static AudioComponentDescription? Main(IReadOnlyList<AudioComponentDescription> announced)
    {
        AudioComponentDescription? first = null;

        foreach (AudioComponentDescription component in announced)
        {
            if (component.StreamContent != Sound)
            {
                continue;
            }

            if (component.IsMainComponent)
            {
                return component;
            }

            first ??= component;
        }

        return first;
    }

    private static AudioMode Mode(int? componentType)
        => componentType switch
        {
            0x01 => AudioMode.Mono,
            0x02 => AudioMode.DualMono,
            0x03 => AudioMode.Stereo,
            >= 0x04 and <= 0x11 => AudioMode.Surround,
            _ => AudioMode.Undetermined,
        };
}
