namespace Carina.Domain.Auth;

public interface IPlaybackGrantStore
{
    void Open(string carrier, Subject subject, PlaybackTarget target);

    Subject? Admit(string? offered, PlaybackTarget target);

    int RevokeEverythingOf(Subject subject);
}
