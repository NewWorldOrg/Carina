using Carina.Api.Authentication;
using Carina.Api.Common;
using Carina.Api.Controllers.Live;
using Carina.Api.Services;
using Carina.Domain.Auth;
using Carina.Domain.DataBroadcast;
using Carina.Infrastructure.DataBroadcast;

using Microsoft.Net.Http.Headers;

namespace Carina.Api.Live;

/// <summary>
/// Hands a Hybridcast application's frame the documents and resources it asks for, and the answers to what it posts,
/// through the relay. The frame has no cookie, so the ticket the application was started with travels in the path,
/// and a ticket opens only the application it started, on its own channel, for the viewer who started it. A post is
/// read no further than one byte past the most the relay sends, and carries its media type and nothing else of the
/// request. An address outside the application's boundary is refused 403 and asked of nobody. A document has every
/// address in its markup and style sheets pointed back at this path and the receiver script put first into its head;
/// a style sheet has its addresses pointed back too, and a script, in the document or on its own, reads the
/// receiver script's stand-in for its location. Everything handed over is sandboxed by its own policy, so a page opened here outside the frame still runs
/// with no origin of its own, and may load from and connect to nothing but this application's relay.
/// </summary>
public static class HybridcastAppDelivery
{
    public const string Path = "/api/live/{networkId:int}-{serviceId:int}/data-broadcast/app/{ticket:length(43)}";

    public const string WhatIsRelayed = "An application names the address of the document or resource it asks for as uri.";

    public const string ServerOutOfReach = "The broadcaster's server could not be reached.";

    private const string Bytes = "application/octet-stream";

    public static string RelayOf(int networkId, int serviceId, string ticket) => $"/api/live/{networkId}-{serviceId}/data-broadcast/app/{ticket}";

    public static Task Invoke(HttpContext context, int networkId, int serviceId, string? uri, DataBroadcastRelayService relay)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(relay);

        if (LiveDataBroadcastChannel.Read(networkId, serviceId) is not { } channel)
        {
            return RefuseAsync(context, StatusCodes.Status400BadRequest, LiveDataBroadcastChannel.TheChannelsThereAre);
        }

        return context.RequestServices
            .GetRequiredService<PlaybackTicketGate>()
            .AdmitForAsLongAsTheGrantLastsAsync(
                context,
                DataBroadcastRelayService.ApplicationTargetOf(channel.Network, channel.Service),
                (viewer, _) => ServeAsync(context, channel, viewer, uri, relay));
    }

    public static Task RefuseAsync(HttpContext context, int status, string said)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.StatusCode = status;
        context.Response.ContentType = PlaybackTicketGate.TheRefusalContentType;

        return context.Response.WriteAsync(said, context.RequestAborted);
    }

    private static async Task ServeAsync(
        HttpContext context,
        LiveDataBroadcastChannel channel,
        Subject viewer,
        string? uri,
        DataBroadcastRelayService relay)
    {
        string ticket = PlaybackTicketCarrier.OfferedBy(context.Request)!;

        ServiceResult<HybridcastLaunch, DataBroadcastRelayRefusal> found = relay.LaunchFor(channel.Network, channel.Service, ticket, viewer);

        if (found.Data is not { } launch)
        {
            await RefuseAsync(context, StatusCodes.Status403Forbidden, found.ErrorMessage!);

            return;
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? address))
        {
            await RefuseAsync(context, StatusCodes.Status400BadRequest, WhatIsRelayed);

            return;
        }

        ServiceResult<RelayedBody, DataBroadcastRelayRefusal> relayed = HttpMethods.IsPost(context.Request.Method)
            ? await relay.PostApplicationAsync(launch, address, await PostedAsync(context), context.RequestAborted)
            : await relay.FetchApplicationAsync(launch, address, context.RequestAborted);

        if (!relayed.IsSuccess)
        {
            await RefuseAsync(context, LiveDataBroadcastChannel.StatusOf(relayed.ErrorType), relayed.ErrorMessage!);

            return;
        }

        await AnswerAsync(context, new HybridcastFrame(channel, ticket, launch), relayed.Data!);
    }

    private static async Task<PostedBody> PostedAsync(HttpContext context)
    {
        byte[] kept = new byte[context.RequestServices.GetRequiredService<HybridcastRelaySettings>().LargestSent + 1];
        int read = await context.Request.Body.ReadAtLeastAsync(kept, kept.Length, throwOnEndOfStream: false, context.RequestAborted);

        return new PostedBody(kept.AsMemory(0, read), context.Request.ContentType);
    }

    private static async Task AnswerAsync(HttpContext context, HybridcastFrame frame, RelayedBody fetched)
    {
        if (fetched is not { HttpStatus: { } status, At: { } at })
        {
            await RefuseAsync(context, StatusCodes.Status502BadGateway, $"{ServerOutOfReach} ({(int)fetched.Result})");

            return;
        }

        string origin = context.RequestServices.GetRequiredService<PublicOrigin>().OriginFor(OidcHandshake.ArrivedAt(context.Request));

        context.Response.StatusCode = status;
        context.Response.ContentType = fetched.ContentType ?? Bytes;
        context.Response.Headers[HeaderNames.ContentSecurityPolicy] = frame.Policy(origin);
        context.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";

        await context.Response.Body.WriteAsync(frame.Handed(fetched.ContentType, fetched.Body, at), context.RequestAborted);
    }
}

/// <summary>
/// The relay a started application's frame is on: where its documents and resources and its WebSockets are asked
/// for, what its documents and style sheets are turned into, and the policy everything handed to it is given.
/// </summary>
public sealed record HybridcastFrame(LiveDataBroadcastChannel Channel, string Ticket, HybridcastLaunch Launch)
{
    public string Relay => HybridcastAppDelivery.RelayOf(Channel.Network.Value, Channel.Service.Value, Ticket);

    public string Socket => HybridcastSocketDelivery.RelayOf(Channel.Network.Value, Channel.Service.Value, Ticket);

    public ReadOnlyMemory<byte> Handed(string? contentType, ReadOnlyMemory<byte> body, Uri at)
    {
        ArgumentNullException.ThrowIfNull(at);

        if (HybridcastPages.IsDocument(contentType))
        {
            return HybridcastPages.Document(
                body.Span,
                at,
                Pointed,
                HybridcastPages.Injection(Launch.ReceiverScript, Relay, Socket, at),
                HybridcastPages.DeclaredShiftJis(contentType));
        }

        if (HybridcastPages.IsScript(contentType))
        {
            return HybridcastPages.Script(body, HybridcastPages.DeclaredShiftJis(contentType));
        }

        return HybridcastPages.IsStyle(contentType) ? HybridcastPages.Style(body.Span, at, Pointed) : body;
    }

    public string Policy(string origin)
    {
        ArgumentException.ThrowIfNullOrEmpty(origin);

        string relay = origin + Relay;
        string socketOrigin = origin.StartsWith("https:", StringComparison.Ordinal) ? "wss:" + origin[6..] : "ws:" + origin[5..];

        return "sandbox allow-scripts allow-forms; default-src 'none'; "
               + $"script-src 'unsafe-inline' 'unsafe-eval' blob: {relay} {origin}{Launch.ReceiverScript}; "
               + $"style-src 'unsafe-inline' {relay}; img-src blob: data: {relay}; media-src blob: {relay}; font-src {relay}; "
               + $"connect-src {relay} {socketOrigin}{Socket}; frame-src {relay}; form-action {relay}; base-uri 'none'";
    }

    private string Pointed(Uri target) => HybridcastPages.Address(Relay, target);
}
