using System.Net.WebSockets;

using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Controllers.Live;
using Carina.Api.Services;
using Carina.Domain.Auth;
using Carina.Domain.DataBroadcast;

namespace Carina.Api.Live;

/// <summary>
/// Carries a Hybridcast application's WebSocket to the broadcaster's server and back, on the subprotocol the server
/// chose, until either side closes; a close is passed on with its status and reason. The ticket the application was
/// started with travels in the path, and opens only the application it started, on its own channel, for the viewer
/// who started it. An address outside the application's boundary is refused 403 and asked of nobody.
/// </summary>
public static class HybridcastSocketDelivery
{
    public const string Path = "/api/live/{networkId:int}-{serviceId:int}/data-broadcast/ws/{ticket}";

    public const string OnlyAWebSocket = "This is opened as a WebSocket.";

    public const string WhatIsCarried = "An application names the ws or wss address it connects to as uri.";

    public static string RelayOf(int networkId, int serviceId, string ticket) => $"/api/live/{networkId}-{serviceId}/data-broadcast/ws/{ticket}";

    public static Task Invoke(HttpContext context, int networkId, int serviceId, string? uri, DataBroadcastRelayService relay)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(relay);

        if (LiveDataBroadcastChannel.Read(networkId, serviceId) is not { } channel)
        {
            return HybridcastAppDelivery.RefuseAsync(context, StatusCodes.Status400BadRequest, LiveDataBroadcastChannel.TheChannelsThereAre);
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            return HybridcastAppDelivery.RefuseAsync(context, StatusCodes.Status400BadRequest, OnlyAWebSocket);
        }

        return context.RequestServices
            .GetRequiredService<PlaybackTicketGate>()
            .AdmitForAsLongAsTheGrantLastsAsync(
                context,
                DataBroadcastRelayService.ApplicationTargetOf(channel.Network, channel.Service),
                (viewer, _) => ServeAsync(context, channel, viewer, uri, relay));
    }

    private static async Task ServeAsync(
        HttpContext context,
        LiveDataBroadcastChannel channel,
        Subject viewer,
        string? uri,
        DataBroadcastRelayService relay)
    {
        ServiceResult<HybridcastLaunch, DataBroadcastRelayRefusal> found = relay.LaunchFor(
            channel.Network,
            channel.Service,
            PlaybackTicketCarrier.OfferedBy(context.Request),
            viewer);

        if (found.Data is not { } launch)
        {
            await HybridcastAppDelivery.RefuseAsync(context, StatusCodes.Status403Forbidden, found.ErrorMessage!);

            return;
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? address))
        {
            await HybridcastAppDelivery.RefuseAsync(context, StatusCodes.Status400BadRequest, WhatIsCarried);

            return;
        }

        ServiceResult<RelayedSocket, DataBroadcastRelayRefusal> opened = await relay.OpenApplicationSocketAsync(
            launch,
            address,
            [.. context.WebSockets.WebSocketRequestedProtocols],
            context.RequestAborted);

        if (!opened.IsSuccess)
        {
            await HybridcastAppDelivery.RefuseAsync(context, LiveDataBroadcastChannel.StatusOf(opened.ErrorType), opened.ErrorMessage!);

            return;
        }

        await CarryAsync(context, opened.Data!);
    }

    private static async Task CarryAsync(HttpContext context, RelayedSocket opened)
    {
        if (opened.Socket is not { } socket)
        {
            await HybridcastAppDelivery.RefuseAsync(
                context,
                StatusCodes.Status502BadGateway,
                $"{HybridcastAppDelivery.ServerOutOfReach} ({(int)opened.Result}, {opened.HttpStatus?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "no status"})");

            return;
        }

        await using (socket)
        {
            using WebSocket viewer = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext { SubProtocol = socket.SubProtocol });

            await socket.CarryAsync(viewer, context.RequestAborted);
        }
    }
}
