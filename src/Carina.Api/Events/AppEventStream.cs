using System.Threading.Channels;

using Carina.Api.Responder;
using Carina.Infrastructure.Events;

namespace Carina.Api.Events;

public static class AppEventStream
{
    public const string Path = "/api/events";

    public const string ContentType = "text/event-stream";

    public static readonly TimeSpan WritePatience = TimeSpan.FromSeconds(5);

    public static async Task Invoke(HttpContext context, AppEventHub hub, TimeSpan? writePatience = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hub);

        if (!hub.TryListen(out var listener))
        {
            await RefuseAsync(context, hub);

            return;
        }

        using (listener)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = ContentType;
            context.Response.Headers.CacheControl = "no-cache";

            var patience = writePatience ?? WritePatience;

            try
            {
                await context.Response.StartAsync(context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);

                while (true)
                {
                    var names = await listener.Take(context.RequestAborted);

                    using var leash = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                    leash.CancelAfter(patience);

                    foreach (var name in names)
                    {
                        await context.Response.WriteAsync(Frame(name.Value), leash.Token);
                    }

                    await context.Response.Body.FlushAsync(leash.Token);
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

    private static Task RefuseAsync(HttpContext context, AppEventHub hub)
    {
        var (status, message) = hub.IsClosed
            ? (StatusCodes.Status503ServiceUnavailable, "The app is shutting down and sends no further signals.")
            : (StatusCodes.Status429TooManyRequests,
                $"This app carries {hub.ListenerLimit} signal listeners at a time and they are all taken.");

        context.Response.StatusCode = status;

        return context.Response.WriteAsJsonAsync(BaseResponder<string>.Error(message));
    }
}
