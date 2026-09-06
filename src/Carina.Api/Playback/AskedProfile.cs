using Carina.Domain.Streaming;

namespace Carina.Api.Playback;

public enum ProfileAnswer
{
    Unasked = 1,

    Named = 2,

    NotOneOfThese = 3,
}

public sealed record AskedProfile
{
    private AskedProfile(ProfileAnswer answer, LiveProfile? named)
    {
        Answer = answer;
        Named = named;
    }

    public ProfileAnswer Answer { get; }

    public LiveProfile? Named { get; }

    public static AskedProfile Read(string? asked)
    {
        if (string.IsNullOrWhiteSpace(asked))
        {
            return new AskedProfile(ProfileAnswer.Unasked, null);
        }

        return LiveProfile.Find(asked) is { } found
            ? new AskedProfile(ProfileAnswer.Named, found)
            : new AskedProfile(ProfileAnswer.NotOneOfThese, null);
    }
}
