using Carina.Domain.Auth;

namespace Carina.Conventions.Tests.Fixtures.Playback;

internal sealed class BoundTicketStore : IPlaybackTicketStore
{
    public IssuedPlaybackTicket? Issue(Subject subject, PlaybackTarget target) => null;

    public PlaybackTicket? Take(string? offered, PlaybackTarget target) => null;

    public void HandBack(PlaybackTicket spent, PlaybackTarget target)
    {
    }
}

internal sealed class UnboundTicketStore : IPlaybackTicketStore
{
    public IssuedPlaybackTicket? Issue(Subject subject, PlaybackTarget target) => null;

    public IssuedPlaybackTicket? Issue(Subject subject) => null;

    public PlaybackTicket? Take(string? offered, PlaybackTarget target) => null;

    public PlaybackTicket? Take(string? offered) => null;

    public void HandBack(PlaybackTicket spent, PlaybackTarget target)
    {
    }
}

internal sealed class PassThatOpensAnything : IPlaybackGrantStore
{
    public void Open(string carrier, Subject subject, PlaybackTarget target)
    {
    }

    public void Open(string carrier, Subject subject)
    {
    }

    public Subject? Admit(string? offered, PlaybackTarget target) => null;

    public int RevokeEverythingOf(Subject subject) => 0;
}
