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

    /// <summary>
    /// Admits one request the way <see cref="AdmitOnceAsync"/> does, and hands the ticket back
    /// unspent when what it was for could not be served at all.
    /// </summary>
    /// <remarks>
    /// A live channel is refused for reasons that have nothing to do with the reader — every tuner
    /// busy is the ordinary answer on a machine recording something — and a reader whose one use
    /// was burnt on that answer has to go and ask for another ticket to try again.
    /// </remarks>
    public async Task AdmitOnceUnlessItIsHandedBackAsync(
        HttpContext context,
        PlaybackTarget target,
        Func<Subject, PlaybackTarget, Task<bool>> serve)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(serve);

        NeverKept(context);

        if (tickets.Take(PlaybackTicketCarrier.OfferedBy(context.Request), target) is not { } taken)
        {
            await RefusedAsync(context);

            return;
        }

        if (!await serve(taken.Subject, target))
        {
            tickets.HandBack(taken, target);
        }
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

    private Subject? Spent(HttpContext context, PlaybackTarget target)
        => tickets.Take(PlaybackTicketCarrier.OfferedBy(context.Request), target)?.Subject;

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
