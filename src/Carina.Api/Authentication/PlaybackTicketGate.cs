using Carina.Domain.Auth;

using Microsoft.Net.Http.Headers;

namespace Carina.Api.Authentication;

public sealed class PlaybackTicketGate(IPlaybackTicketStore tickets, IPlaybackGrantStore grants)
{
    public const string TheSameRefusalForEveryBadTicket = "This is watched with a playback ticket.";

    public const string TheRefusalContentType = "text/plain; charset=utf-8";

    public const string NeverCached = "no-store, private";

    public Task AdmitOnceAsync(
        HttpContext context,
        PlaybackTarget target,
        Func<Subject, PlaybackTarget, Task> serve)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);

        return AnsweredAsync(context, target, serve, Spent(context, target));
    }

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

        context.Response.Headers.CacheControl = NeverCached;
        context.Response.Headers.Vary = HeaderNames.Authorization;

        if (watcher is null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = TheRefusalContentType;

            await context.Response.WriteAsync(TheSameRefusalForEveryBadTicket, context.RequestAborted);

            return;
        }

        await serve(watcher, target);
    }

    private Subject? Spent(HttpContext context, PlaybackTarget target)
        => tickets.Spend(PlaybackTicketCarrier.OfferedBy(context.Request), target);

    private Subject? Entering(string? offered, PlaybackTarget target)
    {
        if (offered is null || tickets.Spend(offered, target) is not { } watcher)
        {
            return null;
        }

        grants.Open(offered, watcher, target);

        return watcher;
    }
}
