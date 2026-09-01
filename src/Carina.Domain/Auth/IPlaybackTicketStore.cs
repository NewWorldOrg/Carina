namespace Carina.Domain.Auth;

public interface IPlaybackTicketStore
{
    IssuedPlaybackTicket Issue(Subject subject, PlaybackTarget target);

    Subject? Spend(string? offered, PlaybackTarget target);
}
