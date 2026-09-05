using Carina.Domain.Auth;

namespace Carina.Conventions.Tests.Fixtures.Playback;

internal sealed class BoundTicketStore : IPlaybackTicketStore
{
    public IssuedPlaybackTicket? Issue(Subject subject, PlaybackTarget target) => null;

    public Subject? Spend(string? offered, PlaybackTarget target) => null;
}

internal sealed class UnboundTicketStore : IPlaybackTicketStore
{
    public IssuedPlaybackTicket? Issue(Subject subject, PlaybackTarget target) => null;

    public IssuedPlaybackTicket? Issue(Subject subject) => null;

    public Subject? Spend(string? offered, PlaybackTarget target) => null;

    public Subject? Spend(string? offered) => null;
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
