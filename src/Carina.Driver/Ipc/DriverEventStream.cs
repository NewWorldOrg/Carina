using System.Threading.Channels;

using Carina.Driver.Events;

using Microsoft.AspNetCore.Http;

namespace Carina.Driver.Ipc;

public static class DriverEventStream
{
    public const string ContentType = "text/event-stream";

    public static readonly TimeSpan WritePatience = TimeSpan.FromSeconds(5);

    public static async Task Invoke(
        HttpContext context,
        DriverEventHub hub,
        TimeSpan? writePatience = null
    )
    {
        if (!hub.TryListen(out var listener))
        {
            if (hub.IsClosed)
            {
                await DriverApi.Problem(
                    context,
                    StatusCodes.Status503ServiceUnavailable,
                    "draining",
                    "The driver is shutting down and sends no further events."
                );

                return;
            }

            await DriverApi.Problem(
                context,
                StatusCodes.Status429TooManyRequests,
                "tooManyListeners",
                $"This driver carries {hub.ListenerLimit} event listeners at a time and they are all taken."
            );

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

                    using var leash = CancellationTokenSource.CreateLinkedTokenSource(
                        context.RequestAborted
                    );
                    leash.CancelAfter(patience);

                    foreach (var name in names)
                    {
                        await context.Response.WriteAsync(
                            $"event: {name}\ndata: {name}\n\n",
                            leash.Token
                        );
                    }

                    await context.Response.Body.FlushAsync(leash.Token);
                }
            }
            catch (Exception error)
                when (error is OperationCanceledException or ChannelClosedException or IOException)
            { }
        }
    }
}
