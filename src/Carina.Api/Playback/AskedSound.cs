using Carina.Domain.Streaming;

namespace Carina.Api.Playback;

public enum SoundAnswer
{
    Unasked = 1,

    Named = 2,

    NotOneOfThese = 3,
}

public sealed record AskedSound
{
    private AskedSound(SoundAnswer answer, SoundTrack track)
    {
        Answer = answer;
        Track = track;
    }

    public SoundAnswer Answer { get; }

    public SoundTrack Track { get; }

    public static AskedSound Read(string? asked)
    {
        if (string.IsNullOrWhiteSpace(asked))
        {
            return new AskedSound(SoundAnswer.Unasked, SoundTrack.Main);
        }

        return SoundTracks.Find(asked) is { } found
            ? new AskedSound(SoundAnswer.Named, found)
            : new AskedSound(SoundAnswer.NotOneOfThese, SoundTrack.Main);
    }
}
