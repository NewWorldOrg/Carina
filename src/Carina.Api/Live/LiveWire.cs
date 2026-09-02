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
        ILiveWireSource source,
        LiveWireSettings settings,
        IHostApplicationLifetime running)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(running);

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

        ILiveViewing? viewing = await source.JoinAsync(context.RequestAborted);

        if (viewing is null)
        {
            await RefuseAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Nothing is being sent live from this app.");

            return;
        }

        await using (viewing)
        {
            using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();

            await new LiveWireSocket(socket, settings, viewing.Startup).CarryAsync(
                viewing.Frames,
                running.ApplicationStopping,
                context.RequestAborted);
        }
    }

    private static Task RefuseAsync(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;

        return context.Response.WriteAsJsonAsync(BaseResponder<string>.Error(message));
    }
}
