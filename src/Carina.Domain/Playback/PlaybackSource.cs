namespace Carina.Domain.Playback;

public enum PlaybackSource
{
    Artefact = 1,

    Recording = 2,
}

public static class PlaybackSources
{
    public const string ArtefactIsCalled = "artefact";

    public const string RecordingIsCalled = "recording";

    public static readonly IReadOnlyList<PlaybackSource> InOrder =
        [PlaybackSource.Artefact, PlaybackSource.Recording];

    public static readonly IReadOnlyList<string> Names = [ArtefactIsCalled, RecordingIsCalled];

    public static string NameOf(PlaybackSource source) => source switch
    {
        PlaybackSource.Artefact => ArtefactIsCalled,
        PlaybackSource.Recording => RecordingIsCalled,
        _ => throw Unknown(source),
    };

    public static PlaybackSource? Find(string? name)
        => InOrder.Cast<PlaybackSource?>().FirstOrDefault(source =>
            string.Equals(NameOf(source!.Value), name, StringComparison.Ordinal));

    private static ArgumentOutOfRangeException Unknown(PlaybackSource source)
        => new(nameof(source), source, "A recording is played from one of the two things named here.");
}
