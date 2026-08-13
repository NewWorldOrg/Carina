using System.Threading.Channels;

using Carina.Driver.Events;

using Microsoft.AspNetCore.Http;

namespace Carina.Driver.Ipc;

public static class DriverEventStream
{
    public const string ContentType = "text/event-stream";

    public static async Task Invoke(HttpContext context, DriverEventHub hub)
    {
        if (!hub.TryListen(out var listener))
        {
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

            try
            {
                await context.Response.StartAsync(context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);

                while (true)
                {
                    foreach (var name in await listener.Take(context.RequestAborted))
                    {
                        await context.Response.WriteAsync(
                            $"event: {name}\ndata: {name}\n\n",
                            context.RequestAborted
                        );
                    }

                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
            }
            catch (Exception error)
                when (error is OperationCanceledException or ChannelClosedException or IOException)
            { }
        }
    }
}
