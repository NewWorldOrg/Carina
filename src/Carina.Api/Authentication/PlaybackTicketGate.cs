using Carina.Domain.Auth;

using Microsoft.Net.Http.Headers;

namespace Carina.Api.Authentication;

public sealed class PlaybackTicketGate(IPlaybackTicketStore tickets, IPlaybackGrantStore grants)
{
    public const string TheSameRefusalForEveryBadTicket = "This is watched with a playback ticket.";

    public const string TheRefusalContentType = "text/plain; charset=utf-8";

    public const string NeverCached = "no-store, private";

    public Task AdmitForAsLongAsTheGrantLastsAsync(
        HttpContext context,
        PlaybackTarget target,
        Func<Subject, PlaybackTarget, Task> serve)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);

        string? offered = PlaybackTicketCarrier.OfferedBy(context.Request);

        return AnsweredAsync(context, target, serve, grants.Admit(offered, target) ?? Entering(offered, target));
    }

    private static async Task AnsweredAsync(
        HttpContext context,
        PlaybackTarget target,
        Func<Subject, PlaybackTarget, Task> serve,
        Subject? watcher)
    {
        ArgumentNullException.ThrowIfNull(serve);

        NeverKept(context);

        if (watcher is null)
        {
            await RefusedAsync(context);

            return;
        }

        await serve(watcher, target);
    }

    private static void NeverKept(HttpContext context)
    {
        context.Response.Headers.CacheControl = NeverCached;
        context.Response.Headers.Vary = HeaderNames.Authorization;
    }

    private static Task RefusedAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = TheRefusalContentType;

        return context.Response.WriteAsync(TheSameRefusalForEveryBadTicket, context.RequestAborted);
    }

    private Subject? Entering(string? offered, PlaybackTarget target)
    {
        if (offered is null || tickets.Take(offered, target) is not { } taken)
        {
            return null;
        }

        grants.Open(offered, taken.Subject, target);

        return taken.Subject;
    }
}
