namespace Carina.Domain.Auth;

public interface IPlaybackTicketStore
{
    IssuedPlaybackTicket? Issue(Subject subject, PlaybackTarget target);

    /// <summary>
    /// Spends a ticket, keeping what was spent so that a caller that cannot serve what the ticket was
    /// for can hand it back.
    /// </summary>
    PlaybackTicket? Take(string? offered, PlaybackTarget target);

    /// <summary>
    /// Puts back, unchanged, a ticket that was taken for something that was never served.
    /// </summary>
    void HandBack(PlaybackTicket spent, PlaybackTarget target);
}
