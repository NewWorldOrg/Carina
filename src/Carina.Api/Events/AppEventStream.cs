using System.Threading.Channels;

using Carina.Api.Responder;
using Carina.Contracts;
using Carina.Infrastructure.Events;

namespace Carina.Api.Events;

public static class AppEventStream
{
    public const string Path = "/api/events";

    public const string ContentType = "text/event-stream";

    public const string Keepalive = ": keepalive\n\n";

    public static readonly TimeSpan WritePatience = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan BetweenKeepalives = TimeSpan.FromSeconds(15);

    public static async Task Invoke(
        HttpContext context,
        AppEventHub hub,
        TimeSpan? writePatience = null,
        TimeSpan? betweenKeepalives = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hub);

        if (!hub.TryListen(out AppEventListener? listener))
        {
            await RefuseAsync(context, hub);

            return;
        }

        using (listener)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = ContentType;
            context.Response.Headers.CacheControl = "no-cache";

            TimeSpan patience = writePatience ?? WritePatience;
            TimeSpan quiet = betweenKeepalives ?? BetweenKeepalives;

            try
            {
                await context.Response.StartAsync(context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);

                Task<IReadOnlyList<AppEventName>> waiting = listener.Take(context.RequestAborted);

                while (true)
                {
                    if (!await SignalledWithin(waiting, quiet, context.RequestAborted))
                    {
                        await WriteAsync(context, Keepalive, patience);

                        continue;
                    }

                    IReadOnlyList<AppEventName> names = await waiting;
                    waiting = listener.Take(context.RequestAborted);

                    await WriteAsync(context, Frames(names), patience);
                }
            }
            catch (Exception error)
                when (error is OperationCanceledException or ChannelClosedException or IOException)
            {
            }
        }
    }

    public static string Frame(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return $"event: {name}\ndata\n\n";
    }

    private static string Frames(IReadOnlyList<AppEventName> names)
        => string.Concat(names.Select(name => Frame(name.Value)));

    private static async Task<bool> SignalledWithin(
        Task<IReadOnlyList<AppEventName>> waiting,
        TimeSpan quiet,
        CancellationToken cancellationToken)
    {
        using var tick = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task quiets = Task.Delay(quiet, tick.Token);
        bool signalled = await Task.WhenAny(waiting, quiets) != quiets;

        await tick.CancelAsync();

        return signalled;
    }

    private static async Task WriteAsync(HttpContext context, string payload, TimeSpan patience)
    {
        using var leash = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        leash.CancelAfter(patience);

        await context.Response.WriteAsync(payload, leash.Token);
        await context.Response.Body.FlushAsync(leash.Token);
    }

    private static Task RefuseAsync(HttpContext context, AppEventHub hub)
    {
        (int status, string? message) = hub.IsClosed
            ? (StatusCodes.Status503ServiceUnavailable, "The app is shutting down and sends no further signals.")
            : (StatusCodes.Status429TooManyRequests,
                $"This app carries {hub.ListenerLimit} signal listeners at a time and they are all taken.");

        context.Response.StatusCode = status;

        return context.Response.WriteAsJsonAsync(BaseResponder<string>.Error(message));
    }
}
