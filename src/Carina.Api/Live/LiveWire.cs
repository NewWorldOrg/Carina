using System.Net.WebSockets;

using Carina.Api.Authentication;
using Carina.Api.Responder;
using Carina.Domain.Streaming;

namespace Carina.Api.Live;

public static class LiveWire
{
    public const string Path = "/api/live/ws";

    public static async Task Invoke(
        HttpContext context,
        ILiveSessionManager sessions,
        ILiveDepartureLedger departures,
        LiveWireSettings settings,
        IHostApplicationLifetime running,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(departures);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(clock);

        if (!context.WebSockets.IsWebSocketRequest)
        {
            await RefuseAsync(
                context,
                StatusCodes.Status400BadRequest,
                "This surface is a WebSocket and is reached by asking to upgrade to one.");

            return;
        }

        if (RequestOrigin.NamesSomewhereElse(context.Request))
        {
            await RefuseAsync(
                context,
                StatusCodes.Status403Forbidden,
                "A wire is opened only from a page this app served.");

            return;
        }

        if (LiveWireRequest.KeyOf(context.Request.Query) is not { } key)
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, LiveWireRequest.TheKeyThereIs);

            return;
        }

        LiveJoin join = await sessions.JoinAsync(key, context.RequestAborted);

        if (join.Viewing is not { } viewing)
        {
            using WebSocket refusing = await context.WebSockets.AcceptWebSocketAsync();

            await new LiveWireSocket(refusing, settings).RefuseAsync(join, context.RequestAborted);

            return;
        }

        await using (viewing)
        {
            using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();

            long opened = clock.GetTimestamp();
            LiveDeparture departure = LiveDeparture.SourceBroke;

            try
            {
                departure = await new LiveWireSocket(
                    socket,
                    settings,
                    viewing.Startup,
                    viewing.Ending,
                    clock).CarryAsync(viewing.Frames, running.ApplicationStopping, context.RequestAborted);
            }
            finally
            {
                departures.Note(key, departure, clock.GetElapsedTime(opened));
            }
        }
    }

    private static Task RefuseAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;

        return context.Response.WriteAsJsonAsync(BaseResponder<string>.Error(message));
    }
}
