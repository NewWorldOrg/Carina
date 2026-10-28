using System.Buffers;

using Carina.Api.Authentication;
using Carina.Api.Services;
using Carina.Domain.Auth;
using Carina.Domain.Channels;
using Carina.Domain.Streaming;

namespace Carina.Api.Live;

public static class LiveStreamDelivery
{
    public const string Path = "/api/live/{networkId:int}-{serviceId:int}/stream";

    public const string WithTheTicketInThePath = "/api/live/{networkId:int}-{serviceId:int}/with-ticket/{ticket}/{*name}";

    public const string MediaType = "video/mp2t";

    public const string NoSeeking = "none";

    public const string NothingCameInTime = "Nothing came from this channel in time to start sending it.";

    public const string PlaylistExtension = ".m3u8";

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
            .AdmitForAsLongAsTheGrantLastsAsync(
                context,
                LiveService.TargetOf(channel.Network, channel.Service),
                (_, _) => CarryAsync(context, channel, sessions));
    }

    /// <summary>
    /// The channel opened by the ticket in the path. A name ending in .m3u8 is answered with a playlist, for a player
    /// that cannot open an endless stream; any other name is handed the stream as it is.
    /// </summary>
    public static async Task InvokeWithTheTicketInThePath(
        HttpContext context,
        int networkId,
        int serviceId,
        string? name,
        ILiveSessionManager sessions,
        ISoundOnAir onAir,
        ILiveEncoderSelector encoders,
        IHlsKeys keys)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(encoders);

        if (!AsksForAPlaylist(name))
        {
            await Invoke(context, networkId, serviceId, sessions);

            return;
        }

        context.Response.Headers.CacheControl = PlaybackTicketGate.NeverCached;

        if (LiveHlsDelivery.Channel(networkId, serviceId) is not { } channel)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            return;
        }

        if (context.User.Identity?.IsAuthenticated is true && SessionClaims.SubjectOf(context.User) is { } signedIn)
        {
            await PlaylistAsync(context, channel, signedIn, sessions, onAir, encoders, keys);

            return;
        }

        await context.RequestServices
            .GetRequiredService<PlaybackTicketGate>()
            .AdmitForAsLongAsTheGrantLastsAsync(
                context,
                LiveService.TargetOf(channel.Network, channel.Service),
                (watcher, _) => PlaylistAsync(context, channel, watcher, sessions, onAir, encoders, keys));
    }

    private static bool AsksForAPlaylist(string? name)
        => name is not null && name.EndsWith(PlaylistExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The playlist of the channel at the profile this machine opens at and with the main sound, which is what an
    /// external player is handed.
    /// </summary>
    private static async Task PlaylistAsync(
        HttpContext context,
        LiveChannelKey channel,
        Subject watcher,
        ILiveSessionManager sessions,
        ISoundOnAir onAir,
        ILiveEncoderSelector encoders,
        IHlsKeys keys)
    {
        LiveEncoderChoice chosen = await encoders.ChooseAsync(context.RequestAborted);

        await LiveHlsDelivery.OpenAsync(
            context,
            new LiveSessionKey(channel.Network, channel.Service, LiveProfile.Unasked(chosen.Encoder)),
            watcher,
            sessions,
            onAir,
            keys);
    }

    private static async Task CarryAsync(HttpContext context, LiveChannelKey channel, ILiveSessionManager sessions)
    {
        LiveHandover handed = await sessions.HandOverAsync(channel, context.RequestAborted);

        if (handed.Handed is not { } carrying)
        {
            LiveRefusal refused = handed.Refusal!.Value;

            await RefuseAsync(context, Of(refused), LiveRefusalClosures.Because(refused));

            return;
        }

        await using (carrying)
        {
            if (!await carrying.ReachedAsync(context.RequestAborted))
            {
                await RefuseAsync(context, StatusCodes.Status503ServiceUnavailable, NothingCameInTime);

                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = MediaType;
            context.Response.Headers.AcceptRanges = NoSeeking;

            await context.Response.StartAsync(context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await QuietlyAsync(carrying.Bytes, context.Response, context.RequestAborted);
        }
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

    private static async Task QuietlyAsync(Stream from, HttpResponse into, CancellationToken cancellationToken)
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
