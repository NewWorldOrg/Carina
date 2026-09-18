using Carina.Domain.Playback;

namespace Carina.Api.Playback;

public enum SourceAnswer
{
    Unasked = 1,

    Named = 2,

    NotOneOfThese = 3,
}

public sealed record AskedSource
{
    private AskedSource(SourceAnswer answer, PlaybackSource source)
    {
        Answer = answer;
        Source = source;
    }

    public SourceAnswer Answer { get; }

    public PlaybackSource Source { get; }

    public static AskedSource Read(string? asked)
    {
        if (string.IsNullOrWhiteSpace(asked))
        {
            return new AskedSource(SourceAnswer.Unasked, PlaybackSource.Artefact);
        }

        return PlaybackSources.Find(asked) is { } found
            ? new AskedSource(SourceAnswer.Named, found)
            : new AskedSource(SourceAnswer.NotOneOfThese, PlaybackSource.Artefact);
    }
}
