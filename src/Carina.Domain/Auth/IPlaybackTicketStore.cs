namespace Carina.Domain.Auth;

public interface IPlaybackTicketStore
{
    IssuedPlaybackTicket? Issue(Subject subject, PlaybackTarget target);

    /// <summary>
    /// Spends a ticket, keeping what was spent so that a caller which then finds it cannot serve
    /// what the ticket was for can hand it back.
    /// </summary>
    PlaybackTicket? Take(string? offered, PlaybackTarget target);

    /// <summary>
    /// Puts back, unchanged, a ticket that was taken for something that was never served, so that a
    /// refusal the reader never asked for does not burn the one use it had.
    /// </summary>
    void HandBack(PlaybackTicket spent, PlaybackTarget target);
}
