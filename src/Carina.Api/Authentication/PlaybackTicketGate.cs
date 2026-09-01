using Carina.Domain.Auth;

namespace Carina.Api.Authentication;

public sealed class PlaybackTicketGate(IPlaybackTicketStore tickets)
{
    public const string TheSameRefusalForEveryBadTicket = "This is watched with a playback ticket.";

    public const string TheRefusalContentType = "text/plain; charset=utf-8";

    public async Task ServeAsync(HttpContext context, PlaybackTarget target, Func<Subject, Task> serve)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(serve);

        if (tickets.Spend(PlaybackTicketCarrier.OfferedBy(context.Request), target) is not { } watcher)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = TheRefusalContentType;

            await context.Response.WriteAsync(TheSameRefusalForEveryBadTicket, context.RequestAborted);

            return;
        }

        await serve(watcher);
    }
}
