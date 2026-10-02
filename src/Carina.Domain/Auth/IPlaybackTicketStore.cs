namespace Carina.Domain.Auth;

public interface IPlaybackTicketStore
{
    IssuedPlaybackTicket? Issue(Subject subject, PlaybackTarget target);

    PlaybackTicket? Take(string? offered, PlaybackTarget target);
}
