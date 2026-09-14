using System.Buffers;

using Carina.Api.Authentication;
using Carina.Api.Services;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Api.Live;

public static class LiveStreamDelivery
{
    public const string Path = "/api/live/{networkId:int}-{serviceId:int}/stream";

    public const string MediaType = "video/mp2t";

    public const string NoSeeking = "none";

    public const string NothingCameInTime = "Nothing came from this channel in time to start sending it.";

    private const int Mouthful = 64 * 1024;

    public static Task Invoke(HttpContext context, int networkId, int serviceId, ILiveSessionManager sessions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sessions);

        context.Response.Headers.CacheControl = PlaybackTicketGate.NeverCached;

        if (networkId is < NetworkId.MinValue or > NetworkId.MaxValue
            || serviceId is < ServiceId.MinValue or > ServiceId.MaxValue)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            return Task.CompletedTask;
        }

        LiveChannelKey channel = new(new NetworkId(networkId), new ServiceId(serviceId));

        if (context.User.Identity?.IsAuthenticated is true)
        {
            return CarryAsync(context, channel, sessions);
        }

        return context.RequestServices
            .GetRequiredService<PlaybackTicketGate>()
            .AdmitOnceUnlessItIsHandedBackAsync(
                context,
                LiveService.TargetOf(channel.Network, channel.Service),
                (_, _) => CarryAsync(context, channel, sessions));
    }

    private static async Task<bool> CarryAsync(HttpContext context, LiveChannelKey channel, ILiveSessionManager sessions)
    {
        LiveHandover handed = await sessions.HandOverAsync(channel, context.RequestAborted);

        if (handed.Handed is not { } carrying)
        {
            LiveRefusal refused = handed.Refusal!.Value;

            await RefuseAsync(context, Of(refused), LiveRefusalClosures.Because(refused));

            return false;
        }

        await using (carrying)
        {
            if (!await carrying.ReachedAsync(context.RequestAborted))
            {
                await RefuseAsync(context, StatusCodes.Status503ServiceUnavailable, NothingCameInTime);

                return false;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = MediaType;
            context.Response.Headers.AcceptRanges = NoSeeking;

            await context.Response.StartAsync(context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await Quietly(carrying.Bytes, context.Response, context.RequestAborted);
        }

        return true;
    }

    private static async Task RefuseAsync(HttpContext context, int status, string because)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = PlaybackTicketGate.TheRefusalContentType;

        try
        {
            await context.Response.WriteAsync(because, context.RequestAborted);
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return;
        }
    }

    private static async Task Quietly(Stream from, HttpResponse into, CancellationToken cancellationToken)
    {
        byte[] mouthful = ArrayPool<byte>.Shared.Rent(Mouthful);

        try
        {
            int read;

            while ((read = await from.ReadAsync(mouthful, cancellationToken)) > 0)
            {
                await into.Body.WriteAsync(mouthful.AsMemory(0, read), cancellationToken);
                await into.Body.FlushAsync(cancellationToken);
            }
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(mouthful);
        }
    }

    private static int Of(LiveRefusal refusal) => refusal switch
    {
        LiveRefusal.NoSuchChannel => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
