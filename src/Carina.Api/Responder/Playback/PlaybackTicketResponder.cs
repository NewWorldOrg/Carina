using Carina.Domain.Auth;

namespace Carina.Api.Responder.Playback;

public sealed record PlaybackTicketResponder(string InTheClear, DateTime LapsesAt)
{
    public static PlaybackTicketResponder Of(IssuedPlaybackTicket issued)
    {
        ArgumentNullException.ThrowIfNull(issued);

        return new PlaybackTicketResponder(issued.InTheClear, issued.LapsesAt);
    }
}
