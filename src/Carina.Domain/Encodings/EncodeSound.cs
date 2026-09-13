using Carina.Domain.Base;
using Carina.Domain.Streaming;

namespace Carina.Domain.Encodings;

public sealed record EncodeSound
{
    public static readonly EncodeSound EveryStreamAsItStands = new(oneChannel: null);

    private EncodeSound(SoundPlacement? oneChannel)
    {
        OneChannel = oneChannel;
    }

    public SoundPlacement? OneChannel { get; }

    public static EncodeSound Of(AudioMode audio, int sounds)
    {
        SoundPlacement main = SoundArrangement
            .Of(new AnnouncedSound(audio, sounds))
            .Placement(SoundTrack.Main);

        return main.IsWholeStream ? EveryStreamAsItStands : new EncodeSound(main);
    }
}
